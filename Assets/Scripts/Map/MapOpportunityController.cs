using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public sealed class MapOpportunityController : MapNonCombatController
{
    public MapOpportunityProfile profile;
    public GameObject rewardCardPrefab;
    public bool IsRevealed { get; private set; }
    public bool IsSpinning { get; private set; }
    public bool IsReplacing => replacementRoot != null && replacementRoot.activeSelf;
    [System.Serializable]
    private sealed class ContentView
    {
        public GameObject root;
        public TMP_Text value;
        public TMP_Text body;
        public bool IsValid => root != null && value != null && body != null;
    }

    [Header("Opportunity Interface")]
    [SerializeField] private ContentView cardContent, recoveryContent, battleContent;
    [SerializeField] private Button secondaryButton;
    [SerializeField] private TMP_Text secondaryText, recoveryDetail;
    [SerializeField] private RawImage icon;
    [SerializeField] private RectTransform revealPose;
    [SerializeField] private GameObject rewardRoot, replacementRoot;
    [SerializeField] private RectTransform replacementContent;
    [SerializeField] private RewardCardView replacementTemplate;
    [SerializeField] private CanvasGroup controls;
    public Button SecondaryButton => secondaryButton;
    private readonly List<RewardCardView> replacementCards = new List<RewardCardView>();
    private Vector2 resultPosition, resultSize;
    private Quaternion resultRotation;
    private TMP_Text revealValue, revealBody;
    private CardDefinition displayedOffer;
    private bool returning;
    private string battleError;

    public override bool TryPrepare(RunSession owner, MapTravelTicket ticket) =>
        base.TryPrepare(owner, ticket) && rewardCardPrefab != null &&
        owner.TryPrepareOpportunityEvent(ticket.EncounterId, profile, routing.cardPool);

    public override void ActivateEncounter()
    {
        if (IsActivated || Receipt == null || session.Progress.Phase != MapProgressPhase.InEncounter ||
            session.Progress.CurrentEncounter?.EncounterId != EncounterId) return;
        IsActivated = true;
        if (Receipt.Outcome == MapOpportunityOutcome.Battle)
            battleError = MapTravelCoordinator.Ensure(session).LastNotice;
        StartCoroutine(Reveal());
    }

    private IEnumerator Reveal()
    {
        SetContent(null);
        // Do not leak the selected outcome or offer actions underneath the reveal.
        value.gameObject.SetActive(false); body.gameObject.SetActive(false); detail.gameObject.SetActive(false);
        capacity.gameObject.SetActive(false); ContinueButton.gameObject.SetActive(false);
        bool shouldSpin = !Receipt.PresentationRevealed && !Receipt.Resolved && !Receipt.Completed;
        if (shouldSpin)
        {
            icon.rectTransform.anchoredPosition = revealPose.anchoredPosition;
            icon.rectTransform.sizeDelta = revealPose.sizeDelta;
        }
        // Let the arrival curtain clear before starting the visible reveal.
        var coordinator = MapTravelCoordinator.Ensure(session);
        while (coordinator.IsBusy) yield return null;
        if (shouldSpin)
        {
            IsSpinning = true;
            int turns = Mathf.Clamp(profile != null ? profile.revealTurns : 7, 3, 12);
            float duration = Mathf.Max(1f, profile != null ? profile.revealDuration : 3.5f);
            // Presentation randomness is independent of the already committed event roll.
            var random = new System.Random();
            int shown = random.Next(3);
            SetIcon((MapOpportunityOutcome)shown);
            int previousTurn = 0;
            for (float elapsed = 0; elapsed < duration; elapsed += Time.unscaledDeltaTime)
            {
                float time = Mathf.Clamp01(elapsed / duration);
                float rotation = turns * (1f - (1f - time) * (1f - time));
                // Change the face only as the icon passes its narrow edge.
                int currentTurn = Mathf.Min(turns, Mathf.FloorToInt(rotation + .5f));
                if (currentTurn != previousTurn)
                {
                    previousTurn = currentTurn;
                    shown = currentTurn == turns ? (int)Receipt.Outcome.Value : (shown + random.Next(1, 3)) % 3;
                    SetIcon((MapOpportunityOutcome)shown);
                }
                // Each half turn reveals a new readable face, never mirrored or tilted like a clock hand.
                float faceAngle = (rotation - Mathf.Floor(rotation + .5f)) * 180f;
                icon.rectTransform.localRotation = Quaternion.Euler(0, faceAngle, 0);
                yield return null;
            }
            SetIcon(Receipt.Outcome.Value);
            icon.rectTransform.localRotation = Quaternion.identity;
            IsSpinning = false;
            yield return new WaitForSecondsRealtime(.3f);
            for (float t = 0; t < .3f; t += Time.unscaledDeltaTime)
            {
                float progress = Mathf.SmoothStep(0, 1, t / .3f);
                icon.rectTransform.anchoredPosition = Vector2.Lerp(revealPose.anchoredPosition, resultPosition, progress);
                icon.rectTransform.sizeDelta = Vector2.Lerp(revealPose.sizeDelta, resultSize, progress);
                icon.rectTransform.localRotation = Quaternion.Slerp(Quaternion.identity, resultRotation, progress);
                yield return null;
            }
            Receipt.PresentationRevealed = true;
        }
        RestoreIconPose();
        value.gameObject.SetActive(true); body.gameObject.SetActive(true); detail.gameObject.SetActive(true);
        if (Receipt.Outcome == MapOpportunityOutcome.Recovery)
        {
            if (!session.TryResolveOpportunityRecovery(EncounterId)) yield break;
            for (float t = 0; t < .6f; t += Time.unscaledDeltaTime)
            { DisplayReceipt(Receipt, Mathf.SmoothStep(0, 1, t / .6f)); yield return null; }
        }
        DisplayReceipt(Receipt, 1); IsRevealed = true;
    }

    private void SetIcon(MapOpportunityOutcome outcome)
    {
        var tile = MapTravelView.AtlasRect(outcome == MapOpportunityOutcome.Card ? 10 :
            outcome == MapOpportunityOutcome.Recovery ? 11 : 8);
        icon.uvRect = new Rect(tile.x, tile.y, tile.z, tile.w);
    }

    private void LateUpdate()
    {
        if (controls == null) return;
        bool ready = !returning && (EncounterId == null || (IsRevealed && session != null &&
            !MapTravelCoordinator.Ensure(session).IsBusy));
        controls.interactable = ready; controls.blocksRaycasts = ready;
    }

    protected override bool BindInterface()
    {
        if (!base.BindInterface() || cardContent == null || !cardContent.IsValid || recoveryContent == null ||
            !recoveryContent.IsValid || battleContent == null || !battleContent.IsValid || secondaryButton == null ||
            secondaryText == null || recoveryDetail == null || icon == null || revealPose == null ||
            rewardRoot == null || rewardRoot.GetComponent<RewardCardView>() == null || replacementRoot == null ||
            replacementContent == null || replacementTemplate == null || controls == null) return false;
        resultPosition = icon.rectTransform.anchoredPosition;
        resultSize = icon.rectTransform.sizeDelta;
        resultRotation = icon.rectTransform.localRotation;
        revealValue = value; revealBody = body;
        controls.interactable = false;
        SecondaryButton.onClick.AddListener(Secondary);
        SecondaryButton.gameObject.SetActive(false);
        replacementTemplate.gameObject.SetActive(false);
        replacementRoot.SetActive(false);
        SetContent(null);
        capacity.gameObject.SetActive(false);
        value.text = "";
        return true;
    }

    private void RestoreIconPose()
    {
        icon.rectTransform.anchoredPosition = resultPosition;
        icon.rectTransform.sizeDelta = resultSize;
        icon.rectTransform.localRotation = resultRotation;
    }

    private void SetContent(ContentView content)
    {
        cardContent.root.SetActive(content == cardContent);
        recoveryContent.root.SetActive(content == recoveryContent);
        battleContent.root.SetActive(content == battleContent);
        heading.gameObject.SetActive(content == null);
        revealValue.gameObject.SetActive(content == null);
        revealBody.gameObject.SetActive(content == null);
        value = content != null ? content.value : revealValue;
        body = content != null ? content.body : revealBody;
    }

    protected override void DisplayReceipt(MapNonCombatReceipt receipt, float progress)
    {
        if (receipt?.Outcome == null) return;
        bool card = receipt.Outcome == MapOpportunityOutcome.Card;
        bool heal = receipt.Outcome == MapOpportunityOutcome.Recovery;
        SetContent(card ? cardContent : heal ? recoveryContent : battleContent);
        replacementRoot.SetActive(false);
        value.gameObject.SetActive(true); body.gameObject.SetActive(true); detail.gameObject.SetActive(true);
        RestoreIconPose();
        detail.text = "";
        capacity.text = $"DECK CAPACITY  {session.DeckCapacity} / {RunSession.CapacityLimit}";
        ContinueButton.gameObject.SetActive(true);
        SecondaryButton.gameObject.SetActive(false);
        SetIcon(receipt.Outcome.Value);
        healthFill.transform.parent.gameObject.SetActive(heal);
        capacity.gameObject.SetActive(card);
        if (card)
        {
            value.text = "";
            if (receipt.Offers.Count > 0 && displayedOffer != receipt.Offers[0])
            {
                displayedOffer = receipt.Offers[0];
                rewardRoot.GetComponent<RewardCardView>().Initialize(displayedOffer, _ => Continue(), true);
            }
            if (rewardRoot != null) rewardRoot.SetActive(true);
            body.text = receipt.GrantedCard != null ? "Card acquired" : receipt.Skipped ? "Reward skipped" : "";
            continueText.text = receipt.Resolved ? "Continue" :
                session.CanAddCard(receipt.Offers[0]) ? "Take Card" : "Replace Card";
            SecondaryButton.gameObject.SetActive(!receipt.Resolved);
            secondaryText.text = "Skip";
        }
        else if (heal)
        {
            int shown = Mathf.RoundToInt(Mathf.Lerp(receipt.HealthBefore, receipt.HealthAfter, progress));
            value.text = $"{shown} <size=55%>/ {receipt.MaxHealth} HP</size>";
            healthFill.rectTransform.anchorMax = new Vector2((float)shown / Mathf.Max(1, receipt.MaxHealth), 1);
            body.text = receipt.Recovered == 0 ? "Full health" : $"+{receipt.Recovered} HP";
            recoveryDetail.text = receipt.Recovered > 0 ? $"{receipt.HealthBefore} -> {receipt.HealthAfter} HP" : "";
            continueText.text = "Continue";
        }
        else
        {
            continueText.text = "Fight";
            if (!string.IsNullOrEmpty(battleError))
            { detail.text = battleError; continueText.text = "Retry Battle"; }
        }
        ContinueButton.interactable = true;
        if (receipt.Completed) IsRevealed = true;
    }

    protected override void Continue()
    {
        if (!CanAct()) return;
        if (Receipt.Completed || Receipt.Resolved) { ReturnToMap(); return; }
        if (Receipt.Outcome == MapOpportunityOutcome.Battle)
        {
            if (!MapTravelCoordinator.Ensure(session).BeginOpportunityBattle(this))
                ShowBattleError("Battle unavailable. Retry when ready.");
            else battleError = null;
        }
        else if (Receipt.Outcome == MapOpportunityOutcome.Card && !IsReplacing)
        {
            if (session.CanAddCard(Receipt.Offers[0])) Claim(null);
            else ShowReplacement();
        }
    }

    private bool CanAct() => IsActivated && IsRevealed && !returning && session != null && Receipt != null &&
        !MapTravelCoordinator.Ensure(session).IsBusy;

    private void Secondary()
    {
        if (!CanAct()) return;
        if (IsReplacing)
        {
            replacementRoot.SetActive(false);
            DisplayReceipt(Receipt, 1); return;
        }
        if (session.TryResolveOpportunityReward(EncounterId, null, null, out var reason)) ReturnToMap();
        else detail.text = reason;
    }

    private void Claim(string outgoing)
    {
        if (!CanAct()) return;
        if (!session.TryResolveOpportunityReward(EncounterId, Receipt.Offers[0], outgoing, out var reason))
        { detail.text = reason; return; }
        replacementRoot.SetActive(false); DisplayReceipt(Receipt, 1);
    }

    private void ShowReplacement()
    {
        if (IsReplacing) return;
        cardContent.root.SetActive(false);
        capacity.gameObject.SetActive(false); detail.text = "";
        ContinueButton.gameObject.SetActive(false);
        secondaryText.text = "Cancel Replacement";
        replacementRoot.SetActive(true);
        int index = 0;
        foreach (var instance in session.RunDeck)
        {
            if (instance.sacrificed || instance.definition == null) continue;
            string id = instance.instanceId;
            if (index == replacementCards.Count)
            {
                var clone = Instantiate(replacementTemplate, replacementContent);
                clone.name = $"C{index + 1:00}"; replacementCards.Add(clone);
            }
            var card = replacementCards[index++];
            card.gameObject.SetActive(true);
            card.Initialize(instance.definition, _ => Claim(id), true);
        }
        for (int i = index; i < replacementCards.Count; i++) replacementCards[i].gameObject.SetActive(false);
        Canvas.ForceUpdateCanvases();
        replacementContent.GetComponentInParent<ScrollRect>().verticalNormalizedPosition = 1;
    }

    private void ReturnToMap()
    {
        returning = MapTravelCoordinator.Ensure(session).ContinueNonCombat(this, EncounterId);
        if (!returning) ShowReturnError("Could not return. Retry.");
    }

    public void ShowBattleError(string message)
    { battleError = message; detail.text = message; continueText.text = "Retry Battle"; ContinueButton.interactable = true; }

    public override void ShowReturnError(string message)
    { returning = false; IsRevealed = true; base.ShowReturnError(message); }

}
