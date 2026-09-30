using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class MapBattleUiRepair
{
    private const string ScenePath = "Assets/Scenes/Deinosavros.unity";
    private const string Evidence = "Library/MapReviewEvidence/2026-09-30/BattleUI";

    [MenuItem("Tools/Map/Repair Battle UI Bindings")]
    public static void Apply()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play Mode before repairing battle bindings.");
        for (int i = 0; i < SceneManager.sceneCount; i++)
            if (SceneManager.GetSceneAt(i).isDirty) throw new InvalidOperationException("Save the open scene before repairing battle bindings.");
        var setup = EditorSceneManager.GetSceneManagerSetup();
        Directory.CreateDirectory(Evidence);
        try
        {
            var scene = SceneManager.GetSceneByPath(ScenePath);
            if (!scene.IsValid() || !scene.isLoaded) scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
            SceneManager.SetActiveScene(scene);
            string backup = Evidence + "/Deinosavros-before-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff") + ".unity";
            if (!EditorSceneManager.SaveScene(scene, backup, true)) throw new IOException("Could not back up the battle scene.");
            var transforms = Objects<Transform>(scene).ToDictionary(t => t, t => EditorJsonUtility.ToJson(t));
            var colliders = Objects<Collider>(scene).ToDictionary(c => c, c => EditorJsonUtility.ToJson(c));
            var actors = Objects<BattleScript>(scene).ToDictionary(a => a, a => EditorJsonUtility.ToJson(a));
            var canvas = Objects<Canvas>(scene).Single(c => c.isRootCanvas);
            var hand = Objects<Transform>(scene).Single(t => t.name == "CardHolder");
            hand.gameObject.SetActive(true);
            var deck = hand.GetComponent<DeckManager>();
            if (deck == null) deck = hand.gameObject.AddComponent<DeckManager>();
            var deckData = new SerializedObject(deck);
            deckData.FindProperty("handContainer").objectReferenceValue = hand;
            var oldCards = hand.GetComponentsInChildren<BuffCards>(true).Select(c => c.gameObject).ToArray();
            SetArray(deckData.FindProperty("legacyCards"), oldCards);
            deckData.ApplyModifiedPropertiesWithoutUndo();
            foreach (var label in Objects<HealthLabel>(scene)) BindCanvas(label, canvas);
            foreach (var label in Objects<TempStatDisplay>(scene)) BindCanvas(label, canvas);
            var hud = canvas.GetComponent<BattleHud>();
            if (hud == null) hud = canvas.gameObject.AddComponent<BattleHud>();
            var hudData = new SerializedObject(hud);
            hudData.FindProperty("targetCanvas").objectReferenceValue = canvas;
            hudData.FindProperty("deck").objectReferenceValue = deck;
            hudData.FindProperty("playerStats").objectReferenceValue = Objects<TempStatDisplay>(scene).Single(s => s.enabled && s.CompareTag("Player"));
            SetArray(hudData.FindProperty("healthLabels"), Objects<HealthLabel>(scene).Where(h => h.enabled).ToArray());
            hudData.ApplyModifiedPropertiesWithoutUndo();
            foreach (var pair in transforms) Require(EditorJsonUtility.ToJson(pair.Key) == pair.Value, "A pre-existing transform changed.");
            foreach (var pair in colliders) Require(EditorJsonUtility.ToJson(pair.Key) == pair.Value, "A collider changed.");
            foreach (var pair in actors) Require(EditorJsonUtility.ToJson(pair.Key) == pair.Value, "Combat values changed.");
            Require(Objects<DeckManager>(scene).Count() == 1 && Objects<BattleHud>(scene).Count() == 1, "Duplicate battle bindings.");
            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene)) throw new IOException("Battle bindings could not be saved.");
            File.WriteAllText(Evidence + "/installation.txt", "PASS: active hand container; one deck manager; explicit scene-owned canvas references. " +
                transforms.Count + " transforms, " + colliders.Count + " colliders and " + actors.Count + " combat components unchanged. Legacy cards retained for direct-scene preview. Backup: " + backup);
        }
        finally { EditorSceneManager.RestoreSceneManagerSetup(setup); }
    }
    private static void BindCanvas(Component component, Canvas canvas)
    {
        var data = new SerializedObject(component); data.FindProperty("battleCanvas").objectReferenceValue = canvas;
        data.ApplyModifiedPropertiesWithoutUndo();
    }
    private static void SetArray(SerializedProperty property, UnityEngine.Object[] values)
    {
        property.arraySize = values.Length;
        for (int i = 0; i < values.Length; i++) property.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
    }
    private static IEnumerable<T> Objects<T>(Scene scene) where T : Component =>
        scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<T>(true));
    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
}
