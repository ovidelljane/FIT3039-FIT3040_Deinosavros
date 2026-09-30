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
public static class MapEnvironmentIntegration
{
    private const int IntegrationRevision = 20;
    private const string ScenePath = "Assets/Scenes/Map.unity";
    private const string AssetRoot = "Assets/Art/MapEnvironment";
    private const string ModelPath = AssetRoot + "/MapEnvironment.fbx";
    private const string ManifestPath = AssetRoot + "/MapEnvironmentManifest.json";
    private const string MaterialFolder = AssetRoot + "/Materials";
    private const string ShaderFolder = AssetRoot + "/Shaders";
    private const string SkyShaderPath = ShaderFolder + "/MapDuskSky.shader";
    private const string MistShaderPath = ShaderFolder + "/MapMistSurface.shader";
    private const string VolumeProfilePath = AssetRoot + "/MapVolumeProfile.asset";
    private const string PendingFlag = "Temp/RunMapEnvironmentIntegration.flag";
    private const string ReportPath = "Library/MapEnvironmentIntegrationReport.json";
    private const string PreviewPath = AssetRoot + "/MapEnvironmentUnityPreview.png";

    private static readonly Dictionary<string, string> TexturePaths = new()
    {
        { "generated_dark_wood", AssetRoot + "/Textures/dark_wood_albedo.png" },
        { "generated_flagstone", AssetRoot + "/Textures/flagstone_albedo.png" },
        { "generated_palm_bark", AssetRoot + "/Textures/palm_bark_albedo.png" },
        { "generated_weathered_rock", AssetRoot + "/Textures/weathered_rock_albedo.png" }
    };

    static MapEnvironmentIntegration()
    {
        EditorApplication.delayCall += TryRunPendingIntegration;
    }

    [MenuItem("Tools/Map/Integrate Blender Environment")]
    public static void IntegrateFromMenu()
    {
        Integrate();
    }

    [MenuItem("Tools/Map/Apply Visual Upgrade Only")]
    public static void ApplyVisualsOnly()
    {
        MapVisualUpgrade.Upgrade();
    }

    private static void TryRunPendingIntegration()
    {
        if (EditorApplication.isCompiling || EditorApplication.isUpdating)
        {
            EditorApplication.delayCall += TryRunPendingIntegration;
            return;
        }

        string flagPath = Path.GetFullPath(PendingFlag);
        if (!File.Exists(flagPath))
        {
            return;
        }

        File.Delete(flagPath);
        Integrate();
    }

    private static void Integrate()
    {
        try
        {
            ConfigureTextureImporters();
            ConfigureModelImporter();
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);

            MapEnvironmentManifest manifest = LoadManifest();
            Dictionary<string, Material> materials = CreateMaterials(manifest.materials);
            Scene scene = OpenMapScene();
            MapEncounterNode[] nodes = Object.FindObjectsByType<MapEncounterNode>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            if (nodes.Length != 11)
            {
                throw new InvalidOperationException($"Expected 11 map nodes but found {nodes.Length}.");
            }

            GameObject mapRoot = GameObject.Find("MapRoot");
            if (mapRoot == null)
            {
                throw new InvalidOperationException("MapRoot was not found.");
            }

            RemovePreviousEnvironment(mapRoot.transform, nodes);
            GameObject environment = InstantiateEnvironment(scene, mapRoot.transform, materials);
            AlignNodesToModel(environment.transform, nodes);
            ConfigureBossPortalInteraction(nodes);
            ConfigureAtmosphere(environment.transform);
            ConfigureLighting(environment.transform);
            Camera camera = ConfigureCamera(environment.transform);
            ConfigurePostProcessing(environment.transform, camera);
            MapSurfaceTextureGenerator.Generate();
            MapVisualUpgrade.Apply(MapVisualUpgrade.LoadProfile());

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, ScenePath);
            CapturePreview(camera);
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);

            IntegrationReport report = ValidateIntegration(environment, nodes, materials, camera);
            File.WriteAllText(Path.GetFullPath(ReportPath), JsonUtility.ToJson(report, true));
            Debug.Log($"Map environment integration revision {IntegrationRevision} completed. Nodes: {report.nodeCount}, renderers: {report.rendererCount}, materials: {report.materialCount}.");
        }
        catch (Exception exception)
        {
            File.WriteAllText(Path.GetFullPath(ReportPath), JsonUtility.ToJson(new IntegrationReport
            {
                success = false,
                message = exception.ToString()
            }, true));
            Debug.LogException(exception);
        }
    }

    private static void ConfigureTextureImporters()
    {
        foreach (string path in TexturePaths.Values)
        {
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            if (AssetImporter.GetAtPath(path) is not TextureImporter importer)
            {
                continue;
            }

            importer.textureType = TextureImporterType.Default;
            importer.sRGBTexture = true;
            importer.wrapMode = TextureWrapMode.Repeat;
            importer.filterMode = FilterMode.Trilinear;
            importer.mipmapEnabled = true;
            importer.maxTextureSize = 2048;
            importer.textureCompression = TextureImporterCompression.CompressedHQ;
            importer.SaveAndReimport();
        }
    }

    private static void ConfigureModelImporter()
    {
        AssetDatabase.ImportAsset(ModelPath, ImportAssetOptions.ForceSynchronousImport);
        if (AssetImporter.GetAtPath(ModelPath) is not ModelImporter importer)
        {
            throw new InvalidOperationException("Map environment model importer was not found.");
        }

        importer.globalScale = 1.0f;
        importer.useFileScale = true;
        importer.importCameras = false;
        importer.importLights = false;
        importer.importAnimation = false;
        importer.importBlendShapes = false;
        importer.importVisibility = false;
        importer.preserveHierarchy = true;
        importer.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
        importer.materialLocation = ModelImporterMaterialLocation.InPrefab;
        importer.isReadable = false;
        importer.SaveAndReimport();
    }

    private static MapEnvironmentManifest LoadManifest()
    {
        string fullPath = Path.GetFullPath(ManifestPath);
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException("Map environment manifest was not found.", fullPath);
        }

        MapEnvironmentManifest manifest = JsonUtility.FromJson<MapEnvironmentManifest>(File.ReadAllText(fullPath));
        if (manifest == null || manifest.materials == null || manifest.materials.Length == 0)
        {
            throw new InvalidOperationException("Map environment manifest contains no materials.");
        }
        return manifest;
    }

    private static Dictionary<string, Material> CreateMaterials(MaterialRecord[] records)
    {
        EnsureFolder(MaterialFolder);
        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null)
        {
            throw new InvalidOperationException("URP Lit shader was not found.");
        }

        Dictionary<string, Material> materials = new(StringComparer.Ordinal);
        foreach (MaterialRecord record in records)
        {
            string path = $"{MaterialFolder}/{SanitizeFileName(record.name)}.mat";
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(shader);
                AssetDatabase.CreateAsset(material, path);
            }
            else
            {
                materials[record.name] = material;
                continue;
            }

            Color baseColor = ResolveBaseColor(record);
            if (!string.IsNullOrEmpty(record.textureId) && TexturePaths.TryGetValue(record.textureId, out string texturePath))
            {
                Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath);
                material.SetTexture("_BaseMap", texture);
                baseColor = Color.white;
            }
            else
            {
                material.SetTexture("_BaseMap", null);
            }

            float alpha = record.transparent ? Mathf.Clamp(record.alpha, 0.08f, 0.72f) : 1.0f;
            baseColor.a = alpha;
            material.SetColor("_BaseColor", baseColor);
            material.SetFloat("_Metallic", Mathf.Clamp01(record.metallic));
            material.SetFloat("_Smoothness", Mathf.Clamp01(1.0f - record.roughness));

            Color emission = ToColor(record.emission, Color.black);
            bool namedEmitter = ContainsAny(record.name, "Flame", "Glow", "Ember", "Portal Core", "Portal Membrane");
            float emissionStrength = namedEmitter ? Mathf.Max(2.5f, record.emissionStrength) : record.emissionStrength;
            if (namedEmitter || emissionStrength > 0.01f)
            {
                if (emission.maxColorComponent < 0.01f)
                {
                    emission = new Color(1.0f, 0.12f, 0.025f, 1.0f);
                }
                material.EnableKeyword("_EMISSION");
                material.SetColor("_EmissionColor", emission * emissionStrength);
                material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            }
            else
            {
                material.DisableKeyword("_EMISSION");
                material.SetColor("_EmissionColor", Color.black);
            }

            ConfigureSurface(material, record.transparent);
            EditorUtility.SetDirty(material);
            materials[record.name] = material;
        }

        AssetDatabase.SaveAssets();
        return materials;
    }

    private static void ConfigureSurface(Material material, bool transparent)
    {
        if (transparent)
        {
            material.SetFloat("_Surface", 1.0f);
            material.SetFloat("_Blend", 0.0f);
            material.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            material.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            material.SetFloat("_ZWrite", 0.0f);
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
        }
        else
        {
            material.SetFloat("_Surface", 0.0f);
            material.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.One);
            material.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.Zero);
            material.SetFloat("_ZWrite", 1.0f);
            material.DisableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.renderQueue = -1;
        }
    }

    private static Scene OpenMapScene()
    {
        Scene activeScene = SceneManager.GetActiveScene();
        if (activeScene.path == ScenePath)
        {
            return activeScene;
        }
        return EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
    }

    private static void RemovePreviousEnvironment(Transform mapRoot, MapEncounterNode[] nodes)
    {
        HashSet<Transform> nodeRoots = nodes.Select(node => node.transform).ToHashSet();
        foreach (Renderer renderer in mapRoot.GetComponentsInChildren<Renderer>(true))
        {
            if (!IsRelatedToAnyNode(renderer.transform, nodeRoots))
            {
                Object.DestroyImmediate(renderer.gameObject);
            }
        }

        foreach (string name in new[] { "MapEnvironment", "MapRoute", "BossPlatform", "BossStairs" })
        {
            GameObject oldRoot = GameObject.Find(name);
            if (oldRoot != null && !IsRelatedToAnyNode(oldRoot.transform, nodeRoots))
            {
                Object.DestroyImmediate(oldRoot);
            }
        }
    }

    private static bool IsRelatedToAnyNode(Transform candidate, HashSet<Transform> nodeRoots)
    {
        return nodeRoots.Any(nodeRoot => candidate == nodeRoot || candidate.IsChildOf(nodeRoot) || nodeRoot.IsChildOf(candidate));
    }

    private static GameObject InstantiateEnvironment(Scene scene, Transform parent, Dictionary<string, Material> materials)
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
        if (prefab == null)
        {
            throw new InvalidOperationException("Imported map environment prefab was not found.");
        }

        GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
        instance.name = "MapEnvironment";
        instance.transform.SetParent(parent, false);
        instance.transform.localPosition = Vector3.zero;
        instance.transform.localRotation = Quaternion.Euler(0.0f, 180.0f, 0.0f);
        instance.transform.localScale = Vector3.one;
        PrefabUtility.UnpackPrefabInstance(instance, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);

        foreach (Renderer renderer in instance.GetComponentsInChildren<Renderer>(true))
        {
            Material[] remapped = renderer.sharedMaterials;
            for (int index = 0; index < remapped.Length; index++)
            {
                Material source = remapped[index];
                if (source != null && materials.TryGetValue(source.name, out Material replacement))
                {
                    remapped[index] = replacement;
                }
            }
            renderer.sharedMaterials = remapped;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            renderer.receiveShadows = true;
        }
        return instance;
    }

    private static void AlignNodesToModel(Transform environment, MapEncounterNode[] nodes)
    {
        Dictionary<string, MapEncounterNode> nodesByName = nodes.ToDictionary(node => node.gameObject.name, StringComparer.Ordinal);
        foreach (KeyValuePair<string, MapEncounterNode> pair in nodesByName)
        {
            Transform anchor = FindDeepChild(environment, "ANCHOR_" + pair.Key);
            if (anchor == null)
            {
                throw new InvalidOperationException($"Missing model anchor for {pair.Key}.");
            }
            pair.Value.transform.position = anchor.position;
            pair.Value.transform.rotation = Quaternion.identity;
            pair.Value.transform.localScale = Vector3.one;
        }
    }

    private static void ConfigureBossPortalInteraction(MapEncounterNode[] nodes)
    {
        MapEncounterNode bossNode = nodes.FirstOrDefault(node => node.gameObject.name == "Node_L06_01");
        if (bossNode == null)
        {
            throw new InvalidOperationException("Boss map node was not found.");
        }

        foreach (MeshRenderer renderer in bossNode.GetComponentsInChildren<MeshRenderer>(true))
        {
            renderer.enabled = false;
        }

        Transform existing = bossNode.transform.Find("BossPortalHitArea");
        GameObject hitArea = existing != null ? existing.gameObject : new GameObject("BossPortalHitArea");
        hitArea.transform.SetParent(bossNode.transform, false);
        hitArea.transform.localPosition = new Vector3(0.0f, 1.8f, 0.0f);
        hitArea.transform.localRotation = Quaternion.identity;
        hitArea.transform.localScale = Vector3.one;
        BoxCollider collider = hitArea.GetComponent<BoxCollider>();
        if (collider == null)
        {
            collider = hitArea.AddComponent<BoxCollider>();
        }
        collider.center = Vector3.zero;
        collider.size = new Vector3(1.4f, 4.6f, 4.6f);

        SerializedObject serializedNode = new(bossNode);
        serializedNode.FindProperty("interactionCollider").objectReferenceValue = collider;
        serializedNode.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(bossNode);
    }

    private static void ConfigureAtmosphere(Transform environment)
    {
        Shader skyShader = AssetDatabase.LoadAssetAtPath<Shader>(SkyShaderPath);
        Shader mistShader = AssetDatabase.LoadAssetAtPath<Shader>(MistShaderPath);
        if (skyShader == null || mistShader == null)
        {
            throw new InvalidOperationException("Map atmosphere shaders were not found.");
        }

        Material skyMaterial = LoadOrCreateMaterial(MaterialFolder + "/MapDuskSky.mat", skyShader);
        skyMaterial.SetColor("_TopColor", new Color(0.035f, 0.07f, 0.12f, 1.0f));
        skyMaterial.SetColor("_HorizonColor", new Color(0.22f, 0.28f, 0.35f, 1.0f));
        skyMaterial.SetColor("_BottomColor", new Color(0.05f, 0.075f, 0.11f, 1.0f));
        skyMaterial.SetColor("_CloudColor", new Color(0.44f, 0.49f, 0.56f, 0.62f));
        skyMaterial.SetFloat("_HorizonSharpness", 6.0f);
        skyMaterial.SetFloat("_CloudScale", 1.7f);
        skyMaterial.SetFloat("_CloudStrength", 0.78f);
        skyMaterial.SetVector("_CloudSpeed", new Vector4(0.0025f, -0.0015f, 0.0f, 0.0f));

        Material chasmMaterial = LoadOrCreateMaterial(MaterialFolder + "/MapChasmMist.mat", mistShader);
        ConfigureMistMaterial(
            chasmMaterial,
            new Color(0.05f, 0.075f, 0.10f, 0.92f),
            new Color(0.16f, 0.20f, 0.25f, 1.0f),
            0.95f,
            0.90f,
            0.022f,
            0.20f,
            3.5f,
            new Vector4(0.004f, 0.0015f, 0.0f, 0.0f),
            new Vector4(-0.002f, 0.003f, 0.0f, 0.0f),
            2990);

        Material hazeMaterial = LoadOrCreateMaterial(MaterialFolder + "/MapValleyHaze.mat", mistShader);
        ConfigureMistMaterial(
            hazeMaterial,
            new Color(0.22f, 0.27f, 0.32f, 0.55f),
            new Color(0.40f, 0.44f, 0.50f, 1.0f),
            0.62f,
            0.70f,
            0.037f,
            0.24f,
            2.5f,
            new Vector4(0.006f, 0.002f, 0.0f, 0.0f),
            new Vector4(-0.003f, 0.005f, 0.0f, 0.0f),
            3010);

        Transform chasm = FindDeepChild(environment, "Chasm floor");
        Transform haze = FindDeepChild(environment, "Low valley haze");
        if (chasm == null || haze == null)
        {
            throw new InvalidOperationException("Map atmosphere meshes were not found.");
        }
        ConfigureMistRenderer(chasm.GetComponent<Renderer>(), chasmMaterial, -2);
        ConfigureMistRenderer(haze.GetComponent<Renderer>(), hazeMaterial, 2);

        Material silhouetteMaterial = LoadOrCreateMaterial(MaterialFolder + "/MapDistantSilhouette.mat", mistShader);
        ConfigureMistMaterial(
            silhouetteMaterial,
            new Color(0.09f, 0.14f, 0.20f, 0.74f),
            new Color(0.23f, 0.29f, 0.35f, 1.0f),
            0.58f,
            0.80f,
            0.045f,
            0.22f,
            4.0f,
            new Vector4(0.0015f, 0.0005f, 0.0f, 0.0f),
            new Vector4(-0.001f, 0.002f, 0.0f, 0.0f),
            2985);
        Material fossilMaterial = LoadOrCreateMaterial(MaterialFolder + "/MapDistantFossil.mat", mistShader);
        ConfigureMistMaterial(
            fossilMaterial,
            new Color(0.16f, 0.19f, 0.20f, 0.60f),
            new Color(0.30f, 0.32f, 0.31f, 1.0f),
            0.44f,
            0.76f,
            0.060f,
            0.24f,
            4.0f,
            new Vector4(0.001f, 0.0015f, 0.0f, 0.0f),
            new Vector4(-0.0015f, 0.001f, 0.0f, 0.0f),
            2986);
        ArrangeDistantBackdrop(environment, silhouetteMaterial, fossilMaterial);

        RenderSettings.skybox = skyMaterial;
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.Linear;
        RenderSettings.fogColor = new Color(0.11f, 0.15f, 0.20f, 1.0f);
        RenderSettings.fogStartDistance = 38.0f;
        RenderSettings.fogEndDistance = 115.0f;
        DynamicGI.UpdateEnvironment();

        EditorUtility.SetDirty(skyMaterial);
        EditorUtility.SetDirty(chasmMaterial);
        EditorUtility.SetDirty(hazeMaterial);
        EditorUtility.SetDirty(silhouetteMaterial);
        EditorUtility.SetDirty(fossilMaterial);
        AssetDatabase.SaveAssets();
    }

    private static void ConfigureMistMaterial(
        Material material,
        Color baseColor,
        Color highlightColor,
        float density,
        float coverage,
        float noiseScale,
        float edgeSoftness,
        float softDepth,
        Vector4 flowA,
        Vector4 flowB,
        int renderQueue)
    {
        material.SetColor("_BaseColor", baseColor);
        material.SetColor("_HighlightColor", highlightColor);
        material.SetFloat("_Density", density);
        material.SetFloat("_Coverage", coverage);
        material.SetFloat("_NoiseScale", noiseScale);
        material.SetFloat("_EdgeSoftness", edgeSoftness);
        material.SetFloat("_SoftDepth", softDepth);
        material.SetVector("_FlowA", flowA);
        material.SetVector("_FlowB", flowB);
        material.renderQueue = renderQueue;
    }

    private static void ConfigureMistRenderer(Renderer renderer, Material material, int sortingOrder)
    {
        if (renderer == null)
        {
            throw new InvalidOperationException("A map atmosphere renderer was not found.");
        }
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        renderer.lightProbeUsage = LightProbeUsage.Off;
        renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
        renderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
        renderer.sortingOrder = sortingOrder;
    }

    private static void ArrangeDistantBackdrop(
        Transform environment,
        Material silhouetteMaterial,
        Material fossilMaterial)
    {
        Renderer[] spires = environment.GetComponentsInChildren<Renderer>(true)
            .Where(renderer => renderer.gameObject.name.StartsWith("Overhaul Distant Spire", StringComparison.Ordinal))
            .OrderBy(renderer => ParseTrailingIndex(renderer.gameObject.name))
            .ToArray();
        if (spires.Length != 14)
        {
            throw new InvalidOperationException($"Expected 14 distant spires but found {spires.Length}.");
        }

        float[] targetX = { -33.0f, -30.0f, -26.5f, -20.0f, -17.0f, -13.5f, -10.5f, 9.0f, 12.0f, 15.0f, 22.5f, 26.0f, 29.5f, 33.0f };
        float[] targetZ = { 22.0f, 25.0f, 28.0f, 23.0f, 26.0f, 29.0f, 25.0f, 27.0f, 23.0f, 29.0f, 22.0f, 26.0f, 24.0f, 21.0f };
        float[] targetHeight = { 14.0f, 18.0f, 16.0f, 15.0f, 20.0f, 17.0f, 19.0f, 16.0f, 20.0f, 15.0f, 18.0f, 14.5f, 19.5f, 15.5f };
        for (int index = 0; index < spires.Length; index++)
        {
            PlaceRenderer(spires[index], targetX[index], targetZ[index], -8.5f, targetHeight[index]);
            spires[index].sharedMaterial = silhouetteMaterial;
            spires[index].shadowCastingMode = ShadowCastingMode.Off;
            spires[index].receiveShadows = false;
            spires[index].motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
            spires[index].sortingOrder = -10;
        }

        Transform fossilTransform = FindDeepChild(environment, "Overhaul Distant Fossil Ribs");
        Renderer fossil = fossilTransform != null ? fossilTransform.GetComponent<Renderer>() : null;
        if (fossil == null)
        {
            throw new InvalidOperationException("The distant fossil renderer was not found.");
        }
        PlaceRenderer(fossil, -15.0f, 25.0f, -7.0f, 11.0f);
        fossil.sharedMaterial = fossilMaterial;
        fossil.shadowCastingMode = ShadowCastingMode.Off;
        fossil.receiveShadows = false;
        fossil.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
        fossil.sortingOrder = -9;
    }

    private static void PlaceRenderer(Renderer renderer, float targetX, float targetZ, float targetBaseY, float targetHeight)
    {
        float currentHeight = Mathf.Max(renderer.bounds.size.y, 0.01f);
        float uniformScale = targetHeight / currentHeight;
        renderer.transform.localScale *= uniformScale;
        Bounds resizedBounds = renderer.bounds;
        Vector3 offset = new(
            targetX - resizedBounds.center.x,
            targetBaseY - resizedBounds.min.y,
            targetZ - resizedBounds.center.z);
        renderer.transform.position += offset;
    }

    private static int ParseTrailingIndex(string value)
    {
        int separator = value.LastIndexOf(' ');
        return separator >= 0 && int.TryParse(value[(separator + 1)..], out int index) ? index : int.MaxValue;
    }

    private static Material LoadOrCreateMaterial(string path, Shader shader)
    {
        Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material == null)
        {
            material = new Material(shader);
            AssetDatabase.CreateAsset(material, path);
        }
        else
        {
            material.shader = shader;
        }
        material.shaderKeywords = Array.Empty<string>();
        return material;
    }

    private static void ConfigureLighting(Transform environment)
    {
        Transform legacyLighting = environment.parent != null ? environment.parent.Find("MapLighting") : null;
        if (legacyLighting != null)
        {
            Object.DestroyImmediate(legacyLighting.gameObject);
        }

        Transform previous = environment.Find("MapEnvironmentLighting");
        if (previous != null)
        {
            Object.DestroyImmediate(previous.gameObject);
        }

        GameObject lightingRoot = new("MapEnvironmentLighting");
        lightingRoot.transform.SetParent(environment, false);

        GameObject keyObject = new("MapMoonKey");
        keyObject.transform.SetParent(lightingRoot.transform, false);
        keyObject.transform.rotation = Quaternion.Euler(48.0f, -34.0f, 0.0f);
        Light key = keyObject.AddComponent<Light>();
        key.type = LightType.Directional;
        key.color = new Color(1.0f, 0.78f, 0.58f);
        key.intensity = 2.3f;
        key.shadows = LightShadows.Soft;
        RenderSettings.sun = key;

        Transform bossAnchor = FindDeepChild(environment, "ANCHOR_Node_L06_01");
        if (bossAnchor == null)
        {
            throw new InvalidOperationException("Boss portal anchor was not found for lighting.");
        }

        GameObject portalObject = new("BossPortalLight");
        portalObject.transform.SetParent(lightingRoot.transform, false);
        portalObject.transform.position = bossAnchor.position + Vector3.up * 2.0f;
        Light portal = portalObject.AddComponent<Light>();
        portal.type = LightType.Point;
        portal.color = new Color(1.0f, 0.16f, 0.04f);
        portal.intensity = 3.8f;
        portal.range = 9.0f;
        portal.shadows = LightShadows.None;

        Renderer[] flames = environment.GetComponentsInChildren<Renderer>(true)
            .Where(renderer => renderer.gameObject.name.StartsWith("Overhaul Brazier Flame", StringComparison.Ordinal))
            .OrderBy(renderer => renderer.gameObject.name, StringComparer.Ordinal)
            .ToArray();
        if (flames.Length != 4)
        {
            throw new InvalidOperationException($"Expected 4 brazier flames but found {flames.Length}.");
        }

        for (int index = 0; index < flames.Length; index++)
        {
            GameObject flameLightObject = new($"BrazierLight_{index:00}");
            flameLightObject.transform.SetParent(lightingRoot.transform, false);
            flameLightObject.transform.position = flames[index].bounds.center + Vector3.up * 0.15f;
            Light flameLight = flameLightObject.AddComponent<Light>();
            flameLight.type = LightType.Point;
            flameLight.color = new Color(1.0f, 0.28f, 0.06f);
            flameLight.intensity = 1.4f;
            flameLight.range = 4.2f;
            flameLight.shadows = LightShadows.None;
        }
    }

    private static Camera ConfigureCamera(Transform environment)
    {
        Camera camera = Camera.main ?? Object.FindFirstObjectByType<Camera>(FindObjectsInactive.Include);
        if (camera == null)
        {
            throw new InvalidOperationException("Map camera was not found.");
        }

        Transform startAnchor = FindDeepChild(environment, "ANCHOR_Node_L01_01");
        Transform bossAnchor = FindDeepChild(environment, "ANCHOR_Node_L06_01");
        if (startAnchor == null || bossAnchor == null)
        {
            throw new InvalidOperationException("Camera framing anchors were not found.");
        }

        Vector3 mapCenter = (startAnchor.position + bossAnchor.position) * 0.5f;
        mapCenter.y = 0.8f;
        camera.gameObject.SetActive(true);
        camera.enabled = true;
        camera.cullingMask = ~0;
        camera.orthographic = false;
        camera.fieldOfView = 35.0f;
        camera.nearClipPlane = 0.1f;
        camera.farClipPlane = 500.0f;
        camera.transform.position = mapCenter + new Vector3(0.0f, 30.0f, -27.0f);
        camera.transform.rotation = Quaternion.LookRotation(mapCenter - camera.transform.position, Vector3.up);
        camera.clearFlags = CameraClearFlags.Skybox;
        camera.backgroundColor = new Color(0.035f, 0.07f, 0.12f, 1.0f);
        camera.allowHDR = true;
        MapCameraCloudOverlay.Ensure(camera);
        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = new Color(0.30f, 0.25f, 0.20f);
        RenderSettings.ambientEquatorColor = new Color(0.18f, 0.14f, 0.12f);
        RenderSettings.ambientGroundColor = new Color(0.06f, 0.045f, 0.035f);
        RenderSettings.ambientIntensity = 1.0f;
        return camera;
    }

    private static void ConfigurePostProcessing(Transform environment, Camera camera)
    {
        VolumeProfile profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(VolumeProfilePath);
        if (profile == null)
        {
            profile = ScriptableObject.CreateInstance<VolumeProfile>();
            AssetDatabase.CreateAsset(profile, VolumeProfilePath);
        }

        Tonemapping tonemapping = GetOrAddVolumeComponent<Tonemapping>(profile);
        tonemapping.mode.Override(TonemappingMode.ACES);

        Bloom bloom = GetOrAddVolumeComponent<Bloom>(profile);
        bloom.threshold.Override(0.85f);
        bloom.intensity.Override(0.35f);
        bloom.scatter.Override(0.65f);
        bloom.highQualityFiltering.Override(true);

        ColorAdjustments colorAdjustments = GetOrAddVolumeComponent<ColorAdjustments>(profile);
        colorAdjustments.postExposure.Override(0.50f);
        colorAdjustments.contrast.Override(10.0f);
        colorAdjustments.saturation.Override(-2.0f);
        colorAdjustments.colorFilter.Override(new Color(1.0f, 0.96f, 0.88f, 1.0f));

        WhiteBalance whiteBalance = GetOrAddVolumeComponent<WhiteBalance>(profile);
        whiteBalance.temperature.Override(12.0f);
        whiteBalance.tint.Override(-1.0f);

        Vignette vignette = GetOrAddVolumeComponent<Vignette>(profile);
        vignette.color.Override(new Color(0.005f, 0.01f, 0.02f, 1.0f));
        vignette.intensity.Override(0.16f);
        vignette.smoothness.Override(0.45f);
        vignette.rounded.Override(false);

        Transform previous = environment.Find("MapPostProcessing");
        if (previous != null)
        {
            Object.DestroyImmediate(previous.gameObject);
        }
        GameObject volumeObject = new("MapPostProcessing");
        volumeObject.transform.SetParent(environment, false);
        Volume volume = volumeObject.AddComponent<Volume>();
        volume.isGlobal = true;
        volume.priority = 20.0f;
        volume.weight = 1.0f;
        volume.sharedProfile = profile;

        UniversalAdditionalCameraData cameraData = camera.GetUniversalAdditionalCameraData();
        cameraData.renderPostProcessing = true;
        cameraData.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
        cameraData.antialiasingQuality = AntialiasingQuality.High;
        cameraData.stopNaN = true;
        cameraData.dithering = true;
        cameraData.requiresDepthOption = CameraOverrideOption.On;
        cameraData.requiresColorOption = CameraOverrideOption.On;

        EditorUtility.SetDirty(profile);
        EditorUtility.SetDirty(cameraData);
        AssetDatabase.SaveAssets();
    }

    private static T GetOrAddVolumeComponent<T>(VolumeProfile profile) where T : VolumeComponent
    {
        profile.components.RemoveAll(value => value == null);
        if (!profile.TryGet(out T component))
        {
            component = profile.Add<T>(true);
        }
        if (!AssetDatabase.Contains(component))
        {
            AssetDatabase.AddObjectToAsset(component, profile);
        }
        EditorUtility.SetDirty(component);
        component.active = true;
        return component;
    }

    private static void CapturePreview(Camera camera)
    {
        const int width = 1920;
        const int height = 1080;
        RenderTexture renderTexture = new(width, height, 24, RenderTextureFormat.ARGB32);
        Texture2D image = new(width, height, TextureFormat.RGBA32, false);
        RenderTexture previousActive = RenderTexture.active;
        RenderTexture previousTarget = camera.targetTexture;
        try
        {
            camera.targetTexture = renderTexture;
            RenderTexture.active = renderTexture;
            camera.Render();
            camera.Render();
            image.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            image.Apply();
            File.WriteAllBytes(Path.GetFullPath(PreviewPath), image.EncodeToPNG());
        }
        finally
        {
            camera.targetTexture = previousTarget;
            RenderTexture.active = previousActive;
            Object.DestroyImmediate(image);
            renderTexture.Release();
            Object.DestroyImmediate(renderTexture);
        }
    }

    private static IntegrationReport ValidateIntegration(
        GameObject environment,
        MapEncounterNode[] nodes,
        Dictionary<string, Material> materials,
        Camera camera)
    {
        Quaternion expectedRotation = Quaternion.Euler(0.0f, 180.0f, 0.0f);
        bool transformValid = environment.transform.localPosition == Vector3.zero
            && Quaternion.Angle(environment.transform.localRotation, expectedRotation) < 0.01f
            && environment.transform.localScale == Vector3.one;
        int missingAnchors = nodes.Count(node => FindDeepChild(environment.transform, "ANCHOR_" + node.gameObject.name) == null);
        int missingMaterials = environment.GetComponentsInChildren<Renderer>(true)
            .SelectMany(renderer => renderer.sharedMaterials)
            .Count(material => material == null || material.shader == null || material.shader.name == "Hidden/InternalErrorShader");
        MapEncounterNode boss = nodes.First(node => node.gameObject.name == "Node_L06_01");
        Transform bossAnchor = FindDeepChild(environment.transform, "ANCHOR_Node_L06_01");
        float bossAnchorDistance = Vector3.Distance(boss.transform.position, bossAnchor.position);
        Renderer[] environmentRenderers = environment.GetComponentsInChildren<Renderer>(true);
        Bounds environmentBounds = environmentRenderers.Length > 0 ? environmentRenderers[0].bounds : default;
        for (int index = 1; index < environmentRenderers.Length; index++)
        {
            environmentBounds.Encapsulate(environmentRenderers[index].bounds);
        }
        int distantSpireCount = environmentRenderers.Count(renderer =>
            renderer.gameObject.name.StartsWith("Overhaul Distant Spire", StringComparison.Ordinal));
        Renderer chasmRenderer = FindDeepChild(environment.transform, "Chasm floor")?.GetComponent<Renderer>();
        Renderer hazeRenderer = FindDeepChild(environment.transform, "Low valley haze")?.GetComponent<Renderer>();
        bool atmosphereConfigured = RenderSettings.fog
            && RenderSettings.skybox != null
            && RenderSettings.skybox.shader != null
            && RenderSettings.skybox.shader.name == "Deinosavros/Map Dusk Sky"
            && chasmRenderer != null
            && chasmRenderer.sharedMaterial != null
            && chasmRenderer.sharedMaterial.shader.name == "Deinosavros/Map Mist Surface"
            && hazeRenderer != null
            && hazeRenderer.sharedMaterial != null
            && hazeRenderer.sharedMaterial.shader.name == "Deinosavros/Map Mist Surface"
            && distantSpireCount == 14;
        UniversalAdditionalCameraData cameraData = camera.GetUniversalAdditionalCameraData();
        bool postProcessingEnabled = cameraData.renderPostProcessing
            && environment.transform.Find("MapPostProcessing")?.GetComponent<Volume>()?.sharedProfile != null;

        IntegrationReport report = new()
        {
            success = transformValid && missingAnchors == 0 && missingMaterials == 0 && bossAnchorDistance < 0.001f
                && !PrefabUtility.IsPartOfPrefabInstance(environment) && atmosphereConfigured && postProcessingEnabled,
            message = "Map environment imported from the Blender production scene.",
            scenePath = ScenePath,
            modelPath = ModelPath,
            nodeCount = nodes.Length,
            rendererCount = environment.GetComponentsInChildren<Renderer>(true).Length,
            editableMeshObjectCount = environment.GetComponentsInChildren<MeshFilter>(true).Length,
            environmentUnpacked = !PrefabUtility.IsPartOfPrefabInstance(environment),
            materialCount = materials.Count,
            missingAnchorCount = missingAnchors,
            missingMaterialCount = missingMaterials,
            distantSpireCount = distantSpireCount,
            atmosphereConfigured = atmosphereConfigured,
            fogEnabled = RenderSettings.fog,
            postProcessingEnabled = postProcessingEnabled,
            rootPosition = environment.transform.localPosition,
            rootScale = environment.transform.localScale,
            environmentBoundsCenter = environmentBounds.center,
            environmentBoundsSize = environmentBounds.size,
            bossNodePosition = boss.transform.position,
            bossAnchorDistance = bossAnchorDistance
        };

        if (!report.success)
        {
            throw new InvalidOperationException(JsonUtility.ToJson(report, true));
        }
        return report;
    }

    private static Transform FindDeepChild(Transform root, string name)
    {
        foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
        {
            if (child.name == name)
            {
                return child;
            }
        }
        return null;
    }

    private static Color ToColor(float[] values, Color fallback)
    {
        if (values == null || values.Length < 3)
        {
            return fallback;
        }
        return new Color(values[0], values[1], values[2], values.Length > 3 ? values[3] : 1.0f);
    }

    private static Color ResolveBaseColor(MaterialRecord record)
    {
        Dictionary<string, Color> palette = new(StringComparer.Ordinal)
        {
            { "Finish_ChasmBackdrop", new Color(0.018f, 0.028f, 0.045f) },
            { "Finish_CushionMoss", new Color(0.035f, 0.13f, 0.05f) },
            { "Finish_FernLeaf", new Color(0.025f, 0.17f, 0.065f) },
            { "Finish_LeafTips", new Color(0.075f, 0.25f, 0.09f) },
            { "Finish_Bark", new Color(0.12f, 0.065f, 0.025f) },
            { "Finish_ValleyMist", new Color(0.17f, 0.23f, 0.31f) },
            { "Overhaul Basalt", new Color(0.045f, 0.055f, 0.07f) },
            { "Overhaul Basalt Highlight", new Color(0.11f, 0.12f, 0.14f) },
            { "Overhaul Blade Steel", new Color(0.28f, 0.32f, 0.36f) },
            { "Overhaul Broad Leaf", new Color(0.025f, 0.16f, 0.055f) },
            { "Overhaul Broad Leaf Light", new Color(0.075f, 0.27f, 0.09f) },
            { "Overhaul Damp Ground", new Color(0.07f, 0.105f, 0.065f) },
            { "Overhaul Dark Iron", new Color(0.025f, 0.03f, 0.038f) },
            { "Overhaul Moss", new Color(0.035f, 0.16f, 0.055f) },
            { "Overhaul Parchment", new Color(0.48f, 0.34f, 0.17f) },
            { "Overhaul Portal Crevice", new Color(0.035f, 0.012f, 0.012f) },
            { "Overhaul Ruin Banner", new Color(0.19f, 0.025f, 0.02f) },
            { "Overhaul Tarnished Bronze", new Color(0.23f, 0.105f, 0.035f) },
            { "Production Shrub Leaf Dark", new Color(0.018f, 0.115f, 0.028f) },
            { "Production Shrub Leaf Mid", new Color(0.035f, 0.205f, 0.052f) },
            { "Production Shrub Leaf Light", new Color(0.07f, 0.30f, 0.075f) }
        };
        return palette.TryGetValue(record.name, out Color color)
            ? color
            : ToColor(record.baseColor, Color.gray);
    }

    private static bool ContainsAny(string value, params string[] terms)
    {
        return terms.Any(term => value.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0);
    }

    private static string SanitizeFileName(string value)
    {
        foreach (char invalid in Path.GetInvalidFileNameChars())
        {
            value = value.Replace(invalid, '_');
        }
        return value;
    }

    private static void EnsureFolder(string folder)
    {
        string[] segments = folder.Split('/');
        string current = segments[0];
        for (int index = 1; index < segments.Length; index++)
        {
            string next = current + "/" + segments[index];
            if (!AssetDatabase.IsValidFolder(next))
            {
                AssetDatabase.CreateFolder(current, segments[index]);
            }
            current = next;
        }
    }

    [Serializable]
    private sealed class MapEnvironmentManifest
    {
        public string sourceBlend;
        public string fbx;
        public int objectCountBeforeJoin;
        public MaterialRecord[] materials;
    }

    [Serializable]
    private sealed class MaterialRecord
    {
        public string name;
        public float[] baseColor;
        public float roughness;
        public float metallic;
        public float[] emission;
        public float emissionStrength;
        public float alpha;
        public bool transparent;
        public string textureId;
    }

    [Serializable]
    private sealed class IntegrationReport
    {
        public bool success;
        public string message;
        public string scenePath;
        public string modelPath;
        public int nodeCount;
        public int rendererCount;
        public int editableMeshObjectCount;
        public bool environmentUnpacked;
        public int materialCount;
        public int missingAnchorCount;
        public int missingMaterialCount;
        public int distantSpireCount;
        public bool atmosphereConfigured;
        public bool fogEnabled;
        public bool postProcessingEnabled;
        public Vector3 rootPosition;
        public Vector3 rootScale;
        public Vector3 environmentBoundsCenter;
        public Vector3 environmentBoundsSize;
        public Vector3 bossNodePosition;
        public float bossAnchorDistance;
    }
}
