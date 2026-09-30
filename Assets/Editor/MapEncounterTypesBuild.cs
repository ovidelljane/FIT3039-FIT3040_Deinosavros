using System;
using System.IO;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class MapEncounterTypesBuild
{
    public const string Evidence = "Library/MapReviewEvidence/2026-09-30/EncounterTypes";
    public const string RoutingPath = "Assets/Art/MapTravel/EncounterRouting.asset";
    public static readonly string[] Scenes = { "Assets/Scenes/Map.unity", "Assets/Scenes/Deinosavros.unity",
        "Assets/Scenes/MapOpportunity.unity", "Assets/Scenes/MapRecovery.unity" };

    [MenuItem("Tools/Map/Verify Encounter Type Reapply")]
    public static void VerifyReapply()
    {
        var paths = Scenes.Concat(new[] { RoutingPath, "Assets/Art/MapTravel/NodeInformation.asset", "Assets/Art/MapTravel/TravelSymbols.png", "ProjectSettings/EditorBuildSettings.asset" }).ToArray();
        var before = paths.ToDictionary(p => p, File.ReadAllBytes);
        Install(); Install();
        EditorSceneManager.OpenScene(Scenes[0], OpenSceneMode.Single);
        foreach (var entry in before)
            if (!entry.Value.SequenceEqual(File.ReadAllBytes(entry.Key))) throw new InvalidOperationException("Reapplication changed saved data: " + entry.Key);
        var controller = UnityEngine.Object.FindFirstObjectByType<MapController>();
        if (controller == null || controller.EncounterRouting == null || controller.Nodes.Length != 14 || controller.TravelView.paths.Length != 19)
            throw new InvalidOperationException("Saved Map references did not survive reload.");
        foreach (var path in Scenes.Skip(2))
        {
            var scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
            var receivers = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<MapNonCombatController>(true)).ToArray();
            if (receivers.Length != 1 || receivers[0].routing == null || receivers[0].symbols == null || receivers[0].font == null)
                throw new InvalidOperationException("Receiver references did not survive reload.");
            EditorSceneManager.CloseScene(scene, true);
        }
        File.WriteAllText(Evidence + "/reapply-reload.txt", "PASS: two applications and saved reopen; four scenes, catalog, routing, atlas and build list byte-identical. Fourteen nodes, nineteen roads and receiver references retained.");
    }

    [MenuItem("Tools/Map/Install Encounter Types")]
    public static void Install()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play Mode first.");
        for (int i = 0; i < SceneManager.sceneCount; i++)
            if (SceneManager.GetSceneAt(i).isDirty) throw new InvalidOperationException("Save unsaved scene changes first.");
        var map = SceneManager.GetActiveScene();
        if (map.path != Scenes[0]) throw new InvalidOperationException("Open the formal Map first.");
        Directory.CreateDirectory(Evidence);
        if (!EditorSceneManager.SaveScene(map, Evidence + "/Map-before-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff") + ".unity", true))
            throw new IOException("Could not preserve the live Map.");
        var protectedObjects = map.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Component>(true))
            .Where(c => c is Transform || c is Collider || c is MeshFilter || c is Camera || c is MapTravelPath)
            .ToDictionary(c => c, EditorJsonUtility.ToJson);
        var controller = map.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<MapController>(true)).Single();
        var font = map.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<TMP_Text>(true)).First(t => t.font != null).font;
        var profile = MapTravelArt.CreateAssets();
        ApplyLayout(profile.nodeInformation);
        var routing = AssetDatabase.LoadAssetAtPath<MapEncounterRouting>(RoutingPath);
        if (routing == null) { routing = ScriptableObject.CreateInstance<MapEncounterRouting>(); AssetDatabase.CreateAsset(routing, RoutingPath); }
        routing.nodes = profile.nodeInformation; routing.cardPool = AssetDatabase.LoadAssetAtPath<CardPool>("Assets/Data/CardPool.asset");
        routing.battleScene = "Deinosavros"; routing.opportunityScene = "MapOpportunity"; routing.recoveryScene = "MapRecovery";
        EditorUtility.SetDirty(routing); AssetDatabase.SaveAssetIfDirty(routing);
        var data = new SerializedObject(controller); data.FindProperty("encounterRouting").objectReferenceValue = routing;
        data.ApplyModifiedPropertiesWithoutUndo();
        foreach (var pair in protectedObjects)
            if (EditorJsonUtility.ToJson(pair.Key) != pair.Value) throw new InvalidOperationException("Protected scene data changed: " + pair.Key.name);
        EditorSceneManager.MarkSceneDirty(map); EditorSceneManager.SaveScene(map);
        var symbols = profile.symbolMaterial.GetTexture("_BaseMap") as Texture2D;
        if (symbols == null) throw new InvalidOperationException("The encounter symbol atlas is missing.");
        CreateScene(Scenes[2], MapEncounterKind.Opportunity, routing, font, symbols);
        CreateScene(Scenes[3], MapEncounterKind.Recovery, routing, font, symbols);
        SceneManager.SetActiveScene(map);
        var builds = EditorBuildSettings.scenes.ToList();
        foreach (var path in Scenes.Skip(2))
        {
            var existing = builds.FirstOrDefault(s => s.path == path);
            if (existing == null) builds.Add(new EditorBuildSettingsScene(path, true)); else existing.enabled = true;
        }
        EditorBuildSettings.scenes = builds.ToArray();
        File.WriteAllText(Evidence + "/installation.txt", "PASS: 7 Battle, 3 Opportunity, 3 Recovery, 1 Boss. " +
            protectedObjects.Count + " existing transforms, colliders, meshes, cameras and route payloads unchanged. Two independent UI scenes and one routing asset installed.");
    }
    private static void ApplyLayout(MapNodeCatalog catalog)
    {
        var opportunity = new[] { "level_02_01", "level_04_03", "level_05_02" };
        var recovery = new[] { "level_02_02", "level_04_01", "level_05_03" };
        foreach (var node in catalog.nodes)
        {
            node.kind = node.nodeId == "level_06_01" ? MapEncounterKind.Boss : opportunity.Contains(node.nodeId) ? MapEncounterKind.Opportunity :
                recovery.Contains(node.nodeId) ? MapEncounterKind.Recovery : MapEncounterKind.Battle;
            node.title = node.kind switch { MapEncounterKind.Opportunity => "Opportunity", MapEncounterKind.Recovery => "Recovery",
                MapEncounterKind.Boss => "The Sanctuary", _ => node.nodeId == "level_01_01" ? "The Entrance" : "An Ancient Trial" };
            node.summary = node.kind switch
            {
                MapEncounterKind.Opportunity => "No cards available yet.",
                MapEncounterKind.Recovery => "Restore 15% of max HP.",
                MapEncounterKind.Boss => "Enter the portal to face the final battle. Victory completes this adventure.",
                _ => "Defeat the enemies to complete this battle and open its connected paths."
            };
            node.enemies = ""; node.rewards = "";
        }
        catalog.Validate(AssetDatabase.LoadAssetAtPath<Deinosavros.MapReview.MapGraphDefinition>("Assets/MapReview/MapGraph.asset"));
        EditorUtility.SetDirty(catalog); AssetDatabase.SaveAssetIfDirty(catalog);
    }
    private static void CreateScene(string path, MapEncounterKind kind, MapEncounterRouting routing, TMP_FontAsset font, Texture2D symbols)
    {
        // Reapplication must not overwrite an independently edited receiver scene.
        if (File.Exists(path))
        {
            var existing = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
            var existingReceiver = existing.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<MapNonCombatController>(true)).Single();
            if (existingReceiver.kind != kind) throw new InvalidOperationException("The receiver scene has a different encounter type.");
            if (existingReceiver.symbols == null) { existingReceiver.symbols = symbols; EditorSceneManager.MarkSceneDirty(existing); EditorSceneManager.SaveScene(existing); }
            EditorSceneManager.CloseScene(existing, true); return;
        }
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        SceneManager.SetActiveScene(scene);
        var root = new GameObject("Encounter"); var receiver = root.AddComponent<MapNonCombatController>();
        receiver.kind = kind; receiver.routing = routing; receiver.font = font; receiver.symbols = symbols;
        var camera = new GameObject("Camera").AddComponent<Camera>(); camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(.075f, .085f, .073f); camera.cullingMask = 0; camera.gameObject.tag = "MainCamera";
        EditorSceneManager.SaveScene(scene, path); EditorSceneManager.CloseScene(scene, true);
    }
}
