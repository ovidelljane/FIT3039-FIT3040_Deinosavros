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
        var active = SceneManager.GetActiveScene(); bool dirty = active.isDirty;
        string timelineBytes = File.ReadAllText("Assets/UI/MapSacrifice.playable");
        var scene = EditorSceneManager.OpenPreviewScene("Assets/Scenes/Map.unity");
        bool asynchronous = ShaderUtil.allowAsyncCompilation;
        Material material = null; TimelineAsset fixture = null;
        int checks = 0;
        void Check(bool value,string message) { checks++; if (!value) throw new InvalidOperationException(message); }
        try
        {
            ShaderUtil.allowAsyncCompilation = false;
            var panel = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<MapSacrificePanel>(true)).Single();
            var director = panel.sequenceDirector;
            var timeline = (TimelineAsset)director.playableAsset;
            var track = timeline.GetOutputTracks().OfType<SacrificeTimelineTrack>().Single();
            var move = track.GetClips().First(c => ((SacrificeTimelineClip)c.asset).phase == SacrificePhase.Move);
            var burn = track.GetClips().First(c => ((SacrificeTimelineClip)c.asset).phase == SacrificePhase.Burn);
            bool overlay = panel.overlay.activeSelf, button = panel.confirm.gameObject.activeSelf;
            float alpha = panel.cardGroup.alpha; var sprite = panel.illustration.sprite; var sourceMaterial = panel.illustration.material;
            var shade = panel.feedbackBackdrop.color; string title = panel.cardName.text;
            var playerCore = panel.playerSymbol.transform.Find("Core"); var coreScale = playerCore.localScale;
            var definition = panel.previewCard;
            string cardJson = EditorJsonUtility.ToJson(definition);
            director.SetGenericBinding(track,panel);
            director.time = move.start + move.duration * .5; director.Evaluate();
            Check(panel.IsTimelinePreview && !panel.IsBusy && panel.burnEffect.IsPlaying,"Native Edit Mode evaluation shows the ceremony without a sacrifice transaction.");
            float expected = ((SacrificeTimelineClip)move.asset).progression.Evaluate(.5f);
            Check(Mathf.Abs(panel.LastTimelineFrame.move - expected) < .001f,"The actual authored move curve controls preview progress.");
            Check(!panel.confirm.gameObject.activeSelf && panel.cardGroup.alpha == 0,"Preview captures the full card without its button.");
            panel.dimOpacity = .73f;
            director.time = burn.start + burn.duration * .5; director.Evaluate();
            Check(Mathf.Abs(panel.burnEffect.Progress - ((SacrificeTimelineClip)burn.asset).progression.Evaluate(.5f)) < .001f,
                "Seeking into the real burn clip controls its shader.");
            panel.burnEffect.focusAnchor.anchoredPosition += new Vector2(64,-25); panel.burnEffect.focusScale = 1.32f;
            material = new Material(panel.burnEffect.cardTemplate.material);
            panel.burnEffect.cardTemplate.material = material; material.SetColor("_EmberColor",Color.cyan);
            director.Evaluate();
            var face = panel.burnEffect.GetComponentsInChildren<RawImage>().Single(i => i.texture is RenderTexture);
            Check(Vector3.Distance(face.transform.position,panel.burnEffect.focusAnchor.position) < .1f,"Moving the Hierarchy focus anchor changes the preview destination.");
            Check(Mathf.Abs(face.transform.lossyScale.x / panel.cardPreview.lossyScale.x - 1.32f) < .01f,"The editable focus scale changes preview size.");
            Check(face.material.GetColor("_EmberColor") == Color.cyan,"The material controls the burn color without a component override.");
            Check(panel.DetailMaterial == sourceMaterial,"Gradient controls keep editing the real material during preview.");
            var camera = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Camera>()).Single(c => c.CompareTag("MainCamera"));
            camera.overrideSceneCullingMask = EditorSceneManager.GetSceneCullingMask(scene);
            Directory.CreateDirectory(MapSacrificeAuthoring.Results);
            MapSacrificeVerification.Capture(panel.GetComponentInParent<Canvas>(),camera,"timeline-edit-preview",Check);
            var enhance = track.GetClips().First(c => ((SacrificeTimelineClip)c.asset).phase == SacrificePhase.Enhance);
            director.time = enhance.start + enhance.duration * .45; director.Evaluate();
            Check(playerCore.localScale.x > coreScale.x,"Player enhancement is visible while scrubbing in Edit Mode.");
            director.time = 0; director.Evaluate();
            Check(panel.burnEffect.Progress == 0 && panel.feedbackBackdrop.color.a == 0,"Seeking backward restores an intact card and clear screen.");
            director.Stop();
            if (director.playableGraph.IsValid()) director.playableGraph.Destroy();
            Check(!panel.IsTimelinePreview && !panel.burnEffect.IsPlaying && panel.overlay.activeSelf == overlay &&
                panel.confirm.gameObject.activeSelf == button && panel.cardGroup.alpha == alpha && panel.illustration.sprite == sprite &&
                panel.illustration.material == sourceMaterial && panel.feedbackBackdrop.color == shade && panel.cardName.text == title,
                "Closing preview restores the authored UI exactly. preview="+panel.IsTimelinePreview+" playing="+panel.burnEffect.IsPlaying+
                " overlay="+(panel.overlay.activeSelf==overlay)+" button="+(panel.confirm.gameObject.activeSelf==button)+
                " alpha="+(panel.cardGroup.alpha==alpha)+" art="+(panel.illustration.sprite==sprite)+" material="+(panel.illustration.material==sourceMaterial)+
                " shade="+(panel.feedbackBackdrop.color==shade)+" title="+(panel.cardName.text==title));
            Check(playerCore.localScale == coreScale && !panel.burnEffect.GetComponentsInChildren<SacrificeAshGraphic>(true).Any(),
                "Preview cleanup restores the player and removes temporary graphics.");
            Check(EditorJsonUtility.ToJson(definition) == cardJson,"Edit Mode preview does not modify card data.");
            fixture = FixtureTimeline(out var fixtureTrack);
            director.playableAsset = fixture; director.SetGenericBinding(fixtureTrack,panel);
            var sample = fixtureTrack.GetClips().First(c => ((SacrificeTimelineClip)c.asset).phase == SacrificePhase.Move);
            sample.duration = 2;
            director.time = sample.start + .5; director.Evaluate();
            Check(Mathf.Abs(panel.LastTimelineFrame.move - .25f) < .001f,"Changing clip duration changes native preview timing.");
            ((SacrificeTimelineClip)sample.asset).progression = AnimationCurve.Linear(0,0,1,.6f);
            director.time = sample.end; director.Evaluate();
            Check(Mathf.Abs(panel.LastTimelineFrame.move - .6f) < .001f,"Curve endpoint values are respected instead of forced to one.");
            Check(panel.LastTimelineFrame.dim == 0,"An omitted phase stays disabled.");
            var lighting = fixture.CreateTrack<SacrificeTimelineTrack>(null,"Lighting");
            var dim = lighting.CreateClip<SacrificeTimelineClip>(); dim.start = 0; dim.duration = .3;
            ((SacrificeTimelineClip)dim.asset).phase = SacrificePhase.Dim;
            director.SetGenericBinding(lighting,panel); director.RebuildGraph(); director.time = sample.end; director.Evaluate();
            Check(panel.LastTimelineFrame.dim == 1 && Mathf.Abs(panel.LastTimelineFrame.move-.6f)<.001f,
                "Separate phase tracks compose without resetting each other's values.");
            lighting.muted = true; director.RebuildGraph(); director.time = sample.end; director.Evaluate();
            Check(panel.LastTimelineFrame.dim == 0 && Mathf.Abs(panel.LastTimelineFrame.move-.6f)<.001f,
                "Muting one phase track leaves the other phases intact.");
            director.Stop();
            Check(File.ReadAllText("Assets/UI/MapSacrifice.playable") == timelineBytes,"Existing user clip edits remain byte-for-byte unchanged.");
            File.WriteAllText(MapSacrificeAuthoring.Results+"/timeline-preview.txt","PASS: "+checks+" checks; native Edit Mode seeking, editable timing/curve/anchor/scale/material, player feedback and exact cleanup.\n");
        }
        finally
        {
            EditorSceneManager.ClosePreviewScene(scene);
            ShaderUtil.allowAsyncCompilation = asynchronous;
            if (material != null) Object.DestroyImmediate(material);
            DisposeTimeline(fixture);
        }
        Check(SceneManager.GetActiveScene() == active && active.isDirty == dirty,"The open scene and its unsaved state remain intact.");
    }

    public static IEnumerator RunTimeline(Action<bool,string> check)
    {
        var menu = Object.FindFirstObjectByType<MainMenuController>();
        typeof(MainMenuController).GetMethod("StartRun",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(menu,null);
        yield return Until(() => TimeTickSystem.Active?.IsStarted == true);
        foreach (var enemy in BattleScript.FindFighters("Enemy")) enemy.TakeDamage(10000);
        yield return Until(() => Object.FindFirstObjectByType<RewardScreenController>()?.IsPresented == true);
        Field<Button>(Object.FindFirstObjectByType<RewardScreenController>(),"skipButton").onClick.Invoke();
        yield return Until(() => Object.FindFirstObjectByType<MapController>() is MapController m && m.NodeInteraction != null && m.CanInteract);
        var map = Object.FindFirstObjectByType<MapController>(); var panel = map.SacrificePanel; var director = panel.sequenceDirector;
        if (EventSystem.current.currentInputModule != null) EventSystem.current.currentInputModule.enabled = false;
        string bytes = File.ReadAllText("Assets/UI/MapSacrifice.playable");
        var fixture = FixtureTimeline(out var track);
        var material = new Material(panel.burnEffect.cardTemplate.material);
        try
        {
            director.playableAsset = fixture; director.SetGenericBinding(track,panel);
            var card = Object.FindObjectsByType<MapCardView>(FindObjectsSortMode.None).First(c => c.gameObject.activeInHierarchy);
            map.ShowCardDetail(card); yield return null;
            string id = card.InstanceId; int capacity = map.Session.DeckCapacity;
            panel.burnEffect.cardTemplate.material = material;
            panel.confirm.onClick.Invoke(); panel.confirm.onClick.Invoke();
            check(panel.IsBusy && map.Session.RunDeck.Single(c => c.instanceId == id).sacrificed &&
                map.Session.DeckCapacity == capacity-card.Definition.capacityCost,"Native Timeline sacrifice commits one exact instance once.");
            yield return Until(() => director.time > .15);
            check(director.timeUpdateMode != DirectorUpdateMode.Manual && director.state == PlayState.Playing,"Native PlayableDirector advances the sequence.");
            var burn = track.GetClips().Single(c => ((SacrificeTimelineClip)c.asset).phase == SacrificePhase.Burn);
            burn.duration = 2.4;
            ((SacrificeTimelineClip)burn.asset).progression = AnimationCurve.Linear(0,0,1,.8f);
            director.RebuildGraph(); director.time = burn.start + 1.2; director.Evaluate();
            check(Mathf.Abs(panel.burnEffect.Progress-.4f)<.005f,"Changed duration and curve control the live burn.");
            double seek = director.time; yield return null; yield return null;
            check(director.time >= seek && panel.burnEffect.Progress >= .4f,"A script clock does not overwrite manual Timeline seeking.");
            panel.burnEffect.focusAnchor.anchoredPosition += new Vector2(50,-20); panel.burnEffect.focusScale = 1.28f;
            material.SetColor("_EmberColor",Color.blue); director.Evaluate();
            var face = panel.burnEffect.GetComponentsInChildren<RawImage>().Single(i => i.texture is RenderTexture);
            check(Vector3.Distance(face.transform.position,panel.burnEffect.focusAnchor.position)<.1f &&
                Mathf.Abs(face.transform.lossyScale.x/panel.cardPreview.lossyScale.x-1.28f)<.01f && face.material.GetColor("_EmberColor")==Color.blue,
                "Hierarchy and material edits affect the live ceremony.");
            director.Pause(); double paused = director.time; yield return Delay(.15f);
            check(panel.IsBusy && director.time == paused,"Pausing the native director keeps its current presentation.");
            director.Resume();
            yield return Until(() => !panel.IsBusy && map.CanInteract);
            check(!panel.IsOpen && panel.feedbackBackdrop.color.a==0 && !director.playableGraph.IsValid() && map.Session.HasPendingModifier,
                "Native completion restores input and keeps the pending offering.");
            check(File.ReadAllText("Assets/UI/MapSacrifice.playable")==bytes,"Runtime tests preserve the user's real Timeline.");
            File.WriteAllText(MapSacrificeAuthoring.Results+"/timeline-runtime.txt","PASS: native playback, live duration/curve changes, seek, pause/resume, anchor/scale/material edits, single commit and cleanup.\n");
        }
        finally { director.Stop(); DisposeTimeline(fixture); Object.Destroy(material); }
    }

    private static TimelineAsset FixtureTimeline(out SacrificeTimelineTrack track)
    {
        var timeline = ScriptableObject.CreateInstance<TimelineAsset>();
        track = timeline.CreateTrack<SacrificeTimelineTrack>(null,"Preview Test");
        foreach (var spec in new[] { (SacrificePhase.Move,.4,.6), (SacrificePhase.Expand,1d,.6),
            (SacrificePhase.Burn,1.1,1.2), (SacrificePhase.Transfer,2.3,.4), (SacrificePhase.Enhance,2.7,.7), (SacrificePhase.Brighten,2.7,1.2) })
        {
            var clip = track.CreateClip<SacrificeTimelineClip>(); clip.start = spec.Item2; clip.duration = spec.Item3;
            var data = (SacrificeTimelineClip)clip.asset; data.phase = spec.Item1; data.progression = AnimationCurve.Linear(0,0,1,1);
        }
        return timeline;
    }
    private static void DisposeTimeline(TimelineAsset timeline)
    {
        if (timeline == null) return;
        foreach (var track in timeline.GetRootTracks().ToArray())
        { foreach (var clip in track.GetClips()) if (clip.asset != null) Object.DestroyImmediate(clip.asset); Object.DestroyImmediate(track); }
        Object.DestroyImmediate(timeline);
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
        var source = cards.Last(); var other = cards[cards.Length / 2]; var third = cards.First();
        var pointer = new PointerEventData(EventSystem.current) { button = PointerEventData.InputButton.Left };
        var tick = typeof(MapSacrificePanel).GetMethod("TickHover",BindingFlags.Instance|BindingFlags.NonPublic);
        var refresh = typeof(MapSacrificePanel).GetMethod("RefreshFace",BindingFlags.Instance|BindingFlags.NonPublic);
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
            other.OnPointerClick(pointer); yield return Delay(.32f); refresh.Invoke(panel,null);
            check(!other.IsBack && panel.combatText.text == panel.DetailEffect(other.Definition,false),"Repeated click flips both views back normally.");
            check(spacing == deck.GetComponent<HorizontalLayoutGroup>().spacing && children == panel.GetComponentsInChildren<Transform>(true).Length &&
                nodes.SequenceEqual(map.Nodes.Select(n => (n.NodeId,n.transform.position,n.transform.localScale))),
                "The protection uses no overlay objects and preserves all layout and node transforms.");
            panel.enabled = true;
            string offered = other.InstanceId; int capacity = map.Session.DeckCapacity;
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
        check(!first.IsBack && panel.confirm.interactable,"The front has a usable Sacrifice action.");
        capacity = session.DeckCapacity;
        panel.confirm.onClick.Invoke(); panel.confirm.onClick.Invoke();
        check(panel.IsBusy && panel.burnEffect.IsPlaying && !map.CanInteract && !panel.confirm.gameObject.activeSelf,
            "The front commits once, locks input and captures the card without its button.");
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
        check(expansion > 1 && expansion < panel.burnEffect.focusScale && panel.burnEffect.Progress > 0,
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
        check(panel.IsBusy && panel.feedbackBackdrop.color.a > 0 && panel.feedbackBackdrop.color.a < panel.dimOpacity &&
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
