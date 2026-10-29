using System;
using System.IO;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

// Migrates only the existing Map inspector; all unrelated scene layout stays authored.
public static class MapCardDetailAuthoring
{
    private const string ScenePath = "Assets/Scenes/Map.unity";
    [MenuItem("Tools/Map/Enable Detail Fade")]
    public static void ApplyDetailFade()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Stop Play Mode first.");
        var scene = SceneManager.GetSceneByPath(ScenePath);
        bool opened = !scene.IsValid() || !scene.isLoaded;
        if (opened) scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
        try
        {
            var ui = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<MapSacrificePanel>(true)).Single();
            bool changed = EnsureDetailFade(ui);
            if (changed)
            {
                EditorSceneManager.MarkSceneDirty(scene);
                if (!EditorSceneManager.SaveScene(scene)) throw new InvalidOperationException("Could not save detail fade bindings.");
            }
            if (ui.detailGroup == ui.cardGroup || ui.detailGroup.transform != ui.panel || ui.detailGroup.ignoreParentGroups)
                throw new InvalidOperationException("Detail visibility must be independent from the sacrifice Timeline.");
            Directory.CreateDirectory(MapSacrificeAuthoring.Results);
            File.WriteAllText(MapSacrificeAuthoring.Results+"/detail-fade-bindings.txt",
                "PASS: saved parent CanvasGroup, existing UI shader, editable fade times and unchanged artwork material.\n");
        }
        finally { if (opened) EditorSceneManager.CloseScene(scene,true); }
    }

    private static bool EnsureDetailFade(MapSacrificePanel ui)
    {
        if (ui.detailGroup != null) return false;
        Undo.RecordObject(ui,"Bind detail fade");
        var group = ui.panel.GetComponent<CanvasGroup>();
        if (group == null) group = Undo.AddComponent<CanvasGroup>(ui.panel.gameObject);
        ui.detailGroup = group;
        EditorUtility.SetDirty(ui);
        return true;
    }

    [MenuItem("Tools/Map/Simplify Card Gradient")]
    public static void SimplifyGradient()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Stop Play Mode first.");
        var scene = SceneManager.GetActiveScene();
        if (scene.path != ScenePath) throw new InvalidOperationException("Open Map before migrating its card gradient.");
        var ui = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<MapSacrificePanel>(true)).Single();
        var material = ui.DetailMaterial;
        if (material == null || !material.HasProperty("_ShadeHeight")) throw new InvalidOperationException("The simplified card shader must compile first.");
        if (ShaderUtil.ShaderHasError(material.shader)) throw new InvalidOperationException("The card shader has compilation errors.");
        Directory.CreateDirectory(MapSacrificeAuthoring.Results);
        const string backup = "Library/MapSacrifice/Map-before-simple-gradient.unity";
        if (!File.Exists(backup)) File.Copy(ScenePath,backup);
        using (var saved = new SerializedObject(material))
        {
            var floats = saved.FindProperty("m_SavedProperties.m_Floats");
            float height = material.GetFloat("_ShadeHeight");
            bool hasHeight = Enumerable.Range(0,floats.arraySize).Any(i => floats.GetArrayElementAtIndex(i).FindPropertyRelative("first").stringValue == "_ShadeHeight");
            for (int i = floats.arraySize-1; i >= 0; i--)
            {
                var pair = floats.GetArrayElementAtIndex(i);
                string key = pair.FindPropertyRelative("first").stringValue;
                if (!hasHeight && key == "_ShadeStart") height = pair.FindPropertyRelative("second").floatValue;
                if (key == "_ShadeStart" || key == "_ShadeEnd" || key == "_TopShade") floats.DeleteArrayElementAtIndex(i);
            }
            saved.ApplyModifiedPropertiesWithoutUndo();
            material.SetFloat("_ShadeHeight",height);
        }
        material.SetFloat("_ShadeSoftness",material.GetFloat("_ShadeSoftness"));
        EditorUtility.SetDirty(material); AssetDatabase.SaveAssetIfDirty(material);
        EditorUtility.SetDirty(ui); EditorSceneManager.MarkSceneDirty(scene);
        if (!EditorSceneManager.SaveScene(scene)) throw new InvalidOperationException("Could not save the simplified gradient references.");
        float originalHeight = material.GetFloat("_ShadeHeight"), originalOpacity = material.GetFloat("_ShadeOpacity"), originalRadius = material.GetFloat("_CornerRadius");
        ui.ApplyArtworkStyle(material,ui.illustration.sprite,false,new Rect(0,0,1,1));
        if (material.GetFloat("_ShadeHeight") != originalHeight || material.GetFloat("_ShadeOpacity") != originalOpacity || material.GetFloat("_CornerRadius") != originalRadius ||
            material.HasProperty("_ShadeStart") || material.HasProperty("_ShadeEnd") || material.HasProperty("_TopShade"))
            throw new InvalidOperationException("The gradient still has obsolete controls or a scripted override.");
        VerifyHierarchyEditing();
        File.WriteAllText(MapSacrificeAuthoring.Results+"/simple-gradient.txt",
            "PASS: editable material corners, solid black coverage and adjustable feather; no component appearance overrides.\n");
    }
    [MenuItem("Tools/Map/Clean Obsolete Sacrifice Content")]
    public static void CleanupCurrent()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Stop Play Mode first.");
        var scene = SceneManager.GetActiveScene();
        if (scene.path != ScenePath) throw new InvalidOperationException("Open Map before cleaning its authored content.");
        var ui = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<MapSacrificePanel>(true)).Single();
        Undo.RegisterFullObjectHierarchyUndo(ui.gameObject,"Clean obsolete sacrifice content");
        var retained = new System.Collections.Generic.List<MapCardDetailText>();
        int removed = 0;
        foreach (var entry in ui.cardContent ?? Array.Empty<MapCardDetailText>())
        {
            bool empty = entry == null || (string.IsNullOrWhiteSpace(entry.title) &&
                string.IsNullOrWhiteSpace(entry.frontText) && string.IsNullOrWhiteSpace(entry.backText));
            if (empty && (entry?.card == null || retained.Any(existing => existing.card == entry.card))) { removed++; continue; }
            retained.Add(entry);
        }
        ui.cardContent = retained.ToArray();
        var pool = AssetDatabase.LoadAssetAtPath<CardPool>("Assets/Data/CardPool.asset");
        int registered = ui.cardContent.Count(entry => entry?.card != null && pool.Cards.Contains(entry.card));
        MapSacrificeAuthoring.Validate(scene);
        EditorUtility.SetDirty(ui);
        EditorSceneManager.MarkSceneDirty(scene);
        if (!EditorSceneManager.SaveScene(scene)) throw new InvalidOperationException("Could not save cleaned Map content.");
        string saved = File.ReadAllText(ScenePath);
        var block = System.Text.RegularExpressions.Regex.Matches(saved,@"(?ms)^--- !u!114 .*?(?=^---|\z)")
            .Cast<System.Text.RegularExpressions.Match>().Single(match => match.Value.Contains("Assembly-CSharp::MapSacrificePanel")).Value;
        var obsolete = new[] { "frame","categoryMedallion","brokenHeartCut","cost","benefitTitle","benefitValue",
            "benefitDetails","benefitLimit","capacity","effectIcon","close","cardOutline" };
        foreach (string field in obsolete)
            if (System.Text.RegularExpressions.Regex.IsMatch(block,@"(?m)^  "+field+":"))
                throw new InvalidOperationException("Obsolete serialized detail field survived: "+field);
        if (saved.Contains("  ignitionDuration:")) throw new InvalidOperationException("The removed ignition phase survived serialization.");
        Directory.CreateDirectory(MapSacrificeAuthoring.Results);
        File.WriteAllText(MapSacrificeAuthoring.Results+"/cleanup.txt","PASS: removed 12 obsolete detail fields and the ignition phase; "+removed+
            " empty or redundant content entries removed; "+registered+" registered card entries retained. Current Hierarchy edits saved without reopening Map.\n");
    }
    [MenuItem("Tools/Map/Enable Hierarchy Card Editing")]
    public static void ApplyCurrent()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Stop Play Mode first.");
        var scene = SceneManager.GetActiveScene();
        if (scene.path != ScenePath) { Apply(); return; }
        var ui = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<MapSacrificePanel>(true)).Single();
        Undo.RegisterFullObjectHierarchyUndo(ui.gameObject,"Enable Hierarchy card editing");
        var position = ui.panel.localPosition; var size = ui.panel.sizeDelta;
        bool changed = Migrate(ui);
        changed |= EnableHierarchyEditing(ui);
        if (ui.panel.localPosition != position || ui.panel.sizeDelta != size)
            throw new InvalidOperationException("Hierarchy editing must preserve the user's outer detail layout.");
        if (changed || scene.isDirty)
        {
            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene)) throw new InvalidOperationException("Could not save current Map edits.");
        }
        Selection.activeGameObject = ui.gameObject;
    }
    [MenuItem("Tools/Map/Apply Hover Card Details")]
    public static void Apply()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Stop Play Mode first.");
        for (int i=0;i<SceneManager.sceneCount;i++)
            if (SceneManager.GetSceneAt(i).isDirty) throw new InvalidOperationException("Save scene changes before migrating card details.");
        Directory.CreateDirectory(MapSacrificeAuthoring.Results);
        const string backup = "Library/MapSacrifice/Map-before-hover-details.unity";
        if (!File.Exists(backup)) File.Copy(ScenePath, backup);
        var setup = EditorSceneManager.GetSceneManagerSetup();
        try
        {
            var scene = EditorSceneManager.OpenScene(ScenePath);
            var ui = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<MapSacrificePanel>(true)).Single();
            bool changed = Migrate(ui);
            changed |= EnableHierarchyEditing(ui);
            if (changed) { EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene); }
            MapSacrificeAuthoring.Validate(scene);
        }
        finally { EditorSceneManager.RestoreSceneManagerSetup(setup); }
    }

    public static bool Migrate(MapSacrificePanel ui)
    {
        bool fadeChanged = EnsureDetailFade(ui);
        if (ui.detailVisual != null)
        {
            if (ui.panel.GetComponent<MapCardDetailPointer>() != null) return fadeChanged;
            ui.panel.gameObject.AddComponent<MapCardDetailPointer>().owner = ui;
            return true;
        }
        var shader = Shader.Find("Deinosavros/UI/Map Card Detail");
        if (shader == null || ShaderUtil.ShaderHasError(shader)) throw new InvalidOperationException("Map detail shader must compile.");
        const string path = "Assets/UI/MapCardDetail.mat";
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material == null)
        {
            material = new Material(shader) { name = "MapCardDetail" };
            AssetDatabase.CreateAsset(material,path);
        }
        var p = ui.panel;
        p.name = "Detail";
        var backdrop = p.GetComponent<UnityEngine.UI.Image>();
        backdrop.color = Color.clear; backdrop.raycastTarget = true;
        foreach (var outline in p.GetComponents<UnityEngine.UI.Outline>()) Object.DestroyImmediate(outline);
        // Retain the established left anchor; replace only its internal composition.
        p.sizeDelta = new Vector2(440,696);
        ui.detailVisual = Rect("DetailVisual",p,Vector2.zero,new Vector2(440,696));
        var visual = Rect("Card",ui.detailVisual,Vector2.zero,new Vector2(440,696));
        ui.cardPreview = visual;
        ui.cardGroup = visual.gameObject.AddComponent<CanvasGroup>();
        ui.illustration.transform.SetParent(visual,false);
        Stretch(ui.illustration.rectTransform);
        ui.illustration.name = "Artwork"; ui.illustration.material = material;
        ui.illustration.preserveAspect = false; ui.illustration.color = Color.white;
        ui.illustration.raycastTarget = false;
        ui.cardName.transform.SetParent(visual,false);
        Place(ui.cardName.rectTransform,new Vector2(0,280),new Vector2(376,92));
        Text(ui.cardName,32,28,true); ui.cardName.maxVisibleLines = 2;
        ui.combatText.transform.SetParent(visual,false);
        Place(ui.combatText.rectTransform,new Vector2(0,-105),new Vector2(376,252));
        Text(ui.combatText,26,22,false);
        ui.combatText.gameObject.SetActive(true);
        ui.confirm.transform.SetParent(ui.detailVisual,false);
        Place((RectTransform)ui.confirm.transform,new Vector2(0,-296),new Vector2(260,52));
        var buttonImage = ui.confirm.GetComponent<UnityEngine.UI.Image>();
        buttonImage.sprite = null; buttonImage.color = Color.clear; buttonImage.raycastTarget = true;
        foreach (var outline in ui.confirm.GetComponents<UnityEngine.UI.Outline>()) Object.DestroyImmediate(outline);
        var buttonText = ui.confirm.GetComponentInChildren<TMP_Text>(true);
        buttonText.text = "Sacrifice"; Text(buttonText,26,26,true);
        ui.confirm.targetGraphic = buttonText;
        var colors = ui.confirm.colors;
        colors.normalColor = new Color(.94f,.91f,.84f); colors.highlightedColor = Color.white;
        colors.pressedColor = ui.accentGold; colors.disabledColor = new Color(.55f,.54f,.51f);
        ui.confirm.colors = colors;
        ui.reason.transform.SetParent(ui.detailVisual,false);
        Place(ui.reason.rectTransform,new Vector2(0,-249),new Vector2(388,36));
        Text(ui.reason,20,18,false); ui.reason.text = ""; ui.reason.gameObject.SetActive(false);
        // References have moved before any obsolete preview/decorative subtree is removed.
        foreach (Transform child in p.Cast<Transform>().ToArray())
            if (child != ui.detailVisual) Object.DestroyImmediate(child.gameObject);
        foreach (var graphic in visual.GetComponentsInChildren<UnityEngine.UI.Graphic>(true)) graphic.raycastTarget = false;
        var shade = ui.overlay.GetComponent<UnityEngine.UI.Image>();
        shade.color = new Color(.06f,.045f,.032f,0); shade.raycastTarget = false;
        ui.feedbackBackdrop = shade;
        if (ui.burnEffect != null) ui.burnEffect.focusScale = 1.04f;
        ui.overlay.SetActive(false);
        p.gameObject.AddComponent<MapCardDetailPointer>().owner = ui;
        EditorUtility.SetDirty(ui);
        return true;
    }

    public static bool EnableHierarchyEditing(MapSacrificePanel ui)
    {
        if (ui.hierarchyEditingVersion >= 1) return false;
        Stretch(ui.detailVisual); Stretch(ui.cardPreview);
        StretchTextWidth(ui.cardName.rectTransform,1);
        StretchTextWidth(ui.combatText.rectTransform,.5f);
        AnchorAtBottom((RectTransform)ui.confirm.transform);
        AnchorAtBottom(ui.reason.rectTransform);
        ui.cardName.name = "Name"; ui.combatText.name = "Effect";
        ui.confirm.name = "Sacrifice";
        var material = ui.illustration.material;
        var pool = AssetDatabase.LoadAssetAtPath<CardPool>("Assets/Data/CardPool.asset");
        if (ui.cardContent == null || ui.cardContent.Length == 0)
            ui.cardContent = pool.Cards.Select(card => new MapCardDetailText { card = card }).ToArray();
        if (ui.previewCard == null) ui.previewCard = AssetDatabase.LoadAssetAtPath<CardDefinition>("Assets/Data/Cards/SolarShield.asset");
        ui.hierarchyEditingVersion = 1;
        Canvas.ForceUpdateCanvases();
        ui.PreviewEditorCard(false);
        EditorUtility.SetDirty(ui); EditorUtility.SetDirty(material); AssetDatabase.SaveAssetIfDirty(material);
        return true;
    }

    [MenuItem("Tools/Map/Verify Hierarchy Card Editing")]
    public static void VerifyHierarchyEditing()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Run hierarchy checks in Edit Mode.");
        var scene = EditorSceneManager.OpenPreviewScene(ScenePath);
        Material clone = null;
        int checks = 0;
        void Check(bool value,string message) { if (!value) throw new InvalidOperationException(message); checks++; }
        try
        {
            var ui = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<MapSacrificePanel>(true)).Single();
            Check(ui.hierarchyEditingVersion == 1 && ui.previewCard != null,"Saved preview references are missing.");
            Check(ui.cardContent.Length == AssetDatabase.LoadAssetAtPath<CardPool>("Assets/Data/CardPool.asset").Cards.Count,
                "Every registered card must have an Inspector content entry.");
            clone = Object.Instantiate(ui.illustration.material); ui.illustration.material = clone;
            var card = AssetDatabase.LoadAssetAtPath<CardDefinition>("Assets/Data/Cards/SolarShield.asset");
            string original = EditorJsonUtility.ToJson(card);
            ui.previewCard = card;
            var entry = ui.cardContent.Single(content => content.card == card);
            entry.title = "Hierarchy Preview"; entry.frontText = "Gain {amount} Shield"; entry.backText = "Gain {magnitude} Shield";
            ui.combatText.enableAutoSizing = false; ui.combatText.fontSize = 37; ui.combatText.color = Color.cyan;
            clone.SetFloat("_CornerRadius",72); clone.SetFloat("_ShadeOpacity",.65f); clone.SetFloat("_ShadeHeight",.35f);
            Canvas.ForceUpdateCanvases();
            float width = ui.cardPreview.rect.width, titleWidth = ui.cardName.rectTransform.rect.width;
            ui.panel.sizeDelta += new Vector2(80,24); Canvas.ForceUpdateCanvases();
            Check(Mathf.Approximately(ui.cardPreview.rect.width,width+80) &&
                Mathf.Approximately(ui.cardName.rectTransform.rect.width,titleWidth+80),"Resizing Detail must resize its artwork and title area.");
            ui.PreviewEditorCard(false);
            Check(ui.cardName.text == entry.title && ui.combatText.text == "Gain 6 Shield","Front overrides must resolve live numeric tokens.");
            ui.PreviewEditorCard(true);
            Check(ui.combatText.text == "Gain 5 Shield" && clone.GetFloat("_Mirror") == 1,"Back preview must use offering values and mirrored art.");
            Check(ui.combatText.fontSize == 37 && ui.combatText.color == Color.cyan,"Refreshing content must preserve Hierarchy typography.");
            Check(clone.GetFloat("_CornerRadius") == 72 && Mathf.Approximately(clone.GetFloat("_ShadeOpacity"),.65f) &&
                Mathf.Approximately(clone.GetFloat("_ShadeHeight"),.35f),
                "Inspector artwork parameters must reach the actual UI shader.");
            Check(EditorJsonUtility.ToJson(card) == original,"Detail text overrides cannot change card definitions or gameplay values.");
            entry.title = entry.frontText = entry.backText = "";
            Check(ui.DetailName(card) == card.displayName && ui.DetailEffect(card,false) == card.GetCombatDescription() &&
                ui.DetailEffect(card,true) == card.GetSacrificeDescription(),"Empty overrides must retain the original card content.");
            Directory.CreateDirectory(MapSacrificeAuthoring.Results);
            string hierarchy = ui.name;
            for (var parent = ui.transform.parent; parent != null; parent = parent.parent) hierarchy = parent.name+"/"+hierarchy;
            File.WriteAllText(MapSacrificeAuthoring.Results+"/hierarchy-editing.txt","PASS: "+checks+
                " checks; saved card entries, edit previews, responsive size, typography preservation, numeric templates, shader controls and unchanged gameplay data.\nHierarchy: "+hierarchy+"\n");
        }
        finally
        {
            EditorSceneManager.ClosePreviewScene(scene);
            if (clone != null) Object.DestroyImmediate(clone);
        }
    }
    private static void StretchTextWidth(RectTransform rect,float verticalAnchor)
    {
        var parent = (RectTransform)rect.parent;
        var position = rect.localPosition; var size = rect.rect.size;
        rect.anchorMin = new Vector2(0,verticalAnchor); rect.anchorMax = new Vector2(1,verticalAnchor);
        rect.sizeDelta = new Vector2(size.x-parent.rect.width,size.y);
        rect.anchoredPosition = new Vector2(position.x,position.y-(verticalAnchor-.5f)*parent.rect.height);
    }
    private static void AnchorAtBottom(RectTransform rect)
    {
        var position = rect.localPosition;
        rect.anchorMin = rect.anchorMax = new Vector2(.5f,0);
        rect.anchoredPosition = new Vector2(position.x,position.y+((RectTransform)rect.parent).rect.height*.5f);
    }

    private static RectTransform Rect(string name, Transform parent, Vector2 position, Vector2 size)
    {
        var root = new GameObject(name,typeof(RectTransform)); root.layer = parent.gameObject.layer;
        root.transform.SetParent(parent,false);
        var rect = (RectTransform)root.transform; Place(rect,position,size); return rect;
    }
    private static void Place(RectTransform rect, Vector2 position, Vector2 size)
    {
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(.5f,.5f);
        rect.anchoredPosition3D = position; rect.sizeDelta = size;
        rect.localScale = Vector3.one; rect.localRotation = Quaternion.identity;
    }
    private static void Stretch(RectTransform rect)
    { rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.offsetMin = rect.offsetMax = Vector2.zero; rect.localScale = Vector3.one; }
    private static void Text(TMP_Text text,float size,float min,bool heading)
    {
        text.font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(heading ? "Assets/Resources/Fonts/Cinzel-Black SDF.asset" : "Assets/Resources/Fonts/Alegreya SDF.asset");
        text.fontSize = text.fontSizeMax = size; text.fontSizeMin = min; text.enableAutoSizing = size > min;
        text.fontStyle = FontStyles.Normal; text.alignment = TextAlignmentOptions.Center;
        text.color = new Color(.98f,.97f,.94f); text.raycastTarget = false;
        text.overflowMode = TextOverflowModes.Overflow;
    }
}
