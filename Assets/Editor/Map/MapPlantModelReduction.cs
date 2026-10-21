using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

// Explicit asset migration, never an importer: keep placements, share three plant meshes.
public static class MapPlantModelReduction
{
    private const string Folder = "Assets/Art/Map/Models/Plants/";
    private const string MapPath = "Assets/Scenes/Map.unity";
    private const string Output = "Library/MapModelReduction";
    private static readonly string[] Sources = { "Fern_02", "Fern_08", "Bush_13" };
    private static readonly string[] Names = { "Fern_A", "Fern_B", "Bush" };
    private static readonly Quaternion ToUnity = Quaternion.Euler(-90, 0, 0);

    private sealed class Shape
    {
        public Mesh Mesh;
        public string Path;
        public Vector3 Root;
        public Bounds Bounds;
        public bool IsFern;

        public Shape(string path)
        {
            Path = path;
            Mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            Require(Mesh != null && Mesh.isReadable && Mesh.blendShapeCount == 0 && Mesh.bindposes.Length == 0,
                "Expected a readable static plant mesh: " + path);
            IsFern = System.IO.Path.GetFileName(path).StartsWith("Fern", StringComparison.Ordinal);
            Require(Mesh.subMeshCount == (IsFern ? 2 : 3), "Unexpected plant material slots: " + path);
            Bounds = Mesh.bounds;
            var vertices = Mesh.vertices;
            var stems = Mesh.GetIndices(Mesh.subMeshCount - 1).Distinct().Select(i => vertices[i]).ToArray();
            float bottom = stems.Min(p => p.z);
            var basePoints = stems.Where(p => p.z <= bottom + .000001f).ToArray();
            Root = basePoints.Aggregate(Vector3.zero, (sum, p) => sum + p) / basePoints.Length;
            Root.z = bottom;
        }
    }

    private sealed class Placement
    {
        public MeshFilter Filter;
        public int Variant;
        public Vector3 Position, Scale, RootWorld;
        public Quaternion Rotation;
        public Material[] Materials;
        public Vector3 ExpectedSize;
    }

    [MenuItem("Tools/Map/Reduce Plant Models to Three")]
    public static void Apply()
    {
        Require(!EditorApplication.isPlayingOrWillChangePlaymode, "Stop Play Mode before changing model assets.");
        var active = SceneManager.GetActiveScene();
        var scene = SceneManager.GetSceneByPath(MapPath);
        bool wasLoaded = scene.IsValid() && scene.isLoaded;
        Directory.CreateDirectory(Output);
        try
        {
            if (!wasLoaded) scene = EditorSceneManager.OpenScene(MapPath, OpenSceneMode.Additive);
            var paths = Directory.GetFiles(Folder, "*.asset").Select(p => p.Replace('\\', '/')).OrderBy(p => p).ToArray();
            if (paths.Length == 3 && Names.All(name => paths.Contains(Folder + name + ".asset")))
            {
                VerifyReferences(scene);
                File.WriteAllText(Output + "/repeat.txt", "PASS: exactly three shared plant models; no changes required.\n");
                return;
            }
            Require(!scene.isDirty, "Save Map before reducing plant models.");
            Require(paths.Length == 76, "Unexpected plant library. Inspect it before applying this migration.");
            CheckConsumers(paths);
            var shapes = paths.Select(path => new Shape(path)).ToDictionary(shape => shape.Mesh);
            var prototypes = Sources.Select(name => shapes.Values.Single(shape => shape.Path == Folder + name + ".asset")).ToArray();
            var filters = Components<MeshFilter>(scene).ToArray();
            var selected = filters.Where(filter => filter.sharedMesh != null && shapes.ContainsKey(filter.sharedMesh)).ToArray();
            Require(selected.Length > 0, "No plant placements were found in Map.");
            var plans = selected.Select(filter => Prepare(filter, shapes[filter.sharedMesh], prototypes)).ToArray();
            var protectedTransforms = Components<Transform>(scene).Except(plans.Select(p => p.Filter.transform))
                .ToDictionary(t => t, t => Matrix4x4.TRS(t.localPosition, t.localRotation, t.localScale));
            var colliders = Components<Collider>(scene).ToDictionary(c => c, c => EditorJsonUtility.ToJson(c));
            var meshReferences = filters.Except(selected).ToDictionary(filter => filter, filter => filter.sharedMesh);
            int objectCount = Components<Transform>(scene).Count();
            long beforeBytes = paths.Sum(path => new FileInfo(path).Length);
            string backup = Output + "/Backup-" + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff");
            Directory.CreateDirectory(backup + "/Plants");
            File.Copy(MapPath, backup + "/Map.unity");
            foreach (string path in paths)
            {
                File.Copy(path, backup + "/Plants/" + Path.GetFileName(path));
                File.Copy(path + ".meta", backup + "/Plants/" + Path.GetFileName(path) + ".meta");
            }
            Capture(scene, Output + "/before.png");
            foreach (var shape in prototypes) Normalize(shape);
            foreach (var plan in plans)
            {
                var transform = plan.Filter.transform;
                Undo.RecordObjects(new Object[] { transform, plan.Filter }, "Share plant model");
                plan.Filter.sharedMesh = prototypes[plan.Variant].Mesh;
                transform.SetLocalPositionAndRotation(plan.Position, plan.Rotation);
                transform.localScale = plan.Scale;
                EditorUtility.SetDirty(plan.Filter);
                Require(Vector3.Distance(transform.position, plan.RootWorld) < .0001f, "Plant root moved: " + transform.name);
                Vector3 size = Vector3.Scale(plan.Filter.sharedMesh.bounds.size, Abs(transform.localScale));
                Require(Vector3.Distance(size, plan.ExpectedSize) < .0001f, "Plant size changed: " + transform.name);
                Require(plan.Filter.GetComponent<MeshRenderer>().sharedMaterials.SequenceEqual(plan.Materials),
                    "Plant materials changed: " + transform.name);
            }
            foreach (var pair in protectedTransforms)
                Require(Matrix4x4.TRS(pair.Key.localPosition, pair.Key.localRotation, pair.Key.localScale) == pair.Value,
                    "An unrelated transform changed: " + pair.Key.name);
            foreach (var pair in colliders)
                Require(EditorJsonUtility.ToJson(pair.Key) == pair.Value, "A collider changed: " + pair.Key.name);
            foreach (var pair in meshReferences)
                Require(pair.Key.sharedMesh == pair.Value, "An unrelated mesh changed: " + pair.Key.name);
            Require(Components<Transform>(scene).Count() == objectCount, "Scene object count changed.");
            for (int i = 0; i < prototypes.Length; i++)
            {
                prototypes[i].Mesh.name = Names[i];
                EditorUtility.SetDirty(prototypes[i].Mesh);
                AssetDatabase.SaveAssetIfDirty(prototypes[i].Mesh);
                Require(string.IsNullOrEmpty(AssetDatabase.MoveAsset(prototypes[i].Path, Folder + Names[i] + ".asset")),
                    "Could not rename a retained model.");
            }
            EditorSceneManager.MarkSceneDirty(scene);
            Require(EditorSceneManager.SaveScene(scene), "Could not save Map references. Old assets were not removed.");
            // Resolve saved dependencies before deleting anything, including references outside Map.
            var obsolete = paths.Except(prototypes.Select(shape => shape.Path)).ToArray();
            CheckConsumers(obsolete, false);
            foreach (string path in obsolete)
            {
                Require(path.StartsWith(Folder, StringComparison.Ordinal) && File.Exists(backup + "/Plants/" + Path.GetFileName(path)),
                    "Deletion requires an exact in-scope backup: " + path);
                Require(AssetDatabase.DeleteAsset(path), "Could not remove the obsolete model: " + path);
            }
            VerifyReferences(scene, plans.Length);
            Capture(scene, Output + "/after.png");
            var preview = EditorSceneManager.OpenPreviewScene(MapPath);
            try { VerifyReferences(preview, plans.Length); }
            finally { EditorSceneManager.ClosePreviewScene(preview); }
            long afterBytes = Names.Sum(name => new FileInfo(Folder + name + ".asset").Length);
            File.WriteAllText(Output + "/result.txt",
                "PASS: 76 plant meshes reduced to 3 shared meshes.\n" +
                "Retained placements: " + plans.Length + "; removed files: " + obsolete.Length + "\n" +
                string.Join("\n", Names.Select((name, i) => name + " instances: " + plans.Count(p => p.Variant == i))) + "\n" +
                "Serialized mesh bytes: " + beforeBytes + " -> " + afterBytes + "\n" +
                "Root positions, size, material slots, other objects and all colliders verified.\n" +
                "Saved scene reopened in preview and references verified.\nBackup: " + backup + "\n");
            Debug.Log("Plant models reduced to three shared variants. Backup: " + backup);
        }
        finally
        {
            if (active.IsValid() && active.isLoaded) SceneManager.SetActiveScene(active);
            if (!wasLoaded && scene.IsValid() && scene.isLoaded) EditorSceneManager.CloseScene(scene, true);
        }
    }

    private static Placement Prepare(MeshFilter filter, Shape source, Shape[] prototypes)
    {
        var t = filter.transform;
        var renderer = filter.GetComponent<MeshRenderer>();
        Require(renderer != null && renderer.additionalVertexStreams == null && t.childCount == 0 &&
            filter.GetComponents<Collider>().Length == 0, "Unexpected plant components: " + filter.name);
        Require(Mathf.Abs(t.localScale.x - t.localScale.y) < .001f && Mathf.Abs(t.localScale.x - t.localScale.z) < .001f && t.localScale.x > 0,
            "The baked plant needs a positive uniform import scale: " + filter.name);
        int variant = source.IsFern ? Enumerable.Range(0, 2).OrderBy(i => Mathf.Abs(Mathf.Log(
            source.Bounds.size.x / source.Bounds.size.y / (prototypes[i].Bounds.size.x / prototypes[i].Bounds.size.y)))).First() : 2;
        var prototype = prototypes[variant];
        Vector3 ratio = Divide(source.Bounds.size, prototype.Bounds.size);
        Vector3 offset = Vector3.Scale(prototype.Bounds.center - prototype.Root, ratio);
        Vector3 originalOffset = source.Bounds.center - source.Root;
        bool halfTurn = offset.x * originalOffset.x + offset.y * originalOffset.y < 0;
        Quaternion turn = halfTurn ? Quaternion.AngleAxis(180, Vector3.forward) : Quaternion.identity;
        return new Placement
        {
            Filter = filter, Variant = variant,
            RootWorld = t.TransformPoint(source.Root),
            Position = t.localPosition + t.localRotation * Vector3.Scale(t.localScale, source.Root),
            Rotation = t.localRotation * turn * Quaternion.Inverse(ToUnity),
            Scale = new Vector3(ratio.x, ratio.z, ratio.y) * (t.localScale.x / 100f),
            ExpectedSize = new Vector3(source.Bounds.size.x, source.Bounds.size.z, source.Bounds.size.y) * t.localScale.x,
            Materials = renderer.sharedMaterials
        };
    }

    private static void Normalize(Shape source)
    {
        var mesh = source.Mesh;
        mesh.vertices = mesh.vertices.Select(v => ToUnity * ((v - source.Root) * 100f)).ToArray();
        mesh.normals = mesh.normals.Select(n => ToUnity * n).ToArray();
        mesh.tangents = mesh.tangents.Select(t =>
        {
            Vector3 xyz = ToUnity * new Vector3(t.x, t.y, t.z);
            return new Vector4(xyz.x, xyz.y, xyz.z, t.w);
        }).ToArray();
        for (int i = 0; i < mesh.subMeshCount; i++) mesh.SetIndices(mesh.GetIndices(i), mesh.GetTopology(i), i, true);
        mesh.RecalculateBounds();
        EditorUtility.SetDirty(mesh);
    }

    private static void CheckConsumers(string[] paths, bool allowMap = true)
    {
        var candidates = new HashSet<string>(paths, StringComparer.Ordinal);
        foreach (string path in AssetDatabase.GetAllAssetPaths().Where(path => path.StartsWith("Assets/", StringComparison.Ordinal) &&
            (path.EndsWith(".unity") || path.EndsWith(".prefab") || path.EndsWith(".asset"))))
        {
            if (candidates.Contains(path) || (allowMap && path == MapPath)) continue;
            Require(!AssetDatabase.GetDependencies(path, true).Any(candidates.Contains),
                "Another asset still uses a model scheduled for replacement: " + path);
        }
    }

    private static void VerifyReferences(Scene scene, int expectedPlacements = -1)
    {
        var retained = Names.Select(name => AssetDatabase.LoadAssetAtPath<Mesh>(Folder + name + ".asset")).ToArray();
        Require(retained.All(mesh => mesh != null) && Directory.GetFiles(Folder, "*.asset").Length == 3,
            "The plant library must contain exactly three meshes.");
        var plants = Components<MeshFilter>(scene).Where(filter => retained.Contains(filter.sharedMesh)).ToArray();
        Require(plants.Length > 0 && (expectedPlacements < 0 || plants.Length == expectedPlacements) &&
            plants.Select(filter => filter.sharedMesh).Distinct().Count() == 3,
            "All independent plants must use the three retained meshes.");
        foreach (var plant in plants)
            Require(plant.GetComponent<MeshRenderer>().sharedMaterials.Length == plant.sharedMesh.subMeshCount,
                "Material slot mismatch: " + plant.name);
        Require(Components<MeshFilter>(scene).All(filter => filter.sharedMesh != null), "Missing scene mesh.");
    }

    private static void Capture(Scene scene, string path)
    {
        var camera = Components<Camera>(scene).FirstOrDefault(c => c.CompareTag("MainCamera"));
        if (camera == null) return;
        var previous = camera.targetTexture;
        var active = RenderTexture.active;
        float aspect = camera.aspect;
        var target = new RenderTexture(1920, 1080, 24, RenderTextureFormat.ARGB32);
        var image = new Texture2D(1920, 1080, TextureFormat.RGB24, false);
        try
        {
            camera.aspect = 1920f / 1080f;
            camera.targetTexture = target;
            var request = new RenderPipeline.StandardRequest { destination = target };
            RenderPipeline.SubmitRenderRequest(camera, request);
            RenderTexture.active = target;
            image.ReadPixels(new Rect(0, 0, 1920, 1080), 0, 0);
            image.Apply();
            File.WriteAllBytes(path, image.EncodeToPNG());
        }
        finally
        {
            camera.targetTexture = previous;
            camera.aspect = aspect;
            RenderTexture.active = active;
            Object.DestroyImmediate(image);
            target.Release();
            Object.DestroyImmediate(target);
        }
    }

    private static IEnumerable<T> Components<T>(Scene scene) where T : Component =>
        scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<T>(true));
    private static Vector3 Divide(Vector3 a, Vector3 b) => new Vector3(a.x / b.x, a.y / b.y, a.z / b.z);
    private static Vector3 Abs(Vector3 v) => new Vector3(Mathf.Abs(v.x), Mathf.Abs(v.y), Mathf.Abs(v.z));
    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
