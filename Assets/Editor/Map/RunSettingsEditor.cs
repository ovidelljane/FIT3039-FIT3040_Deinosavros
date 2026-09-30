using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(RunSettings))]
public sealed class RunSettingsEditor : Editor
{
    public override void OnInspectorGUI()
    {
        serializedObject.Update();
        EditorGUILayout.PropertyField(serializedObject.FindProperty("configuration"));
        serializedObject.ApplyModifiedProperties();
        var settings = (RunSettings)target;
        if (settings.configuration == null) return;
        EditorGUILayout.HelpBox("Shared by MainMenu, Map and Combat. Edits persist in the shared asset. Starting Max Health applies to new runs; current run HP is not reset.", MessageType.Info);
        using var data = new SerializedObject(settings.configuration);
        data.Update();
        EditorGUILayout.PropertyField(data.FindProperty("startingMaxHealth"));
        EditorGUILayout.PropertyField(data.FindProperty("deckCapacity"));
        EditorGUILayout.PropertyField(data.FindProperty("recoveryPercent"));
        EditorGUILayout.PropertyField(data.FindProperty("minimumOfferingAttackInterval"));
        EditorGUILayout.Space();
        EditorGUILayout.PropertyField(data.FindProperty("scaleEnemyStatsByLayer"));
        using (new EditorGUI.DisabledScope(!data.FindProperty("scaleEnemyStatsByLayer").boolValue))
        {
            EditorGUILayout.PropertyField(data.FindProperty("enemyHealthGrowthPercent"));
            EditorGUILayout.PropertyField(data.FindProperty("enemyDamageGrowthPercent"));
        }
        EditorGUILayout.HelpBox("Difficulty uses the destination map layer, including opportunity battles. Layer 1 is unchanged. Growth is linear and applies when the next combat starts, before offerings; attack speed and enemy count stay unchanged.", MessageType.Info);
        data.ApplyModifiedProperties();
    }
}
