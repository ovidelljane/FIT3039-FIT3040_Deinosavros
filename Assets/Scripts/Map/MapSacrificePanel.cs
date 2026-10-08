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
    [Header("Departure Confirmation")]
    public GameObject departureWarning;
    public RectTransform departurePanel;
    public TMP_Text departureMessage;
    public Button departureContinue, departureBack;
    [Header("Presentation")]
    [Min(.1f)] public float feedbackDuration = .6f;
    [Min(.05f)] public float deckSettleDuration = .2f;
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
        benefitTitle.text = preview.Title; benefitValue.text = preview.Value;
        benefitDetails.text = preview.Details; benefitLimit.text = preview.Limit;
        benefitValue.color = EffectColor(preview.Symbol); effectIcon.symbol = preview.Symbol; effectIcon.color = benefitValue.color;
        brokenHeartCut.enabled = preview.Symbol == StatusSymbol.Health;
        effectIcon.SetVerticesDirty();
        capacity.text = "Capacity  " + preview.CapacityBefore + " \u2192 " + preview.CapacityAfter + " / " + preview.CapacityLimit;
        reason.text = preview.CanConfirm ? "Removed from this run." : preview.Reason; reason.color = warningColor;
        confirm.interactable = preview.CanConfirm;
    }

    public void Confirm()
    {
        if (!IsOpen || IsBusy) return;
        IsBusy = true; confirm.interactable = close.interactable = false;
        if (!owner.TryCommitSacrifice(selected, selectedId, out string failure))
        {
            IsBusy = false; close.interactable = true;
            RefreshSelection(); reason.text = failure; return;
        }
        feedback = StartCoroutine(PlayFeedback());
    }

    private IEnumerator PlayFeedback()
    {
        cardOutline.enabled = true; cardOutline.effectColor = accentGold;
        flightIcon.symbol = effectIcon.symbol; flightIcon.color = effectIcon.color;
        flightIcon.SetVerticesDirty(); flightHeartCut.enabled = brokenHeartCut.enabled;
        var rect = flightIcon.rectTransform; rect.gameObject.SetActive(true);
        Vector3 from = effectIcon.transform.position;
        var canvas = GetComponentInParent<Canvas>().rootCanvas;
        var destination = statusButton.transform.position;
        float elapsed = 0;
        reason.text = "Offering prepared."; reason.color = accentGold;
        while (elapsed < feedbackDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / feedbackDuration);
            cardGroup.alpha = 1 - Mathf.SmoothStep(0, 1, Mathf.InverseLerp(.2f, 1, t));
            cardPreview.localScale = cardBaseScale * (1 - .07f * t);
            float travel = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(.18f, 1, t));
            rect.position = Vector3.Lerp(from, destination, travel) + Vector3.up * (Mathf.Sin(travel * Mathf.PI) * 50 * canvas.scaleFactor);
            rect.localScale = Vector3.one * Mathf.Lerp(1, .45f, travel);
            yield return null;
        }
        IsBusy = false; feedback = null; Close();
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
