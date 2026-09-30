using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

[ExecuteAlways, DisallowMultipleComponent, RequireComponent(typeof(Camera))]
public sealed class MapCameraFraming : MonoBehaviour
{
    public MapVisualProfile profile;
    public Renderer[] subjects;
    public RectTransform cardBar;
    public RectTransform topBar;
    [SerializeField] private Vector3[] framingPoints;
    [SerializeField] private float yaw;
    private Camera targetCamera;
    private Vector2Int lastSize;
    private Rect lastSafeArea;
    private UniversalRenderPipelineAsset renderingPipeline;
    private float previousShadowDistance;
    private readonly Vector3[] corners = new Vector3[4];
    public Rect SafeArea { get; private set; }

    public void Configure(MapVisualProfile settings, Renderer[] renderers, RectTransform bottom, RectTransform top)
    {
        profile = settings;
        subjects = renderers;
        cardBar = bottom;
        topBar = top;
        targetCamera = GetComponent<Camera>();
        yaw = transform.eulerAngles.y;
        var points = new List<Vector3>();
        foreach (Renderer renderer in subjects)
        {
            if (renderer == null || !renderer.enabled || !renderer.gameObject.activeInHierarchy) continue;
            // Transform the local mesh bounds instead of inflating rotated objects into world AABBs.
            MeshFilter filter = renderer.GetComponent<MeshFilter>();
            if (filter == null || filter.sharedMesh == null) continue;
            Bounds b = filter.sharedMesh.bounds;
            bool island = renderer.name == "Island" || renderer.name == "Cohesive closed floating island" || renderer.name == "V2 Island continuous fractured bedrock";
            if (filter.sharedMesh.isReadable && (island || renderer.name.Contains("palm crown")))
            {
                // Actual vertices avoid empty corners around curved palms and the tapered island.
                Vector3[] vertices = filter.sharedMesh.vertices;
                int stride = Mathf.Max(1, vertices.Length / 384);
                for (int i = 0; i < vertices.Length; i += stride)
                {
                    Vector3 point = renderer.transform.TransformPoint(vertices[i]);
                    if (island)
                        point.y = Mathf.Max(point.y, renderer.bounds.max.y - profile.visibleCliffDepth);
                    points.Add(point);
                }
                continue;
            }
            for (int i = 0; i < 8; i++)
            {
                Vector3 sign = new((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1);
                Vector3 point = renderer.transform.TransformPoint(b.center + Vector3.Scale(b.extents, sign));
                if (island)
                    point.y = Mathf.Max(point.y, renderer.bounds.max.y - profile.visibleCliffDepth);
                points.Add(point);
            }
        }
        framingPoints = points.ToArray();
        Reframe();
    }

    private void OnEnable()
    {
        targetCamera = GetComponent<Camera>(); lastSize = Vector2Int.zero;
        RenderPipelineManager.beginCameraRendering += BeginCamera;
        RenderPipelineManager.endCameraRendering += EndCamera;
    }
    private void OnDisable()
    {
        RenderPipelineManager.beginCameraRendering -= BeginCamera;
        RenderPipelineManager.endCameraRendering -= EndCamera;
        EndVisualRender();
    }
    private void BeginCamera(ScriptableRenderContext context, Camera camera) { if (camera == targetCamera) BeginVisualRender(); }
    private void EndCamera(ScriptableRenderContext context, Camera camera) { if (camera == targetCamera) EndVisualRender(); }
    public void BeginVisualRender()
    {
        if (renderingPipeline != null || profile == null || SceneManager.GetActiveScene() != gameObject.scene) return;
        if (GraphicsSettings.currentRenderPipeline is not UniversalRenderPipelineAsset pipeline) return;
        // Scope the longer range to this camera's render. Never save or replace shared pipeline assets.
        renderingPipeline = pipeline;
        previousShadowDistance = pipeline.shadowDistance;
        pipeline.shadowDistance = Mathf.Max(previousShadowDistance, profile.shadowRange);
    }
    public void EndVisualRender()
    {
        if (renderingPipeline == null) return;
        renderingPipeline.shadowDistance = previousShadowDistance;
        renderingPipeline = null;
    }
    private void LateUpdate()
    {
        if (targetCamera == null || profile == null) return;
        Vector2Int size = new(targetCamera.pixelWidth, targetCamera.pixelHeight);
        Rect safe = GetSafeArea(size.x, size.y);
        if (size != lastSize || safe != lastSafeArea) Reframe();
    }

    public void Reframe(int width = 0, int height = 0)
    {
        if (targetCamera == null) targetCamera = GetComponent<Camera>();
        if (profile == null || framingPoints == null || framingPoints.Length == 0) return;
        if (width <= 0) width = Mathf.Max(1, targetCamera.pixelWidth);
        if (height <= 0) height = Mathf.Max(1, targetCamera.pixelHeight);
        targetCamera.fieldOfView = profile.fieldOfView;
        targetCamera.orthographic = false;
        SafeArea = GetSafeArea(width, height);
        Rect fit = new(SafeArea.center - SafeArea.size * profile.frameFill * 0.5f, SafeArea.size * profile.frameFill);
        Quaternion rotation = Quaternion.Euler(profile.pitch, yaw, 0);
        Quaternion inverse = Quaternion.Inverse(rotation);
        Vector3 center = Vector3.zero;
        foreach (Vector3 p in framingPoints) center += p;
        center /= framingPoints.Length;
        float tanY = Mathf.Tan(profile.fieldOfView * Mathf.Deg2Rad * 0.5f);
        float tanX = tanY * width / height;
        float left = (fit.xMin * 2 - 1) * tanX;
        float right = (fit.xMax * 2 - 1) * tanX;
        float bottom = (fit.yMin * 2 - 1) * tanY;
        float top = (fit.yMax * 2 - 1) * tanY;
        float near = 0.1f, far = 500;
        Vector2 shift = default;
        // Solve a common camera translation whose projection contains every subject corner.
        for (int step = 0; step < 32; step++)
        {
            float distance = (near + far) * 0.5f;
            float minX = float.NegativeInfinity, maxX = float.PositiveInfinity;
            float minY = float.NegativeInfinity, maxY = float.PositiveInfinity;
            bool valid = true;
            foreach (Vector3 p in framingPoints)
            {
                Vector3 q = inverse * (p - center);
                float z = q.z + distance;
                if (z < targetCamera.nearClipPlane + 1) valid = false;
                minX = Mathf.Max(minX, q.x - right * z);
                maxX = Mathf.Min(maxX, q.x - left * z);
                minY = Mathf.Max(minY, q.y - top * z);
                maxY = Mathf.Min(maxY, q.y - bottom * z);
            }
            if (valid && minX <= maxX && minY <= maxY)
            {
                far = distance;
                shift = new Vector2((minX + maxX) * 0.5f, (minY + maxY) * 0.5f);
            }
            else near = distance;
        }
        transform.SetPositionAndRotation(center + rotation * new Vector3(shift.x, shift.y, -far), rotation);
        float maxDepth = 0;
        foreach (Vector3 p in framingPoints) maxDepth = Mathf.Max(maxDepth, transform.InverseTransformPoint(p).z);
        RenderSettings.fogStartDistance = maxDepth + 3;
        RenderSettings.fogEndDistance = RenderSettings.fogStartDistance + profile.fogDepth;
        lastSize = new Vector2Int(width, height);
        lastSafeArea = SafeArea;
    }

    private Rect GetSafeArea(int width, int height)
    {
        float bottom = 0.26f, top = 1 - 58f / Mathf.Max(1, height);
        if (cardBar != null)
        {
            cardBar.GetWorldCorners(corners);
            Canvas canvas = cardBar.GetComponentInParent<Canvas>();
            float screenHeight = canvas != null ? canvas.pixelRect.height : Screen.height;
            Camera uiCamera = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;
            bottom = Mathf.Clamp01(RectTransformUtility.WorldToScreenPoint(uiCamera, corners[2]).y / Mathf.Max(1, screenHeight));
        }
        if (topBar != null)
        {
            topBar.GetWorldCorners(corners);
            Canvas canvas = topBar.GetComponentInParent<Canvas>();
            float screenHeight = canvas != null ? canvas.pixelRect.height : Screen.height;
            Camera uiCamera = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;
            top = Mathf.Clamp01(RectTransformUtility.WorldToScreenPoint(uiCamera, corners[0]).y / Mathf.Max(1, screenHeight));
        }
        if (top - bottom < 0.3f) { bottom = 0.26f; top = 0.946f; }
        float marginX = profile.uiMarginPixels / Mathf.Max(1, width);
        float marginY = profile.uiMarginPixels / Mathf.Max(1, height);
        return Rect.MinMaxRect(marginX, bottom + marginY, 1 - marginX, top - marginY);
    }
}
