using System;
using System.Collections.Generic;
using System.Linq;
using Deinosavros.MapReview;

public enum MapProgressPhase { OnMap, Traveling, Loading, InEncounter, Won, Lost }
public enum MapLocationState { Locked, Available, Completed, Skipped }

public sealed class MapTravelTicket
{
    public string RunId { get; }
    public string EncounterId { get; }
    public string FromNodeId { get; }
    public string NodeId { get; }
    public MapEncounterKind Kind { get; }
    public bool IsBoss => Kind == MapEncounterKind.Boss;
    public bool IsCombat => Kind == MapEncounterKind.Battle || IsBoss;
    public bool IsInitial => FromNodeId == NodeId;
    internal MapTravelTicket(string run, string from, string to, MapEncounterKind kind)
    {
        RunId = run; EncounterId = Guid.NewGuid().ToString("N");
        FromNodeId = from; NodeId = to; Kind = kind;
    }
}

// Route progression only. The existing RunSession remains the owner of cards and stats.
public sealed class MapProgressState
{
    private readonly Dictionary<string, MapGraphNode> nodes = new();
    private readonly Dictionary<string, MapEncounterKind> kinds = new();
    private readonly HashSet<string> completed = new(), available = new(), traveled = new();
    private int passedLayer;
    public string RunId { get; private set; }
    public string CurrentNodeId { get; private set; }
    public MapProgressPhase Phase { get; private set; }
    public MapTravelTicket CurrentEncounter { get; private set; }
    public int CompletedCount => completed.Count;
    public IEnumerable<string> NodeIds => nodes.Keys;
    public IReadOnlyList<string> NextNodes(string id) => nodes.TryGetValue(id, out var node)
        ? Array.AsReadOnly(node.next) : Array.Empty<string>();
    public event Action Changed;
#if UNITY_EDITOR
    public string TestStartNodeId { get; private set; }

    // Seed a fresh diagnostic run without simulating victories, rewards or traversed roads.
    internal bool TrySetTestStartNode(string id)
    {
        if (string.IsNullOrEmpty(id) || !nodes.TryGetValue(id, out var node) ||
            Phase != MapProgressPhase.OnMap || CurrentEncounter != null || completed.Count != 0 ||
            traveled.Count != 0 || TestStartNodeId != null || nodes[CurrentNodeId].layer != 1) return false;
        CurrentNodeId = id; TestStartNodeId = id; passedLayer = node.layer - 1;
        available.Clear(); available.Add(id);
        Changed?.Invoke(); return true;
    }
#endif

    public MapProgressState(MapGraphDefinition graph, MapNodeCatalog catalog = null) => BeginNewRun(graph, catalog);
    public void BeginNewRun(MapGraphDefinition graph, MapNodeCatalog catalog = null)
    {
        if (graph == null) throw new ArgumentNullException(nameof(graph));
        graph.Validate(); catalog?.Validate(graph); nodes.Clear(); kinds.Clear();
        foreach (var n in graph.nodes)
        {
            nodes.Add(n.id, new MapGraphNode { id = n.id, layer = n.layer, boss = n.boss,
                anchor = n.anchor, terrace = n.terrace, next = (string[])n.next.Clone() });
            kinds.Add(n.id, catalog != null ? catalog.Find(n.id).kind : n.boss ? MapEncounterKind.Boss : MapEncounterKind.Battle);
        }
        completed.Clear(); available.Clear(); traveled.Clear(); passedLayer = 0;
#if UNITY_EDITOR
        TestStartNodeId = null;
#endif
        CurrentNodeId = nodes.Values.Single(n => n.layer == 1).id;
        available.Add(CurrentNodeId); RunId = Guid.NewGuid().ToString("N");
        CurrentEncounter = null; Phase = MapProgressPhase.OnMap; Changed?.Invoke();
    }
    public MapLocationState NodeState(string id)
    {
        if (id == null || !nodes.TryGetValue(id, out var node)) return MapLocationState.Locked;
        if (completed.Contains(id)) return MapLocationState.Completed;
        if (available.Contains(id) && Phase != MapProgressPhase.Won && Phase != MapProgressPhase.Lost)
            return MapLocationState.Available;
        return node.layer <= passedLayer ? MapLocationState.Skipped : MapLocationState.Locked;
    }
    public bool HasTraveled(string from, string to) => traveled.Contains(from + ">" + to);
    public bool HasTraveled(string edgeKey) => traveled.Contains(edgeKey);
    public MapEncounterKind KindOf(string id) => kinds[id];
    public bool TryBeginTravel(string nodeId, out MapTravelTicket ticket)
    {
        ticket = null;
        if (Phase != MapProgressPhase.OnMap || nodeId == null || !available.Contains(nodeId)) return false;
        bool initial = completed.Count == 0 && nodeId == CurrentNodeId;
        if (!initial && !nodes[CurrentNodeId].next.Contains(nodeId)) return false;
        ticket = new MapTravelTicket(RunId, CurrentNodeId, nodeId, kinds[nodeId]);
        CurrentEncounter = ticket; Phase = MapProgressPhase.Traveling; Changed?.Invoke(); return true;
    }
    public bool MarkLoading(string encounterId)
    {
        if (!Matches(encounterId) || Phase != MapProgressPhase.Traveling) return false;
        Phase = MapProgressPhase.Loading; Changed?.Invoke(); return true;
    }
    public bool ConfirmEncounterStarted(string encounterId)
    {
        if (!Matches(encounterId) || Phase != MapProgressPhase.Loading) return false;
        if (!CurrentEncounter.IsInitial) traveled.Add(CurrentEncounter.FromNodeId + ">" + CurrentEncounter.NodeId);
        CurrentNodeId = CurrentEncounter.NodeId;
        Phase = MapProgressPhase.InEncounter; Changed?.Invoke(); return true;
    }
    public bool CancelPreparedTravel(string encounterId)
    {
        if (!Matches(encounterId) || (Phase != MapProgressPhase.Traveling && Phase != MapProgressPhase.Loading)) return false;
        CurrentEncounter = null; Phase = MapProgressPhase.OnMap; Changed?.Invoke(); return true;
    }
    public bool CompleteEncounter(string encounterId, bool victory)
    {
        if (!Matches(encounterId) || Phase != MapProgressPhase.InEncounter) return false;
        var node = nodes[CurrentEncounter.NodeId]; available.Clear(); CurrentEncounter = null;
        if (!victory) Phase = MapProgressPhase.Lost;
        else
        {
            completed.Add(node.id); passedLayer = node.layer;
            Phase = node.boss ? MapProgressPhase.Won : MapProgressPhase.OnMap;
            if (!node.boss) foreach (string next in node.next) available.Add(next);
        }
        Changed?.Invoke(); return true;
    }
    private bool Matches(string id) => CurrentEncounter != null && CurrentEncounter.RunId == RunId &&
        !string.IsNullOrEmpty(id) && CurrentEncounter.EncounterId == id;
}
