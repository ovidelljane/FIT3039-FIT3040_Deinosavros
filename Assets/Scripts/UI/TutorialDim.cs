using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

// Full-screen shade with rectangular cut-outs, each edged in gold. The graphic raycasts across its
// whole rect, so spotlighted areas can be seen but not clicked.
[RequireComponent(typeof(CanvasRenderer))]
public sealed class TutorialDim : MaskableGraphic
{
    private const float BorderWidth = 3f;
    private readonly List<Rect> holes = new();
    private readonly List<float> xs = new(), ys = new();

    // Rects are in this graphic's local space and may overlap; the mesh is rebuilt only when they change.
    public void SetHoles(List<Rect> next)
    {
        if (next.Count == holes.Count)
        {
            bool same = true;
            for (int i = 0; same && i < next.Count; i++) same = next[i] == holes[i];
            if (same) return;
        }
        holes.Clear();
        holes.AddRange(next);
        SetVerticesDirty();
    }

    // Splits the screen along every hole edge, then shades each cell that no hole covers.
    protected override void OnPopulateMesh(VertexHelper mesh)
    {
        mesh.Clear();
        Rect area = rectTransform.rect;
        xs.Clear(); ys.Clear();
        xs.Add(area.xMin); xs.Add(area.xMax);
        ys.Add(area.yMin); ys.Add(area.yMax);
        foreach (Rect hole in holes)
        {
            xs.Add(hole.xMin); xs.Add(hole.xMax);
            ys.Add(hole.yMin); ys.Add(hole.yMax);
        }
        xs.Sort(); ys.Sort();

        for (int x = 0; x + 1 < xs.Count; x++)
            for (int y = 0; y + 1 < ys.Count; y++)
            {
                var cell = Rect.MinMaxRect(xs[x], ys[y], xs[x + 1], ys[y + 1]);
                if (cell.width > 0 && cell.height > 0 && !InHole(cell.center)) AddQuad(mesh, cell, color);
            }

        foreach (Rect hole in holes)
        {
            Color edge = PlayerStatusView.Gold;
            AddQuad(mesh, Rect.MinMaxRect(hole.xMin - BorderWidth, hole.yMin - BorderWidth, hole.xMin, hole.yMax + BorderWidth), edge);
            AddQuad(mesh, Rect.MinMaxRect(hole.xMax, hole.yMin - BorderWidth, hole.xMax + BorderWidth, hole.yMax + BorderWidth), edge);
            AddQuad(mesh, Rect.MinMaxRect(hole.xMin, hole.yMin - BorderWidth, hole.xMax, hole.yMin), edge);
            AddQuad(mesh, Rect.MinMaxRect(hole.xMin, hole.yMax, hole.xMax, hole.yMax + BorderWidth), edge);
        }
    }

    private bool InHole(Vector2 point)
    {
        foreach (Rect hole in holes) if (hole.Contains(point)) return true;
        return false;
    }

    private static void AddQuad(VertexHelper mesh, Rect rect, Color tint)
    {
        int start = mesh.currentVertCount;
        mesh.AddVert(new Vector2(rect.xMin, rect.yMin), tint, Vector2.zero);
        mesh.AddVert(new Vector2(rect.xMin, rect.yMax), tint, Vector2.zero);
        mesh.AddVert(new Vector2(rect.xMax, rect.yMax), tint, Vector2.zero);
        mesh.AddVert(new Vector2(rect.xMax, rect.yMin), tint, Vector2.zero);
        mesh.AddTriangle(start, start + 1, start + 2);
        mesh.AddTriangle(start, start + 2, start + 3);
    }
}
