using System;
using TMPro;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

// Presentation only. The owning panel commits exactly once before starting the burn.
public sealed class SacrificeBurnEffect : MonoBehaviour
{
    [Header("Authored Template")]
    public UnityEngine.UI.RawImage cardTemplate;
    public RectTransform focusAnchor;
    [Header("Center Presentation")]
    [Min(.1f)] public float focusScale = 1.25f;
    [Header("Ash Lifetime")]
    [Min(.1f)] public float ashDuration = .40f;
    [Header("Ash and Sparks (Reference Pixels)")]
    [Range(0, 64)] public int particleCount = 42;
    [Range(1, 15)] public float emberSize = 5;
    [Range(10, 180)] public float rise = 82;
    [Range(0, 80)] public float drift = 25;
    [Range(0, .5f)] public float smokeOpacity = .12f;
    [Range(256, 1024)] public int snapshotHeight = 640;

    private UnityEngine.UI.RawImage face;
    private SacrificeAshGraphic ash;
    private Material faceMaterial, ashMaterial;
    private RenderTexture snapshot;
    private GameObject captureRoot;
    private Camera captureCamera;
    private Canvas captureCanvas;
    private RectTransform source;
    private Rect bounds;
    private float seed;
    private int sequence;
    private float emissionDuration;
    public bool IsPlaying { get; private set; }
    public float Progress { get; private set; }
    public float Elapsed { get; private set; }
    public Vector3 FacePosition => face != null ? face.transform.position : transform.position;
    public int VisibleParticles => ash != null ? ash.VisibleCount : 0;
    public float BurnEmissionDuration => emissionDuration;
    public Color EmberColor => cardTemplate.material.GetColor("_EmberColor");
    public Color HotEdgeColor => cardTemplate.material.GetColor("_HotColor");
    public SacrificeTimelineFrame CurrentFrame { get; private set; }
    public bool IsReady => cardTemplate != null && cardTemplate.material != null &&
        cardTemplate.material.shader != null && cardTemplate.material.shader.isSupported;

    public bool Begin(RectTransform card)
    {
        Clear();
        if (!IsReady || !isActiveAndEnabled || card == null) return false;
        try
        {
            EnsurePresentation();
            source = card; bounds = Bounds(card);
            Capture(card);
            seed = (++sequence % 997) * .731f;
            face.texture = snapshot; face.rectTransform.sizeDelta = bounds.size;
            faceMaterial.SetFloat("_Seed", seed);
            ashMaterial.SetFloat("_Seed", seed);
            face.gameObject.SetActive(true); ash.gameObject.SetActive(true);
            IsPlaying = true;
            EvaluateFrame(new SacrificeTimelineFrame { burnAge = -1, burnDuration = 1 });
            return true;
        }
        catch (Exception exception)
        {
            Clear();
            Debug.LogWarning("Sacrifice burn capture failed: " + exception.Message, this);
            return false;
        }
    }

    public void RefreshSnapshot()
    {
        if (!IsPlaying || source == null) return;
        bounds = Bounds(source); Capture(source); face.texture = snapshot; face.rectTransform.sizeDelta = bounds.size;
    }

    public void EvaluateFrame(SacrificeTimelineFrame frame)
    {
        if (!IsPlaying || source == null) return;
        CurrentFrame = frame;
        Elapsed = Mathf.Max(0, frame.elapsed);
        Progress = Mathf.Clamp01(frame.burn);
        emissionDuration = Mathf.Max(.1f, frame.burnDuration);
        var root = (RectTransform)transform;
        Vector3 origin = root.InverseTransformPoint(source.TransformPoint(bounds.center));
        Vector3 destination = focusAnchor != null ? root.InverseTransformPoint(focusAnchor.position) : Vector3.zero;
        face.rectTransform.localPosition = Vector3.Lerp(origin, destination, frame.move);
        Vector3 startScale = new(source.lossyScale.x / root.lossyScale.x, source.lossyScale.y / root.lossyScale.y, 1);
        Vector3 targetScale = focusAnchor != null
            ? new(focusAnchor.lossyScale.x / root.lossyScale.x, focusAnchor.lossyScale.y / root.lossyScale.y, 1) : startScale;
        face.rectTransform.localScale = Vector3.Scale(Vector3.Lerp(startScale, targetScale * focusScale, frame.expand), cardTemplate.rectTransform.localScale);
        Quaternion destinationRotation = focusAnchor != null ? Quaternion.Inverse(root.rotation) * focusAnchor.rotation : Quaternion.identity;
        face.rectTransform.localRotation = Quaternion.Slerp(Quaternion.Inverse(root.rotation) * source.rotation, destinationRotation, frame.move);
        face.color = cardTemplate.color;
        faceMaterial.CopyPropertiesFromMaterial(cardTemplate.material);
        ashMaterial.CopyPropertiesFromMaterial(cardTemplate.material);
        faceMaterial.SetFloat("_Seed", seed); ashMaterial.SetFloat("_Seed", seed);
        ashMaterial.SetFloat("_ParticleMode", 1);
        faceMaterial.SetFloat("_Progress", Progress);
        faceMaterial.SetFloat("_Clock", Elapsed);
        ashMaterial.SetFloat("_Clock", Elapsed);
        face.enabled = Progress < 1;
        ash.SetFrame(this, Mathf.Max(0, frame.burnAge), seed, face.rectTransform.localPosition,
            Vector2.Scale(bounds.size, face.rectTransform.localScale));
    }

    private void EnsurePresentation()
    {
        if (face != null) return;
        face = Instantiate(cardTemplate, transform); face.name = "BurningCard"; face.raycastTarget = false;
        if (!Application.isPlaying) face.gameObject.hideFlags = HideFlags.HideAndDontSave;
        face.rectTransform.anchorMin = face.rectTransform.anchorMax = face.rectTransform.pivot = new(.5f, .5f);
        faceMaterial = new Material(cardTemplate.material) { name = "Sacrifice Burn Instance" };
        ashMaterial = new Material(cardTemplate.material) { name = "Sacrifice Ash Instance" };
        ashMaterial.SetFloat("_ParticleMode", 1); face.material = faceMaterial;
        ash = new GameObject("Ash", typeof(RectTransform), typeof(CanvasRenderer)).AddComponent<SacrificeAshGraphic>();
        ash.transform.SetParent(transform, false);
        if (!Application.isPlaying) ash.gameObject.hideFlags = HideFlags.HideAndDontSave;
        ash.rectTransform.anchorMin = Vector2.zero; ash.rectTransform.anchorMax = Vector2.one;
        ash.rectTransform.offsetMin = ash.rectTransform.offsetMax = Vector2.zero;
        ash.material = ashMaterial; ash.raycastTarget = false;
        GetComponentInParent<Canvas>().additionalShaderChannels |= AdditionalCanvasShaderChannels.TexCoord1;
    }

    private static Rect Bounds(RectTransform card)
    {
        Vector2 min = new(float.MaxValue, float.MaxValue), max = new(float.MinValue, float.MinValue);
        var corners = new Vector3[4];
        foreach (var graphic in card.GetComponentsInChildren<UnityEngine.UI.Graphic>())
        {
            if (!graphic.isActiveAndEnabled || graphic.color.a <= 0) continue;
            graphic.rectTransform.GetWorldCorners(corners);
            foreach (var point in corners)
            { Vector2 p = card.InverseTransformPoint(point); min = Vector2.Min(min, p); max = Vector2.Max(max, p); }
        }
        return min.x == float.MaxValue ? card.rect : Rect.MinMaxRect(min.x-3, min.y-3, max.x+3, max.y+3);
    }

    private void Capture(RectTransform card)
    {
        if (captureRoot == null)
        {
            captureRoot = new GameObject("Sacrifice Snapshot") { hideFlags = HideFlags.HideAndDontSave };
            captureRoot.transform.position = new(12000, 12000, 0);
            captureCamera = new GameObject("Camera").AddComponent<Camera>();
            captureCamera.transform.SetParent(captureRoot.transform, false);
            captureCamera.transform.localPosition = new(0, 0, -10);
            captureCamera.enabled = false; captureCamera.orthographic = true;
            captureCamera.clearFlags = CameraClearFlags.SolidColor; captureCamera.backgroundColor = Color.clear;
            captureCamera.nearClipPlane = .1f; captureCamera.farClipPlane = 20;
            captureCamera.cullingMask = 1 << 5; captureCamera.allowHDR = false; captureCamera.allowMSAA = false;
            var data = captureCamera.GetUniversalAdditionalCameraData();
            data.renderPostProcessing = false; data.renderShadows = false;
            data.requiresColorOption = data.requiresDepthOption = CameraOverrideOption.Off;
            captureCanvas = new GameObject("Canvas", typeof(RectTransform)).AddComponent<Canvas>();
            captureCanvas.transform.SetParent(captureRoot.transform, false);
            captureCanvas.gameObject.layer = 5; captureCanvas.renderMode = RenderMode.WorldSpace;
            captureCanvas.worldCamera = captureCamera;
        }
        int height = Mathf.Clamp(snapshotHeight, 256, 1024);
        int width = Mathf.Clamp(Mathf.RoundToInt(height*bounds.width/Mathf.Max(1,bounds.height)), 64, 1024);
        if (snapshot == null || snapshot.width != width || snapshot.height != height)
        {
            if (snapshot != null) { snapshot.Release(); Release(snapshot); }
            snapshot = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32)
            { name = "Sacrifice Card Snapshot", filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            snapshot.Create();
        }
        var canvasRect = (RectTransform)captureCanvas.transform; canvasRect.sizeDelta = bounds.size;
        RectTransform clone = null;
        try
        {
            clone = Instantiate(card, canvasRect, false);
            clone.anchorMin = clone.anchorMax = new(.5f, .5f); clone.pivot = card.pivot;
            clone.sizeDelta = card.rect.size; clone.anchoredPosition3D = -(Vector3)bounds.center;
            clone.localRotation = Quaternion.identity; clone.localScale = Vector3.one;
            var group = clone.GetComponent<CanvasGroup>(); if (group != null) group.alpha = 1;
            foreach (Transform child in clone.GetComponentsInChildren<Transform>(true)) child.gameObject.layer = 5;
            foreach (var text in clone.GetComponentsInChildren<TMP_Text>()) text.ForceMeshUpdate();
            Canvas.ForceUpdateCanvases();
            captureCamera.orthographicSize = bounds.height*.5f; captureCamera.aspect = (float)width/height;
            RenderPipeline.SubmitRenderRequest(captureCamera, new UniversalRenderPipeline.SingleCameraRequest { destination = snapshot });
        }
        finally { if (clone != null) { clone.gameObject.SetActive(false); Release(clone.gameObject); } }
    }

    public void Clear()
    {
        IsPlaying = false; Progress = Elapsed = 0;
        if (face != null) face.gameObject.SetActive(false);
        if (ash != null) { ash.ClearFrame(); ash.gameObject.SetActive(false); }
        source = null;
    }
    private void Awake() { if (cardTemplate != null) cardTemplate.gameObject.SetActive(false); }
    private void OnDisable() => Clear();
    public void ReleasePreviewResources()
    {
        if (Application.isPlaying) return;
        if (face != null) Release(face.gameObject);
        if (ash != null) Release(ash.gameObject);
        ReleaseOwnedResources(); face = null; ash = null;
    }
    private void OnDestroy()
    { ReleaseOwnedResources(); }
    private void ReleaseOwnedResources()
    {
        if (snapshot != null) { snapshot.Release(); Release(snapshot); }
        if (captureRoot != null) Release(captureRoot);
        if (faceMaterial != null) Release(faceMaterial);
        if (ashMaterial != null) Release(ashMaterial);
        snapshot = null; captureRoot = null; captureCamera = null; captureCanvas = null; faceMaterial = null; ashMaterial = null;
    }
    private static void Release(UnityEngine.Object value)
    { if (value != null) { if (Application.isPlaying) Destroy(value); else DestroyImmediate(value); } }
}

[RequireComponent(typeof(CanvasRenderer))]
public sealed class SacrificeAshGraphic : UnityEngine.UI.MaskableGraphic
{
    private SacrificeBurnEffect style;
    private float age, seed;
    private Vector2 origin, size;
    public int VisibleCount { get; private set; }
    public void SetFrame(SacrificeBurnEffect settings, float time, float variation, Vector2 center, Vector2 extent)
    { style = settings; age = time; seed = variation; origin = center; size = extent; SetVerticesDirty(); }
    public void ClearFrame() { style = null; VisibleCount = 0; SetVerticesDirty(); }
    protected override void OnPopulateMesh(UnityEngine.UI.VertexHelper mesh)
    {
        mesh.Clear(); VisibleCount = 0;
        if (style == null) return;
        int count = Mathf.Clamp(style.particleCount, 0, 64);
        for (int i = 0; i < count; i++)
        {
            float a = Hash(i*3+seed), b = Hash(i*7+seed+2), c = Hash(i*11+seed+4);
            float born = a*style.BurnEmissionDuration*.80f;
            float life = Mathf.Min(style.BurnEmissionDuration+style.ashDuration-born, style.ashDuration+.12f+b*.18f);
            float t = (age-born)/Mathf.Max(.1f,life);
            if (t <= 0 || t >= 1) continue;
            int kind = i % 9 == 0 ? 2 : i % 3 == 0 ? 1 : 0;
            float inset = a*.36f;
            Vector2 point = i%2 == 0 ? new((i%4 == 0 ? -1 : 1)*(.47f-inset),b-.5f) :
                new(b-.5f,(i%4 == 1 ? -1 : 1)*(.47f-inset));
            Vector2 center = origin+Vector2.Scale(point,size);
            center += new Vector2((c-.5f)*style.drift*t+Mathf.Sin(t*5+c*6)*style.drift*.16f*t, style.rise*t*(.55f+b*.45f));
            float fade = Mathf.Sin(Mathf.Pow(t,.7f)*Mathf.PI);
            Color tint = kind == 2 ? new Color(.28f,.22f,.17f, style.smokeOpacity*fade) :
                kind == 1 ? new Color(.43f,.32f,.22f,fade*.85f) : Color.Lerp(style.EmberColor,style.HotEdgeColor,(1-t)*.75f);
            if (kind == 0) tint.a = fade;
            float radius = kind == 2 ? style.emberSize*(3+t*6) : style.emberSize*(.6f+c*.8f)*(1-.45f*t);
            Vector2 extents = kind == 2 ? new(radius,radius*1.3f) : new(radius*(.35f+.65f*Mathf.Abs(Mathf.Cos(t*9+c))),radius);
            Quad(mesh,center,extents,c*6+t*(kind == 2 ? .2f : 4),tint,kind);
            VisibleCount++;
        }
    }
    private static void Quad(UnityEngine.UI.VertexHelper mesh, Vector2 center, Vector2 size, float angle, Color tint, int kind)
    {
        int start = mesh.currentVertCount;
        Vector2 right = new(Mathf.Cos(angle)*size.x,Mathf.Sin(angle)*size.x);
        Vector2 up = new(-Mathf.Sin(angle)*size.y,Mathf.Cos(angle)*size.y);
        for (int i = 0; i < 4; i++)
        {
            float x = i == 1 || i == 2 ? 1 : -1, y = i >= 2 ? 1 : -1;
            var v = UnityEngine.UIVertex.simpleVert; v.position = center+right*x+up*y;
            v.color = tint; v.uv0 = new((x+1)*.5f,(y+1)*.5f); v.uv1 = new(kind,0);
            mesh.AddVert(v);
        }
        mesh.AddTriangle(start,start+1,start+2); mesh.AddTriangle(start,start+2,start+3);
    }
    private static float Hash(float value) => Mathf.Repeat(Mathf.Sin(value*127.1f+311.7f)*43758.5453f,1);
}
