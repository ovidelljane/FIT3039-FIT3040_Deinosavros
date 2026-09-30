using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

[ExecuteAlways]
[DisallowMultipleComponent]
[RequireComponent(typeof(Camera))]
public sealed class MapCameraCloudOverlay : MonoBehaviour
{
    private const string MapSceneName = "Map";
    private const string ShaderResourcePath = "Map/MapCameraClouds";
    private const string OverlayObjectName = "Map Camera Clouds";

    [SerializeField] private Color cloudColor = new(0.82f, 0.82f, 0.79f, 1.0f);
    [SerializeField] private Color shadowColor = new(0.30f, 0.34f, 0.38f, 1.0f);
    [SerializeField, Range(0.0f, 0.5f)] private float opacity = 0.26f;
    [SerializeField, Range(0.3f, 0.8f)] private float coverage = 0.56f;
    [SerializeField, Range(0.02f, 0.35f)] private float edgeSoftness = 0.09f;
    [SerializeField, Range(0.2f, 5.0f)] private float primaryScale = 1.35f;
    [SerializeField, Range(1.0f, 12.0f)] private float detailScale = 4.8f;
    [SerializeField] private Vector2 primarySpeed = new(0.016f, 0.0015f);
    [SerializeField] private Vector2 detailSpeed = new(-0.008f, 0.003f);
    [SerializeField, Range(0.0f, 1.0f)] private float centerClarity = 0.25f;

    private Camera targetCamera;
    private GameObject overlayObject;
    private Mesh overlayMesh;
    private Material overlayMaterial;
    private bool missingShaderReported;
    private readonly Vector3[] frustumCorners = new Vector3[4];

    public void Configure(float alpha, float clarity, Vector2 speed)
    {
        opacity = alpha;
        centerClarity = clarity;
        primarySpeed = speed;
        detailSpeed = new Vector2(-0.008f, 0.003f);
        cloudColor = new Color(0.82f, 0.82f, 0.79f, 1);
        shadowColor = new Color(0.30f, 0.34f, 0.38f, 1);
        Refresh();
    }

    public void Refresh()
    {
        if (!isActiveAndEnabled) return;
        EnsureResources();
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void InstallForMapScene()
    {
        if (!string.Equals(SceneManager.GetActiveScene().name, MapSceneName, StringComparison.Ordinal))
        {
            return;
        }

        Ensure(Camera.main);
    }

    public static MapCameraCloudOverlay Ensure(Camera camera)
    {
        if (camera == null)
        {
            return null;
        }

        MapCameraCloudOverlay overlay = camera.GetComponent<MapCameraCloudOverlay>();
        if (overlay == null)
        {
            overlay = camera.gameObject.AddComponent<MapCameraCloudOverlay>();
        }
        overlay.EnsureResources();
        return overlay;
    }

    private void OnEnable()
    {
        EnsureResources();
    }

    private void OnValidate()
    {
        opacity = Mathf.Clamp(opacity, 0.0f, 0.5f);
        coverage = Mathf.Clamp(coverage, 0.3f, 0.8f);
        edgeSoftness = Mathf.Clamp(edgeSoftness, 0.02f, 0.35f);
        primaryScale = Mathf.Clamp(primaryScale, 0.2f, 5.0f);
        detailScale = Mathf.Clamp(detailScale, 1.0f, 12.0f);
        centerClarity = Mathf.Clamp01(centerClarity);
        ApplyMaterialSettings();
        UpdateOverlayTransform();
    }

    private void LateUpdate()
    {
        EnsureResources();
    }

    private void OnDisable()
    {
        ReleaseResources();
    }

    private void EnsureResources()
    {
        if (targetCamera == null)
        {
            targetCamera = GetComponent<Camera>();
        }

        if (overlayMaterial == null)
        {
            Shader shader = Resources.Load<Shader>(ShaderResourcePath);
            if (shader == null)
            {
                if (!missingShaderReported)
                {
                    Debug.LogError($"Map camera cloud shader was not found at Resources/{ShaderResourcePath}.", this);
                    missingShaderReported = true;
                }
                return;
            }

            missingShaderReported = false;
            overlayMaterial = new Material(shader)
            {
                name = "Map Camera Clouds Runtime",
                hideFlags = HideFlags.HideAndDontSave,
                renderQueue = 3100
            };
        }

        if (overlayMesh == null)
        {
            overlayMesh = CreateOverlayMesh();
        }

        if (overlayObject == null)
        {
            overlayObject = new GameObject(OverlayObjectName)
            {
                hideFlags = HideFlags.HideAndDontSave,
                layer = gameObject.layer
            };
            overlayObject.transform.SetParent(transform, false);

            MeshFilter meshFilter = overlayObject.AddComponent<MeshFilter>();
            meshFilter.sharedMesh = overlayMesh;

            MeshRenderer meshRenderer = overlayObject.AddComponent<MeshRenderer>();
            meshRenderer.sharedMaterial = overlayMaterial;
            meshRenderer.shadowCastingMode = ShadowCastingMode.Off;
            meshRenderer.receiveShadows = false;
            meshRenderer.lightProbeUsage = LightProbeUsage.Off;
            meshRenderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            meshRenderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
            meshRenderer.sortingOrder = short.MaxValue;
        }

        ApplyMaterialSettings();
        UpdateOverlayTransform();
    }

    private Mesh CreateOverlayMesh()
    {
        Mesh mesh = new()
        {
            name = "Map Camera Clouds Quad",
            hideFlags = HideFlags.HideAndDontSave,
            vertices = new[]
            {
                new Vector3(-0.5f, -0.5f, 0.0f),
                new Vector3(0.5f, -0.5f, 0.0f),
                new Vector3(-0.5f, 0.5f, 0.0f),
                new Vector3(0.5f, 0.5f, 0.0f)
            },
            uv = new[]
            {
                new Vector2(0.0f, 0.0f),
                new Vector2(1.0f, 0.0f),
                new Vector2(0.0f, 1.0f),
                new Vector2(1.0f, 1.0f)
            },
            triangles = new[] { 0, 2, 1, 2, 3, 1 }
        };
        mesh.RecalculateBounds();
        return mesh;
    }

    private void ApplyMaterialSettings()
    {
        if (overlayMaterial == null || targetCamera == null)
        {
            return;
        }

        overlayMaterial.SetColor("_CloudColor", cloudColor);
        overlayMaterial.SetColor("_ShadowColor", shadowColor);
        overlayMaterial.SetFloat("_Opacity", opacity);
        overlayMaterial.SetFloat("_Coverage", coverage);
        overlayMaterial.SetFloat("_Softness", edgeSoftness);
        overlayMaterial.SetFloat("_PrimaryScale", primaryScale);
        overlayMaterial.SetFloat("_DetailScale", detailScale);
        overlayMaterial.SetVector("_PrimarySpeed", new Vector4(primarySpeed.x, primarySpeed.y, 0.0f, 0.0f));
        overlayMaterial.SetVector("_DetailSpeed", new Vector4(detailSpeed.x, detailSpeed.y, 0.0f, 0.0f));
        overlayMaterial.SetFloat("_CenterClarity", centerClarity);
        overlayMaterial.SetFloat("_Aspect", Mathf.Max(0.1f, targetCamera.aspect));
    }

    private void UpdateOverlayTransform()
    {
        if (targetCamera == null || overlayObject == null)
        {
            return;
        }

        float distance = Mathf.Max(targetCamera.nearClipPlane + 0.05f, 0.15f);
        targetCamera.CalculateFrustumCorners(new Rect(0, 0, 1, 1), distance, Camera.MonoOrStereoscopicEye.Mono, frustumCorners);
        float width = frustumCorners[3].x - frustumCorners[0].x;
        float height = frustumCorners[1].y - frustumCorners[0].y;
        Transform overlayTransform = overlayObject.transform;
        overlayTransform.localPosition = (frustumCorners[0] + frustumCorners[2]) * 0.5f;
        overlayTransform.localRotation = Quaternion.identity;
        overlayTransform.localScale = new Vector3(width * 1.04f, height * 1.04f, 1.0f);
    }

    private void ReleaseResources()
    {
        DestroyGeneratedObject(overlayObject);
        DestroyGeneratedObject(overlayMaterial);
        DestroyGeneratedObject(overlayMesh);
        overlayObject = null;
        overlayMaterial = null;
        overlayMesh = null;
    }

    private static void DestroyGeneratedObject(UnityEngine.Object generatedObject)
    {
        if (generatedObject == null)
        {
            return;
        }

        if (Application.isPlaying)
        {
            Destroy(generatedObject);
        }
        else
        {
            DestroyImmediate(generatedObject);
        }
    }
}
