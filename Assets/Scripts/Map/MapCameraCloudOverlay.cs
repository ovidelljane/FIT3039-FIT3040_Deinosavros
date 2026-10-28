using UnityEngine;
using UnityEngine.Rendering;

[ExecuteAlways]
[DisallowMultipleComponent]
[RequireComponent(typeof(Camera))]
public sealed class MapCameraCloudOverlay : MonoBehaviour
{
    private const string ShaderResourcePath = "Map/MapCameraClouds";
    private const string OverlayObjectName = "Map Camera Clouds";

    [SerializeField, Tooltip("Alpha controls cloud opacity.")] private Color color = new(.82f,.82f,.79f,.14f);
    [SerializeField, Min(.1f)] private float cloudScale = 3.4f;
    [SerializeField, Tooltip("Horizontal and vertical movement in viewport units per second.")] private Vector2 flow = new(.024f,.006f);
    [SerializeField, Range(0,1)] private float centerClarity = .8f;

    private Camera targetCamera;
    private GameObject overlayObject;
    private Mesh overlayMesh;
    private Material overlayMaterial;
    private bool missingShaderReported;
    private readonly Vector3[] frustumCorners = new Vector3[4];

    public void Refresh()
    {
        if (!isActiveAndEnabled) return;
        EnsureResources();
    }

    private void OnEnable()
    {
        EnsureResources();
        #if UNITY_EDITOR
        UnityEditor.EditorApplication.update += Preview;
        #endif
    }

    private void OnValidate()
    {
        color.a = Mathf.Clamp01(color.a);
        cloudScale = Mathf.Max(.1f,cloudScale);
        centerClarity = Mathf.Clamp01(centerClarity);
    }

    private void LateUpdate()
    {
        EnsureResources();
    }

    private void OnDisable()
    {
        #if UNITY_EDITOR
        UnityEditor.EditorApplication.update -= Preview;
        #endif
        ReleaseResources();
    }

    #if UNITY_EDITOR
    private double nextPreview;
    private void Preview()
    {
        if (Application.isPlaying || UnityEditor.EditorApplication.timeSinceStartup < nextPreview) return;
        nextPreview = UnityEditor.EditorApplication.timeSinceStartup + 1.0/30;
        Refresh();
        UnityEditor.EditorApplication.QueuePlayerLoopUpdate();
    }
    #endif

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

        Shader.SetGlobalFloat("_MapCloudTime",Time.realtimeSinceStartup);
        overlayMaterial.SetColor("_CloudColor",color);
        overlayMaterial.SetFloat("_CloudScale",cloudScale);
        overlayMaterial.SetVector("_Flow",new Vector4(flow.x,flow.y,0,0));
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
