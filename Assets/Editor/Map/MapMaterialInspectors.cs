using UnityEditor;
using UnityEngine;

public abstract class MapMaterialShaderGUI : ShaderGUI
{
    protected MaterialEditor editor;
    protected MaterialProperty[] properties;
    protected bool texturesOpen, advancedOpen;
    protected MaterialProperty P(string name) => FindProperty(name,properties);
    protected void Field(string name,string label) => editor.ShaderProperty(P(name),label);
    protected void Texture(string name,string label) => editor.TexturePropertySingleLine(new GUIContent(label),P(name));
    protected void Direction(string name,string label,int secondAxis)
    {
        var property=P(name);var value=property.vectorValue;EditorGUI.showMixedValue=property.hasMixedValue;
        EditorGUI.BeginChangeCheck();
        var direction=EditorGUILayout.Vector2Field(label,new Vector2(value.x,value[secondAxis]));
        if(EditorGUI.EndChangeCheck())
        { editor.RegisterPropertyChangeUndo(label);value.x=direction.x;value[secondAxis]=direction.y;property.vectorValue=value; }
        EditorGUI.showMixedValue=false;
    }
}

public sealed class MapFoliageShaderGUI : MapMaterialShaderGUI
{
    public override void OnGUI(MaterialEditor materialEditor,MaterialProperty[] props)
    {
        editor = materialEditor; properties = props;
        Field("_BaseColor","Tint"); Field("_Smoothness","Smoothness");
        if (P("_LeafPalette").floatValue > .5f)
        {
            Field("_LeafColorA","Mature Leaves"); Field("_LeafColorB","New Leaves");
        }
        Field("_WindStrength","Wind Strength"); Field("_WindSpeed","Wind Speed");
        texturesOpen = EditorGUILayout.Foldout(texturesOpen,"Textures",true);
        if (texturesOpen)
        {
            Field("_UseTextures","Use Leaf Atlas");
            if (P("_UseTextures").floatValue > .5f)
            {
                Texture("_BaseMap","Color and Cutout"); Texture("_NormalMap","Normal"); Texture("_RoughnessMap","Roughness");
                Field("_Cutoff","Cutout"); Field("_NormalStrength","Normal Strength");
                Field("_ReferenceRoughness","Reference Roughness"); Field("_RoughnessVariation","Roughness Influence");
            }
        }
        advancedOpen = EditorGUILayout.Foldout(advancedOpen,"Advanced Wind and Rendering",true);
        if (!advancedOpen) return;
        Direction("_WindDirection","Wind Direction (XZ)",2); Field("_WindFlutter","Leaf Flutter");
        Field("_WindSpatialScale","Wind Variation"); Field("_WindRootHeight","World Root Height"); Field("_WindBendHeight","Bend Height");
        Field("_Cull","Visible Sides"); editor.RenderQueueField(); editor.EnableInstancingField();
    }
}
