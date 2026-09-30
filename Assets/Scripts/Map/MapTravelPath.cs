using UnityEngine;

public sealed class MapTravelPath : MonoBehaviour
{
    public string fromNodeId, toNodeId;
    public Transform space;
    public MeshFilter road;
    public Mesh sourceRoadMesh, overrideRoadMesh;
    public bool manualControlPoints;
    public Vector3[] points = System.Array.Empty<Vector3>();
    private Vector3[] worldPoints;
    private float[] distances;
    public float Length { get; private set; }
    public string EdgeKey { get; private set; }

    public void Prepare()
    {
        EdgeKey = fromNodeId + ">" + toNodeId;
        worldPoints = new Vector3[points.Length]; distances = new float[points.Length]; Length = 0;
        for (int i = 0; i < points.Length; i++)
        {
            worldPoints[i] = space != null ? space.TransformPoint(points[i]) : points[i];
            if (i > 0) Length += Vector3.Distance(worldPoints[i - 1], worldPoints[i]);
            distances[i] = Length;
        }
    }
    public Vector3 Sample(float progress)
    {
        if (worldPoints == null || worldPoints.Length != points.Length) Prepare();
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
