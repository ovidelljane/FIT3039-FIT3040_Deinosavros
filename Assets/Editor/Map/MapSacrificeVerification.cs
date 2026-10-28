using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

// Disposable play-session verification, with captures preserving the actual map camera projection.
public static class MapSacrificeVerification
{
    public static IEnumerator Run(Action<bool, string> check) => MapCardDetailVerification.Run(check);

    public static IEnumerator VerifyMaterialAppearance(MapSacrificePanel panel, Action<bool,string> check)
    {
        var source = panel.DetailMaterial;
        float radius = source.GetFloat("_CornerRadius"), height = source.GetFloat("_ShadeHeight"),
            opacity = source.GetFloat("_ShadeOpacity"), softness = source.GetFloat("_ShadeSoftness");
        var effect = panel.burnEffect;
        try
        {
            source.SetFloat("_ShadeHeight",.50f); source.SetFloat("_ShadeOpacity",1); source.SetFloat("_ShadeSoftness",.25f);
            foreach (float testRadius in new[] { 0f,200f })
            {
                source.SetFloat("_CornerRadius",testRadius);
                yield return null; yield return null;
                check(Mathf.Approximately(panel.illustration.material.GetFloat("_CornerRadius"),testRadius),
                    "Material corner radius remains editable at zero and above the previous slider limit.");
                check(effect.Begin(panel.cardPreview),"Capture the real composed card to verify black opacity and rounded clipping.");
                var target = (RenderTexture)effect.GetComponentsInChildren<RawImage>().Single(image => image.texture is RenderTexture).texture;
                var previous = RenderTexture.active;
                var sample = new Texture2D(target.width,target.height,TextureFormat.RGBA32,false);
                try
                {
                    RenderTexture.active = target;
                    sample.ReadPixels(new Rect(0,0,target.width,target.height),0,0); sample.Apply();
                    File.WriteAllBytes(MapSacrificeAuthoring.Results+"/black-mask-radius-"+testRadius+".png",sample.EncodeToPNG());
                    var bounds = Field<Rect>(effect,"bounds");
                    Color ArtworkPixel(float x,float y)
                    {
                        var artwork = panel.illustration.rectTransform;
                        var point = panel.cardPreview.InverseTransformPoint(artwork.TransformPoint(
                            new Vector3(Mathf.Lerp(artwork.rect.xMin,artwork.rect.xMax,x),Mathf.Lerp(artwork.rect.yMin,artwork.rect.yMax,y),0)));
                        return sample.GetPixel(Mathf.Clamp(Mathf.RoundToInt((point.x-bounds.xMin)/bounds.width*target.width),0,target.width-1),
                            Mathf.Clamp(Mathf.RoundToInt((point.y-bounds.yMin)/bounds.height*target.height),0,target.height-1));
                    }
                    var black = ArtworkPixel(.5f,.125f);
                    check(Mathf.Max(black.r,Mathf.Max(black.g,black.b)) < .01f && black.a > .98f,
                        "Opacity one produces actual opaque black across the lower card, not only the bottom edge.");
                    var corner = ArtworkPixel(.06f,.04f);
                    check(testRadius == 0 ? corner.a > .98f : corner.a < .02f,
                        "Rendered corner pixels change from square to rounded without script overrides. radius="+testRadius+" alpha="+corner.a+" size="+target.width+"x"+target.height);
                }
                finally { RenderTexture.active = previous; Object.Destroy(sample); effect.Clear(); }
            }
        }
        finally
        {
            effect.Clear(); source.SetFloat("_CornerRadius",radius); source.SetFloat("_ShadeHeight",height);
            source.SetFloat("_ShadeOpacity",opacity); source.SetFloat("_ShadeSoftness",softness);
        }
        yield return null; yield return null;
    }


    public static IEnumerator VerifyBurn(MapSacrificePanel panel, Canvas canvas, Action<bool, string> check)
    {
        var effect = panel.burnEffect;
        check(effect != null && effect.IsReady && !ShaderUtil.ShaderHasError(effect.cardTemplate.material.shader),
            "The authored sacrifice burn template and shader are ready.");
        var sourcePosition = panel.cardPreview.localPosition;
        var sourceScale = panel.cardPreview.localScale;
        var panelPosition = panel.panel.localPosition;
        check(effect.transform.parent == panel.overlay.transform && effect.focusAnchor != null && panel.playerSymbol != null,
            "The ceremony is authored outside the left panel with a focus anchor and player binding.");
        check(effect.Begin(panel.cardPreview), "A complete preview is captured without touching any run data.");
        panel.cardGroup.alpha = 0;
        yield return null;
        var image = effect.GetComponentsInChildren<RawImage>().Single(i => i.texture is RenderTexture);
        effect.EvaluateFrame(new SacrificeTimelineFrame { move = .5f, burnAge = -1, burnDuration = 1 });
        check(effect.Progress == 0 && effect.VisibleParticles == 0, "The card travels intact before the burn starts.");
        Capture(canvas, Camera.main, "center-01-travel", check);
        effect.EvaluateFrame(new SacrificeTimelineFrame { move = 1, burnAge = -1, burnDuration = 1 });
        check(Vector3.Distance(image.transform.position, effect.focusAnchor.position) < .01f,
            "The intact card arrives at the authored center anchor.");
        check(Mathf.Abs(image.transform.lossyScale.x / panel.cardPreview.lossyScale.x - 1) < .001f,
            "The visual snapshot reaches the center before it expands.");
        Capture(canvas, Camera.main, "center-02-held", check);
        var texture = (RenderTexture)image.texture;
        var active = RenderTexture.active;
        var sample = new Texture2D(texture.width, texture.height, TextureFormat.RGBA32, false);
        try
        {
            RenderTexture.active = texture; sample.ReadPixels(new Rect(0,0,texture.width,texture.height),0,0); sample.Apply();
            var pixels = sample.GetPixels32();
            check(pixels.Any(p => p.a == 0) && pixels.Count(p => p.a > 200) > pixels.Length/4,
                "The composed frame and art retain real transparent boundaries.");
            File.WriteAllBytes(MapSacrificeAuthoring.Results + "/burn-face.png", sample.EncodeToPNG());
        }
        finally { RenderTexture.active = active; Object.Destroy(sample); }
        float[] ages = { .08f, .30f, .63f, 1.08f };
        string[] names = { "burn-01-start", "burn-02-char", "burn-03-through", "burn-04-ash" };
        for (int i = 0; i < ages.Length; i++)
        {
            effect.EvaluateFrame(new SacrificeTimelineFrame { elapsed = ages[i], move = 1, expand = 1,
                burn = Mathf.Clamp01(ages[i]), burnAge = ages[i], burnDuration = 1 }); Canvas.ForceUpdateCanvases();
            Capture(canvas, Camera.main, names[i], check);
            if (i == 2) check(effect.VisibleParticles > 0 && effect.VisibleParticles <= effect.particleCount,
                "The burn emits a bounded set of ash and sparks.");
        }
        check(!image.enabled && effect.Progress == 1, "The card is completely consumed before the last embers settle.");
        check(effect.GetComponentsInChildren<Graphic>(true).All(g => !g.raycastTarget), "All burn presentation ignores pointer input.");
        effect.Clear(); panel.cardGroup.alpha = 1;
        check(!effect.IsPlaying && effect.VisibleParticles == 0 && panel.cardPreview.localPosition == sourcePosition && panel.cardPreview.localScale == sourceScale && panel.panel.localPosition == panelPosition,
            "Clearing leaves the authored card layout untouched.");
        check(effect.Begin(panel.cardPreview), "The snapshot resources can be reused.");
        effect.enabled = false;
        check(!effect.IsPlaying && effect.VisibleParticles == 0, "Disable interrupts presentation without stale sparks.");
        effect.enabled = true;
        check(effect.Begin(panel.cardPreview), "Reenable restores the effect without rebuilding the panel.");
        effect.Clear();
        check(effect.GetComponentsInChildren<RawImage>(true).Length == 2 && effect.GetComponentsInChildren<SacrificeAshGraphic>(true).Length == 1,
            "Repeated previews reuse one face and one ash mesh.");
        var player = panel.playerSymbol;
        var core = player.transform.Find("Core");
        var coreScale = core.localScale;
        var floor = player.FloorPosition;
        player.SetSacrificePulse(.28f, panel.shieldColor);
        check(core.localScale.x > coreScale.x * 1.1f && player.FloorPosition == floor,
            "The fire visibly expands without moving its floor position.");
        player.enabled = false;
        check(player.SacrificePulse == 0 && Vector3.Distance(core.localScale, coreScale) < .001f,
            "Disabling the player renderer clears feedback without leaving it enlarged.");
        player.enabled = true;
    }

    public static IEnumerator VerifyDeparture(MapController map, Action<bool, string> check)
    {
        var session = map.Session; var panel = map.SacrificePanel;
        var definitions = AssetDatabase.LoadAssetAtPath<CardPool>("Assets/Data/CardPool.asset").Cards;
        var costThree = definitions.First(c => c.capacityCost == 3);
        var costTwo = definitions.First(c => c.capacityCost == 2);
        check(session.DeckCapacity == 22 && session.TryAddCard(costThree, out _) && session.DeckCapacity == 25,
            "The boundary fixture has exactly 25 capacity.");
        check(!map.NeedsSacrificeReminder(MapEncounterKind.Battle), "Exactly 25 never prompts.");
        check(session.TryAddCard(costTwo, out _) && session.DeckCapacity == 27, "The upper fixture has 27 capacity.");
        check(map.NeedsSacrificeReminder(MapEncounterKind.Battle) && map.NeedsSacrificeReminder(MapEncounterKind.Boss), "Battle and Boss warn above 25.");
        check(!map.NeedsSacrificeReminder(MapEncounterKind.Opportunity) && !map.NeedsSacrificeReminder(MapEncounterKind.Recovery), "Noncombat destinations do not warn.");
        yield return null;
        var start = map.Nodes.Single(n => n.NodeId == session.Progress.CurrentNodeId);
        string current = session.Progress.CurrentNodeId;
        map.SelectNode(start);
        check(panel.IsDepartureWarningOpen && !map.CanInteract && session.Progress.CurrentEncounter == null && session.Progress.CurrentNodeId == current,
            "The reminder blocks input before movement, a travel ticket or any resource changes.");
        map.SelectNode(start);
        check(session.Progress.CurrentEncounter == null, "Repeated map clicks cannot bypass the reminder.");
        Capture(panel.GetComponentInParent<Canvas>().rootCanvas, Camera.main, "departure-warning", check);
        panel.departureBack.onClick.Invoke(); yield return Until(() => map.CanInteract);
        check(!panel.IsDepartureWarningOpen && session.DeckCapacity == 27 && session.Progress.CurrentNodeId == current && !session.SacrificeUsed,
            "Returning from the reminder changes no run data.");
        var instance = session.RunDeck.Last(c => c.definition == costTwo && !c.sacrificed);
        check(session.TrySacrificeInstance(instance.instanceId) && session.TryAddCard(costTwo, out _), "The prepared-offering fixture remains above 25.");
        check(session.DeckCapacity == 27 && !map.NeedsSacrificeReminder(MapEncounterKind.Battle), "An already prepared offering suppresses the reminder.");
        check(map.TryRestartRunForTesting(), "The confirmed departure uses a fresh disposable run.");
        session.TryAddCard(costThree, out _); session.TryAddCard(costTwo, out _); yield return null;
        start = map.Nodes.Single(n => n.NodeId == session.Progress.CurrentNodeId);
        map.SelectNode(start); panel.departureContinue.onClick.Invoke(); panel.departureContinue.onClick.Invoke();
        string encounter = session.Progress.CurrentEncounter?.EncounterId;
        check(!string.IsNullOrEmpty(encounter) && !session.SacrificeUsed && !session.HasPendingModifier, "Confirming enters once without inventing a sacrifice.");
        yield return Until(() => SceneManager.GetActiveScene().name == "Deinosavros" && TimeTickSystem.Active?.IsStarted == true);
        check(session.Progress.CurrentEncounter.EncounterId == encounter, "Repeated confirmation creates only one encounter.");
        check(!Object.FindObjectsByType<Camera>(FindObjectsInactive.Include, FindObjectsSortMode.None)
            .Any(c => c.transform.parent != null && c.transform.parent.name == "Sacrifice Snapshot"),
            "Leaving Map releases the offscreen sacrifice camera.");
        var card = Object.FindObjectsByType<BuffCards>(FindObjectsSortMode.None).First(c => c.isActiveAndEnabled);
        var label = Field<TextMeshProUGUI>(card, "costText"); label.ForceMeshUpdate();
        check((label.fontStyle & FontStyles.Bold) != 0 && label.fontSize > 22 && !label.isTextOverflowing, "Combat cost digits are enlarged, bold and remain readable.");
    }

    public static void Capture(Canvas canvas, Camera camera, string name, Action<bool, string> check)
    {
        var scaler = canvas.GetComponent<CanvasScaler>(); var rect = (RectTransform)canvas.transform;
        var mode = canvas.renderMode; var assignedCamera = canvas.worldCamera; bool scaleEnabled = scaler.enabled;
        var position = rect.localPosition; var rotation = rect.localRotation; var localScale = rect.localScale; var delta = rect.sizeDelta;
        float scale = canvas.scaleFactor, aspect = camera.aspect; var target = camera.targetTexture; var active = RenderTexture.active;
        try
        {
            scaler.enabled = false; canvas.renderMode = RenderMode.WorldSpace; canvas.worldCamera = camera; canvas.scaleFactor = 1;
            foreach (var size in new[] { new Vector2Int(1920,1080), new Vector2Int(2560,1440), new Vector2Int(1920,1200) })
            {
                camera.aspect = (float)size.x / size.y;
                float uiScale = Mathf.Sqrt(size.x / 1920f * size.y / 1080f);
                float height = size.y / uiScale;
                rect.sizeDelta = new(size.x / uiScale, height);
                float distance = camera.nearClipPlane + 1;
                rect.position = camera.transform.position + camera.transform.forward * distance; rect.rotation = camera.transform.rotation;
                rect.localScale = Vector3.one * (2 * distance * Mathf.Tan(camera.fieldOfView * Mathf.Deg2Rad * .5f) / height);
                Canvas.ForceUpdateCanvases();
                var panel = canvas.GetComponentInChildren<MapSacrificePanel>(true);
                if (panel != null && panel.IsBusy) panel.RefreshFeedbackPresentation();
                else if (panel != null && panel.burnEffect != null && panel.burnEffect.IsPlaying)
                    panel.burnEffect.EvaluateFrame(panel.burnEffect.CurrentFrame);
                Canvas.ForceUpdateCanvases();
                if (panel != null && panel.IsOpen)
                {
                    var corners = new Vector3[4]; panel.panel.GetWorldCorners(corners);
                    foreach (var p in corners) { var v = camera.WorldToViewportPoint(p); check(v.x >= 0 && v.x < .27f && v.y > .26f && v.y < .95f, "Offering stays left of the map and above the cards at " + size); }
                }
                if (panel != null && panel.IsDepartureWarningOpen)
                {
                    foreach (var label in panel.departurePanel.GetComponentsInChildren<TMP_Text>())
                    { label.ForceMeshUpdate(); check(!label.isTextOverflowing, "Departure reminder text fits at " + size); }
                }
                var rt = new RenderTexture(size.x, size.y, 24, RenderTextureFormat.ARGB32); Texture2D image = null;
                try
                {
                    rt.Create(); camera.targetTexture = rt;
                    RenderPipeline.SubmitRenderRequest(camera, new UniversalRenderPipeline.SingleCameraRequest { destination = rt });
                    RenderTexture.active = rt; image = new Texture2D(size.x, size.y, TextureFormat.RGB24, false);
                    image.ReadPixels(new Rect(0,0,size.x,size.y),0,0); image.Apply();
                    Directory.CreateDirectory(MapSacrificeAuthoring.Results);
                    File.WriteAllBytes(MapSacrificeAuthoring.Results + "/" + name + "-" + size.x + "x" + size.y + ".png", image.EncodeToPNG());
                }
                finally { camera.targetTexture = target; RenderTexture.active = active; if (image != null) Object.DestroyImmediate(image); rt.Release(); Object.DestroyImmediate(rt); }
            }
        }
        finally
        {
            camera.aspect = aspect; canvas.renderMode = mode; canvas.worldCamera = assignedCamera; canvas.scaleFactor = scale;
            rect.localPosition = position; rect.localRotation = rotation; rect.localScale = localScale; rect.sizeDelta = delta;
            scaler.enabled = scaleEnabled; Canvas.ForceUpdateCanvases();
            var panel = canvas.GetComponentInChildren<MapSacrificePanel>(true);
            if (panel != null && panel.IsBusy) panel.RefreshFeedbackPresentation();
            else if (panel != null && panel.burnEffect != null && panel.burnEffect.IsPlaying)
                panel.burnEffect.EvaluateFrame(panel.burnEffect.CurrentFrame);
        }
    }
    private static IEnumerator Until(Func<bool> condition)
    { float end = Time.realtimeSinceStartup + 60; while (!condition()) { if (Time.realtimeSinceStartup > end) throw new TimeoutException("Sacrifice check timed out."); yield return null; } }
    private static IEnumerator Delay(float seconds) { float end = Time.realtimeSinceStartup + seconds; while (Time.realtimeSinceStartup < end) yield return null; }
    private static T Field<T>(object target, string name) => (T)target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(target);
}
