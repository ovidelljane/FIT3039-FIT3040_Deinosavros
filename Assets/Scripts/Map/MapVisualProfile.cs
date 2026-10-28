using UnityEngine;

[CreateAssetMenu(menuName = "Map/Visual Profile")]
public sealed class MapVisualProfile : ScriptableObject
{
    [Header("Camera")]
    [Range(20, 60)] public float fieldOfView = 35;
    [Range(35, 65)] public float pitch = 50;
    [Range(0.6f, 0.98f)] public float frameFill = 0.90f;
    [Min(0)] public float uiMarginPixels = 24;
    [Min(0)] public float visibleCliffDepth = 3;

    [Header("Environment")]
    [Min(1)] public float shadowRange = 100;
    [Min(1)] public float fogDepth = 70;
}
