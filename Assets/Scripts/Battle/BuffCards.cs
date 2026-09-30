using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using TMPro;

// Append new values at the end: prefabs store these as ints.
public enum StatType { Damage, AttackSpeed, Heal, Shield, Elixir, DamageAllEnemies, ExtraHits, EnemySlow, ElixirRegen }

public class BuffCards : MonoBehaviour, IPointerClickHandler, IPointerEnterHandler, IPointerExitHandler, IPointerMoveHandler
{
    [SerializeField] StatType stat;
    [SerializeField] float amount = 1;
    [SerializeField] int elixirCost = 1;
    [SerializeField] float effectDuration = 3f;
    [SerializeField] TextMeshProUGUI label;

    BattleScript player;
    public GameObject effectPrefab;
    private GameObject effectSpawn;
    [SerializeField] private AudioSource audioSource;
    [SerializeField] private Image artworkImage;
    [SerializeField] private Image borderImage;

    [Header("Hand Hover")]
    [SerializeField] private float hoverScale = 1.04f;
    [SerializeField] private float hoverAnimationSpeed = 14f;
    [SerializeField] private float maxPitchAngle = 5f;
    [SerializeField] private float maxYawAngle = 8f;
    [SerializeField] private float maxRollAngle = 1.5f;
    [SerializeField] private float tiltSmoothTime = 0.07f;

    private DeckManager owner;
    private CardDefinition definition;
    private TextMeshProUGUI costText;
    private int displayedCost = -1;
    private RectTransform hoverArea;
    private RectTransform visualTransform;
    private bool pointerHovered;
    private Vector3 targetTilt;
    private Vector3 currentTilt;
    private Vector3 tiltVelocity;
    private PlayerStatusView statusView;
    public int Cost => Mathf.Max(0, elixirCost);
    public CardEffectValues Values => new CardEffectValues(stat, amount, Cost, effectDuration);
    public string Description => definition != null ? definition.GetCombatDescription(Values) : $"+{amount} {stat}";
    public AudioClip LegacyPlayClip => audioSource != null ? audioSource.clip : null;
    public bool UsesHealthCost => stat == StatType.Elixir;
    public bool MeetsResourceRequirement => owner != null && owner.Player != null &&
        (UsesHealthCost ? owner.Player.health >= Cost : owner.Player.elixir >= Cost);

    void Start()
    {
        player = owner != null ? owner.Player : BattleHud.FindPlayer(gameObject.scene);
        // Card details show in the hover panel instead of on the card face.
        if (label) label.gameObject.SetActive(false);
    }

    void OnDisable()
    {
        ResetHover();
    }

    void Update()
    {
        if (costText != null && displayedCost != Cost) { displayedCost = Cost; costText.text = Cost.ToString(); }
        if (visualTransform == null) return;
        float deltaTime = Time.unscaledDeltaTime;
        currentTilt = Vector3.SmoothDamp(currentTilt, targetTilt, ref tiltVelocity,
            Mathf.Max(0.01f, tiltSmoothTime), Mathf.Infinity, deltaTime);
        visualTransform.localRotation = Quaternion.Euler(currentTilt);
        float blend = 1f - Mathf.Exp(-hoverAnimationSpeed * deltaTime);
        visualTransform.localScale = Vector3.Lerp(visualTransform.localScale,
            Vector3.one * (pointerHovered ? hoverScale : 1f), blend);
    }

    public void OnPointerEnter(PointerEventData e)
    {
        if (!isActiveAndEnabled || owner == null) return;
        pointerHovered = true;
        if (statusView == null) statusView = BattleHud.Find(gameObject.scene)?.Status;
        statusView?.ShowCost(this);
        UpdateHoverTilt(e);
        string title = definition != null ? definition.displayName : name;
        CardInfoPanel.Show(this, GetComponentInParent<Canvas>().rootCanvas, title, Cost, Description);
    }

    public void OnPointerExit(PointerEventData e)
    {
        pointerHovered = false;
        targetTilt = Vector3.zero;
        statusView?.ClearCost(this);
        CardInfoPanel.Hide(this);
    }

    public void OnPointerMove(PointerEventData e)
    {
        if (pointerHovered) UpdateHoverTilt(e);
    }

    public void ResetHover()
    {
        statusView?.ClearCost(this);
        pointerHovered = false;
        targetTilt = currentTilt = tiltVelocity = Vector3.zero;
        if (visualTransform != null)
        {
            visualTransform.localRotation = Quaternion.identity;
            visualTransform.localScale = Vector3.one;
        }
        CardInfoPanel.Hide(this);
    }

    private void UpdateHoverTilt(PointerEventData e)
    {
        if (hoverArea == null || !RectTransformUtility.ScreenPointToLocalPointInRectangle(
            hoverArea, e.position, e.enterEventCamera, out Vector2 point)) return;
        Rect rect = hoverArea.rect;
        float x = Mathf.Clamp((point.x - rect.center.x) / Mathf.Max(1f, rect.width * 0.5f), -1f, 1f);
        float y = Mathf.Clamp((point.y - rect.center.y) / Mathf.Max(1f, rect.height * 0.5f), -1f, 1f);
        targetTilt = new Vector3(-y * maxPitchAngle, -x * maxYawAngle, -x * maxRollAngle);
    }

    public void Initialize(DeckManager deckManager, CardDefinition cardDefinition)
    {
        owner = deckManager;
        definition = cardDefinition;
        ApplyArt(cardDefinition);
        GameFonts.ApplyHierarchy(transform);
        if (costText != null) GameFonts.Apply(costText, GameFontRole.Numeric);
        if (owner != null) EnsureHoverVisual();
    }

    private void EnsureHoverVisual()
    {
        if (visualTransform != null || !(transform is RectTransform root)) return;

        // Keep layout and hit testing stationary; only the face and its cost text animate.
        // Map card fronts call ApplyArt, not Initialize, so they retain their own animation.
        visualTransform = CreateRect("CardVisual", root);
        visualTransform.anchorMin = Vector2.zero;
        visualTransform.anchorMax = Vector2.one;
        visualTransform.sizeDelta = Vector2.zero;

        int childCount = root.childCount - 1;
        for (int i = 0; i < childCount; i++)
        {
            var child = root.GetChild(0) as RectTransform;
            Vector3 position = child.anchoredPosition3D;
            child.SetParent(visualTransform, false);
            child.anchoredPosition3D = position;
        }

        Image rootImage = GetComponent<Image>();
        if (rootImage != null)
        {
            var backgroundRect = CreateRect("Background", visualTransform);
            backgroundRect.anchorMin = Vector2.zero;
            backgroundRect.anchorMax = Vector2.one;
            backgroundRect.sizeDelta = Vector2.zero;
            backgroundRect.SetAsFirstSibling();
            var background = backgroundRect.gameObject.AddComponent<Image>();
            background.sprite = rootImage.sprite;
            background.color = rootImage.color;
            background.material = rootImage.material;
            background.type = rootImage.type;
            background.preserveAspect = rootImage.preserveAspect;
            background.enabled = rootImage.enabled;
            rootImage.color = Color.clear;
        }

        foreach (var graphic in visualTransform.GetComponentsInChildren<Graphic>(true))
            graphic.raycastTarget = false;

        // The visible border is wider than the hand's layout cell. Preserve that hit area.
        hoverArea = CreateRect("HitArea", root);
        RectTransform source = borderImage != null ? borderImage.rectTransform : visualTransform;
        hoverArea.anchorMin = source.anchorMin;
        hoverArea.anchorMax = source.anchorMax;
        hoverArea.pivot = source.pivot;
        hoverArea.sizeDelta = source.sizeDelta;
        hoverArea.anchoredPosition3D = source.anchoredPosition3D;
        hoverArea.localScale = source.localScale;
        hoverArea.localRotation = source.localRotation;
        var hitImage = hoverArea.gameObject.AddComponent<Image>();
        hitImage.color = Color.clear;
        hitImage.raycastTarget = true;
    }

    private static RectTransform CreateRect(string objectName, Transform parent)
    {
        var rect = (RectTransform)new GameObject(objectName, typeof(RectTransform)).transform;
        rect.gameObject.layer = parent.gameObject.layer;
        rect.SetParent(parent, false);
        return rect;
    }

    public void ApplyArt(CardDefinition cardDefinition)
    {
        if (cardDefinition == null) return;
        if (artworkImage != null)
        {
            artworkImage.sprite = cardDefinition.FrontIllustration;
            artworkImage.enabled = artworkImage.sprite != null;
        }
        if (borderImage != null)
        {
            borderImage.sprite = cardDefinition.frontBorder;
            borderImage.enabled = cardDefinition.frontBorder != null;
            ShowCost();
        }
    }

    private void ShowCost()
    {
        if (costText == null)
        {
            var costObject = new GameObject("CostText", typeof(RectTransform));
            costObject.transform.SetParent(borderImage.transform, false);
            RectTransform rect = (RectTransform)costObject.transform;
            // Centre of the small circle in the top-left corner of every border sprite.
            rect.anchorMin = rect.anchorMax = new Vector2(0.209f, 0.855f);
            rect.sizeDelta = new Vector2(26f, 26f);

            costText = costObject.AddComponent<TextMeshProUGUI>();
            GameFonts.Apply(costText, GameFontRole.Numeric);
            costText.alignment = TextAlignmentOptions.Center;
            costText.enableAutoSizing = true;
            costText.fontSizeMin = 8f;
            costText.fontSizeMax = 22f;
            costText.color = Color.white;
            costText.raycastTarget = false;
        }
        displayedCost = Cost; costText.text = Cost.ToString();
    }

    public void OnPointerClick(PointerEventData e)
    {
        if (!enabled || player == null || player.health <= 0 || owner == null || !owner.CanPlay) return;
        if (MeetsResourceRequirement)
        {
            if (!UsesHealthCost)
            {
                player.elixir -= Cost;
            }
            else
            {
                player.health -= Cost;
            }

            Debug.Log(player.elixir);
            
            switch (stat)
            {
                case StatType.Damage:
                case StatType.AttackSpeed:
                case StatType.ExtraHits:
                case StatType.ElixirRegen: SpawnEffect(player); break;
                case StatType.EnemySlow:
                    foreach (BattleScript enemy in LivingEnemies()) SpawnEffect(enemy);
                    break;
                case StatType.DamageAllEnemies:
                    foreach (BattleScript enemy in LivingEnemies()) enemy.TakeDamage((int)amount, player);
                    break;
                case StatType.Heal: player.health = Mathf.Min(player.health + (int)amount, player.maxHealth); break;
                case StatType.Shield: player.shield += (int)amount; break;
                case StatType.Elixir: player.elixir = Mathf.Min(player.elixir + amount, player.maxElixir); break;
                
            }
            if (owner != null) owner.OnCardPlayed(this, definition);
            else Destroy(gameObject);
        }
    }

    private void SpawnEffect(BattleScript target)
    {
        effectSpawn = Instantiate(effectPrefab);
        effectSpawn.GetComponent<Effect>().SetValues(stat, target, amount, effectDuration);
    }

    private static List<BattleScript> LivingEnemies()
    {
        return BattleScript.FindFighters("Enemy").FindAll(enemy => enemy.health > 0);
    }
    
    
    


}
