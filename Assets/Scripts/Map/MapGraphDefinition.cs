using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Deinosavros.MapReview
{
    [Serializable]
    public sealed class MapGraphNode
    {
        public string id;
        public int layer;
        public string anchor;
        public string terrace;
        public bool boss;
        public string[] next;
    }

    [CreateAssetMenu(menuName = "Map Review/Graph")]
    public sealed class MapGraphDefinition : ScriptableObject
    {
        public MapGraphNode[] nodes = Array.Empty<MapGraphNode>();
        public MapGraphNode Find(string id) => nodes?.FirstOrDefault(n => n != null && n.id == id);

        public void Validate()
        {
            if (nodes == null || nodes.Any(n => n == null || string.IsNullOrWhiteSpace(n.id) ||
                string.IsNullOrWhiteSpace(n.anchor) || n.next == null ||
                n.next.Any(string.IsNullOrWhiteSpace)))
                throw new InvalidOperationException("Nodes require identifiers, anchors and non-null connection lists.");
            if (nodes.Length != 14 || nodes.Sum(n => n.next.Length) != 19)
                throw new InvalidOperationException("The review graph requires 14 nodes and 19 edges.");
            if (nodes.Select(n => n.id).Distinct().Count() != nodes.Length)
                throw new InvalidOperationException("Duplicate graph node identifiers.");
            if (nodes.Select(n => n.anchor).Distinct().Count() != nodes.Length)
                throw new InvalidOperationException("Each graph node requires a unique model anchor.");
            if (nodes.Count(n => n.layer == 1) != 1 || nodes.Count(n => n.boss) != 1)
                throw new InvalidOperationException("Expected one start and one boss.");
            foreach (var node in nodes)
            {
                if (node.next.Distinct().Count() != node.next.Length)
                    throw new InvalidOperationException("Duplicate graph connection.");
                if (node.layer < 1 || node.layer > 6 || node.boss != (node.layer == 6) ||
                    (node.boss && node.next.Length != 0) || (!node.boss && node.next.Length == 0))
                    throw new InvalidOperationException("Every normal layer must lead toward the boss.");
                foreach (string next in node.next)
                    if (Find(next) == null || Find(next).layer != node.layer + 1)
                        throw new InvalidOperationException("Edges must advance exactly one layer.");
            }
            var reached = new HashSet<string>();
            void Visit(string id)
            {
                if (!reached.Add(id)) return;
                foreach (string next in Find(id).next) Visit(next);
            }
            Visit(nodes.Single(n => n.layer == 1).id);
            if (reached.Count != nodes.Length) throw new InvalidOperationException("Unreachable graph nodes.");
        }

        public void SetReviewTopology()
        {
            MapGraphNode Node(string suffix, int terrace, params string[] next) => new()
            {
                id = "level_" + suffix, layer = int.Parse(suffix.Substring(0, 2)),
                anchor = "ANCHOR_Node_L" + suffix,
                terrace = terrace < 0 ? "" : $"Node terrace {terrace:00}",
                boss = suffix == "06_01", next = next.Select(n => "level_" + n).ToArray()
            };
            nodes = new[]
            {
                Node("01_01", 0, "02_01", "02_03", "02_02"),
                Node("02_01", 1, "03_01"), Node("02_03", 2, "03_01", "03_02"),
                Node("02_02", 3, "03_02", "03_03"),
                Node("03_01", 4, "04_01", "04_03"), Node("03_02", 5, "04_03", "04_02"),
                Node("03_03", 6, "04_02"), Node("04_01", 7, "05_01"),
                Node("04_03", 8, "05_03"), Node("04_02", 9, "05_02"),
                Node("05_01", 10, "06_01"), Node("05_03", 11, "06_01"),
                Node("05_02", 12, "06_01"), Node("06_01", -1)
            };
            Validate();
        }
    }
}
