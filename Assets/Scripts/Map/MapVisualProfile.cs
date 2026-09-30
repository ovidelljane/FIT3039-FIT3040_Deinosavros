using UnityEngine;

[CreateAssetMenu(menuName = "Map/Visual Profile")]
public sealed class MapVisualProfile : ScriptableObject
{
    [Header("Surfaces")]
    public Color sandstone = new(0.78f, 0.765f, 0.70f, 1);
    public Color soil = new(0.48f, 0.46f, 0.36f, 1);
    public Color moss = new(0.31f, 0.43f, 0.23f, 1);
    public Color rock = new(0.52f, 0.50f, 0.44f, 1);
    public Color leafDark = new(0.17f, 0.31f, 0.14f, 1);
    public Color leafLight = new(0.40f, 0.56f, 0.28f, 1);
    [Range(0, 1)] public float stoneSmoothness = 0.18f;
    [Range(0, 1)] public float groundSmoothness = 0.10f;
    [Min(0.1f)] public float stoneTileMeters = 3.2f;
    [Min(0.1f)] public float groundTileMeters = 5f;
    [Min(0.1f)] public float rockTileMeters = 4f;
    [Range(0, 1)] public float detailStrength = 0.32f;

    [Header("Camera")]
    [Range(20, 60)] public float fieldOfView = 35;
    [Range(35, 65)] public float pitch = 50;
    [Range(0.6f, 0.98f)] public float frameFill = 0.90f;
    [Min(0)] public float uiMarginPixels = 24;
    [Min(0)] public float visibleCliffDepth = 3;

    [Header("Lighting")]
    public Color sunlight = new(1, 0.894f, 0.729f, 1);
    [Min(0)] public float sunlightIntensity = 1.8f;
    [Range(0, 1)] public float shadowStrength = 0.75f;
    [Min(1)] public float shadowRange = 100;
    public Color ambientSky = new(0.58f, 0.65f, 0.70f, 1);
    public Color ambientEquator = new(0.47f, 0.46f, 0.40f, 1);
    public Color ambientGround = new(0.28f, 0.27f, 0.22f, 1);

    [Header("Atmosphere")]
    public Color skyTop = new(0.27f, 0.43f, 0.56f, 1);
    public Color skyHorizon = new(0.65f, 0.68f, 0.64f, 1);
    public Color skyBottom = new(0.30f, 0.40f, 0.44f, 1);
    [Range(0, 0.5f)] public float cloudOpacity = 0.12f;
    [Range(0, 1)] public float cloudCenterClarity = 0.80f;
    public Vector2 cloudSpeed = new(0.016f, 0.0015f);
    [Min(1)] public float fogDepth = 70;

    [Header("Post Processing")]
    public float exposure = 0.2f;
    public float contrast = 6;
    public float saturation = 5;
    public float temperature = 6;
    [Range(0, 1)] public float bloom = 0.18f;
    [Range(0, 1)] public float vignette = 0.08f;
}
