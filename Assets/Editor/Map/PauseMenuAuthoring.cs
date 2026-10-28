using System;
using System.IO;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

// Explicit scene authoring only. Reapplying never replaces an existing menu or its layout.
public static class PauseMenuAuthoring
{
    public const string PrefabPath = "Assets/Prefabs/UI/PauseMenu.prefab";
    private static readonly Color Gold = new(.70f, .54f, .31f);
    private static readonly Color Brown = new(.17f, .14f, .12f);
    private static readonly Color Cream = new(.96f, .90f, .78f);

    public static void ApplyAndTest()
    {
        Apply();
        var paths = new[] { "Map", "MapOpportunity", "MapRecovery" }.Select(n => "Assets/Scenes/" + n + ".unity").ToArray();
        var saved = paths.Select(File.ReadAllText).ToArray();
        Apply();
        for (int i = 0; i < paths.Length; i++) Require(File.ReadAllText(paths[i]) == saved[i], "Reapplying must preserve the saved scene: " + paths[i]);
        RunIntegrationPlayVerification.RunPauseMenu();
    }

    [MenuItem("Tools/UI/Author Pause Menus")]
    public static void Apply()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Stop Play Mode before authoring pause menus.");
        for (int i = 0; i < SceneManager.sceneCount; i++)
            if (SceneManager.GetSceneAt(i).isDirty) throw new InvalidOperationException("Save scene changes before authoring pause menus.");
        Directory.CreateDirectory("Library/PauseMenu");
        EnsurePrefab();
        var setup = EditorSceneManager.GetSceneManagerSetup();
        try
        {
            foreach (string name in new[] { "Map", "MapOpportunity", "MapRecovery" })
            {
                string path = "Assets/Scenes/" + name + ".unity";
                var scene = EditorSceneManager.OpenScene(path);
                if (!Components<PauseMenu>(scene).Any())
                {
                    string backup = "Library/PauseMenu/" + name + "-before.unity";
                    if (!File.Exists(backup)) File.Copy(path, backup);
                    var canvas = Components<Canvas>(scene).Single(c => c.isRootCanvas);
                    var instance = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath), scene);
                    instance.name = "Pause";
                    instance.transform.SetParent(canvas.transform, false);
                    var rect = (RectTransform)instance.transform;
                    rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
                    rect.offsetMin = rect.offsetMax = Vector2.zero;
                    if (name == "Map")
                    {
                        ((RectTransform)instance.transform.Find("Open")).anchoredPosition = new Vector2(-24, -76);
                        PrefabUtility.RecordPrefabInstancePropertyModifications(instance.transform.Find("Open"));
                    }
                    EditorSceneManager.MarkSceneDirty(scene);
                    if (!EditorSceneManager.SaveScene(scene)) throw new InvalidOperationException("Could not save " + name);
                }
                Verify(scene);
            }
            foreach (string name in new[] { "MainMenu", "Deinosavros" })
            {
                var scene = EditorSceneManager.OpenScene("Assets/Scenes/" + name + ".unity");
                Require(!Components<PauseMenu>(scene).Any(), name + " must not install a pause menu.");
            }
            File.WriteAllText("Library/PauseMenu/authoring.txt", "PASS: three saved scene menus, persistent button events, inactive overlays, original Canvas and EventSystem, no menu in Combat or MainMenu.\n");
        }
        finally
        {
            if (setup.Any(s => s.isLoaded)) EditorSceneManager.RestoreSceneManagerSetup(setup);
            else EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        }
    }

    private static void EnsurePrefab()
    {
        if (AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath) != null) return;
        var root = Rect("Pause", null, Vector2.zero, Vector2.zero);
        Stretch(root);
        try
        {
            var controller = root.gameObject.AddComponent<PauseMenu>();
            var open = Button("Open", root, "", new Vector2(56, 52));
            var openRect = (RectTransform)open.transform;
            openRect.anchorMin = openRect.anchorMax = openRect.pivot = Vector2.one;
            openRect.anchoredPosition = new Vector2(-24, -24);
            foreach (float x in new[] { -7f, 7f }) Image("Bar", open.transform, new Vector2(x, 0), new Vector2(6, 22), Cream);
            var overlay = Rect("Overlay", root, Vector2.zero, Vector2.zero); Stretch(overlay);
            var shade = Image("Shade", overlay, Vector2.zero, Vector2.zero, new Color(.035f, .025f, .02f, .72f));
            Stretch(shade.rectTransform); shade.raycastTarget = true;
            var menu = Panel("Menu", overlay);
            Text("Title", menu, "PAUSED", new Vector2(0, 163), new Vector2(440, 56), 38, true);
            Image("Rule", menu, new Vector2(0, 112), new Vector2(304, 2), Gold);
            var actions = Rect("Actions", menu, new Vector2(0, -16), new Vector2(340, 226));
            var layout = actions.gameObject.AddComponent<UnityEngine.UI.VerticalLayoutGroup>();
            layout.spacing = 18; layout.childAlignment = TextAnchor.MiddleCenter;
            layout.childControlWidth = layout.childControlHeight = layout.childForceExpandWidth = layout.childForceExpandHeight = false;
            var resume = Button("Resume", actions, "Resume", new Vector2(340, 62));
            var main = Button("MainMenu", actions, "Main Menu", new Vector2(340, 62));
            var quit = Button("Quit", actions, "Quit Game", new Vector2(340, 62));
            Text("Hint", menu, "Esc to resume", new Vector2(0, -197), new Vector2(350, 28), 20, false).color = new Color(.72f, .67f, .58f);
            var confirm = Panel("Confirm", overlay);
            var title = Text("Title", confirm, "LEAVE THIS RUN?", new Vector2(0, 125), new Vector2(460, 60), 29, true);
            var body = Text("Body", confirm, "Current run progress will be lost.", new Vector2(0, 52), new Vector2(440, 64), 26, false);
            var leave = Button("Leave", confirm, "Main Menu", new Vector2(340, 62));
            ((RectTransform)leave.transform).anchoredPosition = new Vector2(0, -43);
            var back = Button("Back", confirm, "Back", new Vector2(340, 62));
            ((RectTransform)back.transform).anchoredPosition = new Vector2(0, -123);
            Navigation(resume, quit, main); Navigation(main, resume, quit); Navigation(quit, main, resume);
            Navigation(leave, back, back); Navigation(back, leave, leave);
            UnityEventTools.AddPersistentListener(open.onClick, controller.Open);
            UnityEventTools.AddPersistentListener(resume.onClick, controller.Resume);
            UnityEventTools.AddPersistentListener(main.onClick, controller.RequestMainMenu);
            UnityEventTools.AddPersistentListener(quit.onClick, controller.RequestQuit);
            UnityEventTools.AddPersistentListener(back.onClick, controller.Back);
            UnityEventTools.AddPersistentListener(leave.onClick, controller.ConfirmLeave);
            using (var data = new SerializedObject(controller))
            {
                Bind(data, "overlay", overlay.gameObject); Bind(data, "menu", menu.gameObject); Bind(data, "confirmation", confirm.gameObject);
                Bind(data, "openButton", open); Bind(data, "resumeButton", resume); Bind(data, "backButton", back); Bind(data, "leaveButton", leave);
                Bind(data, "confirmationTitle", title); Bind(data, "confirmationBody", body); Bind(data, "leaveLabel", leave.GetComponentInChildren<TMP_Text>());
                data.ApplyModifiedPropertiesWithoutUndo();
            }
            confirm.gameObject.SetActive(false); overlay.gameObject.SetActive(false);
            PrefabUtility.SaveAsPrefabAsset(root.gameObject, PrefabPath);
        }
        finally { Object.DestroyImmediate(root.gameObject); }
    }

    public static void Verify(Scene scene)
    {
        var controller = Components<PauseMenu>(scene).Single();
        Require(controller.name == "Pause" && controller.transform.parent.GetComponent<Canvas>() != null, "Pause is an editable child of the existing Canvas.");
        Require(Components<Canvas>(scene).Count(c => c.isRootCanvas) == 1 && Components<UnityEngine.EventSystems.EventSystem>(scene).Count() == 1,
            "Pause reuses the existing Canvas and EventSystem.");
        using var data = new SerializedObject(controller);
        foreach (string field in new[] { "overlay", "menu", "confirmation", "openButton", "resumeButton", "backButton", "leaveButton", "confirmationTitle", "confirmationBody", "leaveLabel" })
            Require(data.FindProperty(field).objectReferenceValue != null, "Missing saved pause binding: " + field);
        Require(!((GameObject)data.FindProperty("overlay").objectReferenceValue).activeSelf, "Pause overlay is saved closed.");
        foreach (var button in controller.GetComponentsInChildren<UnityEngine.UI.Button>(true))
            Require(button.onClick.GetPersistentEventCount() == 1 && button.onClick.GetPersistentTarget(0) == controller,
                "The button has an Inspector-editable persistent event: " + button.name);
    }

    private static RectTransform Panel(string name, Transform parent)
    {
        var border = Image(name, parent, Vector2.zero, new Vector2(520, 500), Gold);
        border.raycastTarget = true;
        var fill = Image("Fill", border.transform, Vector2.zero, Vector2.zero, Brown);
        Stretch(fill.rectTransform); fill.rectTransform.offsetMin = Vector2.one * 2; fill.rectTransform.offsetMax = -Vector2.one * 2;
        return border.rectTransform;
    }
    private static UnityEngine.UI.Button Button(string name, Transform parent, string label, Vector2 size)
    {
        var border = Image(name, parent, Vector2.zero, size, Gold); border.raycastTarget = true;
        var fill = Image("Fill", border.transform, Vector2.zero, Vector2.zero, new Color(.25f, .20f, .16f));
        Stretch(fill.rectTransform); fill.rectTransform.offsetMin = Vector2.one * 2; fill.rectTransform.offsetMax = -Vector2.one * 2;
        var button = border.gameObject.AddComponent<UnityEngine.UI.Button>(); button.targetGraphic = fill;
        var colors = button.colors; colors.normalColor = Color.white; colors.highlightedColor = new Color(1.4f, 1.3f, 1.1f);
        colors.selectedColor = colors.highlightedColor; colors.pressedColor = new Color(.72f, .64f, .51f); colors.disabledColor = new Color(.45f, .45f, .45f); colors.fadeDuration = .1f;
        button.colors = colors;
        Text("Label", border.transform, label, Vector2.zero, size - new Vector2(24, 8), 28, true);
        return button;
    }
    private static void Navigation(UnityEngine.UI.Button button, UnityEngine.UI.Button up, UnityEngine.UI.Button down)
    {
        button.navigation = new UnityEngine.UI.Navigation { mode = UnityEngine.UI.Navigation.Mode.Explicit, selectOnUp = up, selectOnDown = down };
    }
    private static TMP_Text Text(string name, Transform parent, string value, Vector2 position, Vector2 size, float fontSize, bool heading)
    {
        var rect = Rect(name, parent, position, size); var text = rect.gameObject.AddComponent<TextMeshProUGUI>();
        text.font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(heading ? "Assets/Resources/Fonts/Cinzel-Black SDF.asset" : "Assets/Resources/Fonts/Alegreya SDF.asset");
        text.text = value; text.fontSize = fontSize; text.color = Cream; text.alignment = TextAlignmentOptions.Center;
        text.textWrappingMode = TextWrappingModes.Normal; text.raycastTarget = false;
        return text;
    }
    private static UnityEngine.UI.Image Image(string name, Transform parent, Vector2 position, Vector2 size, Color color)
    {
        var rect = Rect(name, parent, position, size); var image = rect.gameObject.AddComponent<UnityEngine.UI.Image>();
        image.color = color; image.raycastTarget = false; return image;
    }
    private static RectTransform Rect(string name, Transform parent, Vector2 position, Vector2 size)
    {
        var rect = (RectTransform)new GameObject(name, typeof(RectTransform)).transform;
        rect.gameObject.layer = 5; rect.SetParent(parent, false); rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(.5f, .5f);
        rect.anchoredPosition = position; rect.sizeDelta = size; return rect;
    }
    private static void Stretch(RectTransform rect)
    { rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.offsetMin = rect.offsetMax = Vector2.zero; }
    private static void Bind(SerializedObject data, string name, Object value) => data.FindProperty(name).objectReferenceValue = value;
    private static System.Collections.Generic.IEnumerable<T> Components<T>(Scene scene) where T : Component =>
        scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<T>(true));
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
