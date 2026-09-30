using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using TMPro;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace Deinosavros.MapReview.Editor
{
    public static class MapReviewBuild
    {
        public const string Root = "Assets/MapReview";
        public const string MapScene = Root + "/Scenes/MapReview.unity";
        public const string SampleScene = Root + "/Scenes/MapSampleReview.unity";
        public const string PortalSampleScene = Root + "/Scenes/MapPortalReview.unity";
        public const string EncounterScene = Root + "/Scenes/MapEncounterReview.unity";
        public const string BaselineScene = Root + "/Scenes/MapBaselineReview.unity";
        private const string Evidence = "Library/MapReviewEvidence";
        private static readonly string[] Protected = { "Assets/Scenes/Map.unity", "Assets/Scripts/Map/RunSession.cs", "Assets/Scripts/Map/BattleRunBridge.cs", "Assets/Editor/MapEnvironmentIntegration.cs", "ProjectSettings/EditorBuildSettings.asset" };
        [Serializable] private sealed class MaterialRecord { public string name; public float[] color; public float roughness; }
        [Serializable] private sealed class Manifest { public MaterialRecord[] materials; }
        [Serializable] public sealed class NodeSnapshot
        {
            public string id; public Vector3 position; public Quaternion rotation; public Vector3 scale;
            public string[] colliders;
        }
        [Serializable] private sealed class NodeReport { public NodeSnapshot[] nodes; }

        [MenuItem("Tools/Map Review/Open Map Review")]
        public static void OpenMapReview() => EditorSceneManager.OpenScene(MapScene,OpenSceneMode.Single);

        [MenuItem("Tools/Map Review/Open Representative Sample")]
        public static void OpenSampleReview() => EditorSceneManager.OpenScene(SampleScene,OpenSceneMode.Single);

        [MenuItem("Tools/Map Review/Apply Visuals Only")]
        public static void ApplyCurrentVisuals()
        {
            var scene=SceneManager.GetActiveScene();RequireReviewAsset(scene.path);
            var profile=AssetDatabase.LoadAssetAtPath<MapVisualProfile>(Root+"/MapVisualProfile.asset");
            var environment=scene.GetRootGameObjects().SingleOrDefault(g=>g.name=="Review Environment");
            if(environment!=null)ApplyEnvironmentMaterials(environment,profile);
            ApplyVisuals(scene,profile);AssetDatabase.SaveAssets();EditorSceneManager.SaveScene(scene);
        }

        [MenuItem("Tools/Map Review/Reimport Current Environment")]
        public static void ReimportCurrent()
        {
            var scene=SceneManager.GetActiveScene();RequireReviewAsset(scene.path);
            var source=InScene<MapReviewEnvironmentSource>(scene).Single();
            string path=source.modelAssetPath;bool sample=source.representativeSample;
            var graph=AssetDatabase.LoadAssetAtPath<MapGraphDefinition>(Root+"/MapGraph.asset");
            var profile=AssetDatabase.LoadAssetAtPath<MapVisualProfile>(Root+"/MapVisualProfile.asset");
            ConfigureModel(path);ImportEnvironment(scene,path,graph,profile,source.gameObject.name,sample);
            ApplyVisuals(scene,profile);AssetDatabase.SaveAssets();EditorSceneManager.SaveScene(scene);
        }

        [MenuItem("Tools/Map Review/Run State Contract Tests")]
        public static void RunContractTests() => MapReviewTests.Run();

        public static void BatchPrepare()
        {
            Guard(() =>
            {
                EnsureFolders();
                MapReviewTests.Run();
                var graph = LoadOrCreate<MapGraphDefinition>(Root + "/MapGraph.asset"); graph.SetReviewTopology(); EditorUtility.SetDirty(graph);
                var visual = CreateVisualProfile();
                CardDefinition[] deck = { Card("TidalWave"), Card("FleetFootwork"), Card("SolarShield"), Card("UncertainFates") };
                var art = MakeArtwork(deck.Distinct().ToArray());
                ConfigureModel(Root + "/Art/Models/RuinsSourceReview.fbx");
                ConfigureModel(Root + "/Art/Models/RuinsSample.fbx");
                var source = EditorSceneManager.OpenScene("Assets/Scenes/Map.unity", OpenSceneMode.Single);
                var oldNodes = InScene<MapEncounterNode>(source).OrderBy(n => n.NodeId).ToArray();
                if (oldNodes.Length != 11) throw new InvalidOperationException("Production map does not have the expected eleven nodes.");
                var original = oldNodes.Select(Snapshot).ToArray();
                File.WriteAllText(Evidence + "/unity-original-nodes.json", JsonUtility.ToJson(new NodeReport { nodes=original },true));
                Camera originalCamera = InScene<Camera>(source).First(c=>c.CompareTag("MainCamera"));
                float yaw = originalCamera.transform.eulerAngles.y;
                if (!File.Exists(BaselineScene))
                {
                    EditorSceneManager.SaveScene(source, BaselineScene, true);
                    CaptureCamera(originalCamera, Evidence + "/00-production-camera.png");
                }
                var review = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
                SceneManager.SetActiveScene(review);
                var nodesRoot = new GameObject("Review Nodes");
                foreach (var legacy in oldNodes)
                {
                    var copy = Object.Instantiate(legacy.gameObject, nodesRoot.transform, true);
                    copy.name = legacy.name;
                    copy.transform.position = legacy.transform.position;
                    copy.transform.rotation = legacy.transform.rotation;
                    copy.transform.localScale = legacy.transform.lossyScale;
                    var old = copy.GetComponent<MapEncounterNode>();
                    var collider = new SerializedObject(old).FindProperty("interactionCollider").objectReferenceValue as Collider;
                    foreach (var behaviour in copy.GetComponentsInChildren<MonoBehaviour>(true)) Object.DestroyImmediate(behaviour);
                    foreach (var renderer in copy.GetComponentsInChildren<Renderer>(true)) renderer.enabled=false;
                    foreach (var particles in copy.GetComponentsInChildren<ParticleSystem>(true))
                    { var main=particles.main;main.playOnAwake=false;particles.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear); }
                    var node = copy.AddComponent<MapReviewNode>(); node.nodeId=legacy.NodeId;node.interactionCollider=collider;
                }
                EditorSceneManager.CloseScene(source,true);
                var controller = new GameObject("Map Review Controller").AddComponent<MapReviewController>();
                controller.graph=graph;controller.startingDeck=deck;controller.artwork=art;
                controller.stateOutlineMaterial=StateOutlineMaterial();
                var camera = NewCamera(yaw);
                var framing=camera.gameObject.AddComponent<MapReviewCamera>();framing.profile=visual;framing.yaw=yaw;
                ImportEnvironment(review,Root+"/Art/Models/RuinsSourceReview.fbx",graph,visual,"Review Environment",false);
                AddMissingNodes(review, graph);
                ApplyVisuals(review,visual);
                VerifyOriginalNodes(review,original);
                EditorSceneManager.SaveScene(review,MapScene);
                AssetDatabase.SaveAssets();
                CreateSample(graph,visual,deck,art);
                CreateEncounter(graph,deck);
                AssetDatabase.SaveAssets();
                EditorSceneManager.OpenScene(MapScene,OpenSceneMode.Single);
                VerifyAssets();
                File.WriteAllText(Evidence+"/prepare-success.txt","Prepared isolated review scenes and original node snapshot. Full modeling gate remains pending.");
            });
        }

        public static void BatchReimport()
        {
            Guard(() =>
            {
                string model=Argument("-reviewModel",null);
                string scenePath=Argument("-reviewScene",null);
                if(string.IsNullOrWhiteSpace(model)||string.IsNullOrWhiteSpace(scenePath))
                    throw new ArgumentException("BatchReimport requires explicit -reviewModel and -reviewScene arguments.");
                RequireReviewAsset(scenePath);RequireReviewAsset(model);
                ConfigureModel(model);
                var scene=EditorSceneManager.OpenScene(scenePath,OpenSceneMode.Single);
                var snapshots=InScene<MapReviewNode>(scene).Select(Snapshot).ToArray();
                var graph=AssetDatabase.LoadAssetAtPath<MapGraphDefinition>(Root+"/MapGraph.asset");
                var visual=AssetDatabase.LoadAssetAtPath<MapVisualProfile>(Root+"/MapVisualProfile.asset");
                int count=0;string[] meshIdentity=null;
                for(int pass=0;pass<2;pass++)
                {
                    ImportEnvironment(scene,model,graph,visual,"Review Environment",scenePath==SampleScene||scenePath==PortalSampleScene);
                    ApplyVisuals(scene,visual);
                    int next=scene.GetRootGameObjects().Sum(g=>g.GetComponentsInChildren<Transform>(true).Length);
                    if(pass>0 && next!=count)throw new InvalidOperationException("Reimport created duplicate objects.");count=next;
                    var meshes=InScene<MeshFilter>(scene).Select(m=>m.name+"|"+m.sharedMesh?.name+"|"+m.sharedMesh?.vertexCount).OrderBy(n=>n).ToArray();
                    if(pass>0&&!meshes.SequenceEqual(meshIdentity))throw new InvalidOperationException("Reimport changed independent mesh identities.");meshIdentity=meshes;
                    VerifyOriginalNodes(scene,snapshots);
                    EditorSceneManager.SaveScene(scene,scenePath);
                }
                AssetDatabase.SaveAssets();
                EditorSceneManager.OpenScene(scenePath,OpenSceneMode.Single);
                VerifyAssets();
                File.WriteAllText(Evidence+"/reimport-verification.txt",$"PASS: two imports have {count} transforms and {meshIdentity.Length} independent mesh objects; identical mesh identities; original transforms and colliders unchanged; saved and reopened.");
            });
        }

        public static void BatchBuild()
        {
            Guard(() =>
            {
                MapReviewTests.Run();VerifyAssets();
                string initial=Argument("-reviewInitialScene",MapScene);RequireReviewAsset(initial);
                var scenes=new[]{initial,MapScene,EncounterScene,SampleScene,PortalSampleScene,BaselineScene}.Where(File.Exists).Distinct().ToArray();
                Directory.CreateDirectory("Builds/MapReview");
                var result=BuildPipeline.BuildPlayer(new BuildPlayerOptions
                {
                    scenes=scenes,locationPathName="Builds/MapReview/MapReview.exe",target=BuildTarget.StandaloneWindows64,
                    options=BuildOptions.Development
                });
                if(result.summary.result!=BuildResult.Succeeded)throw new InvalidOperationException("Review player build failed: "+result.summary.result);
                File.WriteAllText(Evidence+"/build-report.txt",$"Standalone Windows development build succeeded. Size {result.summary.totalSize}. Scene list is build-local only.");
            });
        }

        public static void BatchRefreshSampleAndBuild()
        {
            Guard(() =>
            {
                ReviewSurfaceTextures.Generate();
                CreateVisualProfile();
                foreach(string path in new[]{MapScene,SampleScene})
                {
                    var artScene=EditorSceneManager.OpenScene(path,OpenSceneMode.Single);
                    var ui=InScene<MapReviewController>(artScene).Single();
                    ui.startingDeck=ui.startingDeck.Distinct().ToArray();
                    ui.artwork=MakeArtwork(ui.startingDeck.Distinct().ToArray());
                    ui.stateOutlineMaterial=StateOutlineMaterial();
                    EditorSceneManager.SaveScene(artScene);
                }
                ConfigureModel(Root+"/Art/Models/RuinsSample.fbx");
                var scene=EditorSceneManager.OpenScene(SampleScene,OpenSceneMode.Single);
                var graph=AssetDatabase.LoadAssetAtPath<MapGraphDefinition>(Root+"/MapGraph.asset");
                var profile=AssetDatabase.LoadAssetAtPath<MapVisualProfile>(Root+"/MapVisualProfile.asset");
                ImportEnvironment(scene,Root+"/Art/Models/RuinsSample.fbx",graph,profile,"Review Environment",true);
                ApplyVisuals(scene,profile);AssetDatabase.SaveAssets();EditorSceneManager.SaveScene(scene);
                ConfigureModel(Root+"/Art/Models/RuinsPortalSample.fbx");
                EditorSceneManager.SaveScene(scene,PortalSampleScene,true);
                var portalScene=EditorSceneManager.OpenScene(PortalSampleScene,OpenSceneMode.Single);
                ImportEnvironment(portalScene,Root+"/Art/Models/RuinsPortalSample.fbx",graph,profile,"Review Environment",true);
                ApplyVisuals(portalScene,profile);AssetDatabase.SaveAssets();EditorSceneManager.SaveScene(portalScene);
                VerifyAssets();
            });
            BatchBuild();
        }

        public static void BatchFullReviewBuild()
        {
            if(string.IsNullOrEmpty(Argument("-reviewModel",null)))
                throw new ArgumentException("The final review model must be supplied explicitly.");
            BatchReimport();
            BatchRefreshSampleAndBuild();
        }

        // The import surface is explicit; no production scene or node count is implicit.
        public static GameObject ImportEnvironment(Scene targetScene,string modelPath,MapGraphDefinition graph,MapVisualProfile profile,string rootName,bool sample)
        {
            if(!targetScene.IsValid()||!targetScene.isLoaded||graph==null||profile==null)throw new ArgumentException("Explicit target scene, graph and visual profile are required.");
            RequireReviewAsset(modelPath);
            SceneManager.SetActiveScene(targetScene);
            GameObject model=AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
            if(model==null)throw new InvalidOperationException("Review model is not imported: "+modelPath);
            GameObject previous=targetScene.GetRootGameObjects().SingleOrDefault(g=>g.name==rootName);
            var instance=(GameObject)PrefabUtility.InstantiatePrefab(model,targetScene);
            instance.name=rootName+" Pending";
            PrefabUtility.UnpackPrefabInstance(instance,PrefabUnpackMode.Completely,InteractionMode.AutomatedAction);
            try
            {
                if(!sample) {EnsureGraphAnchors(instance.transform,graph);AlignToExistingNodes(instance.transform,targetScene,graph);}
                else instance.transform.rotation=Quaternion.Euler(0,180,0);
                ApplyEnvironmentMaterials(instance,profile);
                if(previous!=null)Object.DestroyImmediate(previous);
                instance.name=rootName;
                var source=instance.AddComponent<MapReviewEnvironmentSource>();source.modelAssetPath=modelPath;source.representativeSample=sample;
                var camera=InScene<Camera>(targetScene).FirstOrDefault(c=>c.CompareTag("MainCamera"));
                if(camera!=null){var framing=camera.GetComponent<MapReviewCamera>();if(framing!=null){framing.environment=instance.transform;framing.sample=sample;}}
                return instance;
            }
            catch {Object.DestroyImmediate(instance);throw;}
        }

        internal static void ApplyEnvironmentMaterials(GameObject instance,MapVisualProfile profile)
        {
            var materials=MakeMaterials(profile);
            foreach(var renderer in instance.GetComponentsInChildren<Renderer>(true))
            {
                renderer.sharedMaterials=renderer.sharedMaterials.Select(m=>m!=null&&materials.TryGetValue(m.name,out var replacement)?replacement:m).ToArray();
                renderer.shadowCastingMode=ShadowCastingMode.On;renderer.receiveShadows=true;
                if(renderer.name=="Background Chasm floor"||renderer.name=="Background Low valley haze")
                {
                    renderer.sharedMaterial=BackgroundMist(renderer.name=="Background Chasm floor");
                    renderer.shadowCastingMode=ShadowCastingMode.Off;renderer.receiveShadows=false;
                }
                if(renderer.sharedMaterials.Any(m=>m!=null && m.shader.name=="Deinosavros/Map Review/Portal Veil"))renderer.shadowCastingMode=ShadowCastingMode.Off;
            }
            var shadowRange=instance.GetComponent<MapEnvironmentShadowRange>();
            if(shadowRange==null)shadowRange=instance.AddComponent<MapEnvironmentShadowRange>();
            shadowRange.profile=profile;
            shadowRange.targetCamera=InScene<Camera>(instance.scene).FirstOrDefault(c=>c.CompareTag("MainCamera"));
        }

        private static void AlignToExistingNodes(Transform environment,Scene scene,MapGraphDefinition graph)
        {
            var nodes=InScene<MapReviewNode>(scene).ToArray();
            var pairs=nodes.Select(n=>(node:n,anchor:Deep(environment,graph.Find(n.nodeId).anchor))).Where(p=>p.anchor!=null).ToArray();
            if(pairs.Length<2)throw new InvalidOperationException("Cannot infer model alignment without known anchors.");
            Vector3 a=pairs[0].anchor.position,b=pairs[1].anchor.position;
            Vector3 u=pairs[0].node.transform.position,v=pairs[1].node.transform.position;
            Vector3 from=b-a,to=v-u;from.y=0;to.y=0;
            float scale=to.magnitude/from.magnitude;
            float angle=Vector3.SignedAngle(new Vector3(from.x,0,from.z),new Vector3(to.x,0,to.z),Vector3.up);
            environment.localScale*=scale;environment.rotation=Quaternion.Euler(0,angle,0)*environment.rotation;
            Vector3 correction=u-pairs[0].anchor.position;
            correction.y=pairs.Select(p=>p.node.transform.position.y-p.anchor.position.y).OrderBy(y=>y).ElementAt(pairs.Length/2);
            environment.position+=correction;
            var offsets=new List<string>();
            foreach(var pair in pairs)
            {
                Vector3 drift=pair.node.transform.position-pair.anchor.position;
                offsets.Add($"{pair.node.nodeId}: preserved vertical interaction offset {drift.y:R}");
                if(new Vector2(drift.x,drift.z).magnitude>.001f)
                    throw new InvalidOperationException($"Imported anchor drift: {pair.node.nodeId}; model {pair.anchor.position:F6}; target {pair.node.transform.position:F6}; scale {environment.localScale:F6}; rotation {environment.eulerAngles:F6}");
            }
            File.WriteAllLines(Evidence+"/model-alignment.txt",new[]{$"Rigid horizontal alignment derived from original anchors. Scale {scale:R}; yaw {angle:R}; position {environment.position:F6}. All {pairs.Length} XZ centers align within 1 mm. Existing interaction heights are intentionally preserved."}.Concat(offsets));
        }

        private static void AddMissingNodes(Scene scene,MapGraphDefinition graph)
        {
            var root=scene.GetRootGameObjects().Single(g=>g.name=="Review Nodes");
            var environment=scene.GetRootGameObjects().Single(g=>g.name=="Review Environment");
            foreach(var definition in graph.nodes)
            {
                if(InScene<MapReviewNode>(scene).Any(n=>n.nodeId==definition.id))continue;
                Transform terrace=Deep(environment.transform,definition.terrace);
                if(terrace==null)throw new InvalidOperationException("Missing terrace for "+definition.id);
                var anchor=Deep(environment.transform,definition.anchor);
                if(anchor==null)throw new InvalidOperationException("Missing generated anchor for "+definition.id);
                var obj=new GameObject("Node_L"+definition.id.Substring(6));obj.transform.SetParent(root.transform,false);obj.transform.position=anchor.position;
                var collider=obj.AddComponent<BoxCollider>();collider.center=new Vector3(0,.1f,0);collider.size=new Vector3(2,.45f,2);
                var node=obj.AddComponent<MapReviewNode>();node.nodeId=definition.id;node.interactionCollider=collider;
            }
        }
        private static void EnsureGraphAnchors(Transform environment,MapGraphDefinition graph)
        {
            var reference=Deep(environment,"ANCHOR_Node_L01_01");
            if(reference==null)throw new InvalidOperationException("Missing source start anchor.");
            foreach(var node in graph.nodes)
            {
                if(Deep(environment,node.anchor)!=null)continue;
                var terrace=Deep(environment,node.terrace);
                if(terrace==null)throw new InvalidOperationException("No source terrace for "+node.id);
                var anchor=new GameObject(node.anchor).transform;anchor.SetParent(environment,false);
                Vector3 point=environment.InverseTransformPoint(terrace.position);
                point.y=environment.InverseTransformPoint(reference.position).y;anchor.localPosition=point;
            }
        }

        private static void CreateSample(MapGraphDefinition graph,MapVisualProfile profile,CardDefinition[] deck,MapReviewCardArt[] art)
        {
            var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Additive);SceneManager.SetActiveScene(scene);
            var camera=NewCamera(0);var framing=camera.gameObject.AddComponent<MapReviewCamera>();framing.profile=profile;framing.sample=true;
            var controller=new GameObject("Map Review Controller").AddComponent<MapReviewController>();controller.graph=graph;controller.startingDeck=deck;controller.artwork=art;
            controller.stateOutlineMaterial=StateOutlineMaterial();
            ImportEnvironment(scene,Root+"/Art/Models/RuinsSample.fbx",graph,profile,"Review Environment",true);
            ApplyVisuals(scene,profile);EditorSceneManager.SaveScene(scene,SampleScene);
        }
        private static void CreateEncounter(MapGraphDefinition graph,CardDefinition[] deck)
        {
            var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Additive);SceneManager.SetActiveScene(scene);
            var camera=NewCamera(0);camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.06f,.08f,.11f);
            var controller=new GameObject("Map Encounter Review Receiver").AddComponent<MapEncounterReviewController>();controller.graph=graph;controller.definitions=deck.Distinct().ToArray();
            EditorSceneManager.SaveScene(scene,EncounterScene);
        }
        private static Camera NewCamera(float yaw)
        {
            var go=new GameObject("Main Camera",typeof(Camera),typeof(AudioListener));go.tag="MainCamera";
            var camera=go.GetComponent<Camera>();camera.fieldOfView=35;camera.transform.rotation=Quaternion.Euler(50,yaw,0);
            camera.clearFlags=CameraClearFlags.Skybox;camera.allowHDR=true;camera.allowMSAA=false;
            var data=camera.GetUniversalAdditionalCameraData();data.renderPostProcessing=true;data.dithering=true;data.stopNaN=true;
            data.antialiasing=AntialiasingMode.SubpixelMorphologicalAntiAliasing;data.antialiasingQuality=AntialiasingQuality.High;
            data.requiresDepthTexture=true;return camera;
        }
        private static MapVisualProfile CreateVisualProfile()
        {
            string path=Root+"/MapVisualProfile.asset";
            var existing=AssetDatabase.LoadAssetAtPath<MapVisualProfile>(path);
            if(existing!=null)return existing;
            var p=LoadOrCreate<MapVisualProfile>(path);
            p.sandstone=new Color(.67f,.64f,.57f);p.soil=new Color(.43f,.38f,.30f);p.rock=new Color(.51f,.52f,.49f);p.moss=new Color(.28f,.34f,.17f);
            p.leafDark=new Color(.258f,.389f,.160f);p.leafLight=new Color(.437f,.542f,.248f);
            p.stoneTileMeters=3;p.groundTileMeters=3;p.rockTileMeters=3;p.detailStrength=.3f;
            p.fieldOfView=35;p.pitch=50;p.frameFill=.9f;p.uiMarginPixels=24;p.visibleCliffDepth=3;
            p.sunlight=new Color(.91f,.94f,1);p.sunlightIntensity=1.4f;p.shadowStrength=.7f;
            p.ambientSky=new Color(.45f,.51f,.58f);p.ambientEquator=new Color(.38f,.38f,.34f);p.ambientGround=new Color(.24f,.23f,.2f);
            p.skyTop=new Color(.22f,.3f,.37f);p.skyHorizon=new Color(.46f,.50f,.51f);p.skyBottom=new Color(.14f,.18f,.2f);
            p.exposure=.15f;p.contrast=8;p.saturation=-5;p.temperature=-3;p.bloom=.15f;p.vignette=.08f;
            p.cloudOpacity=.12f;p.cloudCenterClarity=.8f;
            EditorUtility.SetDirty(p);return p;
        }
        public static void ApplyVisuals(Scene scene,MapVisualProfile profile)
        {
            SceneManager.SetActiveScene(scene);
            var root=scene.GetRootGameObjects().SingleOrDefault(g=>g.name=="Review Lighting");
            if(root!=null)Object.DestroyImmediate(root);root=new GameObject("Review Lighting");
            var light=new GameObject("Cool neutral canopy light").AddComponent<Light>();light.transform.SetParent(root.transform,false);
            light.type=LightType.Directional;light.color=profile.sunlight;light.intensity=profile.sunlightIntensity;
            light.transform.rotation=Quaternion.Euler(48,-35,0);light.shadows=LightShadows.Soft;light.shadowStrength=profile.shadowStrength;
            var portal=Deep(scene.GetRootGameObjects().SingleOrDefault(g=>g.name=="Review Environment")?.transform,"Portal - carved solid ring");
            if(portal!=null)
            {
                var glow=new GameObject("Portal amber spill").AddComponent<Light>();glow.transform.SetParent(root.transform,false);glow.transform.position=portal.position+Vector3.up*.5f;
                glow.type=LightType.Point;glow.color=new Color(1,.37f,.08f);glow.intensity=3.5f;glow.range=9;glow.shadows=LightShadows.None;
            }
            var environmentRoot=scene.GetRootGameObjects().SingleOrDefault(g=>g.name=="Review Environment");
            if(environmentRoot!=null)
                foreach(var flame in environmentRoot.GetComponentsInChildren<Renderer>())
                    if(flame.name.Replace('_',' ').StartsWith("Brazier flame ",StringComparison.Ordinal))
                    {
                        var fire=new GameObject("Warm spill - "+flame.name).AddComponent<Light>();
                        fire.transform.SetParent(root.transform,false);fire.transform.position=flame.bounds.center+Vector3.up*.12f;
                        fire.type=LightType.Point;fire.color=new Color(1,.44f,.13f);fire.intensity=.45f;fire.range=3;
                        fire.shadows=LightShadows.None;
                    }
            RenderSettings.ambientMode=AmbientMode.Trilight;RenderSettings.ambientSkyColor=profile.ambientSky;
            RenderSettings.ambientEquatorColor=profile.ambientEquator;RenderSettings.ambientGroundColor=profile.ambientGround;
            RenderSettings.fog=false;
            var skyShader=Shader.Find("Deinosavros/Map Review/Sky");
            if(skyShader!=null)
            {
                var sky=LoadMaterial(Root+"/Art/Materials/ReviewSky.mat",skyShader);
                sky.SetColor("_TopColor",profile.skyTop);sky.SetColor("_HorizonColor",profile.skyHorizon);sky.SetColor("_BottomColor",profile.skyBottom);
                EditorUtility.SetDirty(sky);RenderSettings.skybox=sky;
            }
            var volume=root.AddComponent<Volume>();volume.isGlobal=true;volume.priority=10;
            volume.sharedProfile=CreateVolume(profile);
            var camera=InScene<Camera>(scene).FirstOrDefault(c=>c.CompareTag("MainCamera"));
            if(camera!=null && scene.path==MapScene)
            {
                var atmosphere=camera.GetComponent<MapReviewAtmosphere>();
                if(atmosphere==null)atmosphere=camera.gameObject.AddComponent<MapReviewAtmosphere>();
                atmosphere.profile=profile;
            }
        }
        private static VolumeProfile CreateVolume(MapVisualProfile p)
        {
            var profile=LoadOrCreate<VolumeProfile>(Root+"/ReviewVolume.asset");
            foreach(var component in profile.components.ToArray())if(component!=null)Object.DestroyImmediate(component,true);
            profile.components.Clear();
            T Add<T>() where T:VolumeComponent {var c=profile.Add<T>(true);AssetDatabase.AddObjectToAsset(c,profile);return c;}
            Add<Tonemapping>().mode.Override(TonemappingMode.ACES);
            var bloom=Add<Bloom>();bloom.intensity.Override(p.bloom);bloom.threshold.Override(.85f);bloom.scatter.Override(.65f);
            var color=Add<ColorAdjustments>();color.postExposure.Override(p.exposure);color.contrast.Override(p.contrast);color.saturation.Override(p.saturation);
            var white=Add<WhiteBalance>();white.temperature.Override(p.temperature);white.tint.Override(-2);
            var vignette=Add<Vignette>();vignette.intensity.Override(p.vignette);vignette.smoothness.Override(.45f);
            EditorUtility.SetDirty(profile);return profile;
        }
        private static Dictionary<string,Material> MakeMaterials(MapVisualProfile profile)
        {
            ConfigureOriginalFoliageTextures();
            var manifest=JsonUtility.FromJson<Manifest>(File.ReadAllText(Root+"/Art/Models/RuinsManifest.json"));
            var result=new Dictionary<string,Material>();
            foreach(var record in manifest.materials)
            {
                string filename=string.Concat(record.name.Select(c=>char.IsLetterOrDigit(c)||c=='-'||c=='_'?c:'_'));
                string surface=record.name switch
                {
                    "V2 Limestone broad planes" or "V2 Exposed fracture" or "RU Aged limestone" or "RU Fracture interior" or "RU Damp stone" => "Limestone",
                    "V2 Cliff grey umber" or "RU Rock" => "Rock",
                    "V2 Warm silt" or "V2 Rooted moss" or "RU Soil" => "Soil",
                    _ => null
                };
                bool useSurface=surface!=null && File.Exists(ReviewSurfaceTextures.Folder+"/"+surface+"_Albedo.png");
                var mat=LoadMaterial(Root+"/Art/Materials/"+filename+".mat",Shader.Find(useSurface?ReviewSurfaceTextures.ShaderName:"Universal Render Pipeline/Lit"));
                var color=record.color;
                mat.SetColor("_BaseColor",color?.Length>=3?new Color(color[0],color[1],color[2],1).gamma:Color.gray);
                mat.SetFloat("_Metallic",0);mat.SetFloat("_Smoothness",record.name.Contains("soil",StringComparison.OrdinalIgnoreCase)?.1f:.18f);
                bool leaves=record.name.Contains("leaf",StringComparison.OrdinalIgnoreCase)||record.name.Contains("frond",StringComparison.OrdinalIgnoreCase);
                mat.SetFloat("_Cull",leaves?0:2);
                bool plantStem=record.name=="V2 Fibrous palm trunk"||record.name=="RU Palm bark"||record.name=="RU Rachis";
                if(leaves||plantStem)
                {
                    var wind=Shader.Find("Deinosavros/Map Review/Foliage Lit");
                    if(wind==null)throw new InvalidOperationException("Review foliage shader is missing.");
                    mat.shader=wind;mat.SetFloat("_UseTextures",0);mat.SetFloat("_NormalStrength",0);
                    mat.SetFloat("_WindStrength",.025f);mat.SetFloat("_WindFlutter",.15f);mat.SetFloat("_WindSpeed",.65f);
                    mat.SetFloat("_WindRootHeight",0);mat.SetFloat("_WindBendHeight",1.5f);mat.SetFloat("_WindSpatialScale",.3f);
                    mat.SetFloat("_Cull",leaves?0:2);mat.renderQueue=leaves?2450:2000;
                    if(record.name.Contains("new",StringComparison.OrdinalIgnoreCase))mat.SetColor("_BaseColor",profile.leafLight);
                    else if(record.name.Contains("mature",StringComparison.OrdinalIgnoreCase))mat.SetColor("_BaseColor",profile.leafDark);
                    if(record.name=="V2 Runtime Original Leaf Atlas")
                    {
                        string prefix=Root+"/Art/Textures/OriginalFoliage/RuinsV2_Leaf_";
                        var atlas=AssetDatabase.LoadAssetAtPath<Texture2D>(prefix+"BaseColor.png");
                        if(atlas==null)throw new InvalidOperationException("Original foliage atlas is required for runtime cards.");
                        mat.SetFloat("_UseTextures",1);mat.SetFloat("_NormalStrength",.25f);mat.SetFloat("_Smoothness",.15f);mat.SetFloat("_Cutoff",.35f);
                        mat.SetColor("_BaseColor",new Color(profile.leafDark.r/.258f,profile.leafDark.g/.389f,profile.leafDark.b/.160f,1));
                        mat.SetTexture("_BaseMap",atlas);mat.SetTexture("_NormalMap",AssetDatabase.LoadAssetAtPath<Texture2D>(prefix+"Normal.png"));
                        mat.SetTexture("_RoughnessMap",AssetDatabase.LoadAssetAtPath<Texture2D>(prefix+"Roughness.png"));
                    }
                }
                if(record.name=="MR Portal veil")
                {
                    mat.shader=Shader.Find("Deinosavros/Map Review/Portal Veil");mat.SetFloat("_Opacity",.22f);
                    mat.SetColor("_Color",new Color(1,.25f,.045f,1));
                }
                if(record.name.Contains("amber",StringComparison.OrdinalIgnoreCase)||record.name.Contains("flame",StringComparison.OrdinalIgnoreCase))
                {mat.EnableKeyword("_EMISSION");mat.SetColor("_EmissionColor",new Color(1,.3f,.035f)*2);}
                if(useSurface)
                {
                    string prefix=ReviewSurfaceTextures.Folder+"/"+surface;
                    Color reference=surface=="Soil"?new Color(.43f,.38f,.30f):surface=="Rock"?new Color(.51f,.52f,.49f):new Color(.67f,.64f,.57f);
                    Color target=surface=="Soil"?profile.soil:surface=="Rock"?profile.rock:profile.sandstone;
                    mat.SetFloat("_UseTextures",1);mat.SetColor("_BaseColor",new Color(target.r/reference.r,target.g/reference.g,target.b/reference.b,1));
                    mat.SetTexture("_BaseMap",AssetDatabase.LoadAssetAtPath<Texture2D>(prefix+"_Albedo.png"));
                    mat.SetTexture("_NormalMap",AssetDatabase.LoadAssetAtPath<Texture2D>(prefix+"_Normal.png"));
                    mat.SetTexture("_RoughnessMap",AssetDatabase.LoadAssetAtPath<Texture2D>(prefix+"_Roughness.png"));
                    bool soil=surface=="Soil";
                    mat.SetFloat("_Smoothness",soil?profile.groundSmoothness:profile.stoneSmoothness);mat.SetFloat("_ReferenceRoughness",soil?.90f:.82f);
                    mat.SetFloat("_TileMeters",soil?profile.groundTileMeters:surface=="Rock"?profile.rockTileMeters:profile.stoneTileMeters);mat.SetFloat("_NormalStrength",soil?profile.detailStrength*2/3:profile.detailStrength);
                    mat.SetFloat("_MacroVariation",.05f);mat.SetFloat("_MacroMeters",7);
                    mat.SetFloat("_MossAmount",soil?.85f:surface=="Rock"?.25f:.16f);
                    mat.SetColor("_MossColor",profile.moss);
                    mat.SetVector("_MossHeightRange",soil?new Vector4(2,4,0,0):new Vector4(-.2f,1.8f,0,0));
                    mat.SetFloat("_MossUpwardBias",.8f);
                    bool terrain=record.name=="V2 Warm silt"||record.name=="V2 Rooted moss"||record.name=="V2 Cliff grey umber";
                    mat.SetFloat("_GroundBlendStrength",terrain?1:0);
                    if(terrain)
                    {
                        // V3 COLOR_0 stores normalized soil, rooted moss and exposed-rock regions.
                        bool cliff=record.name=="V2 Cliff grey umber";
                        string groundPrefix=ReviewSurfaceTextures.Folder+"/"+(cliff?"Rock":"Soil");
                        mat.SetColor("_BaseColor",Color.white);
                        mat.SetColor("_SoilColor",profile.soil);
                        mat.SetColor("_ExposedRockColor",new Color(.53f,.51f,.45f));
                        mat.SetColor("_MossColor",profile.moss);
                        mat.SetFloat("_GroundDetailStrength",.6f);
                        mat.SetColor("_GroundReferenceColor",cliff?new Color(.51f,.52f,.49f):new Color(.43f,.38f,.30f));
                        mat.SetFloat("_Smoothness",cliff?.18f:.10f);mat.SetFloat("_ReferenceRoughness",cliff?.82f:.90f);
                        mat.SetFloat("_TileMeters",3);mat.SetFloat("_NormalStrength",.30f);
                        foreach(string slot in new[]{"Albedo","Normal","Roughness"})
                            mat.SetTexture(slot=="Albedo"?"_BaseMap":slot=="Normal"?"_NormalMap":"_RoughnessMap",AssetDatabase.LoadAssetAtPath<Texture2D>(groundPrefix+"_"+slot+".png"));
                    }
                }
                EditorUtility.SetDirty(mat);result[record.name]=mat;result[filename]=mat;
            }
            return result;
        }
        private static void ConfigureOriginalFoliageTextures()
        {
            string folder=Root+"/Art/Textures/OriginalFoliage/";
            foreach(string kind in new[]{"BaseColor","Normal","Roughness"})
            {
                string path=folder+"RuinsV2_Leaf_"+kind+".png";
                if(!File.Exists(path))continue;
                var importer=AssetImporter.GetAtPath(path) as TextureImporter;
                if(importer==null){AssetDatabase.ImportAsset(path,ImportAssetOptions.ForceSynchronousImport);importer=(TextureImporter)AssetImporter.GetAtPath(path);}
                bool color=kind=="BaseColor",normal=kind=="Normal";
                bool changed=importer.sRGBTexture!=color||importer.textureType!=(normal?TextureImporterType.NormalMap:TextureImporterType.Default)||
                    importer.alphaIsTransparency!=color||importer.mipMapsPreserveCoverage!=color||!importer.mipmapEnabled||
                    importer.maxTextureSize!=2048||Mathf.Abs(importer.alphaTestReferenceValue-.35f)>.0001f||importer.wrapMode!=TextureWrapMode.Clamp;
                if(!changed)continue;
                importer.sRGBTexture=color;importer.textureType=normal?TextureImporterType.NormalMap:TextureImporterType.Default;
                importer.alphaIsTransparency=color;importer.mipmapEnabled=true;importer.mipMapsPreserveCoverage=color;
                importer.alphaTestReferenceValue=.35f;importer.maxTextureSize=2048;importer.wrapMode=TextureWrapMode.Clamp;
                importer.filterMode=FilterMode.Trilinear;importer.anisoLevel=4;importer.SaveAndReimport();
            }
        }
        private static Material BackgroundMist(bool floor)
        {
            var mat=LoadMaterial(Root+"/Art/Materials/"+(floor?"ReviewChasm":"ReviewValleyMist")+".mat",Shader.Find("Deinosavros/Map Mist Surface"));
            mat.SetColor("_BaseColor",floor?new Color(.20f,.27f,.30f,.94f):new Color(.38f,.45f,.46f,.42f));
            mat.SetColor("_HighlightColor",new Color(.55f,.59f,.58f,1));
            mat.SetFloat("_Density",floor?1.35f:.50f);mat.SetFloat("_Coverage",floor?.96f:.65f);
            mat.SetFloat("_NoiseScale",.035f);mat.SetFloat("_EdgeSoftness",.3f);mat.SetFloat("_SoftDepth",4);
            mat.SetFloat("_Ceiling",floor?-5:-2);mat.SetFloat("_HeightFade",2);
            mat.SetVector("_FlowA",new Vector4(.0018f,.0006f,0,0));mat.SetVector("_FlowB",new Vector4(-.0009f,.0012f,0,0));
            mat.renderQueue=floor?2990:3000;EditorUtility.SetDirty(mat);return mat;
        }
        private static Material StateOutlineMaterial()
        {
            var mat=LoadMaterial(Root+"/Art/Materials/ReviewNodeOutline.mat",Shader.Find("Universal Render Pipeline/Unlit"));
            mat.SetColor("_BaseColor",Color.white);mat.SetFloat("_Surface",0);mat.SetFloat("_AlphaClip",0);
            mat.renderQueue=-1;EditorUtility.SetDirty(mat);return mat;
        }
        internal static void ConfigureModel(string path)
        {
            AssetDatabase.ImportAsset(path,ImportAssetOptions.ForceSynchronousImport);
            if(AssetImporter.GetAtPath(path) is not ModelImporter importer)throw new InvalidOperationException("Missing model importer "+path);
            importer.globalScale=1;importer.useFileScale=true;importer.importCameras=false;importer.importLights=false;
            importer.importAnimation=false;importer.importBlendShapes=false;importer.preserveHierarchy=true;
            importer.isReadable=false;importer.materialImportMode=ModelImporterMaterialImportMode.ImportStandard;
            importer.SaveAndReimport();
        }
        private static MapReviewCardArt[] MakeArtwork(CardDefinition[] cards)
        {
            return cards.Select(c=>new MapReviewCardArt{definition=c,front=StaticArt(c.combatPrefab,c.cardId+"_Front"),back=StaticArt(c.backPrefab,c.cardId+"_Back"),faceArtwork=ExistingFace(c),frontContentBounds=OriginalCardBounds(c,false),backContentBounds=OriginalCardBounds(c,true)}).ToArray();
        }
        private static Rect OriginalCardBounds(CardDefinition card,bool back)
        {
            // Measured dominant connected alpha component. Source artwork is unchanged.
            Rect N(float x,float y,float w,float h,float sw,float sh)=>new Rect(x/sw,y/sh,w/sw,h/sh);
            if(back)return card.name switch
            {
                "TidalWave"=>N(99,83,583,938,780,1104),
                "SolarShield"=>N(98,83,585,942,780,1108),
                _=>N(99,83,588,946,784,1112)
            };
            return card.name switch
            {
                "TidalWave"=>N(20,20,1385,2224,1448,2272),
                "FleetFootwork"=>N(27,22,1382,2221,1428,2256),
                "SolarShield"=>N(36,9,1391,2237,1448,2260),
                _=>N(99,83,588,946,784,1112)
            };
        }
        private static Sprite ExistingFace(CardDefinition card)
        {
            string file=card.name switch { "TidalWave"=>"TIDALWAVE 1", "FleetFootwork"=>"FLEETFOOT 1", "SolarShield"=>"SOLARSHIELD 1", _=>null };
            return file==null?null:AssetDatabase.LoadAllAssetsAtPath("Assets/Sprites/Card Faces/"+file+".png").OfType<Sprite>().FirstOrDefault();
        }
        private static GameObject StaticArt(GameObject source,string name)
        {
            if(source==null)return null;
            var temporary=new GameObject("Inactive art preparation");temporary.SetActive(false);
            var copy=Object.Instantiate(source,temporary.transform,false);copy.name=name;
            foreach(var behaviour in copy.GetComponentsInChildren<MonoBehaviour>(true))
                if(behaviour is not UIBehaviour)Object.DestroyImmediate(behaviour);
            foreach(var selectable in copy.GetComponentsInChildren<Selectable>(true))Object.DestroyImmediate(selectable);
            foreach(var graphic in copy.GetComponentsInChildren<Graphic>(true))graphic.raycastTarget=false;
            foreach(var canvas in copy.GetComponentsInChildren<Canvas>(true))Object.DestroyImmediate(canvas);
            copy.SetActive(true);
            string path=Root+"/UI/"+name+".prefab";
            var prefab=PrefabUtility.SaveAsPrefabAsset(copy,path);Object.DestroyImmediate(temporary);return prefab;
        }
        private static void VerifyAssets()
        {
            var volume=AssetDatabase.LoadAssetAtPath<VolumeProfile>(Root+"/ReviewVolume.asset");
            if(volume==null||volume.components.Count!=5||volume.components.Any(c=>c==null||!AssetDatabase.Contains(c)))
                throw new InvalidOperationException("Five persistent review volume subassets required.");
            foreach(string guid in AssetDatabase.FindAssets("t:Material",new[]{Root}))
            {
                var mat=AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(guid));
                if(mat.shader==null)throw new InvalidOperationException("Missing review shader.");
                foreach(var message in ShaderUtil.GetShaderMessages(mat.shader))
                    if(message.severity.ToString()=="Error")throw new InvalidOperationException("Shader error: "+message.message);
                if((mat.shader.name==ReviewSurfaceTextures.ShaderName||mat.shader.name=="Deinosavros/Map Review/Foliage Lit") && mat.GetFloat("_UseTextures")>.5f)
                    foreach(string slot in new[]{"_BaseMap","_NormalMap","_RoughnessMap"})
                        if(mat.GetTexture(slot)==null)throw new InvalidOperationException("Missing original texture: "+mat.name+" "+slot);
            }
            for(int index=0;index<SceneManager.sceneCount;index++)
            {
                var scene=SceneManager.GetSceneAt(index);
                if(!scene.path.StartsWith(Root+"/Scenes/",StringComparison.Ordinal))continue;
                foreach(var renderer in InScene<MeshRenderer>(scene))
                    if(renderer.sharedMaterials.Length==0||renderer.sharedMaterials.Any(m=>m==null))
                        throw new InvalidOperationException("Missing review renderer material: "+renderer.name);
            }
        }
        private static NodeSnapshot Snapshot(MapEncounterNode node)=>Snapshot(node.NodeId,node.transform);
        private static NodeSnapshot Snapshot(MapReviewNode node)=>Snapshot(node.nodeId,node.transform);
        private static NodeSnapshot Snapshot(string id,Transform t)=>new(){id=id,position=t.position,rotation=t.rotation,scale=t.lossyScale,
            colliders=t.GetComponentsInChildren<Collider>(true).Select(ColliderGeometry).ToArray()};
        private static string ColliderGeometry(Collider c)
        {
            string geometry=c switch
            {
                BoxCollider box=>box.center.ToString("F7")+"|"+box.size.ToString("F7"),
                SphereCollider sphere=>sphere.center.ToString("F7")+"|"+sphere.radius.ToString("R",CultureInfo.InvariantCulture),
                CapsuleCollider capsule=>capsule.center.ToString("F7")+"|"+capsule.radius.ToString("R",CultureInfo.InvariantCulture)+"|"+capsule.height.ToString("R",CultureInfo.InvariantCulture)+"|"+capsule.direction,
                MeshCollider mesh=>AssetDatabase.GetAssetPath(mesh.sharedMesh)+"|"+mesh.convex,
                _=>c.bounds.center.ToString("F7")+"|"+c.bounds.size.ToString("F7")
            };
            return c.GetType().Name+"|"+c.transform.position.ToString("F7")+"|"+c.transform.rotation.ToString("F7")+"|"+c.transform.lossyScale.ToString("F7")+"|"+c.enabled+"|"+c.isTrigger+"|"+geometry;
        }
        private static void VerifyOriginalNodes(Scene scene,NodeSnapshot[] originals)
        {
            var nodes=InScene<MapReviewNode>(scene).ToDictionary(n=>n.nodeId);
            foreach(var original in originals)
            {
                if(!nodes.TryGetValue(original.id,out var node))throw new InvalidOperationException("Original node lost: "+original.id);
                var actual=Snapshot(node);
                if(Vector3.Distance(actual.position,original.position)>.00001f||Quaternion.Angle(actual.rotation,original.rotation)>.0001f||Vector3.Distance(actual.scale,original.scale)>.00001f)
                    throw new InvalidOperationException("Original node transform changed: "+original.id);
                if(!actual.colliders.SequenceEqual(original.colliders))throw new InvalidOperationException("Original collider geometry or transforms changed: "+original.id);
            }
        }
        private static void CaptureCamera(Camera camera,string path)
        {
            var rt=new RenderTexture(1920,1080,24,RenderTextureFormat.ARGB32);var old=camera.targetTexture;var active=RenderTexture.active;
            try{camera.targetTexture=rt;camera.Render();RenderTexture.active=rt;var image=new Texture2D(1920,1080,TextureFormat.RGB24,false);image.ReadPixels(new Rect(0,0,1920,1080),0,0);image.Apply();File.WriteAllBytes(path,image.EncodeToPNG());Object.DestroyImmediate(image);}
            finally{camera.targetTexture=old;RenderTexture.active=active;Object.DestroyImmediate(rt);}
        }
        private static void EnsureFolders()
        {
            foreach(string path in new[]{Evidence,Root+"/Scenes",Root+"/UI",Root+"/Art/Materials",Root+"/Docs"})Directory.CreateDirectory(path);
            AssetDatabase.Refresh();
        }
        private static T LoadOrCreate<T>(string path) where T:ScriptableObject
        {var asset=AssetDatabase.LoadAssetAtPath<T>(path);if(asset!=null)return asset;asset=ScriptableObject.CreateInstance<T>();AssetDatabase.CreateAsset(asset,path);return asset;}
        private static Material LoadMaterial(string path,Shader shader)
        {var mat=AssetDatabase.LoadAssetAtPath<Material>(path);if(mat!=null){mat.shader=shader;return mat;}mat=new Material(shader);AssetDatabase.CreateAsset(mat,path);return mat;}
        private static CardDefinition Card(string name)=>AssetDatabase.LoadAssetAtPath<CardDefinition>("Assets/Data/Cards/"+name+".asset");
        private static IEnumerable<T> InScene<T>(Scene scene) where T:Component=>scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<T>(true));
        private static Transform Deep(Transform root,string name)=>root==null?null:root.GetComponentsInChildren<Transform>(true).FirstOrDefault(t=>t.name==name);
        private static void RequireReviewAsset(string path){if(!path.StartsWith(Root+"/",StringComparison.Ordinal)||path.Contains(".."))throw new ArgumentException("Only isolated review assets may be written.");}
        private static string Argument(string name,string fallback){var args=Environment.GetCommandLineArgs();int i=Array.IndexOf(args,name);return i>=0&&i+1<args.Length?args[i+1]:fallback;}
        private static string Hash(string path){using var sha=SHA256.Create();return Convert.ToBase64String(sha.ComputeHash(File.ReadAllBytes(path)));}
        private static void Guard(Action action)
        {
            Directory.CreateDirectory(Evidence);var before=Protected.ToDictionary(p=>p,Hash);
            try
            {
                action();
                foreach(var pair in before)if(Hash(pair.Key)!=pair.Value)throw new InvalidOperationException("Protected file changed: "+pair.Key);
                string previousError=Evidence+"/operation-error.txt";
                if(File.Exists(previousError))File.Move(previousError,Evidence+"/previous-error-"+DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff")+".txt");
                Debug.Log("Map review operation succeeded; protected production files unchanged.");
            }
            catch(Exception ex){File.WriteAllText(Evidence+"/operation-error.txt",ex.ToString());Debug.LogException(ex);throw;}
        }
    }
}
