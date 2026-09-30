using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public sealed class MapCardView : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerMoveHandler
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
    [SerializeField] private float flipDuration = 0.28f;
    [SerializeField] private float detailHoverDelay = 0.35f;
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
    private Coroutine detailHoverRoutine;
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
    public CardDefinition Definition => definition;

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
        if (!initialized)
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
    }

    public void Initialize(MapController owner, CardDefinition cardDefinition)
    {
        EnsureVisualTransform();
        controller = owner;
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
        if (combatFrontInstance == null) BuildCombatFront();
        else ConfigureCombatFront(combatFrontInstance);
        if (backInstance == null) BuildBack();
        else ConfigureBack(backInstance);
        titleText.text = definition.displayName;
        effectText.text = string.Empty;
        effectText.gameObject.SetActive(false);
        scopeText.text = definition.overworldEffect.scopeLabel;
        frontFace.SetActive(true);
        backFace.SetActive(false);
        selectionGlow.SetActive(false);
        sacrificedOverlay.SetActive(false);
        sacrificeButton.onClick.RemoveAllListeners();
        sacrificeButton.onClick.AddListener(() => controller.RequestSacrifice(this));
    }

    private void BuildBack()
    {
        if (backInstance != null)
        {
            Destroy(backInstance);
        }

        if (definition.backPrefab == null)
        {
            backArtwork.gameObject.SetActive(true);
            backArtwork.sprite = definition.backArtwork;
            return;
        }

        backArtwork.gameObject.SetActive(false);
        backInstance = Instantiate(definition.backPrefab, backFace.transform);
        backInstance.name = $"Back_{definition.backPrefab.name}";
        backInstance.transform.SetAsFirstSibling();

        ConfigureBack(backInstance);
    }

    private void ConfigureBack(GameObject target)
    {
        if (backArtwork != null && backArtwork.gameObject != target)
        {
            backArtwork.gameObject.SetActive(false);
        }
        target.SetActive(true);

        foreach (Graphic graphic in target.GetComponentsInChildren<Graphic>(true))
        {
            graphic.raycastTarget = false;
        }

        Image backImage = target.GetComponent<Image>();
        if (backImage != null)
        {
            backImage.preserveAspect = false;
        }

        RectTransform backRect = target.GetComponent<RectTransform>();
        if (backRect != null)
        {
            backRect.anchorMin = new Vector2(0.5f, 0.5f);
            backRect.anchorMax = new Vector2(0.5f, 0.5f);
            backRect.pivot = new Vector2(0.5f, 0.5f);
            backRect.anchoredPosition = Vector2.zero;
            backRect.sizeDelta = new Vector2(150f, 250f);
            backRect.localRotation = Quaternion.identity;
            backRect.localScale = Vector3.one;
        }
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
            frontArtwork.sprite = definition.frontArtwork;
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
            combatLabel.text = definition.combatEffectText;
        }

        // After the labels above, so the card's cost number isn't overwritten with effect text.
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
        if (!initialized) return;
        pointerHovered = true;
        targetBack = true;
        selectionGlow.SetActive(true);
        UpdateTiltTarget(eventData);
        if (showingBack)
        {
            StartDetailHoverCountdown();
        }
    }

    public void OnPointerMove(PointerEventData eventData)
    {
        if (!initialized || !pointerHovered)
        {
            return;
        }

        UpdateTiltTarget(eventData);
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        if (!initialized) return;
        pointerHovered = false;
        targetBack = false;
        targetTilt = Vector2.zero;
        targetRoll = 0f;
        selectionGlow.SetActive(false);
        StopDetailHoverCountdown();
        if (showingBack)
        {
            controller.SetCardDetail(this, false);
        }
    }

    public void SetSacrificed()
    {
        sacrificedOverlay.SetActive(true);
        sacrificeButton.interactable = false;
    }

    private void SetFace(bool backVisible)
    {
        showingBack = backVisible;
        frontFace.SetActive(!showingBack);
        backFace.SetActive(showingBack);

        if (showingBack && pointerHovered)
        {
            StartDetailHoverCountdown();
            return;
        }

        StopDetailHoverCountdown();
        controller.SetCardDetail(this, false);
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

    private void StartDetailHoverCountdown()
    {
        StopDetailHoverCountdown();
        detailHoverRoutine = StartCoroutine(ShowDetailAfterHoverDelay());
    }

    private void StopDetailHoverCountdown()
    {
        if (detailHoverRoutine == null)
        {
            return;
        }

        StopCoroutine(detailHoverRoutine);
        detailHoverRoutine = null;
    }

    private IEnumerator ShowDetailAfterHoverDelay()
    {
        yield return new WaitForSecondsRealtime(detailHoverDelay);
        detailHoverRoutine = null;
        if (pointerHovered && showingBack)
        {
            controller.SetCardDetail(this, true);
        }
    }

    private void OnDisable()
    {
        StopDetailHoverCountdown();
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
        if (controller != null)
        {
            controller.SetCardDetail(this, false);
        }
    }
}
