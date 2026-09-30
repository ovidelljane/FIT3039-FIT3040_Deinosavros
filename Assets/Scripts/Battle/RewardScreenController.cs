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
    public bool IsPresented => presented;

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
        PreparePresentation("Victory");
        SetChoiceMessage();
        ClearOffers();
        foreach (var offer in offers)
        {
            var instance = Instantiate(rewardCardPrefab, rewardCardContainer);
            instance.GetComponent<RewardCardView>().Initialize(offer, ChooseCard);
        }
        UpdatePresentationLayout();
        SetSkip("Skip", SkipReward);
    }

    public void PresentDefeat(Action returnToMenu)
    {
        if (presented) return;
        presented = true; rewardPanel.SetActive(true);
        rewardPanel.transform.SetAsLastSibling();
        ClearOffers();
        PreparePresentation("Defeat");
        SetMessage("This adventure has ended");
        SetSkip("Return to Menu", () =>
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
        title.text = "Replace a Card";
        SetMessage(incoming.displayName + " - Capacity " + session.DeckCapacity + "/" + RunSession.CapacityLimit);
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
        SetSkip("Cancel Replacement", CancelReplacement);
    }

    private void CancelReplacement()
    {
        if (replacementRoot != null) Destroy(replacementRoot);
        replacementRoot = null;
        pendingCard = null;
        rewardCardContainer.gameObject.SetActive(true);
        title.text = "Victory";
        SetChoiceMessage();
        SetSkip("Skip", SkipReward);
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
            ShowReturnError("Your reward is saved. Retry returning to the map.");
    }

    public void ShowReturnError(string text)
    {
        rewardCardContainer.gameObject.SetActive(false);
        if (replacementRoot != null) replacementRoot.SetActive(false);
        SetMessage(text);
        SetSkip("Retry Return", () =>
        {
            if (MapTravelCoordinator.Ensure(session).ContinueBattleReward(this, encounterId))
                skipButton.interactable = false;
        });
    }

    private void SetSkip(string title, UnityEngine.Events.UnityAction action)
    {
        skipButton.onClick.RemoveAllListeners();
        skipButton.onClick.AddListener(action);
        skipButton.interactable = true;
        var label = skipButton.GetComponentInChildren<TMP_Text>();
        if (label != null) label.text = title;
    }

    private void SetMessage(string text)
    {
        if (message == null)
        {
            var root = new GameObject("RewardMessage", typeof(RectTransform));
            var rect = (RectTransform)root.transform;
            rect.SetParent(rewardPanel.transform, false);
            rect.anchorMin = new Vector2(.07f, .78f); rect.anchorMax = new Vector2(.93f, .85f);
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            message = root.AddComponent<TextMeshProUGUI>();
            GameFonts.Apply(message, GameFontRole.Body); message.fontSize = 30;
            message.enableAutoSizing = true; message.fontSizeMin = 23; message.fontSizeMax = 30;
            message.alignment = TextAlignmentOptions.Center; message.raycastTarget = false;
            message.color = new Color(.95f, .88f, .7f);
        }
        message.text = text;
    }

    private void SetChoiceMessage() => SetMessage("Choose one card - Deck capacity " + session.DeckCapacity + "/" + RunSession.CapacityLimit);

    private void PreparePresentation(string heading)
    {
        if (!presentationReady)
        {
            presentationReady = true;
            rewardPanel.GetComponent<Image>().color = backgroundColor;
            title = rewardPanel.transform.Find("Title").GetComponent<TMP_Text>();
            Stretch(title.rectTransform, new Vector2(.06f, .85f), new Vector2(.94f, .96f));
            GameFonts.Apply(title, GameFontRole.Heading);
            title.fontSize = 56; title.color = accentColor; title.raycastTarget = false;
            Stretch((RectTransform)rewardCardContainer, new Vector2(.04f, .18f), new Vector2(.96f, .77f));
            var buttonRect = skipButton.GetComponent<RectTransform>();
            buttonRect.anchorMin = buttonRect.anchorMax = new Vector2(.5f, .08f);
            buttonRect.anchoredPosition = Vector2.zero; buttonRect.sizeDelta = new Vector2(380, 72);
            skipButton.GetComponent<Image>().color = cardColor;
            var border = skipButton.gameObject.AddComponent<Outline>();
            border.effectColor = accentColor; border.effectDistance = new Vector2(1.5f, -1.5f);
            var label = skipButton.GetComponentInChildren<TMP_Text>();
            GameFonts.Apply(label); label.fontSize = 25; label.color = accentColor;
            label.enableAutoSizing = true; label.fontSizeMin = 19; label.fontSizeMax = 25;
        }
        title.text = heading;
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
