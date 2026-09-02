using UnityEngine;
using UnityEngine.InputSystem;

public sealed class MapEncounterNode : MonoBehaviour
{
    [SerializeField] private string nodeId = "level_01_start";
    [SerializeField] private int level = 1;
    [SerializeField] private bool available;
    [SerializeField] private MapController mapController;
    [SerializeField] private Collider interactionCollider;
    [SerializeField] private Transform typeMarker;
    [SerializeField] private GameObject selectionIndicator;
    [SerializeField] private Renderer[] stateRenderers;
    [SerializeField] private Material availableMaterial;
    [SerializeField] private Material lockedMaterial;
    [SerializeField] private Material selectedMaterial;
    [SerializeField] private ParticleSystem hoverGatherParticles;
    [SerializeField] private ParticleSystem selectionBurstParticles;
    [SerializeField] private float hoverScale = 1.08f;
    [SerializeField] private float selectedLift = 0.1f;

    private Camera sceneCamera;
    private Vector3 markerBasePosition;
    private Vector3 markerBaseScale;
    private bool pointerHovered;
    private bool selected;

    public string NodeId => nodeId;
    public int Level => level;
    public bool IsAvailable => available;

    private void Awake()
    {
        sceneCamera = Camera.main;
        markerBasePosition = typeMarker.localPosition;
        markerBaseScale = typeMarker.localScale;
        ApplyVisualState();
    }

    private void Update()
    {
        if (sceneCamera == null || Mouse.current == null || interactionCollider == null)
        {
            return;
        }

        Ray pointerRay = sceneCamera.ScreenPointToRay(Mouse.current.position.ReadValue());
        bool hovered = available && !selected && interactionCollider.Raycast(pointerRay, out _, sceneCamera.farClipPlane);
        if (hovered != pointerHovered)
        {
            pointerHovered = hovered;
            SetHoverParticles(pointerHovered);
        }
        typeMarker.localScale = hovered ? markerBaseScale * hoverScale : markerBaseScale;

        if (hovered && Mouse.current.leftButton.wasPressedThisFrame)
        {
            mapController.SelectNode(this);
        }
    }

    public void SetSelected(bool value)
    {
        bool becameSelected = value && !selected;
        selected = value;
        if (selected)
        {
            pointerHovered = false;
            SetHoverParticles(false);
        }
        if (becameSelected)
        {
            PlaySelectionBurst();
        }
        typeMarker.localPosition = markerBasePosition + (selected ? Vector3.up * selectedLift : Vector3.zero);
        ApplyVisualState();
    }

    private void SetHoverParticles(bool active)
    {
        if (hoverGatherParticles == null)
        {
            return;
        }

        if (active)
        {
            if (!hoverGatherParticles.isPlaying)
            {
                hoverGatherParticles.Play(true);
            }
        }
        else
        {
            hoverGatherParticles.Stop(true, ParticleSystemStopBehavior.StopEmitting);
        }
    }

    private void PlaySelectionBurst()
    {
        if (selectionBurstParticles == null)
        {
            return;
        }

        selectionBurstParticles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        selectionBurstParticles.Play(true);
    }

    private void ApplyVisualState()
    {
        if (selectionIndicator != null)
        {
            selectionIndicator.SetActive(selected);
        }

        Material material = selected ? selectedMaterial : available ? availableMaterial : lockedMaterial;
        if (material == null)
        {
            return;
        }

        foreach (Renderer stateRenderer in stateRenderers)
        {
            if (stateRenderer != null)
            {
                stateRenderer.sharedMaterial = material;
            }
        }
    }
}
