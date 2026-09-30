using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// Opt-in production-flow smoke test. Changes only the disposable Play Mode session.
[InitializeOnLoad]
public static class RunIntegrationPlayVerification
{
    private const string Key = "RunIntegration.PlayVerification";
    private static readonly Stack<IEnumerator> routines = new();
    private static string errors;
    private static double deadline;
    private static int assertions;

    static RunIntegrationPlayVerification()
    {
        EditorApplication.update += Tick;
        Application.logMessageReceived += OnLog;
    }

    [MenuItem("Tools/Map/Verify Integrated Run in Play Mode")]
    public static void Run() => StartVerification(false);
    [MenuItem("Tools/Map/Verify Opportunity Pages in Play Mode")]
    public static void RunOpportunityPages() => StartVerification(true);
    [MenuItem("Tools/Map/Verify Combat Card Hover in Play Mode")]
    public static void RunCombatCardHover()
    {
        StartVerification(false);
        SessionState.SetBool(Key + ".combatHoverOnly", true);
    }

    [MenuItem("Tools/Audio/Verify Card Sounds and Continuous Music")]
    public static void RunAudioPresentation()
    {
        StartVerification(false);
        SessionState.SetBool(Key + ".audioOnly", true);
    }

    [MenuItem("Tools/Battle/Verify Player Idle Loop")]
    public static void RunPlayerLoop()
    {
        StartVerification(false);
        SessionState.SetBool(Key + ".playerLoopOnly", true);
    }

    [MenuItem("Tools/Battle/Verify Enemy Idle Motion")]
    public static void RunEnemyLoop()
    {
        StartVerification(false);
        SessionState.SetBool(Key + ".enemyLoopOnly", true);
    }

    [MenuItem("Tools/Battle/Verify Reward Presentation")]
    public static void RunRewardPresentation()
    {
        StartVerification(false);
        SessionState.SetBool(Key + ".rewardOnly", true);
    }

    [MenuItem("Tools/Battle/Verify Configurable Balance")]
    public static void RunBalance()
    {
        StartVerification(false);
        SessionState.SetBool(Key + ".balanceOnly", true);
    }

    [MenuItem("Tools/Battle/Verify Combat Feedback")]
    public static void RunCombatFeedback()
    {
        StartVerification(false);
        SessionState.SetBool(Key + ".feedbackOnly", true);
    }

    [MenuItem("Tools/Map/Verify Card Artwork and Back Sizes")]
    public static void RunCardArtwork()
    {
        StartVerification(false);
        SessionState.SetBool(Key + ".cardArtworkOnly", true);
    }

    private static void StartVerification(bool opportunityOnly)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Stop Play Mode before verification.");
        for (int i = 0; i < SceneManager.sceneCount; i++)
            if (SceneManager.GetSceneAt(i).isDirty) throw new InvalidOperationException("Save your scene changes first.");
        Directory.CreateDirectory(RunIntegrationSetup.ResultDirectory);
        errors = null; assertions = 0; routines.Clear();
        File.WriteAllText(ResultPath, "RUNNING: integrated production flow.\n");
        File.WriteAllText(RunIntegrationSetup.ResultDirectory + "/play-progress.txt", "Starting from MainMenu.\n");
        EditorSceneManager.OpenScene("Assets/Scenes/MainMenu.unity");
        SessionState.SetInt(Key, 1);
        SessionState.SetBool(Key + ".opportunityOnly", opportunityOnly);
        SessionState.SetBool(Key + ".combatHoverOnly", false);
        SessionState.SetBool(Key + ".audioOnly", false);
        SessionState.SetBool(Key + ".playerLoopOnly", false);
        SessionState.SetBool(Key + ".enemyLoopOnly", false);
        SessionState.SetBool(Key + ".rewardOnly", false);
        SessionState.SetBool(Key + ".balanceOnly", false);
        SessionState.SetBool(Key + ".feedbackOnly", false);
        SessionState.SetBool(Key + ".cardArtworkOnly", false);
        EditorApplication.isPlaying = true;
    }

    private static string ResultPath => RunIntegrationSetup.ResultDirectory + "/play-verification.txt";
    private static void OnLog(string text, string trace, LogType type)
    {
        if (SessionState.GetInt(Key, 0) != 1 || (type != LogType.Error && type != LogType.Exception)) return;
        errors += text + "\n" + trace + "\n";
    }

    private static void Tick()
    {
        int state = SessionState.GetInt(Key, 0);
        if (state == 0 || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
        if (state == 2)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            SessionState.SetInt(Key, 0);
            if (Application.isBatchMode || Environment.GetCommandLineArgs().Contains("-audio-verification-exit"))
                EditorApplication.Exit(File.ReadAllText(ResultPath).StartsWith("PASS:") ? 0 : 1);
            return;
        }
        if (!EditorApplication.isPlaying) return;
        Application.runInBackground = true;
        EditorApplication.QueuePlayerLoopUpdate();
        try
        {
            if (routines.Count == 0)
            {
                deadline = EditorApplication.timeSinceStartup + 360;
                routines.Push(SessionState.GetBool(Key + ".cardArtworkOnly", false) ? MapCardArtworkVerification.Run(Check) :
                    SessionState.GetBool(Key + ".feedbackOnly", false) ? CombatFeedbackVerification.Run(Check) :
                    SessionState.GetBool(Key + ".balanceOnly", false) ? RunBalanceVerification.Run(Check) :
                    SessionState.GetBool(Key + ".rewardOnly", false) ? RewardPresentation() :
                    SessionState.GetBool(Key + ".playerLoopOnly", false) ? PlayerLoopPresentation() :
                    SessionState.GetBool(Key + ".enemyLoopOnly", false) ? EnemyLoopPresentation() :
                    SessionState.GetBool(Key + ".audioOnly", false) ? AudioPresentation() :
                    SessionState.GetBool(Key + ".combatHoverOnly", false) ? CombatCardHover() :
                    SessionState.GetBool(Key + ".opportunityOnly", false) ? OpportunityPages() : Scenario());
            }
            if (EditorApplication.timeSinceStartup >= deadline) throw new InvalidOperationException("Integrated Play Mode verification timeout.");
            if (!string.IsNullOrEmpty(errors)) throw new InvalidOperationException("Runtime error: " + errors);
            while (routines.Count > 0)
            {
                var current = routines.Peek();
                if (!current.MoveNext()) { routines.Pop(); continue; }
                if (current.Current is IEnumerator nested) { routines.Push(nested); continue; }
                return;
            }
            Finish("PASS: " + assertions + " runtime checks; " + (SessionState.GetBool(Key + ".cardArtworkOnly", false)
                ? "all eleven illustrations, single borders, uniform Map backs, titles, reversible hover, deck order, combat and rewards.\n"
                : SessionState.GetBool(Key + ".feedbackOnly", false)
                ? "damage and shield numbers, actor flash, recoil, attack lunge, shared frame properties, pooling, pause, cloud pixels and three resolutions.\n"
                : SessionState.GetBool(Key + ".balanceOnly", false)
                ? "configurable run rules, layer difficulty, offering order, three tick rates, catch-up, timed effects, drawing, health costs, dynamic descriptions and persistent maximum health.\n"
                : SessionState.GetBool(Key + ".rewardOnly", false)
                ? "reward sizing, gray-brown theme, three resolutions, replacement scrolling, cancellation, atomic claim and unchanged shared prefab.\n"
                : SessionState.GetBool(Key + ".playerLoopOnly", false)
                ? "player frame cycle, breath, pinned feet, synchronized shadow, pause, mesh cleanup and unchanged combat state.\n"
                : SessionState.GetBool(Key + ".enemyLoopOnly", false)
                ? "three enemy idle loops, independent timing, planted feet, unchanged UVs, matching shadows, pause, death, cleanup and unchanged gameplay.\n"
                : SessionState.GetBool(Key + ".audioOnly", false)
                ? "all card categories, valid-play-only audio, repeated notifications, 2D playback, persistent music, scene listeners and streaming imports.\n"
                : SessionState.GetBool(Key + ".combatHoverOnly", false)
                ? "combat hover, enemy health bars and attack, live status, resource previews, icons, typography, Map layout, draw and play.\n"
                : SessionState.GetBool(Key + ".opportunityOnly", false)
                ? "opportunity recovery, full health, reload, card replacement, cancel, skip, battle defeat and UI layouts.\n"
                : "integrated production flow, opportunity events, failure recovery, card transactions, HP-only persistence, Boss completion, defeat and restart.\n"));
        }
        catch (Exception ex) { Finish("FAIL: " + ex + "\n" + errors); }
    }

    private static void Finish(string text)
    {
        File.WriteAllText(ResultPath, text); routines.Clear();
        Time.timeScale = 1f; SessionState.SetInt(Key, 2); EditorApplication.isPlaying = false;
    }

    private static IEnumerator RewardPresentation()
    {
        yield return Until(() => Find<MainMenuController>() != null, "Menu ready for reward layout");
        Invoke(Find<MainMenuController>(), "StartRun");
        yield return PreparedBattle();
        var session = RunSession.Instance;
        Check(session.DeckCapacity == 22, "The authored starting deck retains eight free capacity points.");
        foreach (var path in new[] { "Battleborn", "RadiantWard" })
            Check(session.TryAddCard(AssetDatabase.LoadAssetAtPath<CardDefinition>("Assets/Data/Cards/" + path + ".asset"), out _),
                "Test-only cards fill the deck before the reward gate opens.");
        yield return Until(() => TimeTickSystem.Active.IsStarted, "Combat started for reward layout");
        KillEnemies();
        yield return Until(() => Find<RewardScreenController>()?.IsPresented == true, "Reward panel presented");
        var reward = Find<RewardScreenController>();
        var panel = Field<GameObject>(reward, "rewardPanel").GetComponent<RectTransform>();
        var cards = Field<Transform>(reward, "rewardCardContainer").GetComponentsInChildren<RewardCardView>();
        Check(cards.Length == 3 && session.DeckCapacity == 30, "The same three offers and a full test deck are presented.");
        Check(panel.GetComponent<Image>().color == Field<Color>(reward, "backgroundColor") && panel.GetComponent<Image>().color.a == 1,
            "The opaque gray-brown background hides the completed battle.");
        Check(panel.Find("Title").GetComponent<TMPro.TMP_Text>().text == "Victory", "Only one heading names the victory.");
        CaptureRewardLayouts(reward, "reward-choice");
        var prefab = Field<GameObject>(reward, "rewardCardPrefab");
        Check(prefab.GetComponent<RectTransform>().sizeDelta == new Vector2(220, 300),
            "The shared prefab remains unchanged for opportunity screens.");
        Check(session.DeckCapacity == 30, "Replacement starts from a full deck.");
        var selected = cards[0]; var definition = Field<CardDefinition>(selected, "definition");
        var ids = session.RunDeck.Select(card => card.instanceId).ToArray();
        ClickReward(selected); yield return null;
        var replacement = Field<GameObject>(reward, "replacementRoot");
        Check(replacement != null && !Field<Transform>(reward, "rewardCardContainer").gameObject.activeSelf,
            "Only the replacement list is shown when capacity is full.");
        CaptureRewardLayouts(reward, "reward-replacement");
        var scroll = replacement.GetComponent<ScrollRect>();
        Check(scroll.content.rect.height > scroll.viewport.rect.height && !scroll.horizontal,
            "Large replacement cards have a bounded vertical scroll view.");
        scroll.verticalNormalizedPosition = 0; Canvas.ForceUpdateCanvases();
        var last = replacement.GetComponentsInChildren<RewardCardView>().Last().GetComponent<RectTransform>();
        var lastBounds = RectTransformUtility.CalculateRelativeRectTransformBounds(scroll.viewport, last);
        Check(lastBounds.min.y >= scroll.viewport.rect.yMin - 1 && lastBounds.max.y <= scroll.viewport.rect.yMax + 1,
            "Scrolling reaches the final replacement card without covering the footer.");
        reward.ShowReturnError("Your reward is saved. Retry returning to the map.");
        CaptureRewardLayouts(reward, "reward-retry");
        Check(!replacement.activeSelf && Field<Button>(reward, "skipButton").interactable,
            "A return-error layout remains safe to resize with hidden replacement cards.");
        Invoke(reward, "CancelReplacement"); yield return null;
        ClickReward(selected); yield return null;
        Field<Button>(reward, "skipButton").onClick.Invoke(); yield return null;
        Check(Field<GameObject>(reward, "replacementRoot") == null && session.RunDeck.Select(card => card.instanceId).SequenceEqual(ids) &&
            session.HasBattleReward, "Cancel restores offers without changing any card or settling the reward.");
        ClickReward(selected); yield return null;
        replacement = Field<GameObject>(reward, "replacementRoot");
        int outgoingIndex = session.RunDeck.ToList().FindIndex(card => card.definition.capacityCost >= definition.capacityCost);
        string outgoingId = session.RunDeck[outgoingIndex].instanceId;
        int expectedCapacity = 30 - session.RunDeck[outgoingIndex].definition.capacityCost + definition.capacityCost;
        ClickReward(replacement.GetComponentsInChildren<RewardCardView>()[outgoingIndex]);
        Check(session.DeckCapacity == expectedCapacity && !session.RunDeck.Any(card => card.instanceId == outgoingId),
            "The enlarged card still replaces precisely the chosen instance once.");
        yield return ReadyMap();
        Check(session.Progress.CompletedCount == 1 && session.GetActiveDeck().Count == 8,
            "Claim returns to Map and completes exactly one encounter.");
    }

    private static void CaptureRewardLayouts(RewardScreenController reward, string label)
    {
        var panel = Field<GameObject>(reward, "rewardPanel").GetComponent<RectTransform>();
        var canvas = panel.GetComponentInParent<Canvas>();
        var scaler = canvas.GetComponent<CanvasScaler>();
        var camera = Camera.main;
        var oldTarget = camera.targetTexture; var oldActive = RenderTexture.active;
        var oldMode = canvas.renderMode; var oldCamera = canvas.worldCamera;
        float oldAspect = camera.aspect, oldPlane = canvas.planeDistance;
        try
        {
            canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = camera;
            canvas.planeDistance = camera.nearClipPlane + .5f;
            foreach (var size in new[] { new Vector2Int(1920, 1080), new Vector2Int(2560, 1440), new Vector2Int(1920, 1200) })
            {
                var target = new RenderTexture(size.x, size.y, 24, RenderTextureFormat.ARGB32);
                Texture2D image = null;
                try
                {
                    target.Create(); camera.targetTexture = target; camera.aspect = (float)size.x / size.y;
                    typeof(CanvasScaler).GetMethod("Handle", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(scaler, null);
                    Canvas.ForceUpdateCanvases(); Invoke(reward, "UpdatePresentationLayout"); Canvas.ForceUpdateCanvases();
                    var corners = new Vector3[4];
                    foreach (var button in panel.GetComponentsInChildren<Button>())
                    {
                        if (button.GetComponentInParent<ScrollRect>() != null) continue;
                        button.GetComponent<RectTransform>().GetWorldCorners(corners);
                        foreach (var point in corners)
                        {
                            var view = camera.WorldToViewportPoint(point);
                            Check(view.x >= 0 && view.x <= 1 && view.y >= 0 && view.y <= 1,
                                "Reward actions stay visible at " + size);
                        }
                    }
                    foreach (var card in panel.GetComponentsInChildren<RewardCardView>())
                    {
                        var name = Field<TMPro.TMP_Text>(card, "nameText"); var effect = Field<TMPro.TMP_Text>(card, "effectText");
                        name.ForceMeshUpdate(); effect.ForceMeshUpdate();
                        Check(!name.isTextOverflowing && !effect.isTextOverflowing, "Card copy fits at " + size);
                        if (card.GetComponentInParent<ScrollRect>() == null)
                            Check(Field<Image>(card, "border").rectTransform.rect.height >= 350,
                                "Reward artwork is more than twice the previous height.");
                    }
                    UnityEngine.Rendering.RenderPipeline.SubmitRenderRequest(camera,
                        new UnityEngine.Rendering.Universal.UniversalRenderPipeline.SingleCameraRequest { destination = target });
                    RenderTexture.active = target; image = new Texture2D(size.x, size.y, TextureFormat.RGB24, false);
                    image.ReadPixels(new Rect(0, 0, size.x, size.y), 0, 0); image.Apply();
                    File.WriteAllBytes(RunIntegrationSetup.ResultDirectory + "/" + label + "-" + size.x + "x" + size.y + ".png", image.EncodeToPNG());
                }
                finally
                {
                    camera.targetTexture = oldTarget; RenderTexture.active = oldActive;
                    if (image != null) UnityEngine.Object.Destroy(image);
                    target.Release(); UnityEngine.Object.Destroy(target);
                }
            }
        }
        finally
        {
            canvas.renderMode = oldMode; canvas.worldCamera = oldCamera; canvas.planeDistance = oldPlane;
            camera.targetTexture = oldTarget; camera.aspect = oldAspect; RenderTexture.active = oldActive;
            typeof(CanvasScaler).GetMethod("Handle", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(scaler, null);
            Canvas.ForceUpdateCanvases(); Invoke(reward, "UpdatePresentationLayout");
        }
    }

    private static IEnumerator EnemyLoopPresentation()
    {
        yield return Until(() => Find<MainMenuController>() != null, "Menu ready for enemy animation");
        Invoke(Find<MainMenuController>(), "StartRun");
        yield return PreparedBattle();
        var hud = BattleHud.Find(SceneManager.GetActiveScene());
        yield return Until(() => hud.Deck.CanPlay, "Combat active before idle test");
        TimeTickSystem.Active.StopTimer();
        var enemies = BattleScript.FindFighters("Enemy").ToArray();
        var loops = enemies.Select(e => e.GetComponent<EnemyIdleMotion>()).ToArray();
        Check(loops.Length == 3 && loops.All(loop => loop != null && loop.IsReady), "Every formal enemy has one ready animation.");
        Check(loops.Select(loop => Field<float>(loop, "phaseOffset")).Distinct().Count() == 3,
            "Enemy breathing is staggered instead of moving in lockstep.");
        Check(loops.Select(loop => Field<Mesh>(loop, "animatedMesh")).Distinct().Count() == 3,
            "Each enemy has an independent visual mesh.");
        var states = enemies.Select(EditorJsonUtility.ToJson).ToArray();
        var transforms = enemies.Select(e => (e.transform.position, e.transform.rotation, e.transform.localScale)).ToArray();
        var colliders = enemies.Select(e => e.GetComponent<MeshCollider>().sharedMesh).ToArray();
        Time.timeScale = 0;
        try
        {
            foreach (var loop in loops)
            {
                var filters = Field<MeshFilter[]>(loop, "visuals");
                var originals = Field<Mesh[]>(loop, "originals");
                var mesh = Field<Mesh>(loop, "animatedMesh");
                var rest = Field<Vector3[]>(loop, "restVertices");
                var uv = Field<Vector2[]>(loop, "restUV");
                Check(filters.Length == 2 && filters.All(f => f.sharedMesh == mesh), "Body and shadow share the exact pose.");
                Check(mesh != loop.GetComponent<MeshCollider>().sharedMesh && mesh.uv.SequenceEqual(uv),
                    "Animation preserves original texture mapping and the collision mesh.");
                var apply = typeof(EnemyIdleMotion).GetMethod("ApplyPose", BindingFlags.NonPublic | BindingFlags.Instance);
                foreach (float time in new[] { 0f, .7f, 1.6f, 3.3f, 8f })
                {
                    apply.Invoke(loop, new object[] { time });
                    var moved = mesh.vertices;
                    Check(Enumerable.Range(0, rest.Length).Where(i => uv[i].y <= Field<float>(loop, "plantedHeight"))
                        .All(i => Vector3.Distance(rest[i], moved[i]) < .000001f), "Feet stay pinned through the loop.");
                    Check(moved.Zip(rest, (a, b) => Vector3.Distance(a, b)).Max() > .015f, "The upper body has visible secondary motion.");
                    var bounds = mesh.bounds; bounds.Expand(.0001f);
                    Check(moved.All(bounds.Contains), "Animated bounds contain every vertex within floating-point tolerance.");
                }
                var renderer = loop.GetComponent<Renderer>(); var block = new MaterialPropertyBlock();
                renderer.GetPropertyBlock(block); block.SetFloat("_HitBlend", .4f); renderer.SetPropertyBlock(block);
                apply.Invoke(loop, new object[] { .5f }); renderer.GetPropertyBlock(block);
                Check(Mathf.Approximately(block.GetFloat("_HitBlend"), .4f), "Idle animation does not overwrite hit or lighting properties.");
                loop.enabled = false;
                Check(filters.Select((f, i) => f.sharedMesh == originals[i]).All(same => same), "Disable restores authored render meshes.");
                yield return null;
                Check(mesh == null, "Disabling releases the temporary mesh.");
                loop.enabled = true;
                Check(loop.IsReady && filters[0].sharedMesh == filters[1].sharedMesh, "Re-enable safely rebuilds a body/shadow pair.");
            }
            var times = loops.Select(loop => Field<float>(loop, "playbackTime")).ToArray();
            yield return null; yield return null;
            Check(loops.Select((loop, i) => Field<float>(loop, "playbackTime") == times[i]).All(same => same), "Pause freezes all enemy poses.");
            var initial = loops.Select(loop => Field<Mesh>(loop, "animatedMesh").vertices).ToArray();
            CapturePlayerFrame(hud.Canvas, Camera.main, "enemy-idle-a");
            Time.timeScale = 1;
            double until = EditorApplication.timeSinceStartup + 1.6;
            while (EditorApplication.timeSinceStartup < until) yield return null;
            Time.timeScale = 0;
            CapturePlayerFrame(hud.Canvas, Camera.main, "enemy-idle-b");
            for (int i = 0; i < loops.Length; i++)
            {
                Check(Field<float>(loops[i], "playbackTime") > 0 && Field<Mesh>(loops[i], "animatedMesh").vertices
                    .Zip(initial[i], (a, b) => Vector3.Distance(a, b)).Max() > .005f, "Normal updates animate enemy " + i);
                Check(enemies[i].transform.position == transforms[i].position && enemies[i].transform.rotation == transforms[i].rotation &&
                    enemies[i].transform.localScale == transforms[i].localScale && enemies[i].GetComponent<MeshCollider>().sharedMesh == colliders[i],
                    "Idle motion leaves gameplay transforms and colliders untouched.");
                Check(EditorJsonUtility.ToJson(enemies[i]) == states[i], "Idle animation does not change combat values.");
            }
            enemies[0].TakeDamage(10000, hud.Deck.Player);
            float deathTime = Field<float>(loops[0], "playbackTime");
            var deathPose = Field<Mesh>(loops[0], "animatedMesh").vertices;
            Time.timeScale = 1;
            until = EditorApplication.timeSinceStartup + .35;
            while (EditorApplication.timeSinceStartup < until) yield return null;
            Check(!enemies[0].enabled && Field<float>(loops[0], "playbackTime") == deathTime &&
                Field<Mesh>(loops[0], "animatedMesh").vertices.SequenceEqual(deathPose), "A defeated enemy stops breathing immediately.");
            Check(hud.Deck.Player.GetComponent<PlayerIdleLoop>().CurrentFrameIndex >= 0, "The existing player animation remains active.");
        }
        finally { Time.timeScale = 1; }
    }

    private static IEnumerator PlayerLoopPresentation()
    {
        yield return Until(() => Find<MainMenuController>() != null, "Menu ready for player animation");
        Invoke(Find<MainMenuController>(), "StartRun");
        yield return PreparedBattle();
        yield return Until(() => !MapTravelCoordinator.Ensure(RunSession.Instance).IsBusy, "Player visible after arrival fade");
        var hud = BattleHud.Find(SceneManager.GetActiveScene());
        var player = hud.Deck.Player;
        var loop = player.GetComponent<PlayerIdleLoop>();
        Check(loop != null && loop.CurrentFrameIndex >= 0, "The formal combat player owns a ready idle animation.");
        var filters = Field<MeshFilter[]>(loop, "visualMeshes");
        var originals = Field<Mesh[]>(loop, "originalMeshes");
        var frames = Field<Texture2D[]>(loop, "frames");
        var mesh = filters[0].sharedMesh;
        var rest = Field<Vector3[]>(loop, "restVertices");
        var uv = Field<Vector2[]>(loop, "restUV");
        var collider = player.GetComponent<MeshCollider>();
        var colliderMesh = collider.sharedMesh;
        var position = player.transform.position;
        var rotation = player.transform.rotation;
        var scale = player.transform.localScale;
        int hp = player.health, damage = player.attackDmg, shield = player.shield;
        float interval = player.attackSpd, energy = player.elixir;
        var apply = typeof(PlayerIdleLoop).GetMethod("ApplyPose", BindingFlags.NonPublic | BindingFlags.Instance);
        float duration = Field<float>(loop, "loopDuration");
        Time.timeScale = 0;
        var input = EventSystem.current.currentInputModule;
        if (input != null) input.enabled = false;
        try
        {
            Check(filters.Length == 2 && filters[1].sharedMesh == mesh && mesh != colliderMesh,
                "Body and shadow share a private mesh, never the collision mesh.");
            foreach (var sample in new[] { (0f, 2), (.70f, 1), (.77f, 0), (.84f, 1), (.96f, 2), (1f, 2), (2.77f, 0) })
            {
                apply.Invoke(loop, new object[] { sample.Item1 * duration });
                Check(loop.CurrentFrameIndex == sample.Item2, "Ping-pong pose at cycle " + sample.Item1);
                foreach (var filter in filters)
                {
                    var block = new MaterialPropertyBlock(); filter.GetComponent<Renderer>().GetPropertyBlock(block);
                    Check(block.GetTexture("_MainTex") == frames[sample.Item2] && block.GetTexture("_BaseMap") == frames[sample.Item2],
                        "The visible pose and alpha shadow use the same original frame.");
                }
                var moved = mesh.vertices;
                Check(Enumerable.Range(0, rest.Length).Where(i => uv[i].y <= .18f)
                    .All(i => Vector3.Distance(rest[i], moved[i]) < .000001f), "Feet and lower legs stay planted.");
                Check(player.transform.position == position && player.transform.rotation == rotation && player.transform.localScale == scale &&
                    collider.sharedMesh == colliderMesh, "Secondary motion never moves the actor or its collider.");
            }
            apply.Invoke(loop, new object[] { .7f });
            Check(mesh.vertices.Zip(rest, (a,b) => Vector3.Distance(a,b)).Max() > .015f,
                "There is continuous visible secondary movement between texture changes.");
            foreach (var sample in new[] { (0f, "closed"), (.70f, "half"), (.77f, "open") })
            {
                apply.Invoke(loop, new object[] { sample.Item1 * duration });
                CapturePlayerFrame(hud.Canvas, Camera.main, sample.Item2);
            }
            float time = Field<float>(loop, "playbackTime");
            yield return null;
            Check(Field<float>(loop, "playbackTime") == time, "Pausing freezes both the pose and secondary motion.");
            Check(player.health == hp && player.attackDmg == damage && player.shield == shield &&
                player.attackSpd == interval && player.elixir == energy, "Animation does not mutate combat attributes.");
            loop.enabled = false;
            Check(filters.Select((f,i) => f.sharedMesh == originals[i]).All(value => value), "Disabling restores the source render meshes.");
            yield return null;
            Check(mesh == null, "The temporary mesh is released on disable.");
            loop.enabled = true;
            Check(loop.CurrentFrameIndex == 2 && filters[0].sharedMesh == filters[1].sharedMesh &&
                filters[0].sharedMesh != colliderMesh, "Re-enabling safely rebuilds one shared visual mesh.");
            var observedFrames = new HashSet<int>();
            Time.timeScale = 1;
            double until = EditorApplication.timeSinceStartup + duration + .2;
            while (EditorApplication.timeSinceStartup < until)
            {
                observedFrames.Add(loop.CurrentFrameIndex);
                yield return null;
            }
            Time.timeScale = 0;
            Check(observedFrames.SetEquals(new[] { 0, 1, 2 }) && Field<float>(loop, "playbackTime") >= duration,
                "Normal frame updates play all three poses and loop without manual sampling.");
        }
        finally { Time.timeScale = 1; if (input != null) input.enabled = true; }
    }

    private static void CapturePlayerFrame(Canvas canvas, Camera camera, string pose)
    {
        var target = new RenderTexture(1920, 1080, 24, RenderTextureFormat.ARGB32);
        var previousTarget = camera.targetTexture;
        var previousActive = RenderTexture.active;
        var previousMode = canvas.renderMode;
        var previousCamera = canvas.worldCamera;
        float previousPlane = canvas.planeDistance, previousAspect = camera.aspect;
        Texture2D image = null;
        try
        {
            target.Create(); camera.targetTexture = target; camera.aspect = 1920f / 1080f;
            canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = camera;
            canvas.planeDistance = camera.nearClipPlane + .5f;
            Canvas.ForceUpdateCanvases();
            UnityEngine.Rendering.RenderPipeline.SubmitRenderRequest(camera,
                new UnityEngine.Rendering.Universal.UniversalRenderPipeline.SingleCameraRequest { destination = target });
            RenderTexture.active = target;
            image = new Texture2D(1920,1080,TextureFormat.RGB24,false);
            image.ReadPixels(new Rect(0,0,1920,1080),0,0); image.Apply();
            File.WriteAllBytes(RunIntegrationSetup.ResultDirectory + "/player-loop-" + pose + ".png", image.EncodeToPNG());
        }
        finally
        {
            canvas.renderMode = previousMode; canvas.worldCamera = previousCamera; canvas.planeDistance = previousPlane;
            camera.targetTexture = previousTarget; camera.aspect = previousAspect; RenderTexture.active = previousActive;
            if (image != null) UnityEngine.Object.Destroy(image);
            target.Release(); UnityEngine.Object.Destroy(target);
            Canvas.ForceUpdateCanvases();
        }
    }

    private static IEnumerator AudioPresentation()
    {
        yield return Until(() => Find<MainMenuController>() != null, "Menu ready for audio check");
        var profile = Resources.Load<GameAudioProfile>("GameAudio");
        var audio = GameAudio.Ensure();
        var music = Field<AudioSource>(audio, "musicSource");
        var sounds = Field<AudioSource>(audio, "cardSource");
        Check(profile != null && profile.music != null && music.loop && music.spatialBlend == 0 &&
            sounds.spatialBlend == 0 && !sounds.loop && !sounds.playOnAwake, "Music and effects have separate 2D sources.");
        Check(((AudioImporter)AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(profile.music))).defaultSampleSettings.loadType ==
            AudioClipLoadType.Streaming, "The long music track streams instead of allocating full decoded audio.");
        var definitions = AssetDatabase.LoadAssetAtPath<CardPool>("Assets/Data/CardPool.asset").Cards;
        foreach (var definition in definitions)
        {
            var expected = definition.cardType switch
            {
                MapCardType.Attack => profile.attack,
                MapCardType.Debuff => profile.debuff,
                MapCardType.Defense => profile.defense,
                _ => profile.buff
            };
            Check(expected != null && profile.CardClip(definition.cardType) == expected && expected.length < 2,
                "A short effect is explicitly mapped for " + definition.cardId);
            Check(((AudioImporter)AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(expected))).defaultSampleSettings.preloadAudioData,
                "Card sounds are preloaded for immediate feedback.");
        }
        File.WriteAllText(RunIntegrationSetup.ResultDirectory + "/audio-runtime.txt",
            $"Scene={SceneManager.GetActiveScene().name}; Enabled={Field<bool>(audio, "inGameScene")}; " +
            $"Profile={Field<GameAudioProfile>(audio, "profile")}; Expected={profile.music}; " +
            $"Clip={music.clip}; Playing={music.isPlaying}; Time={music.time}; DSP={AudioSettings.dspTime}; " +
            $"Paused={AudioListener.pause}; Volume={AudioListener.volume}; SampleRate={AudioSettings.outputSampleRate}\n");
        yield return Until(() => music.isPlaying && music.time > .1f, "Menu music started");
        float musicBefore = music.time;
        Invoke(Find<MainMenuController>(), "StartRun");
        yield return PreparedBattle();
        Check(GameAudio.Ensure() == audio && music.isPlaying && music.time > musicBefore,
            "The same music voice continues into combat without restarting.");
        var hud = BattleHud.Find(SceneManager.GetActiveScene());
        var deck = hud.Deck;
        int count = audio.CardSoundCount;
        deck.Hand[0].OnPointerClick(new PointerEventData(EventSystem.current));
        Check(audio.CardSoundCount == count, "Countdown input cannot emit a card effect.");
        yield return Until(() => deck.CanPlay, "Combat ready for card audio");
        Time.timeScale = 0;
        var input = EventSystem.current.currentInputModule;
        if (input != null) input.enabled = false;
        try
        {
            foreach (var enemy in BattleScript.FindFighters("Enemy")) enemy.health = 1000;
            var hand = Field<List<BuffCards>>(deck, "hand");
            var parent = hand[0].transform.parent;
            foreach (var definition in definitions)
            {
                var card = UnityEngine.Object.Instantiate(definition.combatPrefab, parent).GetComponent<BuffCards>();
                card.Initialize(deck, definition); hand.Add(card);
                yield return null;
                deck.Player.health = 1000; deck.Player.elixir = 0;
                count = audio.CardSoundCount;
                var pointer = new PointerEventData(EventSystem.current);
                card.OnPointerEnter(pointer); card.OnPointerMove(pointer); card.OnPointerExit(pointer);
                Check(audio.CardSoundCount == count, "Hover and cost preview remain silent.");
                if (card.Cost > 0)
                {
                    if (card.UsesHealthCost) deck.Player.health = card.Cost - 1;
                    card.OnPointerClick(pointer);
                    Check(audio.CardSoundCount == count && hand.Contains(card), "Rejected plays remain silent.");
                }
                deck.Player.health = 1000; deck.Player.elixir = 100;
                card.OnPointerClick(pointer);
                Check(audio.CardSoundCount == count + 1 && audio.LastCardClip == profile.CardClip(definition.cardType),
                    "One accepted play emits the matching category for " + definition.cardId);
                deck.OnCardPlayed(card, definition);
                Check(audio.CardSoundCount == count + 1, "Repeated consumption notifications cannot double the sound.");
                yield return null;
                Check(card == null && sounds != null && sounds.isActiveAndEnabled && sounds.isPlaying,
                    "Destroying the played card does not destroy or cut off its audio source.");
            }
        }
        finally { Time.timeScale = 1; if (input != null) input.enabled = true; }
        hud.StopCombat();
        foreach (string scene in new[] { "Map", "MapRecovery", "MapOpportunity", "MainMenu" })
        {
            musicBefore = music.time;
            var load = SceneManager.LoadSceneAsync(scene);
            yield return Until(() => load.isDone, "Audio scene transition: " + scene);
            yield return null;
            Check(GameAudio.Ensure() == audio && music.isPlaying && music.time >= musicBefore,
                "Music remains continuous in " + scene);
            Check(UnityEngine.Object.FindObjectsByType<GameAudio>(FindObjectsSortMode.None).Length == 1,
                "Scene changes do not create duplicate audio services.");
            Check(UnityEngine.Object.FindObjectsByType<AudioListener>(FindObjectsSortMode.None).Count(l => l.isActiveAndEnabled) == 1,
                "Exactly one active listener exists in " + scene);
        }
    }

    private static IEnumerator CombatCardHover()
    {
        yield return Until(() => Find<MainMenuController>() != null, "Main menu loaded");
        Invoke(Find<MainMenuController>(), "StartRun");
        yield return PreparedBattle();
        yield return Until(() => !MapTravelCoordinator.Ensure(RunSession.Instance).IsBusy, "Battle fade completed");
        var hud = BattleHud.Find(SceneManager.GetActiveScene());
        var card = hud.Deck.Hand.Last();
        var root = (RectTransform)card.transform;
        var visual = (RectTransform)root.Find("CardVisual");
        var hit = (RectTransform)root.Find("HitArea");
        Check(visual != null && hit != null && hud.Deck.Hand.Count == 5, "All drawn cards retain their layout roots.");
        var art = Field<Image>(card, "artworkImage");
        var border = Field<Image>(card, "borderImage");
        Sprite front = art.sprite;
        Check(art.transform.IsChildOf(visual) && border.transform.IsChildOf(visual) &&
            Field<TMPro.TextMeshProUGUI>(card, "costText").transform.IsChildOf(visual), "Art, frame and cost animate together.");
        Check(visual.GetComponentsInChildren<Graphic>(true).All(g => !g.raycastTarget), "Animated graphics cannot steal hover.");
        Vector3 position = root.position;
        Vector3 scale = root.localScale;
        Quaternion rotation = root.localRotation;
        int order = root.GetSiblingIndex();
        var corners = new Vector3[4]; hit.GetWorldCorners(corners);
        var camera = hud.Canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : hud.Canvas.worldCamera;
        var events = EventSystem.current;
        var input = events.currentInputModule;
        if (input != null) input.enabled = false;
        Time.timeScale = 0f;
        try
        {
            var pointer = new PointerEventData(events);
            pointer.position = RectTransformUtility.WorldToScreenPoint(camera,
                hit.TransformPoint(new Vector3(hit.rect.xMax - 2f, hit.rect.center.y)));
            var hits = new List<RaycastResult>(); events.RaycastAll(pointer, hits);
            Check(hits.Count > 0 && hits[0].gameObject.GetComponentInParent<BuffCards>() == card,
                "The visible card edge still receives pointer input.");
            pointer.pointerEnter = hits[0].gameObject;
            pointer.pointerCurrentRaycast = hits[0];
            card.OnPointerEnter(pointer);
            double settle = EditorApplication.timeSinceStartup + .6;
            yield return Until(() => EditorApplication.timeSinceStartup >= settle, "Hover settled while paused");
            Check(Mathf.Abs(visual.localScale.x - 1.04f) < .002f && visual.localEulerAngles.y > 350f,
                "Hover enlarges and tilts towards the pointer without flipping.");
            Check(Find<CardInfoPanel>() != null, "Existing combat card details still appear.");
            double stationary = EditorApplication.timeSinceStartup + .4;
            while (EditorApplication.timeSinceStartup < stationary)
            {
                hits.Clear(); events.RaycastAll(pointer, hits);
                Check(hits.Count > 0 && hits[0].gameObject.GetComponentInParent<BuffCards>() == card,
                    "A stationary edge pointer keeps the same target during animation.");
                yield return null;
            }
            var afterCorners = new Vector3[4]; hit.GetWorldCorners(afterCorners);
            Check(corners.Zip(afterCorners, (a, b) => Vector3.Distance(a, b)).All(d => d < .001f) &&
                root.position == position && root.localScale == scale && root.localRotation == rotation &&
                root.GetSiblingIndex() == order, "Hit bounds and hand layout never move or reorder.");
            pointer.position = RectTransformUtility.WorldToScreenPoint(camera,
                hit.TransformPoint(new Vector3(hit.rect.xMin + 2f, hit.rect.yMax - 2f)));
            card.OnPointerMove(pointer);
            settle = EditorApplication.timeSinceStartup + .5;
            yield return Until(() => EditorApplication.timeSinceStartup >= settle, "Pointer crossed the face");
            Check(visual.localEulerAngles.y > 1f && visual.localEulerAngles.y < 9f &&
                Vector3.Dot(visual.forward, root.forward) > .97f && art.sprite == front,
                "Crossing the card reverses tilt and always retains its readable front.");
            card.OnPointerExit(pointer);
            card.OnPointerEnter(pointer);
            card.OnPointerExit(pointer);
            settle = EditorApplication.timeSinceStartup + .6;
            yield return Until(() => EditorApplication.timeSinceStartup >= settle, "Rapid re-entry restored the rest pose");
            Check(Vector3.Distance(visual.localScale, Vector3.one) < .002f &&
                Quaternion.Angle(visual.localRotation, Quaternion.identity) < .1f, "Exit reverses smoothly from the current pose.");
            card.OnPointerEnter(pointer);
            card.enabled = false;
            Check(visual.localScale == Vector3.one && visual.localRotation == Quaternion.identity && Find<CardInfoPanel>() == null,
                "Disabling a card clears animation and details.");
            card.enabled = true;

            var definition = Field<CardDefinition>(card, "definition");
            var staticFront = UnityEngine.Object.Instantiate(definition.combatPrefab, root.parent);
            var staticCard = staticFront.GetComponent<BuffCards>(); staticCard.enabled = false; staticCard.ApplyArt(definition);
            Check(staticFront.transform.Find("CardVisual") == null, "Static Map and reward card faces do not gain combat animation.");
            UnityEngine.Object.Destroy(staticFront);
        }
        finally
        {
            Time.timeScale = 1f;
            if (input != null) input.enabled = true;
        }
        yield return StatusPresentation(hud);
        yield return EnemyStatusPresentation(hud);
        yield return Until(() => hud.Deck.CanPlay, "Combat input ready");
        var playable = hud.Deck.Hand.First(c => Stat(c) != StatType.DamageAllEnemies && Cost(c) <= hud.Deck.Player.elixir);
        playable.OnPointerEnter(new PointerEventData(events));
        playable.OnPointerClick(new PointerEventData(events));
        Check(hud.Deck.Hand.Count == 4, "Hovering does not block normal card play.");
        yield return Until(() => hud.Deck.Hand.Count == 5, "Replacement drawn");
        Check(hud.Deck.Hand.All(c => c.transform.Find("CardVisual") != null), "Replacement cards also receive hover.");
        var last = hud.Deck.Hand.Last(); last.OnPointerEnter(new PointerEventData(events)); hud.StopCombat();
        Check(last.transform.Find("CardVisual").localScale == Vector3.one && Find<CardInfoPanel>() == null,
            "Combat completion clears card hover and details.");
        Check(hud.Status.PreviewCard == null, "Combat completion clears the resource preview.");
        SceneManager.LoadSceneAsync("Map");
        yield return Until(() => Find<MapController>() != null && Find<MapPlayerStatusPanel>()?.View != null, "Map status loaded");
        var mapStatus = Find<MapPlayerStatusPanel>().View;
        Canvas.ForceUpdateCanvases();
        Check(mapStatus.Health != null && mapStatus.Elixir != null && mapStatus.GetComponentsInChildren<StatusIcon>().Length == 5,
            "Map shares the health and Elixir bars and three attribute icons.");
        MapOpportunityVerification.CaptureCanvas(mapStatus.GetComponentInParent<Canvas>(), Camera.main, "map-status", (RectTransform)mapStatus.transform);
    }

    private static IEnumerator EnemyStatusPresentation(BattleHud hud)
    {
        var enemies = BattleScript.FindFighters("Enemy");
        Check(enemies.Count == 3, "Only the three authored fighters receive enemy status panels.");
        Canvas.ForceUpdateCanvases();
        var panelBounds = new List<Rect>();
        foreach (var fighter in enemies)
        {
            var display = fighter.GetComponent<HealthLabel>();
            var panel = Field<RectTransform>(display, "enemyPanel");
            Check(display.IsReady && panel != null && panel.GetComponentsInChildren<Graphic>().All(g => !g.raycastTarget),
                "Each enemy has one ready, non-interactive health and attack panel.");
            Check(Field<TMPro.TMP_Text>(display, "attackText").text == Mathf.Max(0, fighter.attackDmg).ToString(),
                "The sword value shows per-hit attack, not DPS or multi-hit damage.");
            var corners = new Vector3[4]; panel.GetWorldCorners(corners);
            Vector3 lower = hud.Canvas.transform.InverseTransformPoint(corners[0]);
            Vector3 upper = hud.Canvas.transform.InverseTransformPoint(corners[2]);
            var bounds = Rect.MinMaxRect(lower.x, lower.y, upper.x, upper.y);
            Check(panelBounds.All(other => !other.Overlaps(bounds)), "Enemy status panels do not overlap in the authored formation.");
            panelBounds.Add(bounds);
        }
        Check(Field<RectTransform>(hud.Deck.Player.GetComponent<HealthLabel>(), "enemyPanel") == null,
            "The enemy presentation does not replace the player's existing status.");
        var enemy = enemies[0];
        var status = enemy.GetComponent<HealthLabel>();
        var root = Field<RectTransform>(status, "enemyPanel");
        var fill = Field<Image>(status, "enemyFill");
        int health = enemy.health, maximum = enemy.maxHealth, damage = enemy.attackDmg, shield = enemy.shield;
        Vector3 position = enemy.transform.position;
        float timeScale = Time.timeScale;
        Time.timeScale = 0;
        try
        {
            enemy.health = enemy.maxHealth = 20; enemy.shield = 0;
            enemy.TakeDamage(7); enemy.attackDmg = 4;
            yield return null;
            Check(Mathf.Abs(fill.rectTransform.anchorMax.x - .65f) < .001f &&
                Field<TMPro.TextMeshProUGUI>(status,"label").text == "13 / 20" &&
                Field<TMPro.TMP_Text>(status,"attackText").text == "4", "Damage and attack changes update the enemy panel.");
            Canvas.ForceUpdateCanvases();
            MapOpportunityVerification.CaptureCanvas(hud.Canvas, Camera.main, "enemy-status", root);
            enemy.shield = 5; enemy.TakeDamage(2); yield return null;
            Check(Mathf.Abs(fill.rectTransform.anchorMax.x - .65f) < .001f,
                "Shield absorption does not reduce the health bar.");
            Vector2 anchor = root.anchorMin;
            enemy.transform.position += Vector3.right;
            yield return null;
            var viewport = Camera.main.WorldToViewportPoint(enemy.transform.position + Vector3.up * Field<float>(status,"yOffset"));
            Check(Vector2.Distance(root.anchorMin,new Vector2(viewport.x,viewport.y)) < .001f && root.anchorMin != anchor,
                "Enemy movement updates its own status anchor.");
            enemy.transform.position = Camera.main.transform.position - Camera.main.transform.forward * 5 - Vector3.up * 2;
            yield return null;
            Check(Field<CanvasGroup>(status,"visibility").alpha == 0, "Enemies behind the camera do not leave stray panels.");
            enemy.transform.position = position;
            enemy.health = -2; enemy.maxHealth = 0;
            yield return null;
            Check(fill.rectTransform.anchorMax.x == 0 && Field<TMPro.TextMeshProUGUI>(status,"label").text == "0 / 0",
                "Zero maximum and negative health are safely clamped.");
            status.enabled = false;
            Check(!root.gameObject.activeSelf, "Disabled actors hide their status panel.");
            status.enabled = true;
            yield return null;
            Check(status.IsReady && root.gameObject.activeSelf, "Reactivation reuses the existing status panel.");
        }
        finally
        {
            enemy.health = health; enemy.maxHealth = maximum; enemy.attackDmg = damage; enemy.shield = shield;
            enemy.transform.position = position; status.enabled = true; Time.timeScale = timeScale;
        }
        yield return null;
    }

    private static IEnumerator StatusPresentation(BattleHud hud)
    {
        var view = hud.Status; var player = hud.Deck.Player;
        Check(view != null && GameFonts.Body != null && GameFonts.CinzelBlack != null, "HUD and both font families load.");
        var definitions = AssetDatabase.LoadAssetAtPath<CardPool>("Assets/Data/CardPool.asset").Cards;
        var normalDefinition = definitions.First(d => d.combatPrefab.GetComponent<BuffCards>().Cost > 0 &&
            !d.combatPrefab.GetComponent<BuffCards>().UsesHealthCost);
        var healthDefinition = definitions.First(d => d.combatPrefab.GetComponent<BuffCards>().UsesHealthCost);
        var a = UnityEngine.Object.Instantiate(normalDefinition.combatPrefab,hud.Deck.Hand[0].transform.parent).GetComponent<BuffCards>();
        var b = UnityEngine.Object.Instantiate(healthDefinition.combatPrefab,hud.Deck.Hand[0].transform.parent).GetComponent<BuffCards>();
        a.Initialize(hud.Deck,normalDefinition); b.Initialize(hud.Deck,healthDefinition);
        float energy = player.elixir, maximum = player.maxElixir, interval = player.attackSpd;
        int hp = player.health, shield = player.shield;
        var pointer = new PointerEventData(EventSystem.current);
        Time.timeScale = 0;
        var input = EventSystem.current.currentInputModule; if (input != null) input.enabled = false;
        try
        {
            player.health = 75; player.elixir = 8; player.maxElixir = 10; player.shield = 7; player.attackSpd = 2.5f;
            a.OnPointerEnter(pointer); yield return null;
            Check(view.PreviewCard == a && view.Elixir.Preview.gameObject.activeSelf &&
                Mathf.Abs(view.Elixir.Preview.rectTransform.anchorMin.x - Mathf.Max(0,8-a.Cost)/10f) < .001f &&
                Mathf.Abs(view.Elixir.Preview.rectTransform.anchorMax.x - .8f) < .001f,
                "Preview marks only the consumed interval, without spending resources.");
            Check(player.elixir == 8 && player.health == 75 && Field<TMPro.TMP_Text>(view,"shield").text == "7" &&
                Field<TMPro.TMP_Text>(view,"speed").text == "0.40", "Values track the live fighter and show attacks per second.");
            Canvas.ForceUpdateCanvases();
            foreach (var icon in view.GetComponentsInChildren<StatusIcon>())
            {
                Check(icon.canvasRenderer != null, "Every status icon has a renderer.");
                var mesh = icon.canvasRenderer.GetMesh();
                Check(mesh != null && mesh.vertexCount > 0, "Every status icon has visible geometry.");
            }
            MapOpportunityVerification.CaptureCanvas(hud.Canvas,Camera.main,"combat-cost-preview",(RectTransform)view.transform);
            player.elixir = .5f; yield return null;
            Check(view.Elixir.Hint.text.Contains("Short") && view.Elixir.Preview.color.g < .4f,
                "An unaffordable card has a red preview and a numeric shortfall.");
            player.elixir = 9; yield return null;
            Check(Mathf.Abs(view.Elixir.Preview.rectTransform.anchorMax.x - .9f) < .001f,
                "Resource changes update a stationary hover preview.");
            b.OnPointerEnter(pointer); a.OnPointerExit(pointer); yield return null;
            Check(view.PreviewCard == b && view.Health.Preview.gameObject.activeSelf && !view.Elixir.Preview.gameObject.activeSelf,
                "Health-cost cards use the HP bar and a stale pointer exit cannot clear the next card.");
            b.OnPointerExit(pointer); yield return null;
            Check(!view.Health.Preview.gameObject.activeSelf && !view.Elixir.Preview.gameObject.activeSelf,
                "Leaving the card clears both previews.");
            a.OnPointerEnter(pointer); a.enabled = false; yield return null;
            Check(view.PreviewCard == null && !view.Elixir.Preview.gameObject.activeSelf,
                "Disabled or removed cards leave no cost highlight.");
            view.Elixir.SetValue(0,0,5,true,false);
            Check(view.Elixir.Fill.rectTransform.anchorMax.x == 0 && !view.Elixir.Preview.gameObject.activeSelf,
                "Empty or zero-maximum resources never produce invalid geometry.");
            view.Elixir.SetValue(8,10,0,true,true);
            Check(!view.Elixir.Preview.gameObject.activeSelf && view.Elixir.Hint.text == "No cost", "Zero-cost cards do not highlight a false spend.");
        }
        finally
        {
            view.ClearCost(a); view.ClearCost(b); UnityEngine.Object.Destroy(a.gameObject); UnityEngine.Object.Destroy(b.gameObject);
            player.health=hp; player.elixir=energy; player.maxElixir=maximum; player.shield=shield; player.attackSpd=interval;
            Time.timeScale=1; if (input != null) input.enabled=true;
        }
        yield return null;
    }

    private static IEnumerator Scenario()
    {
        yield return Until(() => Find<MainMenuController>() != null, "Main menu loaded");
        Invoke(Find<MainMenuController>(), "StartRun");
        yield return PreparedBattle();
        var session = RunSession.Instance;
        string firstRun = session.Progress.RunId;
        var hud = BattleHud.Find(SceneManager.GetActiveScene());
        Check(session.Progress.CurrentEncounter.NodeId == "level_01_01" && session.Progress.CompletedCount == 0,
            "Introductory battle is the uncompleted entrance encounter.");
        Check(session.DeckCapacity == 22 && session.GetActiveDeck().Count == 6 && hud.Deck.Hand.Count == 5,
            "Six starter cards leave eight free capacity points; five cards are dealt into the combat hand.");
        var firstCard = hud.Deck.Hand[0];
        float energy = hud.Deck.Player.elixir;
        firstCard.OnPointerClick(new PointerEventData(EventSystem.current));
        Check(hud.Deck.Hand.Count == 5 && hud.Deck.Player.elixir == energy && !TimeTickSystem.Active.IsStarted,
            "Cards cannot play or consume resources during the countdown.");
        double armedAt = EditorApplication.timeSinceStartup;
        yield return Until(() => TimeTickSystem.Active.IsStarted, "Countdown completed");
        Check(EditorApplication.timeSinceStartup - armedAt > 4.5, "The five-second countdown was not bypassed.");
        var playable = hud.Deck.Hand.First(card => Stat(card) != StatType.DamageAllEnemies && Cost(card) <= hud.Deck.Player.elixir);
        playable.OnPointerClick(new PointerEventData(EventSystem.current));
        Check(hud.Deck.Hand.Count == 4, "A played card moves to the discard pile.");
        double playedAt = EditorApplication.timeSinceStartup;
        yield return Until(() => hud.Deck.Hand.Count == 5, "Delayed replacement drawn");
        Check(EditorApplication.timeSinceStartup - playedAt >= 1.7, "Replacement still waits about twenty ticks.");
        var player = hud.Deck.Player;
        player.health = 75; player.attackDmg = 99; player.attackSpd = 1; player.shield = 30;
        player.elixir = 2; player.maxElixir = 40; player.hitsPerAttack = 5; player.elixirRegen = 4;
        KillEnemies();
        yield return Until(() => Find<RewardScreenController>()?.IsPresented == true, "Victory reward presented");
        Check(!TimeTickSystem.Active.IsStarted && !hud.Deck.CanPlay && session.Progress.CompletedCount == 0,
            "Victory stops combat but does not complete the node before its reward.");
        var reward = Find<RewardScreenController>();
        var offer = Field<Transform>(reward, "rewardCardContainer").GetComponentsInChildren<RewardCardView>()[0];
        var incoming = Field<CardDefinition>(offer, "definition");
        var startingIds = session.RunDeck.Select(card => card.instanceId).ToArray();
        int expectedCapacity = session.DeckCapacity + incoming.capacityCost;
        ClickReward(offer);
        Check(Field<GameObject>(reward, "replacementRoot") == null &&
            startingIds.All(id => session.RunDeck.Any(card => card.instanceId == id)) &&
            session.GetActiveDeck().Count == 7 && session.DeckCapacity == expectedCapacity,
            "The first reward uses free capacity without replacing any of the six starter cards.");
        yield return ReadyMap();
        Check(session.Progress.CompletedCount == 1 && session.Progress.NodeState("level_01_01") == MapLocationState.Completed,
            "First victory completes the entrance exactly once.");
        Check(session.Progress.NextNodes("level_01_01").All(id => session.Progress.NodeState(id) == MapLocationState.Available),
            "Only the entrance's three authored successors open.");
        Check(session.PlayerHealth == 75 && session.PlayerDamage == 9 && session.PlayerAttackSpeed == 5 && session.PlayerShield == 0,
            "The map retains HP, not temporary combat attributes.");
        Check(UnityEngine.Object.FindObjectsByType<RunSession>(FindObjectsSortMode.None).Length == 1, "Only one persistent run session survives.");

        var offering = session.RunDeck.First(card => !card.sacrificed && card.definition.overworldEffect.effectType == OverworldEffectType.GrantStartingShield);
        int offeringShield = offering.definition.overworldEffect.magnitude;
        var map = Find<MapController>();
        var view = map.GetComponentsInChildren<MapCardView>(true).FirstOrDefault(card => card.InstanceId == offering.instanceId);
        if (view == null) view = UnityEngine.Object.FindObjectsByType<MapCardView>(FindObjectsSortMode.None).First(card => card.InstanceId == offering.instanceId);
        expectedCapacity = session.DeckCapacity - offering.definition.capacityCost;
        map.RequestSacrifice(view); Field<Button>(map, "confirmSacrificeButton").onClick.Invoke();
        Check(session.DeckCapacity == expectedCapacity && session.HasPendingModifier && session.SacrificeUsed, "Map sacrifice confirmation removes the exact card.");
        session.SetNextOpportunityForTesting(MapOpportunityOutcome.Battle);
        Select("level_02_01");
        yield return Until(() => Find<MapOpportunityController>()?.IsRevealed == true, "Opportunity revealed");
        var chance = Find<MapOpportunityController>();
        Check(chance.Receipt.Outcome == MapOpportunityOutcome.Battle && session.HasPendingModifier && session.SacrificeUsed,
            "Ambush preserves the offering before combat readiness.");
        yield return Until(() => chance.ContinueButton.interactable && !MapTravelCoordinator.Ensure(session).IsBusy,
            "Opportunity transition completed");
        string eventId = chance.EncounterId;
        var battleRouting = chance.routing;
        string validBattle = battleRouting.battleScene;
        try
        {
            battleRouting.battleScene = "MissingTestBattle";
            chance.ContinueButton.onClick.Invoke();
            Check(session.Progress.Phase == MapProgressPhase.InEncounter && session.HasPendingModifier &&
                !MapTravelCoordinator.Ensure(session).IsBusy, "Missing scene keeps the event available for retry.");
        }
        finally { battleRouting.battleScene = validBattle; }
        UnityEngine.Events.UnityAction<Scene, LoadSceneMode> blockHud = null;
        blockHud = (scene, _) =>
        {
            if (scene.name != validBattle) return;
            SceneManager.sceneLoaded -= blockHud;
            BattleHud.Find(scene).enabled = false;
        };
        SceneManager.sceneLoaded += blockHud;
        chance.ContinueButton.onClick.Invoke();
        yield return Until(() => SceneManager.GetActiveScene().name == "MapOpportunity" &&
            Find<MapOpportunityController>()?.IsRevealed == true && !MapTravelCoordinator.Ensure(session).IsBusy,
            "Failed combat receiver returned to the fixed event");
        chance = Find<MapOpportunityController>();
        Check(chance.EncounterId == eventId && chance.Receipt.Outcome == MapOpportunityOutcome.Battle &&
            session.HasPendingModifier && session.Progress.CompletedCount == 1, "Readiness timeout retains event, offering and progression.");
        chance.ContinueButton.onClick.Invoke(); yield return PreparedBattle();
        hud = BattleHud.Find(SceneManager.GetActiveScene()); player = hud.Deck.Player;
        Check(player.shield == offeringShield && session.Progress.CurrentEncounter.Kind == MapEncounterKind.Opportunity &&
            session.Progress.CurrentEncounter.IsCombat && !session.HasPendingModifier, "Ambush receives the offering once and keeps node identity.");
        yield return Until(() => TimeTickSystem.Active.IsStarted, "Ambush countdown");
        KillEnemies(); yield return SkipReward();
        yield return ReadyMap();
        Check(session.Progress.CompletedCount == 2 && session.Progress.CurrentNodeId == "level_02_01" && !session.SacrificeUsed,
            "Ambush victory completes only its opportunity node and restores the offering allowance.");
        Select("level_03_01");
        yield return PreparedBattle();
        hud = BattleHud.Find(SceneManager.GetActiveScene()); player = hud.Deck.Player;
        Check(player.health == 75 && player.shield == 0 && player.attackDmg == 9 && player.attackSpd == 5 &&
            player.elixir == 10 && player.maxElixir == 10 && player.hitsPerAttack == 1 && Mathf.Approximately(player.elixirRegen, .5f),
            "Next battle resets resources and retains no ambush shield.");
        Check(!session.HasPendingModifier, "The confirmed combat consumes the offering.");
        yield return Until(() => TimeTickSystem.Active.IsStarted, "Second combat countdown");
        KillEnemies(); yield return SkipReward(); yield return ReadyMap();
        Check(!session.SacrificeUsed && session.PlayerShield == 0 && session.PlayerHealth == 75, "Combat completion clears its temporary shield and restores sacrifice allowance.");

        Select("level_04_01");
        yield return Until(() => Find<MapNonCombatController>()?.IsActivated == true, "Recovery activated");
        var recovery = Find<MapNonCombatController>();
        yield return Until(() => recovery.ContinueButton.interactable, "Recovery presentation completed");
        VerifyAuthoredEncounter(recovery);
        MapOpportunityVerification.CaptureCanvas(recovery.Panel.GetComponentInParent<Canvas>(), Camera.main, "recovery-hierarchy", recovery.Panel);
        Check(recovery.Receipt.HealthBefore == 75 && recovery.Receipt.HealthAfter == 90 && session.PlayerHealth == 90,
            "The authored recovery restores fifteen HP.");
        session.TryResolveRecovery(recovery.EncounterId, out _);
        Check(session.PlayerHealth == 90, "Recovery cannot be applied twice.");
        recovery.ContinueButton.onClick.Invoke(); yield return ReadyMap();
        Select("level_05_01"); yield return PreparedBattle();
        hud = BattleHud.Find(SceneManager.GetActiveScene());
        Check(hud.Deck.Player.health == 90 && hud.Deck.Player.shield == 0 && hud.Deck.Player.elixir == 10,
            "Recovered HP, but no previous shield, enters the next battle.");
        yield return Until(() => TimeTickSystem.Active.IsStarted, "Fifth-layer countdown");
        KillEnemies(); yield return SkipReward(); yield return ReadyMap();
        Select("level_06_01"); yield return PreparedBattle();
        Check(session.CurrentEncounterIsBoss, "The final node is classified as Boss without changing its combat lineup.");
        yield return Until(() => TimeTickSystem.Active.IsStarted, "Final countdown");
        KillEnemies(); yield return SkipReward();
        yield return Until(() => SceneManager.GetActiveScene().name == "Map" && session.Progress.Phase == MapProgressPhase.Won &&
            Find<MapController>() != null && !MapTravelCoordinator.Ensure(session).IsBusy, "Completed map presentation");
        Check(session.Progress.CompletedCount == 6 && session.Progress.NodeIds.All(id => session.Progress.NodeState(id) != MapLocationState.Available),
            "Six visited nodes complete the run and all further travel is blocked.");
        Find<MapController>().EnterButton.onClick.Invoke();
        yield return Until(() => SceneManager.GetActiveScene().name == "MainMenu" && RunSession.Instance == null, "Return from completed run");
        Invoke(Find<MainMenuController>(), "StartRun"); yield return PreparedBattle();
        session = RunSession.Instance;
        Check(session.Progress.RunId != firstRun && session.Progress.CompletedCount == 0 && session.DeckCapacity == 22 &&
            session.GetActiveDeck().Count == 6 && session.PlayerHealth == 100 && !session.HasPendingModifier && !session.SacrificeUsed,
            "Restart restores the six-card, full-health run with room for rewards.");
        var defeated = BattleHud.Find(SceneManager.GetActiveScene()).Deck.Player;
        defeated.TakeDamage(10000);
        yield return Until(() => Find<RewardScreenController>()?.IsPresented == true, "Defeat presentation");
        Check(session.Progress.Phase == MapProgressPhase.Lost && !TimeTickSystem.Active.IsStarted && session.Progress.CompletedCount == 0,
            "Death stops combat and cannot be mistaken for victory.");
        Field<Button>(Find<RewardScreenController>(), "skipButton").onClick.Invoke();
        yield return Until(() => SceneManager.GetActiveScene().name == "MainMenu" && RunSession.Instance == null, "Defeat returns to clean menu");
        yield return OpportunityPages();
    }

    private static IEnumerator OpportunityPages()
    {
        yield return Until(() => Find<MainMenuController>() != null, "Main menu ready for presentation check");
        var menuCanvas = Find<Canvas>();
        var background = menuCanvas.transform.Find("Background").GetComponent<Image>();
        Check(background.sprite != null && AssetDatabase.GetAssetPath(background.sprite) == "Assets/UI/MainMenu.png",
            "The menu background resolves to the existing authored image.");
        MapOpportunityVerification.CaptureCanvas(menuCanvas, Camera.main, "main-menu");
        SceneManager.LoadSceneAsync("Map"); yield return ReadyMap();
        var map = Find<MapController>();
        var template = UnityEngine.Object.FindObjectsByType<MapCardView>(FindObjectsSortMode.None).First();
        var detailView = UnityEngine.Object.Instantiate(template, template.transform.parent);
        foreach (var definition in AssetDatabase.LoadAssetAtPath<CardPool>("Assets/Data/CardPool.asset").Cards)
        {
            detailView.Initialize(map, definition); map.SetCardDetail(detailView, true);
            Check(Field<Image>(map, "cardDetailBackground").sprite == null &&
                Field<Image>(map, "cardDetailBorder").sprite == definition.backArtwork,
                "The detail uses one back frame and a plain inset for " + definition.name);
            if (definition.cardId == "violet_velocity")
                MapOpportunityVerification.CaptureCanvas(Field<Image>(map, "cardDetailBorder").canvas.rootCanvas,
                    Camera.main, "map-card-detail");
        }
        map.SetCardDetail(detailView, false); UnityEngine.Object.Destroy(detailView);
        Check(map.TryStartTestEncounter("level_02_01", 37, out _, MapOpportunityOutcome.Recovery), "Monitor can force recovery.");
        yield return VerifyOpportunitySpin(MapOpportunityOutcome.Recovery, true);
        yield return ReadyOpportunity();
        var chance = Find<MapOpportunityController>(); var session = RunSession.Instance;
        VerifyAuthoredEncounter(chance);
        MapOpportunityVerification.CaptureLayouts(chance, "recovery");
        string id = chance.EncounterId;
        Check(session.PlayerHealth == 52 && chance.Receipt.Recovered == 15, "Opportunity heals fifteen HP.");
        var reload = SceneManager.LoadSceneAsync("MapOpportunity");
        yield return Until(() => reload.isDone, "Recovery scene reloaded");
        while (Find<MapOpportunityController>()?.IsRevealed != true)
        {
            Check(Find<MapOpportunityController>()?.IsSpinning != true, "A revealed result does not roll again after reload.");
            yield return null;
        }
        yield return ReadyOpportunity();
        chance = Find<MapOpportunityController>();
        Check(chance.EncounterId == id && session.PlayerHealth == 52, "Reloaded recovery does not heal twice.");
        chance.ContinueButton.onClick.Invoke(); yield return ReadyMap();
        Check(Find<MapController>().TryStartTestEncounter("level_02_01", 100, out _, MapOpportunityOutcome.Recovery), "Full-health event test starts.");
        yield return ReadyOpportunity(); chance = Find<MapOpportunityController>();
        Check(chance.Receipt.Recovered == 0 && session.PlayerHealth == 100, "Full health still allows recovery result.");
        chance.ContinueButton.onClick.Invoke(); yield return ReadyMap();
        // Only this disposable fixture starts full, so replacement UI still receives coverage.
        var starterField = typeof(RunSession).GetField("startingDeck", BindingFlags.NonPublic | BindingFlags.Instance);
        var authoredStarter = (CardDefinition[])starterField.GetValue(session);
        try
        {
            var pool = AssetDatabase.LoadAssetAtPath<CardPool>("Assets/Data/CardPool.asset");
            starterField.SetValue(session, authoredStarter.Concat(new[]
            {
                pool.Cards.Single(card => card.cardId == "battleborn"),
                pool.Cards.Single(card => card.cardId == "radiant_ward")
            }).ToArray());
            Check(Find<MapController>().TryStartTestEncounter("level_02_01", 80, out _, MapOpportunityOutcome.Card), "Monitor can force one card.");
        }
        finally { starterField.SetValue(session, authoredStarter); }
        yield return VerifyOpportunitySpin(MapOpportunityOutcome.Card, false);
        yield return ReadyOpportunity(); chance = Find<MapOpportunityController>(); id = chance.EncounterId;
        var offered = chance.Receipt.Offers.Single();
        MapOpportunityVerification.CaptureLayouts(chance, "card");
        Check(session.DeckCapacity == RunSession.CapacityLimit, "Full capacity retained.");
        chance.ContinueButton.onClick.Invoke();
        Check(chance.IsReplacing, "Reward opens replacement choices.");
        var replacementRoot = Field<GameObject>(chance, "replacementRoot");
        var replacementTemplate = Field<RewardCardView>(chance, "replacementTemplate");
        var firstChoices = replacementRoot.GetComponentsInChildren<RewardCardView>();
        Check(!replacementTemplate.gameObject.activeSelf && firstChoices.Length == session.GetActiveDeck().Count,
            "The inactive authored template generates exactly one view per active card.");
        MapOpportunityVerification.CaptureLayouts(chance, "replacement");
        chance.SecondaryButton.onClick.Invoke();
        Check(!chance.IsReplacing && !chance.Receipt.Resolved && chance.Receipt.Offers.Single() == offered,
            "Cancel replacement retains the offered card.");
        chance.ContinueButton.onClick.Invoke();
        Check(replacementRoot == Field<GameObject>(chance, "replacementRoot") &&
            firstChoices.SequenceEqual(replacementRoot.GetComponentsInChildren<RewardCardView>()),
            "Reopening replacement preserves the authored root and reuses the existing card views.");
        chance.SecondaryButton.onClick.Invoke();
        reload = SceneManager.LoadSceneAsync("MapOpportunity");
        yield return Until(() => reload.isDone, "Card event scene reloaded"); yield return ReadyOpportunity();
        chance = Find<MapOpportunityController>();
        Check(chance.EncounterId == id && chance.Receipt.Offers.Single() == offered, "Reload does not reroll the offered card.");
        chance.ContinueButton.onClick.Invoke();
        var active = session.RunDeck.Where(c => !c.sacrificed).ToArray();
        int replacementIndex = Array.FindIndex(active, c => c.definition.capacityCost >= offered.capacityCost);
        Check(replacementIndex >= 0, "At least one starter can be exchanged for the offered capacity tier.");
        string removed = active[replacementIndex].instanceId;
        int expectedCapacity = session.DeckCapacity - active[replacementIndex].definition.capacityCost + offered.capacityCost;
        var choices = Field<GameObject>(chance, "replacementRoot").GetComponentsInChildren<RewardCardView>();
        ClickReward(choices[replacementIndex]);
        Check(chance.Receipt.Resolved && session.DeckCapacity == expectedCapacity && !session.RunDeck.Any(c => c.instanceId == removed),
            "Replacement UI commits only one chosen instance.");
        var coordinator = MapTravelCoordinator.Ensure(session);
        var mapSceneField = typeof(MapTravelCoordinator).GetField("mapScene", BindingFlags.NonPublic | BindingFlags.Instance);
        string mapScene = (string)mapSceneField.GetValue(coordinator);
        try
        {
            mapSceneField.SetValue(coordinator, "MissingTestMap");
            chance.ContinueButton.onClick.Invoke();
            yield return Until(() => !coordinator.IsBusy, "Failed map return retained settled reward");
            Check(chance.Receipt.Completed && session.DeckCapacity == expectedCapacity && SceneManager.GetActiveScene().name == "MapOpportunity",
                "A failed return neither rerolls nor grants again.");
        }
        finally { mapSceneField.SetValue(coordinator, mapScene); }
        chance.ContinueButton.onClick.Invoke(); yield return ReadyMap();
        Check(Find<MapController>().TryStartTestEncounter("level_02_01", 80, out _, MapOpportunityOutcome.Card), "Skip test starts.");
        yield return ReadyOpportunity(); chance = Find<MapOpportunityController>();
        string[] original = session.RunDeck.Select(c => c.instanceId).ToArray();
        chance.SecondaryButton.onClick.Invoke(); yield return ReadyMap();
        Check(original.SequenceEqual(session.RunDeck.Select(c => c.instanceId)) && session.LastNonCombatReceipt.Skipped,
            "Skipping completes the event without changing the deck.");
        Check(Find<MapController>().TryStartTestEncounter("level_02_01", 80, out _, MapOpportunityOutcome.Battle), "Ambush defeat test starts.");
        yield return VerifyOpportunitySpin(MapOpportunityOutcome.Battle, false);
        yield return ReadyOpportunity();
        MapOpportunityVerification.CaptureLayouts(Find<MapOpportunityController>(), "ambush");
        Find<MapOpportunityController>().ContinueButton.onClick.Invoke(); yield return PreparedBattle();
        BattleHud.Find(SceneManager.GetActiveScene()).Deck.Player.TakeDamage(10000);
        yield return Until(() => Find<RewardScreenController>()?.IsPresented == true, "Ambush defeat displayed");
        Check(session.Progress.Phase == MapProgressPhase.Lost && session.Progress.CompletedCount == 0, "Ambush defeat completes no node.");
        Field<Button>(Find<RewardScreenController>(), "skipButton").onClick.Invoke();
        yield return Until(() => SceneManager.GetActiveScene().name == "MainMenu" && RunSession.Instance == null, "Ambush defeat returns to menu");
    }

    private static IEnumerator VerifyOpportunitySpin(MapOpportunityOutcome outcome, bool capture)
    {
        yield return Until(() => Find<MapOpportunityController>()?.IsSpinning == true, "Opportunity spin started");
        var chance = Find<MapOpportunityController>(); var session = RunSession.Instance;
        Check(!MapTravelCoordinator.Ensure(session).IsBusy, "The arrival fade finishes before the spin begins.");
        Check(!chance.IsRevealed && !chance.ContinueButton.gameObject.activeSelf && !chance.SecondaryButton.gameObject.activeSelf &&
            !Field<TMPro.TMP_Text>(chance, "value").gameObject.activeSelf, "The result and actions stay hidden during reveal.");
        var icon = Field<RawImage>(chance, "icon");
        Check(icon.rectTransform.sizeDelta.x == 240 && icon.rectTransform.anchoredPosition == new Vector2(0, 20),
            "The rotating icon occupies the center of the panel.");
        int beforeHealth = session.PlayerHealth; int beforeCapacity = session.DeckCapacity;
        chance.ContinueButton.onClick.Invoke(); chance.SecondaryButton.onClick.Invoke();
        Check(!chance.Receipt.Resolved && session.PlayerHealth == beforeHealth && session.DeckCapacity == beforeCapacity,
            "Early action callbacks cannot settle or skip the event.");
        if (capture) MapOpportunityVerification.CaptureLayouts(chance, "spinning");
        var glyphs = new HashSet<Rect>();
        Quaternion initial = icon.rectTransform.localRotation;
        bool rotated = false;
        double started = EditorApplication.timeSinceStartup;
        while (chance.IsSpinning)
        {
            glyphs.Add(icon.uvRect);
            rotated |= Quaternion.Angle(initial, icon.rectTransform.localRotation) > 10f;
            var angles = icon.rectTransform.localEulerAngles;
            Check(Mathf.Abs(Mathf.DeltaAngle(0, angles.x)) < .1f && Mathf.Abs(Mathf.DeltaAngle(0, angles.z)) < .1f &&
                Mathf.Abs(Mathf.DeltaAngle(0, angles.y)) <= 90.1f,
                "Opportunity flips around its vertical axis; faces are upright and never mirrored.");
            if (EditorApplication.timeSinceStartup - started > 12) throw new InvalidOperationException("Spin did not settle.");
            yield return null;
        }
        var tile = MapTravelView.AtlasRect(outcome == MapOpportunityOutcome.Card ? 10 : outcome == MapOpportunityOutcome.Recovery ? 11 : 8);
        Check(rotated && glyphs.Count >= 2, "The icon rotates and switches among event symbols.");
        Check(icon.uvRect == new Rect(tile.x, tile.y, tile.z, tile.w) &&
            Quaternion.Angle(icon.rectTransform.localRotation, Quaternion.identity) < .1f && chance.Receipt.Outcome == outcome,
            "The spin stops upright on the committed outcome.");
        Check(session.PlayerHealth == beforeHealth && session.DeckCapacity == beforeCapacity,
            "No reward or recovery is committed during the spin.");
    }

    private static void VerifyAuthoredEncounter(MapNonCombatController receiver)
    {
        MapEncounterSceneAuthoring.Validate(receiver.gameObject.scene);
        var texts = receiver.Panel.GetComponentsInChildren<TMPro.TMP_Text>(true);
        Check(texts.All(text => AssetDatabase.Contains(text.font) && AssetDatabase.Contains(text.fontSharedMaterial)),
            "Encounter typography uses persistent assets and is not replaced with runtime fonts.");
        var panel = receiver.Panel;
        var before = panel.anchoredPosition;
        var target = texts.First(text => text.name == "Body");
        var textPosition = target.rectTransform.anchoredPosition;
        float fontSize = target.fontSize;
        Color color = target.color;
        try
        {
            panel.anchoredPosition += new Vector2(13, 7);
            target.rectTransform.anchoredPosition += new Vector2(11, 5);
            target.fontSize += 2; target.color = Color.cyan;
            var display = receiver.GetType().GetMethod("DisplayReceipt", BindingFlags.Instance | BindingFlags.NonPublic);
            display.Invoke(receiver, new object[] { receiver.Receipt, 1f });
            Check(panel.anchoredPosition == before + new Vector2(13, 7) &&
                target.rectTransform.anchoredPosition == textPosition + new Vector2(11, 5) &&
                target.fontSize == fontSize + 2 && target.color == Color.cyan,
                "Updating encounter data preserves authored panel placement, text placement, size and color.");
        }
        finally
        {
            panel.anchoredPosition = before; target.rectTransform.anchoredPosition = textPosition;
            target.fontSize = fontSize; target.color = color;
        }
    }

    private static IEnumerator ReadyOpportunity() => Until(() => SceneManager.GetActiveScene().name == "MapOpportunity" &&
        Find<MapOpportunityController>()?.IsRevealed == true && !MapTravelCoordinator.Ensure(RunSession.Instance).IsBusy,
        "Opportunity interface ready");

    private static IEnumerator PreparedBattle()
    {
        yield return Until(() => SceneManager.GetActiveScene().name == "Deinosavros" &&
            RunSession.Instance?.Progress?.Phase == MapProgressPhase.InEncounter && BattleHud.Find(SceneManager.GetActiveScene())?.CountdownArmed == true,
            "Combat receiver ready");
        var session = RunSession.Instance;
        int layer = session.Progress.CurrentEncounterLayer;
        var enemies = BattleScript.FindFighters("Enemy");
        Check(enemies.Count == 3 && enemies.All(e => e.maxHealth == session.Rules.EnemyMaxHealthAtLayer(20, layer) &&
            e.attackDmg == session.Rules.EnemyDamageAtLayer(2, layer) && Mathf.Approximately(e.attackSpd, 5)),
            "The confirmed receiver applies layer difficulty before countdown, keeping enemy count and attack intervals.");
        Check(!session.GetComponent<BattleRunBridge>().InitializeConfirmedEncounter(session.Progress.CurrentEncounter.EncounterId,
            BattleHud.Find(SceneManager.GetActiveScene()).Deck.Player), "Duplicate confirmed entry is rejected.");
    }
    private static IEnumerator ReadyMap() => Until(() => SceneManager.GetActiveScene().name == "Map" && Find<MapController>()?.CanInteract == true,
        "Interactive map ready");
    private static IEnumerator Until(Func<bool> condition, string name)
    {
        double timeout = EditorApplication.timeSinceStartup + 45;
        while (!condition())
        {
            if (EditorApplication.timeSinceStartup > timeout) throw new InvalidOperationException("Timed out: " + name);
            yield return null;
        }
        File.AppendAllText(RunIntegrationSetup.ResultDirectory + "/play-progress.txt", name + "\n");
        yield return null;
    }
    private static IEnumerator SkipReward()
    {
        yield return Until(() => Find<RewardScreenController>()?.IsPresented == true, "Reward ready to skip");
        Field<Button>(Find<RewardScreenController>(), "skipButton").onClick.Invoke();
    }
    private static void KillEnemies()
    {
        foreach (var enemy in BattleScript.FindFighters("Enemy")) enemy.TakeDamage(10000);
    }
    private static void Select(string id)
    {
        var map = Find<MapController>(); map.SelectNode(map.Nodes.Single(node => node.NodeId == id));
    }
    private static void ClickReward(RewardCardView view) => Field<Button>(view, "chooseButton").onClick.Invoke();
    private static StatType Stat(BuffCards card) => Field<StatType>(card, "stat");
    private static int Cost(BuffCards card) => Field<int>(card, "elixirCost");
    private static T Field<T>(object target, string name) => (T)target.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance).GetValue(target);
    private static void Invoke(object target, string name) => target.GetType().GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance).Invoke(target, null);
    private static T Find<T>() where T : Component => UnityEngine.Object.FindObjectsByType<T>(FindObjectsSortMode.None)
        .FirstOrDefault(component => component is MapController map
            ? map.TravelView != null && map.TravelView.gameObject.scene == SceneManager.GetActiveScene()
            : component.gameObject.scene == SceneManager.GetActiveScene());
    private static void Check(bool value, string message)
    {
        assertions++; if (!value) throw new InvalidOperationException(message);
    }
}
