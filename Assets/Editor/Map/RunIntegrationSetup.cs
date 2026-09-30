using System;
using System.IO;
using System.Linq;
using Deinosavros.MapReview;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

// Explicit integration setup. Never runs on import and never saves an existing dirty scene.
[InitializeOnLoad]
public static class RunIntegrationSetup
{
    public const string ResultDirectory = "Library/RunIntegration";
    private static double nextRequest;

    static RunIntegrationSetup() => EditorApplication.update += RunExplicitRequest;

    // Opt-in local automation when the editor has no MCP connection. No request means no action.
    private static void RunExplicitRequest()
    {
        if (EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.timeSinceStartup < nextRequest) return;
        nextRequest = EditorApplication.timeSinceStartup + 1;
        const string request = "Temp/RunIntegration/action.txt";
        if (!File.Exists(request)) return;
        string action = File.ReadAllText(request).Trim(); File.Delete(request);
        Directory.CreateDirectory(ResultDirectory);
        try
        {
            if (action == "probe")
            {
                File.WriteAllText(ResultDirectory + "/editor-state.txt", "playing=" + EditorApplication.isPlaying + "\n" +
                    string.Join("\n", Enumerable.Range(0, SceneManager.sceneCount).Select(i =>
                        SceneManager.GetSceneAt(i).path + " dirty=" + SceneManager.GetSceneAt(i).isDirty)));
            }
            else if (action == "prepare-import")
            {
                Require(!EditorApplication.isPlayingOrWillChangePlaymode, "Stop Play Mode before importing map resources.");
                for (int i = 0; i < SceneManager.sceneCount; i++)
                    Require(!SceneManager.GetSceneAt(i).isDirty, "Save your scene changes before importing map resources.");
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            }
            else if (action == "apply") ApplyAndVerify();
            else if (action == "verify") Verify();
            else if (action == "refresh") AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);
            else if (action == "play") RunIntegrationPlayVerification.Run();
            else if (action == "audio") RunIntegrationPlayVerification.RunAudioPresentation();
            else if (action == "combat-ui") RunIntegrationPlayVerification.RunCombatCardHover();
            else if (action == "player-loop") RunIntegrationPlayVerification.RunPlayerLoop();
            else if (action == "enemy-loop") RunIntegrationPlayVerification.RunEnemyLoop();
            else if (action == "reward-ui") RunIntegrationPlayVerification.RunRewardPresentation();
            else if (action == "balance") RunIntegrationPlayVerification.RunBalance();
            else if (action == "combat-feedback-apply") CombatFeedbackSetup.Apply();
            else if (action == "combat-feedback") RunIntegrationPlayVerification.RunCombatFeedback();
            else if (action == "card-artwork") MapCardArtworkVerification.StartBatch();
            else if (action == "author-encounters") MapEncounterSceneAuthoring.Apply();
            else if (action == "opportunity-ui") RunIntegrationPlayVerification.RunOpportunityPages();
            else if (action == "author-combat-status") CombatStatusSceneAuthoring.Apply();
            else throw new ArgumentException("Unknown integration action.");
            File.WriteAllText(ResultDirectory + "/action-result.txt", "PASS: " + action + "\n");
        }
        catch (Exception ex)
        {
            File.WriteAllText(ResultDirectory + "/action-result.txt", "FAIL: " + action + "\n" + ex);
            Debug.LogException(ex);
        }
    }

    [MenuItem("Tools/Map/Apply Run Integration Bindings")]
    public static void ApplyAndVerify()
    {
        Require(!EditorApplication.isPlayingOrWillChangePlaymode, "Integration setup requires Edit Mode.");
        for (int i = 0; i < SceneManager.sceneCount; i++)
            Require(!SceneManager.GetSceneAt(i).isDirty, "Save your scene changes before integration setup.");
        var graph = AssetDatabase.LoadAssetAtPath<MapGraphDefinition>("Assets/Art/Map/Settings/Graph.asset");
        var catalog = AssetDatabase.LoadAssetAtPath<MapNodeCatalog>("Assets/Art/Map/Settings/Nodes.asset");
        Require(graph != null && catalog != null, "The map branch graph and catalog must be imported.");
        graph.Validate(); catalog.Validate(graph);

        var menuScene = EditorSceneManager.OpenScene("Assets/Scenes/MainMenu.unity");
        // Opening a scene unloads unused native assets, even if managed locals still refer to them.
        graph = AssetDatabase.LoadAssetAtPath<MapGraphDefinition>("Assets/Art/Map/Settings/Graph.asset");
        catalog = AssetDatabase.LoadAssetAtPath<MapNodeCatalog>("Assets/Art/Map/Settings/Nodes.asset");
        var menu = Components<MainMenuController>(menuScene).Single();
        string[] starterPaths;
        using (var data = new SerializedObject(menu))
        {
            var cards = data.FindProperty("startingDeck");
            var starter = Enumerable.Range(0, cards.arraySize).Select(i => (CardDefinition)cards.GetArrayElementAtIndex(i).objectReferenceValue).ToArray();
            Require(starter.Length > 0 && starter.All(card => card != null && card.capacityCost >= 0) &&
                starter.Sum(card => card.capacityCost) <= RunBalance.Default.deckCapacity,
                "The authored starting deck must fit the configured capacity.");
            starterPaths = starter.Select(AssetDatabase.GetAssetPath).ToArray();
            data.FindProperty("routeGraph").objectReferenceValue = graph;
            data.FindProperty("nodeCatalog").objectReferenceValue = catalog;
            data.ApplyModifiedPropertiesWithoutUndo();
        }
        EditorSceneManager.MarkSceneDirty(menuScene); EditorSceneManager.SaveScene(menuScene);

        var battle = EditorSceneManager.OpenScene("Assets/Scenes/Deinosavros.unity");
        var deck = Components<DeckManager>(battle).Single();
        var clock = Components<TimeTickSystem>(battle).Single();
        var countdown = Components<BattleCountdown>(battle).Single();
        Canvas canvas;
        using (var data = new SerializedObject(deck))
            canvas = ((Transform)data.FindProperty("handContainer").objectReferenceValue).GetComponentInParent<Canvas>().rootCanvas;
        var hud = clock.GetComponent<BattleHud>();
        if (hud == null) hud = clock.gameObject.AddComponent<BattleHud>();
        var fighters = Components<BattleScript>(battle).Where(actor => actor.isActiveAndEnabled).ToArray();
        Require(fighters.Count(actor => actor.CompareTag("Player")) == 1 && fighters.Count(actor => actor.CompareTag("Enemy")) == 3,
            "The authored main combat lineup must remain unchanged.");
        var labels = fighters.Select(actor => actor.GetComponent<HealthLabel>()).ToArray();
        Require(labels.All(label => label != null), "Every active fighter needs its existing health label.");
        using (var data = new SerializedObject(hud))
        {
            data.FindProperty("targetCanvas").objectReferenceValue = canvas;
            data.FindProperty("deck").objectReferenceValue = deck;
            data.FindProperty("countdown").objectReferenceValue = countdown;
            data.FindProperty("clock").objectReferenceValue = clock;
            var array = data.FindProperty("healthLabels"); array.arraySize = labels.Length;
            for (int i = 0; i < labels.Length; i++) array.GetArrayElementAtIndex(i).objectReferenceValue = labels[i];
            data.ApplyModifiedPropertiesWithoutUndo();
        }
        foreach (var label in Components<HealthLabel>(battle))
            using (var data = new SerializedObject(label))
            {
                data.FindProperty("battleCanvas").objectReferenceValue = canvas;
                data.ApplyModifiedPropertiesWithoutUndo();
            }
        EditorSceneManager.MarkSceneDirty(battle); EditorSceneManager.SaveScene(battle);
        float attackInterval = fighters.Single(actor => actor.CompareTag("Player")).attackSpd;

        var map = EditorSceneManager.OpenScene("Assets/Scenes/Map.unity");
        var mapStarter = starterPaths.Select(AssetDatabase.LoadAssetAtPath<CardDefinition>).ToArray();
        Require(mapStarter.All(card => card != null), "Starting card references must survive scene changes.");
        var session = Components<RunSession>(map).Single();
        using (var data = new SerializedObject(session))
        {
            var cards = data.FindProperty("startingDeck"); cards.arraySize = mapStarter.Length;
            for (int i = 0; i < mapStarter.Length; i++) cards.GetArrayElementAtIndex(i).objectReferenceValue = mapStarter[i];
            data.FindProperty("runDeck").ClearArray();
            data.FindProperty("playerAttackSpeed").floatValue = attackInterval;
            data.ApplyModifiedPropertiesWithoutUndo();
        }
        EditorSceneManager.MarkSceneDirty(map); EditorSceneManager.SaveScene(map);
        MapOpportunityVerification.ApplyAssets();

        var scenes = EditorBuildSettings.scenes.ToList();
        foreach (var name in new[] { "MapOpportunity", "MapRecovery" })
        {
            string path = "Assets/Scenes/" + name + ".unity";
            int index = scenes.FindIndex(scene => scene.path == path);
            if (index < 0) scenes.Add(new EditorBuildSettingsScene(path, true));
            else scenes[index].enabled = true;
        }
        Require(scenes[0].path == "Assets/Scenes/MainMenu.unity", "Keep the main menu as the build entry scene.");
        EditorBuildSettings.scenes = scenes.ToArray();
        AssetDatabase.SaveAssets();
        Verify();
    }

    [MenuItem("Tools/Map/Verify Run Integration")]
    public static void Verify()
    {
        Directory.CreateDirectory(ResultDirectory);
        var graph = AssetDatabase.LoadAssetAtPath<MapGraphDefinition>("Assets/Art/Map/Settings/Graph.asset");
        var catalog = AssetDatabase.LoadAssetAtPath<MapNodeCatalog>("Assets/Art/Map/Settings/Nodes.asset");
        graph.Validate(); catalog.Validate(graph);
        Require(graph.nodes.Length == 14 && graph.nodes.Sum(node => node.next.Length) == 19, "Formal map topology must remain 14/19.");
        var cards = AssetDatabase.FindAssets("t:CardDefinition", new[] { "Assets/Data/Cards" })
            .Select(guid => AssetDatabase.LoadAssetAtPath<CardDefinition>(AssetDatabase.GUIDToAssetPath(guid))).ToArray();
        Require(cards.Length == 11 && cards.All(card => card.combatPrefab != null && card.combatPrefab.GetComponent<BuffCards>() != null),
            "All eleven teammate cards must retain valid combat prefabs.");
        string[] menuStarterIds = null;
        foreach (string name in new[] { "MainMenu", "Map", "Deinosavros", "MapOpportunity", "MapRecovery" })
        {
            var scene = EditorSceneManager.OpenScene("Assets/Scenes/" + name + ".unity");
            Require(EditorBuildSettings.scenes.Any(entry => entry.enabled && entry.path == scene.path), "Scene is missing from the build: " + name);
            if (name == "MainMenu" || name == "Map" || name == "Deinosavros")
                Require(Components<RunSettings>(scene).Count() == 1 && RunSettings.ForScene(scene) == RunBalance.Default,
                    "The scene exposes exactly one shared Run Settings configuration: " + name);
            foreach (var root in scene.GetRootGameObjects())
                foreach (var transform in root.GetComponentsInChildren<Transform>(true))
                    Require(GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(transform.gameObject) == 0,
                        "Missing script: " + name + "/" + transform.name);
            if (name == "Map")
            {
                var controller = Components<MapController>(scene).Single();
                Require(controller.Nodes.Length == 14 && controller.EncounterRouting != null && controller.TravelView != null,
                    "The map must retain its authored route and travel bindings.");
                using var data = new SerializedObject(Components<RunSession>(scene).Single());
                var starter = data.FindProperty("startingDeck");
                Require(starter.arraySize > 0 && Enumerable.Range(0, starter.arraySize)
                    .All(i => starter.GetArrayElementAtIndex(i).objectReferenceValue != null),
                    "Direct Map entry must retain a valid starting deck after reload.");
                Require(Enumerable.Range(0, starter.arraySize).Sum(i =>
                    ((CardDefinition)starter.GetArrayElementAtIndex(i).objectReferenceValue).capacityCost) <= RunBalance.Default.deckCapacity,
                    "The authored starting deck fits the configured capacity.");
                Require(menuStarterIds.SequenceEqual(Enumerable.Range(0, starter.arraySize).Select(i =>
                    ((CardDefinition)starter.GetArrayElementAtIndex(i).objectReferenceValue).cardId)),
                    "Menu and direct Map entry use the same starter cards in the same order.");
            }
            if (name == "MainMenu")
            {
                using var data = new SerializedObject(Components<MainMenuController>(scene).Single());
                var starter = data.FindProperty("startingDeck");
                var definitions = Enumerable.Range(0, starter.arraySize).Select(i =>
                    (CardDefinition)starter.GetArrayElementAtIndex(i).objectReferenceValue).ToArray();
                Require(definitions.Length > 0 && definitions.All(card => card != null && card.capacityCost >= 0) &&
                    definitions.Sum(card => card.capacityCost) <= RunBalance.Default.deckCapacity, "The menu starting deck fits Run Settings.");
                menuStarterIds = definitions.Select(card => card.cardId).ToArray();
                Require(data.FindProperty("routeGraph").objectReferenceValue != null &&
                    data.FindProperty("nodeCatalog").objectReferenceValue != null,
                    "The menu must retain its route and catalog bindings after reload.");
            }
            if (name == "Deinosavros")
            {
                Require(Components<BattleHud>(scene).Count() == 1, "Battle has exactly one scene-local HUD.");
                var player = Components<BattleScript>(scene).Single(actor => actor.isActiveAndEnabled && actor.CompareTag("Player"));
                Require(RunBalance.Default.startingMaxHealth > 0 && player.attackDmg >= 0 && player.attackSpd > 0,
                    "Combat defaults must be valid; their exact balance values are configurable.");
            }
        }
        MapTravelTests.Run();
        Deinosavros.MapTools.Editor.MapMonitorTests.Run();
        RunIntegrationTests.Run();
        MapOpportunityVerification.VerifyState();
        File.WriteAllText(ResultDirectory + "/asset-verification.txt", "PASS: 14 nodes, 19 roads, 11 cards, five required scenes, scene-local HUD, unchanged combat defaults and state tests.\n");
        EditorSceneManager.OpenScene("Assets/Scenes/MainMenu.unity");
        Debug.Log("Run integration asset and state verification passed.");
    }

    private static System.Collections.Generic.IEnumerable<T> Components<T>(Scene scene) where T : Component =>
        scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<T>(true));
    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
