using UnityEngine;

// Animates only the player's render meshes. Combat movement and colliders stay authoritative.
[DisallowMultipleComponent]
public sealed class PlayerIdleLoop : MonoBehaviour
{
    [Header("Frames: Open, Half Open, Closed")]
    [SerializeField] private Texture2D[] frames;
    [SerializeField] private MeshFilter[] visualMeshes;
    [SerializeField] private BattleScript actor;
    [Header("Timing")]
    [SerializeField, Min(.5f)] private float loopDuration = 2.8f;
    [Header("Secondary Motion")]
    [SerializeField, Range(0, .025f)] private float breathAmount = .008f;
    [SerializeField, Range(0, .015f)] private float swayAmount = .0035f;
    [SerializeField, Range(0, .02f)] private float tailAmount = .006f;
    [Header("Shared Canvas Alignment")]
    [SerializeField] private Vector2 uvScale = new(1, .845f);
    [SerializeField] private Vector2 uvOffset = new(0, .045f);
    [SerializeField] private float[] frameVerticalOffsets = { .0065f, .0032f, 0 };

    private static readonly int MainTexture = Shader.PropertyToID("_MainTex");
    private static readonly int BaseTexture = Shader.PropertyToID("_BaseMap");
    private Mesh animatedMesh;
    private Mesh[] originalMeshes;
    private Renderer[] renderers;
    private MaterialPropertyBlock[] originalProperties;
    private MaterialPropertyBlock[] frameProperties;
    private Vector3[] restVertices, vertices;
    private Vector2[] restUV, frameUV;
    private Vector3 horizontal, vertical;
    private float playbackTime;
    private int currentFrame = -1;
    public int CurrentFrameIndex => currentFrame;

    private void OnEnable()
    {
        if (frames == null || frames.Length != 3 || frames[0] == null || frames[1] == null || frames[2] == null ||
            visualMeshes == null || visualMeshes.Length == 0 || visualMeshes[0] == null || visualMeshes[0].sharedMesh == null)
            return;

        Mesh source = visualMeshes[0].sharedMesh;
        restVertices = source.vertices;
        restUV = source.uv;
        if (restUV.Length != restVertices.Length || !FindTextureAxes(source)) return;
        vertices = new Vector3[restVertices.Length];
        frameUV = new Vector2[restUV.Length];
        originalMeshes = new Mesh[visualMeshes.Length];
        renderers = new Renderer[visualMeshes.Length];
        originalProperties = new MaterialPropertyBlock[visualMeshes.Length];
        frameProperties = new MaterialPropertyBlock[visualMeshes.Length];
        animatedMesh = Instantiate(source);
        animatedMesh.name = "Player Idle Mesh";
        animatedMesh.hideFlags = HideFlags.DontSave;
        animatedMesh.MarkDynamic();
        for (int i = 0; i < visualMeshes.Length; i++)
        {
            var filter = visualMeshes[i];
            if (filter == null) continue;
            originalMeshes[i] = filter.sharedMesh;
            filter.sharedMesh = animatedMesh;
            renderers[i] = filter.GetComponent<Renderer>();
            if (renderers[i] == null) continue;
            originalProperties[i] = new MaterialPropertyBlock();
            frameProperties[i] = new MaterialPropertyBlock();
            renderers[i].GetPropertyBlock(originalProperties[i]);
            renderers[i].GetPropertyBlock(frameProperties[i]);
        }
        playbackTime = 0;
        currentFrame = -1;
        ApplyPose(0);
    }

    private bool FindTextureAxes(Mesh source)
    {
        // Derive the plane basis from UVs instead of assuming the quad faces local XY.
        int[] triangles = source.triangles;
        for (int i = 0; i < triangles.Length; i += 3)
        {
            int a = triangles[i], b = triangles[i + 1], c = triangles[i + 2];
            Vector2 uv1 = restUV[b] - restUV[a], uv2 = restUV[c] - restUV[a];
            float determinant = uv1.x * uv2.y - uv1.y * uv2.x;
            if (Mathf.Abs(determinant) < .00001f) continue;
            Vector3 edge1 = restVertices[b] - restVertices[a], edge2 = restVertices[c] - restVertices[a];
            horizontal = (edge1 * uv2.y - edge2 * uv1.y) / determinant;
            vertical = (edge2 * uv1.x - edge1 * uv2.x) / determinant;
            return true;
        }
        return false;
    }

    private void LateUpdate()
    {
        if (animatedMesh == null || Time.deltaTime <= 0 || (actor != null && (!actor.enabled || actor.health <= 0))) return;
        playbackTime += Time.deltaTime;
        ApplyPose(playbackTime);
    }

    private void ApplyPose(float time)
    {
        if (animatedMesh == null) return;
        float phase = Mathf.Repeat(time / Mathf.Max(.5f, loopDuration), 1f);
        // Closed -> half -> open -> half -> closed; never jump from open straight to closed.
        int index = phase < .68f || phase >= .86f ? 2 : phase < .72f || phase >= .82f ? 1 : 0;
        if (index != currentFrame)
        {
            currentFrame = index;
            float alignment = frameVerticalOffsets != null && index < frameVerticalOffsets.Length ? frameVerticalOffsets[index] : 0;
            for (int i = 0; i < restUV.Length; i++)
                frameUV[i] = Vector2.Scale(restUV[i], uvScale) + uvOffset + new Vector2(0, alignment);
            animatedMesh.uv = frameUV;
            for (int i = 0; i < renderers.Length; i++)
            {
                if (renderers[i] == null) continue;
                // Preserve hit feedback properties owned by the combat presentation.
                renderers[i].GetPropertyBlock(frameProperties[i]);
                frameProperties[i].SetTexture(MainTexture, frames[index]);
                frameProperties[i].SetTexture(BaseTexture, frames[index]);
                renderers[i].SetPropertyBlock(frameProperties[i]);
            }
        }

        float breath = Mathf.Sin(time * Mathf.PI * 2f / 2.8f);
        float sway = Mathf.Sin(time * Mathf.PI * 2f / 4.7f);
        for (int i = 0; i < vertices.Length; i++)
        {
            Vector2 uv = restUV[i];
            float weight = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(.18f, .72f, uv.y));
            float tail = 1f - Mathf.SmoothStep(0, 1, Mathf.InverseLerp(.12f, .52f, uv.x));
            float rise = breath * breathAmount * weight +
                Mathf.Sin(time * Mathf.PI * 2f / 3.6f - uv.x * 3f) * tailAmount * tail * weight;
            float shift = sway * swayAmount * weight;
            vertices[i] = restVertices[i] + horizontal * shift + vertical * rise;
        }
        // The visible body and its existing shadow copy share the exact deformation and frame.
        animatedMesh.vertices = vertices;
        animatedMesh.RecalculateBounds();
    }

    private void OnDisable()
    {
        if (animatedMesh == null) return;
        for (int i = 0; i < visualMeshes.Length; i++)
        {
            if (visualMeshes[i] != null && visualMeshes[i].sharedMesh == animatedMesh)
                visualMeshes[i].sharedMesh = originalMeshes[i];
            if (renderers[i] != null) renderers[i].SetPropertyBlock(originalProperties[i]);
        }
        Destroy(animatedMesh);
        animatedMesh = null;
        currentFrame = -1;
    }
}
