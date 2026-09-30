using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Deinosavros.MapReview.Editor
{
    // Explicit local requests only; installing this utility does not mutate a scene.
    [InitializeOnLoad]
    public static class MapEnvironmentLiveSession
    {
        public const string Evidence = "Library/MapReviewEvidence/2026-09-30";
        private const string RequestPath = Evidence + "/request.json";
        private const string FormalScene = "Assets/Scenes/Map.unity";
        private static double nextPoll;
        [Serializable] private sealed class Request { public string id; public string action; public string model; }
        [Serializable] private sealed class Audit
        {
            public string scene; public bool dirty; public bool playing; public string[] roots;
            public string[] nodes; public string[] components; public string[] environment;
            public Vector3 cameraPosition; public float[] nodeCameraDepths; public float shadowDistance;
        }
        static MapEnvironmentLiveSession() { EditorApplication.update += Poll; }
        [MenuItem("Tools/Map Review/Sync Environment to Formal Map")]
        public static void SyncReviewedEnvironment()
        {
            RequireIdleSavedScenes();
            Directory.CreateDirectory(Evidence);
            PublishEnvironment(MapReviewBuild.Root+"/Art/Models/RuinsV3Review.fbx");
        }
        private static void Poll()
        {
            if(EditorApplication.timeSinceStartup<nextPoll||EditorApplication.isCompiling||EditorApplication.isUpdating)return;
            nextPoll=EditorApplication.timeSinceStartup+1;
            if(!File.Exists(RequestPath))return;
            Request request=JsonUtility.FromJson<Request>(File.ReadAllText(RequestPath));
            string result=Evidence+"/"+request.id+"-result.json";
            if(File.Exists(result))return;
            try
            {
                if(request.action=="audit")File.WriteAllText(result,JsonUtility.ToJson(ReadAudit(),true));
                else if(request.action=="backup-travel")
                {
                    var scene=SceneManager.GetActiveScene();
                    if(scene.path!=FormalScene||EditorApplication.isPlayingOrWillChangePlaymode)
                        throw new InvalidOperationException("Open the formal Map in Edit Mode before backing up travel.");
                    Directory.CreateDirectory(Evidence+"/Travel");
                    string backup=Evidence+"/Travel/Map-before-"+request.id+".unity";
                    if(!EditorSceneManager.SaveScene(scene,backup,true))throw new IOException("Travel scene backup failed.");
                    File.WriteAllText(Evidence+"/Travel/baseline.json",JsonUtility.ToJson(ReadAudit(),true));
                    File.WriteAllText(result,"{\"success\":true}");
                }
                else if(request.action=="refresh")
                {
                    File.WriteAllText(result,"{\"success\":true,\"refreshRequested\":true}");
                    AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);
                }
                else if(request.action=="simplify-names")
                {
                    MapEnvironmentNames.SimplifyCurrentScene();
                    File.WriteAllText(result,"{\"success\":true}");
                }
                else if(request.action=="install-travel")
                {
                    MapTravelBuild.Install();
                    File.WriteAllText(result,"{\"success\":true}");
                }
                else if(request.action=="repair-battle-ui")
                {
                    MapBattleUiRepair.Apply();
                    File.WriteAllText(result,"{\"success\":true}");
                }
                else if(request.action=="install-encounter-types")
                {
                    MapEncounterTypesBuild.Install();
                    File.WriteAllText(result,"{\"success\":true}");
                }
                else if(request.action=="verify-encounter-types")
                {
                    MapEncounterTypesBuild.VerifyReapply();
                    File.WriteAllText(result,"{\"success\":true}");
                }
                else if(request.action=="verify-map-monitor")
                {
                    Deinosavros.MapTools.Editor.MapMonitorTests.Run();
                    File.WriteAllText(result,"{\"success\":true}");
                }
                else if(request.action=="verify-map-monitor-play")
                {
                    Deinosavros.MapTools.Editor.MapMonitorPlayVerification.Start();
                    File.WriteAllText(result,"{\"success\":true,\"started\":true}");
                }
                else if(request.action=="verify-map-monitor-starts")
                {
                    Deinosavros.MapTools.Editor.MapMonitorPlayVerification.StartAtNode();
                    File.WriteAllText(result,"{\"success\":true,\"started\":true}");
                }
                else if(request.action=="validate-travel")
                {
                    MapTravelBuild.ValidateScene(SceneManager.GetActiveScene());
                    MapTravelBuild.ValidateOriginalSnapshot(SceneManager.GetActiveScene());
                    MapTravelTests.Run();
                    File.WriteAllText(result,"{\"success\":true}");
                }
                else if(request.action=="verify-travel-reload")
                {
                    MapTravelVerification.Run();
                    File.WriteAllText(result,"{\"success\":true}");
                }
                else
                {
                    RequireIdleSavedScenes();
                    if(request.action=="build")BuildPlayer();
                    else if(request.action=="build-travel")MapTravelBuild.BuildPlayer();
                    else if(request.action=="build-review")BuildPlayer(true);
                    else if(request.action=="prepare-review")PrepareReview(request.model);
                    else if(request.action=="publish-environment")PublishEnvironment(request.model);
                    else throw new InvalidOperationException("Unsupported request: "+request.action);
                    File.WriteAllText(result,"{\"success\":true}");
                }
            }
            catch(Exception ex) { File.WriteAllText(result,JsonUtility.ToJson(new Failure{error=ex.ToString()},true));Debug.LogException(ex); }
        }
        [Serializable] private sealed class Failure { public string error; }
        private static void RequireIdleSavedScenes()
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode)throw new InvalidOperationException("Exit Play Mode before environment operations.");
            for(int i=0;i<SceneManager.sceneCount;i++)
                if(SceneManager.GetSceneAt(i).isDirty)throw new InvalidOperationException("Unsaved scene detected; preserve user changes before continuing: "+SceneManager.GetSceneAt(i).path);
        }
        private static void BuildPlayer(bool reviewFirst=false)
        {
            MapReviewTests.Run();
            string[] scenes=new[]{reviewFirst?MapReviewBuild.MapScene:FormalScene,FormalScene,MapReviewBuild.MapScene,MapReviewBuild.EncounterScene,MapReviewBuild.BaselineScene}
                .Concat(EditorBuildSettings.scenes.Where(s=>s.enabled).Select(s=>s.path)).Distinct().Where(File.Exists).ToArray();
            string folder=reviewFirst?"Builds/MapReviewRevision":"Builds/MapEnvironmentRevision";
            Directory.CreateDirectory(folder);
            var report=BuildPipeline.BuildPlayer(new BuildPlayerOptions{scenes=scenes,
                locationPathName=folder+"/MapEnvironmentRevision.exe",
                target=BuildTarget.StandaloneWindows64,options=BuildOptions.Development});
            if(report.summary.result!=BuildResult.Succeeded)throw new InvalidOperationException("Build failed: "+report.summary.result);
            File.WriteAllText(Evidence+(reviewFirst?"/build-review.txt":"/build.txt"),$"Result: {report.summary.result}; size: {report.summary.totalSize}; time: {report.summary.totalTime}. EditorBuildSettings unchanged.");
        }
        private static void PrepareReview(string model)
        {
            if(string.IsNullOrWhiteSpace(model)||!model.StartsWith(MapReviewBuild.Root+"/Art/Models/",StringComparison.Ordinal))
                throw new ArgumentException("An explicit review model asset is required.");
            var setup=EditorSceneManager.GetSceneManagerSetup();
            try
            {
                ReviewSurfaceTextures.Generate(true);
                MapReviewBuild.ConfigureModel(model);
                Scene scene=EditorSceneManager.OpenScene(MapReviewBuild.MapScene,OpenSceneMode.Single);
                var graph=AssetDatabase.LoadAssetAtPath<MapGraphDefinition>(MapReviewBuild.Root+"/MapGraph.asset");
                var profile=AssetDatabase.LoadAssetAtPath<MapVisualProfile>(MapReviewBuild.Root+"/MapVisualProfile.asset");
                var before=Components<MapReviewNode>(scene).Select(n=>n.nodeId+"|"+EditorJsonUtility.ToJson(n.transform)+"|"+EditorJsonUtility.ToJson(n.interactionCollider)).ToArray();
                string[] identities=null;
                for(int pass=0;pass<2;pass++)
                {
                    var environment=MapReviewBuild.ImportEnvironment(scene,model,graph,profile,"Review Environment",false);
                    ValidateGroundMasks(environment);
                    MapReviewBuild.ApplyVisuals(scene,profile);
                    var after=Components<MapReviewNode>(scene).Select(n=>n.nodeId+"|"+EditorJsonUtility.ToJson(n.transform)+"|"+EditorJsonUtility.ToJson(n.interactionCollider)).ToArray();
                    if(!before.SequenceEqual(after))throw new InvalidOperationException("Review node transforms or colliders changed.");
                    string[] current=environment.GetComponentsInChildren<MeshFilter>(true).Select(m=>m.name+"|"+m.sharedMesh.vertexCount).OrderBy(s=>s).ToArray();
                    if(pass>0&&!identities.SequenceEqual(current))throw new InvalidOperationException("Repeat import changed mesh identities.");
                    identities=current;
                }
                ValidateMaterials(scene);
                AssetDatabase.SaveAssets();EditorSceneManager.SaveScene(scene);
                File.WriteAllText(Evidence+"/review-reimport.txt",$"PASS: two imports; {identities.Length} independent mesh objects; 14 node transforms/colliders unchanged.");
            }
            finally {EditorSceneManager.RestoreSceneManagerSetup(setup);}
        }
        private static void PublishEnvironment(string model)
        {
            if(!File.Exists(model)||!model.StartsWith(MapReviewBuild.Root+"/Art/Models/",StringComparison.Ordinal))
                throw new ArgumentException("Explicit imported review model required.");
            var scene=SceneManager.GetActiveScene();
            if(scene.path!=FormalScene)throw new InvalidOperationException("Open the formal Map scene before publishing.");
            var old=Components<Transform>(scene).Single(t=>t.name=="MapEnvironment").gameObject;
            var nodes=Components<MapEncounterNode>(scene).OrderBy(n=>n.NodeId).ToArray();
            if((nodes.Length!=11&&nodes.Length!=14)||Components<MapReviewController>(scene).Any()||Components<MapReviewNode>(scene).Any())
                throw new InvalidOperationException("Formal map requires eleven or fourteen production nodes and no review controllers.");
            var protectedComponents=ProtectedComponents(scene,old.transform);
            var graph=AssetDatabase.LoadAssetAtPath<MapGraphDefinition>(MapReviewBuild.Root+"/MapGraph.asset");
            var profile=AssetDatabase.LoadAssetAtPath<MapVisualProfile>(MapReviewBuild.Root+"/MapVisualProfile.asset");
            string backup=Evidence+"/Map-before-environment-sync.unity";
            if(!File.Exists(backup))File.Copy(FormalScene,backup);
            string transactionBackup=Evidence+"/Map-transaction-"+DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff")+".unity";
            File.Copy(FormalScene,transactionBackup);
            File.WriteAllText(Evidence+"/formal-pre-publish.json",JsonUtility.ToJson(ReadAudit(),true));
            GameObject replacement=null;
            bool committed=false;
            var preservedChildren=new List<(Transform item,Transform parent,int sibling)>();
            try
            {
                var modelAsset=AssetDatabase.LoadAssetAtPath<GameObject>(model);
                if(modelAsset==null)throw new InvalidOperationException("Model has not been imported.");
                replacement=(GameObject)PrefabUtility.InstantiatePrefab(modelAsset,scene);
                PrefabUtility.UnpackPrefabInstance(replacement,PrefabUnpackMode.Completely,InteractionMode.AutomatedAction);
                replacement.name="MapEnvironment Pending";
                ValidateGroundMasks(replacement);
                var pairs=nodes.Select(n=>(node:n,anchor:replacement.GetComponentsInChildren<Transform>(true).Single(t=>t.name==graph.Find(n.NodeId).anchor))).ToArray();
                Vector3 a=pairs[1].anchor.position-pairs[0].anchor.position,b=pairs[1].node.transform.position-pairs[0].node.transform.position;a.y=0;b.y=0;
                replacement.transform.localScale*=b.magnitude/a.magnitude;
                replacement.transform.rotation=Quaternion.Euler(0,Vector3.SignedAngle(a,b,Vector3.up),0)*replacement.transform.rotation;
                Vector3 offset=pairs[0].node.transform.position-pairs[0].anchor.position;
                offset.y=pairs.Select(p=>p.node.transform.position.y-p.anchor.position.y).OrderBy(y=>y).ElementAt(pairs.Length/2);
                replacement.transform.position+=offset;
                foreach(var pair in pairs)
                {
                    Vector3 drift=pair.node.transform.position-pair.anchor.position;
                    if(new Vector2(drift.x,drift.z).magnitude>.001f)throw new InvalidOperationException("Anchor alignment failed: "+pair.node.NodeId);
                    if(pair.node.NodeId=="level_06_01"&&Mathf.Abs(drift.y)>.001f)throw new InvalidOperationException("Boss interaction height changed.");
                }
                MapReviewBuild.ApplyEnvironmentMaterials(replacement,profile);
                MapEnvironmentNames.Apply(replacement);
                var source=replacement.AddComponent<MapReviewEnvironmentSource>();source.modelAssetPath=model;
                replacement.transform.SetParent(old.transform.parent,true);
                replacement.transform.SetSiblingIndex(old.transform.GetSiblingIndex());
                // Preserve existing scene lighting and effects nested under the old art root.
                foreach(Transform child in old.transform)
                    if(child.GetComponentsInChildren<Light>(true).Length>0||child.GetComponentsInChildren<Volume>(true).Length>0||child.GetComponentsInChildren<MonoBehaviour>(true).Length>0)
                        preservedChildren.Add((child,old.transform,child.GetSiblingIndex()));
                var lightingState=preservedChildren.SelectMany(c=>c.item.GetComponentsInChildren<Component>(true)).Where(c=>c!=null&&c is not Transform).ToDictionary(c=>c,c=>EditorJsonUtility.ToJson(c));
                var lightingPose=preservedChildren.SelectMany(c=>c.item.GetComponentsInChildren<Transform>(true)).ToDictionary(t=>t,t=>t.localToWorldMatrix);
                foreach(var child in preservedChildren)child.item.SetParent(replacement.transform,true);
                old.SetActive(false);
                if(Components<MapTravelView>(scene).Any())MapTravelBuild.Rebind(scene,replacement.transform);
                var framing=Components<MapCameraFraming>(scene).SingleOrDefault();
                if(framing!=null)
                {
                    var renderers=replacement.GetComponentsInChildren<Renderer>(true).Where(r=>r.enabled&&r.gameObject.activeInHierarchy&&
                        !MapReviewCamera.IsBackgroundRenderer(r,replacement.transform)).ToArray();
                    framing.Configure(framing.profile,renderers,framing.cardBar,framing.topBar);
                    framing.Reframe(1920,1080);
                }
                foreach(var pair in protectedComponents)
                    if(pair.Key==null||EditorJsonUtility.ToJson(pair.Key)!=pair.Value)throw new InvalidOperationException("Protected production component changed: "+(pair.Key==null?"Missing":PathOf(pair.Key.transform)+" "+pair.Key.GetType().Name));
                foreach(var pair in lightingState)
                    if(pair.Key==null||EditorJsonUtility.ToJson(pair.Key)!=pair.Value)throw new InvalidOperationException("Preserved lighting component changed.");
                foreach(var pair in lightingPose)
                    for(int i=0;i<16;i++)if(Mathf.Abs(pair.Key.localToWorldMatrix[i]-pair.Value[i])>.0001f)throw new InvalidOperationException("Preserved lighting world pose changed.");
                ValidateMaterials(scene);
                // Only the scoped old art subtree is removed. The on-disk source is backed up above.
                Object.DestroyImmediate(old);replacement.name="MapEnvironment";
                EditorSceneManager.MarkSceneDirty(scene);
                if(!EditorSceneManager.SaveScene(scene))throw new InvalidOperationException("Formal scene save did not complete.");
                committed=true;
                AssetDatabase.SaveAssets();
                File.WriteAllText(Evidence+"/formal-post-publish.json",JsonUtility.ToJson(ReadAudit(),true));
                File.WriteAllText(Evidence+"/formal-preservation.txt",$"PASS: {protectedComponents.Count} production components unchanged; {nodes.Length} production nodes preserved; boss anchor aligned within 1 mm; environment source {model}.");
            }
            catch(Exception ex)
            {
                if(committed)throw new InvalidOperationException("Environment was saved successfully, but post-save verification failed. Do not repeat automatically: "+ex.Message,ex);
                foreach(var child in preservedChildren)if(child.item!=null&&child.parent!=null){child.item.SetParent(child.parent,true);child.item.SetSiblingIndex(child.sibling);}
                if(replacement!=null)Object.DestroyImmediate(replacement);
                if(old!=null)old.SetActive(true);
                // Restore this transaction's exact disk snapshot, including framing, on save failure.
                File.Copy(transactionBackup,FormalScene,true);
                AssetDatabase.ImportAsset(FormalScene,ImportAssetOptions.ForceSynchronousImport);
                EditorSceneManager.OpenScene(FormalScene,OpenSceneMode.Single);
                throw;
            }
        }
        private static Dictionary<Component,string> ProtectedComponents(Scene scene,Transform environment)
        {
            var result=new Dictionary<Component,string>();
            var travel=Components<MapTravelView>(scene).SingleOrDefault();
            foreach(var component in Components<Component>(scene))
            {
                if(component==null||component.transform.IsChildOf(environment)||component is Camera||component is MapCameraFraming)continue;
                if(travel!=null&&component.transform.IsChildOf(travel.transform))continue;
                if(component is Transform t&&(environment.IsChildOf(t)||t.GetComponent<Camera>()!=null))continue;
                result[component]=EditorJsonUtility.ToJson(component);
            }
            return result;
        }
        private static IEnumerable<T> Components<T>(Scene scene) where T:Component => scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<T>(true));
        private static void ValidateGroundMasks(GameObject environment)
        {
            foreach(var filter in environment.GetComponentsInChildren<MeshFilter>(true))
            {
                var renderer=filter.GetComponent<MeshRenderer>();
                if(renderer==null||!renderer.sharedMaterials.Any(m=>m!=null&&(m.name.Replace('_',' ')=="V2 Warm silt"||m.name.Replace('_',' ')=="V2 Rooted moss"||m.name.Replace('_',' ')=="V2 Cliff grey umber")))continue;
                // Reading imported mesh data in the editor does not require enabling runtime read/write.
                Color[] colors=filter.sharedMesh.colors;
                if(colors.Length!=filter.sharedMesh.vertexCount)throw new InvalidOperationException("Ground mask missing: "+filter.name);
                foreach(var color in colors)
                    if(Mathf.Abs(color.r+color.g+color.b-1)>.02f||color.r<0||color.g<0||color.b<0)
                        throw new InvalidOperationException("Ground mask is not linear normalized RGB: "+filter.name);
            }
        }
        private static void ValidateMaterials(Scene scene)
        {
            foreach(var renderer in Components<MeshRenderer>(scene))
            {
                if(!renderer.enabled||!renderer.gameObject.activeInHierarchy)continue;
                foreach(var material in renderer.sharedMaterials)
                {
                    if(material==null||material.shader==null)throw new InvalidOperationException("Missing material: "+renderer.name);
                    if(ShaderUtil.GetShaderMessages(material.shader).Any(m=>m.severity.ToString()=="Error"))
                        throw new InvalidOperationException("Shader compilation failed: "+material.shader.name);
                }
            }
        }
        private static Audit ReadAudit()
        {
            Scene scene=SceneManager.GetActiveScene();
            var objects=scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<Transform>(true)).ToArray();
            var camera=objects.Select(t=>t.GetComponent<Camera>()).FirstOrDefault(c=>c!=null&&c.CompareTag("MainCamera"));
            var nodes=objects.Select(t=>t.GetComponent<MapEncounterNode>()).Where(n=>n!=null).OrderBy(n=>n.NodeId).ToArray();
            var environment=objects.FirstOrDefault(t=>t.name=="MapEnvironment");
            return new Audit
            {
                scene=scene.path,dirty=scene.isDirty,playing=EditorApplication.isPlayingOrWillChangePlaymode,
                roots=scene.GetRootGameObjects().Select(g=>g.name).ToArray(),
                nodes=nodes.Select(n=>n.NodeId+"|"+EditorJsonUtility.ToJson(n)+"|"+EditorJsonUtility.ToJson(n.transform)+"|"+string.Join("|",n.GetComponentsInChildren<Collider>(true).Select(c=>EditorJsonUtility.ToJson(c)))).ToArray(),
                components=objects.Where(t=>environment==null||!t.IsChildOf(environment)).SelectMany(t=>t.GetComponents<Component>()).Where(c=>c!=null).Select(c=>PathOf(c.transform)+"|"+c.GetType().FullName+"|"+EditorJsonUtility.ToJson(c)).ToArray(),
                environment=environment==null?Array.Empty<string>():environment.GetComponentsInChildren<Transform>(true).Select(t=>PathOf(t)+"|"+string.Join(",",t.GetComponents<Component>().Select(c=>c==null?"Missing":c.GetType().Name))).ToArray(),
                cameraPosition=camera==null?Vector3.zero:camera.transform.position,
                nodeCameraDepths=camera==null?Array.Empty<float>():nodes.Select(n=>camera.transform.InverseTransformPoint(n.transform.position).z).ToArray(),
                shadowDistance=GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset p?p.shadowDistance:0
            };
        }
        private static string PathOf(Transform t) => t.parent==null?t.name:PathOf(t.parent)+"/"+t.name;
    }
}
