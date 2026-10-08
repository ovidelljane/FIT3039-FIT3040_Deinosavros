using System;
using System.IO;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public static class CombatAttackCountdownAuthoring
{
    private const string ScenePath = "Assets/Scenes/Deinosavros.unity";
    public const string Results = "Library/AttackCountdown";

    [MenuItem("Tools/Battle/Author Attack Countdowns")]
    public static void Apply()
    {
        Require(!EditorApplication.isPlayingOrWillChangePlaymode, "Stop Play Mode before authoring attack countdowns.");
        for (int i = 0; i < SceneManager.sceneCount; i++)
            Require(!SceneManager.GetSceneAt(i).isDirty, "Save your scene changes before authoring attack countdowns.");
        var setup = EditorSceneManager.GetSceneManagerSetup(); Directory.CreateDirectory(Results);
        try
        {
            var scene = EditorSceneManager.OpenScene(ScenePath);
            var hud = Components<BattleHud>(scene).Single();
            var clock = Components<TimeTickSystem>(scene).Single();
            var fighters = Components<BattleScript>(scene).Where(a => a.isActiveAndEnabled).ToArray();
            Require(fighters.Length == 4, "Keep the existing one-player, three-enemy lineup.");
            if (!File.Exists(Results + "/Combat-before-attack-countdowns.unity")) File.Copy(ScenePath, Results + "/Combat-before-attack-countdowns.unity");
            bool changed = false;
            foreach (var actor in fighters)
            {
                var existing = Components<AttackCountdownView>(scene).Where(v => v.Actor == actor).ToArray();
                Require(existing.Length <= 1, "Duplicate attack countdowns: " + actor.name);
                if (existing.Length == 1)
                {
                    var rect = (RectTransform)existing[0].transform;
                    if (rect.Find("Label") != null || rect.Find("Ring") != null)
                    { BuildLine(existing[0], actor.CompareTag("Player")); changed = true; }
                    var number = (RectTransform)rect.Find("Seconds");
                    if (number != null && number.sizeDelta.y == 28)
                    { number.sizeDelta = new Vector2(number.sizeDelta.x, 36); changed = true; }
                    continue;
                }
                RectTransform parent;
                bool player = actor.CompareTag("Player");
                if (player) parent = (RectTransform)hud.Status.transform;
                else
                {
                    using var label = new SerializedObject(actor.GetComponent<HealthLabel>());
                    parent = (RectTransform)label.FindProperty("enemyPanel").objectReferenceValue;
                }
                Require(parent != null && parent.Find("AttackTimer") == null, "Repair the partial AttackTimer hierarchy before continuing.");
                Build(parent, actor, clock, hud, player); changed = true;
            }
            if (changed) { EditorSceneManager.MarkSceneDirty(scene); Require(EditorSceneManager.SaveScene(scene), "Could not save attack countdowns."); }
            Validate(EditorSceneManager.OpenScene(ScenePath));
            File.WriteAllText(Results + "/authoring.txt", "PASS: four scene-owned attack countdowns saved and reopened.\n");
        }
        finally
        {
            if (setup.Any(s => s.isLoaded)) EditorSceneManager.RestoreSceneManagerSetup(setup);
            else EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        }
    }

    public static void ApplyAndVerify()
    {
        Apply(); string snapshot = File.ReadAllText(ScenePath); Apply();
        Require(snapshot == File.ReadAllText(ScenePath), "Repeated application must preserve the saved scene.");
        File.AppendAllText(Results + "/authoring.txt", "PASS: repeated authoring preserves exact scene bytes.\n");
        RunIntegrationPlayVerification.RunAttackCountdowns();
    }

    public static void Validate(Scene scene)
    {
        var views = Components<AttackCountdownView>(scene);
        Require(views.Length == 4 && views.Select(v => v.Actor).Distinct().Count() == 4 && views.All(v => v.IsReady),
            "Every active fighter needs exactly one authored countdown.");
        foreach (var view in views)
        {
            foreach (var graphic in view.GetComponentsInChildren<Graphic>(true))
                Require(!graphic.raycastTarget, "Attack countdowns cannot block card targeting.");
            foreach (var text in view.GetComponentsInChildren<TMP_Text>(true))
                Require(AssetDatabase.Contains(text.font), "Save the countdown font assets.");
        }
    }

    private static void Build(RectTransform parent, BattleScript actor, TimeTickSystem clock, BattleHud hud, bool player)
    {
        var root = PlayerStatusView.Rect("AttackTimer", parent);
        root.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
        var visibility = root.gameObject.AddComponent<CanvasGroup>(); visibility.blocksRaycasts = visibility.interactable = false;
        var view = root.gameObject.AddComponent<AttackCountdownView>();
        using var data = new SerializedObject(view);
        data.FindProperty("actor").objectReferenceValue = actor; data.FindProperty("clock").objectReferenceValue = clock;
        data.FindProperty("hud").objectReferenceValue = hud; data.FindProperty("visibility").objectReferenceValue = visibility;
        data.ApplyModifiedPropertiesWithoutUndo();
        BuildLine(view, player);
    }

    private static void BuildLine(AttackCountdownView view, bool player)
    {
        var root = (RectTransform)view.transform;
        // Replace only this countdown's superseded decorative visuals.
        foreach (var name in new[] { "Icon", "Label", "Seconds", "Track", "Ring", "Back" })
        { var child = root.Find(name); if (child != null) UnityEngine.Object.DestroyImmediate(child.gameObject); }
        if (root.TryGetComponent<Outline>(out var outline)) UnityEngine.Object.DestroyImmediate(outline);
        if (root.TryGetComponent<Image>(out var image)) UnityEngine.Object.DestroyImmediate(image);
        if (player) PlayerStatusView.Place(root, Vector2.one, new Vector2(18, -4), new Vector2(180, 24), new Vector2(0, 1));
        else PlayerStatusView.Place(root, Vector2.zero, new Vector2(0, -4), new Vector2(144, 20), new Vector2(0, 1));
        var icon = PlayerStatusView.Icon(root, StatusSymbol.Damage, new Color(.9f, .68f, .34f));
        PlayerStatusView.Place(icon.rectTransform, new Vector2(0, .5f), new Vector2(8, 0), new Vector2(16, 16));
        var track = PlayerStatusView.Box("Track", root, PlayerStatusView.Ink);
        track.rectTransform.anchorMin = new Vector2(0, .5f); track.rectTransform.anchorMax = new Vector2(1, .5f);
        track.rectTransform.offsetMin = new Vector2(23, -2); track.rectTransform.offsetMax = new Vector2(-50, 2);
        var fill = PlayerStatusView.Box("Fill", track.transform, new Color(.9f, .68f, .34f));
        fill.rectTransform.anchorMin = Vector2.zero; fill.rectTransform.anchorMax = Vector2.one;
        fill.rectTransform.offsetMin = fill.rectTransform.offsetMax = Vector2.zero;
        var seconds = Text(root, "Seconds", view.Actor.AttackIntervalSeconds.ToString("0.0") + "s", player ? 22 : 20, false);
        seconds.fontStyle = FontStyles.Bold; seconds.alignment = TextAlignmentOptions.Right;
        PlayerStatusView.Place(seconds.rectTransform, new Vector2(1, .5f), Vector2.zero, new Vector2(46, 36), new Vector2(1, .5f));
        var edge = seconds.gameObject.AddComponent<Outline>(); edge.effectColor = PlayerStatusView.Ink; edge.effectDistance = new Vector2(1, -1);
        using var data = new SerializedObject(view);
        data.FindProperty("fill").objectReferenceValue = fill; data.FindProperty("seconds").objectReferenceValue = seconds;
        data.ApplyModifiedPropertiesWithoutUndo();
    }

    private static TMP_Text Text(Transform parent, string name, string value, float size, bool heading)
    {
        var text = PlayerStatusView.Rect(name, parent).gameObject.AddComponent<TextMeshProUGUI>();
        text.font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(heading ? "Assets/Resources/Fonts/Cinzel-Black SDF.asset" : "Assets/Resources/Fonts/Alegreya SDF.asset");
        text.text = value; text.fontSize = size; text.color = PlayerStatusView.Cream; text.raycastTarget = false;
        text.textWrappingMode = TextWrappingModes.NoWrap; return text;
    }
    private static T[] Components<T>(Scene scene) where T : Component => scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<T>(true)).ToArray();
    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
}
