using System;
using TMPro;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;

// Presentation only. Card costs, effects and pile changes never wait for this animation.
public sealed class CardUseEffect : MonoBehaviour
{
    [Header("Scene Template")]
    public RawImage cardTemplate;
    [Header("Timing")]
    [Min(.01f)] public float liftDuration = .08f;
    [Min(.05f)] public float dissolveDuration = .37f;
    [Min(.05f)] public float fragmentDuration = .28f;
    [Header("Motion (Reference Pixels)")]
    [Min(0)] public float liftHeight = 16f;
    [Range(1, 1.15f)] public float liftScale = 1.025f;
    [Min(0)] public float flightArc = 65f;
    [Range(-1, 1)] public float dissolveDirection = 1f;
    [Header("Gold Leaf")]
    public Color gold = new(.91f, .69f, .32f, 1);
    public Color highlight = new(1f, .90f, .68f, 1);
    [Range(.002f, .08f)] public float edgeWidth = .018f;
    [Range(0, 2)] public float edgeStrength = .65f;
    [Range(0, 32)] public int fragmentCount = 18;
    [Range(2, 24)] public float fragmentSize = 9f;
    [Header("Resources")]
    [Range(2, 12)] public int poolSize = 6;
    [Range(256, 1024)] public int snapshotHeight = 512;

    private sealed class Slot
    {
        public RawImage image;
        public CardUseFragments fragments;
        public Material material;
        public RenderTexture texture;
        public Vector3 start, scale;
        public Quaternion rotation;
        public Vector2 target, viewport;
        public float age, seed;
        public int count;
        public bool active;
    }

    private Slot[] slots;
    private Material fragmentMaterial;
    private Camera captureCamera;
    private Canvas captureCanvas;
    private GameObject captureRoot;
    private uint sequence;
    private float pilePulse;
    private RawImage pulse;
    public bool IsReady => cardTemplate != null && cardTemplate.material != null &&
        cardTemplate.material.shader != null && cardTemplate.material.shader.isSupported;
    public int ActiveCount { get; private set; }
    public int ActiveFragmentCount { get; private set; }
    public float Duration => Mathf.Max(.01f, liftDuration) + Mathf.Max(.05f, dissolveDuration) + Mathf.Max(.05f, fragmentDuration);

    private void Awake()
    {
        if (cardTemplate != null) cardTemplate.gameObject.SetActive(false);
    }

    public void Play(BuffCards card, RectTransform discard)
    {
        if (!isActiveAndEnabled || !IsReady || card == null || discard == null) return;
        var source = card.transform.Find("CardVisual") as RectTransform;
        if (source == null) return;
        EnsurePool();
        Slot slot = null;
        foreach (var candidate in slots) if (!candidate.active) { slot = candidate; break; }
        if (slot == null)
        {
            slot = slots[0];
            foreach (var candidate in slots) if (candidate.age > slot.age) slot = candidate;
            Release(slot);
        }
        try
        {
            Rect bounds = VisualBounds(source);
            Capture(source, bounds, slot);
            var root = (RectTransform)transform;
            slot.start = root.InverseTransformPoint(source.TransformPoint(bounds.center));
            slot.scale = new Vector3(source.lossyScale.x / root.lossyScale.x, source.lossyScale.y / root.lossyScale.y, 1);
            slot.rotation = Quaternion.Inverse(root.rotation) * source.rotation;
            slot.target = root.InverseTransformPoint(discard.TransformPoint(discard.rect.center));
            slot.viewport = root.rect.size;
            slot.seed = (++sequence % 997) * .731f;
            slot.count = Mathf.Clamp(fragmentCount, 0, 32);
            slot.age = 0; slot.active = true;
            slot.image.rectTransform.sizeDelta = bounds.size;
            slot.image.texture = slot.texture;
            slot.image.gameObject.SetActive(true);
            slot.fragments.gameObject.SetActive(true);
            slot.material.SetColor("_Gold", gold);
            slot.material.SetColor("_Highlight", highlight);
            slot.material.SetFloat("_EdgeWidth", edgeWidth);
            slot.material.SetFloat("_EdgeStrength", edgeStrength);
            slot.material.SetFloat("_Seed", slot.seed);
            slot.material.SetFloat("_Direction", dissolveDirection);
            Evaluate(slot);
            Recount();
        }
        catch (Exception exception)
        {
            // A failed presentation must not interrupt a successfully committed play.
            Release(slot);
            Debug.LogWarning("Card departure snapshot failed: " + exception.Message, this);
        }
    }

    private void EnsurePool()
    {
        if (slots != null) return;
        fragmentMaterial = new Material(cardTemplate.material) { name = "Gold Leaf Fragments" };
        fragmentMaterial.SetFloat("_ParticleMode", 1);
        slots = new Slot[Mathf.Clamp(poolSize, 2, 12)];
        for (int i = 0; i < slots.Length; i++)
        {
            var image = Instantiate(cardTemplate, transform);
            image.name = "Card" + (i + 1).ToString("00");
            image.raycastTarget = false;
            image.rectTransform.anchorMin = image.rectTransform.anchorMax = new Vector2(.5f, .5f);
            image.rectTransform.pivot = new Vector2(.5f, .5f);
            var material = new Material(cardTemplate.material) { name = "Card Departure " + i };
            image.material = material;
            var fragments = new GameObject("Leaves" + (i + 1).ToString("00"), typeof(RectTransform), typeof(CanvasRenderer)).AddComponent<CardUseFragments>();
            fragments.transform.SetParent(transform, false);
            fragments.rectTransform.anchorMin = fragments.rectTransform.anchorMax = new Vector2(.5f, .5f);
            fragments.material = fragmentMaterial; fragments.raycastTarget = false;
            fragments.gameObject.SetActive(false);
            slots[i] = new Slot { image = image, material = material, fragments = fragments };
        }
        pulse = Instantiate(cardTemplate, transform);
        pulse.name = "DiscardGlow"; pulse.raycastTarget = false; pulse.texture = Texture2D.whiteTexture;
        pulse.material = fragmentMaterial;
        pulse.rectTransform.anchorMin = pulse.rectTransform.anchorMax = new Vector2(.5f, .5f);
        pulse.rectTransform.pivot = new Vector2(.5f, .5f);
        pulse.gameObject.SetActive(false);
    }

    private static Rect VisualBounds(RectTransform source)
    {
        Vector2 min = new(float.MaxValue, float.MaxValue), max = new(float.MinValue, float.MinValue);
        var corners = new Vector3[4];
        foreach (var graphic in source.GetComponentsInChildren<Graphic>())
        {
            if (!graphic.isActiveAndEnabled || graphic.color.a <= 0) continue;
            graphic.rectTransform.GetWorldCorners(corners);
            foreach (var point in corners)
            {
                Vector2 local = source.InverseTransformPoint(point);
                min = Vector2.Min(min, local); max = Vector2.Max(max, local);
            }
        }
        if (min.x == float.MaxValue) return source.rect;
        return Rect.MinMaxRect(min.x - 3, min.y - 3, max.x + 3, max.y + 3);
    }

    // A one-shot offscreen UI render preserves authored clipping, SDF text and sprite layers.
    // Only the visual subtree is cloned: no BuffCards, audio, gameplay or pointer components.
    private void Capture(RectTransform source, Rect bounds, Slot slot)
    {
        if (captureRoot == null)
        {
            captureRoot = new GameObject("Card Snapshot") { hideFlags = HideFlags.HideAndDontSave };
            captureRoot.transform.position = new Vector3(10000, 10000, 0);
            captureCamera = new GameObject("Camera").AddComponent<Camera>();
            captureCamera.transform.SetParent(captureRoot.transform, false);
            captureCamera.transform.localPosition = new Vector3(0, 0, -10);
            captureCamera.enabled = false; captureCamera.orthographic = true;
            captureCamera.clearFlags = CameraClearFlags.SolidColor; captureCamera.backgroundColor = Color.clear;
            captureCamera.nearClipPlane = .1f; captureCamera.farClipPlane = 20;
            captureCamera.cullingMask = 1 << 5; captureCamera.allowHDR = false; captureCamera.allowMSAA = false;
            var data = captureCamera.GetUniversalAdditionalCameraData();
            data.renderPostProcessing = false; data.renderShadows = false;
            data.requiresColorOption = CameraOverrideOption.Off; data.requiresDepthOption = CameraOverrideOption.Off;
            captureCanvas = new GameObject("Canvas", typeof(RectTransform)).AddComponent<Canvas>();
            captureCanvas.transform.SetParent(captureRoot.transform, false);
            captureCanvas.gameObject.layer = 5; captureCanvas.renderMode = RenderMode.WorldSpace;
            captureCanvas.worldCamera = captureCamera;
        }
        int height = Mathf.Clamp(snapshotHeight, 256, 1024);
        int width = Mathf.Clamp(Mathf.RoundToInt(height * bounds.width / Mathf.Max(1, bounds.height)), 64, 1024);
        if (slot.texture == null || slot.texture.width != width || slot.texture.height != height)
        {
            if (slot.texture != null) { slot.texture.Release(); Destroy(slot.texture); }
            slot.texture = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32)
            { name = "Card Snapshot", filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            slot.texture.Create();
        }
        var canvasRect = (RectTransform)captureCanvas.transform;
        canvasRect.sizeDelta = bounds.size;
        RectTransform clone = null;
        try
        {
            clone = Instantiate(source, canvasRect, false);
            clone.name = "Face";
            clone.anchorMin = clone.anchorMax = new Vector2(.5f, .5f);
            clone.sizeDelta = source.rect.size; clone.pivot = source.pivot;
            clone.anchoredPosition3D = -(Vector3)bounds.center;
            clone.localRotation = Quaternion.identity; clone.localScale = Vector3.one;
            foreach (Transform child in clone.GetComponentsInChildren<Transform>(true)) child.gameObject.layer = 5;
            foreach (var text in clone.GetComponentsInChildren<TMP_Text>()) text.ForceMeshUpdate();
            Canvas.ForceUpdateCanvases();
            captureCamera.orthographicSize = bounds.height * .5f;
            captureCamera.aspect = (float)width / height;
            RenderPipeline.SubmitRenderRequest(captureCamera,
                new UniversalRenderPipeline.SingleCameraRequest { destination = slot.texture });
        }
        finally
        {
            if (clone != null) { clone.gameObject.SetActive(false); Destroy(clone.gameObject); }
        }
    }

    private void Update() => AdvanceVisuals(Time.deltaTime);

    private void AdvanceVisuals(float delta)
    {
        if (slots == null) return;
        foreach (var slot in slots)
        {
            if (!slot.active) continue;
            slot.age += delta;
            if (slot.age >= Duration)
            {
                pulse.rectTransform.localPosition = slot.target;
                pilePulse = .18f; pulse.gameObject.SetActive(true);
                Release(slot);
            }
            else Evaluate(slot);
        }
        if (pilePulse > 0)
        {
            pilePulse = Mathf.Max(0, pilePulse - delta);
            float t = 1 - pilePulse / .18f;
            pulse.rectTransform.sizeDelta = Vector2.one * Mathf.Lerp(22, 42, t);
            pulse.color = new Color(highlight.r, highlight.g, highlight.b, Mathf.Sin(t * Mathf.PI) * .45f);
            if (pilePulse == 0) pulse.gameObject.SetActive(false);
        }
        Recount();
    }

    private void Evaluate(Slot slot)
    {
        float lift = Mathf.SmoothStep(0, 1, slot.age / Mathf.Max(.01f, liftDuration));
        float progress = Mathf.Clamp01((slot.age - liftDuration) / Mathf.Max(.05f, dissolveDuration));
        var rect = slot.image.rectTransform;
        Vector2 resize = (((RectTransform)transform).rect.size - slot.viewport) * .5f;
        rect.localPosition = slot.start + Vector3.up * (liftHeight * lift - resize.y);
        rect.localScale = slot.scale * Mathf.Lerp(1, liftScale, lift);
        rect.localRotation = Quaternion.Slerp(slot.rotation, Quaternion.identity, lift * .65f);
        slot.material.SetFloat("_Progress", progress);
        slot.material.SetFloat("_Glint", Mathf.Sin(Mathf.Clamp01(slot.age / .2f) * Mathf.PI) * .23f);
        slot.image.enabled = progress < 1;
        slot.fragments.Configure(this, slot.age, slot.seed, slot.count,
            rect.localPosition, Vector2.Scale(rect.sizeDelta, rect.localScale), slot.target + new Vector2(resize.x, -resize.y));
    }

    private void Recount()
    {
        ActiveCount = ActiveFragmentCount = 0;
        if (slots == null) return;
        foreach (var slot in slots) if (slot.active)
        { ActiveCount++; ActiveFragmentCount += slot.fragments.VisibleCount; }
    }

    private static void Release(Slot slot)
    {
        slot.active = false;
        slot.image.gameObject.SetActive(false);
        slot.fragments.gameObject.SetActive(false);
    }

    public void Clear()
    {
        if (slots != null) foreach (var slot in slots) Release(slot);
        if (pulse != null) pulse.gameObject.SetActive(false);
        pilePulse = 0; Recount();
    }

    private void OnDisable() => Clear();

    private void OnDestroy()
    {
        if (slots != null) foreach (var slot in slots)
        {
            if (slot.texture != null) { slot.texture.Release(); Destroy(slot.texture); }
            if (slot.material != null) Destroy(slot.material);
        }
        if (fragmentMaterial != null) Destroy(fragmentMaterial);
        if (captureRoot != null) Destroy(captureRoot);
    }
}

// One pooled mesh per departure; tapered laurel leaves and uneven foil, never square pixels.
[RequireComponent(typeof(CanvasRenderer))]
public sealed class CardUseFragments : MaskableGraphic
{
    private CardUseEffect style;
    private float age, seed;
    private int count;
    private Vector2 origin, size, target;
    public int VisibleCount { get; private set; }

    public void Configure(CardUseEffect settings, float time, float variation, int amount,
        Vector2 from, Vector2 dimensions, Vector2 to)
    {
        style = settings; age = time; seed = variation; count = amount;
        origin = from; size = dimensions; target = to;
        SetVerticesDirty();
    }

    private static float Hash(float x) => Mathf.Repeat(Mathf.Sin(x * 127.1f + 311.7f) * 43758.5453f, 1);

    protected override void OnPopulateMesh(VertexHelper mesh)
    {
        mesh.Clear(); VisibleCount = 0;
        if (style == null) return;
        for (int i = 0; i < count; i++)
        {
            float h = Hash(i * 2.31f + seed), v = Hash(i * 4.73f + seed);
            Vector2 uv = new(.15f + .7f * h, .16f + .68f * v);
            float sweep = .65f * (1 - uv.y) + .35f * (style.dissolveDirection >= 0 ? uv.x : 1 - uv.x);
            float born = style.liftDuration + style.dissolveDuration * sweep;
            float life = Mathf.Max(.05f, style.fragmentDuration) + (1 - sweep) * .08f;
            float t = (age - born) / life;
            if (t <= 0 || t >= 1) continue;
            VisibleCount++;
            Vector2 start = origin + Vector2.Scale(uv - Vector2.one * .5f, size);
            float travel = t * t * (3 - 2 * t);
            Vector2 control = Vector2.Lerp(start, target, .45f) + Vector2.up * (style.flightArc * (.6f + h));
            Vector2 point = (1 - travel) * (1 - travel) * start + 2 * (1 - travel) * travel * control + travel * travel * target;
            float angle = (h * 240 + t * (120 + v * 200)) * Mathf.Deg2Rad;
            float length = style.fragmentSize * (i % 5 == 0 ? 1.75f : .65f + v * .6f) * (1 - t * .6f);
            float width = length * (i % 5 == 0 ? .26f : .4f) * (.5f + .5f * Mathf.Abs(Mathf.Cos(t * 8 + h * 4)));
            Color color = Color.Lerp(style.gold, style.highlight, .2f + .6f * v);
            color.a *= Mathf.SmoothStep(0, 1, t / .12f) * (1 - Mathf.SmoothStep(.75f, 1, t));
            Vector2 axis = new(Mathf.Cos(angle), Mathf.Sin(angle));
            Vector2 normal = new(-axis.y, axis.x);
            if (i % 5 == 0)
            {
                const int segments = 6;
                int begin = mesh.currentVertCount;
                for (int j = 0; j <= segments; j++)
                {
                    float along = j / (float)segments;
                    float curve = Mathf.Sin(along * Mathf.PI);
                    Vector2 center = point + axis * ((along * 2 - 1) * length) + normal * (curve * width * .3f);
                    Add(mesh, center + normal * (curve * width), color, new Vector2(along, 1));
                    Add(mesh, center, color, new Vector2(along, .5f));
                    Add(mesh, center - normal * (curve * width * .8f), color, new Vector2(along, 0));
                    if (j == 0) continue;
                    int a = begin + (j - 1) * 3, b = a + 3;
                    mesh.AddTriangle(a, b, a + 1); mesh.AddTriangle(b, b + 1, a + 1);
                    mesh.AddTriangle(a + 1, b + 1, a + 2); mesh.AddTriangle(b + 1, b + 2, a + 2);
                }
                continue;
            }
            int first = mesh.currentVertCount;
            // A curved, tapered silhouette with a brighter ridge.
            Add(mesh, point - axis * length, color, new Vector2(0, .5f));
            Add(mesh, point - axis * length * .15f + normal * width, color, new Vector2(.45f, 1));
            Add(mesh, point + axis * length, color, new Vector2(1, .5f));
            Add(mesh, point + axis * length * .1f - normal * width * .75f, color, new Vector2(.55f, 0));
            mesh.AddTriangle(first, first + 1, first + 2);
            mesh.AddTriangle(first, first + 2, first + 3);
        }
    }

    private static void Add(VertexHelper mesh, Vector2 position, Color color, Vector2 uv) =>
        mesh.AddVert(new Vector3(position.x, position.y, 0), color, uv);
}
