using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using TMPro;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Deinosavros.MapReview
{
    // Opt-in only: present in a review build, inert unless the explicit command-line flag is supplied.
    public sealed class MapReviewHarness : MonoBehaviour
    {
        private readonly List<string> report=new();
        private readonly List<string> errors=new();
        private readonly List<float> frameTimes=new();
        private readonly List<Mouse> suspendedPointers=new();
        private readonly List<BenchmarkCounter> counters=new();
        private readonly FrameTiming[] timings=new FrameTiming[1];
        private readonly List<double> gpuTimes=new(),cpuMainTimes=new(),cpuRenderTimes=new(),presentWaitTimes=new();
        private bool timingFeature;
        private ulong lastTimingTimestamp;
        private string output;
        private bool sampling;
        private bool finished;
        private double watchdog;
        private Mouse mouse;
        private InputSettings originalInputSettings,testInputSettings;
        private Vector2 requestedPointer;
        private ushort requestedButtons;
        private int queuedPointerEvents,observedPointerEvents;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void StartWhenRequested()
        {
            if(!Environment.GetCommandLineArgs().Contains("-reviewValidation"))return;
            if(FindFirstObjectByType<MapReviewHarness>()!=null)return;
            var obj=new GameObject("Opt-in review verification");DontDestroyOnLoad(obj);obj.AddComponent<MapReviewHarness>();
        }
        private void Start()
        {
            output=Argument("-reviewOutput",Path.Combine(Application.dataPath,"../MapReviewEvidence"));Directory.CreateDirectory(output);
            Application.logMessageReceived+=Log;Application.runInBackground=true;
            QualitySettings.vSyncCount=0;Application.targetFrameRate=-1;
            watchdog=Time.realtimeSinceStartupAsDouble+900;
            StartCoroutine(Guarded(Run()));
        }
        private void Update()
        {
            if(sampling){frameTimes.Add(Time.unscaledDeltaTime*1000);SamplePerformanceCounters();}
            if(Time.realtimeSinceStartupAsDouble>watchdog){errors.Add("Validation watchdog expired.");Finish(1);}
        }
        private void Log(string message,string stack,LogType type){if(type==LogType.Error||type==LogType.Exception||type==LogType.Assert)errors.Add(message+"\n"+stack);}
        private IEnumerator Guarded(IEnumerator routine)
        {
            var stack=new Stack<IEnumerator>();
            stack.Push(routine);
            while(stack.Count>0)
            {
                object current;
                try
                {
                    if(!stack.Peek().MoveNext()){(stack.Pop() as IDisposable)?.Dispose();continue;}
                    current=stack.Peek().Current;
                }
                catch(Exception ex)
                {
                    errors.Add(ex.ToString());
                    while(stack.Count>0)
                    {
                        try{(stack.Pop() as IDisposable)?.Dispose();}
                        catch(Exception cleanup){errors.Add("Validation cleanup failed: "+cleanup);}
                    }
                    Finish(1);yield break;
                }
                if(current is IEnumerator nested)stack.Push(nested);
                else yield return current;
            }
            Finish(errors.Count==0?0:1);
        }
        private IEnumerator Run()
        {
            SetupPointer();Move(AwayPoint());
            yield return ValidatePointerDelivery("Initial synthetic pointer");
            string target=Argument("-reviewCaptureScene",SceneManager.GetActiveScene().name);
            bool captureOnly=Environment.GetCommandLineArgs().Contains("-reviewCaptureOnly")||target=="Map";
            if(SceneManager.GetActiveScene().name!=target)yield return SceneManager.LoadSceneAsync(target);
            Screen.SetResolution(1280,720,FullScreenMode.Windowed);
            yield return new WaitForSecondsRealtime(2);
            Screen.SetResolution(1920,1080,FullScreenMode.Windowed);
            yield return new WaitForSecondsRealtime(6);
            Move(AwayPoint());yield return ValidatePointerDelivery("Neutral baseline pointer");
            yield return new WaitForSecondsRealtime(.8f);
            var baselineUi=FindFirstObjectByType<MapReviewController>();
            if(!captureOnly&&baselineUi!=null)Check(!baselineUi.ModalOpen&&!baselineUi.DetailVisible&&baselineUi.CardViews.All(card=>card.FlipProgress<.001f),
                "The initial screenshot has neutral cards without physical-pointer hover");
            report.Add("Scene: "+target);report.Add("GPU: "+SystemInfo.graphicsDeviceName);report.Add("CPU: "+SystemInfo.processorType);
            report.Add("Development standalone player; uncapped; no editor frame timings.");
            if(captureOnly)
            {
                report.Add("Capture-only mode: keep the loaded scene's own UI and controllers; do not run review state, input interaction, or benchmark workflows.");
                yield return CaptureSceneOnly(target);
                yield break;
            }
            yield return Capture("01-1920x1080-ui");
            if(target!="MapBaselineReview")
            {
                Check(FindObjectsByType<RunSession>(FindObjectsSortMode.None).Length==0,"No legacy RunSession in review runtime");
                Check(FindObjectsByType<MapController>(FindObjectsSortMode.None).Length==0,"No legacy MapController in review runtime");
                Check(FindObjectsByType<BattleRunBridge>(FindObjectsSortMode.None).Length==0,"No legacy battle bridge in review runtime");
                foreach(var size in new[]{new Vector2Int(1920,1080),new Vector2Int(2560,1440),new Vector2Int(1920,1200)})
                {
                    Screen.SetResolution(size.x,size.y,FullScreenMode.Windowed);yield return new WaitForSecondsRealtime(2);
                    var framing=Camera.main.GetComponent<MapReviewCamera>();framing.Reframe();
                    yield return null;
                    Check(Screen.width==size.x&&Screen.height==size.y,$"Requested resolution {size.x}x{size.y} applied");
                    ValidateScene(framing);
                    yield return Capture($"02-{size.x}x{size.y}-layout");
                }
                Screen.SetResolution(1920,1080,FullScreenMode.Windowed);yield return new WaitForSecondsRealtime(2);
                if(Environment.GetCommandLineArgs().Contains("-reviewUiTests"))
                {
                    yield return UiTests();
                }
            }
            float seconds=float.Parse(Argument("-reviewBenchmarkSeconds","0"),CultureInfo.InvariantCulture);
            if(seconds>0)
            {
                if(target!="MapBaselineReview")
                {
                    if(SceneManager.GetActiveScene().name!=target)yield return SceneManager.LoadSceneAsync(target);
                    yield return WaitForMap(target);
                    var ui=FindFirstObjectByType<MapReviewController>();
                    ui.BeginNewAdventure();
                    SetupPointer();Move(AwayPoint());
                    yield return new WaitForSecondsRealtime(1);
                    Check(SceneManager.GetActiveScene().name==target&&ui.State.Phase==MapRunPhase.OnMap&&
                        !ui.ModalOpen&&!ui.DetailVisible&&ui.CardViews.All(card=>card.FlipProgress<.001f),"Benchmark begins on a fresh neutral map, not the encounter receiver");
                    yield return Capture("12-neutral-before-benchmark");
                }
                watchdog=Math.Max(watchdog,Time.realtimeSinceStartupAsDouble+seconds+180);
                StartPerformanceCounters();
                yield return new WaitForSecondsRealtime(15);
                frameTimes.Clear();sampling=true;double started=Time.realtimeSinceStartupAsDouble;
                while(Time.realtimeSinceStartupAsDouble-started<seconds)yield return null;
                sampling=false;
                Check(frameTimes.Count>0,"Standalone benchmark recorded rendered frames");
                var sorted=frameTimes.OrderBy(value=>value).ToArray();
                float Percentile(float p)=>sorted[Mathf.Clamp(Mathf.CeilToInt(sorted.Length*p)-1,0,sorted.Length-1)];
                report.Add($"Measured seconds: {Time.realtimeSinceStartupAsDouble-started:F2}; samples: {frameTimes.Count}");
                report.Add($"Frame ms mean {frameTimes.Average():F3}; median {Percentile(.5f):F3}; p95 {Percentile(.95f):F3}; p99 {Percentile(.99f):F3}; max {sorted.Last():F3}");
                report.Add($"Frames above 16.67 ms: {frameTimes.Count(f=>f>16.667f)} ({100.0*frameTimes.Count(f=>f>16.667f)/frameTimes.Count:F2}%); mean FPS {1000/frameTimes.Average():F1}");
                File.WriteAllLines(Path.Combine(output,"frame-times-ms.csv"),frameTimes.Select(f=>f.ToString("R",CultureInfo.InvariantCulture)));
                ReportPerformanceCounters();
                DisposePerformanceCounters();
            }
        }
        private IEnumerator CaptureSceneOnly(string target)
        {
            Check(SceneManager.GetActiveScene().name==target,"The requested capture scene is active: "+target);
            if(target=="Map")
            {
                Check(FindFirstObjectByType<MapController>()!=null,"Formal Map retains its production controller");
                Check(FindFirstObjectByType<MapReviewController>()==null,"Formal Map capture does not install a review UI controller");
            }
            foreach(var size in new[]{new Vector2Int(1920,1080),new Vector2Int(2560,1440),new Vector2Int(1920,1200)})
            {
                Screen.SetResolution(size.x,size.y,FullScreenMode.Windowed);
                yield return new WaitForSecondsRealtime(2);
                Move(AwayPoint());yield return ValidatePointerDelivery("Capture-only resolution "+size);
                Canvas.ForceUpdateCanvases();
                Check(Camera.main!=null,"Capture scene has an active main camera");
                var productionFraming=Camera.main.GetComponent<global::MapCameraFraming>();
                if(target=="Map")Check(productionFraming!=null,"Formal Map uses its production camera framing component");
                if(productionFraming!=null)
                {
                    productionFraming.Reframe(size.x,size.y);
                    report.Add($"Production framing safe area at {size.x}x{size.y}: {productionFraming.SafeArea}.");
                }
                else Camera.main.GetComponent<MapReviewCamera>()?.Reframe();
                yield return null;yield return null;
                Check(Screen.width==size.x&&Screen.height==size.y,$"Capture-only resolution {size.x}x{size.y} applied");
                string name=size==new Vector2Int(1920,1080)?"01-1920x1080-ui":$"02-{size.x}x{size.y}-layout";
                yield return Capture(name);
            }
            if(Environment.GetCommandLineArgs().Contains("-reviewSurfaceDiagnostic"))yield return CaptureSurfaceDiagnostic();
            report.Add("Capture-only completed without beginning a new run, preparing an encounter, changing cards, or invoking review scene validation.");
        }
        private IEnumerator CaptureSurfaceDiagnostic()
        {
            Screen.SetResolution(1920,1080,FullScreenMode.Windowed);
            yield return new WaitForSecondsRealtime(2);
            Move(AwayPoint());yield return ValidatePointerDelivery("Surface diagnostic setup");
            Canvas.ForceUpdateCanvases();
            Check(Camera.main!=null,"Surface diagnostic has a main camera");
            var productionFraming=Camera.main.GetComponent<global::MapCameraFraming>();
            if(productionFraming!=null)productionFraming.Reframe(1920,1080);
            else Camera.main.GetComponent<MapReviewCamera>()?.Reframe();
            yield return null;yield return null;
            Check(Screen.width==1920&&Screen.height==1080,"Surface diagnostic uses the settled 1920x1080 game view");
            int normalProperty=Shader.PropertyToID("_NormalStrength");
            var scene=SceneManager.GetActiveScene();
            var normals=FindObjectsByType<Renderer>(FindObjectsSortMode.None).Where(renderer=>renderer.enabled&&renderer.gameObject.scene==scene)
                .SelectMany(renderer=>renderer.sharedMaterials).Where(material=>material!=null&&material.HasProperty(normalProperty)).Distinct()
                .ToDictionary(material=>material,material=>material.GetFloat(normalProperty));
            var lights=FindObjectsByType<Light>(FindObjectsSortMode.None).Where(light=>light.isActiveAndEnabled&&light.type==LightType.Directional&&light.gameObject.scene==scene)
                .ToDictionary(light=>light,light=>(shadows:light.shadows,bias:light.shadowBias,normalBias:light.shadowNormalBias));
            Check(normals.Count>0&&lights.Count>0,"Surface diagnostic found normal-enabled materials and directional lights");
            report.Add($"Explicit surface diagnostic: {normals.Count} runtime materials and {lights.Count} directional lights; no asset files are changed.");
            foreach(var entry in lights)report.Add($"Directional baseline {entry.Key.name}: shadows {entry.Value.shadows}, bias {entry.Value.bias:R}, normal bias {entry.Value.normalBias:R}.");
            try
            {
                yield return Capture("03-diagnostic-normal");
                foreach(var entry in normals)entry.Key.SetFloat(normalProperty,0);
                yield return Capture("04-normal-off");
                foreach(var entry in normals)entry.Key.SetFloat(normalProperty,entry.Value);
                foreach(var entry in lights)entry.Key.shadows=LightShadows.None;
                yield return Capture("05-shadow-off");
                foreach(var entry in lights)entry.Key.shadows=entry.Value.shadows;
                foreach(var entry in lights){entry.Key.shadowBias=.2f;entry.Key.shadowNormalBias=.6f;}
                yield return Capture("06-shadow-bias");
            }
            finally
            {
                foreach(var entry in normals)if(entry.Key!=null)entry.Key.SetFloat(normalProperty,entry.Value);
                foreach(var entry in lights)if(entry.Key!=null)
                {
                    entry.Key.shadows=entry.Value.shadows;entry.Key.shadowBias=entry.Value.bias;entry.Key.shadowNormalBias=entry.Value.normalBias;
                }
            }
            Check(normals.All(entry=>entry.Key!=null&&Near(entry.Key.GetFloat(normalProperty),entry.Value))&&
                lights.All(entry=>entry.Key!=null&&entry.Key.shadows==entry.Value.shadows&&Near(entry.Key.shadowBias,entry.Value.bias)&&Near(entry.Key.shadowNormalBias,entry.Value.normalBias)),
                "Surface diagnostic restores every material normal and directional shadow setting");
        }
        private sealed class BenchmarkCounter
        {
            public string Name,Unit;
            public ProfilerRecorder Recorder;
            public double Sum,Peak,Last,Multiplier;
            public int Samples;
        }
        private void StartPerformanceCounters()
        {
            DisposePerformanceCounters();
            gpuTimes.Clear();cpuMainTimes.Clear();cpuRenderTimes.Clear();presentWaitTimes.Clear();lastTimingTimestamp=0;
            void Add(ProfilerCategory category,string name,string unit,double multiplier=1)
            {
                try
                {
                    var recorder=ProfilerRecorder.StartNew(category,name,1);
                    if(!recorder.Valid){report.Add("Profiler counter unavailable: "+name);recorder.Dispose();return;}
                    counters.Add(new BenchmarkCounter{Name=name,Unit=unit,Recorder=recorder,Multiplier=multiplier});
                }
                catch(Exception exception){report.Add("Profiler counter unavailable: "+name+" ("+exception.Message+")");}
            }
            Add(ProfilerCategory.Render,"Draw Calls Count","calls");
            Add(ProfilerCategory.Render,"Triangles Count","triangles");
            Add(ProfilerCategory.Render,"SetPass Calls Count","calls");
            Add(ProfilerCategory.Memory,"Total Used Memory","MiB",1.0/(1024*1024));
            Add(ProfilerCategory.Memory,"Gfx Used Memory","MiB",1.0/(1024*1024));
            Add(ProfilerCategory.Memory,"GC Used Memory","MiB",1.0/(1024*1024));
            Add(ProfilerCategory.Internal,"Main Thread","ms",1e-6);
            Add(ProfilerCategory.Internal,"Render Thread","ms",1e-6);
            timingFeature=FrameTimingManager.IsFeatureEnabled();
            report.Add("FrameTimingManager feature enabled: "+timingFeature);
            if(!timingFeature)report.Add("GPU frame timings unavailable: the player feature is disabled; no GPU pass/fail conclusion will be inferred from zero values.");
        }
        private void SamplePerformanceCounters()
        {
            foreach(var counter in counters)
            {
                if(!counter.Recorder.Valid||counter.Recorder.Count==0)continue;
                double value=counter.Recorder.LastValue*counter.Multiplier;
                counter.Last=value;counter.Sum+=value;counter.Peak=Math.Max(counter.Peak,value);counter.Samples++;
            }
            if(!timingFeature)return;
            FrameTimingManager.CaptureFrameTimings();
            if(FrameTimingManager.GetLatestTimings(1,timings)==0)return;
            var timing=timings[0];
            if(timing.frameStartTimestamp==0||timing.frameStartTimestamp==lastTimingTimestamp)return;
            lastTimingTimestamp=timing.frameStartTimestamp;
            if(double.IsNaN(timing.gpuFrameTime)||double.IsInfinity(timing.gpuFrameTime)||timing.gpuFrameTime<=0)return;
            gpuTimes.Add(timing.gpuFrameTime);
            cpuMainTimes.Add(timing.cpuMainThreadFrameTime);
            cpuRenderTimes.Add(timing.cpuRenderThreadFrameTime);
            presentWaitTimes.Add(timing.cpuMainThreadPresentWaitTime);
        }
        private void ReportPerformanceCounters()
        {
            foreach(var counter in counters)
                report.Add(counter.Samples>0?$"Profiler {counter.Name}: mean {counter.Sum/counter.Samples:F3}, last {counter.Last:F3}, peak {counter.Peak:F3} {counter.Unit}; samples {counter.Samples}.":"Profiler counter returned no samples: "+counter.Name);
            if(gpuTimes.Count==0)
            {
                report.Add(timingFeature?"GPU frame timings unavailable: no positive driver timing samples were returned.":"GPU frame timings were not requested because the feature is disabled.");
                report.Add("Bottleneck classification unavailable without valid GPU timings. Draw, triangle, memory and CPU markers are diagnostic observations, not proof of a GPU or CPU limit.");
                return;
            }
            var sorted=gpuTimes.OrderBy(value=>value).ToArray();
            double gpu=gpuTimes.Average(),main=cpuMainTimes.Average(),render=cpuRenderTimes.Average(),wait=presentWaitTimes.Average();
            report.Add($"GPU work ms mean {gpu:F3}, p95 {sorted[Mathf.Clamp(Mathf.CeilToInt(sorted.Length*.95f)-1,0,sorted.Length-1)]:F3}, max {sorted.Last():F3}; unique samples {sorted.Length}.");
            report.Add($"FrameTiming CPU main {main:F3} ms, render {render:F3} ms, present wait {wait:F3} ms (means paired with positive GPU samples).");
            double work=Math.Max(Math.Max(0,main-wait),render),frame=frameTimes.Average();
            string candidate=wait>.5&&gpu<frame*.8&&work<frame*.8?"presentation/wait limited":gpu>work*1.2?"GPU dominant":work>gpu*1.2?"CPU dominant":"mixed or balanced";
            report.Add("Bottleneck candidate from aggregate timings: "+candidate+". This is an inference, not a replacement for a targeted CPU/GPU profile.");
        }
        private void DisposePerformanceCounters()
        {
            foreach(var counter in counters)counter.Recorder.Dispose();
            counters.Clear();
        }
        private IEnumerator Capture(string name)
        {
            yield return ValidatePointerDelivery("Before capture "+name);
            yield return new WaitForEndOfFrame();
            var image=ScreenCapture.CaptureScreenshotAsTexture();
            File.WriteAllBytes(Path.Combine(output,name+".png"),image.EncodeToPNG());
            var pixels=image.GetPixels32();
            int lit=0,total=0,brightest=0,darkest=255;
            int step=Math.Max(1,pixels.Length/4096);
            for(int index=0;index<pixels.Length;index+=step)
            {
                var pixel=pixels[index];int value=Math.Max(pixel.r,Math.Max(pixel.g,pixel.b));
                if(value>12)lit++;
                brightest=Math.Max(brightest,value);darkest=Math.Min(darkest,value);total++;
            }
            Destroy(image);
            Check(total>0&&lit>total*.02f&&brightest-darkest>12,"Screenshot contains rendered image data, not a black or empty frame: "+name);
            report.Add($"Capture luminance range {darkest}-{brightest}; nonblack samples {lit}/{total}.");
            report.Add("Captured: "+name);Flush();
        }
        private void ValidateScene(MapReviewCamera framing)
        {
            Check(framing!=null&&framing.environment!=null,"Review camera has a framing environment");
            ValidateMapShadowRange();
            Check(framing.HasValidFraming&&framing.AreSubjectBoundsInsideSafeViewport(),
                "The complete framed environment, including the portal and visible cliff depth, fits the UI-safe region");
            report.Add($"Framed subject bounds: {framing.FramingBounds}; reference ground height: {framing.ReferenceGroundHeight:F3}.");
            var nodes=FindObjectsByType<MapReviewNode>(FindObjectsSortMode.None);
            if(nodes.Length>0)Check(nodes.Length==14,"Fourteen interactive nodes loaded");
            var ui=FindFirstObjectByType<MapReviewController>();
            if(nodes.Length>0)Check(ui!=null&&ui.stateOutlineMaterial!=null&&ui.stateOutlineMaterial.shader!=null&&ui.stateOutlineMaterial.shader.isSupported,
                "Node outlines reference a serialized supported review material");
            Physics.SyncTransforms();
            foreach(var node in nodes)
            {
                var collider=node.interactionCollider;
                Check(collider!=null,"Collider present: "+node.nodeId);
                var outline=node.transform.Find("Review state outline")?.GetComponent<LineRenderer>();
                Check(outline!=null&&outline.sharedMaterial!=null&&outline.sharedMaterial.shader==ui.stateOutlineMaterial.shader,
                    "Node outline uses the persisted material template: "+node.nodeId);
                ValidateNodeStateCue(node,framing);
                bool enabled=collider.enabled;collider.enabled=true;Physics.SyncTransforms();
                try
                {
                    var point=Camera.main.WorldToViewportPoint(collider.bounds.center);
                    Check(point.z>0&&framing.SafeViewport.Contains(point),"Node inside UI-safe region: "+node.nodeId);
                    Check(collider.Raycast(Camera.main.ViewportPointToRay(point),out _,500),"Camera ray hits preserved node geometry: "+node.nodeId);
                }
                finally {collider.enabled=enabled;}
            }
            foreach(var renderer in framing.environment.GetComponentsInChildren<Renderer>())
                Check(renderer.sharedMaterials.All(m=>m!=null&&m.shader!=null),"Material references: "+renderer.name);
            report.Add($"Independent meshes: {framing.environment.GetComponentsInChildren<MeshFilter>().Length}; safe viewport: {framing.SafeViewport}");
        }
        private void ValidateMapShadowRange()
        {
            var ranges=FindObjectsByType<MapEnvironmentShadowRange>(FindObjectsSortMode.None);
            Check(ranges.Length==1&&ranges[0].isActiveAndEnabled,"Exactly one active environment shadow-range owner exists on the review map");
            var range=ranges[0];
            var view=Camera.main;
            var framing=view!=null?view.GetComponent<MapReviewCamera>():null;
            Check(framing!=null&&framing.HasValidFraming,"Shadow coverage is evaluated against the rendered review camera framing");
            float farthest=0;
            var bounds=framing.FramingBounds;
            for(int i=0;i<8;i++)
            {
                var point=new Vector3((i&1)==0?bounds.min.x:bounds.max.x,(i&2)==0?bounds.min.y:bounds.max.y,(i&4)==0?bounds.min.z:bounds.max.z);
                farthest=Mathf.Max(farthest,Vector3.Dot(point-view.transform.position,view.transform.forward));
            }
            foreach(var anchor in framing.environment.GetComponentsInChildren<Transform>(true))
                if(anchor.name.StartsWith("ANCHOR_Node_",StringComparison.OrdinalIgnoreCase))
                    farthest=Mathf.Max(farthest,Vector3.Dot(anchor.position-view.transform.position,view.transform.forward));
            Check(range.RequiredShadowDistance>=100&&range.AppliedShadowDistance+.001f>=range.RequiredShadowDistance,
                "Applied review shadow distance covers the required distance and is at least 100 units");
            Check(range.RequiredShadowDistance+.001f>=farthest+Mathf.Max(0,range.depthMargin),
                "Shadow coverage reaches the framed portal, canopy, visible cliff and node-anchor depth plus its margin");
            var pipeline=QualitySettings.renderPipeline as UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset;
            Check(pipeline!=null&&pipeline.name.Contains("(Map environment runtime shadows)")&&Near(pipeline.shadowDistance,range.AppliedShadowDistance),
                "The active quality pipeline uses the map's transient shadow clone at the reported distance");
            report.Add($"Review shadow coverage: applied {range.AppliedShadowDistance:F3}, required {range.RequiredShadowDistance:F3}, framed subject/anchor depth {farthest:F3}, margin {range.depthMargin:F3}. This checks camera-space coverage, not a GPU depth-buffer measurement.");
        }
        private void ValidateReceiverShadowRestore()
        {
            var quality=QualitySettings.renderPipeline;
            var effective=UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline;
            Check(FindObjectsByType<MapEnvironmentShadowRange>(FindObjectsSortMode.None).Length==0,
                "The encounter receiver has no surviving map shadow-range owners");
            Check((quality==null||!quality.name.Contains("(Map environment runtime shadows)"))&&
                (effective==null||!effective.name.Contains("(Map environment runtime shadows)")),
                "Leaving the map restores a non-transient quality and effective render pipeline in the receiver");
            report.Add("Receiver restored quality pipeline: "+(quality!=null?quality.name:"Default graphics pipeline")+"; effective pipeline: "+(effective!=null?effective.name:"None"));
        }
        private void ValidateNodeStateCue(MapReviewNode node,MapReviewCamera framing)
        {
            var label=node.StateLabel;
            Check(label!=null&&label.isActiveAndEnabled&&!string.IsNullOrWhiteSpace(label.text),"State label is active and populated: "+node.nodeId);
            label.ForceMeshUpdate();
            var renderer=label.GetComponent<MeshRenderer>();
            Check(renderer!=null&&renderer.enabled&&label.textInfo.characterCount>0&&renderer.sharedMaterial!=null&&renderer.sharedMaterial.shader.isSupported,
                "State label has supported renderable glyphs: "+node.nodeId);
            var projected=ProjectBounds(renderer.bounds,Camera.main,out float depth);
            const float tolerance=.001f;
            var safe=framing.SafeViewport;
            Check(depth>Camera.main.nearClipPlane&&projected.xMin>=safe.xMin-tolerance&&projected.xMax<=safe.xMax+tolerance&&
                projected.yMin>=safe.yMin-tolerance&&projected.yMax<=safe.yMax+tolerance,
                "State label glyph bounds fit the camera UI-safe region: "+node.nodeId);
            if(node.BossPortalRenderer!=null)
            {
                var portal=ProjectBounds(node.BossPortalRenderer.bounds,Camera.main,out float portalDepth);
                float cueDepth=Camera.main.WorldToViewportPoint(label.transform.position).z;
                Check(projected.yMin>portal.yMax&&cueDepth<portalDepth,
                    "Boss state cue sits above and in front of the imported portal, not inside sanctuary geometry");
                report.Add($"Boss cue viewport {projected}, portal viewport {portal}, cue depth {cueDepth:F3}, nearest portal depth {portalDepth:F3}.");
            }
        }
        private static UnityEngine.Rect ProjectBounds(Bounds bounds,Camera view,out float depth)
        {
            Vector2 min=new(float.PositiveInfinity,float.PositiveInfinity),max=new(float.NegativeInfinity,float.NegativeInfinity);
            depth=float.PositiveInfinity;
            for(int i=0;i<8;i++)
            {
                var point=view.WorldToViewportPoint(new Vector3((i&1)==0?bounds.min.x:bounds.max.x,(i&2)==0?bounds.min.y:bounds.max.y,(i&4)==0?bounds.min.z:bounds.max.z));
                min=Vector2.Min(min,point);max=Vector2.Max(max,point);depth=Mathf.Min(depth,point.z);
            }
            return UnityEngine.Rect.MinMaxRect(min.x,min.y,max.x,max.y);
        }
        private IEnumerator UiTests()
        {
            var ui=FindFirstObjectByType<MapReviewController>();
            Check(ui!=null,"Review UI controller loaded");
            SetupPointer();
            Move(AwayPoint());ui.BeginNewAdventure();yield return null;yield return null;
            yield return ValidatePointerDelivery("UI test setup");
            Check(ui.State.Cards.Count==ui.startingDeck.Count(definition=>definition!=null),"New adventures contain only the configured starting deck");
            foreach(var type in new[]{OverworldEffectType.ReduceEnemyStartingHealth,OverworldEffectType.GrantStartingShield})
            {
                var definition=ui.startingDeck.First(card=>card!=null&&card.overworldEffect.effectType==type);
                while(ui.State.Cards.Count(card=>!card.Sacrificed&&card.Definition==definition)<2)ui.State.AddCard(definition);
            }
            yield return null;yield return null;
            report.Add("Duplicate Tidal Wave and Solar Shield instances were injected only by the opt-in test harness.");
            int initialCount=ui.State.Cards.Count;
            Check(initialCount>5&&ui.CardViews.Count==5,"Five-card page generated from the instance deck");
            Check(ui.CardViews.SelectMany(view=>view.GetComponentsInChildren<TMP_Text>(true)).All(label=>!label.text.Contains("New Text")),"No placeholder card text remains");
            foreach(var face in ui.CardViews)
            {
                string dimensions=$"{face.InstanceId}: front {face.FrontVisibleSize.x:F3}x{face.FrontVisibleSize.y:F3}, back {face.BackVisibleSize.x:F3}x{face.BackVisibleSize.y:F3}";
                report.Add("Authored card body dimensions: "+dimensions);
                Check(Mathf.Abs(face.FrontVisibleSize.y-face.BackVisibleSize.y)<1&&Mathf.Abs(face.FrontVisibleSize.x-face.BackVisibleSize.x)<4,"Front and back authored bodies fit the same footprint without stretching; "+dimensions);
            }
            Check(ui.CardViews.Select(card=>card.InstanceId).SequenceEqual(ui.State.Cards.Take(5).Select(card=>card.InstanceId)),"First page preserves exact instance order");
            ValidateHandLayout(ui);
            float firstPageLeft=ScreenBounds(ui.CardViews[0].HitRect).xMin;
            ui.ChangePage(1);yield return null;
            Check(ui.CurrentPage==1&&ui.CardViews.Count==Math.Min(5,initialCount-5),"Next page shows the partial remainder without filler cards");
            ValidateHandLayout(ui);
            Check(Mathf.Abs(ScreenBounds(ui.CardViews[0].HitRect).xMin-firstPageLeft)<.1f,"Partial pages retain the same left edge instead of centering or spreading cards");
            ui.ChangePage(999);yield return null;
            Check(ui.CurrentPage==(initialCount-1)/5,"Paging beyond the last page is bounded");
            ui.ChangePage(-999);yield return null;
            var duplicateDefinition=ui.State.Cards[0].Definition;
            var reward=ui.State.AddCard(duplicateDefinition);yield return null;yield return null;
            Check(reward!=null&&ui.State.Cards.Count==initialCount+1&&ui.State.Cards.Count(card=>card.Definition==duplicateDefinition)>1,"A reward creates a distinct same-name instance");
            ui.ChangePage(999);yield return null;
            Check(ui.CardViews.Any(card=>card.InstanceId==reward.InstanceId),"The reward appears and remains addressable on its generated page");
            ui.ChangePage(-999);yield return null;
            Check(Mathf.Abs(MapReviewCardView.FlipDuration-.28f)<.0001f&&Mathf.Abs(MapReviewCardView.DetailDelay-.15f)<.0001f,"Flip and settled-detail timings retain their specified values");

            var card=ui.CardViews[0];var rect=card.HitRect;
            Vector2 center=ScreenCenter(rect);
            Vector3 hitPosition=rect.position;Vector2 hitSize=rect.rect.size;
            yield return ObserveProgressiveFlip(ui,card,center,0,"Initial hover");
            yield return ObserveSettledDetail(ui,card);
            Check(card.IsBackVisible&&ui.ActiveDetailInstanceId==card.InstanceId,"The detail panel belongs to the visible back instance");
            yield return Capture("03-hover-back-detail");
            float progress=card.FlipProgress;
            yield return new WaitForSecondsRealtime(1.2f);
            Check(Mathf.Abs(card.FlipProgress-progress)<.0001f&&ui.ActiveDetailInstanceId==card.InstanceId,"Stationary hover remains stable without flip restarts");
            var bounds=ScreenBounds(rect);
            var exposed=bounds;
            if(ui.CardViews.Count>1)exposed.xMax=Mathf.Min(exposed.xMax,ScreenBounds(ui.CardViews[1].HitRect).xMin-1);
            for(int i=0;i<20;i++)
            {
                Move(new Vector2(Mathf.Lerp(exposed.xMin+4,exposed.xMax-4,i/19f),center.y));
                yield return new WaitForSecondsRealtime(.035f);
                Check(card.FlipProgress>.999f,"Horizontal hover retains the back at sample "+i);
            }
            Check(Mathf.Abs(Mathf.DeltaAngle(0,card.VisualRect.localEulerAngles.y))<=6.01f&&Mathf.Abs(Mathf.DeltaAngle(0,card.VisualRect.localEulerAngles.x))<=3.01f,"Pointer tilt stays within six horizontal and three vertical degrees");
            Check(card.VisualRect.localScale.x<=1.0401f&&card.VisualRect.localScale.x>1.03f,"Hover enlargement stays at the subtle 1.04 scale");
            foreach(var edge in new[]{new Vector2(exposed.xMin+1,center.y),new Vector2(exposed.xMax-1,center.y),new Vector2(center.x,bounds.yMin+1),new Vector2(center.x,bounds.yMax-1)})
            {
                Move(edge);yield return new WaitForSecondsRealtime(.1f);
                Check(card.FlipProgress>.999f&&card.IsPointerInside,"Fixed hit rectangle remains stable at a card edge");
            }
            Check(rect.position==hitPosition&&rect.rect.size==hitSize,"Visual animation never moves or resizes the hit rectangle");
            Check(Vector3.Dot(card.VisualRect.forward,rect.forward)>0,"The displayed back is not mirrored toward the camera");
            var detailBounds=ScreenBounds(ui.DetailRect);
            Vector2 bridge=new(center.x,(bounds.yMax+detailBounds.yMin)*.5f);
            Move(bridge);yield return new WaitForSecondsRealtime(.6f);
            Check(ui.DetailVisible&&card.FlipProgress>.999f&&ui.BlocksMapPointer(bridge),"The narrow card-detail bridge preserves hover and blocks map input");
            Move(ScreenCenter(ui.DetailRect));yield return new WaitForSecondsRealtime(.45f);
            Check(ui.DetailVisible&&card.FlipProgress>.999f,"Moving into the detail panel preserves its card owner");
            Move(AwayPoint());yield return new WaitForSecondsRealtime(.9f);
            Check(card.FlipProgress<.001f&&!card.IsBackVisible&&!ui.DetailVisible,"Leaving card and detail clears ownership and restores the front");
            yield return ObserveReversal(ui,card,center);
            Move(AwayPoint());yield return new WaitForSecondsRealtime(.6f);
            foreach(var swept in ui.CardViews){Move(ScreenCenter(swept.HitRect));yield return new WaitForSecondsRealtime(.055f);}
            yield return new WaitForSecondsRealtime(.6f);
            Check(ui.ActiveDetailInstanceId==ui.CardViews.Last().InstanceId&&ui.CardViews.Take(4).All(view=>view.FlipProgress<.001f),"A cross-card sweep leaves only the final card flipped");
            Move(AwayPoint());yield return new WaitForSecondsRealtime(.9f);
            yield return OverlapHoverTests(ui);
            if(SceneManager.GetActiveScene().name=="MapReview")yield return PointerBlockingTest(ui);

            Move(center);yield return new WaitForSecondsRealtime(.6f);
            var offering=ui.State.Cards.First(instance=>instance.InstanceId==card.InstanceId);
            yield return Click(center);
            Check(ui.ModalOpen,"Sacrifice opens blocking confirmation");
            float frozen=card.FlipProgress;
            Quaternion rotation=card.VisualRect.localRotation;Vector3 scale=card.VisualRect.localScale;
            string frozenRun=ui.State.RunId;int frozenPage=ui.CurrentPage;string frozenSelection=ui.SelectedNodeId;
            ui.ChangePage(1);ui.BeginNewAdventure();
            Move(AwayPoint());yield return new WaitForSecondsRealtime(.5f);
            Check(Mathf.Abs(card.FlipProgress-frozen)<.0001f&&Quaternion.Angle(rotation,card.VisualRect.localRotation)<.001f&&Vector3.Distance(scale,card.VisualRect.localScale)<.0001f,"Modal freezes all background card animation");
            Check(ui.State.RunId==frozenRun&&ui.CurrentPage==frozenPage&&ui.SelectedNodeId==frozenSelection&&ui.BlocksMapPointer(AwayPoint()),"Modal blocks background paging, new-run actions, and world input");
            yield return Capture("04-sacrifice-confirmation");
            ui.CancelSacrifice();
            yield return new WaitForSecondsRealtime(.8f);
            Check(!ui.ModalOpen&&!ui.State.SacrificeUsed&&card.FlipProgress<.001f,"Cancel preserves allowance and resynchronizes the outside pointer");
            string id=offering.InstanceId;
            ui.RequestSacrifice(offering);ui.ConfirmSacrifice();ui.ConfirmSacrifice();
            yield return null;yield return null;
            Check(ui.State.Cards.Single(c=>c.InstanceId==id).Sacrificed&&ui.State.SacrificeUsed&&ui.State.Cards.Count(c=>c.Sacrificed)==1,"Repeated confirm removes the exact instance only once");
            Check(ui.State.Cards.Any(c=>c.Definition==offering.Definition&&!c.Sacrificed),"Other instances of the same definition remain available");
            string run=ui.State.RunId;
            string target=SceneManager.GetActiveScene().name;
            string[] identities=ui.State.Cards.Select(instance=>instance.InstanceId).ToArray();
            yield return SceneManager.LoadSceneAsync(target);yield return WaitForMap(target);
            ui=FindFirstObjectByType<MapReviewController>();
            Check(ui.State.RunId==run&&ui.State.SacrificeUsed&&ui.State.PendingEffect.SourceInstanceId==id&&ui.State.Cards.Select(instance=>instance.InstanceId).SequenceEqual(identities),"Map reload preserves offering, pending effect, and exact deck identities");
            yield return Capture("05-after-offering-reload");
            if(target=="MapReview")yield return EncounterSceneTests(id);
            else report.Add("SKIP: World-node and encounter-transition tests are not applicable to the node-free sample scene.");
            ui=FindFirstObjectByType<MapReviewController>();
            ui.BeginNewAdventure();Move(AwayPoint());yield return new WaitForSecondsRealtime(1);
            Check(ui.State.Phase==MapRunPhase.OnMap&&!ui.State.SacrificeUsed&&!ui.State.PendingEffect.IsValid&&ui.State.Cards.All(instance=>!instance.Sacrificed),"UI verification leaves a fresh neutral adventure");
            ValidateMapShadowRange();
        }
        private void ValidateHandLayout(MapReviewController ui)
        {
            for(int index=0;index<ui.CardViews.Count;index++)
            {
                var card=ui.CardViews[index];var rect=card.HitRect;
                Check(Near(rect.anchorMin.x,0)&&Near(rect.anchorMax.x,0)&&Near(rect.anchoredPosition.x,MapReviewController.CardHitWidth*.5f+index*MapReviewController.CardStride),
                    "Card row is left anchored in stable instance order: "+index);
                Check(Near(rect.rect.width,MapReviewController.CardHitWidth)&&Near(rect.anchoredPosition.y,0)&&Quaternion.Angle(rect.localRotation,Quaternion.identity)<.001f,
                    "Card hit rectangles stay horizontal and unrotated at rest: "+index);
                if(index==0)continue;
                float bodyOverlap=(ui.CardViews[index-1].FrontVisibleSize.x+card.FrontVisibleSize.x)*.5f-MapReviewController.CardStride;
                Check(bodyOverlap>=12&&bodyOverlap<=24,$"Original illustrated bodies have modest overlap, not separated slots: {bodyOverlap:F3} reference pixels");
            }
        }
        private IEnumerator OverlapHoverTests(MapReviewController ui)
        {
            yield return ValidatePointerDelivery("Overlapping hand test setup");
            ValidateHandLayout(ui);
            var cards=ui.CardViews.ToArray();
            var positions=cards.Select(card=>card.HitRect.position).ToArray();
            var siblings=cards.Select(card=>card.transform.GetSiblingIndex()).ToArray();
            var hits=new List<RaycastResult>();
            for(int index=0;index<cards.Length-1;index++)
            {
                var left=cards[index];var right=cards[index+1];
                var a=ScreenBounds(left.HitRect);var b=ScreenBounds(right.HitRect);
                float overlap=Mathf.Min(a.xMax,b.xMax)-Mathf.Max(a.xMin,b.xMin);
                Check(overlap>0&&overlap<a.width*.25f,"Adjacent fixed hit rectangles have only a modest shared strip: "+index);
                var point=new Vector2((a.xMax+b.xMin)*.5f,a.center.y);
                Check(ui.CardAtScreenPoint(point)==right,"The right-hand topmost card owns the overlap: "+index);
                hits.Clear();EventSystem.current.RaycastAll(new PointerEventData(EventSystem.current){position=point},hits);
                Check(hits.Count>0&&hits[0].gameObject.GetComponentInParent<MapReviewCardView>()==right,"Actual UI raycast agrees with the stable overlap owner: "+index);
                Move(point);
                yield return WaitFor(()=>right.FlipProgress>.999f&&ui.ActiveDetailInstanceId==right.InstanceId,"Overlap hover settles on the topmost exact instance: "+index,3);
                yield return new WaitForSecondsRealtime(.5f);
                Check(cards.Count(card=>card.IsPointerInside)==1&&right.IsPointerInside&&left.FlipProgress<.001f&&ui.ActiveDetailInstanceId==right.InstanceId,
                    "Stationary overlap does not oscillate between neighboring cards: "+index);
                var panel=ScreenBounds(ui.DetailRect);
                Check(cards.All(card=>panel.yMin>ScreenBounds(card.HitRect).yMax),"The detail panel stays above the entire overlapping hand");
                if(index==1)
                {
                    yield return Capture("03a-compact-overlap-hover");
                    yield return Click(point);
                    Check(ui.ModalOpen&&ui.PendingSacrificeInstanceId==right.InstanceId,"Clicking the shared strip confirms only its topmost card instance");
                    ui.CancelSacrifice();
                    Check(!ui.State.SacrificeUsed,"Canceling an overlap click does not consume the offering allowance");
                }
                for(int sample=0;sample<6;sample++)
                {
                    bool overRight=(sample&1)!=0;
                    var edge=new Vector2(b.xMin+(overRight?1:-1),point.y);
                    Move(edge);yield return ValidatePointerDelivery("Overlap boundary sweep "+index+" / "+sample);
                    var expected=overRight?right:left;
                    Check(ui.CardAtScreenPoint(edge)==expected&&expected.IsPointerInside&&cards.Count(card=>card.IsPointerInside)==1,
                        "Crossing an overlap boundary transfers ownership exactly once: "+index+" / "+sample);
                }
                Move(AwayPoint());
                yield return WaitFor(()=>!ui.DetailVisible&&cards.All(card=>card.FlipProgress<.001f),"Leaving an overlap releases every card: "+index,3);
            }
            for(int index=0;index<cards.Length;index++)
                Check(cards[index].HitRect.position==positions[index]&&cards[index].transform.GetSiblingIndex()==siblings[index],
                    "Overlap hover never moves or reorders the stable hit roots: "+index);
            foreach(var card in cards.Reverse())
            {
                Move(ScreenCenter(card.HitRect));yield return ValidatePointerDelivery("Reverse hand sweep");
                Check(card.IsPointerInside&&cards.Count(view=>view.IsPointerInside)==1,"Right-to-left sweep selects one visible card: "+card.InstanceId);
            }
            Move(AwayPoint());yield return new WaitForSecondsRealtime(.9f);
        }
        private void TraceFlip(string stage,MapReviewController ui,MapReviewCardView card,float previous,double started)
        {
            var position=mouse.position.ReadValue();
            report.Add($"Flip sample {stage}: frame {Time.frameCount}, elapsed {(Time.realtimeSinceStartupAsDouble-started)*1000:F3} ms, delta {Time.unscaledDeltaTime*1000:F3} ms, progress {previous:F5}->{card.FlipProgress:F5}, inside {card.IsPointerInside}, contains {card.ContainsScreenPoint(position)}, pointer {position}, detail {ui.DetailVisible}, modal {ui.ModalOpen}.");
            if(!mouse.enabled||Mouse.current!=mouse||Vector2.Distance(position,requestedPointer)>.1f)
                Check(false,"The synthetic input stream was not delivered: "+PointerDiagnostics());
        }
        private void CheckFlipStep(MapReviewCardView card,float previous,bool forward,string label)
        {
            float expected=Mathf.MoveTowards(previous,forward?1:0,Time.unscaledDeltaTime/MapReviewCardView.FlipDuration);
            Check(Mathf.Abs(card.FlipProgress-expected)<.0002f,$"{label} follows elapsed frame time; expected {expected:F5}, actual {card.FlipProgress:F5}, delta {Time.unscaledDeltaTime*1000:F3} ms");
        }
        private IEnumerator ObserveProgressiveFlip(MapReviewController ui,MapReviewCardView card,Vector2 center,float minimumProgress,string label)
        {
            for(int attempt=1;attempt<=3;attempt++)
            {
                Move(AwayPoint());
                yield return WaitFor(()=>card.FlipProgress<.0001f&&!card.IsPointerInside&&!ui.DetailVisible,label+" starts from a settled front",4);
                yield return ValidatePointerDelivery(label+" neutral setup");
                yield return new WaitForEndOfFrame();
                float previous=card.FlipProgress;
                double started=Time.realtimeSinceStartupAsDouble;
                Move(center);
                while(Time.realtimeSinceStartupAsDouble-started<4)
                {
                    yield return null;yield return new WaitForEndOfFrame();
                    TraceFlip(label+" attempt "+attempt,ui,card,previous,started);
                    if(!card.IsPointerInside)continue;
                    CheckFlipStep(card,previous,true,label);
                    if(card.FlipProgress>minimumProgress&&card.FlipProgress<1)
                    {
                        Check(!ui.DetailVisible,"Hover starts a progressive flip before details appear: "+label);
                        yield break;
                    }
                    if(card.FlipProgress>=1)
                    {
                        // Retry only after a verified time step crossed the entire remaining observation window.
                        report.Add($"UNDERSAMPLED: {label} attempt {attempt} crossed the remaining flip in a {Time.unscaledDeltaTime*1000:F3} ms frame; retrying real input, without changing animation time.");
                        break;
                    }
                    Check(!ui.DetailVisible,"Details remain hidden during a partial flip: "+label);
                    previous=card.FlipProgress;
                }
                if(!card.IsPointerInside)Check(false,label+" pointer did not enter the fixed card hit rectangle; see frame diagnostics");
            }
            Check(false,label+" could not observe an intermediate rendered frame after three verified timing attempts; rerun without competing rendering load");
        }
        private IEnumerator ObserveSettledDetail(MapReviewController ui,MapReviewCardView card)
        {
            float previous=card.FlipProgress,settled=0;
            double started=Time.realtimeSinceStartupAsDouble;
            while(Time.realtimeSinceStartupAsDouble-started<4)
            {
                yield return null;yield return new WaitForEndOfFrame();
                TraceFlip("Completion and detail dwell",ui,card,previous,started);
                Check(card.IsPointerInside,"The pointer remains inside during the flip and detail dwell");
                CheckFlipStep(card,previous,true,"Completion and detail dwell");
                if(card.FlipProgress>=1)settled+=Mathf.Max(0,Time.unscaledDeltaTime-(1-previous)*MapReviewCardView.FlipDuration);
                Check(!ui.DetailVisible||(card.FlipProgress>=1&&settled>=MapReviewCardView.DetailDelay-.0001f),
                    $"Detail never appears before the full flip plus settled dwell; observed dwell {settled:F5} s");
                if(settled>=MapReviewCardView.DetailDelay+.0001f)
                {
                    Check(ui.DetailVisible,"Details appear after the completed flip settles for 0.15 seconds");
                    yield break;
                }
                previous=card.FlipProgress;
            }
            Check(false,"Hover did not finish the flip and detail dwell; see frame diagnostics");
        }
        private IEnumerator ObserveReversal(MapReviewController ui,MapReviewCardView card,Vector2 center)
        {
            for(int attempt=1;attempt<=3;attempt++)
            {
                yield return ObserveProgressiveFlip(ui,card,center,.45f,"Reversal setup "+attempt);
                float halfway=card.FlipProgress;
                double started=Time.realtimeSinceStartupAsDouble;
                Move(AwayPoint());yield return null;yield return new WaitForEndOfFrame();
                TraceFlip("Exit reverses",ui,card,halfway,started);
                Check(!card.IsPointerInside,"Exiting clears the fixed card hit rectangle");
                CheckFlipStep(card,halfway,false,"Exit reverses");
                float reversed=card.FlipProgress;
                if(reversed<=0)
                {
                    report.Add("UNDERSAMPLED: the exit frame reached the front before a partial reversal could be observed; retrying.");
                    continue;
                }
                Check(reversed<halfway&&!ui.DetailVisible,"Leaving reverses the current partial flip progress without details");
                Move(center);yield return null;yield return new WaitForEndOfFrame();
                TraceFlip("Re-entry reverses",ui,card,reversed,started);
                Check(card.IsPointerInside,"Re-entry reaches the fixed card hit rectangle");
                CheckFlipStep(card,reversed,true,"Re-entry reverses");
                if(card.FlipProgress>=1)
                {
                    report.Add("UNDERSAMPLED: the re-entry frame reached the back before a partial reversal could be observed; retrying.");
                    continue;
                }
                Check(card.FlipProgress>reversed&&!ui.DetailVisible,"Re-entering reverses smoothly without jumping to a completed face");
                yield break;
            }
            Check(false,"Partial reversal could not be observed after three verified timing attempts; rerun without competing rendering load");
        }
        private IEnumerator PointerBlockingTest(MapReviewController ui)
        {
            yield return ValidatePointerDelivery("Map click-through test setup");
            var node=FindObjectsByType<MapReviewNode>(FindObjectsSortMode.None).Single(value=>ui.State.NodeState(value.nodeId)==MapNodeState.Available);
            Check(node.interactionCollider.enabled,"The starting node collider is enabled by review state");
            Physics.SyncTransforms();
            Vector2 point=Camera.main.WorldToScreenPoint(node.interactionCollider.bounds.center);
            var canvas=(RectTransform)ui.CardBar.GetComponentInParent<Canvas>().transform;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(canvas,point,null,out var local);
            var blocker=MapReviewController.Rect("Transient verification UI blocker",canvas,new Vector2(.5f,.5f),new Vector2(.5f,.5f),local,new Vector2(150,150));
            blocker.gameObject.AddComponent<Image>().color=new Color(0,0,0,.01f);
            string selected=ui.SelectedNodeId;
            try
            {
                yield return null;yield return null;
                Check(ui.BlocksMapPointer(point),"A real canvas graphic intercepts the underlying node point");
                yield return Click(point);
                Check(ui.SelectedNodeId==selected,"Clicking over a node through UI does not select it");
            }
            finally {Destroy(blocker.gameObject);}
            yield return null;yield return null;
            yield return Click(point);
            Check(ui.SelectedNodeId==node.nodeId,"The same pointer click selects the available node after UI is removed");
            Move(AwayPoint());yield return null;
        }

        private IEnumerator EncounterSceneTests(string firstOfferingId)
        {
            yield return ValidatePointerDelivery("Encounter scene test setup");
            var ui=FindFirstObjectByType<MapReviewController>();
            var host=MapRunHost.Instance;
            string run=ui.State.RunId;
            Check(ui.State.PendingEffect.Type==OverworldEffectType.ReduceEnemyStartingHealth,"The initial integration offering is Tidal Wave");
            var before=ui.State.Player;
            int deckBefore=ui.State.Cards.Count;
            yield return OpenNextEncounter(ui);
            var receiver=FindFirstObjectByType<MapEncounterReviewController>();
            string failedEncounter=receiver.Request.EncounterId;
            yield return new WaitForSecondsRealtime(.65f);
            Check(host.State.Phase==MapRunPhase.Prepared&&host.Simulation==null&&!receiver.Request.IsAcknowledged&&
                host.State.PendingEffect.SourceInstanceId==firstOfferingId&&SamePlayer(host.State.Player,before),"Delayed receiver preparation consumes no offering or persistent stats");
            yield return Capture("06-receiver-delayed-preparation");
            receiver.FailLoad();yield return WaitForMap("MapReview");
            ui=FindFirstObjectByType<MapReviewController>();
            Check(ui.State.RunId==run&&ui.State.Phase==MapRunPhase.OnMap&&ui.State.SacrificeUsed&&
                ui.State.PendingEffect.SourceInstanceId==firstOfferingId&&ui.State.Cards.Single(card=>card.InstanceId==firstOfferingId).Sacrificed&&
                ui.State.Cards.Count==deckBefore&&SamePlayer(ui.State.Player,before),"Simulated loading failure returns to map without undoing or consuming the offering");
            ValidateRuntimeAvailability(ui);

            yield return OpenNextEncounter(ui);
            receiver=FindFirstObjectByType<MapEncounterReviewController>();
            Check(receiver.Request.EncounterId!=failedEncounter&&receiver.Request.Effect.SourceInstanceId==firstOfferingId,"Retry creates a new encounter carrying the original pending offering");
            receiver.Acknowledge();
            var simulation=host.Simulation;
            Check(receiver.Running&&simulation.Enemies["wave-0"]==76&&!host.State.PendingEffect.IsValid,"Successful readiness applies Tidal Wave to the first enemy exactly once");
            receiver.Acknowledge();
            Check(ReferenceEquals(simulation,host.Simulation)&&simulation.Enemies.Count==1&&simulation.Enemies["wave-0"]==76,"Repeated readiness does not replace the active simulation");
            receiver.Spawn();
            Check(simulation.Enemies.Count==2&&simulation.Enemies["wave-1"]==4,"A later enemy wave receives the same next-encounter effect");
            receiver.RepeatSpawn();
            Check(simulation.Enemies.Count==2&&simulation.Enemies["wave-1"]==4,"Repeated enemy identity is not processed twice");
            receiver.Damage();receiver.Spend();
            Check(simulation.PersistentPlayer.Health==before.Health-8&&Near(simulation.PersistentPlayer.Elixir,before.Elixir-2),"The test receiver consumes health and Elixir independently of map previews");
            var existingIds=new HashSet<string>(host.State.Cards.Select(card=>card.InstanceId));
            receiver.Reward();receiver.Reward();
            var rewards=host.State.Cards.Where(card=>!existingIds.Contains(card.InstanceId)).ToArray();
            Check(rewards.Length==2&&rewards.Select(card=>card.InstanceId).Distinct().Count()==2&&
                rewards.All(reward=>host.State.Cards.Count(card=>card.Definition==reward.Definition)>1),"Receiver rewards add two independent duplicate-definition instances");
            yield return Capture("07-receiver-waves-resources-rewards");
            receiver.CompleteWithoutReturning(true);
            var returned=host.State.Player;
            var acceptedResult=host.LastResult;
            Check(host.State.Phase==MapRunPhase.OnMap&&!host.State.SacrificeUsed&&host.Simulation==null,"Victory returns persistent state and restores the next offering allowance");
            receiver.Repeat();
            Check(ReferenceEquals(returned,host.State.Player)&&ReferenceEquals(acceptedResult,host.LastResult)&&host.State.Phase==MapRunPhase.OnMap,"Repeated result delivery leaves accepted map state unchanged");
            receiver.Return();yield return WaitForMap("MapReview");
            ui=FindFirstObjectByType<MapReviewController>();
            Check(ui.State.RunId==run&&SamePlayer(ui.State.Player,returned)&&ui.State.Cards.Single(card=>card.InstanceId==firstOfferingId).Sacrificed,"Actual return scene preserves resource consumption and the sacrificed instance");
            ValidateRuntimeAvailability(ui);
            Check(ui.graph.nodes.Count(node=>ui.State.NodeState(node.id)==MapNodeState.Available)==3,"The first victory opens exactly its three outgoing branches");
            foreach(var reward in rewards)
            {
                var active=ui.State.Cards.Where(card=>!card.Sacrificed).ToArray();
                int index=Array.FindIndex(active,card=>card.InstanceId==reward.InstanceId);
                ui.ChangePage(index/5-ui.CurrentPage);yield return null;
                Check(ui.CardViews.Any(card=>card.InstanceId==reward.InstanceId),"A receiver reward is operable in the generated deck: "+reward.Definition.cardId);
            }
            ui.ChangePage(-999);Move(AwayPoint());yield return null;
            yield return Capture("08-map-after-receiver-victory");

            before=ui.State.Player;
            Check(ui.SelectedNodeId==null,"Returning map starts without an accidentally selected next node");
            yield return Offer(ui,OverworldEffectType.IncreaseStartingElixir);
            Check(SamePlayer(ui.State.Player,before),"Confirming Uncertain Fates does not pre-commit persistent Elixir");
            yield return OpenNextEncounter(ui);
            receiver=FindFirstObjectByType<MapEncounterReviewController>();
            string pendingEncounter=receiver.Request.EncounterId;
            yield return SceneManager.LoadSceneAsync("MapEncounterReview");yield return WaitForReceiver();
            receiver=FindFirstObjectByType<MapEncounterReviewController>();
            Check(receiver.Request.EncounterId==pendingEncounter&&host.State.Phase==MapRunPhase.Prepared&&host.State.SacrificeUsed&&SamePlayer(host.State.Player,before),"Reloading the prepared receiver preserves request identity and does not reset the offering");
            receiver.Acknowledge();
            Check(Near(host.State.Player.MaxElixir,before.MaxElixir+3)&&Near(host.State.Player.Elixir,before.Elixir+3),"Uncertain Fates commits its permanent maximum and one-time restoration on readiness");
            receiver.Spend();
            var spent=host.Simulation.PersistentPlayer;
            receiver.Acknowledge();
            Check(ReferenceEquals(spent,host.Simulation.PersistentPlayer)&&Near(spent.MaxElixir,before.MaxElixir+3)&&Near(spent.Elixir,before.Elixir+1),"Duplicate start cannot reapply restoration after resources have been spent");
            receiver.Complete(true);yield return WaitForMap("MapReview");
            ui=FindFirstObjectByType<MapReviewController>();
            Check(SamePlayer(ui.State.Player,spent)&&!ui.State.SacrificeUsed,"Permanent Elixir capacity and spent resources survive scene return");
            ValidateRuntimeAvailability(ui);

            before=ui.State.Player;
            yield return Offer(ui,OverworldEffectType.GrantStartingShield);
            yield return OpenNextEncounter(ui);
            receiver=FindFirstObjectByType<MapEncounterReviewController>();receiver.Acknowledge();
            Check(host.Simulation.TemporaryShield==5&&host.Simulation.PersistentPlayer.Shield==before.Shield,"Solar Shield creates a separate temporary layer");
            receiver.Damage();
            Check(host.Simulation.TemporaryShield==0&&host.Simulation.PersistentPlayer.Health==before.Health-3,"The receiver consumes temporary shield before persistent HP");
            receiver.Complete(true);yield return WaitForMap("MapReview");
            ui=FindFirstObjectByType<MapReviewController>();
            Check(ui.State.Player.Shield==before.Shield&&ui.State.Player.Health==before.Health-3,"Consumed temporary shield does not leak into map state");

            before=ui.State.Player;
            yield return Offer(ui,OverworldEffectType.ImprovePlayerAttackSpeed);
            yield return OpenNextEncounter(ui);
            receiver=FindFirstObjectByType<MapEncounterReviewController>();receiver.Acknowledge();
            Check(Near(host.Simulation.EffectiveAttackInterval,Mathf.Max(1,before.AttackInterval-1))&&Near(host.Simulation.PersistentPlayer.AttackInterval,before.AttackInterval),"Fleet Footwork changes only the receiver's effective attack interval");
            receiver.Complete(true);yield return WaitForMap("MapReview");
            ui=FindFirstObjectByType<MapReviewController>();
            Check(Near(ui.State.Player.AttackInterval,before.AttackInterval),"The temporary attack interval never overwrites persistent attack speed");

            before=ui.State.Player;
            yield return Offer(ui,OverworldEffectType.GrantStartingShield);
            yield return OpenNextEncounter(ui);
            receiver=FindFirstObjectByType<MapEncounterReviewController>();receiver.Acknowledge();
            var leftover=host.Simulation;
            Check(leftover.TemporaryShield==5,"An unused shield remains temporary during the encounter");
            receiver.Complete(true);yield return WaitForMap("MapReview");
            ui=FindFirstObjectByType<MapReviewController>();
            Check(leftover.TemporaryShield==0&&ui.State.Player.Shield==before.Shield,"Unused temporary shield is discarded on successful return");
            ValidateRuntimeAvailability(ui);
            Check(ui.graph.nodes.Single(node=>ui.State.NodeState(node.id)==MapNodeState.Available).boss,"Five ordinary victories lead to the Boss node");

            yield return Offer(ui,OverworldEffectType.ReduceEnemyStartingHealth);
            yield return OpenNextEncounter(ui);
            receiver=FindFirstObjectByType<MapEncounterReviewController>();receiver.Acknowledge();
            Check(host.Simulation.Enemies["wave-0"]==76,"The Boss receiver honors the same Tidal Wave contract");
            receiver.Complete(true);yield return WaitForMap("MapReview");
            ui=FindFirstObjectByType<MapReviewController>();
            Check(ui.State.Phase==MapRunPhase.Won&&ui.graph.nodes.Count(node=>ui.State.NodeState(node.id)==MapNodeState.Completed)==6,"Boss victory completes a six-encounter adventure through real scene transitions");
            ValidateRuntimeAvailability(ui);
            yield return Capture("09-boss-completed-run");
            string wonRun=ui.State.RunId;
            var oldCards=new HashSet<string>(ui.State.Cards.Select(card=>card.InstanceId));
            ui.BeginNewAdventure();yield return null;yield return null;
            Check(ui.State.RunId!=wonRun&&SamePlayer(ui.State.Player,new MapPlayerState())&&ui.State.Cards.Count==ui.startingDeck.Count(card=>card!=null)&&
                ui.State.Cards.All(card=>!card.Sacrificed&&!oldCards.Contains(card.InstanceId))&&!ui.State.SacrificeUsed&&!ui.State.PendingEffect.IsValid,"New adventure resets route, instances, offerings, resources, and permanent Elixir gains");
            ValidateRuntimeAvailability(ui);

            yield return OpenNextEncounter(ui);
            receiver=FindFirstObjectByType<MapEncounterReviewController>();receiver.Acknowledge();receiver.Complete(false);
            yield return WaitForMap("MapReview");
            ui=FindFirstObjectByType<MapReviewController>();
            Check(ui.State.Phase==MapRunPhase.Lost,"A defeat result returns to the map's terminal failure state");
            ValidateRuntimeAvailability(ui);
            ui.BeginNewAdventure();Move(AwayPoint());yield return new WaitForSecondsRealtime(.8f);
            Check(ui.State.Phase==MapRunPhase.OnMap&&SamePlayer(ui.State.Player,new MapPlayerState()),"The failure flow can be reset to a fresh neutral map");
            yield return Capture("10-fresh-map-after-integration-tests");
        }

        private IEnumerator Offer(MapReviewController ui,OverworldEffectType type)
        {
            var card=ui.State.Cards.FirstOrDefault(value=>!value.Sacrificed&&value.Definition.overworldEffect.effectType==type);
            Check(card!=null,"A concrete card instance is available for offering: "+type);
            ui.RequestSacrifice(card);
            Check(ui.ModalOpen,"Offering confirmation opens: "+type);
            ui.ConfirmSacrifice();yield return null;yield return null;
            Check(card.Sacrificed&&ui.State.SacrificeUsed&&ui.State.PendingEffect.SourceInstanceId==card.InstanceId&&ui.State.PendingEffect.Type==type,"Offering is pending for its exact instance: "+type);
        }

        private IEnumerator OpenNextEncounter(MapReviewController ui)
        {
            Move(AwayPoint());yield return new WaitForSecondsRealtime(.7f);
            var next=ui.graph.nodes.First(node=>ui.State.NodeState(node.id)==MapNodeState.Available);
            var worldNode=FindObjectsByType<MapReviewNode>(FindObjectsSortMode.None).Single(node=>node.nodeId==next.id);
            Check(worldNode.interactionCollider.enabled,"Progression enables the next physical collider: "+next.id);
            Physics.SyncTransforms();
            Vector2 point=Camera.main.WorldToScreenPoint(worldNode.interactionCollider.bounds.center);
            yield return Click(point);
            Check(ui.SelectedNodeId==next.id,"A real pointer click selects the next available node: "+next.id);
            ui.EnterEncounter();
            Check(MapRunHost.Instance.State.Phase==MapRunPhase.Prepared,"Map prepares a request before loading the receiver");
            yield return WaitForReceiver();
        }

        private void ValidateRuntimeAvailability(MapReviewController ui)
        {
            foreach(var node in FindObjectsByType<MapReviewNode>(FindObjectsSortMode.None))
            {
                bool expected=ui.State.Phase==MapRunPhase.OnMap&&ui.State.NodeState(node.nodeId)==MapNodeState.Available;
                Check(node.interactionCollider!=null&&node.interactionCollider.enabled==expected,"Collider availability matches runtime progression: "+node.nodeId);
                ValidateNodeStateCue(node,Camera.main.GetComponent<MapReviewCamera>());
            }
        }

        private IEnumerator WaitForMap(string scene)
        {
            yield return WaitFor(()=>SceneManager.GetActiveScene().name==scene&&FindFirstObjectByType<MapReviewController>()!=null&&
                FindFirstObjectByType<MapReviewController>().State!=null&&FindFirstObjectByType<MapReviewController>().CardBar!=null,"Map UI is ready: "+scene,45);
            yield return null;yield return null;
            yield return ValidatePointerDelivery("Map scene ready: "+scene);
            Canvas.ForceUpdateCanvases();
            if(Camera.main!=null)Camera.main.GetComponent<MapReviewCamera>()?.Reframe();
            Physics.SyncTransforms();
            yield return null;yield return new WaitForEndOfFrame();
            ValidateMapShadowRange();
        }
        private IEnumerator WaitForReceiver()
        {
            yield return WaitFor(()=>SceneManager.GetActiveScene().name=="MapEncounterReview"&&FindFirstObjectByType<MapEncounterReviewController>()!=null&&
                FindFirstObjectByType<MapEncounterReviewController>().Request!=null,"Encounter receiver is ready",45);
            yield return null;
            yield return ValidatePointerDelivery("Receiver scene ready");
            ValidateReceiverShadowRestore();
        }
        private IEnumerator WaitFor(Func<bool> condition,string label,float timeout)
        {
            double deadline=Time.realtimeSinceStartupAsDouble+timeout;
            while(!condition())
            {
                if(Time.realtimeSinceStartupAsDouble>=deadline)throw new InvalidOperationException("Timed out: "+label);
                yield return null;
            }
            Check(true,label);
        }
        private static bool Near(float a,float b)=>Mathf.Abs(a-b)<.0001f;
        private static bool SamePlayer(MapPlayerState a,MapPlayerState b)=>a!=null&&b!=null&&a.Health==b.Health&&a.MaxHealth==b.MaxHealth&&
            a.Damage==b.Damage&&Near(a.AttackInterval,b.AttackInterval)&&a.Shield==b.Shield&&Near(a.Elixir,b.Elixir)&&Near(a.MaxElixir,b.MaxElixir);
        private static Vector2 AwayPoint()=>new(15,Screen.height*.55f);
        private static Vector2 ScreenCenter(RectTransform rect)=>RectTransformUtility.WorldToScreenPoint(null,rect.TransformPoint(rect.rect.center));
        private static UnityEngine.Rect ScreenBounds(RectTransform rect)
        {
            var points=new Vector3[4];rect.GetWorldCorners(points);
            Vector2 a=RectTransformUtility.WorldToScreenPoint(null,points[0]);
            Vector2 b=RectTransformUtility.WorldToScreenPoint(null,points[2]);
            return UnityEngine.Rect.MinMaxRect(a.x,a.y,b.x,b.y);
        }
        private IEnumerator Click(Vector2 point)
        {
            Move(point);yield return ValidatePointerDelivery("Click position");
            QueuePointer(point,1);
            yield return ValidatePointerDelivery("Click press");
            Move(point);yield return ValidatePointerDelivery("Click release");
        }
        private void SetupPointer()
        {
            if(mouse!=null)return;
            originalInputSettings=InputSystem.settings;
            testInputSettings=Instantiate(originalInputSettings);
            testInputSettings.name="Opt-in review input settings";
            testInputSettings.backgroundBehavior=InputSettings.BackgroundBehavior.IgnoreFocus;
            InputSystem.settings=testInputSettings;
            // The copy exists only in this opt-in player. Hidden windows must not disable synthetic devices.
            // Frontend muting and event filtering do not send cursor or device commands to the OS.
            foreach(var pointer in InputSystem.devices.OfType<Mouse>().Where(pointer=>pointer.enabled).ToArray())
            {
                suspendedPointers.Add(pointer);InputSystem.DisableDevice(pointer,keepSendingEvents:true);
            }
            InputSystem.onEvent+=ObservePointerEvent;
            mouse=InputSystem.AddDevice<Mouse>("ReviewTestPointer");
            mouse.MakeCurrent();
            report.Add($"Test input isolation: original background policy {originalInputSettings.backgroundBehavior}, process-only policy {testInputSettings.backgroundBehavior}, update mode {testInputSettings.updateMode}, application focused {Application.isFocused}.");
            Check(mouse.enabled&&Mouse.current==mouse,"The synthetic pointer is enabled and current before captures");
        }
        private void ObservePointerEvent(InputEventPtr inputEvent,InputDevice device)
        {
            if(device is not Mouse||(!inputEvent.IsA<StateEvent>()&&!inputEvent.IsA<DeltaStateEvent>()))return;
            if(device!=mouse){inputEvent.handled=true;return;}
            observedPointerEvents++;
        }
        private string PointerDiagnostics()
        {
            return $"focus {Application.isFocused}, background policy {InputSystem.settings.backgroundBehavior}, synthetic enabled {mouse!=null&&mouse.enabled}, current device {Mouse.current?.deviceId}, synthetic device {mouse?.deviceId}, requested {requestedPointer}, actual {(mouse!=null?mouse.position.ReadValue():Vector2.zero)}, requested buttons {requestedButtons}, actual left {(mouse!=null&&mouse.leftButton.isPressed)}, queued {queuedPointerEvents}, observed {observedPointerEvents}";
        }
        private IEnumerator ValidatePointerDelivery(string label)
        {
            yield return null;yield return null;
            report.Add("Input delivery "+label+": "+PointerDiagnostics());
            Check(mouse!=null&&mouse.enabled&&Mouse.current==mouse&&Vector2.Distance(mouse.position.ReadValue(),requestedPointer)<.1f&&
                mouse.leftButton.isPressed==((requestedButtons&1)!=0)&&observedPointerEvents>0,
                label+" is delivered through the queued event stream");
        }
        private void QueuePointer(Vector2 point,ushort buttons)
        {
            if(mouse==null||!mouse.added||!mouse.enabled)throw new InvalidOperationException("Cannot queue review input: "+PointerDiagnostics());
            requestedPointer=point;requestedButtons=buttons;queuedPointerEvents++;
            InputSystem.QueueStateEvent(mouse,new MouseState{position=point,buttons=buttons});
        }
        private void Move(Vector2 point)=>QueuePointer(point,0);
        private void Check(bool condition,string message)
        {if(!condition)throw new InvalidOperationException("FAILED: "+message);report.Add("PASS: "+message);}
        private void Flush(){File.WriteAllLines(Path.Combine(output,"verification.txt"),report.Concat(new[]{"Runtime errors: "+errors.Count}).Concat(errors));}
        private void Finish(int code)
        {
            if(finished)return;
            finished=true;sampling=false;DisposePerformanceCounters();Flush();Application.logMessageReceived-=Log;
            InputSystem.onEvent-=ObservePointerEvent;
            if(mouse!=null){InputSystem.RemoveDevice(mouse);mouse=null;}
            foreach(var pointer in suspendedPointers)if(pointer.added)InputSystem.EnableDevice(pointer);
            suspendedPointers.Clear();
            if(originalInputSettings!=null)InputSystem.settings=originalInputSettings;
            if(testInputSettings!=null)Destroy(testInputSettings);
            Application.Quit(code);enabled=false;
        }
        private static string Argument(string name,string fallback){var args=Environment.GetCommandLineArgs();int i=Array.IndexOf(args,name);return i>=0&&i+1<args.Length?args[i+1]:fallback;}
    }
}
