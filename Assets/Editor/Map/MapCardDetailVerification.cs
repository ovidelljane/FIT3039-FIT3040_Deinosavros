using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.Playables;
using UnityEngine.Timeline;
using UnityEngine.UI;
using Object = UnityEngine.Object;

public static class MapCardDetailVerification
{
    public static void VerifyTimelinePreview()
    {
        var scene = EditorSceneManager.OpenPreviewScene("Assets/Scenes/Map.unity");
        var timelineBytes = File.ReadAllText("Assets/UI/MapSacrifice.playable");
        int checks = 0;
        void Check(bool value, string message) { checks++; if (!value) throw new InvalidOperationException(message); }
        try
        {
            var panel = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<MapSacrificePanel>(true)).Single();
            var director = panel.sequenceDirector;
            var timeline = (TimelineAsset)director.playableAsset;
            var tracks = timeline.GetOutputTracks().ToArray();
            Check(tracks.OfType<AnimationTrack>().Count() == 5 && tracks.OfType<AudioTrack>().Count() == 1,
                "The ceremony consists of five native animation tracks and one native audio track.");
            Check(tracks.All(track => director.GetGenericBinding(track) != null), "All native tracks have saved scene bindings.");
            foreach (var track in tracks.OfType<AnimationTrack>())
                Check(track.GetClips().All(clip => clip.recordable && ((AnimationPlayableAsset)clip.asset).clip != null),
                    track.name + " exposes editable native animation curves.");
            var fire = tracks.OfType<AudioTrack>().Single().GetClips().Single();
            Check(((AudioPlayableAsset)fire.asset).clip != null &&
                director.GetGenericBinding(tracks.OfType<AudioTrack>().Single()) is AudioSource,
                "The visible audio clip directly owns fire timing, fades and the scene audio source.");
            bool overlay = panel.overlay.activeSelf, button = panel.confirm.gameObject.activeSelf;
            var shade = panel.feedbackBackdrop.color; float alpha = panel.cardGroup.alpha;
            var cardDefinition = AssetDatabase.LoadAssetAtPath<CardDefinition>("Assets/Data/Cards/SolarShield.asset");
            string definition = EditorJsonUtility.ToJson(cardDefinition);
            Check(panel.PrepareTimelinePreview(), "Editor preview captures the selected artwork without a sacrifice transaction.");
            var card = NativeAnimation(timeline, "Card");
            var burn = NativeAnimation(timeline, "Burn");
            var scale = AnimationUtility.GetEditorCurve(card, EditorCurveBinding.FloatCurve("", typeof(Transform), "m_LocalScale.x"));
            var progress = AnimationUtility.GetEditorCurve(burn, EditorCurveBinding.FloatCurve("", typeof(SacrificeBurnEffect), "burnProgress"));
            float burnStart = progress.keys.Last(key => key.value <= 0).time;
            float burnEnd = progress.keys.First(key => key.value >= 1).time;
            float sampleTime = Mathf.Lerp(burnStart, burnEnd, .5f);
            director.time = sampleTime; director.Evaluate(); panel.burnEffect.RefreshPresentation();
            Check(panel.IsTimelinePreview && !panel.IsBusy && panel.burnEffect.IsPlaying,
                "Native evaluation previews presentation without removing a card.");
            Check(Mathf.Abs(panel.burnEffect.Progress - progress.Evaluate(sampleTime)) < .002f,
                "The ordinary Animation Window burn curve directly controls the shader.");
            Check(Mathf.Abs(panel.burnEffect.cardTemplate.rectTransform.localScale.x - scale.Evaluate(sampleTime)) < .002f,
                "The ordinary transform scale curve directly controls expansion.");
            var constraint = panel.burnEffect.cardTemplate.GetComponent<UnityEngine.Animations.PositionConstraint>();
            Check(constraint != null && constraint.GetSource(0).sourceTransform == panel.cardPreview &&
                constraint.GetSource(1).sourceTransform == panel.burnEffect.focusAnchor,
                "Native position constraints expose both the detail source and editable center target.");
            var motion = AnimationUtility.GetEditorCurve(card,
                EditorCurveBinding.FloatCurve("", typeof(UnityEngine.Animations.PositionConstraint), "m_Sources.Array.data[1].weight"));
            director.time = .4; director.Evaluate();
            Check(Mathf.Abs(constraint.GetSource(1).weight - motion.Evaluate(.4f)) < .002f,
                "Scrubbing changes the native position source weight without script interpolation.");
            director.time = 0; director.Evaluate(); panel.burnEffect.RefreshPresentation();
            Check(panel.burnEffect.Progress == 0 && panel.feedbackBackdrop.color.a == 0,
                "Scrubbing backward restores an intact card and a clear screen. burn=" +
                panel.burnEffect.Progress + " dim=" + panel.feedbackBackdrop.color.a);
            director.Stop(); panel.EndTimelinePreview();
            Check(!panel.IsTimelinePreview && !panel.burnEffect.IsPlaying && panel.overlay.activeSelf == overlay &&
                panel.confirm.gameObject.activeSelf == button && panel.cardGroup.alpha == alpha && panel.feedbackBackdrop.color == shade,
                "Stopping preview restores the authored detail state.");
            Check(EditorJsonUtility.ToJson(cardDefinition) == definition &&
                File.ReadAllText("Assets/UI/MapSacrifice.playable") == timelineBytes, "Preview changes no card data or saved animation.");
            Directory.CreateDirectory(MapSacrificeAuthoring.Results);
            File.WriteAllText(MapSacrificeAuthoring.Results + "/timeline-preview.txt", "PASS: " + checks +
                " checks; native animation and audio tracks, editable shader and transform curves, constraints, reverse seeking and cleanup.\n");
        }
        finally { EditorSceneManager.ClosePreviewScene(scene); }
    }

    private static AnimationClip NativeAnimation(TimelineAsset timeline, string track) =>
        ((AnimationPlayableAsset)timeline.GetOutputTracks().Single(item => item.name == track).GetClips().Single().asset).clip;

    public static IEnumerator RunTimeline(Action<bool, string> check)
    {
        var menu = Object.FindFirstObjectByType<MainMenuController>();
        typeof(MainMenuController).GetMethod("StartRun", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(menu, null);
        yield return Until(() => TimeTickSystem.Active?.IsStarted == true);
        foreach (var enemy in BattleScript.FindFighters("Enemy")) enemy.TakeDamage(10000);
        yield return Until(() => Object.FindFirstObjectByType<RewardScreenController>()?.IsPresented == true);
        Field<Button>(Object.FindFirstObjectByType<RewardScreenController>(), "skipButton").onClick.Invoke();
        yield return Until(() => Object.FindFirstObjectByType<MapController>() is MapController m && m.CanInteract);
        var map = Object.FindFirstObjectByType<MapController>(); var panel = map.SacrificePanel;
        var director = panel.sequenceDirector; var timeline = (TimelineAsset)director.playableAsset;
        var card = Object.FindObjectsByType<MapCardView>(FindObjectsSortMode.None).First(c => c.gameObject.activeInHierarchy);
        map.ShowCardDetail(card); yield return null;
        string id = card.InstanceId; int capacity = map.Session.DeckCapacity;
        panel.confirm.onClick.Invoke(); panel.confirm.onClick.Invoke();
        check(panel.IsBusy && map.Session.RunDeck.Single(c => c.instanceId == id).sacrificed &&
            map.Session.DeckCapacity == capacity - card.Definition.capacityCost, "Native playback commits the exact instance only once.");
        yield return Until(() => director.time >= .82);
        yield return null;
        var face = panel.burnEffect.cardTemplate;
        check(panel.burnEffect.Progress > 0 && face.transform.localScale.x > 1,
            "Real native playback burns while expanding.");
        check(Vector3.Distance(face.transform.position, panel.burnEffect.focusAnchor.position) < 1,
            "Native constraints bring the card to the center at the actual game resolution.");
        var fire = timeline.GetOutputTracks().OfType<AudioTrack>().Single();
        var source = (AudioSource)director.GetGenericBinding(fire);
        check(source != null && source.isPlaying, "The native audio track plays during the burn.");
        director.Pause();
        float heldProgress = panel.burnEffect.Progress;
        var heldPosition = face.transform.position;
        yield return Delay(.12f);
        check(Mathf.Approximately(panel.burnEffect.Progress, heldProgress) &&
            Vector3.Distance(face.transform.position, heldPosition) < .001f,
            "Pausing the native director holds both shader progress and card motion.");
        director.Resume();
        var canvas = panel.GetComponentInParent<Canvas>();
        MapSacrificeVerification.Capture(canvas.rootCanvas, Camera.main, "native-timeline-burn", check);
        yield return Until(() => director.time >= 2.1 || !panel.IsBusy);
        check(panel.IsBusy && panel.playerSymbol.SacrificePulse > 0, "The native player curve produces visible enhancement.");
        yield return Until(() => !panel.IsBusy && map.CanInteract);
        check(!source.isPlaying && panel.playerSymbol.SacrificePulse == 0 &&
            panel.feedbackBackdrop.color.a == 0 && !panel.IsOpen, "Completion releases audio, glow, dimmer and input.");
        check(map.Session.HasPendingModifier && map.Session.RunDeck.Count(c => c.instanceId == id && c.sacrificed) == 1,
            "Native presentation preserves the pending effect and exact-instance result.");
    }

    public static IEnumerator RunHover(Action<bool,string> check)
    {
        var menu = Object.FindFirstObjectByType<MainMenuController>();
        typeof(MainMenuController).GetMethod("StartRun",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(menu,null);
        yield return Until(() => TimeTickSystem.Active?.IsStarted == true);
        foreach (var enemy in BattleScript.FindFighters("Enemy")) enemy.TakeDamage(10000);
        yield return Until(() => Object.FindFirstObjectByType<RewardScreenController>()?.IsPresented == true);
        Field<Button>(Object.FindFirstObjectByType<RewardScreenController>(),"skipButton").onClick.Invoke();
        yield return Until(() => Object.FindFirstObjectByType<MapController>() is MapController m && m.NodeInteraction != null && m.CanInteract);
        var map = Object.FindFirstObjectByType<MapController>();
        var panel = map.SacrificePanel;
        var module = EventSystem.current.currentInputModule;
        bool inputEnabled = module != null && module.enabled;
        bool nodeEnabled = map.NodeInteraction.enabled;
        if (module != null) module.enabled = false;
        map.NodeInteraction.enabled = false;
        var cards = Object.FindObjectsByType<MapCardView>(FindObjectsSortMode.None).Where(c => c.gameObject.activeInHierarchy)
            .OrderBy(c => c.transform.position.x).ToArray();
        foreach (var view in cards)
        {
            var fill = Field<Image>(view,"backFill");
            check(fill.sprite == view.Definition.FrontIllustration && fill.material.shader == panel.illustration.material.shader &&
                fill.material.GetFloat("_Mirror") == 1 && fill.color == Color.white,
                view.Definition.name + ": the deck back reuses the mirrored detail illustration and shader.");
        }
        var source = cards.Last(); var other = cards[cards.Length / 2]; var third = cards.First();
        var pointer = new PointerEventData(EventSystem.current) { button = PointerEventData.InputButton.Left };
        var tick = typeof(MapSacrificePanel).GetMethod("TickHover",BindingFlags.Instance|BindingFlags.NonPublic);
        var refresh = typeof(MapSacrificePanel).GetMethod("RefreshFace",BindingFlags.Instance|BindingFlags.NonPublic);
        var fade = typeof(MapSacrificePanel).GetMethod("TickDetailFade",BindingFlags.Instance|BindingFlags.NonPublic);
        var nodes = map.Nodes.Select(n => (n.NodeId,n.transform.position,n.transform.localScale)).ToArray();
        var deck = Field<Transform>(map,"deckContainer");
        float spacing = deck.GetComponent<HorizontalLayoutGroup>().spacing;
        int children = panel.GetComponentsInChildren<Transform>(true).Length;
        // Disable only the real pointer polling; synthetic events and the production state machine still run.
        panel.enabled = false;
        yield return null; yield return null;
        var corners = new Vector3[4]; panel.panel.GetWorldCorners(corners);
        Vector2 detailMin = RectTransformUtility.WorldToScreenPoint(null,corners[0]);
        Vector2 detailMax = RectTransformUtility.WorldToScreenPoint(null,corners[2]);
        Vector2 origin = RectTransformUtility.WorldToScreenPoint(null,source.transform.position);
        origin.y = Mathf.Min(origin.y,detailMin.y - 60);
        Vector2 entry = new(detailMax.x,detailMin.y + 24);
        Vector2 normal = new(origin.x - 180,origin.y);
        float now = 0;
        void Enter(MapCardView card,Vector2 position) { pointer.position = position; card.OnPointerEnter(pointer); }
        void Exit(MapCardView card,Vector2 position) { pointer.position = position; card.OnPointerExit(pointer); }
        void Tick(Vector2 position,float elapsed) => tick.Invoke(panel,new object[] { position,now + elapsed });
        void Reset()
        {
            foreach (var card in cards) Exit(card,origin);
            panel.HideImmediate(); now = Time.unscaledTime;
            Enter(source,origin);
        }
        try
        {
            float originalFadeIn = panel.fadeInDuration, originalFadeOut = panel.fadeOutDuration;
            panel.fadeInDuration = .18f; panel.fadeOutDuration = .14f;
            check(panel.detailGroup != null && panel.detailGroup.transform == panel.panel && panel.detailGroup != panel.cardGroup,
                "Saved detail fade covers the full panel without sharing the sacrifice Timeline group.");
            void Fade(float seconds) => fade.Invoke(panel,new object[] { seconds });
            Reset();
            check(panel.detailGroup.alpha == 0 && panel.IsOpen,"First hover starts the existing shader and text fully transparent.");
            MapSacrificeVerification.Capture(panel.GetComponentInParent<Canvas>(),Camera.main,"detail-fade-start",check);
            Fade(.09f);
            check(panel.detailGroup.alpha > 0 && panel.detailGroup.alpha < 1 && panel.cardGroup.alpha == 1,
                "Halfway fade changes the parent visibility, leaving the Timeline alpha untouched.");
            MapSacrificeVerification.Capture(panel.GetComponentInParent<Canvas>(),Camera.main,"detail-fade-half",check);
            float partial = panel.detailGroup.alpha;
            panel.Close(); Fade(.02f);
            check(!panel.IsOpen && panel.overlay.activeSelf && panel.detailGroup.alpha < partial && !panel.detailGroup.interactable,
                "Closing fades the still-rendered detail and disables sacrifice immediately.");
            partial = panel.detailGroup.alpha;
            panel.Open(source);
            check(panel.IsOpen && panel.detailGroup.alpha == partial,"Rapid hover re-entry reverses from the current opacity without a flash.");
            Fade(.18f);
            check(panel.detailGroup.alpha == 1,"The complete card, text and button reach full visibility.");
            MapSacrificeVerification.Capture(panel.GetComponentInParent<Canvas>(),Camera.main,"detail-fade-full",check);
            panel.Close(); Fade(.07f); partial = panel.detailGroup.alpha;
            panel.OnPointerEnter(pointer);
            check(panel.IsOpen && panel.detailGroup.alpha == partial,"Entering the fading detail itself also cancels its dismissal.");
            panel.Close(); Fade(.15f);
            check(!panel.overlay.activeSelf && panel.SelectedInstanceId == null && panel.detailGroup.alpha == 0 && !panel.detailGroup.blocksRaycasts,
                "Completed fade-out releases references and leaves no invisible input blocker.");
            panel.fadeInDuration = panel.fadeOutDuration = 0;
            panel.Open(source);
            check(panel.detailGroup.alpha == 1,"Zero fade-in duration shows detail immediately.");
            panel.Close();
            check(!panel.overlay.activeSelf,"Zero fade-out duration closes detail immediately.");
            panel.fadeInDuration = originalFadeIn; panel.fadeOutDuration = originalFadeOut;
            Reset(); Fade(.18f); panel.Close();
            panel.ShowDepartureWarning(map.Nodes[0].NodeId); Fade(1);
            check(panel.IsDepartureWarningOpen && !panel.overlay.activeSelf,"A pending detail fade cannot dismiss a new departure warning.");
            panel.CancelDeparture();
            // Closing intentionally blocks background interaction through the next frame.
            yield return null; yield return null;
            Reset(); Fade(panel.fadeInDuration + .01f);
            int capacityBeforeSwitch = map.Session.DeckCapacity;
            panel.Open(other); refresh.Invoke(panel,null);
            panel.Confirm();
            check(panel.SelectedInstanceId == source.InstanceId && !panel.confirm.interactable &&
                !panel.detailGroup.interactable && map.Session.DeckCapacity == capacityBeforeSwitch,
                "A visible-card switch retains the old content and blocks sacrifice until the new instance is shown.");
            Fade(panel.fadeOutDuration * .5f);
            check(panel.detailGroup.alpha > 0 && panel.detailGroup.alpha < 1 && panel.SelectedInstanceId == source.InstanceId &&
                panel.illustration.sprite == source.Definition.FrontIllustration,
                "The current card fades out before its artwork and text are replaced.");
            panel.Open(third); Fade(panel.fadeOutDuration + .01f);
            check(panel.detailGroup.alpha == 0 && panel.SelectedInstanceId == third.InstanceId &&
                panel.illustration.sprite == third.Definition.FrontIllustration,
                "Rapid retargeting shows only the latest requested instance at the transparent midpoint.");
            Fade(panel.fadeInDuration * .5f);
            check(panel.detailGroup.alpha > 0 && panel.detailGroup.alpha < 1 && panel.SelectedInstanceId == third.InstanceId,
                "The replacement card fades in using the same editable timings.");
            Fade(panel.fadeInDuration);
            panel.Open(other); Fade(panel.fadeOutDuration * .25f);
            partial = panel.detailGroup.alpha;
            panel.Open(third);
            check(panel.SelectedInstanceId == third.InstanceId && panel.detailGroup.alpha == partial,
                "Returning to the displayed card cancels the pending switch without an opacity jump.");
            Fade(panel.fadeInDuration);
            panel.Open(other); panel.statusButton.onClick.Invoke(); Fade(1);
            check(panel.statusPopup.activeSelf && !panel.overlay.activeSelf && Field<MapCardView>(panel,"switchingView") == null,
                "Opening the status popup cancels a queued detail switch instead of being dismissed by it.");
            panel.statusButton.onClick.Invoke();
            Reset(); Fade(panel.fadeInDuration + .01f);
            panel.Open(other); panel.Close(); Fade(panel.fadeOutDuration + .01f); Fade(1);
            check(!panel.overlay.activeSelf && Field<MapCardView>(panel,"switchingView") == null,
                "Closing mid-switch clears the queued card and cannot reopen it later.");
            yield return null; yield return null;
            Reset();
            check(panel.IsOpen && panel.SelectedInstanceId == source.InstanceId && !source.TargetBack,
                "First hover immediately shows the front without flipping.");
            Exit(source,origin); Enter(other,normal); Tick(normal,.08f);
            check(panel.SelectedInstanceId == source.InstanceId,"Briefly crossing another card retains the original detail.");
            Exit(other,normal); Enter(third,normal - Vector2.right * 100); Tick(normal - Vector2.right * 100,.12f);
            check(panel.SelectedInstanceId == source.InstanceId,"A rapid sweep does not flash intermediate card details.");
            Tick(normal - Vector2.right * 100,panel.hoverSwitchDelay + .04f);
            check(panel.SelectedInstanceId == third.InstanceId && !third.TargetBack,"Deliberate hover switches after the configured delay.");

            Reset(); Exit(source,origin);
            Vector2 crossing = Vector2.Lerp(origin,entry,.16f);
            Enter(other,crossing);
            Tick(Vector2.Lerp(origin,entry,.27f),.11f);
            Tick(Vector2.Lerp(origin,entry,.40f),.24f);
            Tick(Vector2.Lerp(origin,entry,.52f),.36f);
            check(panel.IsOpen && panel.SelectedInstanceId == source.InstanceId,"Movement toward detail suppresses replacement beyond the ordinary hover delay.");
            Exit(other,Vector2.Lerp(origin,entry,.52f));
            Tick(Vector2.Lerp(origin,entry,.65f),.48f);
            check(panel.IsOpen && panel.SelectedInstanceId == source.InstanceId,"The protected gap also prevents premature hiding.");
            pointer.position = entry; panel.OnPointerEnter(pointer); Tick(entry,1.1f);
            check(panel.IsOpen && panel.SelectedInstanceId == source.InstanceId,"Entering detail cancels the queued card and retains the correct instance.");
            panel.OnPointerExit(pointer); Tick(entry,panel.hideDelay + 1.2f);
            check(!panel.IsOpen,"Leaving detail uses the normal hide delay.");
            yield return null; yield return null;

            Reset(); Exit(source,origin); Enter(other,crossing);
            Tick(crossing,panel.hoverSwitchDelay + .04f);
            check(panel.SelectedInstanceId == other.InstanceId,"Stopping over another card overrides protection after a deliberate dwell.");

            Reset(); Exit(source,origin);
            Tick(Vector2.Lerp(origin,entry,.30f),.10f);
            Tick(origin + Vector2.right * 40,.16f);
            Tick(origin + Vector2.right * 40,panel.hideDelay + .05f);
            check(!panel.IsOpen,"Reversing away cancels travel protection instead of leaving sticky details.");
            yield return null; yield return null;

            Reset(); Exit(source,origin);
            Tick(crossing,.1f); Tick(crossing,panel.detailTransferTime + .05f);
            check(!panel.IsOpen,"Transfer protection has a bounded lifetime.");
            yield return null; yield return null;

            Reset(); Exit(source,origin); Enter(other,crossing);
            other.OnPointerClick(pointer);
            check(panel.SelectedInstanceId == other.InstanceId && other.TargetBack && map.CardShowsBack(other.InstanceId) && !panel.confirm.interactable,
                "Click immediately chooses and flips the clicked instance despite pending hover protection.");
            yield return Delay(.32f);
            refresh.Invoke(panel,null);
            check(other.IsBack && panel.combatText.text == panel.DetailEffect(other.Definition,true) &&
                panel.illustration.material.GetFloat("_Mirror") == 1 && panel.confirm.interactable,
                "After clicking, detail and card still show the same face and sacrifice data.");
            MapSacrificeVerification.Capture(panel.GetComponentInParent<Canvas>(),Camera.main,"detail-back-artwork",check);
            other.OnPointerClick(pointer); yield return Delay(.32f); refresh.Invoke(panel,null);
            check(!other.IsBack && panel.combatText.text == panel.DetailEffect(other.Definition,false) && !panel.confirm.gameObject.activeSelf && !panel.confirm.interactable,
                "Returning to the front hides and disables the detail sacrifice action.");
            check(spacing == deck.GetComponent<HorizontalLayoutGroup>().spacing && children == panel.GetComponentsInChildren<Transform>(true).Length &&
                nodes.SequenceEqual(map.Nodes.Select(n => (n.NodeId,n.transform.position,n.transform.localScale))),
                "The protection uses no overlay objects and preserves all layout and node transforms.");
            panel.enabled = true;
            string offered = other.InstanceId; int capacity = map.Session.DeckCapacity;
            panel.confirm.onClick.Invoke();
            check(!panel.IsBusy && map.Session.DeckCapacity == capacity && !map.Session.SacrificeUsed,
                "A programmatic front-face submit cannot sacrifice a card.");
            other.OnPointerClick(pointer); yield return Delay(.32f); refresh.Invoke(panel,null);
            check(other.IsBack && panel.confirm.gameObject.activeSelf && panel.confirm.interactable,
                "Only the detail back exposes a usable sacrifice action.");
            panel.confirm.onClick.Invoke(); panel.confirm.onClick.Invoke();
            check(panel.IsBusy && map.Session.RunDeck.Single(c => c.instanceId == offered).sacrificed &&
                map.Session.DeckCapacity == capacity - other.Definition.capacityCost && !map.Session.RunDeck.Single(c => c.instanceId == source.InstanceId).sacrificed,
                "Sacrifice still commits only the inspected instance once after a protected handoff.");
            yield return Until(() => !panel.IsBusy && map.CanInteract);
            check(!panel.IsOpen && Field<MapCardView>(panel,"hoverCandidate") == null,"Sacrifice completion clears deferred hover state.");
            Directory.CreateDirectory("Library/MapHover");
            File.WriteAllText("Library/MapHover/runtime.txt","PASS: real card pointer events, delayed switching, direction-aware transfer, stationary override, leave timeout, click priority, reversible faces, exact-instance sacrifice and unchanged UI hierarchy.\n");
        }
        finally
        {
            panel.HideImmediate(); panel.enabled = true;
            map.NodeInteraction.enabled = nodeEnabled;
            if (module != null) module.enabled = inputEnabled;
        }
    }

    public static IEnumerator Run(Action<bool,string> check)
    {
        var menu = Object.FindFirstObjectByType<MainMenuController>();
        typeof(MainMenuController).GetMethod("StartRun",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(menu,null);
        yield return Until(() => SceneManager.GetActiveScene().name == "Deinosavros" && TimeTickSystem.Active?.IsStarted == true);
        foreach (var enemy in BattleScript.FindFighters("Enemy")) enemy.TakeDamage(10000);
        yield return Until(() => Object.FindFirstObjectByType<RewardScreenController>()?.IsPresented == true);
        var reward = Object.FindFirstObjectByType<RewardScreenController>();
        Field<Button>(reward,"skipButton").onClick.Invoke();
        yield return Until(() => SceneManager.GetActiveScene().name == "Map" && Object.FindFirstObjectByType<MapController>()?.CanInteract == true);
        var map = Object.FindFirstObjectByType<MapController>();
        var panel = map.SacrificePanel; var session = map.Session;
        // Synthetic pointer checks must not race the desktop cursor during the disposable session.
        if (EventSystem.current.currentInputModule != null) EventSystem.current.currentInputModule.enabled = false;
        var canvas = panel.GetComponentInParent<Canvas>().rootCanvas;
        var layout = Field<Transform>(map,"deckContainer").GetComponent<HorizontalLayoutGroup>();
        float spacing = layout.spacing;
        var nodes = map.Nodes.Select(n => (n.NodeId,n.transform.position,n.transform.localScale)).ToArray();
        var first = Object.FindObjectsByType<MapCardView>(FindObjectsSortMode.None).First(v => v.InstanceId != null);
        string firstId = first.InstanceId;
        var pointer = new PointerEventData(EventSystem.current) { button = PointerEventData.InputButton.Left,
            position = RectTransformUtility.WorldToScreenPoint(null,first.transform.position) };
        first.OnPointerEnter(pointer); yield return Delay(.32f);
        panel.OnPointerEnter(pointer);
        check(panel.IsOpen && map.CanInteract && !first.TargetBack && !first.IsBack &&
            panel.combatText.text == panel.DetailEffect(first.Definition,false),"Hover previews the front without flipping or locking the map. open="+
            panel.IsOpen+" interact="+map.CanInteract+" back="+first.TargetBack+" selected="+panel.SelectedInstanceId+" expected="+first.InstanceId);
        check(panel.cardPreview.GetComponentsInChildren<Image>(true).Length == 1 &&
            panel.detailVisual != null && !panel.feedbackBackdrop.raycastTarget && panel.feedbackBackdrop.color.a == 0,
            "Only the authored full-art card remains; normal inspection has no screen-wide dimmer.");
        check(panel.panel.GetComponent<MapCardDetailPointer>()?.owner == panel,
            "Only the detail rectangle, not the global status UI, owns the pointer handoff.");
        check(panel.illustration.material.shader.name == "Deinosavros/UI/Map Card Detail" &&
            !ShaderUtil.ShaderHasError(panel.illustration.material.shader),"The rounded, shaded artwork shader compiles.");
        var sourceMaterial = panel.DetailMaterial;
        float savedHeight = sourceMaterial.GetFloat("_ShadeHeight"), savedOpacity = sourceMaterial.GetFloat("_ShadeOpacity");
        try
        {
            foreach (var values in new[] { new Vector2(.25f,.40f),new Vector2(1,0),new Vector2(0,1) })
            {
                sourceMaterial.SetFloat("_ShadeHeight",values.x); sourceMaterial.SetFloat("_ShadeOpacity",values.y);
                yield return null; yield return null;
                check(Mathf.Approximately(panel.illustration.material.GetFloat("_ShadeHeight"),values.x) &&
                    Mathf.Approximately(panel.illustration.material.GetFloat("_ShadeOpacity"),values.y),
                    "Editing the authored gradient updates the live card without a scripted reset, including zero and full ranges.");
            }
        }
        finally
        {
            sourceMaterial.SetFloat("_ShadeHeight",savedHeight); sourceMaterial.SetFloat("_ShadeOpacity",savedOpacity);
        }
        yield return null;
        yield return MapSacrificeVerification.VerifyMaterialAppearance(panel,check);
        check(map.BlocksMapPointer(RectTransformUtility.WorldToScreenPoint(null,panel.confirm.transform.position)) &&
            !map.BlocksMapPointer(new Vector2(Screen.width*.8f,Screen.height*.6f)),
            "Detail UI captures its own clicks while the map remains interactive outside it.");
        Capture(canvas,"detail-front",check);
        first.OnPointerClick(pointer); yield return Delay(.10f);
        check(first.IsFlipping && !panel.confirm.interactable &&
            Quaternion.Angle(panel.detailVisual.localRotation,Quaternion.Euler(0,first.FlipAngle,0)) < .05f,
            "The detail follows the exact card flip, with commit disabled during the transition. progress=" + first.FlipProgress +
            " interactable=" + panel.confirm.interactable + " angle=" + Quaternion.Angle(panel.detailVisual.localRotation,Quaternion.Euler(0,first.FlipAngle,0)));
        first.OnPointerClick(pointer); yield return Delay(.32f);
        check(!first.IsBack && !first.TargetBack && panel.combatText.text == panel.DetailEffect(first.Definition,false),
            "A partial flip reverses smoothly to the front.");
        first.OnPointerClick(pointer); yield return Delay(.32f);
        check(first.IsBack && panel.combatText.text == panel.DetailEffect(first.Definition,true) &&
            panel.illustration.material.GetFloat("_Mirror") == 1 && panel.combatText.transform.lossyScale.x > 0,
            "The back mirrors only the art and shows the actual offering description.");
        Capture(canvas,"detail-back",check);
        first.OnPointerExit(pointer); yield return Delay(.12f); panel.OnPointerEnter(pointer);
        yield return Delay(.36f);
        check(panel.IsOpen && first.IsBack,"Moving from a card into its details retains the preview and face.");
        panel.OnPointerExit(pointer); yield return Delay(.36f);
        check(!panel.IsOpen && first.IsBack,"Leaving both areas hides details without resetting the card.");
        var second = Object.FindObjectsByType<MapCardView>(FindObjectsSortMode.None).First(v => v.InstanceId != firstId);
        second.OnPointerEnter(pointer); yield return null;
        check(panel.SelectedInstanceId == second.InstanceId && !second.IsBack && first.IsBack,
            "Another card shows its independent face.");
        second.OnPointerExit(pointer); first.OnPointerEnter(pointer); yield return Delay(panel.hoverSwitchDelay + .04f);
        check(panel.SelectedInstanceId == firstId && panel.combatText.text == panel.DetailEffect(first.Definition,true),
            "Deliberately hovering a back-facing card restores its saved back description.");
        int capacity = session.DeckCapacity;
        check(session.TryAddCard(first.Definition,out var duplicate),"A duplicate fixture fits in the current deck.");
        yield return null; yield return null;
        first = Object.FindObjectsByType<MapCardView>(FindObjectsSortMode.None).Single(v => v.InstanceId == firstId);
        check(first.IsBack && first.TargetBack && map.CardShowsBack(firstId),"Deck refresh preserves face state by instance.");
        var definitions = AssetDatabase.FindAssets("t:CardDefinition",new[]{"Assets/Data/Cards"})
            .Select(g => AssetDatabase.LoadAssetAtPath<CardDefinition>(AssetDatabase.GUIDToAssetPath(g))).ToArray();
        foreach (var definition in definitions)
        {
            var view = Object.Instantiate(first,canvas.transform);
            string id = "detail-check-" + definition.cardId;
            view.Initialize(map,definition,id);
            ((RectTransform)view.transform).anchoredPosition = new Vector2(-4000,0);
            view.OnPointerEnter(pointer); panel.OnPointerEnter(pointer); yield return null;
            check(panel.combatText.text == panel.DetailEffect(definition,false) && panel.illustration.sprite == definition.FrontIllustration,
                definition.name + ": frameless front art and live values.");
            Canvas.ForceUpdateCanvases(); panel.combatText.ForceMeshUpdate(); panel.cardName.ForceMeshUpdate();
            check(!panel.combatText.isTextOverflowing && !panel.cardName.isTextOverflowing,definition.name + ": front text fits.");
            view.OnPointerClick(pointer); yield return Delay(.32f);
            check(panel.combatText.text == panel.DetailEffect(definition,true) && !panel.confirm.interactable,
                definition.name + ": correct back values and invalid-instance protection.");
            panel.combatText.ForceMeshUpdate();
            check(!panel.combatText.isTextOverflowing,definition.name + ": back text fits.");
            panel.Close(); Object.Destroy(view.gameObject); yield return null; yield return null;
        }
        first.OnPointerEnter(pointer); panel.OnPointerEnter(pointer);
        first.OnPointerClick(pointer); yield return Delay(.32f);
        check(!first.IsBack && !panel.confirm.gameObject.activeSelf && !panel.confirm.interactable,
            "The front hides and disables the sacrifice action.");
        capacity = session.DeckCapacity;
        panel.confirm.onClick.Invoke();
        check(!panel.IsBusy && session.DeckCapacity == capacity,"Submitting from the front leaves the deck unchanged.");
        first.OnPointerClick(pointer); yield return Delay(.32f);
        check(first.IsBack && panel.confirm.gameObject.activeSelf && panel.confirm.interactable,
            "The detail back exposes the sacrifice action.");
        panel.confirm.onClick.Invoke(); panel.confirm.onClick.Invoke();
        check(panel.IsBusy && panel.burnEffect.IsPlaying && !map.CanInteract && !panel.confirm.gameObject.activeSelf,
            "The back commits once, locks input and captures the card without its button.");
        check(session.RunDeck.Single(c => c.instanceId == firstId).sacrificed && !duplicate.sacrificed &&
            session.DeckCapacity == capacity-first.Definition.capacityCost && session.HasPendingModifier,
            "Only the selected instance is removed and one modifier is prepared.");
        check(!panel.burnEffect.cardTemplate.material.HasProperty("_Heat"),"The obsolete ignition flash is absent from the burn shader.");
        check(panel.sequenceDirector != null && panel.sequenceDirector.playableAsset != null,
            "The saved sacrifice Timeline is bound to the live panel.");
        yield return Until(() => panel.sequenceDirector.time >= .10);
        check(panel.feedbackBackdrop.color.a > 0 && panel.burnEffect.Progress == 0,
            "The screen dims before the card begins burning.");
        Capture(canvas,"timeline-01-dim",check);
        yield return Until(() => panel.sequenceDirector.time >= .82);
        var timelineFace = panel.burnEffect.GetComponentsInChildren<RawImage>().Single(i => i.texture is RenderTexture);
        float expansion = timelineFace.transform.lossyScale.x / panel.cardPreview.lossyScale.x;
        var maximumScale = AnimationUtility.GetEditorCurve(NativeAnimation((TimelineAsset)panel.sequenceDirector.playableAsset, "Card"),
            EditorCurveBinding.FloatCurve("", typeof(Transform), "m_LocalScale.x")).keys.Max(key => key.value);
        check(expansion > 1 && expansion <= maximumScale + .002f && panel.burnEffect.Progress > 0,
            "Burn and expansion overlap after the card arrives at screen center.");
        check(Vector3.Distance(timelineFace.transform.position,panel.burnEffect.focusAnchor.position) < .01f,
            "The expanding card remains at the full-screen center.");
        Capture(canvas,"timeline-02-expand-burn",check);
        yield return Until(() => !panel.IsBusy || panel.burnEffect.Progress >= .35f);
        check(panel.IsBusy,"The burn remains visible long enough to present.");
        Capture(canvas,"detail-sacrifice",check);
        var burnImage = panel.burnEffect.GetComponentsInChildren<RawImage>().Single(i => i.texture is RenderTexture);
        check(burnImage.texture != null && !burnImage.raycastTarget,"Burn uses a non-interactive composed snapshot.");
        yield return Until(() => panel.sequenceDirector.time >= 2.18 || !panel.IsBusy);
        check(panel.IsBusy && panel.feedbackBackdrop.color.a > 0 && panel.feedbackBackdrop.color.a < AnimationUtility.GetEditorCurve(
                NativeAnimation((TimelineAsset)panel.sequenceDirector.playableAsset, "Screen"),
                EditorCurveBinding.FloatCurve("", typeof(Image), "m_Color.a")).keys.Max(key => key.value) &&
            panel.burnEffect.Progress == 1,"The screen brightens gradually after the card is consumed.");
        Capture(canvas,"timeline-03-brighten",check);
        yield return Until(() => !panel.IsBusy && map.CanInteract);
        check(panel.feedbackBackdrop.color.a == 0 && !panel.sequenceDirector.playableGraph.IsValid(),
            "Completion clears the dimmer and releases the Timeline graph.");
        check(!panel.IsOpen && panel.statusText.text.StartsWith("Next battle:") && panel.playerSymbol.SacrificePulse == 0,
            "Completion closes the preview and retains the existing pending-effect status.");
        second = Object.FindObjectsByType<MapCardView>(FindObjectsSortMode.None).First(v => v.gameObject.activeInHierarchy);
        second.OnPointerEnter(pointer); panel.OnPointerEnter(pointer); yield return null;
        check(!panel.confirm.interactable && panel.reason.isActiveAndEnabled,"Spent sacrifice allowance has a clear disabled action.");
        Capture(canvas,"detail-unavailable",check);
        panel.Close(); yield return null; yield return null;
        check(layout.spacing == spacing && nodes.SequenceEqual(map.Nodes.Select(n => (n.NodeId,n.transform.position,n.transform.localScale))),
            "The deck layout and all map nodes retain their transforms.");
        check(map.TryRestartRunForTesting(),"A new run can reset after inspection.");
        yield return null; yield return null;
        check(!map.CardShowsBack(firstId) && !session.SacrificeUsed && !panel.IsOpen,"New runs clear visual face state and pending UI.");
        var backView = Object.FindObjectsByType<MapCardView>(FindObjectsSortMode.None).First(v => v.gameObject.activeInHierarchy);
        backView.OnPointerEnter(pointer); panel.OnPointerEnter(pointer); backView.OnPointerClick(pointer);
        yield return Delay(.32f);
        check(backView.IsBack && panel.confirm.interactable,"The back also has a usable Sacrifice action.");
        string backId = backView.InstanceId;
        panel.confirm.onClick.Invoke();
        check(panel.IsBusy && session.RunDeck.Single(c => c.instanceId == backId).sacrificed,
            "The back commits the same exact-instance sacrifice path.");
        yield return Until(() => !panel.IsBusy && map.CanInteract);
        check(map.TryRestartRunForTesting(),"Reset the disposable back-sacrifice fixture.");
        yield return null; yield return null;
        yield return MapSacrificeVerification.VerifyDeparture(map,check);
        Directory.CreateDirectory(MapSacrificeAuthoring.Results);
        File.WriteAllText(MapSacrificeAuthoring.Results+"/checks.txt","PASS: hover, per-instance faces, synchronized reversible flips, handoff, all definitions, text fit, shader, exact-instance commit, burn, disabled state, restart, departure reminder and three resolutions.\n");
    }
    private static void Capture(Canvas canvas,string name,Action<bool,string> check) => MapSacrificeVerification.Capture(canvas,Camera.main,name,check);
    private static IEnumerator Until(Func<bool> predicate)
    { double end = EditorApplication.timeSinceStartup+60; while(!predicate()) { if(EditorApplication.timeSinceStartup>end) throw new TimeoutException("Card detail check timed out."); yield return null; } }
    private static IEnumerator Delay(float seconds)
    { double end = EditorApplication.timeSinceStartup+seconds; while(EditorApplication.timeSinceStartup<end) yield return null; }
    private static T Field<T>(object target,string name) => (T)target.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic).GetValue(target);
}
