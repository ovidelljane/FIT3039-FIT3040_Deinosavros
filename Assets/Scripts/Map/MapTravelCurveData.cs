using UnityEngine;

// Optional presentation-only samples in the same local space as the authored road.
public sealed class MapTravelCurveData : ScriptableObject
{
    public string edgeKey;
    public Vector3[] points;
    public Vector3[] tangents;
    public float[] cumulativeLength;
    public float maximumDeviation;
}
