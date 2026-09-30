using System;
using System.IO;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

// One-time migration. Existing authored pages are validated, never regenerated.
public static class MapEncounterSceneAuthoring
{
    private static readonly Color Ink = new Color(.1f, .073f, .053f);
    private static readonly Color Gold = new Color(.82f, .63f, .34f);
    private static readonly Color Cream = new Color(1f, .9f, .72f);
    private static readonly Color Green = new Color(.48f, .69f, .43f);
    private static readonly Color Muted = new Color(.77f, .73f, .64f);

    [MenuItem("Tools/Map/Author Encounter Scene UI")]
    public static void Apply()
    {
        Require(!EditorApplication.isPlayingOrWillChangePlaymode, "Stop Play Mode before authoring scene UI.");
        for (int i = 0; i < SceneManager.sceneCount; i++)
            Require(!SceneManager.GetSceneAt(i).isDirty, "Save your scene changes before authoring scene UI.");
        var setup = EditorSceneManager.GetSceneManagerSetup();
        Directory.CreateDirectory(RunIntegrationSetup.ResultDirectory);
        string report = "";
        try
        {
            foreach (string name in new[] { "MapRecovery", "MapOpportunity" })
            {
                string path = "Assets/Scenes/" + name + ".unity";
                var scene = EditorSceneManager.OpenScene(path);
                var controller = Components<MapNonCombatController>(scene).Single();
                bool authored = controller.Panel != null;
                if (!authored)
                {
                    Require(!Components<Canvas>(scene).Any(), "A partial page exists. Repair references instead of replacing your UI.");
                    string backup = RunIntegrationSetup.ResultDirectory + "/" + name + "-before-hierarchy.unity";
                    if (!File.Exists(backup)) File.Copy(path, backup);
                    Build(controller);
                    EditorSceneManager.MarkSceneDirty(scene);
                    Require(EditorSceneManager.SaveScene(scene), "Could not save " + name);
                }
                scene = EditorSceneManager.OpenScene(path);
                Validate(scene);
                report += $"PASS: {name}: {Components<RectTransform>(scene).Count()} saved UI objects; " +
                    (authored ? "existing layout preserved." : "authored and reopened.") + "\n";
            }
            File.WriteAllText(RunIntegrationSetup.ResultDirectory + "/encounter-hierarchy.txt", report);
        }
        finally
        {
            if (setup.Any(entry => entry.isLoaded && entry.isActive)) EditorSceneManager.RestoreSceneManagerSetup(setup);
            else EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        }
    }

    public static void Validate(Scene scene)
    {
        var controller = Components<MapNonCombatController>(scene).Single();
        Require(Components<Canvas>(scene).Count() == 1 && Components<EventSystem>(scene).Count() == 1,
            "Each page needs one authored Canvas and EventSystem.");
        using (var data = new SerializedObject(controller))
        {
            foreach (string name in new[] { "panel", "continueButton", "heading", "body", "value", "detail", "capacity", "continueText", "healthFill" })
                Require(data.FindProperty(name).objectReferenceValue != null, "Missing scene binding: " + name);
            if (controller is MapOpportunityController)
            {
                foreach (string name in new[] { "secondaryButton", "secondaryText", "recoveryDetail", "icon", "revealPose", "rewardRoot",
                    "replacementRoot", "replacementContent", "replacementTemplate", "controls" })
                    Require(data.FindProperty(name).objectReferenceValue != null, "Missing opportunity binding: " + name);
                foreach (string group in new[] { "cardContent", "recoveryContent", "battleContent" })
                    foreach (string member in new[] { "root", "value", "body" })
                        Require(data.FindProperty(group).FindPropertyRelative(member).objectReferenceValue != null, "Missing outcome binding: " + group + "." + member);
                Require(Components<RewardCardView>(scene).Count() == 2, "Offer and replacement template must both exist before Play Mode.");
            }
        }
        foreach (var text in Components<TMP_Text>(scene))
            Require(text.font != null && AssetDatabase.Contains(text.font) && text.fontSharedMaterial != null &&
                AssetDatabase.Contains(text.fontSharedMaterial), "Text needs saved font and material assets: " + text.name);
        foreach (var transform in Components<Transform>(scene))
            Require(GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(transform.gameObject) == 0, "Missing script: " + transform.name);
    }

    private static void Build(MapNonCombatController controller)
    {
        var headingFont = SavedFont("Cinzel-Black");
        var bodyFont = SavedFont("Alegreya");
        bool opportunity = controller is MapOpportunityController;
        var canvasRoot = Rect("UI", controller.transform, Vector2.zero, Vector2.zero);
        var canvas = canvasRoot.gameObject.AddComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        var scaler = canvasRoot.gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080); scaler.matchWidthOrHeight = .5f;
        canvasRoot.gameObject.AddComponent<GraphicRaycaster>();
        var events = new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
        events.transform.SetParent(controller.transform, false);
        events.GetComponent<InputSystemUIInputModule>().AssignDefaultActions();
        Stretch(Box("Background", canvasRoot, Vector2.zero, Vector2.zero, new Color(.075f, .085f, .073f)));
        Box("Band", canvasRoot, Vector2.zero, new Vector2(2400, 480), new Color(.095f, .108f, .09f));
        var panel = Box("Panel", canvasRoot, Vector2.zero, opportunity ? new Vector2(1160, 960) : new Vector2(1000, 760), Ink);
        Border(panel, 2);
        Box("Rule", panel, new Vector2(0, opportunity ? 282 : 170), new Vector2(740, 2), Gold);
        var icon = Rect("Icon", panel, new Vector2(0, opportunity ? 390 : 280), new Vector2(96, 96)).gameObject.AddComponent<RawImage>();
        icon.texture = controller.symbols; icon.raycastTarget = false;
        var tile = MapTravelView.AtlasRect(opportunity ? 10 : 11);
        icon.uvRect = new Rect(tile.x, tile.y, tile.z, tile.w);
        var title = Label("Title", panel, new Vector2(0, opportunity ? 326 : 204), new Vector2(850, 58),
            opportunity ? "OPPORTUNITY" : "RECOVERY", 36, Cream, headingFont);
        var value = Label("Value", panel, new Vector2(0, 100), new Vector2(850, 76),
            opportunity ? "" : "100 <size=55%>/ 100 HP</size>", opportunity ? 27 : 54, opportunity ? Gold : Green, bodyFont);
        var body = Label("Body", panel, new Vector2(0, opportunity ? -220 : -45), new Vector2(840, 92),
            opportunity ? "" : "+15 HP", 27, Cream, bodyFont);
        var detail = Label("Detail", panel, new Vector2(0, opportunity ? -347 : -145),
            opportunity ? new Vector2(950, 60) : new Vector2(830, 90), "", 19, Muted, bodyFont);
        var capacity = Label("Capacity", panel, new Vector2(0, opportunity ? -292 : -230), new Vector2(800, 35),
            "DECK CAPACITY", 18, Gold, bodyFont);
        var actions = Rect("Actions", panel, new Vector2(0, opportunity ? -416 : -305), new Vector2(1000, 64));
        var row = actions.gameObject.AddComponent<HorizontalLayoutGroup>();
        row.childAlignment = TextAnchor.MiddleCenter; row.spacing = 40;
        row.childControlWidth = row.childControlHeight = row.childForceExpandWidth = row.childForceExpandHeight = false;
        Button secondary = null;
        TMP_Text secondaryText = null;
        if (opportunity) secondary = Action("Secondary", actions, "Skip", headingFont, out secondaryText);
        var next = Action("Continue", actions, "Continue", headingFont, out var nextText);
        using var data = new SerializedObject(controller);
        Bind(data, "panel", panel); Bind(data, "continueButton", next); Bind(data, "heading", title);
        Bind(data, "body", body); Bind(data, "value", value); Bind(data, "detail", detail);
        Bind(data, "capacity", capacity); Bind(data, "continueText", nextText);
        if (!opportunity)
        {
            Bind(data, "healthFill", Health(panel)); capacity.gameObject.SetActive(false);
        }
        else
        {
            var chance = (MapOpportunityController)controller;
            var card = Group("Card", panel, "LOST OFFERING", headingFont);
            var heal = Group("Recovery", panel, "HIDDEN SPRING", headingFont);
            var battle = Group("Battle", panel, "AMBUSH", headingFont);
            BindContent(data, "cardContent", card, 27, Gold, -220, "", "", bodyFont);
            BindContent(data, "recoveryContent", heal, 54, Green, -48, "100 <size=55%>/ 100 HP</size>", "+15 HP", bodyFont);
            BindContent(data, "battleContent", battle, 27, Gold, -220, "Enemies block your path.", "Win the battle to continue.", bodyFont);
            Bind(data, "healthFill", Health(heal));
            Bind(data, "recoveryDetail", Label("Detail", heal, new Vector2(0, -110), new Vector2(950, 60), "85 -> 100 HP", 19, Muted, bodyFont));
            var offer = Card("Offer", card, chance.rewardCardPrefab, headingFont, bodyFont);
            var offerRect = (RectTransform)offer.transform;
            offerRect.anchoredPosition = new Vector2(0, 18); offerRect.localScale = Vector3.one * 1.25f;
            var offerButton = offer.GetComponent<Button>();
            offerButton.transition = Selectable.Transition.None; offerButton.interactable = false;
            Bind(data, "rewardRoot", offer.gameObject);
            var replacement = Group("Replacement", panel, "LOST OFFERING", headingFont);
            Label("Instruction", replacement, new Vector2(0, -369), new Vector2(950, 32), "Choose one card to replace.", 19, Muted, bodyFont);
            var viewport = Box("Viewport", replacement, new Vector2(0, -34), new Vector2(1040, 620), Ink);
            viewport.gameObject.AddComponent<RectMask2D>();
            var scroll = viewport.gameObject.AddComponent<ScrollRect>();
            var content = Rect("Cards", viewport, Vector2.zero, Vector2.zero);
            content.anchorMin = new Vector2(0, 1); content.anchorMax = Vector2.one; content.pivot = new Vector2(.5f, 1);
            var grid = content.gameObject.AddComponent<GridLayoutGroup>();
            grid.cellSize = new Vector2(220, 300); grid.spacing = new Vector2(12, 12);
            grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount; grid.constraintCount = 4;
            grid.childAlignment = TextAnchor.UpperCenter;
            content.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            scroll.viewport = viewport; scroll.content = content; scroll.horizontal = false;
            scroll.movementType = ScrollRect.MovementType.Clamped; scroll.scrollSensitivity = 35;
            var template = Card("Template", content, chance.rewardCardPrefab, headingFont, bodyFont);
            template.gameObject.SetActive(false);
            Bind(data, "replacementRoot", replacement.gameObject); Bind(data, "replacementContent", content);
            Bind(data, "replacementTemplate", template); Bind(data, "secondaryButton", secondary);
            Bind(data, "secondaryText", secondaryText); Bind(data, "icon", icon);
            Bind(data, "revealPose", Rect("RevealPose", panel, new Vector2(0, 20), new Vector2(240, 240)));
            Bind(data, "controls", panel.gameObject.AddComponent<CanvasGroup>());
            // The editor opens on the card result; all other pages are editable siblings.
            heal.gameObject.SetActive(false); battle.gameObject.SetActive(false); replacement.gameObject.SetActive(false);
            title.gameObject.SetActive(false); value.gameObject.SetActive(false); body.gameObject.SetActive(false);
            detail.transform.SetAsLastSibling(); capacity.transform.SetAsLastSibling(); actions.SetAsLastSibling();
            nextText.text = "Take Card";
        }
        data.ApplyModifiedPropertiesWithoutUndo();
        Canvas.ForceUpdateCanvases();
    }

    private static void BindContent(SerializedObject data, string property, RectTransform root, float size, Color color,
        float bodyY, string value, string body, TMP_FontAsset font)
    {
        var group = data.FindProperty(property);
        group.FindPropertyRelative("root").objectReferenceValue = root.gameObject;
        group.FindPropertyRelative("value").objectReferenceValue = Label("Value", root, new Vector2(0, 100), new Vector2(850, 76), value, size, color, font);
        group.FindPropertyRelative("body").objectReferenceValue = Label("Body", root, new Vector2(0, bodyY), new Vector2(840, 92), body, 27, Cream, font);
    }

    private static RectTransform Group(string name, RectTransform parent, string title, TMP_FontAsset font)
    {
        var root = Rect(name, parent, Vector2.zero, Vector2.zero); Stretch(root);
        Label("Title", root, new Vector2(0, 326), new Vector2(850, 58), title, 36, Cream, font);
        return root;
    }

    private static Image Health(RectTransform parent)
    {
        var bar = Box("Health", parent, new Vector2(0, 28), new Vector2(620, 14), new Color(.2f, .22f, .17f));
        var fill = Box("Fill", bar, Vector2.zero, Vector2.zero, Green); Stretch(fill);
        return fill.GetComponent<Image>();
    }

    private static RewardCardView Card(string name, Transform parent, GameObject prefab, TMP_FontAsset heading, TMP_FontAsset body)
    {
        Require(prefab != null, "A reward card prefab is required for initial authoring.");
        var root = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
        PrefabUtility.UnpackPrefabInstance(root, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
        root.name = name;
        var view = root.GetComponent<RewardCardView>();
        var example = AssetDatabase.LoadAssetAtPath<CardDefinition>("Assets/Data/Cards/TidalWave.asset");
        using var data = new SerializedObject(view);
        ((Image)data.FindProperty("artwork").objectReferenceValue).sprite = example.FrontIllustration;
        ((Image)data.FindProperty("border").objectReferenceValue).sprite = example.frontBorder;
        ((TMP_Text)data.FindProperty("nameText").objectReferenceValue).text = example.displayName;
        ((TMP_Text)data.FindProperty("effectText").objectReferenceValue).text = GameFonts.FormatEffect(example.GetCombatDescription()) + $"\nCapacity: {example.capacityCost}";
        foreach (var text in root.GetComponentsInChildren<TMP_Text>(true))
        {
            bool title = text == (TMP_Text)data.FindProperty("nameText").objectReferenceValue;
            text.font = title ? heading : body; text.fontStyle = FontStyles.Normal; text.characterSpacing = title ? 1.2f : 0;
            text.enableAutoSizing = true; text.fontSizeMax = text.fontSize; text.fontSizeMin = Mathf.Min(14, text.fontSize);
            text.textWrappingMode = TextWrappingModes.Normal;
        }
        return view;
    }

    private static TMP_FontAsset SavedFont(string name)
    {
        string path = "Assets/Resources/Fonts/" + name + " SDF.asset";
        var existing = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(path);
        if (existing != null) return existing;
        var source = AssetDatabase.LoadAssetAtPath<Font>("Assets/Resources/Fonts/" + name + ".ttf");
        Require(source != null, "Missing font source: " + name);
        var asset = TMP_FontAsset.CreateFontAsset(source);
        asset.name = name + " SDF"; asset.atlasPopulationMode = AtlasPopulationMode.Dynamic;
        asset.TryAddCharacters(string.Concat(Enumerable.Range(32, 95).Select(value => (char)value)));
        AssetDatabase.CreateAsset(asset, path);
        asset.material.name = name + " SDF Material"; AssetDatabase.AddObjectToAsset(asset.material, asset);
        foreach (var atlas in asset.atlasTextures)
        {
            atlas.name = name + " SDF Atlas"; AssetDatabase.AddObjectToAsset(atlas, asset);
        }
        EditorUtility.SetDirty(asset); AssetDatabase.SaveAssets();
        return asset;
    }

    private static RectTransform Rect(string name, Transform parent, Vector2 position, Vector2 size)
    {
        var root = new GameObject(name, typeof(RectTransform)) { layer = 5 };
        var rect = (RectTransform)root.transform; rect.SetParent(parent, false);
        rect.anchoredPosition = position; rect.sizeDelta = size; return rect;
    }
    private static RectTransform Box(string name, Transform parent, Vector2 position, Vector2 size, Color color)
    {
        var rect = Rect(name, parent, position, size); rect.gameObject.AddComponent<Image>().color = color; return rect;
    }
    private static TMP_Text Label(string name, Transform parent, Vector2 position, Vector2 size, string value, float points, Color color, TMP_FontAsset font)
    {
        var text = Rect(name, parent, position, size).gameObject.AddComponent<TextMeshProUGUI>();
        text.text = value; text.font = font; text.fontSize = points; text.color = color;
        text.alignment = TextAlignmentOptions.Center; text.raycastTarget = false;
        text.textWrappingMode = TextWrappingModes.Normal;
        if (font.name.StartsWith("Cinzel", StringComparison.Ordinal)) text.characterSpacing = 1.2f;
        return text;
    }
    private static Button Action(string name, Transform parent, string label, TMP_FontAsset font, out TMP_Text text)
    {
        var rect = Box(name, parent, Vector2.zero, new Vector2(340, 64), new Color(.29f, .21f, .12f)); Border(rect, 1);
        var button = rect.gameObject.AddComponent<Button>(); button.targetGraphic = rect.GetComponent<Image>();
        text = Label("Label", rect, Vector2.zero, Vector2.zero, label, 23, Cream, font); Stretch(text.rectTransform);
        return button;
    }
    private static void Stretch(RectTransform rect)
    { rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.offsetMin = rect.offsetMax = Vector2.zero; }
    private static void Border(RectTransform rect, float width)
    { var outline = rect.gameObject.AddComponent<Outline>(); outline.effectColor = Gold; outline.effectDistance = new Vector2(width, -width); }
    private static void Bind(SerializedObject data, string name, Object value) => data.FindProperty(name).objectReferenceValue = value;
    private static T[] Components<T>(Scene scene) where T : Component => scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<T>(true)).ToArray();
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
