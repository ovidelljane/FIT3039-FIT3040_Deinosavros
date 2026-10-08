using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using System.Collections.Generic;
using Deinosavros.MapReview;

public sealed class MapController : MonoBehaviour
{
    [SerializeField] private string battleSceneName = "Deinosavros";
    [SerializeField] private RunSession runSession;
    [SerializeField] private MapEncounterNode[] nodes;
    [SerializeField] private Transform deckContainer;
    [SerializeField] private Button enterBattleButton;
    [SerializeField] private TMP_Text selectedRouteText;
    [SerializeField] private TMP_Text sacrificeStatusText;
    [SerializeField] private TMP_Text capacityText;
    [SerializeField] private MapSacrificePanel sacrificePanel;
    public MapSacrificePanel SacrificePanel => sacrificePanel;
    [SerializeField, Min(0)] private int sacrificeReminderCapacity = 25;
    private string warnedNodeId;
    private int inputReleaseFrame = -1;
    [SerializeField] private MapCardView[] prebuiltCardViews;
    [SerializeField] private MapGraphDefinition routeGraph;
    [SerializeField] private MapTravelView travelView;
    [SerializeField] private MapEncounterRouting encounterRouting;
    private MapEncounterNode selectedNode;
    [SerializeField] private MapPlayerStatusPanel playerStatusPanel;
    private bool loading;
    private bool refreshPending;
    private bool deckRefreshPending;
    private string travelNotice;
    private MapTravelCoordinator travel;
    private MapNodeInteraction nodeInteraction;
    private PointerEventData pointer;
    private EventSystem pointerSystem;
    private readonly List<RaycastResult> uiHits = new(32);
    private int pointerFrame = -1;
    private Vector2 pointerPosition;
    private bool pointerBlocked;
    public string SelectedNodeId => selectedNode != null ? selectedNode.NodeId : null;
    public RunSession Session => runSession;
    public MapTravelView TravelView => travelView;
    public Button EnterButton => enterBattleButton;
    public MapEncounterNode[] Nodes => nodes;
    public bool UsesTravelInteraction => travelView != null && routeGraph != null;
    public MapNodeInteraction NodeInteraction => nodeInteraction;
    public MapEncounterRouting EncounterRouting => encounterRouting;
    public bool CanInteract => !loading && Time.frameCount > inputReleaseFrame && (sacrificePanel == null || !sacrificePanel.IsBlocking) &&
        (runSession?.Progress == null || runSession.Progress.Phase == MapProgressPhase.OnMap) && (travel == null || !travel.IsBusy);

    private void Start()
    {
        if (RunSession.Instance != null)
        {
            runSession = RunSession.Instance;
        }
        if (routeGraph != null && travelView != null)
        {
            runSession.InitializeProgress(routeGraph, encounterRouting != null ? encounterRouting.nodes : travelView.profile.nodeInformation);
            travel = MapTravelCoordinator.Ensure(runSession);
            runSession.Progress.Changed += QueueRefresh;
            travelView.Bind(runSession);
            foreach (var node in nodes) if (node != null) node.Bind(this);
            travelNotice = travel.LastNotice;
        }
        CreatePlayerStatusPanel();
        runSession.DeckChanged += QueueDeckRefresh;
        BuildDeckView();
        enterBattleButton.interactable = false;
        sacrificePanel?.Bind(this);
        enterBattleButton.onClick.AddListener(EnterBattle);
        RefreshHud();
        RefreshProgress();
        if (UsesTravelInteraction)
        {
            nodeInteraction = gameObject.AddComponent<MapNodeInteraction>();
            nodeInteraction.Initialize(this, travelView);
        }
    }
    private void QueueRefresh() => refreshPending = true;
    private void QueueDeckRefresh() => deckRefreshPending = true;
    private void Update()
    {
        if (deckRefreshPending) { deckRefreshPending = false; BuildDeckView(); RefreshHud(); }
        if (refreshPending) { refreshPending = false; RefreshProgress(); RefreshHud(); }
        if (runSession?.Progress != null && loading && runSession.Progress.Phase == MapProgressPhase.OnMap)
        {
            loading = false;
            foreach (var card in prebuiltCardViews) if (card != null) card.SetInteractionLocked(false);
            RefreshProgress(); RefreshHud();
        }
    }
    private void OnDestroy()
    {
        if (runSession?.Progress != null) runSession.Progress.Changed -= QueueRefresh;
        if (runSession != null) runSession.DeckChanged -= QueueDeckRefresh;
    }
    public bool BlocksMapPointer(Vector2 position)
    {
        if (!CanInteract) return true;
        if (pointerFrame == Time.frameCount && position == pointerPosition) return pointerBlocked;
        pointerFrame = Time.frameCount; pointerPosition = position; pointerBlocked = false;
        if (EventSystem.current == null) return false;
        if (pointer == null || pointerSystem != EventSystem.current)
        { pointerSystem = EventSystem.current; pointer = new PointerEventData(pointerSystem); }
        pointer.position = position; uiHits.Clear(); EventSystem.current.RaycastAll(pointer, uiHits);
        foreach (var hit in uiHits) if (hit.module is GraphicRaycaster) { pointerBlocked = true; break; }
        return pointerBlocked;
    }
    private void RefreshProgress()
    {
        if (runSession?.Progress == null || enterBattleButton == null) return;
        foreach (var node in nodes) if (node != null) node.SetAvailable(runSession.Progress.NodeState(node.NodeId) == MapLocationState.Available);
        bool ended = runSession.Progress.Phase == MapProgressPhase.Won || runSession.Progress.Phase == MapProgressPhase.Lost;
        enterBattleButton.gameObject.SetActive(ended);
        enterBattleButton.interactable = ended;
        var text = enterBattleButton.GetComponentInChildren<TMP_Text>();
        if (text != null) text.text = "RETURN TO MENU";
    }
    public void SetTravelNotice(string message) { travelNotice = message; RefreshHud(); }
    public void CloseForTravel()
    {
        warnedNodeId = null;
        nodeInteraction?.Close();
        loading = true; sacrificePanel?.HideImmediate();
        foreach (var card in prebuiltCardViews) if (card != null) card.SetInteractionLocked(true);
        enterBattleButton.interactable = false;
    }
    public void ConfigureTravel(MapGraphDefinition graph, MapTravelView view, MapEncounterNode[] locations)
    { routeGraph = graph; travelView = view; nodes = locations; }

    private void CreatePlayerStatusPanel()
    {
        if (deckContainer == null || runSession == null)
        {
            return;
        }

        RectTransform deckRect = deckContainer as RectTransform;
        Transform panelParent = deckContainer.parent;
        if (deckRect == null || panelParent == null)
        {
            return;
        }

        if (playerStatusPanel == null) playerStatusPanel = panelParent.GetComponentInChildren<MapPlayerStatusPanel>(true);
        if (playerStatusPanel == null)
        {
            playerStatusPanel = MapPlayerStatusPanel.Create(panelParent);
        }
        playerStatusPanel.Initialize(runSession);

        Vector2 offsetMin = deckRect.offsetMin;
        offsetMin.x = Mathf.Max(offsetMin.x, 245f);
        deckRect.offsetMin = offsetMin;

        HorizontalLayoutGroup deckLayout = deckContainer.GetComponent<HorizontalLayoutGroup>();
        if (deckLayout != null)
        {
            deckLayout.childAlignment = TextAnchor.MiddleLeft;
        }
    }

    public void SelectNode(MapEncounterNode node)
    {
        if (!CanInteract || node == null || !node.IsAvailable) return;
        if (travelView != null && runSession.Progress != null && !travelView.HasPath(runSession.Progress.CurrentNodeId, node.NodeId)) return;
        selectedNode = node;
        runSession.SelectNode(node.NodeId);
        travelNotice = null;
        if (travelView != null) travelView.Preview(node.NodeId);
        foreach (MapEncounterNode mapNode in nodes) mapNode.SetSelected(mapNode == selectedNode);
        enterBattleButton.interactable = travel == null;
        RefreshHud();
        if (travel != null) EnterBattle();
    }

    public void RequestSacrifice(MapCardView cardView)
    {
        if (!CanInteract || cardView == null || sacrificePanel == null) return;
        nodeInteraction?.Close();
        foreach (var card in prebuiltCardViews) if (card != null) card.SetInteractionLocked(true);
        sacrificePanel.Open(cardView);
    }

    public bool TryCommitSacrifice(CardDefinition definition, string instanceId, out string reason)
    {
        var preview = MapSacrificePreview.For(runSession, definition, instanceId);
        reason = preview.Reason;
        if (sacrificePanel == null || !sacrificePanel.IsOpen || sacrificePanel.SelectedInstanceId != instanceId ||
            !preview.CanConfirm) return false;
        if (runSession.TrySacrificeInstance(instanceId)) return true;
        reason = "The offering could not be applied. Inspect this card again.";
        return false;
    }

    public void ReleaseSacrificeInput()
    {
        inputReleaseFrame = Time.frameCount + 1;
        pointerFrame = -1;
        foreach (var card in prebuiltCardViews) if (card != null) card.SetInteractionLocked(loading);
        EventSystem.current?.SetSelectedGameObject(null);
        RefreshHud();
    }

    private void BuildDeckView()
    {
        var previous = new Dictionary<string, Vector3>();
        if (sacrificePanel != null && sacrificePanel.IsBusy)
            foreach (var view in prebuiltCardViews)
                if (view != null && view.gameObject.activeInHierarchy && !string.IsNullOrEmpty(view.InstanceId))
                    previous[view.InstanceId] = view.transform.position;
        var views = new List<MapCardView>(prebuiltCardViews);
        var used = new HashSet<MapCardView>();
        int order = 0;
        foreach (var card in runSession.RunDeck)
        {
            if (card.sacrificed || card.definition == null) continue;
            MapCardView view = views.Find(v => v != null && !used.Contains(v) && v.InstanceId == card.instanceId);
            if (view == null) view = views.Find(v => v != null && !used.Contains(v) && v.Definition == card.definition);
            if (view == null)
            {
                var template = views.Find(v => v != null);
                if (card.definition.mapPrefab != null)
                    view = Instantiate(card.definition.mapPrefab, deckContainer).GetComponent<MapCardView>();
                else if (template != null)
                    view = Instantiate(template, deckContainer);
                if (view == null) { Debug.LogError("The map prefab has no card view."); continue; }
                if (template != null)
                {
                    var source = (RectTransform)template.transform; var target = (RectTransform)view.transform;
                    target.anchorMin = source.anchorMin; target.anchorMax = source.anchorMax;
                    target.pivot = source.pivot; target.sizeDelta = source.sizeDelta; target.localScale = source.localScale;
                }
                views.Add(view);
            }
            used.Add(view); view.gameObject.SetActive(true); view.transform.SetSiblingIndex(order++);
            view.Initialize(this, card.definition, card.instanceId);
        }
        foreach (var view in views) if (view != null && !used.Contains(view)) view.gameObject.SetActive(false);
        prebuiltCardViews = views.ToArray();
        if (sacrificePanel != null && sacrificePanel.IsBlocking)
            foreach (var view in prebuiltCardViews) if (view != null) view.SetInteractionLocked(true);
        if (previous.Count > 0)
        {
            Canvas.ForceUpdateCanvases();
            LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)deckContainer);
            foreach (var view in used)
                if (previous.TryGetValue(view.InstanceId, out var position)) view.SettleFrom(position, sacrificePanel.deckSettleDuration);
        }
    }

    private void RefreshHud()
    {
        selectedRouteText.text = selectedNode == null ? "Route: Select Level 1" : $"Route: Level {selectedNode.Level}";
        if (sacrificePanel != null) sacrificePanel.RefreshStatus();
        capacityText.text = $"Deck Capacity {runSession.DeckCapacity}/{runSession.DeckCapacityLimit}";
        if (runSession.Progress != null)
        {
            if (runSession.Progress.Phase == MapProgressPhase.Won) selectedRouteText.text = "Adventure complete";
            else if (runSession.Progress.Phase == MapProgressPhase.Lost) selectedRouteText.text = "Adventure ended";
            else if (!string.IsNullOrEmpty(travelNotice)) selectedRouteText.text = travelNotice;
            else if (selectedNode != null) selectedRouteText.text = "Route: " + selectedNode.NodeId.Replace("level_", "L");
            else selectedRouteText.text = runSession.Progress.CompletedCount == 0 ? "Route: Select Level 1" : "Route: Choose your next destination";
        }
    }

    public bool NeedsSacrificeReminder(MapEncounterKind kind) =>
        (kind == MapEncounterKind.Battle || kind == MapEncounterKind.Boss) &&
        runSession != null && runSession.DeckCapacity > sacrificeReminderCapacity &&
        !runSession.SacrificeUsed && !runSession.HasPendingModifier;

    public void CancelDepartureWarning()
    {
        warnedNodeId = null; selectedNode = null; runSession.SelectNode(null);
        foreach (var node in nodes) if (node != null) node.SetSelected(false);
        travelView?.Preview(null); ReleaseSacrificeInput();
    }

    public void ConfirmDepartureWithoutSacrifice(string nodeId)
    {
        if (string.IsNullOrEmpty(nodeId) || warnedNodeId != nodeId || selectedNode == null ||
            selectedNode.NodeId != nodeId || !selectedNode.IsAvailable || !CanInteract)
        { CancelDepartureWarning(); return; }
        warnedNodeId = null;
        EnterBattle(true);
        if (!loading && (travel == null || !travel.IsBusy)) ReleaseSacrificeInput();
    }

    private void EnterBattle() => EnterBattle(false);

    private void EnterBattle(bool confirmedWithoutSacrifice)
    {
        if (CanInteract && selectedNode != null && !confirmedWithoutSacrifice && sacrificePanel != null &&
            NeedsSacrificeReminder(runSession.Progress != null ? runSession.Progress.KindOf(selectedNode.NodeId) : MapEncounterKind.Battle))
        {
            warnedNodeId = selectedNode.NodeId; nodeInteraction?.Close();
            foreach (var card in prebuiltCardViews) if (card != null) card.SetInteractionLocked(true);
            sacrificePanel.ShowDepartureWarning(warnedNodeId); return;
        }
        if (travel != null)
        {
            if (runSession.Progress.Phase == MapProgressPhase.Won || runSession.Progress.Phase == MapProgressPhase.Lost)
            {
                travel.ReturnToMainMenu(); return;
            }
            if (CanInteract && selectedNode != null) travel.Begin(this, travelView,
                encounterRouting != null ? encounterRouting.SceneFor(runSession.Progress.KindOf(selectedNode.NodeId)) : battleSceneName);
            return;
        }
        if (loading || selectedNode == null || !Application.CanStreamedLevelBeLoaded(battleSceneName)) return;
        loading = true;
        enterBattleButton.interactable = false;
        enabled = false;
        Destroy(this);
        SceneManager.LoadSceneAsync(battleSceneName, LoadSceneMode.Single);
    }

    private void RestartRun()
    {
        runSession.BeginNewRun(); ResetRunPresentation();
    }

    private void ResetRunPresentation()
    {
        warnedNodeId = null;
        nodeInteraction?.Close();
        sacrificePanel?.HideImmediate();
        selectedNode = null; loading = false; travelNotice = null;
        foreach (var node in nodes) if (node != null) node.SetSelected(false);
        BuildDeckView(); travelView.RestorePosition(runSession.Progress.CurrentNodeId);
        RefreshProgress(); RefreshHud();
    }

#if UNITY_EDITOR
    private bool CanResetRunForTesting()
    {
        if (!Application.isPlaying || !isActiveAndEnabled || runSession != RunSession.Instance ||
            runSession?.Progress == null || travel == null || travel.IsBusy || travelView == null ||
            routeGraph == null || loading || UnityEditor.EditorApplication.isPaused ||
            travelView.gameObject.scene != SceneManager.GetActiveScene() ||
            (sacrificePanel != null && sacrificePanel.IsBlocking)) return false;
        var phase = runSession.Progress.Phase;
        return phase == MapProgressPhase.OnMap || phase == MapProgressPhase.Won || phase == MapProgressPhase.Lost;
    }

    public bool TryRestartRunForTesting()
    {
        if (!CanResetRunForTesting()) return false;
        RestartRun(); return true;
    }

    public bool TryStartTestEncounter(string nodeId, int? startingHealth, out string reason,
        MapOpportunityOutcome? opportunityOutcome = null)
    {
        reason = null;
        if (!CanResetRunForTesting())
        { reason = "Resume Play Mode on an idle Map before starting a test encounter."; return false; }
        var node = nodes == null ? null : System.Array.Find(nodes, n => n != null && n.NodeId == nodeId);
        var definition = string.IsNullOrEmpty(nodeId) ? null : routeGraph.Find(nodeId);
        if (node == null || definition == null)
        { reason = "The start node is missing from the map or route graph."; return false; }
        // Legacy marker parents may be hidden; the bound node and authored travel site remain authoritative.
        if (encounterRouting == null || encounterRouting.nodes == null || encounterRouting.nodes.Find(nodeId) == null)
        { reason = "The start node has no encounter routing: " + nodeId; return false; }
        if (travelView.environment == null || travelView.profile == null || travelView.sites == null ||
            !System.Array.Exists(travelView.sites, s => s != null && s.nodeId == nodeId))
        { reason = "The start node has no valid travel site: " + nodeId; return false; }
        string destination = encounterRouting.SceneFor(encounterRouting.nodes.Find(nodeId).kind);
        if (string.IsNullOrEmpty(destination) || !Application.CanStreamedLevelBeLoaded(destination))
        { reason = "The encounter scene is unavailable. The current adventure was not reset."; return false; }
        if (!runSession.TryBeginTestRunAt(nodeId, startingHealth, out reason)) return false;
        runSession.SetNextOpportunityForTesting(opportunityOutcome);
        ResetRunPresentation();
        // Use the same selection, ignition, asynchronous loading and readiness protocol as normal play.
        SelectNode(node);
        if (runSession.Progress.CurrentEncounter?.NodeId == nodeId) return true;
        reason = "The test run was created, but departure did not begin. Select the test start node to retry.";
        return false;
    }
#endif
}
