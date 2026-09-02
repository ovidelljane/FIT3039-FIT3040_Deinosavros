using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public sealed class MapCardView : MonoBehaviour, IPointerClickHandler, IPointerEnterHandler, IPointerExitHandler
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
    [SerializeField] private CardDefinition definition;
    [SerializeField] private GameObject combatFrontInstance;
    [SerializeField] private GameObject backInstance;

    private MapController controller;
    private RectTransform rectTransform;
    private Coroutine detailHoverRoutine;
    private bool showingBack;
    private bool flipping;
    private bool initialized;
    private bool pointerHovered;
    public CardDefinition Definition => definition;

    private void Awake()
    {
        rectTransform = (RectTransform)transform;
        frontArtwork.gameObject.SetActive(false);
        backFace.SetActive(false);
        selectionGlow.SetActive(false);
        sacrificedOverlay.SetActive(false);
    }

    public void Initialize(MapController owner, CardDefinition cardDefinition)
    {
        controller = owner;
        if (cardDefinition != null)
        {
            definition = cardDefinition;
        }
        initialized = true;
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

        foreach (BuffCards combatCard in target.GetComponentsInChildren<BuffCards>(true))
        {
            combatCard.enabled = false;
        }

        foreach (Graphic graphic in target.GetComponentsInChildren<Graphic>(true))
        {
            graphic.raycastTarget = false;
        }

        foreach (TMP_Text combatLabel in target.GetComponentsInChildren<TMP_Text>(true))
        {
            combatLabel.text = definition.combatEffectText;
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

    public void OnPointerClick(PointerEventData eventData)
    {
        if (initialized && !flipping) StartCoroutine(Flip());
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        if (!initialized) return;
        pointerHovered = true;
        selectionGlow.SetActive(true);
        rectTransform.localScale = Vector3.one * 1.04f;
        if (showingBack)
        {
            StartDetailHoverCountdown();
        }
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        if (!initialized) return;
        pointerHovered = false;
        selectionGlow.SetActive(false);
        rectTransform.localScale = Vector3.one;
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

    private IEnumerator Flip()
    {
        flipping = true;
        float elapsed = 0f;
        bool switched = false;
        while (elapsed < flipDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            float angle = Mathf.Lerp(0f, 180f, Mathf.Clamp01(elapsed / flipDuration));
            if (!switched && angle >= 90f)
            {
                switched = true;
                showingBack = !showingBack;
                frontFace.SetActive(!showingBack);
                backFace.SetActive(showingBack);
                if (showingBack && pointerHovered)
                {
                    StartDetailHoverCountdown();
                }
                else
                {
                    StopDetailHoverCountdown();
                    controller.SetCardDetail(this, false);
                }
            }
            rectTransform.localRotation = Quaternion.Euler(0f, angle <= 90f ? angle : angle - 180f, 0f);
            yield return null;
        }
        rectTransform.localRotation = Quaternion.identity;
        flipping = false;
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
}
