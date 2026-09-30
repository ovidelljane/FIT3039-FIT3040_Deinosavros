using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Deinosavros.MapTools.Editor
{
    // Editor-only observer. It never creates sessions, acknowledges encounters or changes gameplay.
    [InitializeOnLoad]
    public static class MapMonitorService
    {
        private const string CacheKey = "Deinosavros.MapMonitor.History";
        private const string SnapshotKey = "Deinosavros.MapMonitor.Snapshot";
        private static RunSession session;
        private static MapProgressState progress;
        private static MapController map;
        private static double nextSample, nextFind;
        private static int sceneHandle;
        public static MapMonitorSnapshot Snapshot { get; private set; }
        public static MapMonitorHistory History { get; private set; }
        public static RunSession Session => session;
        public static MapController Map => map;
        public static event Action Changed;

        static MapMonitorService()
        {
            History = Restore<MapMonitorHistory>(CacheKey) ?? new MapMonitorHistory();
            Snapshot = Restore<MapMonitorSnapshot>(SnapshotKey);
            EditorApplication.update += Update;
            EditorApplication.playModeStateChanged += PlayModeChanged;
            AssemblyReloadEvents.beforeAssemblyReload += BeforeReload;
        }

        private static T Restore<T>(string key) where T : class
        {
            string json = SessionState.GetString(key, "");
            if (string.IsNullOrEmpty(json)) return null;
            try { return JsonUtility.FromJson<T>(json); }
            catch (ArgumentException) { return null; }
        }

        private static void Update()
        {
            if (!EditorApplication.isPlaying || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
            if (EditorApplication.timeSinceStartup < nextSample) return;
            nextSample = EditorApplication.timeSinceStartup + .25;
            Sample();
        }

        public static void Sample()
        {
            if (!EditorApplication.isPlaying) return;
            var owner = RunSession.Instance;
            var state = owner != null ? owner.Progress : null;
            if (session != owner || !ReferenceEquals(progress, state))
            {
                Unbind(); session = owner; progress = state;
                if (session != null) session.DeckChanged += Sample;
                if (progress != null) progress.Changed += Sample;
                map = null; nextFind = 0;
            }
            var scene = SceneManager.GetActiveScene();
            if (scene.handle != sceneHandle) { sceneHandle = scene.handle; map = null; nextFind = 0; }
            if (map == null && EditorApplication.timeSinceStartup >= nextFind)
            {
                nextFind = EditorApplication.timeSinceStartup + 1;
                foreach (var candidate in UnityEngine.Object.FindObjectsByType<MapController>(FindObjectsSortMode.None))
                    // MapController shares the persistent session root. Its view belongs to the loaded Map.
                    if (candidate.isActiveAndEnabled && candidate.TravelView != null &&
                        candidate.TravelView.gameObject.scene == scene && candidate.Session == session)
                    { map = candidate; break; }
            }
            Snapshot = MapMonitorData.Capture(session, scene.name, map,
                session != null ? session.GetComponent<MapTravelCoordinator>() : null);
            string previousEntry = History.entries.Count == 0 ? null : History.entries[History.entries.Count - 1].utc;
            History.Observe(Snapshot);
            if (History.entries.Count > 0 && previousEntry != History.entries[History.entries.Count - 1].utc) Persist();
            Changed?.Invoke();
        }

        public static void RecordAction(string message)
        {
            History.Add("Developer", message); Persist(); Changed?.Invoke();
        }

        public static void ClearHistory()
        {
            History = new MapMonitorHistory(); History.Add("Monitor", "Local history cleared by developer.");
            History.Observe(Snapshot); Persist(); Changed?.Invoke();
        }

        private static void PlayModeChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.EnteredPlayMode)
            {
                History = new MapMonitorHistory(); Snapshot = null;
                History.Add("Monitor", "Play session started. Observing existing state only.");
                nextSample = nextFind = 0; Sample();
            }
            else if (state == PlayModeStateChange.ExitingPlayMode)
            {
                Sample(); History.Add("Monitor", "Play session stopped. Last snapshot retained for inspection.");
                Persist(); Unbind();
            }
            else if (state == PlayModeStateChange.EnteredEditMode) { Unbind(); Changed?.Invoke(); }
        }

        private static void Unbind()
        {
            if (session != null) session.DeckChanged -= Sample;
            if (progress != null) progress.Changed -= Sample;
            session = null; progress = null; map = null;
        }

        private static void Persist()
        {
            SessionState.SetString(CacheKey, JsonUtility.ToJson(History));
            SessionState.SetString(SnapshotKey, Snapshot != null ? JsonUtility.ToJson(Snapshot) : "");
        }

        private static void BeforeReload() { Persist(); Unbind(); }

        [Serializable]
        private sealed class Report
        {
            public string exportedUtc;
            public string source = "Unity Editor only. State changes are event-driven; live values sample every 0.25 seconds. History is capped at 200 entries, not a save game.";
            public bool playing, paused;
            public MapMonitorSnapshot snapshot;
            public MapMonitorHistory history;
        }

        public static string ReportJson() => JsonUtility.ToJson(new Report
        {
            exportedUtc = DateTime.UtcNow.ToString("O"), playing = EditorApplication.isPlaying,
            paused = EditorApplication.isPaused, snapshot = Snapshot, history = History
        }, true);

        public static void ExportReport(string path)
        {
            if (string.IsNullOrEmpty(path)) return;
            File.WriteAllText(path, ReportJson());
        }
    }
}
