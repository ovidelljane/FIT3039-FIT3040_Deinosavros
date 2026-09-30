using UnityEngine;

[CreateAssetMenu(menuName = "Map/Travel Profile")]
public sealed class MapTravelProfile : ScriptableObject
{
    public Material symbolMaterial;
    public Material glowMaterial;
    public Mesh quadMesh;
    public MapNodeCatalog nodeInformation;
    public Color amber = new(0.906f, 0.714f, 0.416f, 1);
    public Color cream = new(1, 0.902f, 0.682f, 1);
    [Tooltip("Quad dimensions at the 1080-pixel reference height, including transparent padding.")]
    public float corePixels = 56, coreHeightPixels = 74, haloPixels = 88;
    public float iconPixels = 44, iconHeight = 1.1f;
    public Vector2 dockPixels = new(-42, -12);
    public float tooltipDelay = .15f, tooltipGrace = .18f;
    public float hoverHeight = .85f, bobAmplitude = .035f;
    public float previewFade = .25f, gatherTime = .3f;
    public float moveSpeed = 5.5f, minimumMoveTime = 1.2f, maximumMoveTime = 2;
    public float arrivalTime = .3f, bossGatherTime = .8f;
    public float fadeOutTime = .35f, fadeInTime = .25f, readyTimeout = 5;
    public float trailLife = .6f, sigilRadius = .56f, routeWidth = .46f;
    public int maximumTrailParticles = 48, maximumBurstParticles = 96;
}
