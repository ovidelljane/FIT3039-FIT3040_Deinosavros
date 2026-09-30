using UnityEngine;

public sealed class MapTravelPath : MonoBehaviour
{
    public string fromNodeId, toNodeId;
    public Transform space;
    public MeshFilter road;
    public Mesh sourceRoadMesh, overrideRoadMesh;
    public bool manualControlPoints;
    public Vector3[] points = System.Array.Empty<Vector3>();
    public MapTravelCurveData presentationCurve;
    private Vector3[] SourcePoints => presentationCurve != null && presentationCurve.points != null && presentationCurve.points.Length > 1
        ? presentationCurve.points : points;
    private Vector3[] worldPoints;
    private float[] distances;
    public float Length { get; private set; }
    public string EdgeKey { get; private set; }
    // Presentation tangent only; the authored path and progression graph are unchanged.
    public Vector3 Tangent(float progress)
    {
        if (worldPoints == null) Prepare();
        float span = Mathf.Min(.08f, .22f / Mathf.Max(.1f, Length));
        return (Sample(Mathf.Min(1, progress + span)) - Sample(Mathf.Max(0, progress - span))).normalized;
    }

    public void Prepare()
    {
        EdgeKey = fromNodeId + ">" + toNodeId;
        var source=SourcePoints;
        worldPoints = new Vector3[source.Length]; distances = new float[source.Length]; Length = 0;
        for (int i = 0; i < source.Length; i++)
        {
            worldPoints[i] = space != null ? space.TransformPoint(source[i]) : source[i];
            if (i > 0) Length += Vector3.Distance(worldPoints[i - 1], worldPoints[i]);
            distances[i] = Length;
        }
    }
    public Vector3 Sample(float progress)
    {
        if (worldPoints == null || worldPoints.Length != SourcePoints.Length) Prepare();
        if (worldPoints.Length == 0) return transform.position;
        if (worldPoints.Length == 1) return worldPoints[0];
        if (progress <= 0) return worldPoints[0];
        if (progress >= 1) return worldPoints[^1];
        float distance = progress * Length;
        int lo = 1, hi = distances.Length - 1;
        while (lo < hi) { int mid = (lo + hi) / 2; if (distances[mid] < distance) lo = mid + 1; else hi = mid; }
        return Vector3.Lerp(worldPoints[lo - 1], worldPoints[lo],
            Mathf.InverseLerp(distances[lo - 1], distances[lo], distance));
    }
    public static float EaseDistance(float t)
    {
        const float ramp = .15f;
        float area = 1 - ramp;
        if (t < ramp) return t * t / (2 * ramp * area);
        if (t > 1 - ramp) { float rest = 1 - t; return 1 - rest * rest / (2 * ramp * area); }
        return (t - ramp * .5f) / area;
    }
    private void OnDrawGizmosSelected()
    {
        if (points == null) return;
        Gizmos.color = new Color(1, .72f, .3f);
        Vector3 previous = default;
        for (int i = 0; i < points.Length; i++)
        {
            Vector3 point = space != null ? space.TransformPoint(points[i]) : points[i];
            Gizmos.DrawSphere(point, .035f);
            if (i > 0) Gizmos.DrawLine(previous, point);
            previous = point;
        }
    }
}
