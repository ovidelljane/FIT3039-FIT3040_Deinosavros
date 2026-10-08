using UnityEngine;
using UnityEngine.UI;

// Texture-free ring; the authored graphic controls thickness and segment count.
[RequireComponent(typeof(CanvasRenderer))]
public sealed class CardBuffRing : MaskableGraphic
{
    [Range(0, 1)] public float remaining = 1;
    [Range(.03f, 1)] public float thickness = .14f;
    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();
        const int segments = 48;
        float radius = Mathf.Min(rectTransform.rect.width, rectTransform.rect.height) * .5f;
        Vector2 center = rectTransform.rect.center;
        for (int i = 0; i < segments; i++)
        {
            float start = (float)i / segments;
            if (start >= remaining) break;
            float end = Mathf.Min((float)(i + 1) / segments, remaining);
            Vector2 a = new(Mathf.Sin(start * Mathf.PI * 2), Mathf.Cos(start * Mathf.PI * 2));
            Vector2 b = new(Mathf.Sin(end * Mathf.PI * 2), Mathf.Cos(end * Mathf.PI * 2));
            int index = vh.currentVertCount;
            vh.AddVert(center + a * radius, color, Vector2.zero);
            vh.AddVert(center + b * radius, color, Vector2.zero);
            vh.AddVert(center + b * radius * (1 - thickness), color, Vector2.zero);
            vh.AddVert(center + a * radius * (1 - thickness), color, Vector2.zero);
            vh.AddTriangle(index, index + 1, index + 2); vh.AddTriangle(index, index + 2, index + 3);
        }
    }
}
