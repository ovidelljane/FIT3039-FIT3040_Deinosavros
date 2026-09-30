using System;
using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// Lives with RunSession so the transition survives destruction of scene-owned UI.
public sealed class MapTravelCoordinator : MonoBehaviour
{
    private RunSession session;
    private CanvasGroup curtain;
    private Coroutine routine;
    private BattleScript player;
    private string battleScene, mapScene = "Map", encounterId;
    private bool battleResultPending;
    private string completedRewardRunId, completedRewardEncounterId;
    private string opportunityReturnScene;
    private GameObject opportunityRetry;
    public bool IsBusy => routine != null;
    public string LastNotice { get; private set; }
    public void ClearNotice() => LastNotice = null;
    public static MapTravelCoordinator Ensure(RunSession owner)
    {
        var value = owner.GetComponent<MapTravelCoordinator>();
        if (value == null) value = owner.gameObject.AddComponent<MapTravelCoordinator>();
        value.session = owner; value.EnsureCurtain(); return value;
    }
    private void OnEnable()
    {
        BattleScript.OnBattleFinished += OnBattleFinished;
        SceneManager.sceneLoaded += SceneLoaded;
    }
    private void OnDisable()
    {
        BattleScript.OnBattleFinished -= OnBattleFinished;
        SceneManager.sceneLoaded -= SceneLoaded;
    }
    private void SceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (scene.name != battleScene) player = null;
    }
    private void Update()
    {
        if (!battleResultPending && session?.Progress?.Phase == MapProgressPhase.InEncounter && player != null && player.health <= 0)
            ReportBattleResult(player, false);
    }

    public bool BeginInitialEncounter(string destination)
    {
        if (IsBusy || session?.Progress == null || session.Progress.CompletedCount != 0 ||
            !Application.CanStreamedLevelBeLoaded(destination) ||
            !session.TryBeginTravel(session.Progress.CurrentNodeId, out var ticket)) return false;
        if (!ticket.IsCombat) { session.CancelPreparedTravel(ticket.EncounterId); return false; }
        ResetBattleResult(ticket, destination);
        routine = StartCoroutine(LoadInitialEncounter(ticket));
        return true;
    }

    private IEnumerator LoadInitialEncounter(MapTravelTicket ticket)
    {
        yield return null;
        AsyncOperation load = null;
        try { load = SceneManager.LoadSceneAsync(battleScene, LoadSceneMode.Single); }
        catch (Exception ex) { LastNotice = "The first encounter could not load: " + ex.Message; }
        if (load == null)
        {
            session.CancelPreparedTravel(ticket.EncounterId);
            routine = null; ReturnToMainMenu(); yield break;
        }
        session.Progress.MarkLoading(ticket.EncounterId);
        while (!load.isDone) yield return null;
        yield return PrepareLoadedEncounter(ticket, 5f, .25f);
        routine = null;
    }

    private void ResetBattleResult(MapTravelTicket ticket, string destination)
    {
        encounterId = ticket.EncounterId; battleScene = destination; player = null;
        battleResultPending = false; completedRewardRunId = null; completedRewardEncounterId = null;
        LastNotice = null;
    }
    public bool Begin(MapController owner, MapTravelView visuals, string destination)
    {
        if (IsBusy || owner == null || visuals == null || session?.Progress == null ||
            !visuals.HasPath(session.Progress.CurrentNodeId, owner.SelectedNodeId)) return false;
        if (!Application.CanStreamedLevelBeLoaded(destination))
        { LastNotice = "The encounter scene is unavailable. Your offering is preserved."; owner.SetTravelNotice(LastNotice); return false; }
        if (!session.TryBeginTravel(owner.SelectedNodeId, out var ticket)) return false;
        mapScene = SceneManager.GetActiveScene().name;
        ResetBattleResult(ticket, destination);
        owner.CloseForTravel();
        routine = StartCoroutine(Travel(owner, visuals, ticket)); return true;
    }
    private IEnumerator Travel(MapController owner, MapTravelView visuals, MapTravelTicket ticket)
    {
        // Let Begin retain the coroutine handle before any synchronous failure can finish it.
        yield return null;
        var profile = visuals.profile; AsyncOperation load = null; string failure = null;
        try { load = SceneManager.LoadSceneAsync(battleScene, LoadSceneMode.Single); load.allowSceneActivation = false; }
        catch (Exception ex) { failure = "The encounter could not load: " + ex.Message; }
        if (load == null)
        {
            session.CancelPreparedTravel(ticket.EncounterId);
            LastNotice = failure ?? "The encounter could not load. Your offering is preserved.";
            visuals.RestorePosition(ticket.FromNodeId); owner.SetTravelNotice(LastNotice); routine = null; yield break;
        }
        var animation = visuals.Animate(ticket);
        while (true)
        {
            bool advanced;
            try { advanced = animation.MoveNext(); }
            catch (Exception ex) { advanced = false; failure = "Travel presentation failed: " + ex.Message; }
            if (!advanced) break;
            yield return animation.Current;
        }
        if (failure != null || !session.Progress.MarkLoading(ticket.EncounterId))
        {
            LastNotice = failure ?? "This departure is no longer active.";
            yield return Fade(1, profile.fadeOutTime);
            load.allowSceneActivation = true; while (!load.isDone) yield return null;
            session.CancelPreparedTravel(ticket.EncounterId);
            if (owner != null) Destroy(owner);
            yield return ReturnToMap(profile.fadeInTime); routine = null; yield break;
        }
        owner.SetTravelNotice("Opening encounter...");
        while (load.progress < .9f) yield return null;
        yield return Fade(1, profile.fadeOutTime);
        load.allowSceneActivation = true;
        while (!load.isDone) yield return null;
        if (owner != null) Destroy(owner);
        yield return PrepareLoadedEncounter(ticket, profile.readyTimeout, profile.fadeInTime);
        routine = null;
    }

    private IEnumerator PrepareLoadedEncounter(MapTravelTicket ticket, float readyTimeout, float fadeInTime)
    {
        // Start methods initialize the hand and player after sceneLoaded.
        yield return null;
        float elapsed = 0;
        var hud = BattleHud.Find(SceneManager.GetSceneByName(battleScene));
        var nonCombat = MapNonCombatController.Find(SceneManager.GetSceneByName(battleScene));
        bool receiverReady = false;
        while (elapsed < readyTimeout)
        {
            if (ticket.IsCombat && hud == null) hud = BattleHud.Find(SceneManager.GetSceneByName(battleScene));
            if (!ticket.IsCombat && nonCombat == null) nonCombat = MapNonCombatController.Find(SceneManager.GetSceneByName(battleScene));
            player = ticket.IsCombat ? FindReadyPlayer(battleScene) : null;
            receiverReady = ticket.IsCombat
                ? player != null && player.health > 0 && hud != null && hud.TryPrepare(session, player)
                : nonCombat != null && nonCombat.TryPrepare(session, ticket);
            if (receiverReady) break;
            elapsed += Time.unscaledDeltaTime; yield return null;
        }
        var bridge = session.GetComponent<BattleRunBridge>();
        if (!receiverReady || elapsed >= readyTimeout || (ticket.IsCombat &&
            (player == null || !player.isActiveAndEnabled || player.health <= 0 || bridge == null)))
        {
            if (ticket.Kind == MapEncounterKind.Opportunity && ticket.IsCombat)
            {
                session.CancelOpportunityBattle(ticket.EncounterId); player = null;
                LastNotice = "Battle not ready. Your offering is preserved. Retry the battle.";
                yield return ReturnToOpportunity(); yield break;
            }
            session.CancelPreparedTravel(ticket.EncounterId); player = null;
            LastNotice = "The encounter did not become ready. Your offering is preserved; choose a route to retry.";
            yield return ReturnToMap(fadeInTime); routine = null; yield break;
        }
        if (!session.ConfirmEncounterStarted(ticket.EncounterId))
        {
            player = null; LastNotice = "The encounter request expired. Return to the map to retry.";
            yield return ReturnToMap(fadeInTime); routine = null; yield break;
        }
        if (ticket.IsCombat)
        {
            if (!bridge.InitializeConfirmedEncounter(ticket.EncounterId, player))
                throw new InvalidOperationException("The confirmed combat receiver could not initialize.");
        }
        else nonCombat.ActivateEncounter();
        yield return Fade(0, fadeInTime);
        if (ticket.IsCombat) hud.ArmCountdown();
    }

    public bool BeginOpportunityBattle(MapOpportunityController receiver)
    {
        if (IsBusy || receiver == null || receiver.gameObject.scene != SceneManager.GetActiveScene() ||
            receiver.routing == null || !Application.CanStreamedLevelBeLoaded(receiver.routing.battleScene) ||
            !Application.CanStreamedLevelBeLoaded(receiver.gameObject.scene.name) ||
            !session.TryPrepareOpportunityBattle(receiver.EncounterId)) return false;
        opportunityReturnScene = receiver.gameObject.scene.name;
        var ticket = session.Progress.CurrentEncounter;
        ResetBattleResult(ticket, receiver.routing.battleScene);
        routine = StartCoroutine(LoadOpportunityBattle(receiver, ticket)); return true;
    }

    private IEnumerator LoadOpportunityBattle(MapOpportunityController receiver, MapTravelTicket ticket)
    {
        yield return null;
        yield return Fade(1, .35f);
        AsyncOperation load = null;
        try { load = SceneManager.LoadSceneAsync(battleScene, LoadSceneMode.Single); }
        catch (Exception ex) { LastNotice = "Battle could not load: " + ex.Message; }
        if (load == null)
        {
            session.CancelOpportunityBattle(ticket.EncounterId);
            yield return Fade(0, .25f);
            if (receiver != null) receiver.ShowBattleError("Battle unavailable. Your offering is preserved.");
            routine = null; yield break;
        }
        while (!load.isDone) yield return null;
        yield return PrepareLoadedEncounter(ticket, 5f, .25f);
        routine = null;
    }

    private IEnumerator ReturnToOpportunity()
    {
        yield return null;
        Time.timeScale = 1;
        AsyncOperation load = null;
        try
        {
            if (Application.CanStreamedLevelBeLoaded(opportunityReturnScene))
                load = SceneManager.LoadSceneAsync(opportunityReturnScene, LoadSceneMode.Single);
        }
        catch (Exception ex) { LastNotice = "Event could not load: " + ex.Message; }
        if (load == null)
        {
            ShowOpportunityReturnRetry(); routine = null; yield break;
        }
        while (!load.isDone) yield return null;
        if (opportunityRetry != null) Destroy(opportunityRetry);
        yield return null;
        var receiver = MapNonCombatController.Find(SceneManager.GetActiveScene()) as MapOpportunityController;
        if (receiver != null) receiver.ShowBattleError("Battle not ready. Retry when ready.");
        yield return Fade(0, .25f);
    }

    private void ShowOpportunityReturnRetry()
    {
        if (opportunityRetry != null) return;
        opportunityRetry = new GameObject("Retry", typeof(RectTransform), typeof(Image), typeof(Button));
        var rect = (RectTransform)opportunityRetry.transform; rect.SetParent(curtain.transform, false);
        rect.anchorMin = rect.anchorMax = new Vector2(.5f, .5f); rect.sizeDelta = new Vector2(400, 90);
        opportunityRetry.GetComponent<Image>().color = new Color(.29f, .21f, .12f);
        var label = new GameObject("Label", typeof(RectTransform), typeof(TMPro.TextMeshProUGUI));
        var text = label.GetComponent<TMPro.TextMeshProUGUI>(); label.transform.SetParent(rect, false);
        text.rectTransform.sizeDelta = rect.sizeDelta; GameFonts.Apply(text);
        text.text = "Event saved - Retry Return"; text.fontSize = 24;
        text.alignment = TMPro.TextAlignmentOptions.Center; text.raycastTarget = false;
        opportunityRetry.GetComponent<Button>().onClick.AddListener(() =>
        { if (!IsBusy) routine = StartCoroutine(RetryOpportunityReturn()); });
    }
    private IEnumerator RetryOpportunityReturn()
    { yield return ReturnToOpportunity(); routine = null; }
    public bool ContinueNonCombat(MapNonCombatController receiver, string id)
    {
        if (IsBusy || receiver == null || receiver.gameObject.scene != SceneManager.GetActiveScene()) return false;
        var receipt = session.GetNonCombatReceipt(id);
        if (receipt == null || receipt.RunId != session.Progress.RunId) return false;
        if (!receipt.Completed && !session.CompleteNonCombatEncounter(id)) return false;
        if (session.LastNonCombatReceipt != receipt || session.Progress.Phase != MapProgressPhase.OnMap ||
            session.Progress.CurrentNodeId != receipt.NodeId) return false;
        routine = StartCoroutine(ReturnNonCombat(receiver)); return true;
    }
    private IEnumerator ReturnNonCombat(MapNonCombatController receiver)
    {
        yield return null;
        yield return Fade(1, .35f);
        AsyncOperation load = null; string error = null;
        try
        {
            if (Application.CanStreamedLevelBeLoaded(mapScene)) load = SceneManager.LoadSceneAsync(mapScene, LoadSceneMode.Single);
            else error = "The map is unavailable. Your result is saved; retry returning.";
        }
        catch (Exception ex) { error = "The map could not load. Your result is saved: " + ex.Message; }
        if (load == null)
        {
            yield return Fade(0, .25f); routine = null;
            receiver.ShowReturnError(error ?? "The map could not load. Retry returning."); yield break;
        }
        while (!load.isDone) yield return null;
        yield return Fade(0, .25f); routine = null;
    }
    private void OnBattleFinished(BattleScript source, bool victory) => ReportBattleResult(source, victory);

    public bool ReportBattleResult(BattleScript source, bool victory)
    {
        if (battleResultPending || source == null || source != player || session?.Progress?.Phase != MapProgressPhase.InEncounter ||
            session.Progress.CurrentEncounter?.EncounterId != encounterId || SceneManager.GetActiveScene().name != battleScene)
            return false;
        victory &= source.health > 0;
        battleResultPending = true;
        session.CapturePlayerStats(source);
        BattleHud.Find(source.gameObject.scene)?.StopCombat();
        Time.timeScale = 0f;
        var receiver = FindRewardReceiver(source.gameObject.scene);
        if (victory)
        {
            if (receiver != null) receiver.PresentVictory(session, encounterId);
            else
            {
                session.TryPrepareBattleReward(encounterId, null, 0, out _);
                session.TryResolveBattleReward(encounterId, null, null, out _);
                FinishBattleReward();
            }
        }
        else
        {
            session.CompleteEncounter(encounterId, false);
            if (receiver != null) receiver.PresentDefeat(ReturnToMainMenu);
            else ReturnToMainMenu();
        }
        return true;
    }

    public bool ContinueBattleReward(RewardScreenController receiver, string id)
    {
        if (IsBusy || receiver == null || receiver.gameObject.scene != SceneManager.GetActiveScene() ||
            id != encounterId || !battleResultPending) return false;
        bool completed = completedRewardEncounterId == id && completedRewardRunId == session.Progress.RunId;
        if (!completed)
        {
            if (!session.IsBattleRewardResolved(id)) return false;
            if (!session.CompleteEncounter(id, true)) return false;
            completedRewardEncounterId = id; completedRewardRunId = session.Progress.RunId;
        }
        routine = StartCoroutine(ReturnBattleReward(receiver));
        return true;
    }

    private void FinishBattleReward()
    {
        if (!session.IsBattleRewardResolved(encounterId) || !session.CompleteEncounter(encounterId, true)) return;
        completedRewardEncounterId = encounterId; completedRewardRunId = session.Progress.RunId;
        routine = StartCoroutine(ReturnBattleReward(null));
    }

    private IEnumerator ReturnBattleReward(RewardScreenController receiver)
    {
        yield return null;
        yield return Fade(1, .35f);
        Time.timeScale = 1f;
        AsyncOperation load = null;
        try
        {
            if (Application.CanStreamedLevelBeLoaded(mapScene)) load = SceneManager.LoadSceneAsync(mapScene, LoadSceneMode.Single);
        }
        catch (Exception ex) { LastNotice = "The map could not load: " + ex.Message; }
        if (load == null)
        {
            LastNotice ??= "The map is unavailable. Your reward is saved; retry returning.";
            yield return Fade(0, .25f); routine = null;
            if (receiver != null) receiver.ShowReturnError(LastNotice);
            yield break;
        }
        while (!load.isDone) yield return null;
        player = null;
        yield return Fade(0, .25f); routine = null;
    }

    public void ReturnToMainMenu()
    {
        if (IsBusy || !Application.CanStreamedLevelBeLoaded("MainMenu")) return;
        routine = StartCoroutine(LoadMainMenu());
    }

    private IEnumerator LoadMainMenu()
    {
        yield return null;
        BattleHud.Find(SceneManager.GetActiveScene())?.StopCombat();
        yield return Fade(1, .25f);
        Time.timeScale = 1f;
        var load = SceneManager.LoadSceneAsync("MainMenu", LoadSceneMode.Single);
        while (!load.isDone) yield return null;
        yield return Fade(0, .25f);
        Destroy(session.gameObject);
    }

    public static BattleScript FindReadyPlayer(string sceneName) => BattleHud.FindPlayer(SceneManager.GetSceneByName(sceneName));

    private static RewardScreenController FindRewardReceiver(Scene scene)
    {
        foreach (var root in scene.GetRootGameObjects())
        {
            var receiver = root.GetComponentInChildren<RewardScreenController>();
            if (receiver != null && receiver.isActiveAndEnabled) return receiver;
        }
        return null;
    }

    private IEnumerator ReturnToMap(float fade)
    {
        Time.timeScale = 1f;
        var load = SceneManager.LoadSceneAsync(mapScene, LoadSceneMode.Single);
        while (load != null && !load.isDone) yield return null;
        player = null;
        yield return Fade(0, fade);
    }
    private void EnsureCurtain()
    {
        if (curtain != null) return;
        var root = new GameObject("Transition", typeof(RectTransform), typeof(Canvas), typeof(CanvasGroup), typeof(GraphicRaycaster));
        root.transform.SetParent(transform, false);
        var canvas = root.GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 32000;
        curtain = root.GetComponent<CanvasGroup>(); curtain.alpha = 0; curtain.blocksRaycasts = false;
        var shade = new GameObject("Shade", typeof(RectTransform), typeof(Image)); shade.transform.SetParent(root.transform, false);
        var rect = shade.GetComponent<RectTransform>(); rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.offsetMin = rect.offsetMax = Vector2.zero;
        shade.GetComponent<Image>().color = new Color(.025f, .018f, .012f, 1);
        root.SetActive(false);
    }
    private IEnumerator Fade(float alpha, float seconds)
    {
        EnsureCurtain(); curtain.gameObject.SetActive(true); curtain.blocksRaycasts = true;
        float start = curtain.alpha;
        for (float t = 0; t < seconds; t += Time.unscaledDeltaTime)
        { curtain.alpha = Mathf.Lerp(start, alpha, Mathf.SmoothStep(0, 1, t / seconds)); yield return null; }
        curtain.alpha = alpha; curtain.blocksRaycasts = alpha > 0;
        if (alpha == 0) curtain.gameObject.SetActive(false);
    }
}
