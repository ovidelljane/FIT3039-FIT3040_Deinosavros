using System;
using System.Collections.Generic;
using UnityEngine;

namespace Deinosavros.MapReview
{
    [RequireComponent(typeof(Camera))]
    public sealed class MapReviewCamera : MonoBehaviour
    {
        public Transform environment;
        public MapVisualProfile profile;
        public bool sample;
        public float yaw;
        private int width, height;
        private Camera view;
        private MapReviewController ui;
        private Transform framedEnvironment;
        private bool needsFrame = true;
        private Rect previousUsable;
        private Vector4 previousCameraSettings;
        private Vector2 previousLayoutSettings;
        private readonly List<Vector3> corners = new();
        private readonly List<float> groundCandidates = new();
        private readonly Vector3[] uiCorners = new Vector3[4];
        private static readonly string[] BackgroundGroups =
        {
            "08 Background", "Background", "Review Background", "Map Background",
            "Atmosphere", "Review Atmosphere", "Clouds", "Cloud Sea", "Distant Ruins", "Distant Silhouettes"
        };
        private static readonly string[] BackgroundPrefixes =
        {
            "Background ", "Distant ", "RU Distant ", "Overhaul Distant ", "Cloud ", "Clouds ",
            "Chasm floor", "Low valley haze", "Valley haze", "Fossil ", "RU Fossil ", "Sky "
        };
        public Rect SafeViewport { get; private set; }
        public Bounds FramingBounds { get; private set; }
        public float ReferenceGroundHeight { get; private set; }
        public bool HasValidFraming { get; private set; }
        private void Start() { view = GetComponent<Camera>(); Reframe(); }
        private void LateUpdate()
        {
            if (environment == null || profile == null) return;
            if (view == null) view = GetComponent<Camera>();
            if (!TryReadUsableViewport(out Rect usable)) { needsFrame = true; return; }
            var cameraSettings = new Vector4(profile.fieldOfView, profile.pitch, yaw, profile.frameFill);
            var layoutSettings = new Vector2(profile.uiMarginPixels, profile.visibleCliffDepth);
            if (needsFrame || Screen.width != width || Screen.height != height || framedEnvironment != environment ||
                cameraSettings != previousCameraSettings || layoutSettings != previousLayoutSettings || !SameRect(usable, previousUsable))
                Reframe();
        }
        public void Reframe()
        {
            needsFrame = true;
            HasValidFraming = false;
            if (view == null) view = GetComponent<Camera>();
            if (environment == null || profile == null) return;
            Canvas.ForceUpdateCanvases();
            // Start order is undefined. Wait for real UI instead of keeping a fallback fit.
            if (!TryReadUsableViewport(out Rect usable)) return;
            width = Screen.width; height = Screen.height;
            view.fieldOfView = Mathf.Clamp(profile.fieldOfView, 1, 100);
            view.transform.rotation = Quaternion.Euler(profile.pitch, yaw, 0);
            view.nearClipPlane = .1f;
            Vector2 center = usable.center;
            Vector2 half = usable.size * Mathf.Clamp(profile.frameFill, .1f, 1) * .5f;
            SafeViewport = Rect.MinMaxRect(center.x - half.x, center.y - half.y, center.x + half.x, center.y + half.y);
            ReferenceGroundHeight = FindReferenceGroundHeight();
            float lowestVisible = ReferenceGroundHeight - Mathf.Max(0, profile.visibleCliffDepth);
            corners.Clear(); bool initialized = false; Bounds bounds = default;
            foreach (var renderer in environment.GetComponentsInChildren<Renderer>())
            {
                if (!renderer.enabled || renderer is ParticleSystemRenderer || IsBackgroundRenderer(renderer, environment)) continue;
                var b = renderer.bounds;
                if (b.max.y < lowestVisible) continue;
                var min = b.min; min.y = Mathf.Max(min.y, lowestVisible); b.SetMinMax(min, b.max);
                if (!initialized) { bounds = b; initialized = true; } else bounds.Encapsulate(b);
                for (int i = 0; i < 8; i++)
                    corners.Add(new Vector3((i & 1) == 0 ? b.min.x : b.max.x,
                        (i & 2) == 0 ? b.min.y : b.max.y, (i & 4) == 0 ? b.min.z : b.max.z));
            }
            if (!initialized) return;
            FramingBounds = bounds;
            float tangent = Mathf.Tan(view.fieldOfView * Mathf.Deg2Rad * .5f);
            void Position(float distance)
            {
                view.transform.position = bounds.center - view.transform.forward * distance
                    - view.transform.up * ((center.y - .5f) * 2 * tangent * distance)
                    - view.transform.right * ((center.x - .5f) * 2 * tangent * distance * view.aspect);
            }
            bool Fits(float distance) { Position(distance); return SubjectFits(.00001f); }
            float lo = .2f, hi = Mathf.Max(10, bounds.size.magnitude * 2);
            int expansion = 0;
            while (!Fits(hi) && expansion++ < 12) hi *= 2;
            if (!Fits(hi)) return;
            for (int step = 0; step < 36; step++)
            {
                float distance = (lo + hi) * .5f;
                if (Fits(distance)) hi = distance; else lo = distance;
            }
            Position(hi); view.farClipPlane = Mathf.Max(500, hi + bounds.size.magnitude * 2);
            previousUsable = usable;
            previousCameraSettings = new Vector4(profile.fieldOfView, profile.pitch, yaw, profile.frameFill);
            previousLayoutSettings = new Vector2(profile.uiMarginPixels, profile.visibleCliffDepth);
            framedEnvironment = environment; needsFrame = false;
            HasValidFraming = SubjectFits(.0001f);
        }

        private bool TryReadUsableViewport(out Rect usable)
        {
            usable = default;
            if (Screen.width < 1 || Screen.height < 1) return false;
            if (ui == null) ui = FindFirstObjectByType<MapReviewController>();
            float screenHeight = Screen.height;
            float canvasScale = Mathf.Sqrt((Screen.width / 1920f) * (screenHeight / 1080f));
            float bottom = .26f, top = 1 - 58 * canvasScale / screenHeight;
            if (ui != null && ui.isActiveAndEnabled)
            {
                if (ui.CardBar == null || ui.TopBar == null)
                {
                    if (Application.isPlaying) return false;
                }
                else
                {
                    var canvas = ui.CardBar.GetComponentInParent<Canvas>();
                    if (canvas == null || ui.CardBar.rect.height <= 0 || ui.TopBar.rect.height <= 0) return false;
                    Camera canvasCamera = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
                    ui.CardBar.GetWorldCorners(uiCorners);
                    bottom = RectTransformUtility.WorldToScreenPoint(canvasCamera, uiCorners[1]).y / screenHeight;
                    ui.TopBar.GetWorldCorners(uiCorners);
                    top = RectTransformUtility.WorldToScreenPoint(canvasCamera, uiCorners[0]).y / screenHeight;
                    canvasScale = canvas.scaleFactor;
                }
            }
            float margin = Mathf.Max(0, profile.uiMarginPixels) * canvasScale / screenHeight;
            if (top - bottom <= margin * 2) return false;
            usable = Rect.MinMaxRect(.025f, bottom + margin, .975f, top - margin);
            return true;
        }

        private float FindReferenceGroundHeight()
        {
            groundCandidates.Clear();
            foreach (var item in environment.GetComponentsInChildren<Transform>(true))
                if (CanonicalName(item.name).StartsWith("Node terrace ", StringComparison.OrdinalIgnoreCase))
                    groundCandidates.Add(item.position.y);
            if (groundCandidates.Count == 0)
                foreach (var item in environment.GetComponentsInChildren<Transform>(true))
                    if (item.name.StartsWith("ANCHOR_Node_L", StringComparison.OrdinalIgnoreCase) &&
                        !item.name.Equals("ANCHOR_Node_L06_01", StringComparison.OrdinalIgnoreCase))
                        groundCandidates.Add(item.position.y);
            if (groundCandidates.Count == 0) return environment.position.y;
            groundCandidates.Sort();
            return groundCandidates[groundCandidates.Count / 2];
        }

        public static bool IsBackgroundRenderer(Renderer renderer, Transform environmentRoot)
        {
            if (renderer == null) return true;
            for (Transform item = renderer.transform; item != null && item != environmentRoot; item = item.parent)
            {
                string name = CanonicalName(item.name);
                foreach (string group in BackgroundGroups)
                    if (name.Equals(group, StringComparison.OrdinalIgnoreCase)) return true;
                foreach (string prefix in BackgroundPrefixes)
                    if (name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        public bool AreSubjectBoundsInsideSafeViewport(float tolerance = .001f) =>
            HasValidFraming && SubjectFits(Mathf.Max(0, tolerance));
        private bool SubjectFits(float tolerance)
        {
            if (view == null || corners.Count == 0) return false;
            foreach (Vector3 corner in corners)
            {
                Vector3 point = view.WorldToViewportPoint(corner);
                if (point.z <= view.nearClipPlane || point.x < SafeViewport.xMin - tolerance ||
                    point.x > SafeViewport.xMax + tolerance || point.y < SafeViewport.yMin - tolerance ||
                    point.y > SafeViewport.yMax + tolerance) return false;
            }
            return true;
        }
        private static string CanonicalName(string name) => name.Replace('_', ' ').Trim();
        private static bool SameRect(Rect a, Rect b) => Mathf.Abs(a.xMin - b.xMin) < .00001f &&
            Mathf.Abs(a.yMin - b.yMin) < .00001f && Mathf.Abs(a.xMax - b.xMax) < .00001f &&
            Mathf.Abs(a.yMax - b.yMax) < .00001f;
    }
}
