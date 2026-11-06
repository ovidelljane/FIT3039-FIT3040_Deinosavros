using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

// Production card input and simulation checks in a disposable Play Mode session.
[InitializeOnLoad]
public static class CardExpansionVerification
{
    private const string Key = "CardExpansionVerification";
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static readonly Stack<IEnumerator> routines = new();
    private static readonly List<string> results = new();
    private static string errors;
    private static double deadline;
    static CardExpansionVerification()
    {
        EditorApplication.update += Tick;
        EditorApplication.playModeStateChanged += state =>
        {
            if (state != PlayModeStateChange.EnteredEditMode || !SessionState.GetBool(Key + ".restore", false)) return;
            EditorSceneManager.playModeStartScene = AssetDatabase.LoadAssetAtPath<SceneAsset>(SessionState.GetString(Key + ".start", ""));
            SessionState.SetBool(Key + ".restore", false);
        };
        Application.logMessageReceived += (message, trace, type) =>
        { if (SessionState.GetBool(Key, false) && (type == LogType.Error || type == LogType.Exception)) errors += message + "\n" + trace; };
    }
    [MenuItem("Tools/Battle/Verify Expanded Card Effects")]
    public static void Start()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Stop Play Mode first.");
        Directory.CreateDirectory("Library/CardExpansion");
        SessionState.SetString(Key + ".start", AssetDatabase.GetAssetPath(EditorSceneManager.playModeStartScene));
        SessionState.SetBool(Key + ".restore", true);
        EditorSceneManager.playModeStartScene = AssetDatabase.LoadAssetAtPath<SceneAsset>("Assets/Scenes/Deinosavros.unity");
        SessionState.SetBool(Key, true); EditorApplication.EnterPlaymode();
    }
    private static void Tick()
    {
        if (!SessionState.GetBool(Key, false) || !EditorApplication.isPlaying || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
        Application.runInBackground = true; EditorApplication.QueuePlayerLoopUpdate();
        try
        {
            if (routines.Count == 0) { results.Clear(); errors = null; deadline = EditorApplication.timeSinceStartup + 150; routines.Push(Run()); }
            if (!string.IsNullOrEmpty(errors)) throw new InvalidOperationException(errors);
            if (EditorApplication.timeSinceStartup > deadline) throw new TimeoutException("Expanded card verification timed out.");
            var current = routines.Peek();
            if (!current.MoveNext()) { routines.Pop(); if (routines.Count == 0) Finish(null); }
            else if (current.Current is IEnumerator nested) routines.Push(nested);
        }
        catch (Exception error) { Finish(error.ToString()); }
    }
    private static void Finish(string error)
    {
        File.WriteAllText("Library/CardExpansion/play-verification.txt", (error == null ? "PASS" : "FAIL") + "\n" + string.Join("\n", results) + (error == null ? "" : "\n" + error));
        SessionState.SetBool(Key, false); routines.Clear(); Time.timeScale = 1; EditorApplication.ExitPlaymode();
    }
    private static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); results.Add("PASS: " + message); }
    private static void Close(float actual, float expected, string message) => Check(Mathf.Abs(actual - expected) < .002f, message);
    private static CardDefinition Card(string name) => AssetDatabase.LoadAssetAtPath<CardDefinition>("Assets/Data/Cards/" + name + ".asset");
    private static T Field<T>(object target, string name) => (T)target.GetType().GetField(name, Private).GetValue(target);
    private static void Advance(TimeTickSystem clock, float seconds) => typeof(TimeTickSystem).GetMethod("Advance", Private).Invoke(clock, new object[] { seconds });
    private static IEnumerator Wait(Func<bool> condition) { while (!condition()) yield return null; }
    private static void ClearEffects()
    { foreach (var effect in Effect.ActiveEffects.ToArray()) if (effect != null) Object.DestroyImmediate(effect.gameObject); }
    private static Effect Active(BattleScript actor, StatType type) => Effect.ActiveEffects.Single(effect => effect != null && effect.IsApplied && effect.target == actor && effect.effectType == type);
    private static BuffCards Spawn(DeckManager deck, CardDefinition definition)
    {
        var card = Object.Instantiate(definition.combatPrefab, Field<Transform>(deck, "handContainer")).GetComponent<BuffCards>();
        card.Initialize(deck, definition); Field<List<BuffCards>>(deck, "hand").Add(card); return card;
    }
    private static void Click(BuffCards card) => card.OnPointerClick(new PointerEventData(EventSystem.current));
    private static void Capture(Canvas canvas, Vector2Int size)
    {
        var camera = Camera.main;
        var target = new RenderTexture(size.x, size.y, 24, RenderTextureFormat.ARGB32);
        var oldMode = canvas.renderMode; var oldCamera = canvas.worldCamera; float oldPlane = canvas.planeDistance, oldAspect = camera.aspect;
        var oldTarget = camera.targetTexture; var active = RenderTexture.active; Texture2D image = null;
        try
        {
            target.Create(); camera.targetTexture = target; camera.aspect = (float)size.x / size.y;
            canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = camera; canvas.planeDistance = camera.nearClipPlane + .5f;
            Canvas.ForceUpdateCanvases();
            foreach (var label in Object.FindObjectsByType<HealthLabel>(FindObjectsSortMode.None).Where(label => label.CompareTag("Enemy") && label.HasSceneBindings))
                typeof(HealthLabel).GetMethod("FollowEnemy", Private)?.Invoke(label, null);
            Canvas.ForceUpdateCanvases();
            RenderPipeline.SubmitRenderRequest(camera, new UniversalRenderPipeline.SingleCameraRequest { destination = target });
            RenderTexture.active = target; image = new Texture2D(size.x, size.y, TextureFormat.RGB24, false);
            image.ReadPixels(new Rect(0, 0, size.x, size.y), 0, 0); image.Apply();
            File.WriteAllBytes("Library/CardExpansion/combat-" + size.x + "x" + size.y + ".png", image.EncodeToPNG());
        }
        finally
        {
            canvas.renderMode = oldMode; canvas.worldCamera = oldCamera; canvas.planeDistance = oldPlane;
            camera.targetTexture = oldTarget; camera.aspect = oldAspect; RenderTexture.active = active;
            if (image != null) Object.Destroy(image); target.Release(); Object.Destroy(target); Canvas.ForceUpdateCanvases();
        }
    }

    private static IEnumerator Run()
    {
        yield return Wait(() => BattleHud.Find(SceneManager.GetActiveScene())?.Deck?.CanPlay == true);
        var scene = SceneManager.GetActiveScene(); var hud = BattleHud.Find(scene); var deck = hud.Deck; var player = deck.Player; var clock = TimeTickSystem.Active;
        clock.enabled = false; clock.StartTimer();
        var enemies = BattleScript.FindFighters("Enemy").Where(enemy => enemy.gameObject.scene == scene).ToArray();
        var boss = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<BossCombat>(true)).Single().GetComponent<BattleScript>();
        boss.gameObject.SetActive(true); yield return null;
        enemies = enemies.Append(boss).Distinct().ToArray();
        foreach (var actor in enemies.Append(player))
        {
            actor.health = actor.maxHealth = actor == player ? 200 : 10000; actor.attackDmg = actor == player ? 20 : 13;
            actor.elixirRegen = 0; actor.shield = 0; actor.encounterAttackFrequency = actor.encounterDamageMultiplier = 1;
            typeof(BattleScript).GetField("attackElapsed", Private).SetValue(actor, -10000d);
        }
        player.attackSpd = 5; player.elixir = player.maxElixir = 100;
        var fleet = Card("FleetFootwork"); var fates = Card("UncertainFates"); var arrow = Card("ArrowHailstorm");
        var pool = AssetDatabase.LoadAssetAtPath<CardPool>("Assets/Data/CardPool.asset");
        var apollo = Card("ApollosGrace"); var hunter = Card("HuntersInstinct");
        Check(pool.Cards.Count == 19 && pool.Cards.Distinct().Count() == 19 && pool.Cards.Contains(arrow) &&
            pool.Cards.Contains(apollo) && pool.Cards.Contains(hunter), "Reward and opportunity pools include 19 unique cards.");
        Check(fleet.BaseValues.Cost == 3 && fates.BaseValues.Cost == 8 && fates.capacityCost == 3 && fates.BaseValues.UsesHealthCost, "Approved Elixir, HP and capacity costs.");

        var card = Spawn(deck, fleet); yield return null; float energy = player.elixir; Click(card); Click(card);
        Close(player.elixir, energy - 3, "Repeated clicks spend Fleet Footwork once.");
        var momentum = Active(player, StatType.CardMomentum);
        Close(player.AttackIntervalSeconds, 4, "Initial frequency bonus turns five seconds into four.");
        Check(momentum.TriggerStacks == 0, "Fleet Footwork does not trigger its own stacks.");
        var blocked = Spawn(deck, Card("SolarShield")); yield return null; player.elixir = 0; Click(blocked);
        Check(momentum.TriggerStacks == 0 && deck.Hand.Contains(blocked), "Unaffordable cards neither stack nor leave the hand.");
        player.elixir = 100; Click(blocked); Click(blocked);
        Check(momentum.TriggerStacks == 1, "A successful card contributes exactly one stack.");
        for (int index = 0; index < 4; index++) { var other = Spawn(deck, Card("SolarShield")); yield return null; Click(other); }
        Check(momentum.TriggerStacks == 3, "Card-triggered stacks stop at the configured cap.");
        Close(player.AttackIntervalSeconds, 5f / 1.7f, "Three stacks provide the configured total frequency.");
        Advance(clock, 3); float remaining = momentum.RemainingSeconds;
        var replay = Spawn(deck, fleet); yield return null; Click(replay);
        Check(Active(player, StatType.CardMomentum) == momentum && momentum.TriggerStacks == 3 && momentum.RemainingSeconds > remaining, "Replaying refreshes one buff without adding a base buff or self-stack.");
        var feedback = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<CombatCardFeedback>(true)).Single();
        var speedBadge = feedback.targets.Single(target => target.actor == player).badges.Single(badge => badge.stat == StatType.AttackSpeed);
        speedBadge.Tick(.3f, false);
        Check(speedBadge.Matches(momentum) && speedBadge.stacks.text == "x3" && feedback.HasEffect(StatType.AttackSpeed), "Existing authored speed timer shows the three momentum stacks.");
        Advance(clock, 8);
        Close(player.AttackIntervalSeconds, 5, "Frequency returns to its actual base after expiration.");

        ClearEffects(); player.elixir = 100; player.shield = 0;
        foreach (var enemy in enemies) enemy.shield = 3;
        var intervals = enemies.ToDictionary(enemy => enemy, enemy => enemy.AttackIntervalSeconds);
        var health = enemies.ToDictionary(enemy => enemy, enemy => enemy.health);
        var rain = Spawn(deck, arrow); yield return null; Click(rain); Click(rain);
        foreach (var enemy in enemies)
        {
            Check(enemy.health == health[enemy] - 5 && enemy.shield == 0, "Arrow damage uses existing shield absorption once: " + enemy.name);
            Check(enemy.AttackDamage == 10, "Damage reduction includes the Boss: " + enemy.name);
            Close(enemy.AttackIntervalSeconds, intervals[enemy], "Damage reduction never changes frequency: " + enemy.name);
        }
        var vfx = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<CombatCardVfx>(true)).Single();
        Check(enemies.All(enemy => vfx.IsShowing(enemy, StatType.DamageAndWeaken)), "All enemies use authored pooled arrow particles.");
        foreach (var target in feedback.targets) foreach (var badge in target.badges) badge.Tick(.3f, false);
        double renderAt = EditorApplication.timeSinceStartup + .12;
        while (EditorApplication.timeSinceStartup < renderAt) yield return null;
        foreach (var size in new[] { new Vector2Int(1920, 1080), new Vector2Int(2560, 1440), new Vector2Int(1920, 1200) }) Capture(hud.Canvas, size);
        var secondRain = Spawn(deck, arrow); yield return null; Click(secondRain);
        Check(enemies.All(enemy => Effect.ActiveEffects.Count(effect => effect.IsApplied && effect.target == enemy && effect.effectType == StatType.DamageReduction) == 1), "Repeated arrow cards refresh rather than multiply weakness.");
        var secondary = new GameObject("Independent weakness").AddComponent<Effect>(); secondary.transform.SetParent(enemies[0].transform);
        secondary.SetValues(StatType.DamageReduction, enemies[0], 10, 12);
        Advance(clock, 8);
        Check(enemies[0].AttackDamage == 12 && enemies.Skip(1).All(enemy => enemy.AttackDamage == 13), "Expiration reveals a remaining weaker effect instead of restoring an old snapshot.");
        Advance(clock, 4); Check(enemies.All(enemy => enemy.AttackDamage == 13), "All temporary weakness expires independently.");

        ClearEffects(); player.maxElixir = 10; player.elixir = 0; player.health = 150;
        var resource = Spawn(deck, fates); yield return null; Click(resource); Click(resource);
        Check(player.health == 142 && player.elixir == 2, "Uncertain Fates pays eight HP once and restores its first two Elixir.");
        var pending = Active(player, StatType.DelayedElixir);
        var resourceBadge = feedback.targets.Single(target => target.actor == player).badges.Single(badge => badge.stat == StatType.ElixirRegen);
        Check(resourceBadge.Matches(pending), "Delayed recovery reuses the authored resource timer.");
        clock.StopTimer(); Advance(clock, 3); Close(player.elixir, 2, "Paused simulation cannot settle delayed Elixir.");
        clock.StartTimer(); Advance(clock, 1); Close(player.elixir, 2, "The second resource payment does not arrive early.");
        player.health = 80; Advance(clock, 1); Close(player.elixir, 5, "The delayed payment reads current low HP and grants its bonus.");
        Advance(clock, 2); Close(player.elixir, 5, "Delayed recovery cannot execute twice.");
        player.health = 80; player.elixir = 0;
        var healed = Spawn(deck, fates); yield return null; Click(healed); player.health = 150; Advance(clock, 2);
        Close(player.elixir, 4, "Healing above the threshold removes the delayed low-health bonus.");
        player.elixir = 9; var capped = Spawn(deck, fates); yield return null; Click(capped); Advance(clock, 2);
        Close(player.elixir, 10, "Both resource payments respect the current maximum.");
        ClearEffects(); player.elixir = 0; player.health = 150;
        var first = Spawn(deck, fates); var second = Spawn(deck, fates); yield return null; Click(first); Click(second); Advance(clock, 2);
        Close(player.elixir, 8, "Separate paid card instances retain independent delayed payments.");

        ClearEffects(); player.health = 80; player.elixir = 10; player.maxElixir = 10;
        var eruption = Spawn(deck, Card("Cataclysm")); yield return null; Click(eruption);
        Close(player.AttackIntervalSeconds, 5f / 1.7f, "Cataclysm keeps its original low-health frequency bonus.");
        Check(player.AttackDamage == 34, "Cataclysm keeps its original low-health damage bonus.");
        var offeringFixture = new GameObject("Offering fixture"); offeringFixture.SetActive(false);
        var bridge = offeringFixture.AddComponent<BattleRunBridge>();
        foreach (var enemy in enemies) enemy.encounterDamageMultiplier = 1;
        typeof(BattleRunBridge).GetMethod("ApplyModifier", Private).Invoke(bridge, new object[] { new PendingEncounterModifier { effectType = OverworldEffectType.ReduceEnemyAttackDamage, magnitude = 10, isValid = true }, player });
        Object.DestroyImmediate(offeringFixture);
        Check(enemies.All(enemy => enemy.AttackDamage == 12) && MapSacrificePreview.For(null, arrow).Compact.Contains("-10%"), "Arrow sacrifice applies its previewed next-battle damage multiplier, including Boss.");
        Check(enemies.All(enemy => Mathf.Approximately(enemy.AttackIntervalSeconds, intervals[enemy])), "The sacrifice modifier does not slow enemy attacks.");
        player.elixir = 0; var canceled = Spawn(deck, fates); yield return null; Click(canceled);
        var abandoned = Active(player, StatType.DelayedElixir); float before = player.elixir;
        abandoned.gameObject.SetActive(false); Advance(clock, 3);
        Close(player.elixir, before, "Disabling a pending effect cancels its payment.");
        foreach (var cardDefinition in pool.Cards)
            Check(!cardDefinition.GetCombatDescription().Contains("{") && cardDefinition.FrontIllustration != null, "Config-driven text and artwork: " + cardDefinition.displayName);
        player.health = player.maxHealth - 9;
        player.elixir = 10;
        int shieldBeforeHealing = player.shield;
        var grace = Spawn(deck, apollo); yield return null; Click(grace); Click(grace);
        Check(player.health == player.maxHealth && player.shield == shieldBeforeHealing && player.elixir == 7,
            "Apollo's Grace heals to the HP cap and spends three Elixir once.");
        var prey = enemies[1];
        prey.health = 35; prey.shield = 5;
        var otherHealth = enemies.Where(enemy => enemy != prey).ToDictionary(enemy => enemy, enemy => enemy.health);
        var hunt = Spawn(deck, hunter); yield return null; Click(hunt); Click(hunt);
        Check(prey.health == 22 && prey.shield == 0 && player.elixir == 4 && otherHealth.All(pair => pair.Key.health == pair.Value),
            "Hunter's Instinct strikes the lowest-HP enemy once and uses ordinary shield absorption.");
        player.elixir = 10; var ending = Spawn(deck, fates); yield return null; Click(ending); before = player.elixir;
        var finished = (Action<BattleScript, bool>)typeof(BattleScript).GetField("OnBattleFinished", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
        finished?.Invoke(player, true);
        Check(!Effect.ActiveEffects.Any(effect => effect != null && effect.IsApplied && effect.target.gameObject.scene == scene), "Battle completion clears all temporary state and pending resource effects.");
        Advance(clock, 3); Close(player.elixir, before, "Battle completion cannot deliver queued resources afterward.");
        clock.enabled = true;
    }
}
