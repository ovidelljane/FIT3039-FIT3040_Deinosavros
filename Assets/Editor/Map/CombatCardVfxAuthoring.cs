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
using Object = UnityEngine.Object;

public static class CombatCardVfxAuthoring
{
    private const string Path = "Assets/Scenes/Deinosavros.unity";
    private const string Report = "Library/CombatCardVfx";
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    [MenuItem("Tools/Battle/Bind Card Particles")]
    public static void Apply()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Stop Play Mode first.");
        var scene = SceneManager.GetSceneByPath(Path);
        bool opened = !scene.IsValid() || !scene.isLoaded;
        if (opened) scene = EditorSceneManager.OpenScene(Path, OpenSceneMode.Additive);
        try
        {
            // Reapplying to a loaded scene preserves its current objects and Inspector values.
            if (scene.isDirty && !scene.GetRootGameObjects().Any(r => r.GetComponent<CombatCardVfx>() != null))
                throw new InvalidOperationException("Save Combat scene edits before the initial particle binding.");
            Directory.CreateDirectory(Report);
            if (!File.Exists(Report + "/before.unity")) File.Copy(Path, Report + "/before.unity");
            var root = scene.GetRootGameObjects().Single(r => r.name == "Particles");
            var sources = root.GetComponentsInChildren<ParticleSystem>(true);
            ParticleSystem Source(string parent, string name) => sources.First(s => s.name == name && s.transform.parent.name == parent);
            var unchanged = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Transform>(true))
                .Where(t => t != root.transform && !t.IsChildOf(root.transform))
                .Select(t => (t, t.localPosition, t.localRotation, t.localScale)).ToArray();
            var vfx = root.GetComponent<CombatCardVfx>();
            if (vfx == null) vfx = Undo.AddComponent<CombatCardVfx>(root);
            if (vfx.bindings == null || vfx.bindings.Length == 0)
            {
                var aura = Source("BuffParticle", "BuffAura");
                var spiral = Source("BuffParticle", "BuffSpiral");
                var ground = Source("DebuffParticle", "DebuffGround");
                var drain = Source("DebuffParticle", "BebuffDrain");
                vfx.bindings = new[]
                {
                    Bind("Haste", true, new[] { StatType.AttackSpeed }, Part(spiral, -.25f, .10f, true)),
                    Bind("MultiHit", true, new[] { StatType.ExtraHits }, Part(aura, -.40f, .045f, true), Part(spiral, -.25f, .10f, true)),
                    Bind("Cataclysm", true, new[] { StatType.Cataclysm }, Part(aura, -.40f, .045f, true)),
                    Bind("Damage", true, new[] { StatType.Damage }, Part(Source("AttackEnemyParticle", "WaterAura"), -.40f, .045f, true),
                        Part(Source("AttackEnemyParticle", "Ripple"), -.42f, .15f, true)),
                    Bind("Shield", true, new[] { StatType.Shield }, Part(Source("ShieldParticle", "ShieldGather"), 0, .15f, false),
                        Part(Source("ShieldParticle", "Shield"), 0, .18f, true)),
                    Bind("Elixir", false, new[] { StatType.Elixir, StatType.ElixirRegen },
                        Part(Source("ElixirParticle", "ElixirRise"), -.35f, .18f, false), Part(Source("ElixirParticle", "Flash"), .10f, .12f, false)),
                    Bind("Slow", true, new[] { StatType.EnemySlow, StatType.FrequencySlow }, Part(ground, -.23f, .12f, true), Part(drain, -.10f, .18f, true))
                };
                vfx.attackImpact = Part(Source("MainAttack", "Claw"), .05f, .24f, false);
                vfx.attackImpact.playbackSpeed = 3;
                EditorUtility.SetDirty(vfx);
            }
            // Keep the teammate's assets as editable templates, not free-running demonstrations.
            foreach (Transform child in root.transform)
            {
                if (!child.gameObject.activeSelf) continue;
                Undo.RecordObject(child.gameObject, "Disable particle demonstration");
                child.gameObject.SetActive(false);
                PrefabUtility.RecordPrefabInstancePropertyModifications(child.gameObject);
            }
            foreach (var system in sources)
            {
                Undo.RecordObject(system, "Disable particle autoplay");
                var main = system.main; main.playOnAwake = false;
                PrefabUtility.RecordPrefabInstancePropertyModifications(system);
            }
            foreach (var item in unchanged)
                Require(item.t.localPosition == item.localPosition && item.t.localRotation == item.localRotation && item.t.localScale == item.localScale,
                    "An unrelated transform changed: " + item.t.name);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Verify(scene);
        }
        finally { if (opened) EditorSceneManager.CloseScene(scene, true); }
    }

    private static CombatCardVfx.Part Part(ParticleSystem template, float height, float scale, bool loop) =>
        new() { template = template, offset = new Vector3(0, height, 0), scale = scale, loop = loop };
    private static CombatCardVfx.Binding Bind(string name, bool sustained, StatType[] stats, params CombatCardVfx.Part[] parts) =>
        new() { name = name, sustained = sustained, stats = stats, parts = parts };
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }

    private static void Verify(Scene scene)
    {
        var root = scene.GetRootGameObjects().Single(r => r.name == "Particles");
        var vfx = root.GetComponent<CombatCardVfx>();
        Require(vfx != null && vfx.bindings.Length == 7 && vfx.attackImpact.template != null, "Missing VFX bindings.");
        Require(root.GetComponentsInChildren<ParticleSystem>().Length == 0, "A demonstration is still active.");
        Require(vfx.bindings.All(b => b.parts.All(p => p.template != null && p.template.gameObject.scene == scene)), "A template reference is missing.");
        var report = new List<string> { "PASS: seven stat bindings, attack impact, disabled demonstrations and unchanged non-particle transforms." };
        foreach (var actor in scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<BattleScript>(true)))
            if (actor.enabled) report.Add(actor.name + " bounds=" + actor.GetComponent<Renderer>()?.bounds);
        foreach (var part in vfx.bindings.SelectMany(b => b.parts).Concat(new[] { vfx.attackImpact }))
            report.Add(part.template.name + " size=" + part.template.main.startSize.constantMax + " lifetime=" +
                part.template.main.startLifetime.constantMax + " sourceScale=" + part.template.transform.localScale + " scale=" + part.scale);
        File.WriteAllLines(Report + "/bindings.txt", report);
    }

    public static IEnumerator VerifyRuntime(Action<bool, string> check)
    {
        yield return Wait(() => Object.FindFirstObjectByType<MainMenuController>() != null);
        typeof(MainMenuController).GetMethod("StartRun", Private).Invoke(Object.FindFirstObjectByType<MainMenuController>(), null);
        yield return Wait(() => BattleHud.Find(SceneManager.GetActiveScene())?.Deck?.CanPlay == true);
        var hud = BattleHud.Find(SceneManager.GetActiveScene());
        var deck = hud.Deck; var player = deck.Player;
        var vfx = Object.FindFirstObjectByType<CombatCardVfx>();
        var enemies = BattleScript.FindFighters("Enemy");
        check(vfx != null && vfx.ActiveVisualCount == 0, "No demonstration particles play before a card or attack.");
        foreach (var enemy in enemies) { enemy.health = enemy.maxHealth = 10000; enemy.attackDmg = 0; enemy.attackSpd = 10000; }
        player.attackSpd = 10000;
        var hand = (List<BuffCards>)typeof(DeckManager).GetField("hand", Private).GetValue(deck);
        foreach (var definition in AssetDatabase.LoadAssetAtPath<CardPool>("Assets/Data/CardPool.asset").Cards)
        {
            ClearEffects(); player.shield = 0;
            vfx.enabled = false; vfx.enabled = true;
            yield return null;
            player.health = 80; player.maxHealth = 100; player.elixir = 10; player.maxElixir = 20;
            var card = Object.Instantiate(definition.combatPrefab, deck.Hand[0].transform.parent).GetComponent<BuffCards>();
            card.Initialize(deck, definition); hand.Add(card);
            yield return null;
            var values = card.Values;
            int bursts = vfx.BurstCount, handCount = deck.Hand.Count;
            card.OnPointerClick(new PointerEventData(EventSystem.current));
            card.OnPointerClick(new PointerEventData(EventSystem.current));
            yield return null;
            check(deck.Hand.Count == handCount - 1, "Single accepted play: " + definition.cardId);
            switch (values.Stat)
            {
                case StatType.EnemySlow:
                    check(enemies.All(e => vfx.IsShowing(e, values.Stat)) && !vfx.IsShowing(player, values.Stat), "Clouded Minds targets enemies only."); break;
                case StatType.JudgementDay:
                    check(enemies.All(e => vfx.IsShowing(e, StatType.FrequencySlow)) && vfx.IsShowing(player, StatType.FrequencySlow), "Judgement slows all combatants, including the player."); break;
                case StatType.DamageAllEnemies:
                    check(vfx.BurstCount == bursts, "Thunderbolt uses the existing damage impact, not a water or claw burst."); break;
                case StatType.Elixir:
                case StatType.ElixirRegen:
                    check(vfx.BurstCount == bursts + 1, "One Elixir burst per accepted card."); break;
                default:
                    check(vfx.IsShowing(player, values.Stat), "Player buff uses the correct binding: " + definition.cardId); break;
            }
            if (values.Stat == StatType.Damage || values.Stat == StatType.AttackSpeed || values.Stat == StatType.Shield ||
                values.Stat == StatType.Cataclysm || values.Stat == StatType.EnemySlow)
            {
                yield return Delay(1f);
                File.AppendAllLines(Report + "/runtime-particles.txt", vfx.GetComponentsInChildren<ParticleSystem>().Select(s =>
                    definition.cardId + "/" + s.name + " count=" + s.particleCount + " position=" + s.transform.position + " bounds=" +
                    s.GetComponent<Renderer>().bounds));
                ScreenCapture.CaptureScreenshot(Report + "/" + definition.cardId + ".png");
                yield return null;
            }
        }
        ClearEffects(); player.shield = 0; yield return null;
        var first = AddEffect(player, StatType.Damage, 2, 3);
        var second = AddEffect(player, StatType.Damage, 4, 5);
        yield return null;
        int count = vfx.ActiveVisualCount;
        first.enabled = false; yield return null;
        check(vfx.IsShowing(player, StatType.Damage) && vfx.ActiveVisualCount <= count, "Overlapping buffs share a visual; removing one keeps the other.");
        second.enabled = false; yield return null;
        check(!vfx.IsShowing(player, StatType.Damage), "The final removal stops its visual without modifying stats.");
        player.shield = 6; yield return null;
        check(vfx.IsShowing(player, StatType.Shield), "Initial and sacrifice shields are recognized from real shield state.");
        player.TakeDamage(6, enemies[0]); yield return null;
        check(player.shield == 0 && !vfx.IsShowing(player, StatType.Shield), "Shield particles stop when damage consumes the shield.");
        player.elixir = 0; player.elixirRegen = 1;
        int beforeNatural = vfx.BurstCount;
        yield return Delay(.3f);
        check(vfx.BurstCount == beforeNatural, "Natural Elixir ticks do not replay card particles.");
        player.elixir = 0;
        var unaffordable = AssetDatabase.LoadAssetAtPath<CardPool>("Assets/Data/CardPool.asset").Cards.First(d => d.BaseValues.Cost > 0 && d.BaseValues.Stat != StatType.Elixir);
        var rejected = Object.Instantiate(unaffordable.combatPrefab, deck.Hand[0].transform.parent).GetComponent<BuffCards>();
        rejected.Initialize(deck, unaffordable); hand.Add(rejected); yield return null;
        int beforeRejected = vfx.ActiveVisualCount;
        rejected.OnPointerClick(new PointerEventData(EventSystem.current)); yield return null;
        check(vfx.ActiveVisualCount <= beforeRejected && vfx.BurstCount == beforeNatural, "Rejected cards do not play success VFX.");
        var haste = AddEffect(player, StatType.AttackSpeed, 1, .2f);
        yield return Wait(() => !haste.IsApplied);
        yield return null;
        check(!vfx.IsShowing(player, StatType.AttackSpeed), "Natural expiration stops VFX on the combat clock.");
        AddEffect(player, StatType.ExtraHits, 1, 3);
        Time.timeScale = 0; yield return null;
        var live = vfx.GetComponentsInChildren<ParticleSystem>().First(s => s.isPlaying);
        float time = live.time;
        yield return Delay(.15f);
        check(Mathf.Approximately(time, live.time), "Pausing freezes particle animation.");
        Time.timeScale = 1;
        int beforeAttack = vfx.BurstCount;
        typeof(BattleScript).GetMethod("Attack", Private).Invoke(player, null);
        yield return null;
        check(vfx.BurstCount == beforeAttack + 1, "A real basic attack emits one claw impact.");
        yield return Delay(.15f);
        ScreenCapture.CaptureScreenshot(Report + "/combat-effects.png");
        yield return null;
        vfx.enabled = false; yield return null;
        check(vfx.ActiveVisualCount == 0, "Disabling the controller cleans up visuals and subscriptions.");
        vfx.enabled = true;
        AddEffect(player, StatType.Damage, 2, 3);
        foreach (var enemy in enemies) enemy.TakeDamage(100000, player);
        yield return Wait(() => !player.enabled);
        check(vfx.ActiveVisualCount == 0, "Battle completion clears all card and attack particles.");
    }

    private static void ClearEffects()
    {
        foreach (var effect in Effect.ActiveEffects.ToArray())
        { effect.enabled = false; Object.Destroy(effect.gameObject); }
    }
    private static Effect AddEffect(BattleScript target, StatType stat, float amount, float duration)
    {
        var effect = new GameObject("Verification Effect").AddComponent<Effect>();
        effect.SetValues(stat, target, amount, duration); return effect;
    }
    private static IEnumerator Wait(Func<bool> condition)
    {
        double end = EditorApplication.timeSinceStartup + 45;
        while (!condition())
        {
            if (EditorApplication.timeSinceStartup > end) throw new InvalidOperationException("Combat particle verification timed out.");
            yield return null;
        }
    }
    private static IEnumerator Delay(float duration)
    {
        double end = EditorApplication.timeSinceStartup + duration;
        while (EditorApplication.timeSinceStartup < end) yield return null;
    }
}
