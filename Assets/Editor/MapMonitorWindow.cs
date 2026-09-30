using System;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Deinosavros.MapTools.Editor
{
    public sealed class MapMonitorWindow : EditorWindow
    {
        private static readonly string[] Tabs = { "Overview", "Nodes / Roads", "Deck / Offering", "Event History" };
        [SerializeField] private int tab;
        [SerializeField] private Vector2 scroll;
        [SerializeField] private string filter = "";
        [SerializeField] private string testStartNode = "level_02_02";
        [SerializeField] private bool overrideStartingHealth = true;
        [SerializeField] private int startingHealth = 50;
        private bool testControls;
        private string testNotice;
        private GUIStyle wrap, heading;
        public static int DrawCount { get; private set; }

        [MenuItem("Tools/Map/Monitor", priority = 1)]
        public static void Open()
        {
            var window = GetWindow<MapMonitorWindow>("Map Monitor");
            window.minSize = new Vector2(640, 460);
            window.Show();
        }

        private void OnEnable()
        {
            testControls = false;
            MapMonitorService.Changed += Repaint;
            EditorApplication.playModeStateChanged += ModeChanged;
        }

        private void OnDisable()
        {
            MapMonitorService.Changed -= Repaint;
            EditorApplication.playModeStateChanged -= ModeChanged;
        }

        private void ModeChanged(PlayModeStateChange state) { testControls = false; Repaint(); }

        private void OnGUI()
        {
            DrawCount++;
            wrap ??= new GUIStyle(EditorStyles.label) { wordWrap = true };
            heading ??= new GUIStyle(EditorStyles.boldLabel) { fontSize = 16 };
            var value = MapMonitorService.Snapshot;
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                GUILayout.Label(EditorApplication.isPlaying ? (EditorApplication.isPaused ? "PAUSED" : "LIVE") : "EDIT MODE", EditorStyles.boldLabel);
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("Copy Report", EditorStyles.toolbarButton)) EditorGUIUtility.systemCopyBuffer = MapMonitorService.ReportJson();
                if (GUILayout.Button("Export JSON", EditorStyles.toolbarButton))
                {
                    string path = EditorUtility.SaveFilePanel("Export Map Monitor Report", "", "MapMonitor-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".json", "json");
                    try { MapMonitorService.ExportReport(path); }
                    catch (Exception ex) { EditorUtility.DisplayDialog("Report export failed", ex.Message, "OK"); }
                }
            }
            GUILayout.Space(8);
            GUILayout.Label("Map Monitor", heading);
            GUILayout.Label("Live session observer | No scene or asset edits | 0.25 s sampling", EditorStyles.miniLabel);
            if (!EditorApplication.isPlaying)
                EditorGUILayout.HelpBox(value?.hasSession == true
                    ? "Play Mode stopped. This is the last recorded snapshot, not the current scene state. Start Play Mode from Map to observe a new run."
                    : "Open Map and enter Play Mode. The monitor attaches automatically and follows scene changes. It does not create or reset a run.", MessageType.Info);
            else if (value == null || !value.hasSession)
                EditorGUILayout.HelpBox("Waiting for RunSession. Start the adventure from Map; opening a receiver scene directly does not create map progression.", MessageType.Info);
            else if (!value.hasProgress)
                EditorGUILayout.HelpBox("RunSession exists, but map progression has not initialized. No state has been changed by this monitor.", MessageType.Warning);

            tab = GUILayout.Toolbar(tab, Tabs);
            scroll = EditorGUILayout.BeginScrollView(scroll);
            if (tab == 3) DrawHistory();
            else if (value?.hasSession == true)
            {
                if (tab == 0) DrawOverview(value);
                else if (tab == 1) DrawNodes(value);
                else DrawDeck(value);
            }
            EditorGUILayout.EndScrollView();
            DrawControls(value);
        }

        private void DrawOverview(MapMonitorSnapshot s)
        {
            Section("Adventure / Location");
            Field("Scene", s.scene);
            Field("Phase", s.hasProgress ? s.phase.ToString() : "Not initialized");
            Field("Committed location", s.currentNode);
            Field("Moving from / to", s.encounterId == null ? "No active departure" : s.fromNode + " -> " + s.targetNode);
            Field("Encounter type", s.kind);
            Field("Run ID", s.runId); Field("Encounter ID", s.encounterId);
            if (!string.IsNullOrEmpty(s.testStartNode))
                EditorGUILayout.HelpBox("TEST RUN starting at " + s.testStartNode + ". Earlier layers were skipped; no prior wins or rewards were simulated.", MessageType.Warning);
            Field("Selected / hovered", (s.selectedNode ?? "None") + " / " + (s.hoveredNode ?? "None"));
            Field("Input / transition", (s.hasMap ? (s.canInteract ? "Map input enabled" : "Map input locked") : "Map UI not loaded") +
                " | " + (s.busy ? "Transition busy" : "Transition idle"));
            Field("Progress", $"{s.completed}/{s.nodes.Length} completed; {s.nodes.Count(n => n.state == MapLocationState.Available)} available");
            if (s.phase == MapProgressPhase.Traveling || s.phase == MapProgressPhase.Loading)
                EditorGUILayout.HelpBox("The committed location changes only after the receiver confirms readiness. The target above is not yet committed.", MessageType.Info);
            if (!string.IsNullOrEmpty(s.notice)) EditorGUILayout.HelpBox(s.notice, MessageType.Warning);
            if (s.hasCore)
            {
                Field("Fire seed position", s.corePosition.ToString("F3"));
                Field("Presentation", (s.moving ? "Moving" : "Resting") + $" | {s.particles} live particles");
            }
            Section("Player State");
            var rect = GUILayoutUtility.GetRect(10, 22, GUILayout.ExpandWidth(true));
            EditorGUI.ProgressBar(rect, s.maxHealth > 0 ? Mathf.Clamp01((float)s.health / s.maxHealth) : 0, $"HP {s.health} / {s.maxHealth}");
            Field("Damage / shield", $"{s.damage} / {s.shield}");
            Field("Attack speed", s.attackInterval > 0 ? $"{1f / s.attackInterval:F2} attacks/s ({s.attackInterval:F2} s/attack)" : "Invalid attack interval");
            Field("Elixir", $"{s.elixir:F2} / {s.maxElixir:F2}");
            Field("Deck capacity", $"{s.capacity}/{RunSession.CapacityLimit} points (not card count)");
            GUILayout.Label("These are the session's current values, including captured battle effects; they are not a separate base-stat breakdown.", wrap);
            Section("Current / Last Non-combat Receipt");
            var receipt = s.receipt;
            if (receipt == null) GUILayout.Label("No recovery or opportunity receipt yet.", wrap);
            else
            {
                Field("Receipt", receipt.kind + " | " + receipt.nodeId);
                Field("Encounter ID", receipt.encounterId);
                Field("Status", $"Resolved: {receipt.resolved} | Completed: {receipt.completed}");
                if (receipt.kind == MapEncounterKind.Recovery.ToString())
                    Field("Recovery", $"{receipt.before} -> {receipt.after} / {receipt.maxHealth} (+{receipt.recovered})");
                else
                {
                    Field("Offered candidates", receipt.offers.ToString());
                    Field("Granted instance", receipt.grantedInstance);
                }
            }
            Field("Snapshot UTC", s.capturedUtc);
        }

        private void DrawNodes(MapMonitorSnapshot s)
        {
            Section($"{s.nodes.Length} Nodes / {s.nodes.Sum(n => n.next.Length)} Explicit Roads");
            GUILayout.Label("CURRENT = committed location; TARGET = active encounter destination. Selecting an object here never travels or unlocks a node.", wrap);
            filter = EditorGUILayout.TextField("Filter ID / type / state", filter);
            foreach (var node in s.nodes)
            {
                if (!Matches(node.id + " " + node.kind + " " + node.state)) continue;
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        string flags = (node.id == s.currentNode ? "  [CURRENT]" : "") + (node.id == s.targetNode ? "  [TARGET]" : "");
                        GUILayout.Label(node.id + flags, EditorStyles.boldLabel);
                        GUILayout.FlexibleSpace();
                        using (new EditorGUI.DisabledScope(!IsLiveMap()))
                            if (GUILayout.Button("Locate", GUILayout.Width(60))) LocateNode(node.id);
                    }
                    Field("Type / state", node.kind + " / " + node.state);
                    GUILayout.Label("Next: " + (node.next.Length == 0 ? "End of adventure" : string.Join(", ", node.next)), wrap);
                    if (node.traveled.Length > 0) GUILayout.Label("Traversed: " + string.Join(", ", node.traveled), wrap);
                }
            }
        }

        private void DrawDeck(MapMonitorSnapshot s)
        {
            Section("Offering");
            Field("Sacrifice allowance", s.sacrificeUsed ? "Used for the next battle" : "Available");
            Field("Pending effect", s.pending.isValid ? s.pending.effectType.ToString() : "None");
            if (s.pending.isValid)
            {
                Field("Source / magnitude", s.pending.sourceCardId + " / " + s.pending.magnitude);
                EditorGUILayout.HelpBox("Pending effects are consumed only after a battle receiver is ready. Opportunity and recovery do not consume or reset them.", MessageType.Info);
            }
            Section($"Deck: {s.cards.Count(c => !c.sacrificed)} active instances | {s.capacity}/{RunSession.CapacityLimit} capacity points");
            if (s.capacity > RunSession.CapacityLimit) EditorGUILayout.HelpBox("Capacity exceeds the limit. Existing cards are retained; further additions are blocked.", MessageType.Warning);
            filter = EditorGUILayout.TextField("Filter name / instance", filter);
            foreach (var card in s.cards)
            {
                if (!Matches(card.name + " " + card.instanceId + " " + card.cardId)) continue;
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        GUILayout.Label(card.name, EditorStyles.boldLabel);
                        GUILayout.FlexibleSpace();
                        if (!string.IsNullOrEmpty(card.assetPath) && GUILayout.Button("Ping Asset", GUILayout.Width(85)))
                            EditorGUIUtility.PingObject(AssetDatabase.LoadAssetAtPath<CardDefinition>(card.assetPath));
                    }
                    Field("Instance ID", card.instanceId);
                    Field("Definition / cost", card.cardId + " / " + card.cost);
                    Field("Status", card.missing ? "Missing card definition" : card.sacrificed ? "Sacrificed (excluded from active deck)" : "Active");
                }
            }
        }

        private void DrawHistory()
        {
            Section($"Recent Events ({MapMonitorService.History.entries.Count}/{MapMonitorHistory.Limit})");
            GUILayout.Label("Recorded while Play Mode runs, even with this window closed. Cleared on the next Play session; export before closing Unity. No per-frame stat spam.", wrap);
            using (new EditorGUILayout.HorizontalScope())
            {
                filter = EditorGUILayout.TextField("Filter", filter);
                if (GUILayout.Button("Clear Log", GUILayout.Width(85)) && EditorUtility.DisplayDialog("Clear monitor history?",
                    "Only the local diagnostic history will be cleared. The adventure is unchanged.", "Clear", "Cancel")) MapMonitorService.ClearHistory();
            }
            var entries = MapMonitorService.History.entries;
            for (int i = entries.Count - 1; i >= 0; i--)
            {
                var entry = entries[i];
                if (!Matches(entry.category + " " + entry.message)) continue;
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    GUILayout.Label(entry.utc + "  |  " + entry.category, EditorStyles.miniBoldLabel);
                    GUILayout.Label(entry.message, wrap);
                }
            }
        }

        private void DrawControls(MapMonitorSnapshot value)
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    using (new EditorGUI.DisabledScope(!EditorApplication.isPlaying))
                    {
                        if (GUILayout.Button(EditorApplication.isPaused ? "Resume" : "Pause"))
                        {
                            EditorApplication.isPaused = !EditorApplication.isPaused;
                            MapMonitorService.RecordAction(EditorApplication.isPaused ? "Editor paused." : "Editor resumed.");
                        }
                        using (new EditorGUI.DisabledScope(!EditorApplication.isPaused))
                            if (GUILayout.Button("Step Frame")) EditorApplication.Step();
                    }
                    using (new EditorGUI.DisabledScope(MapMonitorService.Session == null || !EditorApplication.isPlaying))
                        if (GUILayout.Button("Select Session")) Selection.activeGameObject = MapMonitorService.Session.gameObject;
                    using (new EditorGUI.DisabledScope(!IsLiveMap()))
                        if (GUILayout.Button("Locate Current")) LocateNode(value?.currentNode);
                }
                testControls = EditorGUILayout.ToggleLeft("Enable destructive test controls for this window session", testControls);
                if (testControls)
                {
                    GUILayout.Label("New Run resets route progress, cards, offerings and player attributes. Allowed only on an idle Map, never during travel, loading or an encounter. Resume before using it.", wrap);
                    using (new EditorGUI.DisabledScope(!CanRestartNow(value)))
                    {
                        if (GUILayout.Button("Start New Test Run...")) ConfirmRestart(value.runId);
                        var options = value?.nodes ?? Array.Empty<MapMonitorNode>();
                        if (options.Length > 0)
                        {
                            int index = Array.FindIndex(options, n => n.id == testStartNode);
                            if (index < 0) index = 0;
                            index = EditorGUILayout.Popup("Start Node", index,
                                options.Select(n => n.id.Replace("level_", "L") + " - " + n.kind).ToArray());
                            testStartNode = options[index].id;
                            overrideStartingHealth = EditorGUILayout.Toggle("Set Starting HP", overrideStartingHealth);
                            int max = MapMonitorService.Session != null ? MapMonitorService.Session.StartingMaxHealthForTesting : 0;
                            if (overrideStartingHealth) startingHealth = EditorGUILayout.IntField("Starting HP (1-" + max + ")", startingHealth);
                            bool validHealth = !overrideStartingHealth || (startingHealth >= 1 && startingHealth <= max);
                            if (!validHealth) EditorGUILayout.HelpBox("Enter a living HP value within the starting maximum. No maximum health change is made.", MessageType.Warning);
                            using (new EditorGUI.DisabledScope(!validHealth))
                                if (GUILayout.Button("Start Test Encounter..."))
                                    ConfirmTestEncounter(value.runId, testStartNode, overrideStartingHealth ? startingHealth : (int?)null);
                        }
                    }
                    GUILayout.Label("Start Test Encounter creates a fresh run at the selected node, skips earlier layers without rewards, and enters through normal scene loading.", wrap);
                }
                if (!string.IsNullOrEmpty(testNotice)) EditorGUILayout.HelpBox(testNotice, MessageType.Info);
            }
        }

        private static bool IsLiveMap() => EditorApplication.isPlaying && MapMonitorService.Map != null &&
            MapMonitorService.Map.isActiveAndEnabled && MapMonitorService.Map.TravelView != null &&
            MapMonitorService.Map.TravelView.gameObject.scene == SceneManager.GetActiveScene();

        private static bool CanRestartNow(MapMonitorSnapshot value) => MapMonitorData.CanRestart(value,
            EditorApplication.isPlaying, EditorApplication.isPaused,
            IsLiveMap() && MapMonitorService.Session != null && MapMonitorService.Map.Session == MapMonitorService.Session &&
            value?.runId == MapMonitorService.Session.Progress?.RunId);

        private void ConfirmRestart(string expectedRun)
        {
            if (!EditorUtility.DisplayDialog("Start a new test run?",
                "This discards the current run's progress, cards and resources and restores the starting setup. No scene or asset is saved. This cannot be undone.",
                "Reset Current Run", "Cancel")) return;
            EditorApplication.delayCall += () =>
            {
                MapMonitorService.Sample();
                if (MapMonitorService.Snapshot?.runId != expectedRun || !CanRestartNow(MapMonitorService.Snapshot) ||
                    !MapMonitorService.Map.TryRestartRunForTesting())
                {
                    MapMonitorService.RecordAction("New run rejected: state changed or Map is not idle."); return;
                }
                testControls = false;
                MapMonitorService.RecordAction("Developer confirmed a new test run. Previous run: " + expectedRun);
                MapMonitorService.Sample();
            };
        }

        private void ConfirmTestEncounter(string expectedRun, string nodeId, int? health)
        {
            string description = "Replace the current adventure with a TEST RUN starting at " + nodeId.Replace("level_", "L") +
                "?\nStarting HP: " + (health.HasValue ? health.Value.ToString() : "Default") +
                "\nProgress, cards and offerings will reset. Earlier layers are skipped without rewards. The selected encounter will load normally. No scene or asset is saved. This cannot be undone.";
            if (!EditorUtility.DisplayDialog("Start test encounter?", description, "Reset and Enter", "Cancel")) return;
            EditorApplication.delayCall += () =>
            {
                MapMonitorService.Sample();
                string reason;
                bool accepted = false;
                if (MapMonitorService.Snapshot?.runId != expectedRun || !CanRestartNow(MapMonitorService.Snapshot))
                    reason = "Test start rejected: the run changed or Map is no longer idle.";
                else accepted = MapMonitorService.Map.TryStartTestEncounter(nodeId, health, out reason);
                testNotice = accepted ? "Test departure started at " + nodeId + "." : reason;
                testControls = false;
                MapMonitorService.RecordAction(accepted ? "Started TEST RUN at " + nodeId +
                    "; starting HP " + (health.HasValue ? health.Value.ToString() : "Default") + "; previous run " + expectedRun : reason);
                MapMonitorService.Sample(); Repaint();
            };
        }

        private static void LocateNode(string id)
        {
            if (!IsLiveMap() || string.IsNullOrEmpty(id)) return;
            var node = MapMonitorService.Map.Nodes.FirstOrDefault(n => n != null && n.NodeId == id);
            if (node == null) return;
            Selection.activeGameObject = node.gameObject;
            EditorGUIUtility.PingObject(node.gameObject);
            if (SceneView.lastActiveSceneView != null) SceneView.lastActiveSceneView.FrameSelected();
        }

        private bool Matches(string text) => string.IsNullOrWhiteSpace(filter) ||
            text.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0;
        private static void Section(string title) { GUILayout.Space(10); GUILayout.Label(title, EditorStyles.boldLabel); }
        private static void Field(string label, string value)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label(label, GUILayout.Width(165));
                EditorGUILayout.SelectableLabel(string.IsNullOrEmpty(value) ? "None" : value, GUILayout.Height(EditorGUIUtility.singleLineHeight));
            }
        }
    }
}
