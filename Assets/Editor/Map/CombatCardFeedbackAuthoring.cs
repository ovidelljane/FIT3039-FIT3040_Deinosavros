using System;
using System.IO;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

// Explicit, additive authoring. Existing scene-owned UI is never rebuilt or repositioned.
public static class CombatCardFeedbackAuthoring
{
    private const string ScenePath = "Assets/Scenes/Deinosavros.unity";

    public static void EnlargeCountdownsAndVerify()
    {
        EnlargeCountdowns();
        VerifySavedSceneAndPlay();
    }

    [MenuItem("Tools/Battle/Enlarge Status Countdowns")]
    public static void EnlargeCountdowns()
    {
        Require(!EditorApplication.isPlayingOrWillChangePlaymode, "Stop Play Mode before editing countdowns.");
        for (int i = 0; i < SceneManager.sceneCount; i++)
            Require(!SceneManager.GetSceneAt(i).isDirty, "Save your scene changes first.");
        var setup = EditorSceneManager.GetSceneManagerSetup();
        Directory.CreateDirectory(RunIntegrationSetup.ResultDirectory);
        string backup = RunIntegrationSetup.ResultDirectory + "/Combat-before-large-countdowns.unity";
        if (!File.Exists(backup)) File.Copy(ScenePath, backup);
        try
        {
            var scene = EditorSceneManager.OpenScene(ScenePath);
            Validate(scene);
            var feedback = Components<CombatCardFeedback>(scene).Single();
            foreach (var target in feedback.targets)
                ConfigureCountdownRow(target.badges, target.actor == feedback.player);
            EditorSceneManager.MarkSceneDirty(scene);
            Require(EditorSceneManager.SaveScene(scene), "Could not save countdown layout.");
            scene = EditorSceneManager.OpenScene(ScenePath);
            Validate(scene);
            var badges = Components<CardBuffBadge>(scene);
            Require(badges.Length == 7 && badges.All(b => b.visibility.alpha == 1 &&
                ((RectTransform)b.transform).sizeDelta == new Vector2(52, 52) && b.seconds.fontSize == 24),
                "Seven enlarged countdown previews must survive saving and reopening.");
            File.WriteAllText(RunIntegrationSetup.ResultDirectory + "/countdown-hierarchy.txt",
                "PASS: Seven editable scene-owned countdowns; 52px rings, 32px symbols, 24px seconds and 20px stack counts.\n" +
                string.Join("\n", badges.Select(b => HierarchyPath(b.transform))) + "\n");
        }
        finally
        {
            if (setup.Any(s => s.isActive && s.isLoaded)) EditorSceneManager.RestoreSceneManagerSetup(setup);
            else EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        }
    }

    private static string HierarchyPath(Transform item) => item.parent != null ? HierarchyPath(item.parent) + "/" + item.name : item.name;

    private static void ConfigureCountdownRow(CardBuffBadge[] badges, bool player)
    {
        var row = (RectTransform)badges[0].transform.parent;
        // Player buffs stay below the status panel; enemy buffs sit beside their bar,
        // leaving the larger icon and its seconds clear of the portrait underneath.
        PlayerStatusView.Place(row, player ? Vector2.zero : Vector2.one,
            player ? new Vector2(18, -14) : new Vector2(14, 0),
            new Vector2(player ? 300 : 64, 100), new Vector2(0, 1));
        for (int i = 0; i < badges.Length; i++)
        {
            var badge = badges[i];
            var root = (RectTransform)badge.transform;
            PlayerStatusView.Place(root, new Vector2(0, 1), new Vector2(26 + i * 76, -26), new Vector2(52, 52));
            var icon = (RectTransform)root.Find("Icon");
            PlayerStatusView.Place(icon, new Vector2(.5f, .5f), Vector2.zero, new Vector2(32, 32));
            PlayerStatusView.Place(badge.seconds.rectTransform, new Vector2(.5f, 0), new Vector2(0, -23), new Vector2(72, 40));
            badge.seconds.fontSize = 24; badge.seconds.fontStyle = FontStyles.Bold; badge.seconds.enableAutoSizing = false;
            PlayerStatusView.Place(badge.stacks.rectTransform, Vector2.one, new Vector2(2, 1), new Vector2(40, 34));
            badge.stacks.fontSize = 20; badge.stacks.fontStyle = FontStyles.Bold; badge.stacks.enableAutoSizing = false;
            badge.ring.thickness = .14f; badge.ring.remaining = .75f;
            // These saved examples are presentation only. OnEnable clears them in Play Mode.
            badge.visibility.alpha = 1; badge.visibility.blocksRaycasts = false; badge.hitArea.raycastTarget = false;
            badge.seconds.text = "0.0"; badge.stacks.text = "x2";
            badge.ring.SetVerticesDirty();
        }
    }

    public static void VerifySavedSceneAndPlay()
    {
        string before = File.ReadAllText(ScenePath);
        Apply();
        Require(File.ReadAllText(ScenePath) == before, "Reapplying must not change saved scene bytes.");
        Apply();
        Require(File.ReadAllText(ScenePath) == before, "A second reapplication must remain identical.");
        File.AppendAllText(RunIntegrationSetup.ResultDirectory + "/card-feedback-authoring.txt",
            "PASS: two consecutive applications preserve scene bytes, transforms, bindings and authored styles.\n");
        RunIntegrationPlayVerification.RunCardFeedback();
    }
    [MenuItem("Tools/Battle/Author Card Feedback")]
    public static void Apply()
    {
        Require(!EditorApplication.isPlayingOrWillChangePlaymode, "Stop Play Mode before authoring card feedback.");
        for (int i = 0; i < SceneManager.sceneCount; i++)
            Require(!SceneManager.GetSceneAt(i).isDirty, "Save your scene changes first.");
        var setup = EditorSceneManager.GetSceneManagerSetup();
        Directory.CreateDirectory(RunIntegrationSetup.ResultDirectory);
        try
        {
            string backup = RunIntegrationSetup.ResultDirectory + "/Combat-before-card-feedback.unity";
            if (!File.Exists(backup)) File.Copy(ScenePath, backup);
            var scene = EditorSceneManager.OpenScene(ScenePath);
            var hud = Components<BattleHud>(scene).Single();
            Require(hud.StatusRoot != null && hud.Status.IsReady, "Author the existing combat status first.");
            var feedback = Components<CombatCardFeedback>(scene).SingleOrDefault();
            if (feedback == null)
            {
                Require(hud.StatusRoot.Find("Feedback") == null, "Inspect the partial Feedback root before authoring.");
                var root = PlayerStatusView.Rect("Feedback", hud.StatusRoot); Stretch(root);
                feedback = root.gameObject.AddComponent<CombatCardFeedback>();
                feedback.playerView = hud.Status; feedback.player = BattleHud.FindPlayer(scene);
            }
            bool changed = BuildMissing(feedback, hud, scene);
            // Only save when something was added; reapplication preserves the exact scene bytes.
            if (changed) { EditorSceneManager.MarkSceneDirty(scene); Require(EditorSceneManager.SaveScene(scene), "Scene save failed."); }
            scene = EditorSceneManager.OpenScene(ScenePath);
            Validate(scene);
            File.WriteAllText(RunIntegrationSetup.ResultDirectory + "/card-feedback-authoring.txt",
                "PASS: saved scene bindings, 7 authored buff slots, pooled text template, resource overlays and persistent fonts.\n");
        }
        finally
        {
            if (setup.Any(s => s.isActive && s.isLoaded)) EditorSceneManager.RestoreSceneManagerSetup(setup);
            else EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        }
    }

    private static bool BuildMissing(CombatCardFeedback feedback, BattleHud hud, Scene scene)
    {
        bool changed = false;
        var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/Resources/Fonts/Alegreya SDF.asset");
        Require(font != null, "The saved body font must exist.");
        if (feedback.popupLayer == null)
        {
            feedback.popupLayer = PlayerStatusView.Rect("Floats", feedback.transform); Stretch(feedback.popupLayer);
            var group = feedback.popupLayer.gameObject.AddComponent<CanvasGroup>(); group.blocksRaycasts = group.interactable = false;
            changed = true;
        }
        if (feedback.popupTemplate == null)
        {
            feedback.popupTemplate = Text("Template", feedback.popupLayer, font, 22);
            feedback.popupTemplate.fontStyle = FontStyles.Bold;
            feedback.popupTemplate.color = feedback.bonusColor;
            feedback.popupTemplate.rectTransform.sizeDelta = new Vector2(180, 30);
            var outline = feedback.popupTemplate.gameObject.AddComponent<Outline>();
            outline.effectColor = new Color(.09f, .055f, .03f, .95f); outline.effectDistance = new Vector2(1, -1);
            feedback.popupTemplate.gameObject.SetActive(false); changed = true;
        }
        if (feedback.tooltip == null)
        {
            var background = PlayerStatusView.Box("Tooltip", feedback.transform, PlayerStatusView.Ink);
            feedback.tooltip = background.rectTransform; feedback.tooltip.pivot = new Vector2(0, 1);
            feedback.tooltip.sizeDelta = new Vector2(300, 100);
            Border(background.gameObject);
            feedback.tooltipText = Text("Text", feedback.tooltip, font, 18);
            Stretch(feedback.tooltipText.rectTransform);
            feedback.tooltipText.margin = new Vector4(12, 8, 12, 8);
            feedback.tooltipText.alignment = TextAlignmentOptions.TopLeft;
            feedback.tooltip.gameObject.SetActive(false); changed = true;
        }
        if (feedback.targets == null || feedback.targets.Length == 0)
        {
            var bindings = new System.Collections.Generic.List<CombatCardFeedback.TargetView>();
            bindings.Add(BuildTarget(feedback, feedback.player, (RectTransform)hud.Status.transform, font, true));
            foreach (var label in Components<HealthLabel>(scene).Where(l => l.enabled && l.CompareTag("Enemy") &&
                l.TryGetComponent<BattleScript>(out var actor) && actor.enabled).OrderBy(l => l.transform.position.x))
            {
                using var data = new SerializedObject(label);
                var panel = (RectTransform)data.FindProperty("enemyPanel").objectReferenceValue;
                bindings.Add(BuildTarget(feedback, label.GetComponent<BattleScript>(), panel, font, false));
            }
            feedback.targets = bindings.ToArray(); changed = true;
        }
        foreach (var target in feedback.targets)
            foreach (var badge in target.badges)
            {
                if (badge.transform.Find("Back") == null)
                {
                    var back = PlayerStatusView.Rect("Back", badge.transform).gameObject.AddComponent<CardBuffRing>();
                    Stretch(back.rectTransform); back.thickness = 1; back.color = PlayerStatusView.Ink;
                    back.raycastTarget = false; back.transform.SetAsFirstSibling(); changed = true;
                }
                foreach (var ring in badge.GetComponentsInChildren<CardBuffRing>(true))
                    if (ring.GetComponent<CanvasRenderer>() == null)
                    { ring.gameObject.AddComponent<CanvasRenderer>(); changed = true; }
                foreach (var text in new[] { badge.seconds, badge.stacks })
                    if (text.GetComponent<Outline>() == null)
                    {
                        var edge = text.gameObject.AddComponent<Outline>(); edge.effectColor = PlayerStatusView.Ink;
                        edge.effectDistance = new Vector2(1, -1); changed = true;
                    }
            }
        foreach (var bar in new[] { hud.Status.Health, hud.Status.Elixir })
        {
            if (bar.ChangeOverlay != null) continue;
            var overlay = PlayerStatusView.Box("Change", bar.Fill.transform.parent, Color.clear);
            Stretch(overlay.rectTransform); overlay.transform.SetSiblingIndex(bar.Preview.transform.GetSiblingIndex());
            overlay.gameObject.SetActive(false);
            using var data = new SerializedObject(bar);
            data.FindProperty("changeOverlay").objectReferenceValue = overlay; data.ApplyModifiedPropertiesWithoutUndo();
            changed = true;
        }
        if (changed) EditorUtility.SetDirty(feedback);
        return changed;
    }

    private static CombatCardFeedback.TargetView BuildTarget(CombatCardFeedback owner, BattleScript actor,
        RectTransform panel, TMP_FontAsset font, bool player)
    {
        Require(panel.Find("Buffs") == null, "Inspect the existing Buffs row before authoring: " + panel.name);
        var row = PlayerStatusView.Rect("Buffs", panel);
        var ignore = row.gameObject.AddComponent<LayoutElement>(); ignore.ignoreLayout = true;
        PlayerStatusView.Place(row, new Vector2(0, 0), new Vector2(player ? 18 : 4, -8),
            new Vector2(player ? 220 : 50, 50), new Vector2(0, 1));
        var group = row.gameObject.AddComponent<CanvasGroup>();
        group.ignoreParentGroups = true; group.blocksRaycasts = group.interactable = true;
        var types = player ? new[] { StatType.Damage, StatType.AttackSpeed, StatType.ExtraHits, StatType.ElixirRegen } :
            new[] { StatType.EnemySlow };
        var badges = new CardBuffBadge[types.Length];
        for (int i = 0; i < types.Length; i++)
        {
            string name = types[i] == StatType.AttackSpeed ? "Speed" : types[i] == StatType.ExtraHits ? "Hits" :
                types[i] == StatType.ElixirRegen ? "Regen" : types[i] == StatType.EnemySlow ? "Slow" : "Damage";
            var root = PlayerStatusView.Rect(name, row);
            PlayerStatusView.Place(root, new Vector2(0, 1), new Vector2(16 + i * 52, -16), new Vector2(32, 32));
            var badge = root.gameObject.AddComponent<CardBuffBadge>(); badges[i] = badge;
            badge.owner = owner; badge.actor = actor; badge.stat = types[i];
            badge.visibility = root.gameObject.AddComponent<CanvasGroup>(); badge.visibility.alpha = 0;
            badge.visibility.blocksRaycasts = false;
            badge.hitArea = root.gameObject.AddComponent<Image>(); badge.hitArea.color = Color.clear; badge.hitArea.raycastTarget = false;
            var track = PlayerStatusView.Rect("Track", root).gameObject.AddComponent<CardBuffRing>();
            Stretch(track.rectTransform); track.color = PlayerStatusView.Gold; track.thickness = .18f; track.raycastTarget = false;
            badge.ring = PlayerStatusView.Rect("Ring", root).gameObject.AddComponent<CardBuffRing>();
            Stretch(badge.ring.rectTransform); badge.ring.raycastTarget = false;
            badge.ring.color = types[i] == StatType.ElixirRegen ? owner.elixirColor :
                types[i] == StatType.EnemySlow ? owner.debuffColor : owner.activeColor;
            var symbol = types[i] == StatType.Damage ? StatusSymbol.Damage : types[i] == StatType.ExtraHits ? StatusSymbol.Hits :
                types[i] == StatType.ElixirRegen ? StatusSymbol.Elixir : StatusSymbol.Speed;
            var icon = PlayerStatusView.Icon(root, symbol, badge.ring.color); icon.rectTransform.sizeDelta = new Vector2(20, 20);
            badge.seconds = Text("Seconds", root, font, 16);
            PlayerStatusView.Place(badge.seconds.rectTransform, new Vector2(.5f, 0), new Vector2(0, -9), new Vector2(48, 20));
            badge.stacks = Text("Stacks", root, font, 14);
            PlayerStatusView.Place(badge.stacks.rectTransform, Vector2.one, new Vector2(5, 2), new Vector2(24, 18));
        }
        ConfigureCountdownRow(badges, player);
        return new CombatCardFeedback.TargetView { actor = actor, anchor = panel, badges = badges };
    }

    public static void Validate(Scene scene)
    {
        var feedback = Components<CombatCardFeedback>(scene).Single();
        Require(feedback.IsReady && feedback.targets.Length == 4, "One player and three enemy bindings are required.");
        Require(feedback.targets.Sum(t => t.badges.Length) == 7, "Keep four player and three enemy buff slots.");
        Require(feedback.playerView.Health.ChangeOverlay != null && feedback.playerView.Elixir.ChangeOverlay != null,
            "Both resource bars need authored change overlays.");
        foreach (var target in feedback.targets)
            foreach (var badge in target.badges)
                Require(badge.owner == feedback && badge.actor == target.actor && badge.ring != null &&
                    badge.seconds != null && badge.stacks != null && badge.hitArea != null && badge.ring.canvasRenderer != null,
                    "Incomplete buff slot.");
        foreach (var text in feedback.GetComponentsInChildren<TMP_Text>(true))
            Require(text.font != null && AssetDatabase.Contains(text.font), "Keep persistent feedback fonts.");
        CombatStatusSceneAuthoring.Validate(scene);
    }
    private static TMP_Text Text(string name, Transform parent, TMP_FontAsset font, float size)
    {
        var text = PlayerStatusView.Rect(name, parent).gameObject.AddComponent<TextMeshProUGUI>();
        text.font = font; text.fontSize = size; text.color = PlayerStatusView.Cream;
        text.alignment = TextAlignmentOptions.Center; text.raycastTarget = false; text.text = "";
        text.textWrappingMode = TextWrappingModes.NoWrap; return text;
    }
    private static void Stretch(RectTransform rect)
    { rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.offsetMin = rect.offsetMax = Vector2.zero; }
    private static void Border(GameObject root)
    { var outline = root.AddComponent<Outline>(); outline.effectColor = PlayerStatusView.Gold; outline.effectDistance = new Vector2(1, -1); }
    private static T[] Components<T>(Scene scene) where T : Component => scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<T>(true)).ToArray();
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
