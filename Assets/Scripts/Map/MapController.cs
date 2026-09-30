using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

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
    private MapEncounterNode selectedNode;
    private MapCardView pendingSacrificeView;
    private MapCardView detailedCardView;
    private MapPlayerStatusPanel playerStatusPanel;
    private bool loading;

    private void Start()
    {
        if (RunSession.Instance != null)
        {
            runSession = RunSession.Instance;
        }
        CreatePlayerStatusPanel();
        BuildDeckView();
        enterBattleButton.interactable = false;
        confirmationModal.SetActive(false);
        cardDetailPanel.SetActive(false);
        confirmSacrificeButton.onClick.AddListener(ConfirmSacrifice);
        cancelSacrificeButton.onClick.AddListener(CancelSacrifice);
        enterBattleButton.onClick.AddListener(EnterBattle);
        RefreshHud();
    }

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
        if (cardView == null || cardDetailPanel == null)
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
        if (node == null || !node.IsAvailable) return;
        selectedNode = node;
        runSession.SelectNode(node.NodeId);
        foreach (MapEncounterNode mapNode in nodes) mapNode.SetSelected(mapNode == selectedNode);
        enterBattleButton.interactable = true;
        RefreshHud();
    }

    public void RequestSacrifice(MapCardView cardView)
    {
        if (cardView == null || runSession.SacrificeUsed) return;
        pendingSacrificeView = cardView;
        confirmationText.text = $"Permanently remove {cardView.Definition.displayName} and apply its next-battle effect?";
        confirmationModal.SetActive(true);
    }

    private void ConfirmSacrifice()
    {
        if (pendingSacrificeView != null && runSession.TrySacrifice(pendingSacrificeView.Definition)) pendingSacrificeView.SetSacrificed();
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
        var unusedViews = new List<MapCardView>();
        foreach (MapCardView cardView in prebuiltCardViews)
        {
            if (cardView == null) continue;
            cardView.gameObject.SetActive(false);
            unusedViews.Add(cardView);
        }
        MapCardView template = unusedViews.Count > 0 ? unusedViews[0] : null;

        // Reuse a prebuilt view for each card when one exists; clone the template for cards won in battle.
        foreach (CardDefinition definition in runSession.GetActiveDeck())
        {
            MapCardView cardView = unusedViews.Find(candidate => candidate.Definition == definition);
            if (cardView != null)
            {
                unusedViews.Remove(cardView);
            }
            else
            {
                if (template == null) continue;
                cardView = Instantiate(template, deckContainer);
            }
            cardView.gameObject.SetActive(true);
            cardView.Initialize(this, definition);
        }
    }

    private void RefreshHud()
    {
        selectedRouteText.text = selectedNode == null ? "Route: Select Level 1" : $"Route: Level {selectedNode.Level}";
        sacrificeStatusText.text = runSession.SacrificeUsed ? "Sacrifice 1/1" : "Sacrifice 0/1";
        int capacity = 0;
        foreach (RunCardInstance card in runSession.RunDeck)
        {
            if (!card.sacrificed && card.definition != null) capacity += card.definition.capacityCost;
        }
        capacityText.text = $"Deck Capacity {capacity}/20";
    }

    private void EnterBattle()
    {
        if (loading || selectedNode == null || !Application.CanStreamedLevelBeLoaded(battleSceneName)) return;
        loading = true;
        enterBattleButton.interactable = false;
        enabled = false;
        Destroy(this);
        SceneManager.LoadSceneAsync(battleSceneName, LoadSceneMode.Single);
    }
}
