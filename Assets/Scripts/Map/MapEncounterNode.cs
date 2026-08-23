using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

[RequireComponent(typeof(Collider))]
public sealed class MapEncounterNode : MonoBehaviour
{
    [SerializeField] private string targetScene = "Deinosavros";
    [SerializeField] private float hoverScale = 1.12f;

    private Vector3 initialScale;
    private Camera sceneCamera;
    private Collider nodeCollider;
    private bool isLoading;

    private void Awake()
    {
        initialScale = transform.localScale;
        sceneCamera = Camera.main;
        nodeCollider = GetComponent<Collider>();
    }

    private void Update()
    {
        if (isLoading || sceneCamera == null || Mouse.current == null)
        {
            return;
        }

        Ray pointerRay = sceneCamera.ScreenPointToRay(Mouse.current.position.ReadValue());
        bool isHovered = nodeCollider.Raycast(pointerRay, out _, sceneCamera.farClipPlane);
        transform.localScale = isHovered ? initialScale * hoverScale : initialScale;

        if (!isHovered || !Mouse.current.leftButton.wasPressedThisFrame)
        {
            return;
        }

        EnterEncounter();
    }

    private void EnterEncounter()
    {
        if (!Application.CanStreamedLevelBeLoaded(targetScene))
        {
            return;
        }

        isLoading = true;
        SceneManager.LoadSceneAsync(targetScene, LoadSceneMode.Single);
    }
}
