using System;
using System.IO;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// Creates the Map status panel once; later applications preserve scene-authored styling.
public static class MapStatusSceneAuthoring
{
    private const string ScenePath = "Assets/Scenes/Map.unity";
    public const string Results = "Library/MapStatus";

    [MenuItem("Tools/Map/Author Status Panel")]
    public static void Apply()
    {
        Require(!EditorApplication.isPlayingOrWillChangePlaymode, "Stop Play Mode before authoring Map status.");
        for (int i = 0; i < SceneManager.sceneCount; i++)
            Require(!SceneManager.GetSceneAt(i).isDirty, "Save your scene changes before authoring Map status.");
        Directory.CreateDirectory(Results);
        var setup = EditorSceneManager.GetSceneManagerSetup();
        try
        {
            var scene = EditorSceneManager.OpenScene(ScenePath);
            var controller = Components<MapController>(scene).Single();
            using var data = new SerializedObject(controller);
            var deck = (Transform)data.FindProperty("deckContainer").objectReferenceValue;
            Require(deck != null && deck.parent != null, "The current Map card bar must remain intact.");
            var panels = Components<MapPlayerStatusPanel>(scene);
            Require(panels.Length <= 1, "Repair duplicate Map panels before authoring.");
            var panel = panels.SingleOrDefault();
            bool changed = false;
            if (panel == null)
            {
                Require(!Components<PlayerStatusView>(scene).Any(), "Do not replace a partially authored status panel.");
                if (!File.Exists(Results + "/Map-before-status.unity")) File.Copy(ScenePath, Results + "/Map-before-status.unity");
                panel = MapPlayerStatusPanel.Create(deck.parent); panel.name = "Status";
                panel.View.name = "Player";
                Configure(panel.View);
                panel.View.Bind(Components<RunSession>(scene).Single());
                changed = true;
            }
            if (data.FindProperty("playerStatusPanel").objectReferenceValue != panel)
            {
                data.FindProperty("playerStatusPanel").objectReferenceValue = panel;
                data.ApplyModifiedPropertiesWithoutUndo(); changed = true;
            }
            if (changed)
            {
                Canvas.ForceUpdateCanvases(); EditorSceneManager.MarkSceneDirty(scene);
                Require(EditorSceneManager.SaveScene(scene), "Could not save Map status.");
            }
            scene = EditorSceneManager.OpenScene(ScenePath); Validate(scene);
            File.WriteAllText(Results + "/authoring.txt", "PASS: MapCanvas/CardBar/Status saved and reopened with one player view, two bars and five status values.\n");
        }
        finally
        {
            if (setup.Any(s => s.isLoaded)) EditorSceneManager.RestoreSceneManagerSetup(setup);
            else EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        }
    }

    public static void ApplyAndVerify()
    {
        Apply(); string first = File.ReadAllText(ScenePath); Apply(); Apply();
        Require(first == File.ReadAllText(ScenePath), "Repeated authoring changed the status hierarchy.");
        File.AppendAllText(Results + "/authoring.txt", "PASS: two repeated applications preserve exact scene bytes.\n");
        RunIntegrationPlayVerification.RunMapStatus();
    }

    [MenuItem("Tools/Map/Select Status Panel")]
    public static void Show()
    {
        Require(!EditorApplication.isPlayingOrWillChangePlaymode, "Stop Play Mode before opening Map.");
        for (int i = 0; i < SceneManager.sceneCount; i++)
            Require(!SceneManager.GetSceneAt(i).isDirty, "Save your scene changes before opening Map.");
        var scene = EditorSceneManager.OpenScene(ScenePath); Validate(scene);
        var panel = Components<MapPlayerStatusPanel>(scene).Single();
        Selection.activeGameObject = panel.gameObject; EditorGUIUtility.PingObject(panel.gameObject);
    }

    public static void Validate(Scene scene)
    {
        var panel = Components<MapPlayerStatusPanel>(scene).Single();
        Require(panel.View != null && panel.View.IsReady, "All Map status references must be serialized.");
        Require(Components<PlayerStatusView>(scene).Length == 1 && Components<ResourceBarView>(scene).Length == 2,
            "The Map has exactly one player view with two resource bars.");
        using var data = new SerializedObject(Components<MapController>(scene).Single());
        Require(data.FindProperty("playerStatusPanel").objectReferenceValue == panel, "The controller must reference its saved panel.");
        foreach (var text in panel.GetComponentsInChildren<TMP_Text>(true))
            Require(text.font != null && AssetDatabase.Contains(text.font) && text.fontSharedMaterial != null &&
                AssetDatabase.Contains(text.fontSharedMaterial), "Save typography assets for " + text.name);
        foreach (var graphic in panel.GetComponentsInChildren<Graphic>(true))
            Require(!graphic.raycastTarget, "Status decorations must not intercept input.");
        foreach (var item in panel.GetComponentsInChildren<Transform>(true))
            Require(GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(item.gameObject) == 0, "Missing status script: " + item.name);
    }

    private static void Configure(PlayerStatusView view)
    {
        var heading = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/Resources/Fonts/Cinzel-Black SDF.asset");
        var body = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/Resources/Fonts/Alegreya SDF.asset");
        Require(heading != null && body != null, "The existing saved font assets are required.");
        var attributes = view.transform.Find("Attributes");
        attributes.GetChild(0).name = "Shield"; attributes.GetChild(1).name = "Speed"; attributes.GetChild(2).name = "Damage";
        view.Health.transform.Find("Label").GetComponent<TMP_Text>().text = "HP";
        var speedLabel = attributes.Find("Speed/Label").GetComponent<TMP_Text>();
        speedLabel.text = "INTERVAL";
        foreach (var label in attributes.GetComponentsInChildren<TMP_Text>(true).Where(t => t.name == "Label"))
            label.rectTransform.sizeDelta = new Vector2(80, label.rectTransform.sizeDelta.y);
        using (var data = new SerializedObject(view))
        {
            data.FindProperty("displayAttackInterval").boolValue = true;
            data.FindProperty("showAttackSpeedUnit").boolValue = true;
            data.ApplyModifiedPropertiesWithoutUndo();
        }
        foreach (var text in view.GetComponentsInChildren<TMP_Text>(true))
            text.font = text.name == "Title" || text.name == "Label" ? heading : body;
    }

    private static T[] Components<T>(Scene scene) where T : Component => scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<T>(true)).ToArray();
    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
}
