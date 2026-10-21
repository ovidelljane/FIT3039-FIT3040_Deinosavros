using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using UnityEngine.InputSystem;

// Authored scene UI. Only the controller can commit a sacrifice; this is presentation.
public sealed class MapSacrificePanel : MonoBehaviour
{
    [Header("Scene References")]
    public GameObject overlay;
    public RectTransform panel, cardPreview;
    public CanvasGroup cardGroup;
    public Image illustration, frame, categoryMedallion;
    public TMP_Text cardName, combatText, cost, benefitTitle, benefitValue, benefitDetails, benefitLimit;
    public TMP_Text capacity, reason, statusText, statusDetails;
    public StatusIcon effectIcon, flightIcon;
    public Image brokenHeartCut, flightHeartCut;
    public Button confirm, close, statusButton;
    public GameObject statusPopup;
    public Outline cardOutline;
    public SacrificeBurnEffect burnEffect;
    public MapTravelView playerSymbol;
    public Image feedbackBackdrop;
    [Header("Departure Confirmation")]
    public GameObject departureWarning;
    public RectTransform departurePanel;
    public TMP_Text departureMessage;
    public Button departureContinue, departureBack;
    [Header("Presentation")]
    [Min(.1f)] public float feedbackDuration = .6f;
    [Min(.05f)] public float deckSettleDuration = .2f;
    [Header("Offering Transfer")]
    [Min(.1f)] public float transferDuration = .42f;
    [Min(.1f)] public float playerPulseDuration = .60f;
    [Range(0, 120)] public float transferArc = 50;
    [Range(0, 1)] public float playerRevealOpacity = .10f;
    public Color bodyColor = new(.94f, .88f, .76f);
    public Color accentGold = new(.88f, .70f, .40f);
    public Color warningColor = new(.85f, .64f, .48f);
    [Header("Offering Colors")]
    public Color attackSpeedColor = new(.91f, .73f, .40f);
    public Color shieldColor = new(.56f, .79f, .83f);
    public Color enemyHealthColor = new(.82f, .48f, .43f);
    public Color elixirColor = new(.73f, .60f, .88f);
    private MapController owner;
    private CardDefinition selected;
    private string selectedId, lastStatus;
    private Coroutine feedback;
    private Vector3 cardBaseScale;
    private float feedbackElapsed, transferStart, pulseStart, backdropAlpha;
    private bool burning, restoreBackdrop;
    private string departureNodeId;
    private int selectionKey = int.MinValue, statusKey = int.MinValue;
    public bool IsOpen => overlay != null && overlay.activeSelf;
    public bool IsDepartureWarningOpen => departureWarning != null && departureWarning.activeSelf;
    public bool IsBlocking => IsOpen || IsDepartureWarningOpen;
    public bool IsBusy { get; private set; }
    public string SelectedInstanceId => selectedId;
    public bool IsReady => overlay != null && panel != null && confirm != null && cardGroup != null && statusText != null;

    public void Bind(MapController controller)
    {
        owner = controller;
        cardBaseScale = cardPreview.localScale;
        confirm.onClick.RemoveListener(Confirm);
        close.onClick.RemoveListener(Close);
        statusButton.onClick.RemoveListener(ToggleStatus);
        confirm.onClick.AddListener(Confirm);
        close.onClick.AddListener(Close);
        statusButton.onClick.AddListener(ToggleStatus);
        departureContinue.onClick.RemoveListener(ContinueDeparture);
        departureBack.onClick.RemoveListener(CancelDeparture);
        departureContinue.onClick.AddListener(ContinueDeparture);
        departureBack.onClick.AddListener(CancelDeparture);
        HideImmediate(); RefreshStatus();
    }

    public void Open(MapCardView view)
    {
        selected = view.Definition; selectedId = view.InstanceId;
        selectionKey = int.MinValue;
        statusPopup.SetActive(false); overlay.SetActive(true); overlay.transform.SetAsLastSibling();
        cardGroup.alpha = 1; cardPreview.localScale = cardBaseScale;
        cardOutline.enabled = false;
        illustration.sprite = selected.FrontIllustration;
        frame.sprite = selected.frontBorder; frame.enabled = frame.sprite != null;
        cardName.text = selected.displayName;
        combatText.text = selected.GetCombatDescription();
        cost.text = selected.BaseValues.Cost.ToString();
        categoryMedallion.color = EffectColor(MapSacrificePreview.For(owner.Session, selected, selectedId).Symbol);
        RefreshSelection();
        EventSystem.current?.SetSelectedGameObject(close.gameObject);
    }

    private void Update()
    {
        if (owner == null) return;
        if (Keyboard.current?.escapeKey.wasPressedThisFrame == true)
        {
            if (IsDepartureWarningOpen) CancelDeparture();
            else if (IsOpen) Close();
            else statusPopup.SetActive(false);
        }
        RefreshStatus();
        if (IsOpen && !IsBusy) RefreshSelection();
        if (!owner.CanInteract && !IsOpen) statusPopup.SetActive(false);
    }

    private void RefreshSelection()
    {
        var session = owner.Session;
        int key = System.HashCode.Combine(session.DeckCapacity, session.DeckCapacityLimit, session.SacrificeUsed,
            session.PlayerAttackSpeed, session.MinimumOfferingAttackInterval, session.Progress?.Phase,
            selected != null ? selected.overworldEffect.magnitude : 0, selectedId);
        if (key == selectionKey) return;
        selectionKey = key;
        var preview = MapSacrificePreview.For(owner.Session, selected, selectedId);
        benefitValue.text = preview.BackValue;
        benefitValue.color = EffectColor(preview.Symbol); effectIcon.SetSymbol(preview.Symbol, benefitValue.color);
        brokenHeartCut.enabled = preview.Symbol == StatusSymbol.Health;
        ShowReason(preview.CanConfirm ? "" : preview.Reason);
        confirm.interactable = preview.CanConfirm;
    }

    private void ShowReason(string message)
    {
        reason.text = message;
        reason.color = warningColor;
        reason.gameObject.SetActive(!string.IsNullOrEmpty(message));
    }

    public void Confirm()
    {
        if (!IsOpen || IsBusy) return;
        IsBusy = true; confirm.interactable = close.interactable = false;
        if (!owner.TryCommitSacrifice(selected, selectedId, out string failure))
        {
            IsBusy = false; close.interactable = true;
            RefreshSelection(); ShowReason(failure); return;
        }
        feedback = StartCoroutine(PlayFeedback());
    }

    private IEnumerator PlayFeedback()
    {
        burning = burnEffect != null && burnEffect.Begin(cardPreview);
        transferStart = burning ? burnEffect.BurnStart + burnEffect.ignitionDuration + burnEffect.burnDuration * .85f : feedbackDuration * .5f;
        pulseStart = transferStart + Mathf.Max(.1f, transferDuration);
        float duration = Mathf.Max(burning ? burnEffect.Duration : feedbackDuration, pulseStart + Mathf.Max(.1f, playerPulseDuration));
        cardOutline.enabled = !burning; cardOutline.effectColor = accentGold;
        if (burning) cardGroup.alpha = 0;
        flightIcon.SetSymbol(effectIcon.symbol, effectIcon.color);
        flightHeartCut.enabled = brokenHeartCut.enabled;
        flightIcon.gameObject.SetActive(false);
        flightIcon.transform.SetAsLastSibling();
        restoreBackdrop = feedbackBackdrop != null;
        if (restoreBackdrop) backdropAlpha = feedbackBackdrop.color.a;
        feedbackElapsed = 0;
        ShowReason("");
        while (feedbackElapsed < duration)
        {
            // A slow render or window refocus must not skip the entire impact beat.
            feedbackElapsed += Mathf.Min(Time.unscaledDeltaTime, .05f);
            RefreshFeedbackPresentation();
            yield return null;
        }
        burnEffect?.Clear();
        IsBusy = false; feedback = null; Close();
        RefreshStatus();
    }

    // Reprojects onto the current Canvas, including when its resolution changes.
    public void RefreshFeedbackPresentation()
    {
        if (!IsBusy) return;
        if (burning) burnEffect.Evaluate(feedbackElapsed);
        else
        {
            float t = Mathf.Clamp01(feedbackElapsed / Mathf.Max(.1f, feedbackDuration));
            cardGroup.alpha = 1 - Mathf.SmoothStep(0, 1, Mathf.InverseLerp(.2f, 1, t));
            cardPreview.localScale = cardBaseScale * (1 - .07f * t);
        }
        var root = (RectTransform)overlay.transform;
        Vector3 origin = root.InverseTransformPoint(burning ? burnEffect.FacePosition : effectIcon.transform.position);
        Vector3 destination = root.InverseTransformPoint(statusButton.transform.position);
        if (playerSymbol != null && playerSymbol.TryGetCoreViewport(out var point))
            destination = new Vector3(root.rect.xMin + point.x * root.rect.width, root.rect.yMin + point.y * root.rect.height, 0);
        float travel = Mathf.SmoothStep(0, 1, (feedbackElapsed - transferStart) / Mathf.Max(.1f, transferDuration));
        var rect = flightIcon.rectTransform;
        rect.gameObject.SetActive(feedbackElapsed >= transferStart && feedbackElapsed < pulseStart);
        rect.position = root.TransformPoint(Vector3.Lerp(origin, destination, travel) + Vector3.up * (Mathf.Sin(travel * Mathf.PI) * transferArc));
        rect.localScale = Vector3.one * Mathf.Lerp(1, .45f, travel);
        playerSymbol?.SetSacrificePulse(Mathf.Clamp01((feedbackElapsed - pulseStart) / Mathf.Max(.1f, playerPulseDuration)), effectIcon.color);
        if (restoreBackdrop)
        {
            var tint = feedbackBackdrop.color;
            tint.a = Mathf.Lerp(backdropAlpha, Mathf.Min(backdropAlpha, playerRevealOpacity), travel);
            feedbackBackdrop.color = tint;
        }
    }

    public void Close()
    {
        if (IsBusy || !IsOpen) return;
        HideImmediate(); owner?.ReleaseSacrificeInput();
    }

    public void HideImmediate()
    {
        if (feedback != null) StopCoroutine(feedback);
        feedback = null; IsBusy = false;
        burnEffect?.Clear();
        playerSymbol?.ClearSacrificePulse();
        if (restoreBackdrop && feedbackBackdrop != null)
        {
            var tint = feedbackBackdrop.color; tint.a = backdropAlpha; feedbackBackdrop.color = tint;
        }
        restoreBackdrop = burning = false;
        cardGroup.alpha = 1; cardPreview.localScale = cardBaseScale;
        cardOutline.enabled = false;
        overlay.SetActive(false); flightIcon.gameObject.SetActive(false); statusPopup.SetActive(false);
        close.interactable = true;
        if (departureWarning != null) departureWarning.SetActive(false);
        departureNodeId = null;
        selected = null; selectedId = null;
    }

    private void ToggleStatus()
    {
        if (owner == null || !owner.CanInteract) return;
        statusPopup.SetActive(!statusPopup.activeSelf);
        if (statusPopup.activeSelf) statusPopup.transform.SetAsLastSibling();
    }

    public void ShowDepartureWarning(string nodeId)
    {
        departureNodeId = nodeId;
        statusPopup.SetActive(false);
        departureMessage.text = "Capacity " + owner.Session.DeckCapacity + " / " + owner.Session.DeckCapacityLimit +
            "\nEnter battle without sacrificing a card?";
        departureWarning.SetActive(true); departureWarning.transform.SetAsLastSibling();
        EventSystem.current?.SetSelectedGameObject(departureBack.gameObject);
    }

    public void CancelDeparture()
    {
        if (!IsDepartureWarningOpen) return;
        departureWarning.SetActive(false); departureNodeId = null;
        owner.CancelDepartureWarning();
    }

    public void ContinueDeparture()
    {
        if (!IsDepartureWarningOpen) return;
        string nodeId = departureNodeId;
        departureWarning.SetActive(false); departureNodeId = null;
        owner.ConfirmDepartureWithoutSacrifice(nodeId);
    }

    public Color EffectColor(StatusSymbol symbol) => symbol switch
    {
        StatusSymbol.Speed => attackSpeedColor,
        StatusSymbol.Shield => shieldColor,
        StatusSymbol.Health => enemyHealthColor,
        StatusSymbol.Elixir => elixirColor,
        _ => accentGold
    };

    public void RefreshStatus()
    {
        if (owner?.Session == null) return;
        var session = owner.Session;
        var modifier = session.PendingModifier;
        int key = System.HashCode.Combine(session.SacrificeUsed, modifier.isValid, modifier.sourceCardId,
            modifier.effectType, modifier.magnitude, session.PlayerAttackSpeed, session.MinimumOfferingAttackInterval);
        if (statusKey == key) return;
        statusKey = key;
        var pending = MapSacrificePreview.Pending(session);
        string text = pending != null ? "Next battle: " + pending.Compact :
            session.SacrificeUsed ? "Sacrifice used" : "Sacrifice available";
        if (text != lastStatus)
        {
            statusText.text = text; lastStatus = text;
            statusText.color = pending != null ? accentGold : bodyColor;
        }
        statusDetails.text = pending != null ? pending.Title + "\n" + pending.Value + "\n\n" + pending.Details + "\n" + pending.Limit +
            "" :
            session.SacrificeUsed ? "Win a normal battle to restore your sacrifice.\nYou may still inspect your cards." :
            "Choose a card to sacrifice before battle.";
    }

    private void OnDisable()
    {
        if (owner == null) return;
        HideImmediate(); owner.ReleaseSacrificeInput();
    }
}
