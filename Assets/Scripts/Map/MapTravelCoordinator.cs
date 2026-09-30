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
        BattleScript.OnAllEnemiesDefeated += OnVictory;
        SceneManager.sceneLoaded += SceneLoaded;
    }
    private void OnDisable()
    {
        BattleScript.OnAllEnemiesDefeated -= OnVictory;
        SceneManager.sceneLoaded -= SceneLoaded;
    }
    private void SceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (scene.name != battleScene) player = null;
    }
    private void Update()
    {
        if (session?.Progress?.Phase == MapProgressPhase.InEncounter && player != null && player.health <= 0)
            ReportResult(false);
        if (session?.Progress?.Phase == MapProgressPhase.Lost && player != null && routine == null)
        { LastNotice = "The adventure has ended."; routine = StartCoroutine(ReturnAfterDefeat()); }
        if (session?.Progress != null && player != null && routine == null && SceneManager.GetActiveScene().name == battleScene &&
            (session.Progress.Phase == MapProgressPhase.OnMap || session.Progress.Phase == MapProgressPhase.Won) && !HasRewardReceiver())
        { LastNotice = null; routine = StartCoroutine(ReturnAfterDefeat()); }
    }
    public bool Begin(MapController owner, MapTravelView visuals, string destination)
    {
        if (IsBusy || owner == null || visuals == null || session?.Progress == null ||
            !visuals.HasPath(session.Progress.CurrentNodeId, owner.SelectedNodeId)) return false;
        if (!Application.CanStreamedLevelBeLoaded(destination))
        { LastNotice = "The encounter scene is unavailable. Your offering is preserved."; owner.SetTravelNotice(LastNotice); return false; }
        if (!session.TryBeginTravel(owner.SelectedNodeId, out var ticket)) return false;
        LastNotice = null; battleScene = destination; mapScene = SceneManager.GetActiveScene().name;
        encounterId = ticket.EncounterId;
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
        // Start methods initialize the hand and player after sceneLoaded.
        yield return null;
        float elapsed = 0;
        var hud = BattleHud.Find(SceneManager.GetSceneByName(battleScene));
        var nonCombat = MapNonCombatController.Find(SceneManager.GetSceneByName(battleScene));
        bool receiverReady = false;
        while (elapsed < profile.readyTimeout)
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
        if (!receiverReady || elapsed >= profile.readyTimeout || (ticket.IsCombat &&
            (player == null || !player.isActiveAndEnabled || player.health <= 0 || bridge == null)))
        {
            session.CancelPreparedTravel(ticket.EncounterId); player = null;
            LastNotice = "The encounter did not become ready. Your offering is preserved; choose a route to retry.";
            yield return ReturnToMap(profile.fadeInTime); routine = null; yield break;
        }
        if (!session.ConfirmEncounterStarted(ticket.EncounterId))
        {
            player = null; LastNotice = "The encounter request expired. Return to the map to retry.";
            yield return ReturnToMap(profile.fadeInTime); routine = null; yield break;
        }
        if (ticket.IsCombat) bridge.InitializeConfirmedEncounter(ticket.EncounterId, player);
        else nonCombat.ActivateEncounter();
        yield return Fade(0, profile.fadeInTime);
        routine = null;
    }
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
    private void OnVictory()
    {
        if (session?.Progress?.Phase != MapProgressPhase.InEncounter || player == null ||
            SceneManager.GetActiveScene().name != battleScene) return;
        ReportResult(player.health > 0);
    }
    public static BattleScript FindReadyPlayer(string sceneName)
    {
        // Shadow objects share the Player tag but have disabled combat components.
        foreach (var candidate in FindObjectsByType<BattleScript>(FindObjectsSortMode.None))
            if (candidate.gameObject.scene.name == sceneName && candidate.CompareTag("Player") && candidate.isActiveAndEnabled) return candidate;
        return null;
    }
    private static bool HasRewardReceiver()
    {
        foreach (var receiver in FindObjectsByType<RewardScreenController>(FindObjectsSortMode.None))
            if (receiver.isActiveAndEnabled) return true;
        return false;
    }
    private void ReportResult(bool victory)
    {
        if (session?.Progress?.CurrentEncounter == null || session.Progress.CurrentEncounter.EncounterId != encounterId) return;
        session.CapturePlayerStats(player);
        if (!session.CompleteEncounter(encounterId, victory)) return;
        if (!victory && routine == null)
        { LastNotice = "The adventure has ended."; routine = StartCoroutine(ReturnAfterDefeat()); }
    }
    private IEnumerator ReturnAfterDefeat()
    {
        yield return Fade(1, .35f); yield return ReturnToMap(.25f); routine = null;
    }
    private IEnumerator ReturnToMap(float fade)
    {
        // The reward controller also restores time on normal returns. This path owns failed-entry and defeat returns only.
        Time.timeScale = 1;
        var load = SceneManager.LoadSceneAsync(mapScene, LoadSceneMode.Single);
        while (load != null && !load.isDone) yield return null;
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
