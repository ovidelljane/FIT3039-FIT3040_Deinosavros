using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

[InitializeOnLoad]
public static class MapVisualUpgrade
{
    public const string Root = "Assets/Art/MapEnvironment";
    public const string ProfilePath = Root + "/MapVisualProfile.asset";
    public const string Output = "Library/MapVisualUpgrade";
    private const string Request = "Library/MapVisualUpgrade.request";
    private static double nextPoll;

    static MapVisualUpgrade() { EditorApplication.update += Poll; }
    private static void Poll()
    {
        if (EditorApplication.timeSinceStartup < nextPoll || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
        nextPoll = EditorApplication.timeSinceStartup + 1;
        if (!File.Exists(Request) || EditorApplication.isPlayingOrWillChangePlaymode) return;
        string command = File.ReadAllText(Request).Trim();
        File.Delete(Request);
        try
        {
            if (command == "inspect") Inspect();
            else if (command == "apply") Upgrade();
            else if (command == "capture") CaptureAll();
            else if (command == "play") MapVisualPlayVerification.Begin();
        }
        catch (Exception ex)
        {
            Directory.CreateDirectory(Output);
            File.WriteAllText(Output + "/error.txt", ex.ToString());
            Debug.LogException(ex);
        }
    }

    public static Scene RequireScene()
    {
        Scene scene = SceneManager.GetActiveScene();
        if (scene.path != "Assets/Scenes/Map.unity") throw new InvalidOperationException("Open the Map scene before applying its visual profile.");
        return scene;
    }

    public static MapVisualProfile LoadProfile()
    {
        MapVisualProfile profile = AssetDatabase.LoadAssetAtPath<MapVisualProfile>(ProfilePath);
        if (profile != null) return profile;
        profile = ScriptableObject.CreateInstance<MapVisualProfile>();
        AssetDatabase.CreateAsset(profile, ProfilePath);
        return profile;
    }

    public static Transform Environment => GameObject.Find("MapEnvironment")?.transform;

    public static void BatchUpgrade()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/Map.unity", OpenSceneMode.Single);
        Upgrade();
        MapVisualPlayVerification.ValidateSaved();
    }

    public static void BatchCapture()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/Map.unity", OpenSceneMode.Single);
        CaptureAll();
    }

    public static void BatchBaseline()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/Map.unity", OpenSceneMode.Single);
        Capture("01-before", 1920, 1080, false);
    }

    public static void Upgrade()
    {
        Scene scene = RequireScene();
        if (scene.isDirty) EditorSceneManager.SaveScene(scene);
        Directory.CreateDirectory(Output);
        Inspect();
        MapVisualProfile profile = LoadProfile();
        string before = Snapshot();
        MapSurfaceTextureGenerator.Generate();
        ApplySurfaces(profile);
        Capture("02-materials", 1920, 1080, false);
        ApplyFraming(profile);
        Capture("03-framing", 1920, 1080, true);
        ApplyLighting(profile);
        ApplyAtmosphere(profile);
        ApplyPostProcessing(profile);
        VerifyUnchanged(before);
        AssetDatabase.SaveAssets();
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        int count = scene.GetRootGameObjects().Sum(go => go.GetComponentsInChildren<Transform>(true).Length);
        Apply(profile);
        VerifyUnchanged(before);
        int repeatedCount = scene.GetRootGameObjects().Sum(go => go.GetComponentsInChildren<Transform>(true).Length);
        if (count != repeatedCount) throw new InvalidOperationException("Repeated visual application changed the object count.");
        EditorSceneManager.SaveScene(scene);
        CaptureAll();
        File.WriteAllText(Output + "/completed.txt", $"Visual upgrade completed at {DateTime.Now:O}. Object count: {count}. Repeated application: stable.\n");
    }

    public static void Apply(MapVisualProfile profile)
    {
        RequireScene();
        ApplySurfaces(profile);
        ApplyFraming(profile);
        ApplyLighting(profile);
        ApplyAtmosphere(profile);
        ApplyPostProcessing(profile);
        AssetDatabase.SaveAssets();
    }

    private static void Inspect()
    {
        Scene scene = RequireScene();
        Directory.CreateDirectory(Output);
        if (!File.Exists(Output + "/before-snapshot.json"))
        {
            File.WriteAllText(Output + "/before-snapshot.json", Snapshot());
            Directory.CreateDirectory(Output + "/Before");
            File.Copy(scene.path, Output + "/Before/Map-saved.unity", true);
            Capture("01-before", 1920, 1080, false);
        }
        var lines = new List<string>();
        foreach (Renderer r in SceneObjects<Renderer>())
        {
            if (!r.enabled || !r.gameObject.activeInHierarchy) continue;
            lines.Add($"{r.name} | center={r.bounds.center:F2} size={r.bounds.size:F2} | {string.Join(",", r.sharedMaterials.Select(m => m != null ? m.name : "MISSING"))}");
        }
        File.WriteAllLines(Output + "/renderers.txt", lines);
    }

    public static T[] SceneObjects<T>() where T : Component => RequireScene().GetRootGameObjects()
        .SelectMany(root => root.GetComponentsInChildren<T>(true)).ToArray();

    [Serializable] private sealed class SnapshotData { public string[] transforms; public string[] colliders; public string[] meshes; }
    private static string Key(Transform t) => t.parent == null ? t.name : Key(t.parent) + "/" + t.name;
    public static string Snapshot()
    {
        var data = new SnapshotData
        {
            transforms = SceneObjects<Transform>().Where(t => t is not RectTransform && t.GetComponent<Light>() == null
                && t.GetComponent<Camera>() == null && t.name != "Map Camera Clouds")
                .Select(t => $"{Key(t)}|{t.localPosition:R}|{t.localRotation:R}|{t.localScale:R}").OrderBy(s => s).ToArray(),
            colliders = SceneObjects<Collider>().Select(c => Key(c.transform) + "|" + EditorJsonUtility.ToJson(c)).OrderBy(s => s).ToArray(),
            meshes = SceneObjects<MeshFilter>().Where(m => m.name != "Map Camera Clouds")
                .Select(m => Key(m.transform) + "|" + (m.sharedMesh != null ? AssetDatabase.GetAssetPath(m.sharedMesh) + "/" + m.sharedMesh.name : "null"))
                .OrderBy(s => s).ToArray()
        };
        return JsonUtility.ToJson(data, true);
    }

    private static void VerifyUnchanged(string before)
    {
        if (before != Snapshot()) throw new InvalidOperationException("A protected transform, collider or mesh reference changed.");
    }

    private static void ApplySurfaces(MapVisualProfile profile)
    {
        Shader shader = AssetDatabase.LoadAssetAtPath<Shader>(Root + "/Shaders/MapSurfaceLit.shader");
        if (shader == null) throw new InvalidOperationException("Map surface shader is missing.");
        Directory.CreateDirectory(Root + "/Materials/Stylized");
        AssetDatabase.Refresh();
        var replacements = new Dictionary<Material, Material>();
        var stoneNames = new HashSet<string> { "Overhaul Path Stone", "Overhaul Worn Stone Edge", "Overhaul Weathered Stone" };
        var rockNames = new HashSet<string> { "Overhaul Basalt", "Overhaul Basalt Highlight", "Overhaul Portal Volcanic Stone", "Overhaul Portal Crevice" };
        var groundNames = new HashSet<string> { "Overhaul Damp Ground", "Overhaul Moss", "Finish_CushionMoss" };
        var leafNames = new HashSet<string> { "Finish_FernLeaf", "Finish_LeafTips", "Overhaul Broad Leaf", "Overhaul Broad Leaf Light", "Production Shrub Leaf Dark", "Production Shrub Leaf Mid", "Production Shrub Leaf Light" };
        foreach (Renderer renderer in Environment.GetComponentsInChildren<Renderer>(true))
        {
            Material[] materials = renderer.sharedMaterials;
            bool changed = false;
            for (int i = 0; i < materials.Length; i++)
            {
                Material source = materials[i];
                if (source == null) continue;
                string name = source.name.Replace(" [Map Warm]", "");
                if (!stoneNames.Contains(name) && !rockNames.Contains(name) && !groundNames.Contains(name) && !leafNames.Contains(name)) continue;
                if (!replacements.TryGetValue(source, out Material material))
                {
                    string path = Root + "/Materials/Stylized/" + name + ".mat";
                    material = AssetDatabase.LoadAssetAtPath<Material>(path);
                    if (material == null)
                    {
                        material = new Material(source) { name = name + " [Map Warm]" };
                        AssetDatabase.CreateAsset(material, path);
                    }
                    if (leafNames.Contains(name))
                    {
                        float shade = name.Contains("Light") || name.Contains("Tips") ? 0.9f : name.Contains("Dark") ? 0.0f : 0.48f;
                        material.SetColor("_BaseColor", Color.Lerp(profile.leafDark, profile.leafLight, shade));
                        material.SetFloat("_Smoothness", 0.16f);
                    }
                    else
                    {
                        material.shader = shader;
                        material.shaderKeywords = Array.Empty<string>();
                        bool ground = groundNames.Contains(name), stone = stoneNames.Contains(name);
                        string kind = ground ? "Earth" : stone ? "Sandstone" : "Rock";
                        Color color = ground ? profile.soil : stone ? profile.sandstone : profile.rock;
                        if (name.Contains("Moss")) color = profile.moss;
                        if (name.Contains("Worn") || name.Contains("Highlight")) color = Color.Lerp(color, Color.white, 0.13f);
                        if (name.Contains("Crevice")) color *= 0.72f;
                        color.a = 1;
                        material.SetColor("_BaseColor", color);
                        material.SetColor("_MossColor", profile.moss);
                        material.SetFloat("_Smoothness", ground ? profile.groundSmoothness : profile.stoneSmoothness);
                        material.SetFloat("_TileMeters", ground ? profile.groundTileMeters : stone ? profile.stoneTileMeters : profile.rockTileMeters);
                        material.SetFloat("_DetailStrength", profile.detailStrength);
                        material.SetFloat("_MossAmount", ground ? 0.90f : 0.08f);
                        material.SetFloat("_MacroVariation", ground ? 0.18f : 0.06f);
                        material.SetFloat("_Cull", rockNames.Contains(name) ? 0 : source.HasProperty("_Cull") ? source.GetFloat("_Cull") : 2);
                        material.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>($"{MapSurfaceTextureGenerator.Folder}/{kind}_Albedo.png"));
                        material.SetTexture("_NormalMap", AssetDatabase.LoadAssetAtPath<Texture2D>($"{MapSurfaceTextureGenerator.Folder}/{kind}_Normal.png"));
                        material.SetTexture("_MaskMap", AssetDatabase.LoadAssetAtPath<Texture2D>($"{MapSurfaceTextureGenerator.Folder}/{kind}_Mask.png"));
                        material.renderQueue = -1;
                    }
                    EditorUtility.SetDirty(material);
                    replacements[source] = material;
                }
                materials[i] = material;
                changed = true;
            }
            if (changed) { renderer.sharedMaterials = materials; EditorUtility.SetDirty(renderer); }
        }
    }

    private static void ApplyFraming(MapVisualProfile profile)
    {
        Camera camera = Camera.main;
        var subjects = Environment.GetComponentsInChildren<MeshRenderer>(true).Where(r => r.enabled && r.gameObject.activeInHierarchy
            && !Deinosavros.MapReview.MapReviewCamera.IsBackgroundRenderer(r, Environment.transform)
            && !r.name.Contains("Distant") && !r.name.Contains("haze") && r.name != "Chasm floor"
            && r.bounds.size.magnitude < 100).Cast<Renderer>().ToList();
        subjects.AddRange(SceneObjects<MapEncounterNode>().SelectMany(n => n.GetComponentsInChildren<MeshRenderer>(true))
            .Where(r => r.enabled && r.gameObject.activeInHierarchy));
        var framing = camera.GetComponent<MapCameraFraming>() ?? camera.gameObject.AddComponent<MapCameraFraming>();
        framing.Configure(profile, subjects.Distinct().ToArray(), GameObject.Find("CardBar")?.GetComponent<RectTransform>(), GameObject.Find("TopStatusBar")?.GetComponent<RectTransform>());
        Canvas.ForceUpdateCanvases();
        framing.Reframe(1920, 1080);
        EditorUtility.SetDirty(framing);
        EditorUtility.SetDirty(camera);
    }

    private static void ApplyLighting(MapVisualProfile p)
    {
        Light key = GameObject.Find("MapMoonKey")?.GetComponent<Light>();
        if (key == null) throw new InvalidOperationException("Map key light is missing.");
        key.color = p.sunlight;
        key.intensity = p.sunlightIntensity;
        key.shadows = LightShadows.Soft;
        key.shadowStrength = p.shadowStrength;
        key.transform.rotation = Quaternion.Euler(48, -35, 0);
        RenderSettings.sun = key;
        RenderSettings.ambientMode = AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = p.ambientSky;
        RenderSettings.ambientEquatorColor = p.ambientEquator;
        RenderSettings.ambientGroundColor = p.ambientGround;
        RenderSettings.ambientIntensity = 1;
        EditorUtility.SetDirty(key);
    }

    private static Material OwnMaterial(string original)
    {
        string path = Root + "/Materials/Stylized/" + original + ".mat";
        Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material == null)
        {
            material = new Material(AssetDatabase.LoadAssetAtPath<Material>(Root + "/Materials/" + original + ".mat"));
            material.name = original + " [Map Warm]";
            AssetDatabase.CreateAsset(material, path);
        }
        return material;
    }

    private static void ApplyAtmosphere(MapVisualProfile p)
    {
        Material sky = OwnMaterial("MapDuskSky");
        sky.SetColor("_TopColor", p.skyTop);
        sky.SetColor("_HorizonColor", p.skyHorizon);
        sky.SetColor("_BottomColor", p.skyBottom);
        sky.SetColor("_CloudColor", new Color(0.82f, 0.81f, 0.74f, 0.32f));
        sky.SetFloat("_CloudStrength", 0.4f);
        RenderSettings.skybox = sky;
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.Linear;
        RenderSettings.fogColor = p.skyHorizon;
        Camera.main.clearFlags = CameraClearFlags.Skybox;
        foreach (Renderer renderer in Environment.GetComponentsInChildren<Renderer>(true).Where(r => r.name.Contains("Distant")))
        {
            Material distant = OwnMaterial(renderer.name.Contains("Fossil") ? "MapDistantFossil" : "MapDistantSilhouette");
            distant.shader = AssetDatabase.LoadAssetAtPath<Shader>(Root + "/Shaders/MapMistSurface.shader");
            distant.shaderKeywords = Array.Empty<string>();
            distant.SetColor("_BaseColor", new Color(0.32f, 0.43f, 0.48f, 0.22f));
            distant.SetColor("_HighlightColor", new Color(0.48f, 0.56f, 0.58f, 1));
            distant.SetFloat("_Density", 0.7f);
            distant.SetFloat("_Coverage", 1);
            distant.SetFloat("_Ceiling", 1000);
            distant.SetFloat("_BaseHeight", -8.5f);
            distant.SetFloat("_BaseFadeDistance", 9);
            distant.renderQueue = 2985;
            renderer.sharedMaterial = distant;
            EditorUtility.SetDirty(distant);
        }
        foreach (var pair in new[] { ("Chasm floor", "MapChasmMist"), ("Low valley haze", "MapValleyHaze") })
        {
            Renderer renderer = Environment.GetComponentsInChildren<Renderer>(true).FirstOrDefault(r => r.name == pair.Item1);
            if (renderer == null) continue;
            Material mist = OwnMaterial(pair.Item2);
            bool low = pair.Item1 == "Chasm floor";
            mist.SetColor("_BaseColor", low ? new Color(0.37f, 0.46f, 0.49f, 0.8f) : new Color(0.69f, 0.74f, 0.71f, 0.4f));
            mist.SetColor("_HighlightColor", new Color(0.77f, 0.79f, 0.72f, 1));
            mist.SetFloat("_Density", low ? 0.8f : 0.32f);
            mist.SetFloat("_NoiseScale", low ? 0.022f : 0.037f);
            mist.SetFloat("_Ceiling", -2.5f);
            mist.SetFloat("_HeightFade", 2);
            mist.SetVector("_FlowA", new Vector4(0.004f, 0.0015f, 0, 0));
            mist.SetVector("_FlowB", new Vector4(-0.002f, 0.003f, 0, 0));
            renderer.sharedMaterial = mist;
            EditorUtility.SetDirty(mist);
        }
        MapCameraCloudOverlay clouds = MapCameraCloudOverlay.Ensure(Camera.main);
        clouds.Configure(p.cloudOpacity, p.cloudCenterClarity, p.cloudSpeed);
        EditorUtility.SetDirty(clouds);
        EditorUtility.SetDirty(sky);
    }

    private static void ApplyPostProcessing(MapVisualProfile p)
    {
        const string path = Root + "/MapVolumeProfile.asset";
        VolumeProfile profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(path);
        if (profile == null) { profile = ScriptableObject.CreateInstance<VolumeProfile>(); AssetDatabase.CreateAsset(profile, path); }
        profile.components.RemoveAll(c => c == null);
        Persist<Tonemapping>(profile).mode.Override(TonemappingMode.ACES);
        Bloom bloom = Persist<Bloom>(profile);
        bloom.threshold.Override(0.85f); bloom.intensity.Override(p.bloom); bloom.scatter.Override(0.65f);
        ColorAdjustments color = Persist<ColorAdjustments>(profile);
        color.postExposure.Override(p.exposure); color.contrast.Override(p.contrast); color.saturation.Override(p.saturation);
        color.colorFilter.Override(Color.white);
        WhiteBalance white = Persist<WhiteBalance>(profile);
        white.temperature.Override(p.temperature); white.tint.Override(0);
        Vignette vignette = Persist<Vignette>(profile);
        vignette.intensity.Override(p.vignette); vignette.smoothness.Override(0.45f);
        foreach (Volume volume in SceneObjects<Volume>().Where(v => v.name == "MapPostProcessing"))
        { volume.sharedProfile = profile; volume.enabled = true; EditorUtility.SetDirty(volume); }
        UniversalAdditionalCameraData data = Camera.main.GetUniversalAdditionalCameraData();
        data.renderPostProcessing = true;
        data.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
        data.antialiasingQuality = AntialiasingQuality.High;
        data.dithering = true; data.stopNaN = true; data.requiresDepthOption = CameraOverrideOption.On;
        Camera.main.allowHDR = true;
        foreach (VolumeComponent component in profile.components) EditorUtility.SetDirty(component);
        EditorUtility.SetDirty(profile); EditorUtility.SetDirty(data);
    }

    private static T Persist<T>(VolumeProfile profile) where T : VolumeComponent
    {
        if (!profile.TryGet(out T component)) component = profile.Add<T>(true);
        if (!AssetDatabase.Contains(component)) AssetDatabase.AddObjectToAsset(component, profile);
        component.active = true;
        return component;
    }

    public static void CaptureAll()
    {
        RequireScene();
        Capture("04-final-1080", 1920, 1080, true);
        Capture("05-final-1440", 2560, 1440, true);
        Capture("06-final-1200", 1920, 1200, true);
        Camera.main.GetComponent<MapCameraFraming>()?.Reframe();
    }

    public static void Capture(string name, int width, int height, bool reframe)
    {
        Directory.CreateDirectory(Output);
        Camera camera = Camera.main;
        RenderTexture previous = camera.targetTexture;
        RenderTexture active = RenderTexture.active;
        float aspect = camera.aspect;
        Vector3 position = camera.transform.position;
        Quaternion rotation = camera.transform.rotation;
        var canvas = SceneObjects<Canvas>().FirstOrDefault(c => c.isRootCanvas && c.renderMode == RenderMode.ScreenSpaceOverlay);
        var uiPositions = SceneObjects<RectTransform>().ToDictionary(rect => rect, rect => rect.anchoredPosition3D);
        RenderTexture target = new(width, height, 24, RenderTextureFormat.ARGB32);
        Texture2D texture = new(width, height, TextureFormat.RGBA32, false);
        bool asyncShaders = ShaderUtil.allowAsyncCompilation;
        try
        {
            ShaderUtil.allowAsyncCompilation = false;
            camera.targetTexture = target;
            camera.aspect = (float)width / height;
            if (reframe) camera.GetComponent<MapCameraFraming>()?.Reframe(width, height);
            camera.GetComponent<MapCameraCloudOverlay>()?.Refresh();
            if (canvas != null)
            {
                canvas.renderMode = RenderMode.ScreenSpaceCamera;
                canvas.worldCamera = camera;
                canvas.planeDistance = 0.5f;
                Canvas.ForceUpdateCanvases();
            }
            for (int pass = 0; pass < 2; pass++)
            {
                camera.GetComponent<MapCameraFraming>()?.BeginVisualRender();
                camera.Render();
                camera.GetComponent<MapCameraFraming>()?.EndVisualRender();
            }
            RenderTexture.active = target;
            texture.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            texture.Apply();
            File.WriteAllBytes(Output + "/" + name + ".png", texture.EncodeToPNG());
        }
        finally
        {
            camera.GetComponent<MapCameraFraming>()?.EndVisualRender();
            if (canvas != null) { canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.worldCamera = null; canvas.planeDistance = 100; Canvas.ForceUpdateCanvases(); }
            foreach (var pair in uiPositions) if (pair.Key != null) pair.Key.anchoredPosition3D = pair.Value;
            camera.targetTexture = previous; camera.aspect = aspect;
            camera.transform.SetPositionAndRotation(position, rotation);
            RenderTexture.active = active;
            Object.DestroyImmediate(texture); Object.DestroyImmediate(target);
            ShaderUtil.allowAsyncCompilation = asyncShaders;
        }
    }
}
