using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Deinosavros.MapReview;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class CombatEnvironmentSetup
{
    private const string ScenePath = "Assets/Scenes/Deinosavros.unity";
    private const string Results = "Library/CombatEnvironment";

    [MenuItem("Tools/Battle/Bind Forest and Desert")]
    public static void Apply()
    {
        Require(!EditorApplication.isPlayingOrWillChangePlaymode, "Stop Play Mode before binding scenery.");
        var scene = SceneManager.GetActiveScene();
        Require(scene.path == ScenePath && !scene.isDirty, "Open and save Deinosavros before binding scenery.");
        var roots = scene.GetRootGameObjects();
        var forest = roots.Single(root => PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(root) ==
            "Assets/Prefabs/Meshes/BackgroundForest.prefab");
        var desert = roots.Single(root => PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(root) ==
            "Assets/Prefabs/Meshes/BackgroundDesert.prefab");
        var hud = roots.SelectMany(root => root.GetComponentsInChildren<BattleHud>(true)).Single();
        var existing = roots.SelectMany(root => root.GetComponentsInChildren<CombatEnvironment>(true)).ToArray();
        Require(existing.Length <= 1, "Duplicate combat environment selectors.");
        Directory.CreateDirectory(Results);
        if (!File.Exists(Results + "/Before.unity")) File.Copy(ScenePath, Results + "/Before.unity");
        var selector = existing.SingleOrDefault();
        bool changed = selector == null;
        if (selector == null) selector = hud.gameObject.AddComponent<CombatEnvironment>();
        if (selector.forest != forest || selector.desert != desert)
        {
            selector.forest = forest; selector.desert = desert; changed = true;
        }
        if (changed)
        {
            EditorUtility.SetDirty(selector);
            EditorSceneManager.MarkSceneDirty(scene);
            Require(EditorSceneManager.SaveScene(scene), "Could not save environment bindings.");
        }
        // Keep the user's edit-time environment visibility and all transforms untouched.
        Verify();
    }

    [MenuItem("Tools/Battle/Verify Forest and Desert Progression")]
    public static void Verify()
    {
        Require(!EditorApplication.isPlayingOrWillChangePlaymode, "Run saved-scene verification in Edit Mode.");
        var scene = EditorSceneManager.OpenPreviewScene(ScenePath);
        int checks = 0;
        void Check(bool value, string message) { Require(value, message); checks++; }
        try
        {
            var selector = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<CombatEnvironment>(true)).Single();
            Check(selector.forest != null && selector.desert != null && selector.forest != selector.desert &&
                selector.forest.scene == scene && selector.desert.scene == scene, "Saved scenery references must be local and distinct.");
            Check(selector.forestBattleCount == 2, "The first two battles must use the forest.");
            var forestPose = EditorJsonUtility.ToJson(selector.forest.transform);
            var desertPose = EditorJsonUtility.ToJson(selector.desert.transform);
            var graph = AssetDatabase.LoadAssetAtPath<MapGraphDefinition>("Assets/Art/Map/Settings/Graph.asset");
            var catalog = AssetDatabase.LoadAssetAtPath<MapNodeCatalog>("Assets/Art/Map/Settings/Nodes.asset");
            var routes = new List<string[]>();
            void Visit(string id, List<string> path)
            {
                path.Add(id);
                if (graph.Find(id).boss) routes.Add(path.ToArray());
                else foreach (var next in graph.Find(id).next) Visit(next, new List<string>(path));
            }
            Visit(graph.nodes.Single(node => node.layer == 1).id, new List<string>());
            foreach (var route in routes)
            {
                int opportunities = route.Count(id => catalog.Find(id).kind == MapEncounterKind.Opportunity);
                for (int mask = 0; mask < 1 << opportunities; mask++)
                {
                    var state = new MapProgressState(graph, catalog);
                    int combats = 0, opportunity = 0;
                    foreach (string id in route)
                    {
                        Check(state.TryBeginTravel(id, out var ticket) && state.MarkLoading(ticket.EncounterId), "Could not prepare " + id);
                        bool opportunityBattle = ticket.Kind == MapEncounterKind.Opportunity && (mask & (1 << opportunity++)) != 0;
                        if (opportunityBattle)
                        {
                            Check(state.ConfirmEncounterStarted(ticket.EncounterId), "Opportunity did not start.");
                            typeof(MapProgressState).GetMethod("PrepareOpportunityBattle", System.Reflection.BindingFlags.Instance |
                                System.Reflection.BindingFlags.NonPublic).Invoke(state, new object[] { ticket.EncounterId });
                            Check(state.CurrentEncounter.IsCombat && state.CompletedCombatCount == combats,
                                "Preparing opportunity combat cannot increment the completed count.");
                        }
                        if (ticket.IsCombat)
                        {
                            bool desert = combats >= 2 || ticket.IsBoss;
                            selector.Apply(state);
                            Check(selector.desert.activeSelf == desert && selector.forest.activeSelf != desert,
                                "Incorrect environment for actual combat " + (combats + 1) + " at " + id);
                            selector.Apply(state);
                            Check(state.CompletedCombatCount == combats, "Repeated selection must not advance battle count.");
                        }
                        Check(state.ConfirmEncounterStarted(ticket.EncounterId) && !state.ConfirmEncounterStarted(ticket.EncounterId),
                            "Encounter confirmation must be idempotent.");
                        if (ticket.IsCombat) combats++;
                        Check(state.CompleteEncounter(ticket.EncounterId, true) && state.CompletedCombatCount == combats &&
                            !state.CompleteEncounter(ticket.EncounterId, true), "Only completed combat increments the count once.");
                    }
                    Check(state.Phase == MapProgressPhase.Won && combats >= 3, "A complete route must reach the desert Boss.");
                    state.BeginNewRun(graph, catalog);
                    Check(state.CompletedCombatCount == 0, "New runs must reset the environment progression.");
                }
            }
            var retry = new MapProgressState(graph, catalog);
            retry.TryBeginTravel(retry.CurrentNodeId, out var canceled);
            retry.MarkLoading(canceled.EncounterId);
            Check(retry.CancelPreparedTravel(canceled.EncounterId) && retry.CompletedCombatCount == 0,
                "Canceled loads cannot advance the environment.");
            retry.TryBeginTravel(retry.CurrentNodeId, out var failed);
            retry.MarkLoading(failed.EncounterId); retry.ConfirmEncounterStarted(failed.EncounterId);
            Check(retry.CompleteEncounter(failed.EncounterId, false) && retry.CompletedCombatCount == 0,
                "A failed first battle cannot advance the environment.");
            selector.previewDesertWithoutEncounter = false; selector.Apply(null);
            Check(selector.forest.activeSelf && !selector.desert.activeSelf, "Direct Combat Play defaults to forest.");
            selector.Apply(null, true);
            Check(!selector.forest.activeSelf && selector.desert.activeSelf, "Direct Boss preview uses the desert.");
            Check(EditorJsonUtility.ToJson(selector.forest.transform) == forestPose &&
                EditorJsonUtility.ToJson(selector.desert.transform) == desertPose, "Selecting scenery must preserve authored transforms.");
            Directory.CreateDirectory(Results);
            File.WriteAllText(Results + "/verification.txt", "PASS: " + checks + " checks across " + routes.Count +
                " routes and every opportunity combat/noncombat combination; saved references, duplicate completion, cancel, defeat, reset and preview.\n");
        }
        finally { EditorSceneManager.ClosePreviewScene(scene); }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    public static System.Collections.IEnumerator VerifyRuntime(Action<bool, string> check)
    {
        while (UnityEngine.Object.FindFirstObjectByType<MainMenuController>() == null) yield return null;
        var menu = UnityEngine.Object.FindFirstObjectByType<MainMenuController>();
        typeof(MainMenuController).GetMethod("StartRun", System.Reflection.BindingFlags.Instance |
            System.Reflection.BindingFlags.NonPublic).Invoke(menu, null);
        while (SceneManager.GetActiveScene().path != ScenePath || RunSession.Instance?.Progress?.Phase != MapProgressPhase.InEncounter ||
            RunSession.Instance.GetComponent<MapTravelCoordinator>().IsBusy) yield return null;
        var session = RunSession.Instance;
        var selector = UnityEngine.Object.FindFirstObjectByType<CombatEnvironment>();
        var playerPose = BattleHud.FindPlayer(SceneManager.GetActiveScene()).transform.position;
        var forestPose = selector.forest.transform.position;
        var desertPose = selector.desert.transform.position;
        void CheckEnvironment(bool desert, int count)
        {
            var active = SceneManager.GetActiveScene();
            var current = UnityEngine.Object.FindFirstObjectByType<CombatEnvironment>();
            check(current != null && current.forest.activeInHierarchy != desert && current.desert.activeInHierarchy == desert,
                "Awake selects exactly one environment after scene load: " + (desert ? "desert" : "forest"));
            check(session.Progress.CompletedCombatCount == count, "Scene loading does not increment the battle count.");
            check(BattleHud.FindPlayer(active).transform.position == playerPose && current.forest.transform.position == forestPose &&
                current.desert.transform.position == desertPose, "Environment selection does not move the player or scenery.");
            BattleHud.Find(active).StopCombat();
        }
        CheckEnvironment(false, 0);
        Time.timeScale = 0;
        foreach (var id in new[] { "level_02_03", "level_03_02" })
        {
            string previous = session.Progress.CurrentEncounter.EncounterId;
            check(session.CompleteEncounter(previous, true), "Complete the previous disposable combat.");
            int count = session.Progress.CompletedCombatCount;
            check(!session.CompleteEncounter(previous, true) && session.Progress.CompletedCombatCount == count,
                "Duplicate results cannot skip a forest battle.");
            check(session.TryBeginTravel(id, out var ticket) && session.Progress.MarkLoading(ticket.EncounterId),
                "Prepare the next actual combat scene.");
            var load = SceneManager.LoadSceneAsync("Deinosavros");
            while (!load.isDone) yield return null;
            yield return null;
            CheckEnvironment(count >= 2, count);
            var hud = BattleHud.Find(SceneManager.GetActiveScene());
            var player = BattleHud.FindPlayer(SceneManager.GetActiveScene());
            check(hud.TryPrepare(session, player) && session.ConfirmEncounterStarted(ticket.EncounterId) &&
                session.GetComponent<BattleRunBridge>().InitializeConfirmedEncounter(ticket.EncounterId, player),
                "Existing combat hand, HUD and battle receiver remain ready.");
        }
        var reload = SceneManager.LoadSceneAsync("Deinosavros");
        while (!reload.isDone) yield return null;
        yield return null;
        CheckEnvironment(true, 2);
        session.BeginNewRun();
        check(session.TryBeginTravel(session.Progress.CurrentNodeId, out var first) && session.Progress.MarkLoading(first.EncounterId),
            "Restart prepares the first forest battle.");
        var restart = SceneManager.LoadSceneAsync("Deinosavros");
        while (!restart.isDone) yield return null;
        yield return null;
        CheckEnvironment(false, 0);
    }
}
