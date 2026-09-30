using UnityEngine;

namespace Deinosavros.MapReview
{
    [RequireComponent(typeof(Camera))]
    public sealed class MapReviewAtmosphere : MonoBehaviour
    {
        public MapVisualProfile profile;
        private Camera view;
        private MapReviewCamera framing;
        private Transform cachedEnvironment;
        private readonly System.Collections.Generic.List<Transform> anchors = new();
        private void Start()
        {
            view=GetComponent<Camera>();framing=GetComponent<MapReviewCamera>();
            if(profile!=null)MapCameraCloudOverlay.Ensure(view).Configure(Mathf.Min(.12f,profile.cloudOpacity),Mathf.Max(.8f,profile.cloudCenterClarity),profile.cloudSpeed*.3f);
        }
        private void LateUpdate()
        {
            if(view==null||framing==null||framing.environment==null||profile==null)return;
            if(cachedEnvironment!=framing.environment)
            {
                cachedEnvironment=framing.environment;anchors.Clear();
                foreach(var marker in cachedEnvironment.GetComponentsInChildren<Transform>())
                    if(marker.name.StartsWith("ANCHOR_Node_",System.StringComparison.Ordinal))anchors.Add(marker);
            }
            float farthest=0;
            foreach(var marker in anchors)
                if(marker!=null)farthest=Mathf.Max(farthest,Vector3.Dot(marker.position-view.transform.position,view.transform.forward));
            if(farthest<=0)return;
            RenderSettings.fog=true;RenderSettings.fogMode=FogMode.Linear;
            RenderSettings.fogColor=profile.skyHorizon;RenderSettings.fogStartDistance=farthest+8;
            RenderSettings.fogEndDistance=farthest+8+profile.fogDepth;
        }
    }
}
