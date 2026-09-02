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
    private bool loading;

    private void Start()
    {
        if (RunSession.Instance != null)
        {
            runSession = RunSession.Instance;
        }
        BuildDeckView();
        enterBattleButton.interactable = false;
        confirmationModal.SetActive(false);
        cardDetailPanel.SetActive(false);
        confirmSacrificeButton.onClick.AddListener(ConfirmSacrifice);
        cancelSacrificeButton.onClick.AddListener(CancelSacrifice);
        enterBattleButton.onClick.AddListener(EnterBattle);
        RefreshHud();
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
        foreach (MapCardView cardView in prebuiltCardViews)
        {
            if (cardView == null || cardView.Definition == null) continue;
            RunCardInstance runCard = null;
            foreach (RunCardInstance candidate in runSession.RunDeck)
            {
                if (candidate.definition == cardView.Definition)
                {
                    runCard = candidate;
                    break;
                }
            }
            bool active = runCard != null && !runCard.sacrificed;
            cardView.gameObject.SetActive(active);
            if (active) cardView.Initialize(this, cardView.Definition);
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
