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
    [SerializeField] private GameObject confirmationModal;
    [SerializeField] private TMP_Text confirmationText;
    [SerializeField] private Button confirmSacrificeButton;
    [SerializeField] private Button cancelSacrificeButton;
    [SerializeField] private GameObject cardDetailPanel;
    [SerializeField] private Transform cardDetailContainer;
    [SerializeField] private Image cardDetailBackground;
    [SerializeField] private Image cardDetailBorder;
    [SerializeField] private TMP_Text cardDetailNameText;
    [SerializeField] private TMP_Text cardDetailEffectText;
    [SerializeField] private TMP_Text cardDetailWarningText;
    [SerializeField] private MapCardView[] prebuiltCardViews;
    [SerializeField] private MapGraphDefinition routeGraph;
    [SerializeField] private MapTravelView travelView;
    [SerializeField] private MapEncounterRouting encounterRouting;
    private MapEncounterNode selectedNode;
    private MapCardView pendingSacrificeView;
    private MapCardView detailedCardView;
    private MapPlayerStatusPanel playerStatusPanel;
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
    public bool CanInteract => !loading && (confirmationModal == null || !confirmationModal.activeSelf) &&
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
        confirmationModal.SetActive(false);
        cardDetailPanel.SetActive(false);
        confirmSacrificeButton.onClick.AddListener(ConfirmSacrifice);
        cancelSacrificeButton.onClick.AddListener(CancelSacrifice);
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
        if (text != null) text.text = "START NEW RUN";
    }
    public void SetTravelNotice(string message) { travelNotice = message; RefreshHud(); }
    public void CloseForTravel()
    {
        nodeInteraction?.Close();
        loading = true; detailedCardView = null; cardDetailPanel.SetActive(false);
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

        playerStatusPanel = panelParent.GetComponentInChildren<MapPlayerStatusPanel>(true);
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

    public void SetCardDetail(MapCardView cardView, bool visible)
    {
        if (cardView == null || cardDetailPanel == null || (visible && !CanInteract))
        {
            return;
        }

        if (!visible)
        {
            if (detailedCardView == cardView)
            {
                detailedCardView = null;
                cardDetailPanel.SetActive(false);
            }
            return;
        }

        detailedCardView = cardView;
        CardDefinition definition = cardView.Definition;
        if (definition == null || cardDetailContainer == null)
        {
            cardDetailPanel.SetActive(false);
            return;
        }

        if (cardDetailNameText != null)
        {
            cardDetailNameText.text = definition.displayName;
        }
        if (cardDetailEffectText != null)
        {
            cardDetailEffectText.text = definition.overworldEffect.description;
        }
        if (cardDetailWarningText != null)
        {
            cardDetailWarningText.text = $"Sacrificing permanently removes {definition.displayName} from the run deck.";
        }

        ApplyDetailArtwork(definition.backPrefab);

        cardDetailPanel.SetActive(true);
    }

    private void ApplyDetailArtwork(GameObject sourcePrefab)
    {
        if (sourcePrefab == null)
        {
            return;
        }

        Transform sourceBackgroundTransform = sourcePrefab.transform.Find("EffectBackground");
        Transform sourceBorderTransform = sourcePrefab.transform.Find("BorderArtwork");
        Image sourceBackground = sourceBackgroundTransform != null ? sourceBackgroundTransform.GetComponent<Image>() : null;
        Image sourceBorder = sourceBorderTransform != null ? sourceBorderTransform.GetComponent<Image>() : null;

        if (cardDetailBackground != null && sourceBackground != null)
        {
            cardDetailBackground.color = sourceBackground.color;
        }
        if (cardDetailBorder != null && sourceBorder != null)
        {
            cardDetailBorder.sprite = sourceBorder.sprite;
            cardDetailBorder.color = sourceBorder.color;
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
        if (!CanInteract || cardView == null || runSession.SacrificeUsed) return;
        pendingSacrificeView = cardView;
        confirmationText.text = $"Permanently remove {cardView.Definition.displayName} and apply its next-battle effect?";
        confirmationModal.SetActive(true);
    }

    private void ConfirmSacrifice()
    {
        if (pendingSacrificeView != null && runSession.TrySacrificeInstance(pendingSacrificeView.InstanceId)) pendingSacrificeView.SetSacrificed();
        pendingSacrificeView = null;
        confirmationModal.SetActive(false);
        RefreshHud();
    }

    private void CancelSacrifice()
    {
        pendingSacrificeView = null;
        confirmationModal.SetActive(false);
    }

    private void BuildDeckView()
    {
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
                if (card.definition.mapPrefab == null) { Debug.LogError("The run card has no map prefab: " + card.definition.cardId); continue; }
                view = Instantiate(card.definition.mapPrefab, deckContainer).GetComponent<MapCardView>();
                if (view == null) { Debug.LogError("The map prefab has no card view."); continue; }
                var template = views.Find(v => v != null);
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
    }

    private void RefreshHud()
    {
        selectedRouteText.text = selectedNode == null ? "Route: Select Level 1" : $"Route: Level {selectedNode.Level}";
        sacrificeStatusText.text = runSession.SacrificeUsed ? "Sacrifice 1/1" : "Sacrifice 0/1";
        capacityText.text = $"Deck Capacity {runSession.DeckCapacity}/{RunSession.CapacityLimit}";
        if (runSession.Progress != null)
        {
            if (runSession.Progress.Phase == MapProgressPhase.Won) selectedRouteText.text = "Adventure complete";
            else if (runSession.Progress.Phase == MapProgressPhase.Lost) selectedRouteText.text = "Adventure ended";
            else if (!string.IsNullOrEmpty(travelNotice)) selectedRouteText.text = travelNotice;
            else if (selectedNode != null) selectedRouteText.text = "Route: " + selectedNode.NodeId.Replace("level_", "L");
            else selectedRouteText.text = runSession.Progress.CompletedCount == 0 ? "Route: Select Level 1" : "Route: Choose your next destination";
        }
    }

    private void EnterBattle()
    {
        if (travel != null)
        {
            if (runSession.Progress.Phase == MapProgressPhase.Won || runSession.Progress.Phase == MapProgressPhase.Lost)
            {
                RestartRun(); return;
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
        nodeInteraction?.Close();
        pendingSacrificeView = null; detailedCardView = null;
        confirmationModal.SetActive(false); cardDetailPanel.SetActive(false);
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
            (confirmationModal != null && confirmationModal.activeSelf)) return false;
        var phase = runSession.Progress.Phase;
        return phase == MapProgressPhase.OnMap || phase == MapProgressPhase.Won || phase == MapProgressPhase.Lost;
    }

    public bool TryRestartRunForTesting()
    {
        if (!CanResetRunForTesting()) return false;
        RestartRun(); return true;
    }

    public bool TryStartTestEncounter(string nodeId, int? startingHealth, out string reason)
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
        ResetRunPresentation();
        // Use the same selection, ignition, asynchronous loading and readiness protocol as normal play.
        SelectNode(node);
        if (runSession.Progress.CurrentEncounter?.NodeId == nodeId) return true;
        reason = "The test run was created, but departure did not begin. Select the test start node to retry.";
        return false;
    }
#endif
}
