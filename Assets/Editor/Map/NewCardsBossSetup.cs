using System;
using System.IO;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

// Explicit migration, scoped to the two cards and the user's existing Boss object.
public static class NewCardsBossSetup
{
    public const string ScenePath = "Assets/Scenes/Deinosavros.unity";
    public static void VerifySavedBindings()
    {
        string before = File.ReadAllText(ScenePath);
        Apply(); Require(File.ReadAllText(ScenePath) == before, "Reapplication changed the saved scene.");
        Apply(); Require(File.ReadAllText(ScenePath) == before, "Second reapplication changed the saved scene.");
        var scene = EditorSceneManager.OpenScene(ScenePath);
        var roster = All<CombatEncounterRoster>(scene).Single();
        var boss = roster.boss.GetComponent<BattleScript>();
        Require(boss.GetComponent<BossCombat>() != null && boss.GetComponent<HealthLabel>().HasSceneBindings,
            "Boss bindings did not survive reopening.");
        var feedback = All<CombatCardFeedback>(scene).Single();
        Require(feedback.targets.Select(t => t.actor).Distinct().Count() == 5 &&
            feedback.targets.Single(t => t.actor == feedback.player).badges.Length == 6,
            "Actor and timer references must be unique after repeated setup.");
        var pool = AssetDatabase.LoadAssetAtPath<CardPool>("Assets/Data/CardPool.asset");
        Require(pool.Cards.Count == 13 && pool.Cards.Distinct().Count() == 13, "Card pool cannot contain duplicate entries.");
        File.AppendAllText("Library/NewCardsBoss/authoring.txt", "PASS: two reapplications preserved exact scene bytes; reopening retained unique card, actor and timer bindings.\n");
    }
    [MenuItem("Tools/Battle/Configure New Cards and Boss")]
    public static void Apply()
    {
        Require(!EditorApplication.isPlayingOrWillChangePlaymode, "Stop Play Mode first.");
        for (int i = 0; i < SceneManager.sceneCount; i++) Require(!SceneManager.GetSceneAt(i).isDirty, "Save scene changes first.");
        var scene = SceneManager.GetActiveScene();
        Require(scene.path == ScenePath, "Open Deinosavros before configuring the authored Boss.");
        Directory.CreateDirectory("Library/NewCardsBoss");
        if (!File.Exists("Library/NewCardsBoss/Before-authoring.unity")) File.Copy(ScenePath, "Library/NewCardsBoss/Before-authoring.unity");

        var judgement = Card("JudgementDay", "Judgement Day", "judgement-day", "Thunderbolt", "JUDGEMENT_DAY.png",
            StatType.JudgementDay, MapCardType.Attack, 8, 8, 50, 8,
            "Deal {amount} damage to one random combatant (lose {selfDamage} HP if it is you).\nEveryone's attack rate -{slowPercent}% for {duration}s.",
            OverworldEffectType.DivinePunishment, 70, 4,
            "Every {triggerEvery} damage instances you deal, strike a random enemy for {magnitude} damage. Next battle only.");
        var cataclysm = Card("Cataclysm", "Cataclysm", "cataclysm", "Battleborn", "CATACLYSM 1.png",
            StatType.Cataclysm, MapCardType.Buff, 3, 5, 0, 10,
            "For {duration}s, gain damage and attack rate based on current HP:\nBelow {highThreshold}%: +{highBonus}%\nBelow {midThreshold}%: +{midBonus}%\nBelow {lowThreshold}%: +{lowBonus}%\nReplaying refreshes the duration.",
            OverworldEffectType.ReduceEnemyAttackFrequency, 25, 0,
            "Enemy attack rate -{magnitude}% for the entire next battle.");
        var pool = AssetDatabase.LoadAssetAtPath<CardPool>("Assets/Data/CardPool.asset");
        using (var data = new SerializedObject(pool))
        {
            var list = data.FindProperty("cards");
            foreach (var card in new[] { judgement, cataclysm })
                if (!Enumerable.Range(0, list.arraySize).Any(i => list.GetArrayElementAtIndex(i).objectReferenceValue == card))
                { int index = list.arraySize++; list.GetArrayElementAtIndex(index).objectReferenceValue = card; }
            data.ApplyModifiedPropertiesWithoutUndo();
        }

        var boss = All<BattleScript>(scene).Single(actor => actor.name == "Boss" && actor.enabled);
        var pose = boss.transform.localToWorldMatrix;
        bool newlyConfigured = boss.GetComponent<BossCombat>() == null;
        if (newlyConfigured)
        {
            boss.gameObject.AddComponent<BossCombat>();
            boss.health = boss.maxHealth = 300; boss.attackDmg = 13; boss.hitsPerAttack = 1;
        }
        var texture = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Sprites/BOSS_DINO 1.png");
        Require(texture != null, "Import the Boss portrait first.");
        foreach (var renderer in boss.GetComponentsInChildren<Renderer>(true))
        {
            string role = renderer.gameObject == boss.gameObject ? "CombatBoss" : "CombatBossShadow";
            string path = "Assets/Sprites/Materials/" + role + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(renderer.sharedMaterial) { name = role };
                material.SetTexture("_MainTex", texture);
                // Preserve the authored UV mirroring: the scene quad itself is mirrored too.
                AssetDatabase.CreateAsset(material, path);
            }
            renderer.sharedMaterial = material;
        }
        var hud = All<BattleHud>(scene).Single();
        var roster = hud.GetComponent<CombatEncounterRoster>();
        if (roster == null) roster = hud.gameObject.AddComponent<CombatEncounterRoster>();
        roster.boss = boss.gameObject;
        roster.ordinaryEnemies = All<BattleScript>(scene).Where(a => a.enabled && a.CompareTag("Enemy") && a != boss)
            .Select(a => a.gameObject).ToArray();
        Require(roster.ordinaryEnemies.Length == 3, "Preserve the three authored ordinary enemies.");
        var label = boss.GetComponent<HealthLabel>();
        var feedback = All<CombatCardFeedback>(scene).Single();
        using (var data = new SerializedObject(label))
        {
            var oldPanel = (RectTransform)data.FindProperty("enemyPanel").objectReferenceValue;
            var panel = oldPanel.parent.Find("Boss") as RectTransform;
            if (panel == null)
            {
                panel = Object.Instantiate(oldPanel, oldPanel.parent);
                panel.name = "Boss";
                foreach (string field in new[] { "label", "enemyPanel", "enemyFill", "attackText", "visibility" })
                {
                    var component = (Component)data.FindProperty(field).objectReferenceValue;
                    string relative = AnimationUtility.CalculateTransformPath(component.transform, oldPanel);
                    var match = string.IsNullOrEmpty(relative) ? panel : panel.Find(relative);
                    data.FindProperty(field).objectReferenceValue = match.GetComponent(component.GetType());
                }
                data.FindProperty("displayName").stringValue = "Boss";
                data.ApplyModifiedPropertiesWithoutUndo();
                var title = panel.Find("Name")?.GetComponent<TMP_Text>(); if (title != null) title.text = "Boss";
                foreach (var countdown in panel.GetComponentsInChildren<AttackCountdownView>(true))
                {
                    using var binding = new SerializedObject(countdown);
                    binding.FindProperty("actor").objectReferenceValue = boss; binding.ApplyModifiedPropertiesWithoutUndo();
                }
                foreach (var badge in panel.GetComponentsInChildren<CardBuffBadge>(true)) { badge.actor = boss; badge.owner = feedback; }
                panel.gameObject.SetActive(false);
            }
            if (!feedback.targets.Any(t => t.actor == boss))
                feedback.targets = feedback.targets.Append(new CombatCardFeedback.TargetView {
                    actor = boss, anchor = panel, badges = panel.GetComponentsInChildren<CardBuffBadge>(true) }).ToArray();
        }
        // New timers are authored next to the existing row; existing slots and layout remain untouched.
        var playerTarget = feedback.targets.Single(t => t.actor == feedback.player);
        AddBadge(feedback, playerTarget, StatType.Cataclysm, "Cataclysm", StatusSymbol.Damage);
        AddBadge(feedback, playerTarget, StatType.FrequencySlow, "Slow", StatusSymbol.Speed);
        using (var data = new SerializedObject(hud))
        {
            var labels = data.FindProperty("healthLabels");
            if (!Enumerable.Range(0, labels.arraySize).Any(i => labels.GetArrayElementAtIndex(i).objectReferenceValue == label))
            { int i = labels.arraySize++; labels.GetArrayElementAtIndex(i).objectReferenceValue = label; }
            data.ApplyModifiedPropertiesWithoutUndo();
        }
        EditorUtility.SetDirty(feedback); EditorUtility.SetDirty(roster); EditorUtility.SetDirty(boss);
        Require(boss.transform.localToWorldMatrix == pose, "Boss placement must not change.");
        AssetDatabase.SaveAssets();
        EditorSceneManager.MarkSceneDirty(scene);
        Require(EditorSceneManager.SaveScene(scene), "Scene save failed.");
        File.WriteAllText("Library/NewCardsBoss/authoring.txt", "PASS: two cards in the pool; Boss stats, portrait, separate status and roster saved. Existing Boss placement preserved.\n");
    }

    private static void AddBadge(CombatCardFeedback feedback, CombatCardFeedback.TargetView target, StatType stat, string name, StatusSymbol symbol)
    {
        if (target.badges.Any(b => b.stat == stat)) return;
        var template = target.badges[0];
        var badge = Object.Instantiate(template, template.transform.parent); badge.name = name;
        badge.stat = stat; badge.actor = target.actor; badge.owner = feedback;
        var rect = (RectTransform)badge.transform;
        rect.anchoredPosition = ((RectTransform)template.transform).anchoredPosition + new Vector2(76 * target.badges.Length, 0);
        var icon = badge.GetComponentInChildren<StatusIcon>(true); if (icon != null) { icon.symbol = symbol; icon.SetVerticesDirty(); }
        target.badges = target.badges.Append(badge).ToArray();
    }

    private static CardDefinition Card(string name, string title, string id, string templateName, string art,
        StatType stat, MapCardType type, int cost, int capacity, float amount, float duration, string text,
        OverworldEffectType sacrifice, int magnitude, int every, string sacrificeText)
    {
        string path = "Assets/Data/Cards/" + name + ".asset";
        var card = AssetDatabase.LoadAssetAtPath<CardDefinition>(path);
        if (card != null) return card; // Reapplying never overwrites Inspector tuning.
        var template = AssetDatabase.LoadAssetAtPath<CardDefinition>("Assets/Data/Cards/" + templateName + ".asset");
        Require(template != null, "Missing card template.");
        card = ScriptableObject.CreateInstance<CardDefinition>();
        card.name = name; card.displayName = title; card.cardId = id; card.cardType = type;
        card.capacityCost = capacity; card.combatEffectText = text;
        card.frontArtwork = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Sprites/Card Faces/" + art);
        Require(card.frontArtwork != null, "Card artwork must be imported as a Sprite.");
        card.frontBorder = template.frontBorder; card.backArtwork = template.backArtwork;
        card.overworldEffect = new OverworldEffectDefinition { effectType = sacrifice, magnitude = magnitude,
            triggerEvery = every, title = title, description = sacrificeText, scopeLabel = "Next battle" };
        string prefabPath = "Assets/Prefabs/Cards/" + (type == MapCardType.Attack ? "Attack/" : "Buffs/") + name + ".prefab";
        Require(!File.Exists(prefabPath), "Inspect the partial card prefab before retrying.");
        AssetDatabase.CopyAsset(AssetDatabase.GetAssetPath(template.combatPrefab), prefabPath);
        var root = PrefabUtility.LoadPrefabContents(prefabPath);
        try
        {
            root.name = name;
            var controller = root.GetComponent<BuffCards>();
            using var data = new SerializedObject(controller);
            data.FindProperty("stat").enumValueIndex = (int)stat;
            data.FindProperty("amount").floatValue = amount;
            data.FindProperty("effectDuration").floatValue = duration;
            data.FindProperty("elixirCost").intValue = cost;
            var artwork = data.FindProperty("artworkImage").objectReferenceValue as UnityEngine.UI.Image;
            if (artwork != null) { artwork.sprite = card.frontArtwork; artwork.color = Color.white; }
            var label = data.FindProperty("label").objectReferenceValue as TMP_Text;
            if (label != null) label.gameObject.SetActive(false);
            data.ApplyModifiedPropertiesWithoutUndo();
            root.AddComponent<AdvancedCardRules>();
            PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
        card.combatPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
        AssetDatabase.CreateAsset(card, path);
        return card;
    }

    internal static T[] All<T>(Scene scene) where T : Component => scene.GetRootGameObjects()
        .SelectMany(root => root.GetComponentsInChildren<T>(true)).ToArray();
    internal static void Require(bool ok, string message) { if (!ok) throw new InvalidOperationException(message); }
}
