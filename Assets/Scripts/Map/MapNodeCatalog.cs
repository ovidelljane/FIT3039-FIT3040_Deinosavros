using System;
using UnityEngine;

public enum MapEncounterKind { Battle = 0, Boss = 1, Opportunity = 2, Recovery = 3 }

[Serializable]
public sealed class MapNodeInfo
{
    public string nodeId;
    public MapEncounterKind kind;
    public string title;
    [TextArea(2, 5)] public string summary;
    [TextArea(1, 3)] public string enemies;
    [TextArea(1, 3)] public string rewards;
    public int Glyph => 8 + (int)kind;
    public string KindLabel => kind == MapEncounterKind.Boss ? "BOSS" : kind.ToString().ToUpperInvariant();
}

// Stable node types drive both presentation and receiver selection. Topology remains in the graph.
[CreateAssetMenu(menuName = "Map/Node Information")]
public sealed class MapNodeCatalog : ScriptableObject
{
    public MapNodeInfo[] nodes = Array.Empty<MapNodeInfo>();
    public MapNodeInfo Find(string id)
    {
        foreach (var node in nodes) if (node != null && node.nodeId == id) return node;
        return null;
    }
    public void Validate(Deinosavros.MapReview.MapGraphDefinition graph)
    {
        if (nodes == null || nodes.Length != graph.nodes.Length) throw new InvalidOperationException("Node information must cover the entire graph.");
        var ids = new System.Collections.Generic.HashSet<string>();
        foreach (var info in nodes)
        {
            var node = info == null ? null : graph.Find(info.nodeId);
            if (node == null || !ids.Add(info.nodeId) || !Enum.IsDefined(typeof(MapEncounterKind), info.kind) ||
                node.boss != (info.kind == MapEncounterKind.Boss))
                throw new InvalidOperationException("Node information contains an invalid type, identity or Boss binding.");
        }
    }
}
