using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Deinosavros.MapReview.Editor
{
    public static class MapReviewTests
    {
        private static readonly List<string> passed = new();
        private static void Check(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException("FAILED: " + message);
            passed.Add(message);
        }
        private static void Reject(Action action, string message)
        {
            bool rejected = false;
            try { action(); }
            catch (ArgumentException) { rejected = true; }
            catch (InvalidOperationException) { rejected = true; }
            Check(rejected, message);
        }
        public static string[] Run()
        {
            passed.Clear();
            var graph = ScriptableObject.CreateInstance<MapGraphDefinition>(); graph.SetReviewTopology();
            var definitions = new[] { "TidalWave", "FleetFootwork", "SolarShield", "UncertainFates" }
                .Select(name => AssetDatabase.LoadAssetAtPath<CardDefinition>("Assets/Data/Cards/" + name + ".asset")).ToArray();
            Check(definitions.All(d => d != null), "All four original card definitions are available");
            Check(graph.nodes.Length == 14 && graph.nodes.Sum(n => n.next.Length) == 19, "14 nodes and 19 explicit edges");
            string[] approvedEdges =
            {
                "01_01:02_01", "01_01:02_03", "01_01:02_02", "02_01:03_01",
                "02_03:03_01", "02_03:03_02", "02_02:03_02", "02_02:03_03",
                "03_01:04_01", "03_01:04_03", "03_02:04_03", "03_02:04_02",
                "03_03:04_02", "04_01:05_01", "04_03:05_03", "04_02:05_02",
                "05_01:06_01", "05_03:06_01", "05_02:06_01"
            };
            var configuredEdges = graph.nodes.SelectMany(n => n.next.Select(next => n.id.Substring(6) + ":" + next.Substring(6)));
            Check(new HashSet<string>(approvedEdges).SetEquals(configuredEdges), "Configured route table matches every approved edge exactly");
            Check(graph.Find("level_02_03").terrace == "Node terrace 02" &&
                graph.Find("level_04_03").terrace == "Node terrace 08" &&
                graph.Find("level_05_03").terrace == "Node terrace 11", "Three new nodes reference their exact source terraces");
            Action<Action<MapGraphDefinition>, string> rejectGraph = (mutate, message) =>
            {
                graph.SetReviewTopology(); mutate(graph); Reject(graph.Validate, message); graph.SetReviewTopology();
            };
            rejectGraph(g => g.nodes = null, "Null graph data rejected with a descriptive validation failure");
            rejectGraph(g => g.nodes[2] = null, "Null graph node rejected");
            rejectGraph(g => g.nodes[2].next = null, "Null edge collection rejected");
            rejectGraph(g => g.nodes[2].id = g.nodes[1].id, "Duplicate graph node rejected");
            rejectGraph(g => g.nodes[2].anchor = g.nodes[1].anchor, "Duplicate model anchor rejected");
            rejectGraph(g => g.nodes[0].next[0] = "missing", "Missing edge target rejected");
            rejectGraph(g => g.nodes[0].next[0] = "level_03_01", "Skipping a layer rejected");
            rejectGraph(g => g.nodes[0].next[1] = g.nodes[0].next[0], "Duplicate outgoing edge rejected");
            rejectGraph(g => g.nodes[2].layer = -1, "Invalid node layer rejected");
            rejectGraph(g => g.nodes[13].next = new[] { "level_01_01" }, "Boss cannot connect back into the adventure");

            var uninitialized = new MapRunState();
            Check(!uninitialized.TryPrepareEncounter(null, out _) && !uninitialized.TrySacrifice(null) &&
                uninitialized.NodeState(null) == MapNodeState.Locked && uninitialized.AddCard(definitions[0]) == null,
                "Uninitialized state and missing identifiers are safe no-ops");
            var paths = new List<List<string>>();
            void Visit(string id, List<string> path)
            {
                var branch = new List<string>(path) { id }; var node = graph.Find(id);
                if (node.boss) paths.Add(branch);
                foreach (string next in node.next) Visit(next, branch);
            }
            Visit("level_01_01", new List<string>());
            var usedEdges = new HashSet<string>();
            foreach (var path in paths)
            {
                var run = new MapRunState(); run.BeginNewRun(graph, definitions);
                Check(graph.nodes.Count(n => run.NodeState(n.id) == MapNodeState.Available) == 1 &&
                    run.NodeState("level_01_01") == MapNodeState.Available, "A new adventure opens only its start node");
                Check(path.Count == 6, "Every route has five ordinary encounters then the boss");
                for (int i = 0; i < path.Count; i++)
                {
                    if (i > 0) usedEdges.Add(path[i - 1] + ":" + path[i]);
                    Check(run.TryPrepareEncounter(path[i], out var request), "Prepare reachable " + path[i]);
                    Check(run.AcknowledgeEncounterStarted(request.EncounterId), "Acknowledge reachable " + path[i]);
                    Check(run.CompleteEncounter(new MapEncounterSimulation(request).Result(true)), "Complete reachable " + path[i]);
                    Check(!run.TryPrepareEncounter(path[i], out _), "Completed nodes cannot be replayed");
                    var expected = new HashSet<string>(graph.Find(path[i]).next);
                    Check(graph.nodes.Where(n => run.NodeState(n.id) == MapNodeState.Available).All(n => expected.Contains(n.id)) &&
                        expected.All(id => run.NodeState(id) == MapNodeState.Available), "Victory opens exactly the explicit outgoing branch");
                    Check(graph.nodes.Where(n => n.layer <= i + 1 && n.id != path[i] && !path.Take(i).Contains(n.id))
                        .All(n => run.NodeState(n.id) == MapNodeState.Skipped), "Passed alternative branches cannot be farmed");
                }
                Check(run.Phase == MapRunPhase.Won, "Boss victory ends the run");
                Check(!run.TrySacrifice(run.Cards[0].InstanceId) && run.AddCard(definitions[0]) == null,
                    "An ended adventure cannot consume or receive more cards");
            }
            Check(usedEdges.Count == 19, "All nineteen connections exercised through complete runs");

            for (int effect = 0; effect < definitions.Length; effect++)
            {
                var run = new MapRunState(); run.BeginNewRun(graph, new[] { definitions[effect], definitions[effect] },
                    new MapPlayerState(100, 100, 9, 2.5f, 7, 8, 10));
                string first = run.Cards[0].InstanceId, second = run.Cards[1].InstanceId;
                Check(first != second, "Duplicate definitions have unique instance IDs");
                Check(run.TrySacrifice(second) && !run.Cards[0].Sacrificed && run.Cards[1].Sacrificed, "Only selected instance is removed");
                Check(!run.TrySacrifice(second) && !run.TrySacrifice(first), "Sacrifice is atomic and limited to one per encounter");
                Check(run.TryPrepareEncounter("level_01_01", out var failed), "Prepare before simulated load failure");
                Check(!run.TryPrepareEncounter("level_01_01", out _) && run.AddCard(definitions[effect]) == null,
                    "Preparation freezes the pending request until acknowledgement or cancellation");
                Reject(() => new MapEncounterSimulation(failed), "Receiver cannot apply effects before readiness acknowledgement");
                Check(!run.CancelPreparedEncounter("stale") && !run.AcknowledgeEncounterStarted("stale"),
                    "Unrelated callbacks cannot alter a preparation");
                Check(run.CancelPreparedEncounter(failed.EncounterId), "Cancel failed preparation");
                Check(!run.CancelPreparedEncounter(failed.EncounterId), "Repeated cancellation is ignored");
                Check(run.PendingEffect.IsValid && run.SacrificeUsed, "Load failure preserves offering and allowance");
                Check(run.Player.MaxElixir == 10 && run.Player.Elixir == 8, "Prepare and cancel never commit permanent Elixir");
                Check(!run.AcknowledgeEncounterStarted(failed.EncounterId), "Stale acknowledgement rejected");
                Check(run.TryPrepareEncounter("level_01_01", out var request), "Retry creates a new request");
                Check(request.EncounterId != failed.EncounterId && request.Deck.Count == 1 && request.Deck[0].InstanceId == first,
                    "Retry has a new encounter identity and excludes only the sacrificed instance");
                Check(!run.TrySacrifice(first), "Prepared request cannot be mutated by sacrificing");
                Check(run.AcknowledgeEncounterStarted(request.EncounterId), "Receiver readiness commits exactly once");
                Check(!run.AcknowledgeEncounterStarted(request.EncounterId), "Duplicate acknowledgement rejected");
                Check(!run.CancelPreparedEncounter(request.EncounterId), "An already started encounter cannot be cancelled as a load failure");
                var simulation = new MapEncounterSimulation(request);
                Check(request.IsAcknowledged && !run.PendingEffect.IsValid && run.SacrificeUsed,
                    "Starting consumes the effect but does not restore the offering allowance");
                int enemy = simulation.SpawnEnemy("enemy-a", 101);
                Check(enemy == (effect == 0 ? 76 : 101), "Initial enemy health uses ceiling at 75 percent");
                Check(simulation.SpawnEnemy("enemy-a", 999) == enemy, "Enemy modifier applied once per instance");
                Check(simulation.SpawnEnemy("wave-two", 5) == (effect == 0 ? 4 : 5), "Later wave receives the same modifier");
                Check(simulation.SpawnEnemy("minimum", 1) == 1, "Enemy health never falls below one");
                Check(Math.Abs(simulation.EffectiveAttackInterval - (effect == 1 ? 1.5f : 2.5f)) < .001f, "Floating point attack interval preserved");
                Check(simulation.TemporaryShield == (effect == 2 ? 5 : 0), "Temporary shield is its own additive layer");
                simulation.TakeDamage(3);
                Check(simulation.PersistentPlayer.Shield == (effect == 2 ? 7 : 4), "Temporary shield absorbs damage before persistent shield");
                Check(run.Player.MaxElixir == (effect == 3 ? 13 : 10), "Permanent maximum committed once at start");
                Check(run.Player.Elixir == (effect == 3 ? 11 : 8), "Permanent offering restores exactly three Elixir once");
                Check(simulation.SpendElixir(2) && !simulation.SpendElixir(float.NaN), "Elixir consumption and invalid input validation");
                Check(!simulation.SpendElixir(-1) && !simulation.SpendElixir(float.PositiveInfinity) &&
                    !simulation.SpendElixir(1000), "Invalid and unaffordable Elixir spending leave state unchanged");
                Check(!run.CompleteEncounter(new MapEncounterResult("stale-run", request.EncounterId, true, simulation.PersistentPlayer)) &&
                    !run.CompleteEncounter(new MapEncounterResult(run.RunId, failed.EncounterId, true, simulation.PersistentPlayer)) &&
                    !run.CompleteEncounter(new MapEncounterResult(run.RunId, request.EncounterId, true, null)),
                    "Wrong run, cancelled encounter and missing result state cannot complete a fight");
                var incorrectMaximum = new MapPlayerState(maxElixir: run.Player.MaxElixir + 3);
                Check(!run.CompleteEncounter(new MapEncounterResult(run.RunId, request.EncounterId, true, incorrectMaximum)),
                    "Receiver cannot apply the permanent maximum a second time");
                var result = simulation.Result(true);
                Check(ReferenceEquals(result, simulation.Result(false)) && simulation.IsCompleted && simulation.TemporaryShield == 0,
                    "Receiver seals one outcome and discards temporary shield at completion");
                var finalPlayer = simulation.PersistentPlayer;
                simulation.TakeDamage(1000);
                Check(ReferenceEquals(finalPlayer, simulation.PersistentPlayer) && !simulation.SpendElixir(1),
                    "Late receiver input cannot mutate an already delivered result");
                Reject(() => simulation.SpawnEnemy("too-late", 100), "Completed encounter rejects new enemy spawns");
                Check(run.CompleteEncounter(result) && !run.CompleteEncounter(result), "Result delivery is idempotent");
                Check(!run.SacrificeUsed && !run.PendingEffect.IsValid, "Victory refreshes allowance but not consumed effect");
                Check(run.Player.AttackInterval == 2.5f, "Temporary attack speed does not leak into persistent state");
                Check(run.Player.Shield == (effect == 2 ? 7 : 4), "Unused temporary shield is not returned");
                var reward = run.AddCard(definitions[effect]);
                Check(reward != null && reward.InstanceId != first && reward.InstanceId != second, "Reward duplicate receives its own identity");
                Check(run.TrySacrifice(reward.InstanceId), "Reward can be offered after victory");
                Check(request.Deck.Count == 1 && request.Deck[0].InstanceId == first && !request.Deck[0].Sacrificed,
                    "Old encounter deck does not change when a reward instance is sacrificed");
                Check(run.TryPrepareEncounter("level_02_03", out var next) && run.AcknowledgeEncounterStarted(next.EncounterId), "Next explicit middle branch starts");
                if (effect == 3) Check(run.Player.MaxElixir == 16 && run.Player.Elixir == 12,
                    "Permanent Elixir increases stack and restoration is not repeated on acknowledgement retries");
                Check(run.CompleteEncounter(new MapEncounterSimulation(next).Result(false)), "Defeat accepted");
                Check(run.Phase == MapRunPhase.Lost && !run.TryPrepareEncounter("level_02_01", out _), "Defeat ends run without reopening branches");
                run.BeginNewRun(graph, definitions);
                Check(run.Player.MaxElixir == 10 && !run.SacrificeUsed && run.Cards.Count == 4 && run.Cards.All(c => !c.Sacrificed), "New run resets deck, offerings and permanent gains");
                Check(!run.CompleteEncounter(result), "Previous run result rejected");
                Check(!run.AcknowledgeEncounterStarted(next.EncounterId) && !run.CancelPreparedEncounter(next.EncounterId),
                    "Previous adventure callbacks do not affect the fresh adventure");
            }
            var minimumRun = new MapRunState();
            minimumRun.BeginNewRun(graph, new[] { definitions[1] }, new MapPlayerState(attackInterval: 1.2f));
            minimumRun.TrySacrifice(minimumRun.Cards[0].InstanceId);
            minimumRun.TryPrepareEncounter("level_01_01", out var minimumRequest);
            minimumRun.AcknowledgeEncounterStarted(minimumRequest.EncounterId);
            Check(new MapEncounterSimulation(minimumRequest).EffectiveAttackInterval == 1, "Attack interval floor is one second");
            CheckImmutableSnapshots(graph, definitions);
            CheckCapacityAndValidation(graph);
            UnityEngine.Object.DestroyImmediate(graph);
            Directory.CreateDirectory("Library/MapReviewEvidence");
            File.WriteAllLines("Library/MapReviewEvidence/state-tests.txt", passed.Prepend($"PASS: {passed.Count} assertions; {paths.Count} complete paths"));
            Debug.Log($"Map review state tests passed: {passed.Count} assertions.");
            return passed.ToArray();
        }

        private static void CheckImmutableSnapshots(MapGraphDefinition graph, CardDefinition[] definitions)
        {
            var run = new MapRunState(); run.BeginNewRun(graph, definitions);
            graph.Find("level_01_01").next[0] = "level_06_01";
            run.TryPrepareEncounter("level_01_01", out var request);
            run.AcknowledgeEncounterStarted(request.EncounterId);
            run.CompleteEncounter(new MapEncounterSimulation(request).Result(true));
            Check(run.NodeState("level_02_01") == MapNodeState.Available && run.NodeState("level_06_01") == MapNodeState.Locked,
                "Changing a graph asset cannot alter an already active run");
            Check(run.TrySacrifice(request.Deck[0].InstanceId) && !request.Deck[0].Sacrificed && run.Cards[0].Sacrificed,
                "Later sacrifice does not mutate a completed encounter deck snapshot");
            graph.SetReviewTopology();
        }

        private static void CheckCapacityAndValidation(MapGraphDefinition graph)
        {
            var definition = ScriptableObject.CreateInstance<CardDefinition>();
            definition.cardId = "test-reward"; definition.capacityCost = 15;
            definition.overworldEffect = new OverworldEffectDefinition { effectType = OverworldEffectType.GrantStartingShield };
            var run = new MapRunState(); run.BeginNewRun(graph, new[] { definition });
            var reward = run.AddCard(definition);
            Check(reward != null && run.ActiveCapacity == 30 && run.Cards.Count == 2,
                "Capacity above twenty is reported without deleting or refusing reward cards");
            Check(run.TrySacrifice(reward.InstanceId) && run.ActiveCapacity == 15 && run.Cards.Count == 2,
                "Sacrificed history remains while active capacity excludes that exact instance");
            definition.overworldEffect = new OverworldEffectDefinition { effectType = (OverworldEffectType)999 };
            run.BeginNewRun(graph, new[] { definition });
            Check(!run.TrySacrifice(run.Cards[0].InstanceId) && !run.SacrificeUsed && !run.Cards[0].Sacrificed,
                "Unsupported offering is rejected without partially consuming the card");
            Reject(() => new MapPlayerState(attackInterval: 0), "Zero attack interval rejected");
            Reject(() => new MapPlayerState(elixir: float.NaN), "Non-finite Elixir rejected");
            Reject(() => new MapPlayerState(health: 101), "Health above maximum rejected");
            Reject(() => new MapPlayerState(shield: -1), "Negative persistent shield rejected");
            Reject(() => run.BeginNewRun(graph, null), "Missing starting deck rejected before changing run state");
            UnityEngine.Object.DestroyImmediate(definition);
        }
    }
}
