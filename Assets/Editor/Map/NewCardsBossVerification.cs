using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Deinosavros.MapReview;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

// Opt-in tests run only in a disposable Play Mode session, never in saved scenes or assets.
[InitializeOnLoad]
public static class NewCardsBossVerification
{
    private const string Key = "NewCardsBossVerification";
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static readonly Stack<IEnumerator> steps = new();
    private static readonly List<string> results = new();
    private static double deadline;
    private static string error;
    static NewCardsBossVerification()
    {
        EditorApplication.update += Tick;
        Application.logMessageReceived += (message, trace, type) =>
        {
            if (SessionState.GetBool(Key, false) && (type == LogType.Error || type == LogType.Exception || type == LogType.Assert))
                error = message + "\n" + trace;
        };
    }
    [MenuItem("Tools/Battle/Verify New Cards and Boss")]
    public static void Start()
    {
        Check(!EditorApplication.isPlayingOrWillChangePlaymode, "Requires Edit Mode.");
        Check(SceneManager.GetActiveScene().path == NewCardsBossSetup.ScenePath && !SceneManager.GetActiveScene().isDirty,
            "Save and open Combat first.");
        SessionState.SetBool(Key, true); EditorApplication.EnterPlaymode();
    }
    private static void Tick()
    {
        if (!SessionState.GetBool(Key, false) || !EditorApplication.isPlaying || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
        Application.runInBackground = true;
        EditorApplication.QueuePlayerLoopUpdate();
        try
        {
            if (steps.Count == 0) { deadline = EditorApplication.timeSinceStartup + 180; results.Clear(); steps.Push(Run()); }
            if (!string.IsNullOrEmpty(error)) throw new InvalidOperationException(error);
            if (EditorApplication.timeSinceStartup > deadline) throw new TimeoutException("Verification timed out.");
            var current = steps.Peek();
            if (!current.MoveNext()) { steps.Pop(); if (steps.Count == 0) Finish(null); }
            else if (current.Current is IEnumerator nested) steps.Push(nested);
        }
        catch (Exception ex) { Finish(ex.ToString()); }
    }
    private static void Finish(string failure)
    {
        File.WriteAllText("Library/NewCardsBoss/verification.txt", (failure == null ? "PASS" : "FAIL") +
            "\n" + string.Join("\n", results) + (failure == null ? "" : "\n" + failure));
        SessionState.SetBool(Key, false); steps.Clear(); error = null;
        Time.timeScale = 1; EditorApplication.ExitPlaymode();
    }
    private static void Check(bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); results.Add("PASS: " + message); }
    private static void Close(float actual, float expected, string message) => Check(Mathf.Abs(actual - expected) < .002f, message);
    private static object Call(object value, string name, params object[] args) => value.GetType().GetMethod(name, Private).Invoke(value, args);
    private static void Set(object value, string name, object content) => value.GetType().GetField(name, Private).SetValue(value, content);
    private static IEnumerator Wait(Func<bool> predicate)
    { while (!predicate()) yield return null; }
    private static IEnumerator Delay(float seconds)
    { double end = EditorApplication.timeSinceStartup + seconds; while (EditorApplication.timeSinceStartup < end) yield return null; }
    private static void ClearEffects()
    {
        foreach (var effect in Effect.ActiveEffects.ToArray()) { effect.enabled = false; Object.Destroy(effect.gameObject); }
    }
    private static BuffCards Card(DeckManager deck, CardDefinition definition)
    {
        var card = Object.Instantiate(definition.combatPrefab, deck.Hand[0].transform.parent).GetComponent<BuffCards>();
        card.Initialize(deck, definition);
        ((List<BuffCards>)typeof(DeckManager).GetField("hand", Private).GetValue(deck)).Add(card);
        return card;
    }
    private static IEnumerator Run()
    {
        yield return Wait(() => BattleHud.Find(SceneManager.GetActiveScene())?.Deck?.CanPlay == true);
        var hud = BattleHud.Find(SceneManager.GetActiveScene()); var deck = hud.Deck;
        var player = deck.Player; var enemies = BattleScript.FindFighters("Enemy"); var clock = TimeTickSystem.Active;
        Time.timeScale = 0;
        Check(enemies.Count == 3 && enemies.All(e => e.GetComponent<BossCombat>() == null), "Ordinary combat keeps exactly three ordinary enemies.");
        Check(hud.TryPrepare(null, player), "Normal combat HUD, hand and countdown are ready.");
        var pool = AssetDatabase.LoadAssetAtPath<CardPool>("Assets/Data/CardPool.asset");
        var judgement = pool.Cards.Single(c => c.cardId == "judgement-day"); var cat = pool.Cards.Single(c => c.cardId == "cataclysm");
        Check(pool.Cards.Count == 13 && judgement.BaseValues.Cost == 8 && cat.BaseValues.Cost == 3 &&
            judgement.capacityCost == 8 && cat.capacityCost == 5 && cat.BaseValues.Duration == 10, "Pool, costs, capacity and duration match the requested values.");
        Check(!judgement.GetCombatDescription().Contains("{") && !cat.GetCombatDescription().Contains("{"), "New card descriptions resolve all configurable tokens.");
        foreach (var enemy in enemies) { enemy.maxHealth = enemy.health = 10000; enemy.attackDmg = 0; enemy.attackSpd = 10000; }
        player.maxHealth = player.health = 100; player.attackDmg = 100; player.attackSpd = 5; player.shield = 100;
        var rules = judgement.combatPrefab.GetComponent<AdvancedCardRules>();
        var combatants = enemies.Append(player).ToArray();
        rules.ResolveJudgement(player, player, combatants, 50, 8);
        Check(player.health == 80 && player.shield == 100, "Judgement player outcome loses exactly 20 HP and bypasses shield.");
        Check(Effect.ActiveEffects.Count == 4, "Judgement applies one eight-second slow to every living combatant.");
        Close(player.AttackIntervalSeconds, 10, "Half attack rate means twice the interval.");
        ClearEffects();
        rules.ResolveJudgement(player, enemies[0], combatants, 50, 8);
        Check(enemies[0].health == 9950 && enemies[1].health == 10000 && player.health == 80, "Enemy outcome damages only the selected enemy by 50.");
        clock.StopTimer(); Call(clock, "Advance", 4f);
        Close(Effect.ActiveEffects[0].RemainingSeconds, 8, "Effect lifetime pauses with the simulation.");
        clock.StartTimer(); Call(clock, "Advance", 7.9f);
        Check(Effect.ActiveEffects.Count == 4, "Judgement slow remains active before eight seconds.");
        Call(clock, "Advance", .1f);
        Check(Effect.ActiveEffects.Count == 0 && player.AttackIntervalSeconds == 5, "Judgement slow expires once at eight seconds without changing base interval.");

        var rage = cat.combatPrefab.GetComponent<AdvancedCardRules>();
        rage.PlayCataclysm(player, 10);
        foreach (var point in new[] { (100, 100), (99, 115), (75, 115), (74, 140), (50, 140), (49, 170) })
        { player.health = point.Item1; Check(player.AttackDamage == point.Item2, "Cataclysm strict HP threshold: " + point.Item1 + "%."); }
        Close(player.AttackIntervalSeconds, 5 / 1.7f, "Cataclysm +70% attack rate divides interval by 1.7.");
        player.health = 75; Check(player.AttackDamage == 115, "Healing immediately lowers the health-based bonus.");
        rage.PlayCataclysm(player, 10);
        Check(Effect.ActiveEffects.Count == 1 && Effect.ActiveEffects[0].RemainingSeconds == 10, "Replaying Cataclysm refreshes one effect, never stacks.");
        player.health = 49;
        var additive = new GameObject("Additive Test").AddComponent<Effect>(); additive.SetValues(StatType.Damage, player, 10, 20);
        Check(player.AttackDamage == 187, "Existing additive damage and new percentage bonus compose.");
        player.encounterAttackFrequency = .75f;
        Close(player.AttackIntervalSeconds, 5 / (1.7f * .75f), "Permanent and timed rate multipliers compose.");
        player.encounterAttackFrequency = 1;
        Call(clock, "Advance", 10f);
        Check(player.AttackDamage == 110 && player.AttackIntervalSeconds == 5, "Cataclysm expires after ten seconds without reverting other buffs.");
        ClearEffects();

        // Actual card click path covers charging, insufficient resources, discard and double-click protection.
        player.health = 100; player.shield = 0; player.attackDmg = 9; player.elixirRegen = 0;
        var pointer = new PointerEventData(EventSystem.current);
        foreach (var definition in new[] { judgement, cat })
        {
            var card = Card(deck, definition); yield return null;
            player.elixir = 0; int beforeCount = deck.Hand.Count;
            card.OnPointerClick(pointer);
            Check(deck.Hand.Count == beforeCount && Effect.ActiveEffects.Count == 0, "Insufficient Elixir rejects " + definition.cardId + ".");
            player.elixir = player.maxElixir = 30;
            card.OnPointerClick(pointer); card.OnPointerClick(pointer);
            Close(player.elixir, 30 - definition.BaseValues.Cost, "Repeated click charges once: " + definition.cardId + ".");
            Check(deck.Hand.Count == beforeCount - 1, "Successful play discards exactly one card: " + definition.cardId + ".");
            ClearEffects(); yield return null;
        }

        var graph = AssetDatabase.LoadAssetAtPath<MapGraphDefinition>("Assets/Art/Map/Settings/Graph.asset");
        var catalog = AssetDatabase.LoadAssetAtPath<MapNodeCatalog>("Assets/Art/Map/Settings/Nodes.asset");
        var session = RunSession.StartNewRun(new[] { judgement, cat }, graph, catalog);
        Check(session.TrySacrificeInstance(session.RunDeck[0].instanceId), "Judgement sacrifice uses the selected run-card instance.");
        Check(session.PendingModifier.triggerEvery == 4 && session.PendingModifier.magnitude == 70, "Pending punishment retains the 4-hit/70-damage snapshot.");
        Check(session.TryBeginTravel("level_01_01", out var ticket), "Prepare normal encounter.");
        session.Progress.MarkLoading(ticket.EncounterId); session.ConfirmEncounterStarted(ticket.EncounterId);
        Check(session.GetComponent<BattleRunBridge>().InitializeConfirmedEncounter(ticket.EncounterId, player), "Confirmed receiver installs the punishment passive.");
        var passive = player.GetComponent<DivinePunishment>();
        Check(passive != null && !session.HasPendingModifier, "Punishment is installed only when the pending sacrifice is consumed.");
        foreach (var enemy in enemies) { enemy.health = enemy.maxHealth = 10000; enemy.shield = 0; }
        for (int i = 0; i < 3; i++) enemies[0].TakeDamage(1, player);
        Check(passive.Strikes == 0, "Three damage instances do not trigger punishment.");
        int total = enemies.Sum(e => e.health); enemies[0].TakeDamage(1, player);
        Check(total - enemies.Sum(e => e.health) == 71 && passive.Strikes == 1 && passive.DamageInstances == 0,
            "Fourth damage instance triggers 70 damage once and does not recursively recharge.");
        enemies[0].TakeDamage(0, player); enemies[0].TakeDamage(1, null); player.LoseHealth(1, player);
        Check(passive.DamageInstances == 0, "Zero damage, environmental damage and self-harm do not charge punishment.");
        for (int i = 0; i < 4; i++) enemies[1].TakeDamage(1, player);
        Check(passive.Strikes == 2, "Punishment repeats every four valid damage instances.");
        passive.enabled = false;

        // Load the real Boss receiver through an actual Boss ticket, not a separate test prefab.
        session = RunSession.StartNewRun(new[] { judgement, cat }, graph, catalog);
        var args = new object[] { "level_06_01", null, null };
        Check((bool)Call(session, "TryBeginTestRunAt", args), "Monitor-compatible Boss start point.");
        Check(session.TrySacrificeInstance(session.RunDeck[1].instanceId), "Cataclysm sacrifice prepares permanent enemy slow.");
        Check(session.TryBeginTravel("level_06_01", out ticket) && ticket.IsBoss, "Boss ticket is correctly marked.");
        session.Progress.MarkLoading(ticket.EncounterId);
        var loading = SceneManager.LoadSceneAsync("Deinosavros"); yield return Wait(() => loading.isDone); yield return null; yield return null;
        hud = BattleHud.Find(SceneManager.GetActiveScene()); player = BattleHud.FindPlayer(SceneManager.GetActiveScene());
        Check(hud.TryPrepare(session, player), "Boss scene has valid dedicated health, buff, countdown and card bindings.");
        enemies = BattleScript.FindFighters("Enemy");
        Check(enemies.Count == 1 && enemies[0].GetComponent<BossCombat>() != null, "Boss encounter enables only the authored Boss.");
        var boss = enemies[0];
        Check(boss.health == 300 && boss.maxHealth == 300 && boss.attackDmg == 13 && boss.attackSpd == 5,
            "Authored Boss starts at 300 HP, 13 damage and the preserved five-second interval.");
        session.ConfirmEncounterStarted(ticket.EncounterId);
        Check(session.GetComponent<BattleRunBridge>().InitializeConfirmedEncounter(ticket.EncounterId, player), "Boss receiver confirms exactly once.");
        Check(boss.maxHealth == 300 && boss.attackDmg == 13, "Layer-six Encounter Difficulty never scales Boss stats.");
        Close(boss.AttackIntervalSeconds, 5 / .75f, "Cataclysm sacrifice reduces Boss frequency by 25% for this encounter.");
        Check(!session.GetComponent<BattleRunBridge>().InitializeConfirmedEncounter(ticket.EncounterId, player), "Repeated readiness cannot apply sacrifice twice.");
        Check(boss.GetComponent<Renderer>().sharedMaterial.GetTexture("_MainTex").name == "BOSS_DINO 1", "Boss keeps its dedicated portrait instead of random enemy artwork.");
        player.health = player.maxHealth = 10000; player.shield = 0; boss.RefreshOpponents();
        int hits = 0; void Count(BattleScript victim, BattleScript source, int hp, int shield) { if (source == boss && victim == player) hits++; }
        BattleScript.OnDamageReceived += Count;
        try
        {
            for (int attack = 1; attack <= 15; attack++)
            {
                hits = 0; int before = player.health; Call(boss, "Attack");
                int damage = 13 + attack / 5 * 5, expectedHits = attack % 3 == 0 ? 2 : 1;
                Check(hits == expectedHits && before - player.health == damage * expectedHits,
                    "Boss attack " + attack + ": " + expectedHits + " hit(s) at " + damage + " damage.");
            }
        }
        finally { BattleScript.OnDamageReceived -= Count; }
        // Capture a clean, paused preview with the two new cards. Saved gameplay values are untouched.
        boss.attackDmg = 13; player.maxHealth = 100; player.health = 65;
        player.attackDmg = 9; player.attackSpd = 5;
        hud.Deck.SetCombatEnabled(true); TimeTickSystem.Active.StartTimer();
        rage.PlayCataclysm(player, 10);
        Set(player, "attackElapsed", 0d); Set(boss, "attackElapsed", 0d);
        Time.timeScale = 1;
        yield return Delay(1.5f);
        Time.timeScale = 0;
        ScreenCapture.CaptureScreenshot(Path.GetFullPath("Library/NewCardsBoss/Boss-preview.png"));
        yield return Delay(1f);
        Check(Effect.ActiveEffects.Any(e => e.effectType == StatType.Cataclysm) &&
            NewCardsBossSetup.All<CardBuffBadge>(SceneManager.GetActiveScene()).Any(b => b.stat == StatType.Cataclysm && b.StackCount == 1),
            "Cataclysm uses its scene-authored countdown ring.");
        session.CompleteEncounter(ticket.EncounterId, true);
        Check(session.Progress.Phase == MapProgressPhase.Won, "Boss victory completes the adventure.");
    }
}
