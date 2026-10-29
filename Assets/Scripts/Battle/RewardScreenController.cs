using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Presents rewards; RunSession owns claims and the coordinator owns completion and travel.
public sealed class RewardScreenController : MonoBehaviour
{
    [SerializeField] private GameObject rewardPanel;
    [SerializeField] private Transform rewardCardContainer;
    [SerializeField] private GameObject rewardCardPrefab;
    [SerializeField] private CardPool cardPool;
    [SerializeField] private Button skipButton;
    [SerializeField, Min(1)] private int rewardOptionCount = 3;
    [Header("Scene Text")]
    [SerializeField] private ResultTextView victory;
    [SerializeField] private ResultTextView defeat;
    [SerializeField] private ResultTextView replacement;
    [SerializeField] private TMP_Text returnError;
    [Header("Button Labels")]
    [SerializeField] private TMP_Text victoryButtonLabel, defeatButtonLabel, replacementButtonLabel, retryButtonLabel;
    [Header("Presentation")]
    [SerializeField] private Color backgroundColor = new Color(.31f, .28f, .25f, 1);
    [SerializeField] private Color cardColor = new Color(.205f, .18f, .155f, 1);
    [SerializeField] private Color accentColor = new Color(.89f, .73f, .47f, 1);
    [SerializeField] private Vector2 offerCardSize = new Vector2(360, 600);
    [SerializeField, Min(0)] private float cardSpacing = 56;
    [SerializeField] private Vector2 replacementCardSize = new Vector2(280, 438);
    [SerializeField, Min(1)] private int replacementColumns = 4;
    private RunSession session;
    private string encounterId;
    private TMP_Text message;
    private GameObject replacementRoot;
    private CardDefinition pendingCard;
    private bool presented;
    private bool presentationReady;
    private Vector2 previousPanelSize;
    private TMP_Text title;
    private string returnErrorTemplate;
    public bool IsPresented => presented;
    public Transform PresentationRoot => rewardPanel != null ? rewardPanel.transform : null;

    [Serializable]
    public sealed class ResultTextView
    {
        public GameObject root;
        public TMP_Text title;
        [Tooltip("Optional tokens: {capacity}, {limit}, {card}.")]
        public TMP_Text message;
        private string template;

        public string FormatMessage(int capacity, int limit, string card = "")
        {
            template ??= message.text;
            return template.Replace("{capacity}",capacity.ToString()).Replace("{limit}",limit.ToString()).Replace("{card}",card);
        }
    }

    private void Start()
    {
        rewardPanel.SetActive(false);
        skipButton.onClick.AddListener(SkipReward);
    }

    public void PresentVictory(RunSession owner, string id)
    {
        if (presented || owner == null || !owner.TryPrepareBattleReward(id, cardPool, rewardOptionCount, out var offers)) return;
        session = owner; encounterId = id; presented = true;
        rewardPanel.SetActive(true);
        rewardPanel.transform.SetAsLastSibling();
        PreparePresentation(victory);
        SetChoiceMessage();
        ClearOffers();
        foreach (var offer in offers)
        {
            var instance = Instantiate(rewardCardPrefab, rewardCardContainer);
            instance.GetComponent<RewardCardView>().Initialize(offer, ChooseCard);
        }
        UpdatePresentationLayout();
        SetSkip(victoryButtonLabel, SkipReward);
    }

    public void PresentDefeat(Action returnToMenu)
    {
        if (presented) return;
        presented = true; rewardPanel.SetActive(true);
        rewardPanel.transform.SetAsLastSibling();
        ClearOffers();
        PreparePresentation(defeat);
        SetSkip(defeatButtonLabel, () =>
        {
            skipButton.interactable = false;
            returnToMenu?.Invoke();
        });
    }

    private void ChooseCard(CardDefinition definition)
    {
        if (session == null || !session.HasBattleReward) return;
        if (session.CanAddCard(definition)) TryClaim(definition, null);
        else ShowReplacement(definition);
    }

    private void ShowReplacement(CardDefinition incoming)
    {
        pendingCard = incoming;
        rewardCardContainer.gameObject.SetActive(false);
        PreparePresentation(replacement);
        SetMessage(replacement.FormatMessage(session.DeckCapacity,RunSession.CapacityLimit,incoming.displayName));
        if (replacementRoot != null) Destroy(replacementRoot);
        replacementRoot = new GameObject("RewardReplacement", typeof(RectTransform), typeof(Image), typeof(RectMask2D), typeof(ScrollRect));
        var viewport = (RectTransform)replacementRoot.transform;
        viewport.SetParent(rewardPanel.transform, false);
        Stretch(viewport, new Vector2(.07f, .18f), new Vector2(.93f, .77f));
        replacementRoot.GetComponent<Image>().color = new Color(0, 0, 0, .08f);
        var content = new GameObject("Cards", typeof(RectTransform), typeof(GridLayoutGroup), typeof(ContentSizeFitter));
        var rect = (RectTransform)content.transform; rect.SetParent(viewport, false);
        rect.anchorMin = new Vector2(0, 1); rect.anchorMax = Vector2.one;
        rect.pivot = new Vector2(.5f, 1); rect.sizeDelta = Vector2.zero;
        var grid = content.GetComponent<GridLayoutGroup>();
        grid.spacing = new Vector2(24, 24); grid.padding = new RectOffset(12, 12, 12, 12);
        grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        grid.childAlignment = TextAnchor.UpperCenter;
        content.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        var scroll = replacementRoot.GetComponent<ScrollRect>(); scroll.viewport = viewport; scroll.content = rect;
        scroll.horizontal = false; scroll.movementType = ScrollRect.MovementType.Clamped; scroll.scrollSensitivity = 45;
        foreach (var card in session.RunDeck)
        {
            if (card.sacrificed || card.definition == null) continue;
            string instanceId = card.instanceId;
            var view = Instantiate(rewardCardPrefab, rect).GetComponent<RewardCardView>();
            view.Initialize(card.definition, _ => TryClaim(pendingCard, instanceId));
        }
        UpdatePresentationLayout();
        SetSkip(replacementButtonLabel, CancelReplacement);
    }

    private void CancelReplacement()
    {
        if (replacementRoot != null) Destroy(replacementRoot);
        replacementRoot = null;
        pendingCard = null;
        rewardCardContainer.gameObject.SetActive(true);
        PreparePresentation(victory);
        SetChoiceMessage();
        SetSkip(victoryButtonLabel, SkipReward);
    }

    private void TryClaim(CardDefinition incoming, string outgoingId)
    {
        if (!session.TryResolveBattleReward(encounterId, incoming, outgoingId, out var reason))
        { SetMessage(reason); return; }
        FinishReward();
    }

    private void SkipReward()
    {
        if (session == null || !session.TryResolveBattleReward(encounterId, null, null, out _)) return;
        FinishReward();
    }

    private void FinishReward()
    {
        foreach (var button in rewardPanel.GetComponentsInChildren<Button>()) button.interactable = false;
        if (!MapTravelCoordinator.Ensure(session).ContinueBattleReward(this, encounterId))
            ShowReturnError(null);
    }

    public void ShowReturnError(string text)
    {
        rewardCardContainer.gameObject.SetActive(false);
        if (replacementRoot != null) replacementRoot.SetActive(false);
        victory.message.gameObject.SetActive(false);
        defeat.message.gameObject.SetActive(false);
        replacement.message.gameObject.SetActive(false);
        returnError.gameObject.SetActive(true);
        message = returnError;
        SetMessage(string.IsNullOrEmpty(text) ? returnErrorTemplate : text);
        SetSkip(retryButtonLabel, () =>
        {
            if (MapTravelCoordinator.Ensure(session).ContinueBattleReward(this, encounterId))
                skipButton.interactable = false;
        });
    }

    private void SetSkip(TMP_Text label, UnityEngine.Events.UnityAction action)
    {
        skipButton.onClick.RemoveAllListeners();
        skipButton.onClick.AddListener(action);
        skipButton.interactable = true;
        foreach (var candidate in new[] { victoryButtonLabel, defeatButtonLabel, replacementButtonLabel, retryButtonLabel })
            candidate.gameObject.SetActive(candidate == label);
    }

    private void SetMessage(string text)
    {
        message.text = text;
    }

    private void SetChoiceMessage() => SetMessage(victory.FormatMessage(session != null ? session.DeckCapacity : 0,RunSession.CapacityLimit));

    private void PreparePresentation(ResultTextView content)
    {
        if (!presentationReady)
        {
            presentationReady = true;
            rewardPanel.GetComponent<Image>().color = backgroundColor;
            returnErrorTemplate = returnError.text;
        }
        victory.root.SetActive(content == victory);
        defeat.root.SetActive(content == defeat);
        replacement.root.SetActive(content == replacement);
        returnError.gameObject.SetActive(false);
        title = content.title;
        message = content.message;
        message.gameObject.SetActive(true);
        SetMessage(content.FormatMessage(session != null ? session.DeckCapacity : 0,RunSession.CapacityLimit));
    }

    private void LateUpdate()
    {
        if (!presented || !rewardPanel.activeInHierarchy) return;
        if (((RectTransform)rewardPanel.transform).rect.size != previousPanelSize) UpdatePresentationLayout();
    }

    private void UpdatePresentationLayout()
    {
        Canvas.ForceUpdateCanvases();
        previousPanelSize = ((RectTransform)rewardPanel.transform).rect.size;
        var rect = (RectTransform)rewardCardContainer;
        var cards = rewardCardContainer.GetComponentsInChildren<RewardCardView>(true);
        float preferredWidth = Mathf.Max(1, cards.Length) * offerCardSize.x + Mathf.Max(0, cards.Length - 1) * cardSpacing;
        float scale = Mathf.Min(1, rect.rect.width / Mathf.Max(1, preferredWidth), rect.rect.height / Mathf.Max(1, offerCardSize.y));
        rewardCardContainer.GetComponent<HorizontalLayoutGroup>().spacing = cardSpacing * scale;
        foreach (var card in cards) card.SetPresentation(offerCardSize * scale, cardColor, accentColor);
        LayoutRebuilder.ForceRebuildLayoutImmediate(rect);
        if (replacementRoot == null) return;
        var grid = replacementRoot.GetComponentInChildren<GridLayoutGroup>(true);
        grid.constraintCount = Mathf.Max(1, replacementColumns);
        float availableWidth = ((RectTransform)replacementRoot.transform).rect.width - grid.padding.horizontal;
        float width = Mathf.Min(replacementCardSize.x,
            (availableWidth - grid.spacing.x * (grid.constraintCount - 1)) / grid.constraintCount);
        grid.cellSize = replacementCardSize * Mathf.Max(.1f, width / Mathf.Max(1, replacementCardSize.x));
        foreach (var card in grid.GetComponentsInChildren<RewardCardView>(true)) card.SetPresentation(grid.cellSize, cardColor, accentColor);
        LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)grid.transform);
    }

    private static void Stretch(RectTransform rect, Vector2 min, Vector2 max)
    {
        rect.anchorMin = min; rect.anchorMax = max; rect.offsetMin = rect.offsetMax = Vector2.zero;
    }

    private void ClearOffers()
    {
        foreach (Transform child in rewardCardContainer) Destroy(child.gameObject);
    }
}
