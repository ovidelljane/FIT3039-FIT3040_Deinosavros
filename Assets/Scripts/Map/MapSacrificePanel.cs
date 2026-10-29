using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using UnityEngine.InputSystem;
using UnityEngine.Playables;

// Authored scene UI. Only the controller can commit a sacrifice; this is presentation.
[ExecuteAlways]
public sealed class MapSacrificePanel : MonoBehaviour
{
    [Header("Edit Mode Preview")]
    public CardDefinition previewCard;
    public bool previewBack;
    [Header("Detail Artwork")]
    public Vector2 artworkFocus = new(.5f,.5f);
    [Header("Card Text Overrides")]
    [Tooltip("Empty fields use the card definition. Numeric tokens remain linked to card values.")]
    public MapCardDetailText[] cardContent = System.Array.Empty<MapCardDetailText>();
    [HideInInspector] public int hierarchyEditingVersion;
    [Header("Scene References")]
    public GameObject overlay;
    public RectTransform panel, cardPreview;
    public CanvasGroup cardGroup;
    public CanvasGroup detailGroup;
    public Image illustration;
    public TMP_Text cardName, combatText, reason, statusText, statusDetails;
    public StatusIcon flightIcon;
    public Image flightHeartCut;
    public Button confirm, statusButton;
    public GameObject statusPopup;
    public SacrificeBurnEffect burnEffect;
    public MapTravelView playerSymbol;
    public Image feedbackBackdrop;
    public RectTransform detailVisual;
    [Header("Detail Hover")]
    [Min(0)] public float fadeInDuration = .18f;
    [Min(0)] public float fadeOutDuration = .14f;
    [Min(0)] public float hideDelay = .30f;
    [Min(0), Tooltip("Time over another card before replacing the current detail.")]
    public float hoverSwitchDelay = .20f;
    [Min(0), Tooltip("Maximum protected travel time from a card toward its detail.")]
    public float detailTransferTime = .80f;
    [Min(0), Tooltip("Padding around the detail destination in reference pixels.")]
    public float detailTransferPadding = 12;
    [Min(1), Tooltip("Movement tolerance in reference pixels before restarting the stationary timer.")]
    public float hoverMotionTolerance = 6;
    [Header("Sacrifice Timeline")]
    public PlayableDirector sequenceDirector;
    [Range(0, .9f)] public float dimOpacity = .50f;
    [Header("Departure Confirmation")]
    public GameObject departureWarning;
    public RectTransform departurePanel;
    public TMP_Text departureMessage;
    public Button departureContinue, departureBack;
    [Header("Presentation")]
    [Min(.05f)] public float deckSettleDuration = .2f;
    [Header("Offering Transfer")]
    [Range(0, 120)] public float transferArc = 50;
    [Min(0)] public float transferEndScale = .45f;
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
    private bool burning;
    private Vector3 flightBaseScale;
    private string departureNodeId;
    private int selectionKey = int.MinValue, statusKey = int.MinValue;
    private MapCardView selectedView;
    private MapCardView switchingView;
    private string switchingId;
    private Material artworkMaterial, artworkSourceMaterial;
    private bool detailHovered, displayedBack, canConfirm;
    private bool fadingOut;
    private float visibility;
    private float hideAt = -1;
    private MapCardView hoverCandidate;
    private string hoverCandidateId;
    private float candidateSince, stationarySince, transferUntil = -1, nearestDetailDistance;
    private Vector2 pointerPosition, stationaryAnchor, selectedPointer, transferOrigin;
    private Rect transferDestination;
    private bool pointerKnown;
    private readonly Vector3[] detailCorners = new Vector3[4];
    private StatusSymbol selectedSymbol;
    private Color selectedColor;
    private int contentRevision, displayedRevision = -1;
    private TimelinePreviewState timelinePreview;
    private sealed class TimelinePreviewState
    {
        public bool overlay, confirm, flight, heart;
        public float alpha, detailAlpha;
        public Color backdrop, flightColor;
        public Sprite sprite;
        public Material material, copy;
        public string title, effect;
        public Vector3 flightPosition, flightScale;
        public Quaternion flightRotation;
        public StatusSymbol symbol;
    }
    public bool IsTimelinePreview => timelinePreview != null;
    public SacrificeTimelineFrame LastTimelineFrame { get; private set; }
    public bool IsOpen => overlay != null && overlay.activeSelf && !fadingOut;
    public bool IsDepartureWarningOpen => departureWarning != null && departureWarning.activeSelf;
    public bool IsBlocking => IsBusy || IsDepartureWarningOpen;
    public bool IsBusy { get; private set; }
    public string SelectedInstanceId => selectedId;
    public bool IsReady => overlay != null && panel != null && confirm != null && cardGroup != null && statusText != null;
    public Material DetailMaterial => timelinePreview != null ? timelinePreview.material :
        artworkSourceMaterial != null ? artworkSourceMaterial : illustration != null ? illustration.material : null;

    public void Bind(MapController controller)
    {
        owner = controller;
        cardBaseScale = cardPreview.localScale;
        confirm.onClick.RemoveListener(Confirm);
        statusButton.onClick.RemoveListener(ToggleStatus);
        confirm.onClick.AddListener(Confirm);
        statusButton.onClick.AddListener(ToggleStatus);
        departureContinue.onClick.RemoveListener(ContinueDeparture);
        departureBack.onClick.RemoveListener(CancelDeparture);
        departureContinue.onClick.AddListener(ContinueDeparture);
        departureBack.onClick.AddListener(CancelDeparture);
        HideImmediate(); RefreshStatus();
    }

    public void Open(MapCardView view)
    {
        if (IsBusy || view == null) return;
        if (switchingView == view && switchingId == view.InstanceId) return;
        ClearHoverCandidate(); transferUntil = -1;
        if (pointerKnown) selectedPointer = pointerPosition;
        bool different = selectedView != view || selectedId != view.InstanceId;
        bool same = !different && IsOpen;
        if (different && overlay.activeSelf && detailGroup != null && visibility > 0 && fadeOutDuration > 0)
        {
            switchingView = view; switchingId = view.InstanceId;
            fadingOut = false; hideAt = -1;
            confirm.interactable = false;
            SetDetailVisibility(visibility, false);
            return;
        }
        ClearDetailSwitch();
        if (different) visibility = 0;
        if (!overlay.activeSelf) visibility = 0;
        if (fadeInDuration <= 0) visibility = 1;
        fadingOut = false;
        SetDetailVisibility(visibility, true);
        selectedView = view;
        hideAt = -1;
        if (same) { RefreshFace(); return; }
        SetSelection(view);
    }

    private void SetSelection(MapCardView view)
    {
        selectedView = view;
        selected = view.Definition; selectedId = view.InstanceId;
        selectionKey = int.MinValue;
        statusPopup.SetActive(false); overlay.SetActive(true); overlay.transform.SetAsLastSibling();
        cardGroup.alpha = 1; cardPreview.localScale = cardBaseScale;
        illustration.sprite = selected.FrontIllustration;
        cardName.text = DetailName(selected);
        displayedBack = !view.IsBack;
        RefreshSelection();
        RefreshFace();
    }

    private void ClearDetailSwitch() { switchingView = null; switchingId = null; }

    public void HoverCard(MapCardView view, PointerEventData eventData)
    {
        if (IsBusy || view == null || owner == null || !owner.CanInteract) return;
        float now = Time.unscaledTime;
        if (eventData != null) ObservePointer(eventData.position, now);
        if (switchingView == view && switchingId == view.InstanceId)
        { selectedPointer = pointerPosition; return; }
        if (!IsOpen || selectedView == view)
        {
            Open(view);
            selectedPointer = pointerPosition;
            return;
        }
        if (hoverCandidate == view && hoverCandidateId == view.InstanceId) return;
        hoverCandidate = view; hoverCandidateId = view.InstanceId;
        candidateSince = stationarySince = now; stationaryAnchor = pointerPosition;
    }

    public void LeaveCard(MapCardView view, PointerEventData eventData = null)
    {
        if (hoverCandidate == view) ClearHoverCandidate();
        if (!IsOpen || detailHovered || IsBusy) return;
        hideAt = Time.unscaledTime + hideDelay;
        if ((switchingView != null ? switchingView : selectedView) != view || eventData == null || !pointerKnown || detailTransferTime <= 0) return;
        transferOrigin = selectedPointer;
        var canvas = panel.GetComponentInParent<Canvas>().rootCanvas;
        var camera = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
        panel.GetWorldCorners(detailCorners);
        Vector2 min = RectTransformUtility.WorldToScreenPoint(camera, detailCorners[0]);
        Vector2 max = min;
        for (int i = 1; i < detailCorners.Length; i++)
        {
            Vector2 point = RectTransformUtility.WorldToScreenPoint(camera, detailCorners[i]);
            min = Vector2.Min(min, point); max = Vector2.Max(max, point);
        }
        float padding = detailTransferPadding * canvas.scaleFactor;
        transferDestination = Rect.MinMaxRect(min.x - padding, min.y - padding, max.x + padding, max.y + padding);
        nearestDetailDistance = Vector2.Distance(transferOrigin, ClosestDetailPoint(transferOrigin));
        transferUntil = Time.unscaledTime + detailTransferTime;
        ObservePointer(eventData.position, Time.unscaledTime);
    }
    public void OnPointerEnter(PointerEventData eventData)
    {
        if (fadingOut && overlay.activeSelf && selectedView != null)
        {
            fadingOut = false;
            if (fadeInDuration <= 0) visibility = 1;
            SetDetailVisibility(visibility, true);
        }
        if (!IsOpen) return;
        ClearHoverCandidate(); transferUntil = -1;
        detailHovered = true; hideAt = -1;
    }
    public void OnPointerExit(PointerEventData eventData)
    {
        detailHovered = false;
        if (IsOpen && !IsBusy && (selectedView == null || !selectedView.IsHovered)) hideAt = Time.unscaledTime + hideDelay;
    }

    private void LateUpdate()
    {
        if (!Application.isPlaying)
        {
            if (timelinePreview != null) return;
            if (overlay != null && overlay.activeInHierarchy && previewCard != null && illustration != null)
                ApplyArtworkStyle(illustration.material,illustration.sprite,previewBack,previewCard.frontArtworkRegion);
            return;
        }
        if (IsBusy || PauseMenu.IsPaused) return;
        TickDetailFade(Time.unscaledDeltaTime);
        if (!IsOpen) return;
        if (selectedView == null || !selectedView.gameObject.activeInHierarchy || selectedView.InstanceId != selectedId) { Close(); return; }
        TickHover(Mouse.current != null ? Mouse.current.position.ReadValue() : pointerPosition, Time.unscaledTime);
        if (!IsOpen) return;
        RefreshFace();
    }

    private void TickDetailFade(float delta)
    {
        if (overlay == null || !overlay.activeSelf) return;
        if (switchingId != null && (switchingView == null || !switchingView.gameObject.activeInHierarchy || switchingView.InstanceId != switchingId))
        {
            ClearDetailSwitch();
            if (!detailHovered && (selectedView == null || !selectedView.IsHovered)) hideAt = Time.unscaledTime + hideDelay;
        }
        bool switching = switchingView != null;
        bool disappearing = fadingOut || switching;
        float duration = disappearing ? fadeOutDuration : fadeInDuration;
        visibility = duration <= 0 ? (disappearing ? 0 : 1) :
            Mathf.MoveTowards(visibility, disappearing ? 0 : 1, Mathf.Max(0, delta) / duration);
        SetDetailVisibility(visibility, !disappearing);
        if (switching && visibility <= 0)
        {
            var next = switchingView;
            ClearDetailSwitch();
            SetSelection(next);
            if (fadeInDuration <= 0) visibility = 1;
            SetDetailVisibility(visibility, true);
        }
        if (fadingOut && visibility <= 0) HideImmediate();
    }

    private void SetDetailVisibility(float amount, bool interactable)
    {
        if (detailGroup == null) return;
        detailGroup.alpha = Mathf.SmoothStep(0, 1, amount);
        detailGroup.interactable = interactable;
        // Keep the visible card's hover region during fade-out, allowing pointer re-entry.
        detailGroup.blocksRaycasts = amount > 0 || interactable;
    }

    private void TickHover(Vector2 position, float now)
    {
        ObservePointer(position, now);
        bool protectedTravel = now < transferUntil && InsideTransfer(position);
        if (now < transferUntil && !protectedTravel) transferUntil = -1;
        bool pending = hoverCandidate != null && hoverCandidate.gameObject.activeInHierarchy &&
            hoverCandidate.IsHovered && hoverCandidate.InstanceId == hoverCandidateId;
        if (pending && !detailHovered && owner.CanInteract && now - candidateSince >= hoverSwitchDelay &&
            (!protectedTravel || now - stationarySince >= hoverSwitchDelay))
        {
            var next = hoverCandidate;
            Open(next); selectedPointer = position;
            return;
        }
        if (!pending) ClearHoverCandidate();
        if (hideAt >= 0 && !detailHovered && !selectedView.IsHovered && !pending && !protectedTravel && now >= hideAt) Close();
    }

    private void ObservePointer(Vector2 position, float now)
    {
        float scale = panel != null ? panel.GetComponentInParent<Canvas>().rootCanvas.scaleFactor : 1;
        float tolerance = Mathf.Max(1, hoverMotionTolerance * scale);
        if (!pointerKnown || (position - stationaryAnchor).sqrMagnitude > tolerance * tolerance)
        { stationaryAnchor = position; stationarySince = now; }
        pointerPosition = position; pointerKnown = true;
        if (now >= transferUntil) return;
        float distance = Vector2.Distance(position, ClosestDetailPoint(position));
        if (distance > nearestDetailDistance + tolerance) transferUntil = -1;
        else nearestDetailDistance = Mathf.Min(nearestDetailDistance, distance);
    }

    private Vector2 ClosestDetailPoint(Vector2 point) => new(
        Mathf.Clamp(point.x, transferDestination.xMin, transferDestination.xMax),
        Mathf.Clamp(point.y, transferDestination.yMin, transferDestination.yMax));

    private bool InsideTransfer(Vector2 point)
    {
        float edge = transferOrigin.x >= transferDestination.center.x ? transferDestination.xMax : transferDestination.xMin;
        Vector2 top = new(edge, transferDestination.yMax), bottom = new(edge, transferDestination.yMin);
        float a = Cross(transferOrigin, top, point), b = Cross(top, bottom, point), c = Cross(bottom, transferOrigin, point);
        return !((a < -.01f || b < -.01f || c < -.01f) && (a > .01f || b > .01f || c > .01f));
    }
    private static float Cross(Vector2 a, Vector2 b, Vector2 point) => (b.x - a.x) * (point.y - a.y) - (b.y - a.y) * (point.x - a.x);
    private void ClearHoverCandidate() { hoverCandidate = null; hoverCandidateId = null; }

    private void RefreshFace()
    {
        if (selectedView == null || detailVisual == null) return;
        detailVisual.localRotation = Quaternion.Euler(0, selectedView.FlipAngle, 0);
        if (displayedBack != selectedView.IsBack || displayedRevision != contentRevision)
        {
            displayedBack = selectedView.IsBack;
            displayedRevision = contentRevision;
            cardName.text = DetailName(selected);
            combatText.text = DetailEffect(selected,displayedBack);
        }
        if (artworkMaterial == null || illustration.material != artworkMaterial)
        {
            artworkSourceMaterial = illustration.material;
            if (artworkMaterial != null) Destroy(artworkMaterial);
            artworkMaterial = new Material(artworkSourceMaterial) { name = "Map Detail Instance" };
            illustration.material = artworkMaterial;
        }
        // Only crop and face orientation are runtime data. The material owns all appearance settings.
        artworkMaterial.CopyPropertiesFromMaterial(artworkSourceMaterial);
        ApplyArtworkStyle(artworkMaterial,illustration.sprite,displayedBack,new Rect(0,0,1,1));
        confirm.interactable = canConfirm && !selectedView.IsFlipping && switchingView == null;
    }

    public void ApplyArtworkStyle(Material material, Sprite sprite, bool back, Rect region)
    {
        if (material == null || illustration == null) return;
        var rect = illustration.rectTransform.rect;
        if (sprite != null && rect.width > 0 && rect.height > 0)
        {
            var uv = UnityEngine.Sprites.DataUtility.GetOuterUV(sprite);
            material.SetVector("_SourceUV",new Vector4(uv.x,uv.y,uv.z-uv.x,uv.w-uv.y));
            float left = Mathf.Clamp01(region.xMin), bottom = Mathf.Clamp01(region.yMin);
            float regionWidth = Mathf.Clamp(region.width,.001f,1-left), regionHeight = Mathf.Clamp(region.height,.001f,1-bottom);
            float sourceAspect = sprite.rect.width*regionWidth / (sprite.rect.height*regionHeight), aspect = rect.width / rect.height;
            float width = Mathf.Min(1, aspect / sourceAspect), height = Mathf.Min(1, sourceAspect / aspect);
            float focusX = Mathf.Clamp01(artworkFocus.x), focusY = Mathf.Clamp01(artworkFocus.y);
            material.SetVector("_ArtworkUV", new Vector4(Mathf.Lerp(uv.x,uv.z,left+(1-width)*regionWidth*focusX),
                Mathf.Lerp(uv.y,uv.w,bottom+(1-height)*regionHeight*focusY), (uv.z-uv.x)*regionWidth*width, (uv.w-uv.y)*regionHeight*height));
            material.SetVector("_PanelRect", new Vector4(rect.xMin,rect.yMin,rect.width,rect.height));
        }
        material.SetFloat("_Mirror",back ? 1 : 0);
    }

    private MapCardDetailText ContentFor(CardDefinition card)
    {
        foreach (var entry in cardContent)
            if (entry != null && entry.card == card) return entry;
        return null;
    }
    public string DetailName(CardDefinition card)
    {
        var content = ContentFor(card);
        return !string.IsNullOrEmpty(content?.title) ? content.title : card.displayName;
    }
    public string DetailEffect(CardDefinition card, bool back)
    {
        var content = ContentFor(card);
        string text = back ? content?.backText : content?.frontText;
        if (string.IsNullOrEmpty(text)) text = null;
        return back ? card.GetSacrificeDescription(owner != null ? owner.Session : null,text) : card.GetCombatDescription(template:text);
    }
    public void PreviewEditorCard(bool back)
    {
        if (Application.isPlaying || previewCard == null || !IsReady) return;
        previewBack = back; overlay.SetActive(true); cardGroup.alpha = 1;
        SetDetailVisibility(1, true);
        detailVisual.localRotation = Quaternion.identity;
        illustration.sprite = previewCard.frontArtwork;
        cardName.text = DetailName(previewCard); combatText.text = DetailEffect(previewCard,back);
        confirm.gameObject.SetActive(true); confirm.interactable = true;
        reason.gameObject.SetActive(false);
        ApplyArtworkStyle(illustration.material,illustration.sprite,back,previewCard.frontArtworkRegion);
    }

    private void OnValidate() => contentRevision++;

    private void Update()
    {
        if (!Application.isPlaying || owner == null || PauseMenu.IsPaused) return;
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
        canConfirm = preview.CanConfirm;
        selectedSymbol = preview.Symbol; selectedColor = EffectColor(preview.Symbol);
        ShowReason(preview.CanConfirm ? "" : preview.Reason);
        confirm.interactable = preview.CanConfirm && switchingView == null;
    }

    private void ShowReason(string message)
    {
        reason.text = message;
        reason.color = warningColor;
        reason.gameObject.SetActive(!string.IsNullOrEmpty(message));
    }

    public void Confirm()
    {
        if (!IsOpen || IsBusy || switchingView != null || selectedView == null || selectedView.IsFlipping || !confirm.interactable) return;
        if (sequenceDirector == null || sequenceDirector.playableAsset == null || burnEffect == null || !burnEffect.IsReady)
        { ShowReason("Sacrifice animation is not configured."); return; }
        IsBusy = true; confirm.interactable = false;
        if (!owner.TryCommitSacrifice(selected, selectedId, out string failure))
        {
            IsBusy = false;
            RefreshSelection(); ShowReason(failure); return;
        }
        owner.LockSacrificeInput();
        visibility = 1; fadingOut = false;
        SetDetailVisibility(1, true);
        detailVisual.localRotation = Quaternion.identity;
        confirm.gameObject.SetActive(false);
        if (feedbackBackdrop != null)
        {
            var tint = feedbackBackdrop.color; tint.a = 0; feedbackBackdrop.color = tint;
            feedbackBackdrop.raycastTarget = true;
        }
        feedback = StartCoroutine(PlayFeedback());
    }

    private IEnumerator PlayFeedback()
    {
        burning = burnEffect != null && burnEffect.Begin(cardPreview);
        if (burning) cardGroup.alpha = 0;
        flightBaseScale = flightIcon.rectTransform.localScale;
        flightIcon.SetSymbol(selectedSymbol, selectedColor);
        flightHeartCut.enabled = selectedSymbol == StatusSymbol.Health;
        flightIcon.gameObject.SetActive(false);
        // Migrate old scene bindings; the native director owns playback and seeking.
        if (sequenceDirector.timeUpdateMode == DirectorUpdateMode.Manual)
            sequenceDirector.timeUpdateMode = DirectorUpdateMode.UnscaledGameTime;
        sequenceDirector.time = 0;
        sequenceDirector.Play();
        ShowReason("");
        while (sequenceDirector.playableGraph.IsValid() && sequenceDirector.time < sequenceDirector.duration)
            yield return null;
        sequenceDirector.Stop();
        flightIcon.rectTransform.localScale = flightBaseScale;
        burnEffect?.Clear();
        IsBusy = false; feedback = null; Close();
        RefreshStatus();
    }

    // Reprojects onto the current Canvas, including when its resolution changes.
    public void RefreshFeedbackPresentation()
    {
        if ((IsBusy || timelinePreview != null) && sequenceDirector != null) sequenceDirector.Evaluate();
    }

    public void ApplyTimelineFrame(SacrificeTimelineFrame frame)
    {
        if (!IsBusy && timelinePreview == null) return;
        LastTimelineFrame = frame;
        if (burning) burnEffect.EvaluateFrame(frame);
        else cardGroup.alpha = 1 - frame.burn;
        PresentTransfer(frame.transfer, frame.enhance);
        if (feedbackBackdrop != null)
        {
            var tint = feedbackBackdrop.color;
            tint.a = dimOpacity * frame.dim * (1 - frame.brighten);
            feedbackBackdrop.color = tint;
        }
    }

    // Timeline preview changes presentation only, never cards, attributes or offering state.
    public bool PrepareTimelinePreview()
    {
        if (Application.isPlaying || !IsReady || burnEffect == null || !burnEffect.IsReady) return false;
        if (timelinePreview == null)
        {
            var rect = flightIcon.rectTransform;
            timelinePreview = new TimelinePreviewState
            {
                overlay = overlay.activeSelf, confirm = confirm.gameObject.activeSelf, flight = flightIcon.gameObject.activeSelf,
                heart = flightHeartCut.enabled, alpha = cardGroup.alpha, detailAlpha = detailGroup != null ? detailGroup.alpha : 1,
                backdrop = feedbackBackdrop.color,
                sprite = illustration.sprite, material = illustration.material, title = cardName.text, effect = combatText.text,
                flightPosition = rect.localPosition, flightScale = rect.localScale, flightRotation = rect.localRotation,
                symbol = flightIcon.symbol, flightColor = flightIcon.color,
                copy = new Material(illustration.material) { hideFlags = HideFlags.HideAndDontSave }
            };
            flightBaseScale = rect.localScale;
#if UNITY_EDITOR
            UnityEditor.EditorApplication.update += RefreshTimelinePreview;
            sequenceDirector.stopped += StopTimelinePreview;
#endif
        }
        var state = timelinePreview;
        overlay.SetActive(true); confirm.gameObject.SetActive(false); cardGroup.alpha = 1;
        if (detailGroup != null) detailGroup.alpha = 1;
        illustration.material = state.copy;
        state.copy.CopyPropertiesFromMaterial(state.material);
        if (previewCard != null)
        {
            illustration.sprite = previewCard.FrontIllustration;
            cardName.text = DetailName(previewCard); combatText.text = DetailEffect(previewCard, previewBack);
            var preview = MapSacrificePreview.For(null, previewCard, null);
            selectedSymbol = preview.Symbol; selectedColor = EffectColor(selectedSymbol);
            flightIcon.SetSymbol(selectedSymbol, selectedColor);
            flightHeartCut.enabled = selectedSymbol == StatusSymbol.Health;
        }
        ApplyArtworkStyle(state.copy, illustration.sprite, previewBack, new Rect(0,0,1,1));
        if (burnEffect.IsPlaying) burnEffect.RefreshSnapshot();
        else burning = burnEffect.Begin(cardPreview);
        cardGroup.alpha = burning ? 0 : 1;
        return true;
    }

    public void EndTimelinePreview()
    {
        if (timelinePreview == null) return;
#if UNITY_EDITOR
        UnityEditor.EditorApplication.update -= RefreshTimelinePreview;
        if (sequenceDirector != null) sequenceDirector.stopped -= StopTimelinePreview;
#endif
        var state = timelinePreview; timelinePreview = null;
        burnEffect?.Clear(); burnEffect?.ReleasePreviewResources(); playerSymbol?.ClearSacrificePulse(); burning = false;
        if (illustration != null) { illustration.sprite = state.sprite; illustration.material = state.material; }
        if (cardName != null) cardName.text = state.title;
        if (combatText != null) combatText.text = state.effect;
        if (cardGroup != null) cardGroup.alpha = state.alpha;
        if (detailGroup != null) detailGroup.alpha = state.detailAlpha;
        if (feedbackBackdrop != null) feedbackBackdrop.color = state.backdrop;
        if (confirm != null) confirm.gameObject.SetActive(state.confirm);
        if (flightIcon != null)
        {
            var rect = flightIcon.rectTransform;
            rect.localPosition = state.flightPosition; rect.localScale = state.flightScale; rect.localRotation = state.flightRotation;
            flightIcon.SetSymbol(state.symbol, state.flightColor); flightIcon.gameObject.SetActive(state.flight);
        }
        if (flightHeartCut != null) flightHeartCut.enabled = state.heart;
        if (overlay != null) overlay.SetActive(state.overlay);
        if (state.copy != null) DestroyImmediate(state.copy);
    }

#if UNITY_EDITOR
    private double nextPreviewRefresh;
    private void StopTimelinePreview(PlayableDirector director) => EndTimelinePreview();
    private void RefreshTimelinePreview()
    {
        if (timelinePreview == null) return;
        if (Application.isPlaying || !UnityEditor.AnimationMode.InAnimationMode() || sequenceDirector == null || !sequenceDirector.playableGraph.IsValid())
        { EndTimelinePreview(); return; }
        if (UnityEditor.EditorApplication.timeSinceStartup < nextPreviewRefresh) return;
        nextPreviewRefresh = UnityEditor.EditorApplication.timeSinceStartup + 1d / 30;
        sequenceDirector.Evaluate(); UnityEditor.SceneView.RepaintAll();
    }
#endif

    private void PresentTransfer(float travel, float pulse)
    {
        var root = (RectTransform)overlay.transform;
        Vector3 origin = root.InverseTransformPoint(burning ? burnEffect.FacePosition : cardPreview.position);
        Vector3 destination = root.InverseTransformPoint(statusButton.transform.position);
        if (playerSymbol != null && playerSymbol.TryGetCoreViewport(out var point))
            destination = new Vector3(root.rect.xMin + point.x * root.rect.width, root.rect.yMin + point.y * root.rect.height, 0);
        var rect = flightIcon.rectTransform;
        rect.gameObject.SetActive(travel > 0 && travel < 1);
        rect.position = root.TransformPoint(Vector3.Lerp(origin, destination, travel) + Vector3.up * (Mathf.Sin(travel * Mathf.PI) * transferArc));
        rect.localScale = flightBaseScale * Mathf.Lerp(1, transferEndScale, travel);
        playerSymbol?.SetSacrificePulse(pulse, selectedColor);
    }

    public void Close()
    {
        if (IsBusy || !IsOpen) return;
        ClearDetailSwitch();
        if (detailGroup == null || fadeOutDuration <= 0) HideImmediate();
        else
        {
            fadingOut = true;
            hideAt = -1;
            ClearHoverCandidate(); transferUntil = -1;
            SetDetailVisibility(visibility, false);
        }
        owner?.ReleaseSacrificeInput();
    }

    public void HideImmediate()
    {
        if (feedback != null) StopCoroutine(feedback);
        if (IsBusy && sequenceDirector != null) sequenceDirector.Stop();
        if (IsBusy && flightIcon != null) flightIcon.rectTransform.localScale = flightBaseScale;
        feedback = null; IsBusy = false;
        visibility = 0; fadingOut = false;
        ClearDetailSwitch();
        SetDetailVisibility(0, false);
        burnEffect?.Clear();
        playerSymbol?.ClearSacrificePulse();
        burning = false;
        cardGroup.alpha = 1; cardPreview.localScale = cardBaseScale;
        overlay.SetActive(false); flightIcon.gameObject.SetActive(false); statusPopup.SetActive(false);
        confirm.gameObject.SetActive(true);
        if (feedbackBackdrop != null)
        {
            var tint = feedbackBackdrop.color; tint.a = 0; feedbackBackdrop.color = tint;
            feedbackBackdrop.raycastTarget = false;
        }
        detailHovered = false; hideAt = -1; selectedView = null;
        ClearHoverCandidate(); transferUntil = -1; pointerKnown = false;
        if (detailVisual != null) detailVisual.localRotation = Quaternion.identity;
        if (departureWarning != null) departureWarning.SetActive(false);
        departureNodeId = null;
        selected = null; selectedId = null;
    }

    private void ToggleStatus()
    {
        if (owner == null || !owner.CanInteract) return;
        if (fadingOut || switchingView != null) HideImmediate();
        statusPopup.SetActive(!statusPopup.activeSelf);
        if (statusPopup.activeSelf) statusPopup.transform.SetAsLastSibling();
    }

    public void ShowDepartureWarning(string nodeId)
    {
        if (overlay != null && overlay.activeSelf) HideImmediate();
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
        EndTimelinePreview();
        if (owner == null) return;
        HideImmediate(); owner.ReleaseSacrificeInput();
    }

    private void OnDestroy()
    {
        EndTimelinePreview();
        if (illustration != null && illustration.material == artworkMaterial) illustration.material = artworkSourceMaterial;
        if (artworkMaterial != null) Destroy(artworkMaterial);
    }
}

[System.Serializable]
public sealed class MapCardDetailText
{
    public CardDefinition card;
    public string title;
    [TextArea(2,6)] public string frontText;
    [TextArea(2,6)] public string backText;
}
