using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Deinosavros.MapReview;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Deinosavros.MapTools.Editor
{
    public static class MapMonitorTests
    {
        public const string Evidence = "Library/MapMonitor/verification.txt";
        private static int assertions;

        [MenuItem("Tools/Map/Verify Monitor")]
        public static void Run()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Run monitor verification in Edit Mode. The live adventure will not be modified.");
            assertions = 0;
            var existing = RunSession.Instance;
            var scene = SceneManager.GetActiveScene();
            bool wasDirty = scene.isDirty;
            var graph = AssetDatabase.LoadAssetAtPath<MapGraphDefinition>("Assets/MapReview/MapGraph.asset");
            var catalog = AssetDatabase.LoadAssetAtPath<MapNodeCatalog>("Assets/Art/MapTravel/NodeInformation.asset");
            var root = new GameObject("Monitor Test Fixture") { hideFlags = HideFlags.HideAndDontSave };
            root.SetActive(false);
            var fixture = root.AddComponent<RunSession>();
            var card = ScriptableObject.CreateInstance<CardDefinition>();
            card.cardId = "monitor-fixture"; card.displayName = "Monitor Fixture"; card.capacityCost = 5;
            card.overworldEffect = new OverworldEffectDefinition { effectType = OverworldEffectType.GrantStartingShield, magnitude = 5 };
            try
            {
                Check(RunSession.Instance == existing, "Inactive Edit Mode fixture does not replace the live singleton.");
                fixture.InitializeProgress(graph, catalog);
                Check(fixture.TryAddCard(card, out var first) && fixture.TryAddCard(card, out _), "Fixture supports same-name instances.");
                string before = EditorJsonUtility.ToJson(fixture);
                string run = fixture.Progress.RunId;
                var initial = MapMonitorData.Capture(fixture, "Map");
                Check(initial.nodes.Length == 14 && initial.nodes.Sum(n => n.next.Length) == 19, "All formal nodes and roads are observed.");
                Check(initial.nodes.Count(n => n.kind == MapEncounterKind.Battle) == 7 &&
                    initial.nodes.Count(n => n.kind == MapEncounterKind.Opportunity) == 3 &&
                    initial.nodes.Count(n => n.kind == MapEncounterKind.Recovery) == 3 &&
                    initial.nodes.Count(n => n.kind == MapEncounterKind.Boss) == 1, "All four encounter kinds match the catalog.");
                Check(initial.cards.Length == 2 && initial.cards[0].instanceId != initial.cards[1].instanceId, "Same-name cards remain distinct in monitor.");
                Check(initial.capacity == 10 && initial.health == fixture.PlayerHealth && initial.attackInterval == fixture.PlayerAttackSpeed,
                    "Capacity and player values are read from the session.");
                for (int i = 0; i < 20; i++) MapMonitorData.Capture(fixture, "Map");
                Check(before == EditorJsonUtility.ToJson(fixture) && run == fixture.Progress.RunId && fixture.Progress.CompletedCount == 0,
                    "Repeated snapshots do not change session, progression or serialized fields.");
                initial.cards[0].sacrificed = true; initial.nodes[0].next[0] = "changed-snapshot";
                initial.pending.isValid = true;
                Check(!fixture.RunDeck[0].sacrificed && !fixture.Progress.NextNodes("level_01_01").Contains("changed-snapshot") && !fixture.HasPendingModifier,
                    "Snapshot edits cannot mutate the source run, roads or offering.");
                var next = fixture.Progress.NextNodes("level_01_01") as IList<string>;
                bool denied = false;
                try { next[0] = "illegal"; } catch (NotSupportedException) { denied = true; }
                Check(denied, "Public road inspection is read-only.");
                var history = new MapMonitorHistory();
                history.Observe(MapMonitorData.Capture(fixture, "Map"));
                int count = history.entries.Count;
                history.Observe(MapMonitorData.Capture(fixture, "Map"));
                Check(count == history.entries.Count, "Unchanged sampling does not spam history.");
                Check(fixture.TrySacrificeInstance(first.instanceId), "Fixture sacrifice accepted.");
                var offering = MapMonitorData.Capture(fixture, "Map"); history.Observe(offering);
                Check(offering.cards.Count(c => c.sacrificed) == 1 && offering.capacity == 5 && offering.pending.isValid,
                    "Offering monitor tracks exact instance and pending effect without consuming it.");
                Check(history.entries.Any(e => e.category == "Offering" && e.message.Contains("GrantStartingShield")), "Offering event recorded.");

                var routes = new List<string[]>();
                void Visit(string id, List<string> path)
                {
                    path.Add(id);
                    if (graph.Find(id).boss) routes.Add(path.ToArray());
                    else foreach (string target in graph.Find(id).next) Visit(target, new List<string>(path));
                }
                Visit("level_01_01", new List<string>());
                foreach (var route in routes)
                {
                    fixture.Progress.BeginNewRun(graph, catalog);
                    foreach (var id in route)
                    {
                        string origin = fixture.Progress.CurrentNodeId;
                        Check(fixture.TryBeginTravel(id, out var ticket), "Departure accepted for monitor route test.");
                        var moving = MapMonitorData.Capture(fixture, "Map"); history.Observe(moving);
                        Check(moving.currentNode == origin && moving.targetNode == id && moving.encounterId == ticket.EncounterId &&
                            moving.phase == MapProgressPhase.Traveling, "Moving snapshot distinguishes committed origin from target.");
                        Check(moving.kind == catalog.Find(id).kind.ToString(), "Ticket type visible.");
                        fixture.Progress.MarkLoading(ticket.EncounterId);
                        history.Observe(MapMonitorData.Capture(fixture, "Map"));
                        fixture.ConfirmEncounterStarted(ticket.EncounterId);
                        var entered = MapMonitorData.Capture(fixture, "Receiver"); history.Observe(entered);
                        Check(entered.currentNode == id && entered.phase == MapProgressPhase.InEncounter, "Ready receiver commits observed location.");
                        fixture.Progress.CompleteEncounter(ticket.EncounterId, true);
                        var returned = MapMonitorData.Capture(fixture, "Map"); history.Observe(returned);
                        Check(returned.targetNode == null && returned.nodes.Single(n => n.id == id).state == MapLocationState.Completed,
                            "Completed node and cleared encounter observed.");
                    }
                    Check(MapMonitorData.Capture(fixture, "Map").phase == MapProgressPhase.Won, "Boss completion observed.");
                }
                Check(history.entries.Any(e => e.message.Contains("receiver confirmed")) && history.entries.Any(e => e.message.Contains("Boss completed")),
                    "History records readiness and completion, not just scene changes.");

                fixture.Progress.BeginNewRun(graph, catalog);
                history = new MapMonitorHistory(); history.Observe(MapMonitorData.Capture(fixture, "Map"));
                fixture.TryBeginTravel("level_01_01", out var canceled); history.Observe(MapMonitorData.Capture(fixture, "Map"));
                fixture.CancelPreparedTravel(canceled.EncounterId); history.Observe(MapMonitorData.Capture(fixture, "Map"));
                Check(history.entries.Any(e => e.message.Contains("origin retained")), "Canceled travel recorded without claiming victory.");
                fixture.TryBeginTravel("level_01_01", out var failed); fixture.Progress.MarkLoading(failed.EncounterId);
                fixture.ConfirmEncounterStarted(failed.EncounterId); history.Observe(MapMonitorData.Capture(fixture, "Battle"));
                fixture.CompleteEncounter(failed.EncounterId, false); history.Observe(MapMonitorData.Capture(fixture, "Map"));
                Check(history.entries.Any(e => e.message.Contains("adventure failed")), "Failure observed.");

                fixture.Progress.BeginNewRun(graph, catalog);
                CompleteFixtureNode(fixture, "level_01_01");
                fixture.TryBeginTravel("level_02_02", out var healing); fixture.Progress.MarkLoading(healing.EncounterId);
                fixture.ConfirmEncounterStarted(healing.EncounterId);
                using (var serialized = new SerializedObject(fixture))
                {
                    serialized.FindProperty("playerHealth").intValue = 40;
                    serialized.ApplyModifiedPropertiesWithoutUndo();
                }
                Check(fixture.TryResolveRecovery(healing.EncounterId, out _), "Recovery fixture resolved.");
                var recovery = MapMonitorData.Capture(fixture, "MapRecovery");
                Check(recovery.receipt.before == 40 && recovery.receipt.after == 55 && recovery.receipt.recovered == 15,
                    "Recovery receipt displays before, after and exact healing.");
                fixture.CompleteNonCombatEncounter(healing.EncounterId);
                Check(MapMonitorData.Capture(fixture, "Map").receipt.completed && fixture.HasPendingModifier,
                    "Last receipt remains visible after return; observation preserves pending offering.");

                var guard = new MapMonitorSnapshot { hasProgress = true, hasMap = true, canInteract = true, phase = MapProgressPhase.OnMap };
                Check(MapMonitorData.CanRestart(guard, true, false, true), "Idle Map allows explicit reset.");
                foreach (MapProgressPhase phase in Enum.GetValues(typeof(MapProgressPhase)))
                {
                    guard.phase = phase;
                    Check(MapMonitorData.CanRestart(guard, true, false, true) ==
                        (phase == MapProgressPhase.OnMap || phase == MapProgressPhase.Won || phase == MapProgressPhase.Lost),
                        "Reset prohibited during travel, loading and encounters.");
                }
                guard.phase = MapProgressPhase.OnMap;
                Check(!MapMonitorData.CanRestart(guard, false, false, true) && !MapMonitorData.CanRestart(guard, true, true, true) &&
                    !MapMonitorData.CanRestart(guard, true, false, false), "Edit Mode, pause and stale binding reject reset.");
                guard.busy = true; Check(!MapMonitorData.CanRestart(guard, true, false, true), "Transition locks reset.");
                guard.busy = false; guard.canInteract = false; Check(!MapMonitorData.CanRestart(guard, true, false, true), "Modal input lock rejects reset.");
                Check(!fixture.gameObject.AddComponent<MapController>().TryRestartRunForTesting(), "Runtime reset hook refuses Edit Mode fixtures.");
                for (int i = 0; i < 220; i++) history.Add("Test", "Event " + i);
                Check(history.entries.Count == 200 && history.entries[0].message == "Event 20", "History bounded to newest 200 events.");
                var json = JsonUtility.ToJson(recovery);
                var restored = JsonUtility.FromJson<MapMonitorSnapshot>(json);
                Check(restored.nodes.Length == 14 && restored.receipt.recovered == 15 && restored.runId == recovery.runId,
                    "Report snapshots serialize without losing routes or receipts.");
                Check(MapMonitorData.Capture(null, "Map").hasSession == false, "No-session state safe.");
                VerifyTestStarts(graph, catalog);
            }
            finally
            {
                Object.DestroyImmediate(root); Object.DestroyImmediate(card);
            }
            Check(RunSession.Instance == existing, "Singleton unchanged after verification.");
            Check(scene.isDirty == wasDirty, "Verification preserves the open scene dirty state.");
            Directory.CreateDirectory(Path.GetDirectoryName(Evidence));
            File.WriteAllText(Evidence, $"PASS: {assertions} assertions. Fourteen nodes, nineteen roads, nine routes, read-only snapshots, same-name card identity, offering observation, cancellation, failure, healing receipt, reset gates, bounded history, JSON export and every test start node. No gameplay scene saved.\n");
            MapMonitorWindow.Open();
            Debug.Log("Map Monitor verification passed: " + assertions + " assertions.");
        }

        private static void CompleteFixtureNode(RunSession session, string id)
        {
            session.TryBeginTravel(id, out var ticket); session.Progress.MarkLoading(ticket.EncounterId);
            session.ConfirmEncounterStarted(ticket.EncounterId); session.CompleteEncounter(ticket.EncounterId, true);
        }

        private static void VerifyTestStarts(MapGraphDefinition graph, MapNodeCatalog catalog)
        {
            // The seed operation is internal to runtime assembly and excluded from player builds.
            var seed = typeof(MapProgressState).GetMethod("TrySetTestStartNode",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            bool Seed(MapProgressState state, string id) => (bool)seed.Invoke(state, new object[] { id });
            foreach (var node in graph.nodes)
            {
                var state = new MapProgressState(graph, catalog);
                Check(Seed(state, node.id), "Every authored node can seed a fresh test run.");
                Check(state.CurrentNodeId == node.id && state.TestStartNodeId == node.id && state.CompletedCount == 0,
                    "Test start does not simulate prior victories.");
                foreach (var other in graph.nodes)
                {
                    var expected = other.id == node.id ? MapLocationState.Available :
                        other.layer < node.layer ? MapLocationState.Skipped : MapLocationState.Locked;
                    Check(state.NodeState(other.id) == expected, "Only the test start is open; prior layers are skipped.");
                    foreach (string target in other.next) Check(!state.HasTraveled(other.id, target), "Test start invents no traveled roads.");
                }
                Check(!Seed(state, node.id), "Duplicate reseed rejected.");
                Check(state.TryBeginTravel(node.id, out var ticket) && ticket.IsInitial && ticket.Kind == catalog.Find(node.id).kind,
                    "Test entry uses a normal initial travel ticket with the correct type.");
                Check(!Seed(state, "level_01_01"), "Active travel cannot be reseeded.");
                Check(state.CancelPreparedTravel(ticket.EncounterId) && state.CurrentNodeId == node.id &&
                    state.NodeState(node.id) == MapLocationState.Available, "Failed test entry retains its seed for retry.");
                state.TryBeginTravel(node.id, out var retry); state.MarkLoading(retry.EncounterId); state.ConfirmEncounterStarted(retry.EncounterId);
                Check(state.CompleteEncounter(retry.EncounterId, true), "Test encounter completes through normal progression.");
                Check(state.CompletedCount == 1 && state.CurrentNodeId == node.id, "Returning position is the completed test node.");
                foreach (var other in graph.nodes)
                    Check((state.NodeState(other.id) == MapLocationState.Available) == node.next.Contains(other.id), "Only normal outgoing branches open after test encounter.");
                Check(!Seed(state, "level_01_01"), "Completed test adventure cannot be reseeded in place.");
                state.BeginNewRun(graph, catalog);
                Check(state.TestStartNodeId == null && state.CurrentNodeId == "level_01_01" && state.CompletedCount == 0,
                    "Normal New Run clears test marker and restores real entry point.");
            }
            var invalid = new MapProgressState(graph, catalog);
            string run = invalid.RunId;
            Check(!Seed(invalid, null) && !Seed(invalid, "missing") && invalid.RunId == run && invalid.TestStartNodeId == null,
                "Invalid start IDs leave the run unchanged.");
        }

        private static void Check(bool condition, string message)
        {
            assertions++;
            if (!condition) throw new InvalidOperationException("Map Monitor: " + message);
        }
    }
}
