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

// The editor creates these objects once. Runtime code only binds actors and updates values.
public static class CombatStatusSceneAuthoring
{
    private const string ScenePath = "Assets/Scenes/Deinosavros.unity";

    [MenuItem("Tools/Battle/Author Combat Status UI")]
    public static void Apply()
    {
        Require(!EditorApplication.isPlayingOrWillChangePlaymode, "Stop Play Mode before authoring status UI.");
        for (int i = 0; i < SceneManager.sceneCount; i++)
            Require(!SceneManager.GetSceneAt(i).isDirty, "Save your scene changes before authoring status UI.");
        var setup = EditorSceneManager.GetSceneManagerSetup();
        Directory.CreateDirectory(RunIntegrationSetup.ResultDirectory);
        try
        {
            var scene = EditorSceneManager.OpenScene(ScenePath);
            var hud = Components<BattleHud>(scene).Single();
            if (hud.StatusRoot == null)
            {
                Require(hud.Canvas != null && hud.Canvas.transform.Find("Status") == null &&
                    !Components<PlayerStatusView>(scene).Any(), "Repair a partial status hierarchy instead of replacing it.");
                string backup = RunIntegrationSetup.ResultDirectory + "/Combat-before-status-hierarchy.unity";
                if (!File.Exists(backup)) File.Copy(ScenePath, backup);
                Build(hud, scene);
                EditorSceneManager.MarkSceneDirty(scene);
                Require(EditorSceneManager.SaveScene(scene), "Could not save the combat status hierarchy.");
            }
            scene = EditorSceneManager.OpenScene(ScenePath);
            Validate(scene);
            var status = Components<BattleHud>(scene).Single().StatusRoot;
            File.WriteAllText(RunIntegrationSetup.ResultDirectory + "/combat-status-hierarchy.txt",
                $"PASS: saved and reopened {status.GetComponentsInChildren<RectTransform>(true).Length} status UI objects.\n" +
                "One player panel, three enemy panels and the existing player label use scene references.\n" +
                "Repeated authoring preserves existing objects, bindings and layout.\n");
        }
        finally
        {
            if (setup.Any(entry => entry.isLoaded && entry.isActive)) EditorSceneManager.RestoreSceneManagerSetup(setup);
            else EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        }
    }

    public static void Validate(Scene scene)
    {
        var hud = Components<BattleHud>(scene).Single();
        Require(hud.StatusRoot != null && hud.Status != null && hud.Status.IsReady &&
            hud.Status.transform.IsChildOf(hud.StatusRoot), "The scene must bind a complete player status panel.");
        Require(hud.StatusRoot.GetComponentsInChildren<PlayerStatusView>(true).Length == 1 &&
            hud.StatusRoot.GetComponentsInChildren<ResourceBarView>(true).Length == 2,
            "Exactly one player panel owns the Health and Elixir bars.");
        var labels = Components<HealthLabel>(scene).Where(label => label.enabled &&
            label.TryGetComponent<BattleScript>(out var actor) && actor.enabled).ToArray();
        Require(labels.Length == 4 && labels.Count(label => label.CompareTag("Enemy")) == 3,
            "Keep the authored one-player, three-enemy lineup.");
        foreach (var label in labels)
        {
            Require(label.HasSceneBindings, "Missing status references: " + label.name);
            using var data = new SerializedObject(label);
            var text = (TMP_Text)data.FindProperty("label").objectReferenceValue;
            Require(text.transform.IsChildOf(hud.StatusRoot) && text.canvas == hud.Canvas,
                "Every actor label belongs to the combat status hierarchy.");
        }
        foreach (var graphic in hud.StatusRoot.GetComponentsInChildren<Graphic>(true))
            Require(!graphic.raycastTarget, "Status graphics cannot intercept card input.");
        foreach (var text in hud.StatusRoot.GetComponentsInChildren<TMP_Text>(true))
            Require(text.font != null && AssetDatabase.Contains(text.font) && text.fontSharedMaterial != null &&
                AssetDatabase.Contains(text.fontSharedMaterial), "Status typography needs saved assets: " + text.name);
        foreach (var item in hud.StatusRoot.GetComponentsInChildren<Transform>(true))
            Require(GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(item.gameObject) == 0, "Missing script: " + item.name);
    }

    private static void Build(BattleHud hud, Scene scene)
    {
        var heading = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/Resources/Fonts/Cinzel-Black SDF.asset");
        var body = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/Resources/Fonts/Alegreya SDF.asset");
        Require(heading != null && body != null, "Import the existing scene font assets before authoring status UI.");
        var root = PlayerStatusView.Rect("Status", hud.Canvas.transform); Stretch(root);
        root.SetAsFirstSibling();
        var canvasGroup = root.gameObject.AddComponent<CanvasGroup>();
        canvasGroup.interactable = canvasGroup.blocksRaycasts = false;
        var view = PlayerStatusView.Create(root, false); view.name = "Player";
        PlayerStatusView.Place((RectTransform)view.transform, new Vector2(0, 1), new Vector2(32, -32),
            new Vector2(380, 250), new Vector2(0, 1));
        var attributes = view.transform.Find("Attributes");
        attributes.GetChild(0).name = "Shield"; attributes.GetChild(1).name = "Speed"; attributes.GetChild(2).name = "Damage";
        view.Bind(BattleHud.FindPlayer(scene));
        using (var data = new SerializedObject(hud))
        {
            Bind(data, "statusRoot", root); Bind(data, "status", view); data.ApplyModifiedPropertiesWithoutUndo();
        }
        var enemies = PlayerStatusView.Rect("Enemies", root); Stretch(enemies);
        var labels = Components<HealthLabel>(scene).Where(label => label.enabled &&
            label.TryGetComponent<BattleScript>(out var actor) && actor.enabled)
            .OrderBy(label => label.transform.position.x).ThenBy(label => label.name).ToArray();
        int enemyIndex = 0;
        foreach (var label in labels)
        {
            using var data = new SerializedObject(label);
            Bind(data, "battleCanvas", hud.Canvas);
            var actor = label.GetComponent<BattleScript>();
            string display = data.FindProperty("displayName").stringValue;
            RectTransform positionRoot;
            if (label.CompareTag("Enemy"))
            {
                var panel = PlayerStatusView.Rect($"E{++enemyIndex:00}", enemies); positionRoot = panel;
                panel.pivot = new Vector2(.5f, 0); panel.sizeDelta = new Vector2(144, 54);
                var background = panel.gameObject.AddComponent<Image>();
                background.color = PlayerStatusView.Ink; background.raycastTarget = false;
                Border(panel);
                var visibility = panel.gameObject.AddComponent<CanvasGroup>();
                visibility.interactable = visibility.blocksRaycasts = false;
                var title = PlayerStatusView.Text(panel, "Name", display, 15, GameFontRole.Heading);
                title.alignment = TextAlignmentOptions.Left; title.enableAutoSizing = true;
                title.fontSizeMin = 11; title.fontSizeMax = 15;
                PlayerStatusView.Place(title.rectTransform, new Vector2(0, 1), new Vector2(8, -14), new Vector2(78, 22), new Vector2(0, .5f));
                var icon = PlayerStatusView.Icon(panel, StatusSymbol.Damage, new Color(.88f, .52f, .32f));
                PlayerStatusView.Place(icon.rectTransform, Vector2.one, new Vector2(-44, -14), new Vector2(20, 20));
                var attack = PlayerStatusView.Text(panel, "Attack", actor.attackDmg.ToString(), 22, GameFontRole.Numeric);
                PlayerStatusView.Place(attack.rectTransform, Vector2.one, new Vector2(-19, -14), new Vector2(30, 26));
                var track = PlayerStatusView.Box("Health", panel, new Color(.08f, .065f, .06f));
                PlayerStatusView.Place(track.rectTransform, new Vector2(.5f, 0), new Vector2(0, 8), new Vector2(124, 16), new Vector2(.5f, 0));
                Border(track.rectTransform);
                var fill = PlayerStatusView.Box("Fill", track.transform, new Color(.68f, .21f, .19f)); Stretch(fill.rectTransform);
                fill.rectTransform.anchorMax = new Vector2(actor.maxHealth > 0 ? Mathf.Clamp01((float)actor.health / actor.maxHealth) : 0, 1);
                var number = PlayerStatusView.Text(track.transform, "Value", $"{actor.health} / {actor.maxHealth}", 18, GameFontRole.Numeric);
                PlayerStatusView.Place(number.rectTransform, new Vector2(.5f, .5f), Vector2.zero, new Vector2(124, 22));
                Bind(data, "enemyPanel", panel); Bind(data, "enemyFill", fill); Bind(data, "label", number);
                Bind(data, "attackText", attack); Bind(data, "visibility", visibility);
            }
            else
            {
                var number = PlayerStatusView.Text(root, "PlayerLabel", $"{display}: {actor.health}/{actor.maxHealth}",
                    data.FindProperty("fontSize").floatValue, GameFontRole.Numeric);
                number.alignment = TextAlignmentOptions.Bottom;
                positionRoot = number.rectTransform; positionRoot.pivot = new Vector2(.5f, 0);
                positionRoot.sizeDelta = new Vector2(300, 60); Bind(data, "label", number);
            }
            var camera = Components<Camera>(scene).Single(candidate => candidate.CompareTag("MainCamera"));
            var viewport = camera.WorldToViewportPoint(label.transform.position + Vector3.up * data.FindProperty("yOffset").floatValue);
            positionRoot.anchorMin = positionRoot.anchorMax = new Vector2(viewport.x, viewport.y);
            positionRoot.anchoredPosition = Vector2.zero;
            positionRoot.gameObject.SetActive(label.isActiveAndEnabled);
            data.ApplyModifiedPropertiesWithoutUndo();
        }
        foreach (var text in root.GetComponentsInChildren<TMP_Text>(true))
            text.font = text.name == "Title" || text.name == "Label" || text.name == "Name" ? heading : body;
        Canvas.ForceUpdateCanvases();
    }

    private static void Stretch(RectTransform rect)
    { rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.offsetMin = rect.offsetMax = Vector2.zero; }
    private static void Border(RectTransform rect)
    { var edge = rect.gameObject.AddComponent<Outline>(); edge.effectColor = PlayerStatusView.Gold; edge.effectDistance = new Vector2(1, -1); }
    private static void Bind(SerializedObject data, string name, Object value) => data.FindProperty(name).objectReferenceValue = value;
    private static T[] Components<T>(Scene scene) where T : Component => scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<T>(true)).ToArray();
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
