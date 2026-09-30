using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace Deinosavros.MapReview
{
    /// <summary>
    /// Extends shadows only while the active review environment is running.
    /// The URP asset is cloned in memory; renderer data and project assets are never edited.
    /// Attach to the environment root. Assign its camera/profile, or let the camera resolve by tag.
    /// Multiple enabled roots share one lease, avoiding nested pipeline clones during reimport.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(200)]
    public sealed class MapEnvironmentShadowRange : MonoBehaviour
    {
        public Camera targetCamera;
        public MapVisualProfile profile;
        [Min(100)] public float minimumShadowDistance = 100;
        [Min(0)] public float depthMargin = 12;

        private readonly List<Transform> anchors = new();
        private bool registered;
        private static readonly List<MapEnvironmentShadowRange> owners = new();
        private static UniversalRenderPipelineAsset runtimePipeline;
        private static RenderPipelineAsset previousQualityOverride;
        private static int leasedQualityLevel = -1;
        private static float sourceShadowDistance;

        public float RequiredShadowDistance { get; private set; }
        public float AppliedShadowDistance => runtimePipeline != null ? runtimePipeline.shadowDistance : 0;

        private void OnEnable()
        {
            if (!Application.isPlaying) return;
            RefreshAnchors();
            UpdateRegistration();
        }

        public void RefreshAnchors()
        {
            anchors.Clear();
            foreach (var item in GetComponentsInChildren<Transform>(true))
                if (item.name.StartsWith("ANCHOR_Node_", StringComparison.OrdinalIgnoreCase)) anchors.Add(item);
        }

        private void LateUpdate()
        {
            if (!Application.isPlaying) return;
            UpdateRegistration();
            if (!registered || owners.Count == 0 || owners[0] != this) return;

            // A changed quality level or an external pipeline selection becomes the new source.
            if (runtimePipeline != null && (QualitySettings.GetQualityLevel() != leasedQualityLevel ||
                QualitySettings.renderPipeline != runtimePipeline)) ReleasePipeline();
            if (runtimePipeline == null && !CreatePipelineLease()) return;

            float required = Mathf.Max(100, sourceShadowDistance);
            foreach (var owner in owners)
                if (owner != null && owner.isActiveAndEnabled)
                    required = Mathf.Max(required, owner.CalculateRequiredDistance());
            runtimePipeline.shadowDistance = required;
        }

        private void UpdateRegistration()
        {
            bool shouldRegister = gameObject.scene == SceneManager.GetActiveScene();
            if (shouldRegister && !registered)
            {
                owners.Add(this);
                registered = true;
            }
            else if (!shouldRegister && registered) Unregister();
        }

        private float CalculateRequiredDistance()
        {
            if (targetCamera == null) targetCamera = Camera.main;
            float required = Mathf.Max(100, minimumShadowDistance);
            if (profile != null) required = Mathf.Max(required, profile.shadowRange);
            if (targetCamera != null)
            {
                Vector3 origin = targetCamera.transform.position;
                Vector3 forward = targetCamera.transform.forward;
                float farthest = 0;
                foreach (var anchor in anchors)
                    if (anchor != null)
                        farthest = Mathf.Max(farthest, Vector3.Dot(anchor.position - origin, forward));

                // Also include the full portal and high canopy, not only ground interaction centers.
                var framing = targetCamera.GetComponent<MapReviewCamera>();
                if (framing != null && framing.HasValidFraming)
                {
                    Bounds bounds = framing.FramingBounds;
                    for (int i = 0; i < 8; i++)
                    {
                        Vector3 point = new Vector3((i & 1) == 0 ? bounds.min.x : bounds.max.x,
                            (i & 2) == 0 ? bounds.min.y : bounds.max.y,
                            (i & 4) == 0 ? bounds.min.z : bounds.max.z);
                        farthest = Mathf.Max(farthest, Vector3.Dot(point - origin, forward));
                    }
                }
                required = Mathf.Max(required, farthest + Mathf.Max(0, depthMargin));
            }
            RequiredShadowDistance = required;
            return required;
        }

        private static bool CreatePipelineLease()
        {
            var source = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            if (source == null) return false;
            leasedQualityLevel = QualitySettings.GetQualityLevel();
            previousQualityOverride = QualitySettings.renderPipeline;
            sourceShadowDistance = source.shadowDistance;
            runtimePipeline = Instantiate(source);
            runtimePipeline.name = source.name + " (Map environment runtime shadows)";
            runtimePipeline.hideFlags = HideFlags.HideAndDontSave;
            runtimePipeline.shadowDistance = Mathf.Max(source.shadowDistance, 100);
            QualitySettings.renderPipeline = runtimePipeline;
            return true;
        }

        private void OnDisable() => Unregister();
        private void OnDestroy() => Unregister();

        private void Unregister()
        {
            if (!registered) return;
            registered = false;
            owners.Remove(this);
            if (owners.Count == 0) ReleasePipeline();
        }

        private static void ReleasePipeline()
        {
            if (runtimePipeline == null) return;
            var released = runtimePipeline;
            int currentQuality = QualitySettings.GetQualityLevel();
            // Restore only the slot we own, never overwrite someone else's pipeline selection.
            if (leasedQualityLevel >= 0 && leasedQualityLevel < QualitySettings.names.Length &&
                QualitySettings.GetRenderPipelineAssetAt(leasedQualityLevel) == released)
            {
                if (currentQuality != leasedQualityLevel) QualitySettings.SetQualityLevel(leasedQualityLevel, false);
                QualitySettings.renderPipeline = previousQualityOverride;
                if (currentQuality != leasedQualityLevel) QualitySettings.SetQualityLevel(currentQuality, false);
            }
            runtimePipeline = null;
            previousQualityOverride = null;
            leasedQualityLevel = -1;
            sourceShadowDistance = 0;
            if (Application.isPlaying) Destroy(released);
            else DestroyImmediate(released);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetRuntimeLease()
        {
            ReleasePipeline();
            foreach (var owner in owners)
                if (owner != null) owner.registered = false;
            owners.Clear();
        }
    }
}
