using TMPro;
using UnityEngine;

namespace Deinosavros.MapReview
{
    public sealed class MapReviewNode : MonoBehaviour
    {
        public string nodeId;
        public Collider interactionCollider;
        private MapReviewController controller;
        private TextMeshPro label;
        private LineRenderer outline;
        private Material outlineMaterial;
        private MapNodeState? lastState;
        private bool lastSelected;
        private MapRunState observedState;
        private Collider originalCollider;
        private bool originalColliderEnabled;
        private bool boss;
        public TextMeshPro StateLabel => label;
        public Renderer BossPortalRenderer { get; private set; }
        public void Bind(MapReviewController owner)
        {
            controller = owner;
            ObserveState();
            SynchronizeAvailability();
            if (outline != null && label != null) return;
            boss=owner.graph!=null&&owner.graph.Find(nodeId)?.boss==true;
            if(boss)
            {
                var framing=Camera.main!=null?Camera.main.GetComponent<MapReviewCamera>():null;
                if(framing!=null&&framing.environment!=null)
                    foreach(var renderer in framing.environment.GetComponentsInChildren<Renderer>())
                        if(renderer.name.Replace('_',' ').Equals("Portal - carved solid ring",System.StringComparison.OrdinalIgnoreCase))
                        {BossPortalRenderer=renderer;break;}
                if(BossPortalRenderer==null)Debug.LogError("The Boss state cue requires the imported carved portal renderer.",this);
            }
            var marker = new GameObject("Review state outline"); marker.transform.SetParent(transform, false);
            outline = marker.AddComponent<LineRenderer>(); outline.useWorldSpace = false; outline.loop = true;
            outline.positionCount=48; outline.widthMultiplier=.045f; outline.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;
            outline.receiveShadows=false;
            if(owner.stateOutlineMaterial!=null)
            {
                outlineMaterial=new Material(owner.stateOutlineMaterial);
                outline.sharedMaterial=outlineMaterial;
            }
            else Debug.LogError("Review nodes require a serialized state outline material.",owner);
            for(int i=0;i<48;i++)
            {
                float a=i*Mathf.PI*2/48;
                outline.SetPosition(i,boss?new Vector3(Mathf.Cos(a)*1.8f,Mathf.Sin(a)*.36f,0):new Vector3(Mathf.Cos(a)*.82f,.025f,Mathf.Sin(a)*.82f));
            }
            label=new GameObject("Review state label").AddComponent<TextMeshPro>(); label.transform.SetParent(transform,false);
            label.transform.localPosition=Vector3.up*.16f; label.fontSize=boss?4.2f:3.2f; label.alignment=TextAlignmentOptions.Center;
            label.rectTransform.sizeDelta=new Vector2(boss?4.4f:2,1); label.outlineWidth=.2f; label.outlineColor=Color.black;
        }
        private void ObserveState()
        {
            var state=controller!=null?controller.State:null;
            if(observedState==state)return;
            if(observedState!=null)observedState.Changed-=SynchronizeAvailability;
            observedState=state;
            if(observedState!=null)observedState.Changed+=SynchronizeAvailability;
        }
        private void SynchronizeAvailability()
        {
            if(interactionCollider==null)return;
            if(originalCollider!=interactionCollider)
            {
                RestoreCollider();
                originalCollider=interactionCollider;
                originalColliderEnabled=interactionCollider.enabled;
            }
            // Legacy nodes start disabled; only the review state controls availability at runtime.
            interactionCollider.enabled=isActiveAndEnabled&&observedState!=null&&
                observedState.Phase==MapRunPhase.OnMap&&observedState.NodeState(nodeId)==MapNodeState.Available;
        }
        private void RestoreCollider()
        {
            if(originalCollider!=null)originalCollider.enabled=originalColliderEnabled;
        }
        private void OnEnable()
        {
            if(controller==null)return;
            ObserveState();
            SynchronizeAvailability();
        }
        private void OnDisable()
        {
            if(observedState!=null)observedState.Changed-=SynchronizeAvailability;
            observedState=null;
            RestoreCollider();
            lastState=null;
        }
        private void Update()
        {
            if(controller==null || controller.State==null || Camera.main==null || label==null || outline==null)return;
            var state=controller.State.NodeState(nodeId);
            bool available=state==MapNodeState.Available;
            bool selected=controller.IsSelected(nodeId);
            if(lastState!=state||lastSelected!=selected)
            {
                var color=state switch { MapNodeState.Available=>new Color(.95f,.72f,.28f), MapNodeState.Completed=>new Color(.42f,.76f,.6f), _=>new Color(.48f,.46f,.40f)};
                outline.startColor=outline.endColor=Color.white;
                if(outlineMaterial!=null)outlineMaterial.SetColor("_BaseColor",color);
                outline.enabled=outlineMaterial!=null&&(available || state==MapNodeState.Completed);
                outline.widthMultiplier=selected ? .075f : .045f;
                label.color=boss&&!available&&state!=MapNodeState.Completed?new Color(.84f,.81f,.73f):color;
                label.text=state switch {MapNodeState.Available=>selected?"SELECTED":"OPEN",MapNodeState.Completed=>"DONE",MapNodeState.Skipped=>"PASSED",_=>"LOCKED"};
                if(boss)label.text="BOSS / "+label.text;
                lastState=state;lastSelected=selected;
            }
            label.transform.rotation=Camera.main.transform.rotation;
            if(boss&&BossPortalRenderer!=null)PositionBossCue(Camera.main);
        }
        private void PositionBossCue(Camera view)
        {
            var bounds=BossPortalRenderer.bounds;
            float left=float.PositiveInfinity,right=float.NegativeInfinity,top=float.NegativeInfinity,depth=float.PositiveInfinity;
            for(int i=0;i<8;i++)
            {
                var corner=new Vector3((i&1)==0?bounds.min.x:bounds.max.x,(i&2)==0?bounds.min.y:bounds.max.y,(i&4)==0?bounds.min.z:bounds.max.z);
                var point=view.WorldToScreenPoint(corner);
                left=Mathf.Min(left,point.x);right=Mathf.Max(right,point.x);top=Mathf.Max(top,point.y);depth=Mathf.Min(depth,point.z);
            }
            float margin=14*Mathf.Sqrt((Screen.width/1920f)*(Screen.height/1080f));
            // Move only generated state visuals above the imported crown and in front of its depth range.
            label.transform.position=view.ScreenToWorldPoint(new Vector3((left+right)*.5f,top+margin,Mathf.Max(view.nearClipPlane+.2f,depth-.15f)));
            outline.transform.SetPositionAndRotation(label.transform.position,view.transform.rotation);
        }
        private void OnDestroy()
        {
            if(observedState!=null)observedState.Changed-=SynchronizeAvailability;
            RestoreCollider();
            if(outlineMaterial!=null)Destroy(outlineMaterial);
        }
    }
}
