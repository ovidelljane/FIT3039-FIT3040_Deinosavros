using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

// All changed values belong to disposable Play Mode objects, never the authored assets.
public static class RunBalanceVerification
{
    private static readonly BindingFlags Fields = BindingFlags.NonPublic | BindingFlags.Instance;
    private static void Set(object target, string name, object value) => target.GetType().GetField(name, Fields).SetValue(target, value);
    private static T Get<T>(object target, string name) => (T)target.GetType().GetField(name, Fields).GetValue(target);
    private static void Call(object target, string name, params object[] args) => target.GetType().GetMethod(name, Fields).Invoke(target, args);
    private static IEnumerator Wait(Func<bool> ready)
    {
        double deadline = EditorApplication.timeSinceStartup + 45;
        while (!ready())
        {
            if (EditorApplication.timeSinceStartup > deadline) throw new InvalidOperationException("Balance verification timed out.");
            yield return null;
        }
        yield return null;
    }

    public static IEnumerator Run(Action<bool, string> check)
    {
        yield return Wait(() => Object.FindFirstObjectByType<MainMenuController>() != null);
        var source = RunBalance.Default;
        int originalMaxHealth = source.startingMaxHealth, originalCapacity = source.deckCapacity;
        var rules = Object.Instantiate(source);
        rules.startingMaxHealth = 147; rules.deckCapacity = 37;
        rules.recoveryPercent = 23; rules.minimumOfferingAttackInterval = .7f;
        Object.FindFirstObjectByType<RunSettings>().configuration = rules;
        Call(Object.FindFirstObjectByType<MainMenuController>(), "StartRun");
        yield return Wait(() => BattleHud.Find(SceneManager.GetActiveScene())?.Deck?.CanPlay == true);
        var session = RunSession.Instance;
        var hud = BattleHud.Find(SceneManager.GetActiveScene());
        var deck = hud.Deck; var player = deck.Player;
        var clock = TimeTickSystem.Active;
        check(player.maxHealth == 147 && player.health == 147 && session.PlayerMaxHealth == 147,
            "Configured maximum HP starts full instead of retaining the old 100 HP default.");
        check(session.Rules == rules && session.DeckCapacityLimit == 37 && session.RecoveryPercent == 23,
            "Hierarchy rules travel with the session rather than reverting to another scene's defaults.");
        var enemies = BattleScript.FindFighters("Enemy");
        Time.timeScale = 0; clock.enabled = false;
        try
        {
            VerifyDifficulty(session, player, check);
            foreach (var enemy in enemies) { enemy.health = enemy.maxHealth = 10000; enemy.attackDmg = 0; }
            foreach (float step in new[] { .05f, .1f, .2f })
            {
                clock.StopTimer(); Set(clock, "tickRateMax", step); clock.StartTimer();
                player.attackSpd = 1; player.attackDmg = 2; player.hitsPerAttack = 1;
                player.elixir = 0; player.maxElixir = 100; player.elixirRegen = .5f;
                Set(player, "attackElapsed", 0d);
                int before = enemies.Sum(e => e.health);
                var effect = new GameObject("Timed Test").AddComponent<Effect>();
                effect.SetValues(StatType.ElixirRegen, player, 1.5f, 6);
                Call(clock, "Advance", 10f);
                check(before - enemies.Sum(e => e.health) == 20, "Ten attacks in ten seconds at step " + step);
                check(Mathf.Abs(player.elixir - 14) < .003f && Mathf.Approximately(player.elixirRegen, .5f),
                    "Six-second regeneration buff and ten-second base regeneration agree at step " + step);
                int handCount = deck.Hand.Count;
                var card = deck.Hand[0];
                deck.OnCardPlayed(card, Get<CardDefinition>(card, "definition"));
                Call(clock, "Advance", 1.8f);
                check(deck.Hand.Count == handCount - 1, "Draw still waits before two seconds at step " + step);
                Call(clock, "Advance", .2f);
                check(deck.Hand.Count == handCount, "Draw occurs after two seconds at step " + step);
                yield return null;
            }
            Set(clock, "tickRateMax", .1f); clock.StopTimer(); clock.StartTimer();
            int ticks = 0; Action count = () => ticks++;
            TimeTickSystem.OnTick += count;
            try { Call(clock, "Advance", .7f); }
            finally { TimeTickSystem.OnTick -= count; }
            check(ticks == 7, "A long frame catches up all due simulation steps.");
            player.attackSpd = .05f; player.attackDmg = 2; Set(player, "attackElapsed", 0d);
            int fastBefore = enemies.Sum(e => e.health);
            Call(clock, "Advance", .2f);
            check(fastBefore - enemies.Sum(e => e.health) == 8, "Intervals below one tick are not silently clamped to 0.1 seconds.");
            player.attackSpd = 1;
            clock.StopTimer(); clock.StartTimer();
            int normalDamage = player.attackDmg;
            var cancel = new GameObject("Canceled Effect").AddComponent<Effect>();
            cancel.SetValues(StatType.Damage, player, 4, 6);
            cancel.enabled = false; Object.Destroy(cancel.gameObject);
            check(player.attackDmg == normalDamage, "Canceled effects remove their contribution once.");

            var definition = AssetDatabase.LoadAssetAtPath<CardDefinition>("Assets/Data/Cards/UncertainFates.asset");
            var costCard = Object.Instantiate(definition.combatPrefab, deck.Hand[0].transform.parent).GetComponent<BuffCards>();
            costCard.Initialize(deck, definition); Get<System.Collections.Generic.List<BuffCards>>(deck, "hand").Add(costCard);
            yield return null;
            player.health = costCard.Cost - 1; player.elixir = 100;
            check(!costCard.MeetsResourceRequirement, "Elixir cannot pay for an insufficient-health card.");
            int beforeCount = deck.Hand.Count;
            costCard.OnPointerClick(new PointerEventData(EventSystem.current));
            check(deck.Hand.Count == beforeCount && player.health == costCard.Cost - 1, "Insufficient HP neither spends nor discards.");
            hud.Status.ShowCost(costCard); yield return null;
            check(hud.Status.Health.Hint.text.Contains("Health") && !hud.Status.Health.Hint.text.Contains("Elixir"),
                "Insufficient-health preview names the correct resource.");
            player.health = costCard.Cost;
            check(costCard.MeetsResourceRequirement, "Exact HP can pay the cost, retaining the lethal-cost rule.");
            player.health = 80; player.maxHealth = 147; player.elixir = 0; player.maxElixir = 10;
            int price = costCard.Cost; float gain = costCard.Values.Amount;
            costCard.OnPointerClick(new PointerEventData(EventSystem.current));
            check(player.health == 80 - price && player.elixir == Mathf.Min(10, gain), "Health pays successfully even with zero Elixir.");
            yield return null;

            foreach (var card in AssetDatabase.LoadAssetAtPath<CardPool>("Assets/Data/CardPool.asset").Cards)
            {
                check(!card.GetCombatDescription().Contains("{") && !card.GetSacrificeDescription(session).Contains("{"),
                    "All description tokens resolve: " + card.cardId);
                var values = card.BaseValues;
                var changed = new CardEffectValues(values.Stat, values.Amount + 3, values.Cost, values.Duration);
                check(card.GetCombatDescription(changed).Contains("<color=#9CD889>") && !card.GetCombatDescription().Contains("<color"),
                    "A changed instance value is highlighted without editing the shared card: " + card.cardId);
            }
            var fleet = AssetDatabase.LoadAssetAtPath<CardDefinition>("Assets/Data/Cards/FleetFootwork.asset");
            check(fleet.GetSacrificeDescription(session).Contains("minimum 0.7s"), "Offering text reads the configured attack floor.");
            player.attackSpd = 1;
            Call(session.GetComponent<BattleRunBridge>(), "ApplyModifier", new PendingEncounterModifier
                { effectType = OverworldEffectType.ImprovePlayerAttackSpeed, magnitude = 3, isValid = true }, player);
            check(Mathf.Approximately(player.attackSpd, .7f), "The offering calculation uses the same configurable floor.");
            session.CapturePlayerStats(player);
            check(session.IncreaseMaxHealth(23) && player.maxHealth == 170 && session.PlayerMaxHealth == 170,
                "Permanent maximum-health gains update the session and current fighter.");
            player.maxHealth += 7; player.health = 80; session.CapturePlayerStats(player);
            check(session.PlayerMaxHealth == 177 && session.PlayerHealth == 80, "Future cards can raise maximum HP without losing it during capture.");
        }
        finally { Time.timeScale = 1; clock.enabled = true; }
        foreach (var enemy in enemies) enemy.TakeDamage(20000);
        yield return Wait(() => Object.FindFirstObjectByType<RewardScreenController>()?.IsPresented == true);
        var reward = Object.FindFirstObjectByType<RewardScreenController>();
        Get<UnityEngine.UI.Button>(reward, "skipButton").onClick.Invoke();
        yield return Wait(() => SceneManager.GetActiveScene().name == "Map" && Object.FindFirstObjectByType<MapController>()?.CanInteract == true);
        check(session.PlayerMaxHealth == 177 && session.PlayerHealth == 80, "Maximum HP and current HP survive the battle-to-map return.");
        check(session.TryBeginTravel("level_02_02", out var ticket) && session.Progress.MarkLoading(ticket.EncounterId) &&
            session.ConfirmEncounterStarted(ticket.EncounterId), "Recovery fixture prepares a valid next node.");
        check(session.TryResolveRecovery(ticket.EncounterId, out var receipt) && receipt.Recovered == 41 && session.PlayerHealth == 121,
            "Recovery uses ceil of 23 percent of the increased 177 maximum HP.");
        rules.recoveryPercent = 90;
        check(session.TryResolveRecovery(ticket.EncounterId, out _) && session.PlayerHealth == 121, "Changing recovery settings cannot heal a settled encounter twice.");
        session.CompleteNonCombatEncounter(ticket.EncounterId);
        session.BeginNewRun();
        check(session.PlayerMaxHealth == 147 && session.PlayerHealth == 147, "A new run removes acquired maximum-health gains.");
        rules.startingMaxHealth = 160; session.BeginNewRun();
        check(session.PlayerMaxHealth == 160 && session.PlayerHealth == 160 && session.StartingMaxHealthForTesting == 160,
            "New runs and Monitor adopt a changed configured maximum at full health.");
        rules.deckCapacity = session.DeckCapacity;
        var candidate = session.RunDeck[0].definition;
        check(!session.CanAddCard(candidate), "Current configured capacity blocks additional cards without deleting existing cards.");
        rules.deckCapacity += candidate.capacityCost;
        check(session.TryAddCard(candidate, out _) && session.DeckCapacity == session.DeckCapacityLimit,
            "Raising capacity permits a card exactly at the new limit.");
        check(source == RunBalance.Default && source.startingMaxHealth == originalMaxHealth && source.deckCapacity == originalCapacity,
            "Verification did not write its custom values into the authored balance asset.");
    }

    private static void VerifyDifficulty(RunSession session, BattleScript player, Action<bool, string> check)
    {
        var rules = Object.Instantiate(session.Rules);
        var graph = AssetDatabase.LoadAssetAtPath<Deinosavros.MapReview.MapGraphDefinition>("Assets/Art/Map/Settings/Graph.asset");
        var catalog = AssetDatabase.LoadAssetAtPath<MapNodeCatalog>("Assets/Art/Map/Settings/Nodes.asset");
        var progress = new MapProgressState(graph, catalog);
        var enemies = BattleScript.FindFighters("Enemy").Where(e => e.gameObject.scene == player.gameObject.scene).ToArray();
        var originals = enemies.Select(e => (e.health, e.maxHealth, e.attackDmg, e.attackSpd)).ToArray();
        string playerBefore = EditorJsonUtility.ToJson(player);
        var apply = typeof(BattleRunBridge).GetMethod("ApplyEnemyDifficulty", BindingFlags.Static | BindingFlags.NonPublic);
        void ResetEnemies()
        {
            foreach (var enemy in enemies) { enemy.health = enemy.maxHealth = 20; enemy.attackDmg = 2; }
        }
        void Apply(int layer) => apply.Invoke(null, new object[] { rules, layer, player });
        try
        {
            rules.scaleEnemyStatsByLayer = true;
            rules.enemyHealthGrowthPercent = 10; rules.enemyDamageGrowthPercent = 8;
            int[] expectedHealth = { 20, 22, 24, 26, 28, 30 };
            int[] expectedDamage = { 2, 2, 2, 2, 3, 3 };
            for (int layer = 1; layer <= 6; layer++)
            {
                ResetEnemies(); Apply(layer);
                check(enemies.Length == 3 && enemies.All(e => e.health == expectedHealth[layer - 1] &&
                    e.maxHealth == expectedHealth[layer - 1] && e.attackDmg == expectedDamage[layer - 1]),
                    "Linear enemy HP and rounded attack for map layer " + layer);
            }
            foreach (var node in graph.nodes)
            {
                progress.BeginNewRun(graph, catalog);
                Call(progress, "TrySetTestStartNode", node.id);
                check(progress.TryBeginTravel(node.id, out var ticket) && progress.CurrentEncounterLayer == node.layer,
                    "Difficulty uses the destination graph layer: " + node.id);
                progress.MarkLoading(ticket.EncounterId); progress.ConfirmEncounterStarted(ticket.EncounterId);
                if (ticket.Kind == MapEncounterKind.Opportunity)
                {
                    Call(progress, "PrepareOpportunityBattle", ticket.EncounterId);
                    check(ticket.IsCombat && progress.CurrentEncounterLayer == node.layer,
                        "Opportunity combat retains its map layer rather than counting fought battles.");
                }
            }
            ResetEnemies(); Apply(5);
            Call(session.GetComponent<BattleRunBridge>(), "ApplyModifier", new PendingEncounterModifier
                { effectType = OverworldEffectType.ReduceEnemyStartingHealth, magnitude = 75, isValid = true }, player);
            check(enemies.All(e => e.maxHealth == 28 && e.health == 21),
                "Starting-health offerings reduce scaled current HP without reducing maximum HP.");
            check(!session.GetComponent<BattleRunBridge>().InitializeConfirmedEncounter(session.Progress.CurrentEncounter.EncounterId, player) &&
                enemies.All(e => e.maxHealth == 28 && e.health == 21), "Duplicate receiver confirmation cannot apply scaling or offerings twice.");
            ResetEnemies(); enemies[0].health = 10; Apply(5);
            check(enemies[0].health == 14 && enemies[0].maxHealth == 28, "Authored partial health keeps its fraction.");
            ResetEnemies(); enemies[0].health = 0; enemies[1].attackDmg = 0; Apply(6);
            check(enemies[0].health == 0 && enemies[0].maxHealth == 20 && enemies[1].attackDmg == 0,
                "Scaling neither revives defeated actors nor invents damage for harmless enemies.");
            rules.enemyHealthGrowthPercent = 20; rules.enemyDamageGrowthPercent = 20;
            ResetEnemies(); Apply(4);
            check(enemies.All(e => e.maxHealth == 32 && e.attackDmg == 3), "Edited growth percentages are used by the combat bridge.");
            rules.scaleEnemyStatsByLayer = false; ResetEnemies(); Apply(6);
            check(enemies.All(e => e.maxHealth == 20 && e.attackDmg == 2), "Disabling progression restores authored encounter values.");
            rules.scaleEnemyStatsByLayer = true;
            rules.enemyHealthGrowthPercent = -20; rules.enemyDamageGrowthPercent = -20; ResetEnemies(); Apply(6);
            check(enemies.All(e => e.maxHealth == 20 && e.attackDmg == 2), "Negative serialized growth cannot weaken or invert enemies.");
            check(EditorJsonUtility.ToJson(player) == playerBefore && enemies.Select(e => e.attackSpd).SequenceEqual(originals.Select(e => e.attackSpd)),
                "Difficulty does not change player stats or enemy attack intervals.");
        }
        finally
        {
            for (int i = 0; i < enemies.Length; i++)
                (enemies[i].health, enemies[i].maxHealth, enemies[i].attackDmg, enemies[i].attackSpd) = originals[i];
            Object.Destroy(rules);
        }
    }
}
