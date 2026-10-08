using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public sealed class MapCardView : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerMoveHandler, IPointerClickHandler
{
    [SerializeField] private GameObject frontFace;
    [SerializeField] private GameObject backFace;
    [SerializeField] private Image frontArtwork;
    [SerializeField] private Image backArtwork;
    [SerializeField] private TMP_Text titleText;
    [SerializeField] private TMP_Text effectText;
    [SerializeField] private TMP_Text scopeText;
    [SerializeField] private GameObject selectionGlow;
    [SerializeField] private GameObject sacrificedOverlay;
    [SerializeField] private Button sacrificeButton;
    [SerializeField] private StatusIcon offeringIcon;
    [SerializeField] private Image brokenHeartCut;
    [SerializeField] private Image backFill;
    [SerializeField] private float flipDuration = 0.28f;
    [SerializeField] private float hoverScale = 1.04f;
    [SerializeField] private float hoverAnimationSpeed = 14f;
    [SerializeField] private float maxPitchAngle = 5f;
    [SerializeField] private float maxYawAngle = 8f;
    [SerializeField] private float maxRollAngle = 1.5f;
    [SerializeField] private float tiltSmoothTime = 0.07f;
    [SerializeField] private CardDefinition definition;
    [SerializeField] private GameObject combatFrontInstance;
    [SerializeField] private GameObject backInstance;

    private MapController controller;
    private RectTransform rectTransform;
    private RectTransform visualTransform;
    private Vector3 visualBaseScale;
    private Vector2 targetTilt;
    private Vector2 currentTilt;
    private Vector2 tiltVelocity;
    private float targetRoll;
    private float currentRoll;
    private float rollVelocity;
    private float flipProgress;
    private bool showingBack;
    private bool targetBack;
    private bool initialized;
    private bool pointerHovered;
    private bool interactionLocked;
    private float settleRemaining, settleDuration;
    private Vector3 settleOffset;
    public CardDefinition Definition => definition;
    public string InstanceId { get; private set; }
    public bool PreservesBackTypography(Transform target) => backFace != null && target.IsChildOf(backFace.transform);

    private void Awake()
    {
        rectTransform = (RectTransform)transform;
        EnsureVisualTransform();

        frontArtwork.gameObject.SetActive(false);
        backFace.SetActive(false);
        selectionGlow.SetActive(false);
        sacrificedOverlay.SetActive(false);
    }

    private void Update()
    {
        if (!initialized || interactionLocked)
        {
            return;
        }

        EnsureVisualTransform();

        float deltaTime = Time.unscaledDeltaTime;
        float duration = Mathf.Max(flipDuration, 0.01f);
        float targetProgress = targetBack ? 1f : 0f;
        flipProgress = Mathf.MoveTowards(flipProgress, targetProgress, deltaTime / duration);

        bool backVisible = flipProgress >= 0.5f;
        if (backVisible != showingBack)
        {
            SetFace(backVisible);
        }

        currentTilt = Vector2.SmoothDamp(
            currentTilt,
            targetTilt,
            ref tiltVelocity,
            Mathf.Max(tiltSmoothTime, 0.01f),
            Mathf.Infinity,
            deltaTime);
        currentRoll = Mathf.SmoothDamp(
            currentRoll,
            targetRoll,
            ref rollVelocity,
            Mathf.Max(tiltSmoothTime, 0.01f),
            Mathf.Infinity,
            deltaTime);

        float easedProgress = flipProgress * flipProgress * (3f - 2f * flipProgress);
        float fullFlipAngle = easedProgress * 180f;
        float visibleFlipAngle = fullFlipAngle <= 90f ? fullFlipAngle : fullFlipAngle - 180f;
        visualTransform.localRotation = Quaternion.Euler(
            currentTilt.x,
            visibleFlipAngle + currentTilt.y,
            currentRoll);

        float scaleBlend = 1f - Mathf.Exp(-hoverAnimationSpeed * deltaTime);
        Vector3 targetScale = visualBaseScale * (pointerHovered ? hoverScale : 1f);
        visualTransform.localScale = Vector3.Lerp(visualTransform.localScale, targetScale, scaleBlend);
        if (settleRemaining > 0)
        {
            settleRemaining = Mathf.Max(0, settleRemaining - deltaTime);
            visualTransform.localPosition = settleOffset * Mathf.SmoothStep(0, 1, settleRemaining / settleDuration);
        }
    }

    public void Initialize(MapController owner, CardDefinition cardDefinition, string instanceId = null)
    {
        EnsureVisualTransform();
        controller = owner;
        InstanceId = instanceId;
        interactionLocked = false;
        if (cardDefinition != null && cardDefinition != definition)
        {
            definition = cardDefinition;
            // Views cloned from another card still hold that card's art, so rebuild it.
            if (combatFrontInstance != null) Destroy(combatFrontInstance);
            if (backInstance != null) Destroy(backInstance);
            combatFrontInstance = null;
            backInstance = null;
        }
        initialized = true;
        pointerHovered = false;
        targetBack = false;
        showingBack = false;
        flipProgress = 0f;
        targetTilt = Vector2.zero;
        currentTilt = Vector2.zero;
        targetRoll = 0f;
        currentRoll = 0f;
        visualTransform.localRotation = Quaternion.identity;
        visualTransform.localScale = visualBaseScale;
        visualTransform.localPosition = Vector3.zero; settleRemaining = 0;
        if (combatFrontInstance == null) BuildCombatFront();
        else ConfigureCombatFront(combatFrontInstance);
        BuildBack();
        titleText.text = definition.displayName;
        var preview = MapSacrificePreview.For(owner != null ? owner.Session : null, definition, instanceId);
        effectText.text = preview.BackValue;
        effectText.color = owner != null && owner.SacrificePanel != null ? owner.SacrificePanel.EffectColor(preview.Symbol) : preview.Accent;
        effectText.gameObject.SetActive(true);
        scopeText.text = "Next battle";
        if (offeringIcon != null) { offeringIcon.symbol = preview.Symbol; offeringIcon.color = effectText.color; offeringIcon.SetVerticesDirty(); }
        if (brokenHeartCut != null) brokenHeartCut.enabled = preview.Symbol == StatusSymbol.Health;
        frontFace.SetActive(true);
        backFace.SetActive(false);
        selectionGlow.SetActive(false);
        sacrificedOverlay.SetActive(false);
        if (sacrificeButton != null) sacrificeButton.gameObject.SetActive(false);
    }

    private void BuildBack()
    {
        if (backInstance != null)
        {
            backInstance.SetActive(false);
            Destroy(backInstance);
            backInstance = null;
        }
        // One rendering path for authored and dynamically cloned cards. These are the
        // same padded frame dimensions as the combat face, not the narrower layout cell.
        backArtwork.gameObject.SetActive(true);
        backArtwork.sprite = definition.backArtwork;
        backArtwork.enabled = definition.backArtwork != null;
        backArtwork.color = Color.white;
        backArtwork.preserveAspect = true;
        backArtwork.raycastTarget = false;
        // The authored template owns dimensions, typography and fill; binding only changes content.
    }

    private void BuildCombatFront()
    {
        if (combatFrontInstance != null)
        {
            Destroy(combatFrontInstance);
        }

        if (definition.combatPrefab == null)
        {
            frontArtwork.gameObject.SetActive(true);
            frontArtwork.sprite = definition.FrontIllustration;
            return;
        }

        frontArtwork.gameObject.SetActive(false);
        combatFrontInstance = Instantiate(definition.combatPrefab, frontFace.transform);
        combatFrontInstance.name = $"Front_{definition.combatPrefab.name}";

        ConfigureCombatFront(combatFrontInstance);
    }

    private void ConfigureCombatFront(GameObject target)
    {
        if (frontArtwork != null && frontArtwork.gameObject != target)
        {
            frontArtwork.gameObject.SetActive(false);
        }
        target.SetActive(true);

        foreach (TMP_Text combatLabel in target.GetComponentsInChildren<TMP_Text>(true))
        {
            combatLabel.text = GameFonts.FormatEffect(definition.GetCombatDescription());
        }

        // After the labels above, so the card's cost number isn't overwritten with effect text.
        GameFonts.ApplyHierarchy(target.transform);
        foreach (BuffCards combatCard in target.GetComponentsInChildren<BuffCards>(true))
        {
            combatCard.enabled = false;
            combatCard.ApplyArt(definition);
        }

        foreach (Graphic graphic in target.GetComponentsInChildren<Graphic>(true))
        {
            graphic.raycastTarget = false;
        }

        RectTransform combatRect = target.GetComponent<RectTransform>();
        if (combatRect != null)
        {
            combatRect.anchorMin = new Vector2(0.5f, 0.5f);
            combatRect.anchorMax = new Vector2(0.5f, 0.5f);
            combatRect.pivot = new Vector2(0.5f, 0.5f);
            combatRect.anchoredPosition = Vector2.zero;
            combatRect.sizeDelta = new Vector2(150f, 250f);
            combatRect.localRotation = Quaternion.identity;
            combatRect.localScale = Vector3.one;
        }
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        if (!initialized || interactionLocked || controller == null || !controller.CanInteract) return;
        pointerHovered = true;
        targetBack = true;
        selectionGlow.SetActive(true);
        UpdateTiltTarget(eventData);
    }

    public void OnPointerMove(PointerEventData eventData)
    {
        if (!initialized || interactionLocked || !pointerHovered)
        {
            return;
        }

        UpdateTiltTarget(eventData);
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        if (!initialized || interactionLocked) return;
        pointerHovered = false;
        targetBack = false;
        targetTilt = Vector2.zero;
        targetRoll = 0f;
        selectionGlow.SetActive(false);
    }

    public void SetSacrificed()
    {
        sacrificedOverlay.SetActive(true);
        if (sacrificeButton != null) sacrificeButton.interactable = false;
    }

    public void SetInteractionLocked(bool value)
    {
        interactionLocked = value;
        if (!initialized) return;
        if (!value) OnPointerExit(null);
        if (value)
        {
            targetTilt = currentTilt = Vector2.zero; targetRoll = currentRoll = 0;
            visualTransform.localRotation = Quaternion.identity;
            flipProgress = showingBack ? 1 : 0;
            targetBack = showingBack;
        }
    }

    private void SetFace(bool backVisible)
    {
        showingBack = backVisible;
        frontFace.SetActive(!showingBack);
        backFace.SetActive(showingBack);

    }

    public void OnPointerClick(PointerEventData eventData)
    {
        if (initialized && !interactionLocked && controller != null && controller.CanInteract &&
            eventData.button == PointerEventData.InputButton.Left) controller.RequestSacrifice(this);
    }

    public void SettleFrom(Vector3 worldPosition, float duration)
    {
        settleOffset = transform.InverseTransformPoint(worldPosition);
        settleDuration = Mathf.Max(.01f, duration); settleRemaining = settleDuration;
        visualTransform.localPosition = settleOffset;
        StartCoroutine(SettleWhileLocked());
    }

    private IEnumerator SettleWhileLocked()
    {
        while (settleRemaining > 0 && interactionLocked)
        {
            settleRemaining = Mathf.Max(0, settleRemaining - Time.unscaledDeltaTime);
            visualTransform.localPosition = settleOffset * Mathf.SmoothStep(0, 1, settleRemaining / settleDuration);
            yield return null;
        }
    }

    private void EnsureVisualTransform()
    {
        if (visualTransform != null)
        {
            return;
        }

        if (rectTransform == null)
        {
            rectTransform = (RectTransform)transform;
        }

        visualTransform = rectTransform.Find("CardVisual") as RectTransform;
        if (visualTransform == null)
        {
            GameObject visualObject = new("CardVisual", typeof(RectTransform));
            visualTransform = visualObject.GetComponent<RectTransform>();
            visualTransform.SetParent(rectTransform, false);
            visualTransform.anchorMin = Vector2.zero;
            visualTransform.anchorMax = Vector2.one;
            visualTransform.pivot = new Vector2(0.5f, 0.5f);
            visualTransform.anchoredPosition = Vector2.zero;
            visualTransform.sizeDelta = Vector2.zero;
        }

        Transform[] visualChildren =
        {
            selectionGlow.transform,
            frontFace.transform,
            backFace.transform,
            sacrificedOverlay.transform
        };

        foreach (Transform visualChild in visualChildren)
        {
            if (visualChild.parent != visualTransform)
            {
                visualChild.SetParent(visualTransform, false);
            }
        }

        visualBaseScale = Vector3.one;
    }

    private void UpdateTiltTarget(PointerEventData eventData)
    {
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                rectTransform,
                eventData.position,
                eventData.enterEventCamera,
                out Vector2 localPoint))
        {
            return;
        }

        Rect rect = rectTransform.rect;
        float normalizedX = rect.width > 0f
            ? Mathf.Clamp(((localPoint.x - rect.xMin) / rect.width) * 2f - 1f, -1f, 1f)
            : 0f;
        float normalizedY = rect.height > 0f
            ? Mathf.Clamp(((localPoint.y - rect.yMin) / rect.height) * 2f - 1f, -1f, 1f)
            : 0f;

        targetTilt = new Vector2(-normalizedY * maxPitchAngle, -normalizedX * maxYawAngle);
        targetRoll = -normalizedX * maxRollAngle;
    }

    private void OnDisable()
    {
        pointerHovered = false;
        targetBack = false;
        targetTilt = Vector2.zero;
        currentTilt = Vector2.zero;
        targetRoll = 0f;
        currentRoll = 0f;
        flipProgress = 0f;
        showingBack = false;

        if (visualTransform != null)
        {
            visualTransform.localRotation = Quaternion.identity;
            visualTransform.localScale = visualBaseScale;
        }

        if (frontFace != null)
        {
            frontFace.SetActive(true);
        }
        if (backFace != null)
        {
            backFace.SetActive(false);
        }
        if (selectionGlow != null)
        {
            selectionGlow.SetActive(false);
        }
    }
}
