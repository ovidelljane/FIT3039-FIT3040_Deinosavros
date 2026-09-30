using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEditor.SceneManagement;
using UnityEditor.Build.Reporting;

[InitializeOnLoad]
public static class MapVisualPlayVerification
{
    public static void BatchAudit()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/Map.unity", OpenSceneMode.Single);
        ValidateSaved();
    }
    public static void BatchBuild()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/Map.unity", OpenSceneMode.Single);
        ValidateSaved();
        string folder = "Builds/MapVisualValidation";
        Directory.CreateDirectory(folder);
        BuildReport result = BuildPipeline.BuildPlayer(new BuildPlayerOptions
        {
            scenes = File.Exists("Assets/ValidationBaseline/Map.unity")
                ? new[] { "Assets/Scenes/Map.unity", "Assets/ValidationBaseline/Map.unity" }
                : new[] { "Assets/Scenes/Map.unity" },
            locationPathName = folder + "/MapValidation.exe",
            target = BuildTarget.StandaloneWindows64,
            options = BuildOptions.Development
        });
        if (result == null || result.summary.result != BuildResult.Succeeded) throw new InvalidOperationException("Map validation player build failed.");
    }

    public static void ValidateSaved()
    {
        Camera camera = Camera.main;
        MapCameraFraming framing = camera.GetComponent<MapCameraFraming>();
        framing.Reframe(1920, 1080);
        var report = new List<string>();
        var environment = MapVisualUpgrade.Environment;
        report.Add($"Environment independent meshes: {environment.GetComponentsInChildren<MeshFilter>(true).Length}");
        report.Add($"Camera: {camera.transform.position:F3}, rotation: {camera.transform.eulerAngles:F2}, safe area: {framing.SafeArea}");
        report.Add($"Fog: {RenderSettings.fogStartDistance:F2} to {RenderSettings.fogEndDistance:F2}");
        var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(MapVisualUpgrade.Root + "/MapVolumeProfile.asset");
        bool persisted = profile.components.Count == 5 && profile.components.All(c => c != null && AssetDatabase.Contains(c));
        report.Add($"Five persistent post-processing components after reopening: {persisted}");
        if (!persisted) throw new InvalidOperationException("Volume profile failed persistence validation.");
        foreach (Shader shader in environment.GetComponentsInChildren<Renderer>(true).SelectMany(r => r.sharedMaterials)
            .Where(m => m != null).Select(m => m.shader).Distinct())
        {
            if (shader == null) throw new InvalidOperationException("Missing material shader.");
            foreach (var message in ShaderUtil.GetShaderMessages(shader))
            {
                report.Add($"Shader {shader.name}: {message.severity}: {message.message}");
                if (message.severity.ToString() == "Error") throw new InvalidOperationException(message.message);
            }
        }
        foreach (Renderer r in environment.GetComponentsInChildren<Renderer>(true))
        {
            if (!r.name.Contains("island") && !r.name.Contains("Distant Spire 00") && !r.name.Contains("haze")) continue;
            report.Add($"{r.name}: shadow={r.shadowCastingMode}, viewport={camera.WorldToViewportPoint(r.bounds.center)}, layer={r.gameObject.layer}, active={r.gameObject.activeInHierarchy}");
            var filter = r.GetComponent<MeshFilter>();
            if (filter != null) report.Add($"Mesh {filter.sharedMesh.name}: {filter.sharedMesh.vertexCount} vertices, {filter.sharedMesh.subMeshCount} submeshes, readable={filter.sharedMesh.isReadable}");
        }
        foreach (MapEncounterNode node in MapVisualUpgrade.SceneObjects<MapEncounterNode>())
        {
            Collider collider = new SerializedObject(node).FindProperty("interactionCollider").objectReferenceValue as Collider;
            if (collider == null) throw new InvalidOperationException("A map node collider is missing.");
            if (collider is BoxCollider box)
            {
                Vector3 center = box.transform.TransformPoint(box.center);
                Vector3 viewport = camera.WorldToViewportPoint(center);
                bool hit = BoxGeometryHit(box, camera.ViewportPointToRay(viewport));
                report.Add($"{node.NodeId}: collider enabled={box.enabled}, hierarchy active={box.gameObject.activeInHierarchy}; box geometry ray hit={hit}; viewport={viewport:F3}; safe={framing.SafeArea.Contains(viewport)}");
                if (node.Level == 6)
                {
                    Renderer portal = environment.GetComponentsInChildren<Renderer>(true).First(r => r.name == "Overhaul Portal Integrated Stone Body");
                    bool aligned = BoxGeometryHit(box, camera.ViewportPointToRay(camera.WorldToViewportPoint(portal.bounds.center)));
                    report.Add($"Boss collider geometry aligned with portal center: {aligned}");
                }
            }
        }
        File.WriteAllLines(MapVisualUpgrade.Output + "/saved-verification.txt", report);
        File.WriteAllText(MapVisualUpgrade.Output + "/after-snapshot.json", MapVisualUpgrade.Snapshot());
    }

    private static bool BoxGeometryHit(BoxCollider box, Ray worldRay)
    {
        Ray localRay = new(box.transform.InverseTransformPoint(worldRay.origin), box.transform.InverseTransformVector(worldRay.direction));
        return new Bounds(box.center, box.size).IntersectRay(localRay);
    }

    private static readonly List<float> frameTimes = new();
    private static readonly List<string> errors = new();
    private static double started;
    private static int stage;
    static MapVisualPlayVerification()
    {
        EditorApplication.playModeStateChanged += ModeChanged;
        if (SessionState.GetBool("MapVisualVerify", false) && EditorApplication.isPlaying) StartSampling();
    }

    public static void Begin()
    {
        MapVisualUpgrade.RequireScene();
        SessionState.SetBool("MapVisualVerify", true);
        EditorApplication.isPlaying = true;
    }

    private static void ModeChanged(PlayModeStateChange change)
    {
        if (!SessionState.GetBool("MapVisualVerify", false)) return;
        if (change == PlayModeStateChange.EnteredPlayMode) StartSampling();
        if (change == PlayModeStateChange.EnteredEditMode)
        {
            SessionState.SetBool("MapVisualVerify", false);
            EditorApplication.update -= Sample;
            Application.logMessageReceived -= Log;
        }
    }

    private static void StartSampling()
    {
        started = EditorApplication.timeSinceStartup;
        stage = 0;
        frameTimes.Clear(); errors.Clear();
        EditorApplication.update -= Sample;
        EditorApplication.update += Sample;
        Application.logMessageReceived -= Log;
        Application.logMessageReceived += Log;
    }

    private static void Log(string message, string stack, LogType type)
    {
        if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) errors.Add(message);
    }

    private static void Sample()
    {
        if (!EditorApplication.isPlaying) return;
        double elapsed = EditorApplication.timeSinceStartup - started;
        if (elapsed > 3 && elapsed < 18) frameTimes.Add(Time.unscaledDeltaTime * 1000);
        if (elapsed > 5 && stage == 0)
        {
            stage = 1;
            MapVisualUpgrade.Capture("07-play-ui", 1920, 1080, true);
        }
        if (elapsed < 20 || stage == 2) return;
        stage = 2;
        try
        {
            Camera camera = Camera.main;
            var nodes = UnityEngine.Object.FindObjectsByType<MapEncounterNode>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            var report = new List<string>
            {
                "Editor play-mode verification (not standalone player profiling)",
                $"Node count: {nodes.Length}",
                $"Camera pixels: {camera.pixelWidth} x {camera.pixelHeight}",
                $"Frame samples: {frameTimes.Count}",
                $"Mean frame ms: {(frameTimes.Count > 0 ? frameTimes.Average() : 0):F2}",
                $"Runtime errors: {errors.Count}"
            };
            foreach (var node in nodes)
            {
                SerializedObject serialized = new(node);
                Collider collider = serialized.FindProperty("interactionCollider").objectReferenceValue as Collider;
                Vector3 screen = collider != null ? camera.WorldToScreenPoint(collider.bounds.center) : Vector3.zero;
                bool hit = collider != null && collider.Raycast(camera.ScreenPointToRay(screen), out _, camera.farClipPlane);
                report.Add($"{node.NodeId}: collider ray hit={hit}; screen={screen:F1}");
            }
            report.AddRange(errors);
            File.WriteAllLines(MapVisualUpgrade.Output + "/play-verification.txt", report);
        }
        catch (Exception ex) { File.WriteAllText(MapVisualUpgrade.Output + "/play-error.txt", ex.ToString()); }
        finally { EditorApplication.isPlaying = false; }
    }
}
