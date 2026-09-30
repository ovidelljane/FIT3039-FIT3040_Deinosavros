using UnityEngine;

// Deforms the existing portrait and shadow together without moving gameplay transforms.
[DisallowMultipleComponent]
[RequireComponent(typeof(BattleScript), typeof(MeshFilter))]
public sealed class EnemyIdleMotion : MonoBehaviour
{
    [Header("Timing")]
    [SerializeField, Min(1f)] private float breathDuration = 3.2f;
    [SerializeField, Range(0, 1)] private float phaseOffset;
    [Header("Secondary Motion")]
    [SerializeField, Range(0, .025f)] private float breathAmount = .012f;
    [SerializeField, Range(0, .015f)] private float swayAmount = .0045f;
    [SerializeField, Range(0, .02f)] private float tailAmount = .008f;
    [SerializeField, Range(0, 3)] private float headAngle = 1.4f;
    [SerializeField] private Vector2 headPivot = new(.52f, .42f);
    [SerializeField, Range(0, .4f)] private float plantedHeight = .24f;

    private BattleScript actor;
    private MeshFilter[] visuals;
    private Mesh[] originals;
    private Mesh animatedMesh;
    private Vector3[] restVertices, vertices;
    private Vector2[] restUV;
    private Vector3 horizontal, vertical;
    private float playbackTime;
    public bool IsReady => animatedMesh != null;

    private void OnEnable()
    {
        actor = GetComponent<BattleScript>();
        var source = GetComponent<MeshFilter>().sharedMesh;
        if (source == null || !source.isReadable) return;
        restVertices = source.vertices; restUV = source.uv;
        if (restUV.Length != restVertices.Length || !FindTextureAxes(source)) return;
        visuals = GetComponentsInChildren<MeshFilter>(true);
        originals = new Mesh[visuals.Length];
        vertices = new Vector3[restVertices.Length];
        animatedMesh = Instantiate(source);
        animatedMesh.name = "Enemy Idle Mesh";
        animatedMesh.hideFlags = HideFlags.DontSave;
        animatedMesh.MarkDynamic();
        for (int i = 0; i < visuals.Length; i++)
        {
            originals[i] = visuals[i].sharedMesh;
            // Only the body and matching shadow use this portrait, not unrelated attachments.
            if (originals[i] == source) visuals[i].sharedMesh = animatedMesh;
        }
        playbackTime = 0;
        ApplyPose(0);
    }

    private bool FindTextureAxes(Mesh source)
    {
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
        if (!IsReady || Time.deltaTime <= 0 || actor == null || !actor.enabled || actor.health <= 0) return;
        playbackTime += Time.deltaTime;
        ApplyPose(playbackTime);
    }

    private void ApplyPose(float time)
    {
        float phase = time * Mathf.PI * 2f / Mathf.Max(1f, breathDuration) + phaseOffset * Mathf.PI * 2f;
        float breath = Mathf.Sin(phase), sway = Mathf.Sin(phase * .67f + .8f);
        float angle = Mathf.Sin(phase * .83f + .45f) * headAngle * Mathf.Deg2Rad;
        float sine = Mathf.Sin(angle), cosine = Mathf.Cos(angle);
        for (int i = 0; i < vertices.Length; i++)
        {
            Vector2 uv = restUV[i];
            float planted = Smooth(plantedHeight, .62f, uv.y);
            float head = Smooth(.49f, .64f, uv.x) * Smooth(.32f, .55f, uv.y);
            float tail = (1f - Smooth(.23f, .41f, uv.x)) * Smooth(.28f, .49f, uv.y);
            Vector2 relative = uv - headPivot;
            Vector2 nod = new Vector2(relative.x * cosine - relative.y * sine,
                relative.x * sine + relative.y * cosine) - relative;
            float shift = sway * swayAmount + breath * breathAmount * .45f * (uv.x - .46f);
            float rise = breath * breathAmount + Mathf.Sin(phase * .74f - uv.x * 4f) * tailAmount * tail;
            Vector2 motion = (new Vector2(shift, rise) + nod * head) * planted;
            vertices[i] = restVertices[i] + horizontal * motion.x + vertical * motion.y;
        }
        animatedMesh.vertices = vertices;
        animatedMesh.RecalculateBounds();
    }

    private static float Smooth(float start, float end, float value) =>
        Mathf.SmoothStep(0, 1, Mathf.InverseLerp(start, end, value));

    private void OnDisable()
    {
        if (animatedMesh == null) return;
        for (int i = 0; i < visuals.Length; i++)
            if (visuals[i] != null && visuals[i].sharedMesh == animatedMesh)
                visuals[i].sharedMesh = originals[i];
        Destroy(animatedMesh);
        animatedMesh = null;
    }
}
