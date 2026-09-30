using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Deinosavros.MapTools.Editor
{
    // Explicit opt-in smoke test; never runs on import or against an existing Play session.
    [InitializeOnLoad]
    public static class MapMonitorPlayVerification
    {
        private const string Key = "Deinosavros.MapMonitor.Verification";
        private const string Result = "Library/MapMonitor/play-verification.txt";
        private static double nextTick;
        private static int drawsBefore;

        static MapMonitorPlayVerification()
        {
            EditorApplication.update += Tick;
            Application.logMessageReceived += OnLog;
        }

        [MenuItem("Tools/Map/Verify Monitor Play Mode")]
        public static void Start() => Start(false);

        [MenuItem("Tools/Map/Verify Monitor Test Starts")]
        public static void StartAtNode() => Start(true);

        private static void Start(bool testStarts)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || SessionState.GetInt(Key, 0) != 0)
                throw new InvalidOperationException("Monitor smoke test requires Edit Mode, not an existing Play session.");
            var scene = SceneManager.GetActiveScene();
            if (scene.path != "Assets/Scenes/Map.unity")
                throw new InvalidOperationException("Open the formal Map before the monitor smoke test.");
            bool wasDirty = scene.isDirty;
            Directory.CreateDirectory("Library/MapMonitor");
            if (!EditorSceneManager.SaveScene(scene, "Library/MapMonitor/Map-before-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff") + ".unity", true))
                throw new IOException("Could not back up the live Map. No test was started.");
            if (wasDirty && !scene.isDirty) EditorSceneManager.MarkSceneDirty(scene);
            File.WriteAllText(Result, "RUNNING: explicit monitor Play Mode smoke test.\n");
            SessionState.SetBool(Key + ".testStarts", testStarts);
            SessionState.SetBool(Key + ".dirty", wasDirty);
            SessionState.SetString(Key + ".error", "");
            SessionState.SetBool(Key + ".background", Application.runInBackground);
            SessionState.SetInt(Key, 1);
            SessionState.SetFloat(Key + ".deadline", (float)EditorApplication.timeSinceStartup + 180);
            MapMonitorWindow.Open();
            EditorApplication.isPaused = false;
            EditorApplication.isPlaying = true;
        }

        private static void OnLog(string message, string trace, LogType type)
        {
            if (SessionState.GetInt(Key, 0) == 0 || (type != LogType.Error && type != LogType.Exception && type != LogType.Assert)) return;
            SessionState.SetString(Key + ".error", SessionState.GetString(Key + ".error", "") + message + "\n" + trace + "\n");
        }

        private static void Tick()
        {
            int stage = SessionState.GetInt(Key, 0);
            if (stage == 0 || EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.timeSinceStartup < nextTick) return;
            nextTick = EditorApplication.timeSinceStartup + .6;
            try
            {
                if (stage == 10)
                {
                    if (EditorApplication.isPlayingOrWillChangePlaymode) return;
                    SessionState.SetInt(Key, 0);
                    Application.runInBackground = SessionState.GetBool(Key + ".background", false);
                    Require(SceneManager.GetActiveScene().path == "Assets/Scenes/Map.unity" &&
                        SceneManager.GetActiveScene().isDirty == SessionState.GetBool(Key + ".dirty", false), "Original Map and its dirty state restored after Play Mode.");
                    var window = EditorWindow.GetWindow<MapMonitorWindow>();
                    using (var serialized = new SerializedObject(window))
                    { serialized.FindProperty("tab").intValue = 0; serialized.ApplyModifiedPropertiesWithoutUndo(); }
                    window.Repaint();
                    File.AppendAllText(Result, "PASS: Play Mode stopped; original Map and dirty state restored without overwriting the scene. Historical snapshot remains available.\n");
                    return;
                }
                Require(EditorApplication.timeSinceStartup < SessionState.GetFloat(Key + ".deadline", 0), "Monitor smoke test timeout.");
                if (!EditorApplication.isPlaying) return;
                // Only this explicit test needs off-focus player frames. Never change PlayerSettings.
                Application.runInBackground = true;
                EditorApplication.QueuePlayerLoopUpdate();
                Require(string.IsNullOrEmpty(SessionState.GetString(Key + ".error", "")), "Runtime or GUI error: " + SessionState.GetString(Key + ".error", ""));
                MapMonitorService.Sample();
                var value = MapMonitorService.Snapshot;
                if (stage == 1)
                {
                    if (value?.hasProgress != true || MapMonitorService.Map == null) return;
                    Require(value.nodes.Length == 14 && value.nodes.Sum(n => n.next.Length) == 19, "Live graph coverage.");
                    Require(value.hasCore && value.health > 0 && value.cards.Length > 0, "Live fire seed, stats and cards.");
                    SessionState.SetString(Key + ".run", value.runId);
                    SessionState.SetInt(Key + ".cards", value.cards.Length);
                    File.AppendAllText(Result, "PASS: automatic live session attachment; 14 nodes, 19 roads, player state, cards and fire seed.\n");
                    SessionState.SetInt(Key, 2);
                }
                else if (stage >= 2 && stage <= 5)
                {
                    var window = EditorWindow.GetWindow<MapMonitorWindow>();
                    if (SessionState.GetBool(Key + ".testStarts", false))
                        typeof(MapMonitorWindow).GetField("testControls", System.Reflection.BindingFlags.Instance |
                            System.Reflection.BindingFlags.NonPublic).SetValue(window, true);
                    using (var serialized = new SerializedObject(window))
                    { serialized.FindProperty("tab").intValue = stage - 2; serialized.ApplyModifiedPropertiesWithoutUndo(); }
                    window.Repaint(); drawsBefore = MapMonitorWindow.DrawCount;
                    SessionState.SetInt(Key, stage + 20);
                }
                else if (stage >= 22 && stage <= 25)
                {
                    if (MapMonitorWindow.DrawCount <= drawsBefore) return;
                    File.AppendAllText(Result, "PASS: live monitor tab " + (stage - 22) + " GUI rendered without errors.\n");
                    SessionState.SetInt(Key, stage - 19);
                }
                else if (stage == 6)
                {
                    Require(value.runId == SessionState.GetString(Key + ".run", "") && value.completed == 0,
                        "Observing all pages does not advance or reset the adventure.");
                    Require(MapMonitorData.CanRestart(value, true, false, true), "Live idle Map reset gate.");
                    if (SessionState.GetBool(Key + ".testStarts", false))
                    {
                        VerifyStartRejections(MapMonitorService.Map, value.runId);
                        SessionState.SetInt(Key + ".maxHealth", MapMonitorService.Session.StartingMaxHealthForTesting);
                        Require(MapMonitorService.Map.TryStartTestEncounter("level_02_02", 50, out string reason), reason ?? "Recovery test start.");
                        string seededRun = MapMonitorService.Session.Progress.RunId;
                        Require(!MapMonitorService.Map.TryStartTestEncounter("level_02_02", 50, out _) &&
                            MapMonitorService.Session.Progress.RunId == seededRun, "Duplicate start rejected while departure is locked.");
                        MapMonitorService.RecordAction("Smoke test started L02_02 at 50 HP using the test start command.");
                        SessionState.SetInt(Key, 30); return;
                    }
                    Require(MapMonitorService.Map.TryRestartRunForTesting(), "Confirmed reset hook executes on idle Map.");
                    MapMonitorService.RecordAction("Smoke test explicitly exercised the guarded new-run hook.");
                    MapMonitorService.Sample();
                    SessionState.SetInt(Key, 7);
                }
                else if (stage == 7)
                {
                    Require(value.runId != SessionState.GetString(Key + ".run", "") && value.currentNode == "level_01_01" &&
                        value.completed == 0 && value.targetNode == null && value.selectedNode == null, "New run clears route and selection.");
                    Require(value.cards.Length == SessionState.GetInt(Key + ".cards", 0) && !value.sacrificeUsed && !value.pending.isValid,
                        "New run restores cards and offerings.");
                    Require(value.canInteract && Vector3.Distance(MapMonitorService.Map.TravelView.FloorPosition,
                        MapMonitorService.Map.TravelView.SitePoint(value.currentNode)) < .001f, "New run restores input and fire seed.");
                    File.AppendAllText(Result, "PASS: guarded new-run hook resets identity, route, selection, cards, offering, input and fire seed.\n");
                    SessionState.SetString(Key + ".run", value.runId);
                    var map = MapMonitorService.Map;
                    map.SelectNode(map.Nodes.Single(n => n.NodeId == value.currentNode));
                    SessionState.SetInt(Key, 8);
                }
                else if (stage == 8)
                {
                    if (value?.phase != MapProgressPhase.InEncounter) return;
                    Require(value.runId == SessionState.GetString(Key + ".run", "") && value.kind == "Battle" &&
                        value.scene == "Deinosavros" && !value.hasMap && value.currentNode == "level_01_01", "Monitor follows persistent session into real battle.");
                    Require(value.health > 0 && value.cards.Length > 0 && value.encounterId != null, "Battle snapshot retains player, deck and encounter identity.");
                    Require(!MapMonitorData.CanRestart(value, true, false, true), "Reset disabled in real battle.");
                    Require(MapMonitorService.History.entries.Any(e => e.message.Contains("receiver confirmed")), "Real readiness event recorded.");
                    MapMonitorService.ExportReport("Library/MapMonitor/live-report.json");
                    File.AppendAllText(Result, "PASS: real Map -> Battle transition observed with same run, committed location, stats, cards, readiness history and disabled reset; live report exported.\n");
                    SessionState.SetInt(Key, 10); EditorApplication.isPlaying = false;
                }
                else if (stage == 30)
                {
                    if (value?.phase != MapProgressPhase.InEncounter || value.busy || value.receipt == null) return;
                    var receiver = MapNonCombatController.Find(SceneManager.GetActiveScene());
                    if (receiver == null || !receiver.ContinueButton.interactable) return;
                    int max = SessionState.GetInt(Key + ".maxHealth", 100);
                    int after = Mathf.Min(max, 50 + (int)(((long)max * 15 + 99) / 100));
                    Require(value.scene == "MapRecovery" && value.currentNode == "level_02_02" && value.testStartNode == "level_02_02" &&
                        value.completed == 0 && value.receipt.before == 50 && value.health == after, "Direct recovery start uses real receiver and custom HP.");
                    Require(MapMonitorService.Session.TryResolveRecovery(value.encounterId, out _) && MapMonitorService.Session.PlayerHealth == after,
                        "Repeated recovery initialization never heals twice.");
                    Require(value.nodes.Single(n => n.id == "level_01_01").state == MapLocationState.Skipped, "Skipped start is not a fake win.");
                    SessionState.SetInt(Key + ".healed", after);
                    receiver.ContinueButton.onClick.Invoke(); SessionState.SetInt(Key, 31);
                }
                else if (stage == 31)
                {
                    if (!ReadyMap(value)) return;
                    Require(value.currentNode == "level_02_02" && value.completed == 1 && value.health == SessionState.GetInt(Key + ".healed", 0),
                        "Recovery returns to exact selected node with saved health.");
                    Require(value.nodes.Where(n => n.state == MapLocationState.Available).Select(n => n.id).OrderBy(n => n)
                        .SequenceEqual(new[] { "level_03_02", "level_03_03" }), "Normal outgoing routes open after recovery.");
                    MapMonitorService.ExportReport("Library/MapMonitor/test-start-recovery-report.json");
                    File.AppendAllText(Result, "PASS: L02_02 direct start, 50 HP, one recovery transaction, Continue return and only L03_02/L03_03 unlocked.\n");
                    Require(MapMonitorService.Map.TryStartTestEncounter("level_02_01", null, out string reason), reason ?? "Opportunity test start.");
                    SessionState.SetInt(Key, 32);
                }
                else if (stage == 32)
                {
                    if (value?.phase != MapProgressPhase.InEncounter || value.busy || value.receipt == null) return;
                    var receiver = MapNonCombatController.Find(SceneManager.GetActiveScene());
                    if (receiver == null || !receiver.ContinueButton.interactable) return;
                    Require(value.scene == "MapOpportunity" && value.currentNode == "level_02_01" && value.testStartNode == "level_02_01" &&
                        value.receipt.offers == 0 && value.receipt.grantedInstance == null && value.cards.Length == SessionState.GetInt(Key + ".cards", 0),
                        "Opportunity test entry is the production no-grant placeholder.");
                    receiver.ContinueButton.onClick.Invoke(); SessionState.SetInt(Key, 33);
                }
                else if (stage == 33)
                {
                    if (!ReadyMap(value)) return;
                    Require(value.currentNode == "level_02_01" && value.completed == 1 && value.nodes.Count(n => n.state == MapLocationState.Available) == 1 &&
                        value.nodes.Single(n => n.id == "level_03_01").state == MapLocationState.Available, "Opportunity returns and opens only its real next branch.");
                    Require(MapMonitorService.Map.TryRestartRunForTesting(), "Return to normal new run after test seed.");
                    SessionState.SetInt(Key, 34);
                }
                else if (stage == 34)
                {
                    if (!ReadyMap(value)) return;
                    Require(value.testStartNode == null && value.currentNode == "level_01_01" && value.completed == 0 && !value.sacrificeUsed && !value.pending.isValid,
                        "Normal New Run removes the test seed and restores true start.");
                    File.AppendAllText(Result, "PASS: opportunity placeholder grants nothing, returns to L02_01; normal New Run clears seed and restores L01_01.\n");
                    Require(MapMonitorService.Map.TryStartTestEncounter("level_06_01", null, out string reason), reason ?? "Boss test start.");
                    SessionState.SetInt(Key, 35);
                }
                else if (stage == 35)
                {
                    if (value?.phase != MapProgressPhase.InEncounter || value.busy) return;
                    Require(value.scene == "Deinosavros" && value.kind == "Boss" && value.currentNode == "level_06_01" &&
                        value.testStartNode == "level_06_01" && value.completed == 0 && MapMonitorService.Session.CurrentEncounterIsBoss,
                        "Boss test uses normal battle receiver with Boss ticket and no forged wins.");
                    Require(!MapMonitorData.CanRestart(value, true, false, true), "Test start/reset disabled in an active Boss encounter.");
                    MapMonitorService.ExportReport("Library/MapMonitor/test-start-boss-report.json");
                    File.AppendAllText(Result, "PASS: Boss direct start reaches real battle with correct Boss identity; test controls blocked during encounter.\n");
                    SessionState.SetInt(Key, 10); EditorApplication.isPlaying = false;
                }
            }
            catch (Exception ex)
            {
                SessionState.SetInt(Key, 0);
                Application.runInBackground = SessionState.GetBool(Key + ".background", false);
                File.AppendAllText(Result, "FAIL: " + ex + "\n");
                EditorApplication.isPaused = false;
                if (EditorApplication.isPlaying) EditorApplication.isPlaying = false;
            }
        }

        private static void Require(bool condition, string message)
        { if (!condition) throw new InvalidOperationException(message); }

        private static bool ReadyMap(MapMonitorSnapshot s) => s != null && s.hasMap && s.phase == MapProgressPhase.OnMap && !s.busy &&
            MapMonitorService.Map != null && s.canInteract;

        private static void VerifyStartRejections(MapController map, string originalRun)
        {
            Require(!map.TryStartTestEncounter("missing", 50, out _), "Invalid node rejected.");
            Require(!map.TryStartTestEncounter("level_02_02", 0, out _) &&
                !map.TryStartTestEncounter("level_02_02", map.Session.StartingMaxHealthForTesting + 1, out _), "Dead and over-maximum HP rejected.");
            EditorApplication.isPaused = true;
            try { Require(!map.TryStartTestEncounter("level_02_02", 50, out _), "Paused test start rejected."); }
            finally { EditorApplication.isPaused = false; }
            var original = map.EncounterRouting;
            var missingScene = UnityEngine.Object.Instantiate(original);
            missingScene.hideFlags = HideFlags.HideAndDontSave; missingScene.recoveryScene = "Missing_Monitor_Test_Scene";
            using (var serialized = new SerializedObject(map))
            {
                try
                {
                    serialized.FindProperty("encounterRouting").objectReferenceValue = missingScene;
                    serialized.ApplyModifiedPropertiesWithoutUndo();
                    Require(!map.TryStartTestEncounter("level_02_02", 50, out string reason) && reason.Contains("unavailable"),
                        "Missing receiver scene rejected before reset.");
                }
                finally
                {
                    serialized.FindProperty("encounterRouting").objectReferenceValue = original;
                    serialized.ApplyModifiedPropertiesWithoutUndo(); UnityEngine.Object.DestroyImmediate(missingScene);
                }
            }
            Require(map.Session.Progress.RunId == originalRun && map.Session.Progress.CompletedCount == 0, "Rejected requests do not reset or mutate the old adventure.");
            File.AppendAllText(Result, "PASS: invalid node, invalid HP, pause and missing scene all rejected without resetting the existing run.\n");
        }
    }
}
