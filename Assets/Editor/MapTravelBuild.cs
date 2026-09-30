using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Deinosavros.MapReview;
using Deinosavros.MapReview.Editor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

public static class MapTravelBuild
{
    public const string Evidence = MapEnvironmentLiveSession.Evidence + "/Travel";
    private static readonly string[] Order = { "01_01", "02_01", "02_03", "02_02", "03_01", "03_02", "03_03", "04_01", "04_03", "04_02", "05_01", "05_03", "05_02", "06_01" };
    private static readonly int[,] Edges = { {0,1},{0,2},{0,3},{1,4},{2,5},{3,6},{2,4},{3,5},{4,7},{5,8},{6,9},{4,8},{5,9},{7,10},{8,11},{9,12},{10,13},{11,13},{12,13} };

    [MenuItem("Tools/Map/Install Fire Seed Travel")]
    public static void Install()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play Mode before installing travel.");
        var scene = SceneManager.GetActiveScene();
        if (scene.path != "Assets/Scenes/Map.unity") throw new InvalidOperationException("Open the formal Map scene first.");
        Directory.CreateDirectory(Evidence);
        string backup = Evidence + "/Map-before-install-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff") + ".unity";
        if (!EditorSceneManager.SaveScene(scene, backup, true)) throw new IOException("Could not back up the active Map.");
        var originalNodes = Objects<MapEncounterNode>(scene).ToArray();
        var nodePoses = originalNodes.ToDictionary(n => n, n => EditorJsonUtility.ToJson(n.transform));
        var colliders = originalNodes.SelectMany(n => n.GetComponentsInChildren<Collider>(true)).ToDictionary(c => c, c => EditorJsonUtility.ToJson(c));
        var hud = Objects<RectTransform>(scene).ToDictionary(t => t, t => EditorJsonUtility.ToJson(t));
        var environment = Objects<Transform>(scene).Single(t => t.name == "MapEnvironment");
        var sourceMeshes = environment.GetComponentsInChildren<MeshFilter>(true).ToDictionary(m => m, m => EditorJsonUtility.ToJson(m));
        var sourcePoses = environment.GetComponentsInChildren<Transform>(true).ToDictionary(t => t, t => EditorJsonUtility.ToJson(t));
        MapEnvironmentNames.Apply(environment.gameObject, true);
        var profile = MapTravelArt.CreateAssets();
        Rebind(scene, environment, profile);
        ValidateScene(scene);
        foreach (var pair in nodePoses) Require(EditorJsonUtility.ToJson(pair.Key.transform) == pair.Value, "An existing node transform changed.");
        foreach (var pair in colliders) Require(EditorJsonUtility.ToJson(pair.Key) == pair.Value, "An existing collider changed.");
        foreach (var pair in hud) Require(EditorJsonUtility.ToJson(pair.Key) == pair.Value, "The card or HUD layout changed.");
        foreach (var pair in sourceMeshes)
            Require(pair.Key.name=="Path_17"||pair.Key.name=="Path_19"||EditorJsonUtility.ToJson(pair.Key)==pair.Value,"An unapproved environment mesh reference changed.");
        foreach (var pair in sourcePoses) Require(EditorJsonUtility.ToJson(pair.Key) == pair.Value, "The environment layout changed.");
        ValidateOriginalSnapshot(scene);
        MapTravelTests.Run();
        EditorSceneManager.MarkSceneDirty(scene);
        if (!EditorSceneManager.SaveScene(scene)) throw new IOException("The Map travel scene could not be saved.");
        File.WriteAllText(Evidence + "/install.txt", $"PASS: 14 nodes, 19 authored routes; preserved {originalNodes.Length} node transforms, {colliders.Count} colliders, {hud.Count} UI transforms and {sourceMeshes.Count} independent environment meshes. Only the two approved Boss approach mesh references changed. Backup: {backup}");
    }
    public static void Rebind(Scene scene, Transform environment, MapTravelProfile supplied = null)
    {
        var controller = Objects<MapController>(scene).Single();
        var graph = AssetDatabase.LoadAssetAtPath<MapGraphDefinition>("Assets/MapReview/MapGraph.asset"); graph.Validate();
        var root = Objects<Transform>(scene).Single(t => t.name == "MapRoot");
        var routeRoot = root.Find("MapRoute");
        var nodes = Objects<MapEncounterNode>(scene).ToDictionary(n => n.NodeId);
        foreach (var definition in graph.nodes)
        {
            if (nodes.ContainsKey(definition.id)) continue;
            var anchor = environment.GetComponentsInChildren<Transform>(true).Single(t => t.name == definition.anchor);
            var layer = routeRoot.Find($"Level_{definition.layer:00}");
            if (layer == null) { layer = new GameObject($"Level_{definition.layer:00}").transform; layer.SetParent(routeRoot, false); }
            var go = new GameObject("Node_L" + definition.id.Substring(6)); go.transform.SetParent(layer, false); go.transform.position = anchor.position;
            var collider = go.AddComponent<BoxCollider>(); collider.size = new Vector3(1.4f,1.4f,1.4f); collider.center = new Vector3(0,.7f,0); collider.enabled = false;
            var node = go.AddComponent<MapEncounterNode>(); node.Configure(definition.id, definition.layer, controller, collider); nodes.Add(definition.id,node);
        }
        var travelRoot = root.Find("Travel");
        if (travelRoot == null) { travelRoot = new GameObject("Travel").transform; travelRoot.SetParent(root,false); }
        var view = GetOrAdd<MapTravelView>(travelRoot.gameObject);
        view.environment = environment; view.profile = supplied ?? AssetDatabase.LoadAssetAtPath<MapTravelProfile>(MapTravelArt.Root + "/MapTravelProfile.asset");
        if (view.profile == null) throw new InvalidOperationException("Travel profile is missing.");
        var filters = environment.GetComponentsInChildren<MeshFilter>(true);
        var sites = new MapTravelSite[Order.Length];
        var surfaces = new Surface[Order.Length];
        for (int i = 0; i < Order.Length; i++)
        {
            var node = nodes["level_" + Order[i]];
            var filter = i < 13 ? filters.Single(f => f.name == $"Platform_{i+1:00}" || f.name == $"Node terrace {i:00}") : null;
            var surfacesForNode = filter != null ? new[]{filter} : filters.Where(f => f.name == "Landing" || f.name == "Base_01" || f.name == "Stairs").ToArray();
            surfaces[i] = new Surface(surfacesForNode); Vector3 point = node.transform.position;
            if (surfaces[i].Height(point, out float y)) point.y = y;
            sites[i] = new MapTravelSite { nodeId = node.NodeId, point = environment.InverseTransformPoint(point) };
        }
        view.sites = sites;
        var stairSurface = new Surface(filters.Where(f => f.name == "Stairs" || f.name == "Landing" || f.name == "Base_01"));
        var stairFilter=filters.Single(f=>f.name=="Stairs");
        var islandSurface=new Surface(filters.Where(f=>f.name=="Island"));
        Vector3 centralStart=environment.TransformPoint(sites[11].point),bossFloor=environment.TransformPoint(sites[13].point);
        Vector3 stairAxis=bossFloor-centralStart;stairAxis.y=0;stairAxis.Normalize();
        Vector3 stairEntry=MapTravelBossRoads.StairEntry(stairFilter,bossFloor,stairAxis);
        var routes = travelRoot.Find("Routes"); if (routes == null) { routes = new GameObject("Routes").transform; routes.SetParent(travelRoot,false); }
        var paths = new MapTravelPath[Edges.GetLength(0)];
        for (int i = 0; i < paths.Length; i++)
        {
            int a = Edges[i,0], b = Edges[i,1]; string from = sites[a].nodeId, to = sites[b].nodeId;
            Require(graph.Find(from).next.Contains(to), "An authored road is not a graph edge.");
            var child = routes.Find($"R{i+1:00}"); if (child == null) { child = new GameObject($"R{i+1:00}").transform; child.SetParent(routes,false); }
            var path = GetOrAdd<MapTravelPath>(child.gameObject);
            path.fromNodeId = from; path.toNodeId = to; path.space = environment;
            path.road = filters.Single(f => f.name == $"Path_{i+1:00}" || f.name == $"Paved route {i:00}");
            Vector3 start = environment.TransformPoint(sites[a].point), end = environment.TransformPoint(sites[b].point);
            Vector3[] guide=new[]{start,end};
            if(b==13&&a!=11)
                guide=MapTravelBossRoads.Apply(path,start,end,centralStart,stairFilter,p=>islandSurface.Height(p,out float y)?y:start.y-.05f);
            var road = new Surface(new[]{path.road});
            if(path.manualControlPoints&&path.points.Length>=3){path.Prepare();paths[i]=path;EditorUtility.SetDirty(path);continue;}
            var baked=new List<Vector3>();float previousY=start.y;
            for(int segment=0;segment<guide.Length-1;segment++)
            {
                int count=Mathf.Max(1,Mathf.CeilToInt(Vector3.Distance(guide[segment],guide[segment+1])/.14f));
                for(int p=0;p<count;p++)
                {
                    Vector3 point=Vector3.Lerp(guide[segment],guide[segment+1],p/(float)count);
                    bool hit=road.Height(point,out float y);
                    if(!hit)hit=surfaces[a].Height(point,out y);
                    if(!hit)hit=surfaces[b].Height(point,out y);
                    if(islandSurface.Height(point,out float terrainY)){if(!hit||terrainY>y)y=terrainY;hit=true;}
                    if(b==13&&stairSurface.Height(point,out float stairY)){if(!hit||stairY>y)y=stairY;hit=true;}
                    if(hit)point.y=y;
                    if(b==13&&Vector3.Dot(point-stairEntry,stairAxis)>-.02f)point.y=Mathf.Max(point.y,previousY);
                    previousY=point.y;baked.Add(environment.InverseTransformPoint(point));
                }
            }
            baked.Add(sites[b].point);path.points=baked.ToArray();
            path.points[0] = sites[a].point; path.points[^1] = sites[b].point;
            path.Prepare(); paths[i] = path; EditorUtility.SetDirty(path);
        }
        view.paths = paths;
        var portal = filters.FirstOrDefault(f => f.name == "PortalFX" || f.name == "Portal - energy veil");
        Require(portal != null, "The Boss portal veil is missing.");
        view.portalRenderer = portal.GetComponent<Renderer>();
        view.portalFocus = environment.InverseTransformPoint(portal.transform.TransformPoint(portal.sharedMesh.bounds.center));
        controller.ConfigureTravel(graph,view,graph.nodes.Select(n=>nodes[n.id]).ToArray());
        foreach (var renderer in nodes["level_01_01"].GetComponentsInChildren<Renderer>(true)) renderer.enabled = false;
        CreatePreview(view);
        var camera = Objects<Camera>(scene).FirstOrDefault(c=>c.CompareTag("MainCamera"));
        if (camera != null) camera.GetUniversalAdditionalCameraData().requiresDepthTexture = true;
        EditorUtility.SetDirty(view); EditorUtility.SetDirty(controller);
    }
    private static void CreatePreview(MapTravelView view)
    {
        var camera = Camera.main;
        Vector3 floor = view.environment.TransformPoint(view.sites[0].point), point = floor + Vector3.up*view.profile.hoverHeight;
        float depth = camera != null ? Vector3.Dot(point-camera.transform.position,camera.transform.forward) : 50;
        float scale = camera != null ? 2*depth*Mathf.Tan(camera.fieldOfView*Mathf.Deg2Rad*.5f)/1080 : .04f;
        if (camera != null) point += (camera.transform.right * view.profile.dockPixels.x + camera.transform.up * view.profile.dockPixels.y) * scale;
        PreviewQuad(view,"Core",view.profile.symbolMaterial,point,camera!=null?camera.transform.rotation:Quaternion.identity,scale*view.profile.corePixels);
        view.transform.Find("Core").localScale = new Vector3(scale * view.profile.corePixels, scale * view.profile.coreHeightPixels, 1);
        PreviewQuad(view,"Halo",view.profile.glowMaterial,point,camera!=null?camera.transform.rotation:Quaternion.identity,scale*view.profile.haloPixels);
        var sigil = AssetDatabase.LoadAssetAtPath<Material>(MapTravelArt.Root+"/Sigil.mat");
        PreviewQuad(view,"Sigil",sigil,floor+Vector3.up*.035f,Quaternion.Euler(90,0,0),view.profile.sigilRadius*2);
        if(view.transform.Find("Trail")==null) new GameObject("Trail").transform.SetParent(view.transform,false);
    }
    private static void PreviewQuad(MapTravelView view,string name,Material material,Vector3 point,Quaternion rotation,float size)
    {
        var child=view.transform.Find(name);if(child==null){child=new GameObject(name).transform;child.SetParent(view.transform,false);}
        child.SetPositionAndRotation(point,rotation);child.localScale=Vector3.one*size;
        var filter=GetOrAdd<MeshFilter>(child.gameObject);filter.sharedMesh=view.profile.quadMesh;
        var renderer=GetOrAdd<MeshRenderer>(child.gameObject);renderer.sharedMaterial=material;
        renderer.shadowCastingMode=ShadowCastingMode.Off;renderer.receiveShadows=false;
    }
    public static void ValidateScene(Scene scene)
    {
        var nodes=Objects<MapEncounterNode>(scene).ToArray();Require(nodes.Length==14&&nodes.Select(n=>n.NodeId).Distinct().Count()==14,"Expected fourteen distinct production nodes.");
        var view=Objects<MapTravelView>(scene).Single();Require(view.paths.Length==19&&view.sites.Length==14,"Travel route/site count is incorrect.");
        Require(view.profile.nodeInformation != null && view.profile.nodeInformation.nodes.Length == 14,
            "Fourteen explicit node presentation entries are required.");
        Require(view.profile.nodeInformation.nodes.Select(n => n.nodeId).Distinct().Count() == 14 &&
            nodes.All(n => view.profile.nodeInformation.Find(n.NodeId) != null), "Node presentation IDs must match the route graph.");
        var edges=new HashSet<string>();
        foreach(var path in view.paths)
        {
            Require(path.road!=null&&path.space==view.environment&&path.points.Length>=3,"A road binding is invalid.");
            Require(edges.Add(path.fromNodeId+">"+path.toNodeId),"Duplicate authored edge.");path.Prepare();
            Require(path.Length>.1f,"Zero-length route.");
            Require(Vector3.Distance(path.Sample(0),view.SitePoint(path.fromNodeId))<.001f&&Vector3.Distance(path.Sample(1),view.SitePoint(path.toNodeId))<.001f,"Route endpoints are not aligned.");
            foreach(var point in path.points) Require(float.IsFinite(point.x)&&float.IsFinite(point.y)&&float.IsFinite(point.z),"A path point is not finite.");
        }
        Require(Objects<Transform>(scene).Count(t=>t.name=="Travel")==1,"Duplicate Travel root.");
        foreach(var material in new[]{view.profile.symbolMaterial,view.profile.glowMaterial})
            Require(material!=null&&!ShaderUtil.GetShaderMessages(material.shader).Any(m=>m.severity.ToString()=="Error"),"Travel shader failed to compile.");
        var stairsFilter=view.environment.GetComponentsInChildren<MeshFilter>(true).Single(f=>f.name=="Stairs");
        var stairs=new Surface(new[]{stairsFilter});
        var lines=new List<string>{"route,length,samples,stair_samples,maximum_height_change"};
        foreach(var path in view.paths)
        {
            int stairHits=0;float maximum=0;
            for(int i=0;i<path.points.Length;i++)
            {
                Vector3 point=path.space.TransformPoint(path.points[i]);
                if(stairs.Height(point,out _))stairHits++;
                if(i>0)maximum=Mathf.Max(maximum,Mathf.Abs(point.y-path.space.TransformPoint(path.points[i-1]).y));
            }
            if(path.toNodeId=="level_06_01")Require(stairHits>=6&&maximum<.36f,"Boss route misses the stairs or has a vertical jump: "+path.name);
            lines.Add(FormattableString.Invariant($"{path.name},{path.Length:F4},{path.points.Length},{stairHits},{maximum:F4}"));
        }
        lines.Add("Stair bounds: "+stairsFilter.GetComponent<Renderer>().bounds);
        Directory.CreateDirectory(Evidence);File.WriteAllLines(Evidence+"/road-height-audit.csv",lines);
    }
    public static bool GroundHeight(MapTravelPath path,Vector3 world,out float height)
    {
        var filters=path.space.GetComponentsInChildren<MeshFilter>(true).Where(f=>f.name=="Island"||f.name.StartsWith("Platform_")||f==path.road||f.name=="Stairs"||f.name=="Base_01"||f.name=="Landing");
        return new Surface(filters).Height(world,out height);
    }
    [Serializable] private sealed class OriginalSnapshot { public string[] nodes, components; }
    public static void ValidateOriginalSnapshot(Scene scene)
    {
        string file=Evidence+"/baseline.json";
        if(!File.Exists(file))return;
        var snapshot=JsonUtility.FromJson<OriginalSnapshot>(File.ReadAllText(file));
        var nodes=Objects<MapEncounterNode>(scene).ToDictionary(n=>n.NodeId);
        foreach(string entry in snapshot.nodes)
        {
            var parts=entry.Split('|');var node=nodes[parts[0]];
            Require(EditorJsonUtility.ToJson(node.transform)==parts[2],"A baseline node transform changed: "+parts[0]);
            string colliders=string.Join("|",node.GetComponentsInChildren<Collider>(true).Select(c=>EditorJsonUtility.ToJson(c)));
            Require(colliders==string.Join("|",parts.Skip(3)),"A baseline collider changed: "+parts[0]);
        }
        var rectangles=Objects<RectTransform>(scene).ToDictionary(t=>HierarchyPath(t));int count=0;
        foreach(string entry in snapshot.components)
        {
            var parts=entry.Split('|');if(parts[1]!=typeof(RectTransform).FullName)continue;
            Require(rectangles.TryGetValue(parts[0],out var rectangle)&&EditorJsonUtility.ToJson(rectangle)==parts[2],"The baseline HUD layout changed: "+parts[0]);count++;
        }
        File.WriteAllText(Evidence+"/baseline-preservation.txt",$"PASS: all {snapshot.nodes.Length} original node transforms and collider payloads, {count} existing UI rectangles match the pre-install live scene snapshot.");
    }
    private static string HierarchyPath(Transform item)=>item.parent==null?item.name:HierarchyPath(item.parent)+"/"+item.name;
    public static void BuildPlayer()
    {
        ValidateScene(SceneManager.GetActiveScene());ValidateOriginalSnapshot(SceneManager.GetActiveScene());MapTravelTests.Run();
        Directory.CreateDirectory("Builds/MapTravel");
        var report=UnityEditor.BuildPipeline.BuildPlayer(new UnityEditor.BuildPlayerOptions
        {
            scenes=MapEncounterTypesBuild.Scenes.Where(File.Exists).ToArray(),
            target=UnityEditor.BuildTarget.StandaloneWindows64,locationPathName="Builds/MapTravel/MapTravel.exe",
            options=UnityEditor.BuildOptions.Development
        });
        Require(report.summary.result==UnityEditor.Build.Reporting.BuildResult.Succeeded,"Travel validation player build failed.");
        File.WriteAllText(Evidence+"/build.txt",$"PASS: {report.summary.result}; {report.summary.totalSize} bytes; {report.summary.totalTime}. EditorBuildSettings unchanged.");
    }
    private static IEnumerable<T> Objects<T>(Scene scene) where T:Component => scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<T>(true));
    private static T GetOrAdd<T>(GameObject target) where T:Component
    {
        var component=target.GetComponent<T>();
        return component!=null?component:target.AddComponent<T>();
    }
    private static void Require(bool condition,string message){if(!condition)throw new InvalidOperationException(message);}

    private sealed class Surface
    {
        private readonly List<(Vector3 a,Vector3 b,Vector3 c)> faces=new();
        public Surface(IEnumerable<MeshFilter> filters)
        {
            foreach(var filter in filters)
            {
                var mesh=filter.sharedMesh;if(mesh==null)continue;
                var vertices=mesh.vertices;var indices=mesh.triangles;
                for(int i=0;i<indices.Length;i+=3)
                {
                    var a=filter.transform.TransformPoint(vertices[indices[i]]);var b=filter.transform.TransformPoint(vertices[indices[i+1]]);var c=filter.transform.TransformPoint(vertices[indices[i+2]]);
                    var normal=Vector3.Cross(b-a,c-a).normalized;if(normal.y>.35f)faces.Add((a,b,c));
                }
            }
        }
        public bool Height(Vector3 point,out float height)
        {
            height=float.NegativeInfinity;bool found=false;
            foreach(var (a,b,c) in faces)
            {
                float det=(b.z-c.z)*(a.x-c.x)+(c.x-b.x)*(a.z-c.z);if(Mathf.Abs(det)<.000001f)continue;
                float u=((b.z-c.z)*(point.x-c.x)+(c.x-b.x)*(point.z-c.z))/det;
                float v=((c.z-a.z)*(point.x-c.x)+(a.x-c.x)*(point.z-c.z))/det;
                if(u<-.0001f||v<-.0001f||u+v>1.0001f)continue;
                height=Mathf.Max(height,u*a.y+v*b.y+(1-u-v)*c.y);found=true;
            }
            return found;
        }
    }
}
