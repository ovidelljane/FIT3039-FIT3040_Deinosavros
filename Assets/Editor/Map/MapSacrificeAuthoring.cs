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

// Explicit migration of only the Map offering UI. Reapplication preserves authored layout.
public static class MapSacrificeAuthoring
{
    private const string ScenePath = "Assets/Scenes/Map.unity";
    public const string Results = "Library/MapSacrifice";
    private static readonly Color Panel = new(.235f, .205f, .172f), Ink = new(.15f, .119f, .093f);
    private static readonly Color Gold = new(.68f, .52f, .32f), Cream = new(.94f, .88f, .76f);
    private static TMP_FontAsset Body => AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/Resources/Fonts/Alegreya SDF.asset");
    private static TMP_FontAsset Heading => AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/Resources/Fonts/Cinzel-Black SDF.asset");

    [MenuItem("Tools/Map/Author Sacrifice Interface")]
    public static void Apply()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Stop Play Mode first.");
        for (int i = 0; i < SceneManager.sceneCount; i++)
            if (SceneManager.GetSceneAt(i).isDirty) throw new InvalidOperationException("Save your scene changes first.");
        Directory.CreateDirectory(Results);
        if (!File.Exists(Results + "/Map-before-sacrifice.unity")) File.Copy(ScenePath, Results + "/Map-before-sacrifice.unity");
        var setup = EditorSceneManager.GetSceneManagerSetup();
        try
        {
            foreach (var path in AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/Prefabs/Map" })
                .Select(AssetDatabase.GUIDToAssetPath).OrderBy(p => p.Length))
            {
                var asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (asset.GetComponent<MapCardView>() == null) continue;
                var root = PrefabUtility.LoadPrefabContents(path);
                try { if (AuthorBack(root.GetComponent<MapCardView>())) PrefabUtility.SaveAsPrefabAsset(root, path); }
                finally { PrefabUtility.UnloadPrefabContents(root); }
            }
            var scene = EditorSceneManager.OpenScene(ScenePath);
            var controller = Components<MapController>(scene).Single();
            bool changed = false;
            foreach (var card in Components<MapCardView>(scene)) changed |= AuthorBack(card);
            var authored = Components<MapSacrificePanel>(scene).SingleOrDefault();
            if (authored == null)
            {
                using var data = new SerializedObject(controller);
                var status = (TMP_Text)data.FindProperty("sacrificeStatusText").objectReferenceValue;
                var canvas = status.canvas.rootCanvas;
                foreach (string name in new[] { "CardDetailPanel", "SacrificeConfirmation" })
                {
                    var old = canvas.transform.Find(name);
                    if (old != null) Object.DestroyImmediate(old.gameObject);
                }
                authored = BuildPanel(canvas, status);
                data.FindProperty("sacrificePanel").objectReferenceValue = authored;
                data.ApplyModifiedPropertiesWithoutUndo(); changed = true;
            }
            if (changed) { EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene); }
            scene = EditorSceneManager.OpenScene(ScenePath);
            Validate(scene);
            File.WriteAllText(Results + "/authoring.txt", "PASS: saved Map panel, status, card backs, one front frame and persistent typography.\n");
        }
        finally
        {
            if (setup.Any(s => s.isLoaded)) EditorSceneManager.RestoreSceneManagerSetup(setup);
            else EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        }
    }

    public static void ApplyAndVerify()
    {
        Apply(); var before = File.ReadAllText(ScenePath); Apply(); Apply();
        if (File.ReadAllText(ScenePath) != before) throw new InvalidOperationException("Repeated authoring changed Map.");
        File.AppendAllText(Results + "/authoring.txt", "PASS: two additional applications preserve exact scene bytes.\n");
        RunIntegrationPlayVerification.RunSacrifice();
    }

    public static bool AuthorBack(MapCardView view)
    {
        using var data = new SerializedObject(view);
        bool changed = false;
        var oldBack = (GameObject)data.FindProperty("backInstance").objectReferenceValue;
        if (oldBack != null)
        {
            Object.DestroyImmediate(oldBack);
            data.FindProperty("backInstance").objectReferenceValue = null;
            data.ApplyModifiedPropertiesWithoutUndo(); changed = true;
        }
        var face = ((GameObject)data.FindProperty("backFace").objectReferenceValue).transform;
        var title = (TMP_Text)data.FindProperty("titleText").objectReferenceValue;
        if (data.FindProperty("offeringIcon").objectReferenceValue != null) return changed;
        Stretch((RectTransform)face); face.localScale = Vector3.one; face.localRotation = Quaternion.identity;
        var fill = face.Find("BackFill")?.GetComponent<Image>() ?? Box("BackFill", face, Ink, Vector2.zero, new(152, 258));
        fill.color = Ink; fill.transform.SetAsFirstSibling(); Set(fill.rectTransform, new(-1, -1), new(152, 258));
        var artwork = (Image)data.FindProperty("backArtwork").objectReferenceValue;
        artwork.transform.SetSiblingIndex(1); Set(artwork.rectTransform, Vector2.zero, new(240, 340)); artwork.preserveAspect = true;
        Style(title, true, 17, 13); Set(title.rectTransform, new(0, 91), new(136, 40));
        var effect = (TMP_Text)data.FindProperty("effectText").objectReferenceValue;
        Style(effect, false, 21, 17); Set(effect.rectTransform, new(0, -8), new(134, 72)); effect.gameObject.SetActive(true);
        var scope = (TMP_Text)data.FindProperty("scopeText").objectReferenceValue;
        Style(scope, false, 15, 15); Set(scope.rectTransform, new(0, -58), new(134, 28)); scope.text = "Next battle";
        var icon = Icon("Offering", face, new(0, 43), 46);
        var cut = Box("Fracture", icon.transform, Ink, Vector2.zero, new(3, 27)); cut.rectTransform.localEulerAngles = new(0, 0, -18); cut.enabled = false;
        var oldButton = (Button)data.FindProperty("sacrificeButton").objectReferenceValue;
        if (oldButton != null) { Object.DestroyImmediate(oldButton.gameObject); data.FindProperty("sacrificeButton").objectReferenceValue = null; }
        data.FindProperty("offeringIcon").objectReferenceValue = icon;
        data.FindProperty("brokenHeartCut").objectReferenceValue = cut;
        data.FindProperty("backFill").objectReferenceValue = fill;
        data.ApplyModifiedPropertiesWithoutUndo(); CompactBack(view); return true;
    }

    private static MapSacrificePanel BuildPanel(Canvas canvas, TMP_Text status)
    {
        var root = Rect("Sacrifice", canvas.transform); Stretch(root);
        var ui = root.gameObject.AddComponent<MapSacrificePanel>();
        ui.statusText = status; status.text = "Sacrifice available";
        status.font = Body; status.fontSize = 22; status.fontSizeMin = 18; status.enableAutoSizing = true;
        var statusRect = status.rectTransform;
        statusRect.anchorMin = new(.66f, 0); statusRect.anchorMax = Vector2.one;
        statusRect.offsetMin = new(12, 4); statusRect.offsetMax = new(-24, -4);
        status.alignment = TextAlignmentOptions.MidlineRight; status.raycastTarget = true;
        ui.statusButton = status.gameObject.AddComponent<Button>(); ui.statusButton.targetGraphic = status;
        PlaceStatus(ui);
        ui.statusPopup = Box("Pending", root, Panel, Vector2.zero, new(430, 320)).gameObject;
        var popupRect = (RectTransform)ui.statusPopup.transform; popupRect.anchorMin = popupRect.anchorMax = popupRect.pivot = Vector2.one;
        popupRect.anchoredPosition = new(-24, -68); Border(ui.statusPopup, Gold);
        ui.statusDetails = Label("Details", popupRect, "", Vector2.zero, new(390, 280), 20);
        ui.statusDetails.alignment = TextAlignmentOptions.TopLeft; ui.statusPopup.GetComponent<Image>().raycastTarget = true;
        ui.statusPopup.SetActive(false);
        var overlay = Box("Overlay", root, new(.06f, .045f, .032f, .48f), Vector2.zero, Vector2.zero);
        Stretch(overlay.rectTransform); overlay.raycastTarget = true; ui.overlay = overlay.gameObject;
        var safe = Rect("SafeArea", overlay.transform); safe.anchorMin = new(0, .27f); safe.anchorMax = new(1, .94f); safe.offsetMin = safe.offsetMax = Vector2.zero;
        var panel = Box("Panel", safe, Panel, Vector2.zero, new(960, 560)); panel.raycastTarget = true;
        ui.panel = panel.rectTransform; Border(panel.gameObject, Gold);
        Box("Header", panel.transform, Ink, new(0, 243), new(958, 72));
        var heading = Label("Title", panel.transform, "SACRIFICE", new(-208, 241), new(480, 42), 30, true); heading.alignment = TextAlignmentOptions.MidlineLeft;
        Label("Subtitle", panel.transform, "A card for a blessing", new(239, 241), new(320, 30), 21);
        Box("Rule", panel.transform, Gold, new(0, 207), new(902, 1));
        // Restrained meander accents, made from original flat vector segments.
        for (int side = -1; side <= 1; side += 2)
        {
            var corner = Rect(side < 0 ? "LeftKey" : "RightKey", panel.transform); Set(corner, new(side * 452, -191), new(18, 18));
            Box("A", corner, Gold, new(0, 0), new(20, 2)); Box("B", corner, Gold, new(9, 5), new(2, 12));
            Box("C", corner, Gold, new(3, 10), new(12, 2)); Box("D", corner, Gold, new(-2, 7), new(2, 8));
        }
        Label("Give", panel.transform, "YOU GIVE", new(-320, 183), new(248, 25), 18, true);
        ui.cardPreview = Rect("Card", panel.transform); Set(ui.cardPreview, new(-320, 15), new(220, 292));
        ui.cardGroup = ui.cardPreview.gameObject.AddComponent<CanvasGroup>(); ui.cardGroup.blocksRaycasts = false;
        Box("Paper", ui.cardPreview, Ink, Vector2.zero, new(164, 276));
        ui.illustration = Box("Art", ui.cardPreview, Color.white, new(0, 8), new(163, 224)); ui.illustration.preserveAspect = true;
        ui.frame = Box("Frame", ui.cardPreview, Color.white, Vector2.zero, new(264, 363)); ui.frame.preserveAspect = true;
        ui.cardOutline = ui.frame.gameObject.AddComponent<Outline>(); ui.cardOutline.effectDistance = new(3, -3); ui.cardOutline.enabled = false;
        ui.combatText = Label("CombatText", ui.cardPreview, "", new(0, -17), new(150, 200), 21); ui.combatText.fontSizeMin = 18;
        var textShadow = ui.combatText.gameObject.AddComponent<Shadow>(); textShadow.effectColor = new(0, 0, 0, .95f); textShadow.effectDistance = new(1, -1);
        ui.cost = Label("Cost", ui.cardPreview, "", new(-75, 132), new(24, 28), 19);
        ui.categoryMedallion = Box("Category", ui.cardPreview, Color.clear, new(0, -137), new(35, 2));
        ui.cardName = Label("Name", panel.transform, "", new(-320, -147), new(265, 44), 19, true); ui.cardName.fontSizeMin = 16; ui.cardName.enableAutoSizing = true;
        Box("Divider", panel.transform, new(.46f, .37f, .26f), new(-167, 9), new(1, 345));
        var next = Label("Scope", panel.transform, "NEXT BATTLE", new(160, 183), new(510, 26), 18, true); next.alignment = TextAlignmentOptions.MidlineLeft;
        ui.effectIcon = Icon("Effect", panel.transform, new(-101, 112), 66);
        ui.brokenHeartCut = Box("Fracture", ui.effectIcon.transform, Panel, Vector2.zero, new(4, 39));
        ui.brokenHeartCut.rectTransform.localEulerAngles = new(0, 0, -18); ui.brokenHeartCut.enabled = false;
        ui.benefitTitle = Label("BenefitTitle", panel.transform, "", new(181, 139), new(444, 42), 26, true);
        ui.benefitTitle.alignment = TextAlignmentOptions.MidlineLeft;
        ui.benefitValue = Label("Value", panel.transform, "", new(181, 80), new(444, 68), 36); ui.benefitValue.fontSizeMin = 30; ui.benefitValue.enableAutoSizing = true;
        ui.benefitValue.alignment = TextAlignmentOptions.MidlineLeft;
        ui.benefitDetails = Label("Details", panel.transform, "", new(145, -19), new(520, 100), 24); ui.benefitDetails.alignment = TextAlignmentOptions.TopLeft;
        ui.benefitLimit = Label("Limit", panel.transform, "", new(145, -114), new(520, 58), 20); ui.benefitLimit.alignment = TextAlignmentOptions.TopLeft;
        ui.benefitLimit.color = new(.76f, .71f, .62f);
        Box("Footer", panel.transform, Ink, new(0, -224), new(958, 110));
        ui.capacity = Label("Capacity", panel.transform, "", new(-174, -190), new(545, 36), 22); ui.capacity.alignment = TextAlignmentOptions.MidlineLeft;
        Label("Removal", panel.transform, "Removed from this run.", new(-18, -220), new(240, 30), 18);
        ui.reason = Label("Reason", panel.transform, "", new(-5, -251), new(450, 44), 17);
        ui.reason.enableAutoSizing = true; ui.reason.fontSizeMin = 15;
        ui.confirm = Button("Confirm", panel.transform, "Sacrifice Card", new(324, -239), new(240, 52), true);
        ui.close = Button("Close", panel.transform, "X", new(447, 243), new(40, 40), false);
        ui.flightIcon = Icon("OfferingFlight", overlay.transform, Vector2.zero, 66);
        ui.flightHeartCut = Box("Fracture", ui.flightIcon.transform, Ink, Vector2.zero, new(4, 39));
        ui.flightHeartCut.rectTransform.localEulerAngles = new(0, 0, -18);
        ui.flightIcon.gameObject.SetActive(false); overlay.gameObject.SetActive(false);
        LayoutLeftPanel(ui);
        return ui;
    }

    [MenuItem("Tools/Map/Use Left Sacrifice Details")]
    public static void RefineLeftLayout()
    {
        Apply();
        string backup = Results + "/Map-before-left-details.unity";
        if (!File.Exists(backup)) File.Copy(ScenePath, backup);
        var setup = EditorSceneManager.GetSceneManagerSetup();
        try
        {
            foreach (string path in AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/Prefabs/Map" }).Select(AssetDatabase.GUIDToAssetPath))
            {
                if (AssetDatabase.LoadAssetAtPath<GameObject>(path).GetComponent<MapCardView>() == null) continue;
                var root = PrefabUtility.LoadPrefabContents(path);
                try { CompactBack(root.GetComponent<MapCardView>()); PrefabUtility.SaveAsPrefabAsset(root, path); }
                finally { PrefabUtility.UnloadPrefabContents(root); }
            }
            var scene = EditorSceneManager.OpenScene(ScenePath);
            var ui = Components<MapSacrificePanel>(scene).Single();
            LayoutLeftPanel(ui);
            foreach (var card in Components<MapCardView>(scene)) CompactBack(card);
            var controller = Components<MapController>(scene).Single();
            using var data = new SerializedObject(controller);
            foreach (string field in new[] { "selectedRouteText", "capacityText" })
            {
                var label = (TMP_Text)data.FindProperty(field).objectReferenceValue;
                Style(label, false, 25, 22);
                if (field == "selectedRouteText") label.alignment = TextAlignmentOptions.MidlineLeft;
            }
            EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene);
            Validate(EditorSceneManager.OpenScene(ScenePath));
        }
        finally
        {
            if (setup.Any(s => s.isLoaded)) EditorSceneManager.RestoreSceneManagerSetup(setup);
            else EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        }
    }

    public static void RefineAndVerify()
    {
        RefineLeftLayout(); string first = File.ReadAllText(ScenePath); RefineLeftLayout();
        if (first != File.ReadAllText(ScenePath)) throw new InvalidOperationException("Left layout is not idempotent.");
        RunIntegrationPlayVerification.RunSacrifice();
    }

    private static void Remove(Transform parent, params string[] names)
    {
        foreach (string name in names) { var child = parent.Find(name); if (child != null) Object.DestroyImmediate(child.gameObject); }
    }

    private static void CompactBack(MapCardView view)
    {
        using var data = new SerializedObject(view);
        var back = ((GameObject)data.FindProperty("backFace").objectReferenceValue).transform;
        Remove(back, "Capacity", "Inspect", "Rule");
        var title = (TMP_Text)data.FindProperty("titleText").objectReferenceValue;
        Style(title, true, 19, 14); Set(title.rectTransform, new(0, 88), new(136, 45));
        var effect = (TMP_Text)data.FindProperty("effectText").objectReferenceValue;
        Style(effect, false, 24, 20); Set(effect.rectTransform, new(0, -19), new(136, 94));
        var scope = (TMP_Text)data.FindProperty("scopeText").objectReferenceValue;
        Style(scope, false, 18, 18); Set(scope.rectTransform, new(0, -91), new(136, 30));
        Set(((StatusIcon)data.FindProperty("offeringIcon").objectReferenceValue).rectTransform, new(0, 34), new(48, 48));
        EditorUtility.SetDirty(view);
    }

    private static void LayoutLeftPanel(MapSacrificePanel ui)
    {
        var p = ui.panel;
        Set(p, new(24, 0), new(440, 696)); p.anchorMin = p.anchorMax = p.pivot = new(0, .5f);
        // Restore a centered local coordinate system for the already-authored children.
        p.pivot = new(.5f, .5f); p.anchoredPosition = new(244, 0);
        ui.overlay.GetComponent<Image>().color = new(.06f, .045f, .032f, .12f);
        Remove(p, "Subtitle", "Give", "Divider", "LeftKey", "RightKey", "Keep", "Freed", "Removal");
        Set((RectTransform)p.Find("Header"), new(0, 312), new(438, 70));
        var title = p.Find("Title").GetComponent<TMP_Text>();
        Set(title.rectTransform, new(-32, 312), new(338, 48)); Style(title, true, 30, 30); title.alignment = TextAlignmentOptions.MidlineLeft;
        Set((RectTransform)p.Find("Rule"), new(0, 276), new(394, 1));
        Set(ui.cardPreview, new(-112, 161), new(220, 292)); ui.cardPreview.localScale = Vector3.one * .62f;
        ui.combatText.gameObject.SetActive(false);
        Set(ui.cardName.rectTransform, new(84, 235), new(212, 74)); Style(ui.cardName, true, 26, 21);
        Set(ui.effectIcon.rectTransform, new(82, 165), new(60, 60));
        Set(ui.benefitTitle.rectTransform, new(82, 102), new(212, 68)); Style(ui.benefitTitle, true, 24, 21);
        var scope = p.Find("Scope").GetComponent<TMP_Text>();
        Set(scope.rectTransform, new(0, 31), new(396, 34)); Style(scope, true, 22, 22); scope.text = "NEXT BATTLE";
        Set(ui.benefitValue.rectTransform, new(0, -29), new(396, 88)); Style(ui.benefitValue, false, 34, 30);
        Set(ui.benefitDetails.rectTransform, new(0, -114), new(396, 90)); Style(ui.benefitDetails, false, 25, 24); ui.benefitDetails.alignment = TextAlignmentOptions.TopLeft;
        Set(ui.benefitLimit.rectTransform, new(0, -174), new(396, 64)); Style(ui.benefitLimit, false, 22, 21); ui.benefitLimit.alignment = TextAlignmentOptions.TopLeft;
        Set((RectTransform)p.Find("Footer"), new(0, -277), new(438, 140));
        Set(ui.capacity.rectTransform, new(0, -229), new(400, 38)); Style(ui.capacity, false, 24, 23);
        Set(ui.reason.rectTransform, new(0, -273), new(400, 50)); Style(ui.reason, false, 21, 19);
        Set((RectTransform)ui.confirm.transform, new(0, -315), new(258, 52));
        ui.confirm.GetComponentInChildren<TMP_Text>().text = "Sacrifice";
        Style(ui.confirm.GetComponentInChildren<TMP_Text>(), true, 24, 24);
        Set((RectTransform)ui.close.transform, new(185, 312), new(42, 42));
        Style(ui.statusText, false, 25, 21); ui.statusText.alignment = TextAlignmentOptions.MidlineRight;
        Style(ui.statusDetails, false, 22, 22); ui.statusDetails.alignment = TextAlignmentOptions.TopLeft;
        Set(ui.cost.rectTransform, new(-75, 132), new(30, 36)); Style(ui.cost, false, 28, 24); ui.cost.fontStyle = FontStyles.Bold;
        var warning = ui.transform.Find("TravelWarning");
        if (warning == null)
        {
            var shade = Box("TravelWarning", ui.transform, new(.06f, .045f, .032f, .38f), Vector2.zero, Vector2.zero);
            Stretch(shade.rectTransform); shade.raycastTarget = true; ui.departureWarning = shade.gameObject;
            var box = Box("Panel", shade.transform, Panel, new(0, 70), new(600, 280)); box.raycastTarget = true; Border(box.gameObject, Gold);
            ui.departurePanel = box.rectTransform;
            Label("Title", box.transform, "NO SACRIFICE", new(0, 93), new(540, 50), 30, true);
            ui.departureMessage = Label("Message", box.transform, "", new(0, 16), new(548, 104), 27);
            ui.departureBack = Button("Back", box.transform, "Back", new(-145, -87), new(200, 54), false);
            ui.departureContinue = Button("Continue", box.transform, "Enter Battle", new(123, -87), new(260, 54), true);
            ui.departureWarning.SetActive(false);
        }
        EditorUtility.SetDirty(ui);
    }

    private static void PlaceStatus(MapSacrificePanel ui)
    {
        var text = ui.statusText; var rect = text.rectTransform;
        rect.SetParent(ui.transform, false);
        rect.anchorMin = new(.67f, .947f); rect.anchorMax = Vector2.one;
        rect.offsetMin = new(12, 4); rect.offsetMax = new(-24, -4); rect.localScale = Vector3.one;
        text.font = Body; text.fontSize = text.fontSizeMax = 22; text.fontSizeMin = 18;
        text.fontStyle = FontStyles.Normal; text.enableAutoSizing = true; text.alignment = TextAlignmentOptions.MidlineRight;
        rect.SetAsFirstSibling();
    }

    public static void Validate(Scene scene)
    {
        var panel = Components<MapSacrificePanel>(scene).Single();
        if (!panel.IsReady || panel.illustration == null || panel.frame == null || panel.statusButton == null)
            throw new InvalidOperationException("Missing saved sacrifice references.");
        foreach (var view in Components<MapCardView>(scene))
        {
            using var data = new SerializedObject(view);
            if (data.FindProperty("offeringIcon").objectReferenceValue == null)
                throw new InvalidOperationException("Missing authored card back.");
        }
    }

    private static T[] Components<T>(Scene scene) where T : Component => scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<T>(true)).ToArray();
    private static RectTransform Rect(string name, Transform parent)
    { var existing = parent.Find(name) as RectTransform; if (existing != null) return existing; var obj = new GameObject(name, typeof(RectTransform)); obj.layer = parent.gameObject.layer; obj.transform.SetParent(parent, false); return (RectTransform)obj.transform; }
    private static void Set(RectTransform rect, Vector2 position, Vector2 size)
    { rect.anchorMin = rect.anchorMax = rect.pivot = new(.5f, .5f); rect.anchoredPosition3D = new(position.x, position.y, 0); rect.sizeDelta = size; rect.localScale = Vector3.one; rect.localRotation = Quaternion.identity; }
    private static void Stretch(RectTransform rect) { rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.offsetMin = rect.offsetMax = Vector2.zero; }
    private static Image Box(string name, Transform parent, Color color, Vector2 position, Vector2 size)
    { var rect = Rect(name, parent); Set(rect, position, size); var image = rect.GetComponent<Image>() ?? rect.gameObject.AddComponent<Image>(); image.color = color; image.raycastTarget = false; return image; }
    private static void Border(GameObject obj, Color color) { var outline = obj.AddComponent<Outline>(); outline.effectColor = color; outline.effectDistance = new(1, -1); }
    private static TMP_Text Label(string name, Transform parent, string text, Vector2 position, Vector2 size, float fontSize, bool heading = false)
    { var rect = Rect(name, parent); Set(rect, position, size); var label = rect.GetComponent<TMP_Text>() ?? rect.gameObject.AddComponent<TextMeshProUGUI>(); Style(label, heading, fontSize, fontSize); label.text = text; return label; }
    private static void Style(TMP_Text label, bool heading, float size, float minimum)
    { label.font = heading ? Heading : Body; label.fontSize = label.fontSizeMax = size; label.fontSizeMin = minimum; label.enableAutoSizing = minimum < size; label.color = Cream; label.alignment = TextAlignmentOptions.Center; label.margin = Vector4.zero; label.raycastTarget = false; label.fontStyle = FontStyles.Normal; }
    private static StatusIcon Icon(string name, Transform parent, Vector2 position, float size)
    { var rect = Rect(name, parent); Set(rect, position, new(size, size)); var icon = rect.GetComponent<StatusIcon>() ?? rect.gameObject.AddComponent<StatusIcon>(); icon.raycastTarget = false; icon.color = Cream; return icon; }
    private static Button Button(string name, Transform parent, string text, Vector2 position, Vector2 size, bool primary)
    {
        var image = Box(name, parent, primary ? new(.44f, .31f, .16f) : new(.24f, .20f, .15f), position, size); image.raycastTarget = true;
        Border(image.gameObject, Gold); var button = image.gameObject.AddComponent<Button>(); button.targetGraphic = image;
        var colors = button.colors; colors.normalColor = Color.white; colors.highlightedColor = new(1.25f, 1.20f, 1.09f); colors.pressedColor = new(.72f, .70f, .65f); colors.disabledColor = new(.46f, .44f, .41f); button.colors = colors;
        Label("Label", image.transform, text, Vector2.zero, size - new Vector2(12, 4), primary ? 22 : 20, true);
        return button;
    }
}
