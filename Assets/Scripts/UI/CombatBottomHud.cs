using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Combat presentation only: the health/shield arc and elixir bar along the bottom of the screen,
// with the remaining player stats following the player like the enemy panels.
// The hierarchy is authored in the combat scene; this only updates values. BattleScript
// remains the source of truth, and PlayerStatusView keeps the cost preview and feedback anchors.
[ExecuteAlways]
public sealed class CombatBottomHud : MonoBehaviour
{
    // Arc geometry baked into the sprites; must match ArtSource/BattleHUD/gen_hud.py.
    public const float ArcWidth = 1800, ArcHeight = 900, ArcInner = 844, ArcOuter = 892;
    public const float ArcLeft = 125, ArcRight = 55, DomeRadius = 930;
    private const float FillStart = (180 - ArcLeft) / 180, FillSpan = (ArcLeft - ArcRight) / 180;

    [Header("Scene References")]
    [SerializeField] private BattleHud hud;
    [SerializeField] private PlayerStatusView status;
    [SerializeField] private RectTransform arcCentre;
    [SerializeField] private Image healthFill, healthPreview, shieldFill;
    [SerializeField] private RectTransform shieldBadge;
    [SerializeField] private TMP_Text healthText, shieldText, elixirText;
    [SerializeField] private RectTransform elixirFill;
    [SerializeField] private Image elixirPreview;
    [Header("Player Panel")]
    [SerializeField] private RectTransform playerPanel;
    [Tooltip("Optional point above the player's head. When empty, the top of the player's rendered body is used.")]
    [SerializeField] private Transform headAnchor;
    [SerializeField] private float playerPanelGap = 8;
    [Header("Colours")]
    [SerializeField] private Color affordableColor = new(1f, .91f, .74f);
    [SerializeField] private Color unaffordableColor = new(1f, .27f, .21f);

    private BattleScript player;
    private Renderer[] playerRenderers;
    private Camera worldCamera;
    private float shownHealth = float.NaN, shownMaxHealth, shownElixir = float.NaN, shownMaxElixir;
    private int shownShield = int.MinValue;

    // The circle centre shared by the arc sprites, the dome and the hand fan.
    public RectTransform ArcCentre => arcCentre;
    public bool IsReady => status != null && arcCentre != null && healthFill != null && healthPreview != null &&
        shieldFill != null && shieldBadge != null && healthText != null && shieldText != null &&
        elixirText != null && elixirFill != null && elixirPreview != null && playerPanel != null;

    private void LateUpdate()
    {
        if (!IsReady) return;
        if (player == null || !player.isActiveAndEnabled)
        {
            player = hud != null && hud.Deck != null && hud.Deck.Player != null ? hud.Deck.Player : BattleHud.FindPlayer(gameObject.scene);
            playerRenderers = player != null ? player.GetComponentsInChildren<Renderer>() : null;
        }
        if (player == null) return;
        UpdateBars();
        FollowPlayer();
    }

    private void UpdateBars()
    {
        float hp = Mathf.Clamp(player.health, 0, Mathf.Max(0, player.maxHealth)), maxHp = Mathf.Max(1, player.maxHealth);
        float energy = Mathf.Clamp(player.elixir, 0, Mathf.Max(0, player.maxElixir)), maxEnergy = Mathf.Max(.01f, player.maxElixir);
        var card = Application.isPlaying ? status.PreviewCard : null;
        bool preview = hp > 0 && card != null && card.isActiveAndEnabled && card.Cost > 0;
        bool healthCost = preview && card.UsesHealthCost;
        bool affordable = !preview || card.MeetsResourceRequirement;
        int cost = preview ? card.Cost : 0;
        float pulseAlpha = .5f + .2f * Mathf.Sin(Time.unscaledTime * 4f);

        // Health arc: when a card costs health, the chunk it would spend pulses behind the red.
        SetActive(healthPreview.gameObject, healthCost);
        if (healthCost)
        {
            healthPreview.fillAmount = ArcFill(hp / maxHp);
            healthPreview.color = Pulse(affordable && hp > cost, pulseAlpha);
        }
        float afterHp = healthCost ? Mathf.Max(0, hp - cost) : hp;
        healthFill.fillAmount = afterHp > 0 ? ArcFill(afterHp / maxHp) : 0;

        // Shield covers the start of the health arc, never past the current health.
        int shield = Mathf.Max(0, player.shield);
        float shieldShare = Mathf.Min(shield, afterHp) / maxHp;
        shieldFill.fillAmount = shieldShare > 0 ? ArcFill(shieldShare) : 0;
        SetActive(shieldBadge.gameObject, shield > 0);
        if (shield > 0) shieldBadge.anchoredPosition = OnArc(shieldShare);

        // Elixir bar and its cost preview.
        float end = energy / maxEnergy, start = Mathf.Max(0, energy - cost) / maxEnergy;
        bool elixirPreviewOn = preview && !healthCost && end > start;
        float shown = elixirPreviewOn ? start : end;
        SetSpan(elixirFill, 0, shown);
        SetActive(elixirFill.gameObject, shown > 0);
        SetActive(elixirPreview.gameObject, elixirPreviewOn);
        if (elixirPreviewOn) { SetSpan((RectTransform)elixirPreview.transform, start, end); elixirPreview.color = Pulse(affordable, pulseAlpha); }

        if (hp != shownHealth || maxHp != shownMaxHealth)
        { shownHealth = hp; shownMaxHealth = maxHp; healthText.text = $"{hp:0} / {maxHp:0}"; }
        if (energy != shownElixir || maxEnergy != shownMaxElixir)
        { shownElixir = energy; shownMaxElixir = maxEnergy; elixirText.text = $"{energy:0.0} / {maxEnergy:0}"; }
        if (shield != shownShield) { shownShield = shield; shieldText.text = shield.ToString(); }
    }

    private void FollowPlayer()
    {
        if (worldCamera == null) worldCamera = Camera.main;
        if (worldCamera == null) return;
        Vector3 viewport = worldCamera.WorldToViewportPoint(headAnchor != null ? headAnchor.position : HeadPoint());
        var anchor = new Vector2(viewport.x, viewport.y);
        if (playerPanel.anchorMin != anchor || playerPanel.anchorMax != anchor)
            playerPanel.anchorMin = playerPanel.anchorMax = anchor;
        var offset = new Vector2(0, playerPanelGap);
        if (playerPanel.anchoredPosition != offset) playerPanel.anchoredPosition = offset;
    }

    // Top-centre of the player's visible body; effects and particles are ignored.
    private Vector3 HeadPoint()
    {
        bool found = false; Bounds bounds = default;
        if (playerRenderers != null)
            foreach (var body in playerRenderers)
            {
                if (body == null || !body.enabled || !body.gameObject.activeInHierarchy ||
                    body is ParticleSystemRenderer || body is TrailRenderer || body is LineRenderer) continue;
                if (!found) { bounds = body.bounds; found = true; } else bounds.Encapsulate(body.bounds);
            }
        return found ? new Vector3(bounds.center.x, bounds.max.y, bounds.center.z) : player.transform.position + Vector3.up * 2f;
    }

    private Color Pulse(bool canAfford, float alpha)
    { Color tint = canAfford ? affordableColor : unaffordableColor; tint.a = alpha; return tint; }

    public static float ArcFill(float share) => FillStart + FillSpan * Mathf.Clamp01(share);

    // Point on the middle of the band, `share` of the way from the left end to the right end,
    // relative to the arc centre.
    public static Vector2 OnArc(float share)
    {
        float angle = Mathf.Lerp(ArcLeft, ArcRight, Mathf.Clamp01(share)) * Mathf.Deg2Rad;
        float radius = (ArcInner + ArcOuter) / 2;
        return new Vector2(Mathf.Cos(angle) * radius, Mathf.Sin(angle) * radius);
    }

    private static void SetActive(GameObject target, bool value) { if (target.activeSelf != value) target.SetActive(value); }

    private static void SetSpan(RectTransform rect, float left, float right)
    {
        var min = new Vector2(left, 0); var max = new Vector2(right, 1);
        if (rect.anchorMin == min && rect.anchorMax == max) return;
        rect.anchorMin = min; rect.anchorMax = max; rect.offsetMin = rect.offsetMax = Vector2.zero;
    }
}
