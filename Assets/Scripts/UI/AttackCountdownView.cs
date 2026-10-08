using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Presents the real attack clock without scheduling attacks or changing fighter attributes.
public sealed class AttackCountdownView : MonoBehaviour
{
    [Header("Scene References")]
    [SerializeField] private BattleScript actor;
    [SerializeField] private TimeTickSystem clock;
    [SerializeField] private BattleHud hud;
    [SerializeField] private Image fill;
    [SerializeField] private TMP_Text seconds;
    [SerializeField] private CanvasGroup visibility;
    [Header("Presentation")]
    [SerializeField] private Color normalColor = new(.9f, .68f, .34f);
    [SerializeField] private Color imminentColor = new(1f, .43f, .2f);
    [SerializeField] private Color attackColor = new(1f, .92f, .7f);
    [SerializeField, Min(0)] private float imminentSeconds = .65f;
    [SerializeField, Min(.01f)] private float attackFlashDuration = .16f;
    [SerializeField, Range(0, 1)] private float waitingOpacity = .55f;
    [SerializeField] private bool smoothBetweenTicks = true;
    public BattleScript Actor => actor;
    public float RemainingSeconds { get; private set; }
    public bool IsReady => actor != null && clock != null && hud != null && fill != null && seconds != null && visibility != null;
    private int displayedTenth = -1;
    private float flash;
    private bool ended;

    private void OnEnable()
    {
        ended = clock != null && clock.TickDeltaSeconds > 0 && !clock.IsStarted && hud != null && !hud.CountdownArmed;
        displayedTenth = -1; flash = 0;
        BattleScript.OnAttackPerformed += OnAttack;
        BattleScript.OnBattleFinished += OnBattleFinished;
    }

    private void OnDisable()
    {
        BattleScript.OnAttackPerformed -= OnAttack;
        BattleScript.OnBattleFinished -= OnBattleFinished;
    }

    private void OnAttack(BattleScript attacker, BattleScript target)
    { if (attacker == actor && target != null && !ended) flash = attackFlashDuration; }

    private void OnBattleFinished(BattleScript player, bool victory)
    { if (player != null && player.gameObject.scene == gameObject.scene) ended = true; }

    private void LateUpdate()
    {
        if (!IsReady) return;
        bool alive = !ended && actor.isActiveAndEnabled && actor.health > 0;
        visibility.alpha = !alive ? 0 : clock.IsStarted ? 1 : waitingOpacity;
        if (!alive) return;
        float interpolation = smoothBetweenTicks && clock.IsStarted && clock.isActiveAndEnabled ? clock.SecondsSinceLastTick : 0;
        RemainingSeconds = Mathf.Max(0, actor.SecondsUntilNextAttack - interpolation);
        float fraction = Mathf.Clamp01(RemainingSeconds / actor.AttackIntervalSeconds);
        var end = fill.rectTransform.anchorMax;
        if (!Mathf.Approximately(end.x, fraction)) fill.rectTransform.anchorMax = new Vector2(fraction, end.y);
        int tenth = Mathf.CeilToInt(RemainingSeconds * 10);
        if (tenth != displayedTenth)
        { displayedTenth = tenth; seconds.SetText("{0:1}s", tenth / 10f); }
        if (clock.IsStarted) flash = Mathf.Max(0, flash - Time.deltaTime);
        var tint = clock.IsStarted && RemainingSeconds <= imminentSeconds ? imminentColor : normalColor;
        fill.color = Color.Lerp(tint, attackColor, Mathf.Clamp01(flash / Mathf.Max(.01f, attackFlashDuration)));
    }
}
