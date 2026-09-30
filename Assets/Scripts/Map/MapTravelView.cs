using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

[Serializable]
public sealed class MapTravelSite
{
    public string nodeId;
    public Vector3 point;
}

public sealed class MapTravelView : MonoBehaviour
{
    public MapTravelProfile profile;
    public Transform environment;
    public MapTravelSite[] sites = Array.Empty<MapTravelSite>();
    public MapTravelPath[] paths = Array.Empty<MapTravelPath>();
    public Renderer portalRenderer;
    public Vector3 portalFocus;
    private RunSession session;
    private Camera view;
    private MeshRenderer core, halo, sigil, trailRenderer;
    private MeshRenderer[] nodeSymbols, routeSymbols, typeSymbols;
    private MaterialPropertyBlock properties, portalBefore, portalPulse;
    private readonly List<Mesh> ownedMeshes = new();
    private readonly System.Random random = new(704021);
    private Vector3 floor, floatingPoint;
    private MapTravelPath activePath;
    private string selected;
    private float previewBlend, gather, arrival, appear = 1, lastParticle, head, dockBlend = 1;
    private bool moving, bossGather, portalChanged;
    private Mesh particleMesh;
    private Particle[] particles;
    private Vector3[] vertices;
    private Vector2[] uv;
    private Color[] colors;
    private struct Particle { public Vector3 position, velocity; public float age, life, size, rotation; public bool leaf, returning; }
    private static readonly int Atlas = Shader.PropertyToID("_AtlasRect"), Opacity = Shader.PropertyToID("_Opacity"),
        Emission = Shader.PropertyToID("_Emission"), Tint = Shader.PropertyToID("_Tint"), Mode = Shader.PropertyToID("_Mode"),
        Head = Shader.PropertyToID("_Head"), Repeats = Shader.PropertyToID("_Repeats"), Soft = Shader.PropertyToID("_SoftDepth");
    private static readonly int CreamTint = Shader.PropertyToID("_CreamTint");
    public Vector3 FloorPosition => floor;
    public Vector3 CorePosition => floatingPoint;
    public bool IsMoving => moving;
    public int LiveParticles { get; private set; }
#if UNITY_EDITOR || DEVELOPMENT_BUILD
    public bool MeasureUpdateAllocations { get; set; }
    public long MeasuredUpdateBytes { get; private set; }
    public int MeasuredUpdateCount { get; private set; }
#endif
    public static Vector4 AtlasRect(int glyph) => new((glyph % 4) * .25f, (glyph / 4) * .25f, .25f, .25f);
    public string PreviewNodeId => selected;
    public Rect IconScreenRect(string id)
    {
        if (view == null) return Rect.zero;
        Vector3 p = view.WorldToScreenPoint(SitePoint(id) + Vector3.up * profile.iconHeight);
        if (p.z <= 0) return Rect.zero;
        float size = profile.iconPixels * view.pixelHeight / 1080f;
        return new Rect(p.x - size * .5f, p.y - size * .5f, size, size);
    }

    public void Bind(RunSession owner)
    {
        if (session != null && session.Progress != null) session.Progress.Changed -= RefreshSymbols;
        session = owner; view = Camera.main; EnsureVisuals();
        foreach (var path in paths) path.Prepare();
        session.Progress.Changed += RefreshSymbols;
        floor = SitePoint(session.Progress.CurrentNodeId); appear = 0;
        selected = null; activePath = null; moving = bossGather = false;
        RefreshSymbols(); PositionCore();
    }
    public bool HasPath(string from, string to) => from == to || FindPath(from, to) != null;
    public MapTravelPath FindPath(string from, string to)
    {
        foreach (var path in paths) if (path.fromNodeId == from && path.toNodeId == to) return path;
        return null;
    }
    public Vector3 SitePoint(string id)
    {
        foreach (var site in sites)
            if (site.nodeId == id) return environment.TransformPoint(site.point);
        throw new InvalidOperationException("Missing travel site: " + id);
    }
    public void Preview(string id)
    {
        selected = id; previewBlend = 0;
        activePath = session == null ? null : FindPath(session.Progress.CurrentNodeId, id);
        RefreshSymbols();
    }
    public void RestorePosition(string id)
    {
        floor = SitePoint(id); moving = bossGather = false; gather = arrival = 0; dockBlend = 1;
        RestorePortal(); appear = 0; Preview(null); PositionCore();
    }
    public IEnumerator Animate(MapTravelTicket ticket)
    {
        if (core == null) EnsureVisuals();
        activePath = ticket.IsInitial ? null : FindPath(ticket.FromNodeId, ticket.NodeId);
        if (!ticket.IsInitial && activePath == null) throw new InvalidOperationException("No authored travel route.");
        moving = true; head = 0; selected = ticket.NodeId;
        CollectMotes();
        for (float t = 0; t < profile.gatherTime; t += Time.unscaledDeltaTime)
        { gather = Mathf.Clamp01(t / profile.gatherTime); dockBlend = 1 - Mathf.SmoothStep(0, 1, gather); yield return null; }
        gather = 1; dockBlend = 0;
        if (ticket.IsInitial)
        {
            for (float t = 0; t < .7f; t += Time.unscaledDeltaTime) { arrival = t / .7f; yield return null; }
        }
        else
        {
            float duration = Mathf.Clamp(activePath.Length / profile.moveSpeed, profile.minimumMoveTime, profile.maximumMoveTime);
            for (float t = 0; t < duration; t += Time.unscaledDeltaTime)
            {
                head = MapTravelPath.EaseDistance(t / duration); floor = activePath.Sample(head); yield return null;
            }
            head = 1; floor = activePath.Sample(1); EmitBurst(32);
            bool recalled = false;
            for (float t = 0; t < profile.arrivalTime; t += Time.unscaledDeltaTime)
            {
                arrival = t / profile.arrivalTime;
                dockBlend = ticket.IsBoss ? 0 : Mathf.SmoothStep(0, 1, arrival);
                if (!recalled && arrival > .4f) { CollectMotes(); recalled = true; }
                yield return null;
            }
        }
        if (ticket.IsBoss)
        {
            bossGather = true; Vector3 start = floor + Vector3.up * profile.hoverHeight;
            Vector3 target = environment.TransformPoint(portalFocus);
            if (portalRenderer != null) { portalRenderer.GetPropertyBlock(portalBefore); portalChanged = true; }
            for (float t = 0; t < profile.bossGatherTime; t += Time.unscaledDeltaTime)
            {
                float u = Mathf.SmoothStep(0, 1, t / profile.bossGatherTime);
                floatingPoint = Vector3.Lerp(start, target, u); gather = 1 - u;
                PulsePortal(1 + Mathf.Sin(u * Mathf.PI) * .65f); yield return null;
            }
            floatingPoint = target; gather = 0;
        }
        moving = false; arrival = 0; dockBlend = ticket.IsBoss ? 0 : 1;
    }
    private void LateUpdate()
    {
        if (session == null || core == null || view == null) return;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        long allocationStart = MeasureUpdateAllocations ? GC.GetAllocatedBytesForCurrentThread() : 0;
#endif
        float dt = Time.unscaledDeltaTime;
        appear = Mathf.MoveTowards(appear, 1, dt / .5f);
        previewBlend = Mathf.MoveTowards(previewBlend, 1, dt / profile.previewFade);
        PositionCore(); PositionIcons(); UpdateRoutes(); UpdateParticles(dt);
        float rate = moving ? .035f : .22f;
        lastParticle += dt;
        if (lastParticle >= rate && !bossGather) { lastParticle = 0; EmitParticle(moving); }
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        if (MeasureUpdateAllocations)
        {
            MeasuredUpdateBytes += GC.GetAllocatedBytesForCurrentThread() - allocationStart;
            MeasuredUpdateCount++;
        }
#endif
    }
    private void PositionCore()
    {
        if (!bossGather)
        {
            floatingPoint = floor + Vector3.up * (profile.hoverHeight + Mathf.Sin(Time.unscaledTime * 2.2f) * profile.bobAmplitude);
            float units = ReferencePixelSize(floatingPoint);
            floatingPoint += (view.transform.right * profile.dockPixels.x + view.transform.up * profile.dockPixels.y) * (units * dockBlend);
        }
        float depth = Mathf.Max(1, Vector3.Dot(floatingPoint - view.transform.position, view.transform.forward));
        float pixelSize = 2 * depth * Mathf.Tan(view.fieldOfView * Mathf.Deg2Rad * .5f) / 1080f;
        float breath = 1 + Mathf.Sin(Time.unscaledTime * 2.2f) * .035f;
        float size = profile.corePixels * pixelSize * (bossGather ? Mathf.Max(.04f, gather) : 1 - gather * .06f);
        SetPose(core.transform, floatingPoint, view.transform.rotation, size);
        core.transform.localScale = new Vector3(size, size * profile.coreHeightPixels / profile.corePixels, 1);
        SetPose(halo.transform, floatingPoint + view.transform.forward * .006f, view.transform.rotation, profile.haloPixels * pixelSize * breath);
        Paint(core, 0, Color.white, appear * (bossGather ? gather : 1), 1 + gather * .08f);
        Paint(halo, 6, Color.white, .16f * appear * (bossGather ? gather : 1), 1);
        SetPose(sigil.transform, floor + Vector3.up * .035f, Quaternion.Euler(90, 0, 0), profile.sigilRadius * 2 * (1 + arrival * .7f));
        Paint(sigil, 4, Color.white, appear * (arrival > 0 ? (1 - arrival) * .85f : .35f), 1);
    }
    private float ReferencePixelSize(Vector3 point)
    {
        float depth = Mathf.Max(1, Vector3.Dot(point - view.transform.position, view.transform.forward));
        return 2 * depth * Mathf.Tan(view.fieldOfView * Mathf.Deg2Rad * .5f) / 1080f;
    }
    private void PositionIcons()
    {
        if (typeSymbols == null) return;
        for (int i = 0; i < sites.Length; i++)
        {
            Vector3 point = environment.TransformPoint(sites[i].point) + Vector3.up * profile.iconHeight;
            float focus = selected == sites[i].nodeId ? 1.1f : 1;
            SetPose(typeSymbols[i].transform, point, view.transform.rotation, ReferencePixelSize(point) * profile.iconPixels * focus);
        }
    }
    private static void SetPose(Transform target, Vector3 position, Quaternion rotation, float size)
    { target.SetPositionAndRotation(position, rotation); target.localScale = Vector3.one * size; }
    private void RefreshSymbols()
    {
        if (nodeSymbols == null || session?.Progress == null) return;
        for (int i = 0; i < sites.Length; i++)
        {
            var state = session.Progress.NodeState(sites[i].nodeId);
            bool chosen = selected == sites[i].nodeId;
            int glyph = state == MapLocationState.Completed ? 5 : state == MapLocationState.Skipped ? 7 : chosen ? 2 : 4;
            float alpha = state == MapLocationState.Available ? (chosen ? .85f : .52f) : state == MapLocationState.Completed ? .38f : .1f;
            var tint = state == MapLocationState.Completed ? new Color(.72f, .92f, .76f) : Color.white;
            Paint(nodeSymbols[i], glyph, tint, alpha, 1);
            if (typeSymbols != null)
            {
                var info = profile.nodeInformation != null ? profile.nodeInformation.Find(sites[i].nodeId) : null;
                var iconTint = state == MapLocationState.Available ? Color.white : state == MapLocationState.Completed ? new Color(.7f, .85f, .7f) : new Color(.6f, .62f, .61f);
                Paint(typeSymbols[i], info != null ? info.Glyph : 8, iconTint, state == MapLocationState.Skipped ? .27f : .95f, 1);
            }
        }
        UpdateRoutes();
    }
    private void UpdateRoutes()
    {
        if (routeSymbols == null || session?.Progress == null) return;
        for (int i = 0; i < paths.Length; i++)
        {
            var path = paths[i]; var renderer = routeSymbols[i];
            bool traveled = session.Progress.HasTraveled(path.EdgeKey);
            bool active = path == activePath;
            renderer.enabled = traveled || active;
            if (!renderer.enabled) continue;
            renderer.GetPropertyBlock(properties);
            properties.SetFloat(Opacity, active ? (moving ? .85f : .8f * previewBlend) : .16f);
            properties.SetFloat(Mode, active ? (moving ? 2 : 1) : 3);
            properties.SetFloat(Head, head); renderer.SetPropertyBlock(properties);
        }
    }
    private void Paint(Renderer renderer, int glyph, Color tint, float alpha, float emission)
    {
        properties.Clear(); properties.SetVector(Atlas, AtlasRect(glyph));
        properties.SetColor(Tint, tint * PaletteRatio(profile.amber, new Color(.906f, .714f, .416f)));
        properties.SetColor(CreamTint, tint * PaletteRatio(profile.cream, new Color(1, .902f, .682f)));
        properties.SetFloat(Opacity, alpha); properties.SetFloat(Emission, emission);
        properties.SetFloat(Soft, renderer == core || renderer == halo ? .06f : 0);
        renderer.SetPropertyBlock(properties);
    }
    private static Color PaletteRatio(Color value, Color basis)
    {
        var a = value.linear; var b = basis.linear;
        return new Color(a.r / b.r, a.g / b.g, a.b / b.b, 1);
    }
    public void EnsureVisuals()
    {
        if (profile == null || profile.quadMesh == null) throw new InvalidOperationException("Travel assets are not installed.");
        properties ??= new MaterialPropertyBlock();
        portalBefore ??= new MaterialPropertyBlock();
        portalPulse ??= new MaterialPropertyBlock();
        view = Camera.main;
        core = Quad("Core", transform); halo = Quad("Halo", transform); sigil = Quad("Sigil", transform);
        var nodeRoot = Child("Nodes", transform);
        var iconRoot = Child("Icons", transform);
        nodeSymbols = new MeshRenderer[sites.Length];
        typeSymbols = new MeshRenderer[sites.Length];
        for (int i = 0; i < sites.Length; i++)
        {
            nodeSymbols[i] = Quad($"N{i + 1:00}", nodeRoot);
            typeSymbols[i] = Quad($"I{i + 1:00}", iconRoot);
            SetPose(nodeSymbols[i].transform, environment.TransformPoint(sites[i].point) + Vector3.up * .028f,
                Quaternion.Euler(90, 0, 0), profile.sigilRadius * 2);
        }
        PositionIcons();
        routeSymbols = new MeshRenderer[paths.Length];
        for (int i = 0; i < paths.Length; i++)
        {
            paths[i].Prepare(); var child = paths[i].transform;
            var filter = GetOrAdd<MeshFilter>(child.gameObject);
            var renderer = GetOrAdd<MeshRenderer>(child.gameObject);
            filter.sharedMesh = RouteMesh(paths[i]); SetupRenderer(renderer); routeSymbols[i] = renderer;
            properties.Clear(); properties.SetVector(Atlas, AtlasRect(3)); properties.SetFloat(Repeats, Mathf.Max(1, paths[i].Length / .85f));
            properties.SetColor(Tint, PaletteRatio(profile.amber, new Color(.906f,.714f,.416f)));
            properties.SetColor(CreamTint, PaletteRatio(profile.cream, new Color(1,.902f,.682f)));
            properties.SetFloat(Soft, 0); properties.SetFloat(Emission, 1); renderer.SetPropertyBlock(properties); renderer.enabled = false;
        }
        var trail = Child("Trail", transform);
        var trailFilter = GetOrAdd<MeshFilter>(trail.gameObject);
        trailRenderer = GetOrAdd<MeshRenderer>(trail.gameObject);
        SetupRenderer(trailRenderer); InitParticles(); trailFilter.sharedMesh = particleMesh;
        properties.Clear(); properties.SetVector(Atlas, new Vector4(0, 0, 1, 1)); properties.SetFloat(Soft, .05f); properties.SetFloat(Emission, 1.1f);
        trailRenderer.SetPropertyBlock(properties);
    }
    private MeshRenderer Quad(string name, Transform parent)
    {
        var child = Child(name, parent); var filter = GetOrAdd<MeshFilter>(child.gameObject);
        var renderer = GetOrAdd<MeshRenderer>(child.gameObject);
        filter.sharedMesh = profile.quadMesh; SetupRenderer(renderer); return renderer;
    }
    private static Transform Child(string name, Transform parent)
    {
        var child = parent.Find(name);
        if (child == null) { child = new GameObject(name).transform; child.SetParent(parent, false); }
        return child;
    }
    private static T GetOrAdd<T>(GameObject target) where T : Component
    {
        var component = target.GetComponent<T>();
        return component != null ? component : target.AddComponent<T>();
    }
    private void SetupRenderer(MeshRenderer renderer)
    {
        renderer.sharedMaterial = profile.symbolMaterial; renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false; renderer.lightProbeUsage = LightProbeUsage.Off;
        renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
    }
    private Mesh RouteMesh(MapTravelPath path)
    {
        int count = path.points.Length; var positions = new Vector3[count * 2]; var texture = new Vector2[count * 2];
        var tint = new Color[count * 2]; var indices = new int[(count - 1) * 6]; float length = 0;
        Vector3 previous = path.Sample(0);
        for (int i = 0; i < count; i++)
        {
            Vector3 point = environment.TransformPoint(path.points[i]);
            Vector3 next = environment.TransformPoint(path.points[Mathf.Min(count - 1, i + 1)]);
            Vector3 direction = i == count - 1 ? point - previous : next - point;
            Vector3 side = Vector3.Cross(Vector3.up, direction).normalized * profile.routeWidth * .5f;
            if (i > 0) length += Vector3.Distance(previous, point);
            for (int k = 0; k < 2; k++)
            {
                positions[i * 2 + k] = path.transform.InverseTransformPoint(point + Vector3.up * .032f + side * (k == 0 ? -1 : 1));
                texture[i * 2 + k] = new Vector2(length / Mathf.Max(.01f, path.Length), k); tint[i * 2 + k] = Color.white;
            }
            if (i < count - 1)
            {
                int index = i * 6, a = i * 2;
                indices[index] = a; indices[index + 1] = a + 2; indices[index + 2] = a + 1;
                indices[index + 3] = a + 1; indices[index + 4] = a + 2; indices[index + 5] = a + 3;
            }
            previous = point;
        }
        var mesh = new Mesh { name = "Travel Route", vertices = positions, uv = texture, colors = tint, triangles = indices };
        mesh.RecalculateBounds(); ownedMeshes.Add(mesh); return mesh;
    }
    private void InitParticles()
    {
        int count = Mathf.Clamp(profile.maximumBurstParticles, 1, 96);
        particles = new Particle[count]; vertices = new Vector3[count * 4]; uv = new Vector2[count * 4]; colors = new Color[count * 4];
        int[] indices = new int[count * 6];
        for (int i = 0; i < count; i++)
        { int a = i * 4, b = i * 6; indices[b] = a; indices[b + 1] = a + 1; indices[b + 2] = a + 2; indices[b + 3] = a; indices[b + 4] = a + 2; indices[b + 5] = a + 3; }
        particleMesh = new Mesh { name = "Travel Motes", vertices = vertices, uv = uv, colors = colors, triangles = indices };
        particleMesh.MarkDynamic(); ownedMeshes.Add(particleMesh);
    }
    private float Random(float min, float max) => min + (float)random.NextDouble() * (max - min);
    private void EmitParticle(bool wake)
    {
        int limit = Mathf.Min(profile.maximumTrailParticles, particles.Length);
        for (int i = 0; i < limit; i++) if (particles[i].life <= 0)
        {
            particles[i] = new Particle { position = floatingPoint + new Vector3(Random(-.05f, .05f), Random(-.04f, .04f), Random(-.05f, .05f)),
                velocity = new Vector3(Random(-.14f, .14f), Random(.06f, .19f), Random(-.14f, .14f)),
                life = wake ? profile.trailLife : .8f, size = Random(.05f, .11f), rotation = Random(-180, 180), leaf = random.Next(3) != 0 };
            return;
        }
    }
    private void EmitBurst(int count)
    {
        for (int n = 0, i = 0; i < particles.Length && n < count; i++) if (particles[i].life <= 0)
        {
            float angle = Random(0, Mathf.PI * 2);
            particles[i] = new Particle { position = floatingPoint, velocity = new Vector3(Mathf.Cos(angle) * .45f, Random(.05f, .35f), Mathf.Sin(angle) * .45f),
                life = Random(.4f, .65f), size = Random(.05f, .12f), rotation = Random(-180, 180), leaf = true }; n++;
        }
    }
    private void CollectMotes()
    {
        if (particles == null) return;
        int live = 0;
        for (int i = 0; i < particles.Length; i++)
            if (particles[i].life > 0) { particles[i].returning = true; live++; }
        for (int i = 0, added = 0; i < particles.Length && live + added < profile.maximumTrailParticles && added < 7; i++)
        {
            if (particles[i].life > 0) continue;
            float angle = Random(0, Mathf.PI * 2);
            particles[i] = new Particle { position = floatingPoint + new Vector3(Mathf.Cos(angle), Random(-.2f,.2f), Mathf.Sin(angle)) * .38f,
                life = profile.trailLife, size = Random(.055f,.095f), rotation = Random(-180,180), leaf = true, returning = true };
            added++;
        }
    }
    private void UpdateParticles(float dt)
    {
        if (particles == null) return;
        LiveParticles = 0; Vector3 right = view.transform.right, up = view.transform.up;
        for (int i = 0; i < particles.Length; i++)
        {
            ref var p = ref particles[i]; int a = i * 4;
            if (p.life <= 0 || (p.age += dt) >= p.life)
            { p.life = 0; for (int k = 0; k < 4; k++) colors[a + k] = Color.clear; continue; }
            LiveParticles++;
            if (p.returning) p.position = Vector3.Lerp(p.position, floatingPoint, 1 - Mathf.Exp(-dt * 12));
            else p.position += p.velocity * dt;
            float angle = p.rotation * Mathf.Deg2Rad;
            Vector3 x = (right * Mathf.Cos(angle) + up * Mathf.Sin(angle)) * p.size * .5f;
            Vector3 y = (-right * Mathf.Sin(angle) + up * Mathf.Cos(angle)) * p.size * .5f;
            Vector4 rect = AtlasRect(p.leaf ? 1 : 2);
            float alpha = Mathf.Sin(p.age / p.life * Mathf.PI) * .85f;
            for (int k = 0; k < 4; k++)
            {
                bool r = k == 1 || k == 2, u = k >= 2;
                vertices[a + k] = trailRenderer.transform.InverseTransformPoint(p.position + x * (r ? 1 : -1) + y * (u ? 1 : -1));
                uv[a + k] = new Vector2(rect.x + (r ? .995f : .005f) * rect.z, rect.y + (u ? .995f : .005f) * rect.w);
                colors[a + k] = new Color(1, 1, 1, alpha);
            }
        }
        particleMesh.SetVertices(vertices); particleMesh.SetUVs(0, uv); particleMesh.SetColors(colors);
        particleMesh.RecalculateBounds(); trailRenderer.enabled = LiveParticles > 0;
    }
    private void PulsePortal(float strength)
    {
        if (portalRenderer == null || portalRenderer.sharedMaterial == null) return;
        portalRenderer.GetPropertyBlock(portalPulse);
        var material = portalRenderer.sharedMaterial;
        string property = material.HasProperty("_EmissionColor") ? "_EmissionColor" : material.HasProperty("_Color") ? "_Color" : "_BaseColor";
        if (material.HasProperty(property)) portalPulse.SetColor(property, material.GetColor(property) * strength);
        if (material.HasProperty("_Opacity")) portalPulse.SetFloat("_Opacity", Mathf.Clamp01(material.GetFloat("_Opacity") * strength));
        portalRenderer.SetPropertyBlock(portalPulse);
    }
    private void RestorePortal() { if (portalChanged && portalRenderer != null) portalRenderer.SetPropertyBlock(portalBefore); portalChanged = false; }
    private void OnDestroy()
    {
        if (session?.Progress != null) session.Progress.Changed -= RefreshSymbols;
        RestorePortal(); foreach (var mesh in ownedMeshes) if (mesh != null) Destroy(mesh);
    }
}
