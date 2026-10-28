using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

[CustomEditor(typeof(MapSacrificePanel))]
public sealed class MapCardDetailInspector : Editor
{
    private bool detail = true, ceremony = true, colors, bindings;
    public override void OnInspectorGUI()
    {
        var ui = (MapSacrificePanel)target;
        serializedObject.Update();
        Fields("previewCard");
        using (new EditorGUI.DisabledScope(Application.isPlaying || ui.IsTimelinePreview))
        {
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Preview Front")) Preview(ui,false);
            if (GUILayout.Button("Preview Back")) Preview(ui,true);
            if (GUILayout.Button("Hide Preview"))
            {
                Undo.RecordObject(ui.overlay,"Hide card preview");
                ui.overlay.SetActive(false); EditorSceneManager.MarkSceneDirty(ui.gameObject.scene);
            }
            EditorGUILayout.EndHorizontal();
        }
        EditorGUI.BeginChangeCheck();
        detail = EditorGUILayout.Foldout(detail,"Card Detail",true);
        if (detail)
        {
            Fields("artworkFocus","cardContent","hideDelay","hoverSwitchDelay","detailTransferTime","detailTransferPadding","hoverMotionTolerance");
            DrawGradient(ui);
        }
        ceremony = EditorGUILayout.Foldout(ceremony,"Sacrifice",true);
        if (ceremony) Fields("sequenceDirector","dimOpacity","transferArc","transferEndScale","deckSettleDuration");
        colors = EditorGUILayout.Foldout(colors,"Colors",true);
        if (colors) Fields("bodyColor","accentGold","warningColor","attackSpeedColor","shieldColor","enemyHealthColor","elixirColor");
        bindings = EditorGUILayout.Foldout(bindings,"Bindings",true);
        if (bindings) Fields("overlay","panel","cardPreview","cardGroup","illustration","cardName","combatText","reason","statusText","statusDetails",
            "flightIcon","flightHeartCut","confirm","statusButton","statusPopup","burnEffect","playerSymbol","feedbackBackdrop","detailVisual",
            "departureWarning","departurePanel","departureMessage","departureContinue","departureBack");
        bool changed = EditorGUI.EndChangeCheck();
        serializedObject.ApplyModifiedProperties();
        if (changed && !Application.isPlaying)
        {
            if (ui.IsTimelinePreview) ui.sequenceDirector.Evaluate();
            else if (ui.IsOpen) Preview(ui,ui.previewBack);
        }
    }
    private void Fields(params string[] names)
    { foreach (string name in names) EditorGUILayout.PropertyField(serializedObject.FindProperty(name),true); }
    private static void DrawGradient(MapSacrificePanel ui)
    {
        var material = ui.DetailMaterial;
        if (material == null || !material.HasProperty("_ShadeHeight")) return;
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Artwork Appearance",EditorStyles.boldLabel);
        EditorGUI.BeginChangeCheck();
        float radius = Mathf.Max(0,EditorGUILayout.FloatField("Corner Radius",material.GetFloat("_CornerRadius")));
        EditorGUILayout.LabelField("Black Gradient",EditorStyles.boldLabel);
        float height = EditorGUILayout.Slider("Height",material.GetFloat("_ShadeHeight"),0,1);
        float softness = EditorGUILayout.Slider("Softness",material.GetFloat("_ShadeSoftness"),0,1);
        float opacity = EditorGUILayout.Slider("Opacity",material.GetFloat("_ShadeOpacity"),0,1);
        if (EditorGUI.EndChangeCheck())
        {
            Undo.RecordObject(material,"Edit card gradient");
            material.SetFloat("_CornerRadius",radius); material.SetFloat("_ShadeHeight",height);
            material.SetFloat("_ShadeSoftness",softness); material.SetFloat("_ShadeOpacity",opacity);
            EditorUtility.SetDirty(material);
            SceneView.RepaintAll();
        }
        EditorGUILayout.Space();
    }
    private static void Preview(MapSacrificePanel ui,bool back)
    {
        if (ui.previewCard == null) return;
        Undo.RecordObjects(new Object[]{ui,ui.overlay,ui.illustration,ui.cardName,ui.combatText,ui.illustration.material},"Preview Map card");
        ui.PreviewEditorCard(back);
        EditorUtility.SetDirty(ui); EditorUtility.SetDirty(ui.illustration.material);
        EditorSceneManager.MarkSceneDirty(ui.gameObject.scene); SceneView.RepaintAll();
    }
}

[CustomPropertyDrawer(typeof(MapCardDetailText))]
public sealed class MapCardDetailTextDrawer : PropertyDrawer
{
    private static readonly string[] Fields = { "card","title","frontText","backText" };
    public override float GetPropertyHeight(SerializedProperty property,GUIContent label)
    {
        float height = EditorGUIUtility.singleLineHeight;
        if (property.isExpanded)
            foreach (string name in Fields) height += EditorGUI.GetPropertyHeight(property.FindPropertyRelative(name),true)+3;
        return height;
    }
    public override void OnGUI(Rect position,SerializedProperty property,GUIContent label)
    {
        var card = property.FindPropertyRelative("card").objectReferenceValue as CardDefinition;
        var line = new Rect(position.x,position.y,position.width,EditorGUIUtility.singleLineHeight);
        property.isExpanded = EditorGUI.Foldout(line,property.isExpanded,card != null ? card.displayName : label.text,true);
        if (!property.isExpanded) return;
        EditorGUI.indentLevel++;
        foreach (string name in Fields)
        {
            var child = property.FindPropertyRelative(name);
            line.y += line.height+3; line.height = EditorGUI.GetPropertyHeight(child,true);
            EditorGUI.PropertyField(line,child,true);
        }
        EditorGUI.indentLevel--;
    }
}
