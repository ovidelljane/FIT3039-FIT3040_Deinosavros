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
            changed |= MapCardDetailAuthoring.Migrate(authored);
            changed |= MapCardDetailAuthoring.EnableHierarchyEditing(authored);
            changed |= SimplifyPanelCopy(authored);
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

    [MenuItem("Tools/Map/Apply Sacrifice Burn")]
    public static void ApplyBurn()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Stop Play Mode first.");
        for (int i = 0; i < SceneManager.sceneCount; i++)
            if (SceneManager.GetSceneAt(i).isDirty) throw new InvalidOperationException("Save your scene changes first.");
        Directory.CreateDirectory(Results);
        if (!File.Exists(Results + "/Map-before-burn.unity")) File.Copy(ScenePath, Results + "/Map-before-burn.unity");
        if (!File.Exists(Results + "/Map-before-center-feedback.unity")) File.Copy(ScenePath, Results + "/Map-before-center-feedback.unity");
        var setup = EditorSceneManager.GetSceneManagerSetup();
        try
        {
            var shader = Shader.Find("Deinosavros/UI/Sacrifice Burn");
            if (shader == null || ShaderUtil.ShaderHasError(shader)) throw new InvalidOperationException("The sacrifice burn shader must compile.");
            const string path = "Assets/UI/SacrificeBurn.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(shader) { name = "SacrificeBurn" };
                AssetDatabase.CreateAsset(material, path);
            }
            var scene = EditorSceneManager.OpenScene(ScenePath);
            var panel = Components<MapSacrificePanel>(scene).Single();
            var root = panel.burnEffect != null ? (RectTransform)panel.burnEffect.transform : panel.panel.Find("BurnFX") as RectTransform;
            bool changed = false;
            if (root == null)
            {
                root = Rect("BurnFX", panel.overlay.transform); Stretch(root);
                var group = root.gameObject.AddComponent<CanvasGroup>();
                group.interactable = false; group.blocksRaycasts = false;
                changed = true;
            }
            var effect = root.GetComponent<SacrificeBurnEffect>();
            if (effect == null) { effect = root.gameObject.AddComponent<SacrificeBurnEffect>(); changed = true; }
            if (root.parent != panel.overlay.transform)
            {
                root.SetParent(panel.overlay.transform, false); Stretch(root); changed = true;
            }
            if (effect.focusAnchor == null)
            {
                var focus = Rect("OfferingFocus", panel.overlay.transform.Find("SafeArea"));
                focus.anchorMin = focus.anchorMax = focus.pivot = new(.5f, .5f);
                focus.anchoredPosition = Vector2.zero; focus.sizeDelta = Vector2.zero;
                effect.focusAnchor = focus; changed = true;
            }
            var player = Components<MapTravelView>(scene).Single();
            if (panel.playerSymbol != player) { panel.playerSymbol = player; changed = true; }
            if (player.transform.Find("Pulse") == null)
            {
                var pulse = new GameObject("Pulse", typeof(MeshFilter), typeof(MeshRenderer));
                pulse.transform.SetParent(player.transform, false);
                pulse.GetComponent<MeshFilter>().sharedMesh = player.profile.quadMesh;
                var renderer = pulse.GetComponent<MeshRenderer>(); renderer.sharedMaterial = player.profile.symbolMaterial;
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                renderer.receiveShadows = false; renderer.enabled = false;
                changed = true;
            }
            var backdrop = panel.overlay.GetComponent<Image>();
            if (panel.feedbackBackdrop != backdrop) { panel.feedbackBackdrop = backdrop; changed = true; }
            if (effect.cardTemplate == null)
            {
                var template = new GameObject("CardTemplate", typeof(RectTransform)).AddComponent<UnityEngine.UI.RawImage>();
                template.transform.SetParent(root, false); template.rectTransform.sizeDelta = new(264, 363);
                template.raycastTarget = false; template.material = material;
                template.gameObject.SetActive(false); effect.cardTemplate = template; changed = true;
            }
            if (panel.burnEffect != effect) { panel.burnEffect = effect; changed = true; }
            if (changed) { EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene); }
            scene = EditorSceneManager.OpenScene(ScenePath);
            panel = Components<MapSacrificePanel>(scene).Single();
            if (panel.burnEffect == null || !panel.burnEffect.IsReady || panel.burnEffect.cardTemplate.gameObject.activeSelf ||
                panel.burnEffect.focusAnchor == null || panel.playerSymbol == null || panel.feedbackBackdrop == null)
                throw new InvalidOperationException("The saved burn template or material is missing.");
            AssetDatabase.SaveAssetIfDirty(material);
            File.WriteAllText(Results + "/burn-authoring.txt", "PASS: saved BurnFX, material, non-interactive template and Inspector settings; existing layout preserved.\n");
        }
        finally { EditorSceneManager.RestoreSceneManagerSetup(setup); }
    }

    public static void ApplyBurnAndVerify()
    {
        ApplyBurn(); string before = File.ReadAllText(ScenePath); ApplyBurn();
        if (File.ReadAllText(ScenePath) != before) throw new InvalidOperationException("Repeated burn authoring changed Map.");
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
        var root = Rect("Sacrifice",canvas.transform); Stretch(root);
        var ui = root.gameObject.AddComponent<MapSacrificePanel>();
        ui.statusText = status; status.text = "Sacrifice available";
        ui.statusButton = status.GetComponent<Button>() ?? status.gameObject.AddComponent<Button>();
        ui.statusButton.targetGraphic = status; status.raycastTarget = true;
        PlaceStatus(ui);
        ui.statusPopup = Box("Pending",root,Panel,Vector2.zero,new(430,320)).gameObject;
        var popup = (RectTransform)ui.statusPopup.transform;
        popup.anchorMin = popup.anchorMax = popup.pivot = Vector2.one;
        popup.anchoredPosition = new(-24,-68); Border(ui.statusPopup,Gold);
        ui.statusDetails = Label("Details",popup,"",Vector2.zero,new(390,280),20);
        ui.statusDetails.alignment = TextAlignmentOptions.TopLeft;
        ui.statusPopup.GetComponent<Image>().raycastTarget = true; ui.statusPopup.SetActive(false);
        var overlay = Box("Overlay",root,new(.06f,.045f,.032f,0),Vector2.zero,Vector2.zero);
        Stretch(overlay.rectTransform); ui.overlay = overlay.gameObject;
        var safe = Rect("SafeArea",overlay.transform);
        safe.anchorMin = new(0,.27f); safe.anchorMax = new(1,.94f); safe.offsetMin = safe.offsetMax = Vector2.zero;
        var panel = Box("Detail",safe,Color.clear,new(244,0),new(440,696));
        panel.rectTransform.anchorMin = panel.rectTransform.anchorMax = new(0,.5f);
        panel.raycastTarget = true; ui.panel = panel.rectTransform;
        // Supply only the live content; the shared migration arranges the full-art presentation.
        ui.cardPreview = Rect("Card",panel.transform);
        ui.illustration = Box("Artwork",ui.cardPreview,Color.white,Vector2.zero,new(440,696));
        ui.cardName = Label("Name",ui.cardPreview,"",new(0,280),new(376,92),32,true);
        ui.combatText = Label("Effect",ui.cardPreview,"",new(0,-105),new(376,252),26);
        ui.reason = Label("Reason",panel.transform,"",new(0,-249),new(388,36),20);
        ui.confirm = Button("Sacrifice",panel.transform,"Sacrifice",new(0,-296),new(260,52),false);
        ui.flightIcon = Icon("OfferingFlight",overlay.transform,Vector2.zero,66);
        ui.flightHeartCut = Box("Fracture",ui.flightIcon.transform,Ink,Vector2.zero,new(4,39));
        ui.flightHeartCut.rectTransform.localEulerAngles = new(0,0,-18);
        ui.flightIcon.gameObject.SetActive(false); overlay.gameObject.SetActive(false);
        BuildDepartureWarning(ui);
        return ui;
    }

    private static void BuildDepartureWarning(MapSacrificePanel ui)
    {
        var shade = Box("TravelWarning",ui.transform,new(.06f,.045f,.032f,.38f),Vector2.zero,Vector2.zero);
        Stretch(shade.rectTransform); shade.raycastTarget = true; ui.departureWarning = shade.gameObject;
        var box = Box("Panel",shade.transform,Panel,new(0,70),new(600,280));
        box.raycastTarget = true; Border(box.gameObject,Gold); ui.departurePanel = box.rectTransform;
        Label("Title",box.transform,"NO SACRIFICE",new(0,93),new(540,50),30,true);
        ui.departureMessage = Label("Message",box.transform,"",new(0,16),new(548,104),27);
        ui.departureBack = Button("Back",box.transform,"Back",new(-145,-87),new(200,54),false);
        ui.departureContinue = Button("Continue",box.transform,"Enter Battle",new(123,-87),new(260,54),true);
        ui.departureWarning.SetActive(false);
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
            MapCardDetailAuthoring.Migrate(ui);
            MapCardDetailAuthoring.EnableHierarchyEditing(ui);
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



    private static bool SimplifyPanelCopy(MapSacrificePanel ui)
    {
        bool changed = false;
        foreach (var label in new[] { ui.reason })
        {
            if (label == null) continue;
            if (!string.IsNullOrEmpty(label.text)) { label.text = ""; changed = true; }
            if (label.gameObject.activeSelf) { label.gameObject.SetActive(false); changed = true; }
        }
        return changed;
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
        if (!panel.IsReady || panel.illustration == null || panel.detailVisual == null || panel.statusButton == null)
            throw new InvalidOperationException("Missing saved sacrifice references.");
        if (new[] { panel.reason }
            .Any(label => label != null && (label.gameObject.activeSelf || !string.IsNullOrEmpty(label.text))))
            throw new InvalidOperationException("The saved sacrifice panel still contains redundant copy.");
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
