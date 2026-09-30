using UnityEngine;

namespace Deinosavros.MapReview
{
    public sealed class MapRunHost : MonoBehaviour
    {
        public static MapRunHost Instance { get; private set; }
        public MapRunState State { get; private set; }
        public MapEncounterSimulation Simulation { get; set; }
        public MapEncounterResult LastResult { get; set; }
        private string observedRunId;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatic() => Instance = null;
        public static MapRunHost Ensure(MapGraphDefinition graph, CardDefinition[] deck)
        {
            if (Instance != null)
            {
                if (Instance.State.Player == null) Instance.State.BeginNewRun(graph, deck);
                return Instance;
            }
            var host = new GameObject("Map Review Runtime").AddComponent<MapRunHost>();
            host.State.BeginNewRun(graph, deck); return host;
        }
        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this; State = new MapRunState(); State.Changed += OnStateChanged;
            DontDestroyOnLoad(gameObject);
        }
        private void OnStateChanged()
        {
            if (observedRunId == State.RunId) return;
            observedRunId = State.RunId; Simulation = null; LastResult = null;
        }
        private void OnDestroy()
        {
            if (State != null) State.Changed -= OnStateChanged;
            if (Instance == this) Instance = null;
        }
    }
}
