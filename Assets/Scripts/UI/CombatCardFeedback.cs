using System;
using System.Globalization;
using System.Text;
using TMPro;
using UnityEngine;

// This component owns presentation only. It never writes to a fighter or an Effect.
[DefaultExecutionOrder(700)]
public sealed class CombatCardFeedback : MonoBehaviour
{
    [Serializable]
    public sealed class TargetView
    {
        public BattleScript actor;
        public RectTransform anchor;
        public CardBuffBadge[] badges;
    }
    [Header("Scene References")]
    public PlayerStatusView playerView;
    public BattleScript player;
    public RectTransform popupLayer;
    public TMP_Text popupTemplate;
    public RectTransform tooltip;
    public TMP_Text tooltipText;
    public TargetView[] targets;
    [Header("Floating Values")]
    [Range(8, 48)] public int poolSize = 24;
    [Min(.1f)] public float popupDuration = .8f;
    public float popupRise = 28;
    public Vector2 popupOffset = new(0, 22);
    [Min(.05f)] public float numberDuration = .2f;
    [Range(0, .25f)] public float numberPunch = .1f;
    [Min(.1f)] public float instantHighlightDuration = .55f;
    public Color bonusColor = new(.47f, .81f, 1);
    public Color activeColor = new(.6f, .85f, 1);
    public Color healColor = new(.58f, .88f, .49f);
    public Color elixirColor = new(.78f, .63f, 1);
    public Color costColor = new(1, .65f, .46f);
    public Color debuffColor = new(.78f, .64f, .96f);

    private sealed class Popup
    {
        public TMP_Text text;
        public RectTransform anchor;
        public float age;
        public int lane;
        public Color color;
        public bool active;
    }
    private sealed class NumberState
    {
        public StatType stat;
        public TMP_Text text;
        public Color originalColor;
        public Vector3 originalScale;
        public float from, shown, age = 10, highlight;
        public Color flashColor;
    }
    private Popup[] pool;
    private NumberState[] numbers;
    private int nextPopup;
    private bool ended;
    private CardBuffBadge hovered;
    private float tooltipAge;
    private readonly StringBuilder tooltipBuilder = new(256);
    public int NotificationCount { get; private set; }
    public int ActivePopupCount { get; private set; }
    public bool Ended => ended;
    public bool IsReady => playerView != null && player != null && popupLayer != null && popupTemplate != null &&
        tooltip != null && tooltipText != null && targets != null;

    private void OnEnable()
    {
        CardEffectFeedback.Changed += OnFeedback;
        BattleScript.OnBattleFinished += OnFinished;
        Canvas.willRenderCanvases += RefreshPopupLayout;
        ended = false;
    }

    private void Start()
    {
        if (!IsReady) { Debug.LogError("Assign the authored card feedback hierarchy.", this); enabled = false; return; }
        pool = new Popup[Mathf.Clamp(poolSize, 8, 48)];
        for (int i = 0; i < pool.Length; i++)
        {
            TMP_Text text = Instantiate(popupTemplate, popupLayer);
            text.name = "F" + (i + 1).ToString("00"); text.gameObject.SetActive(false);
            pool[i] = new Popup { text = text };
        }
        popupTemplate.gameObject.SetActive(false); tooltip.gameObject.SetActive(false);
        var types = new[] { StatType.Damage, StatType.AttackSpeed, StatType.Shield, StatType.Heal, StatType.Elixir };
        numbers = new NumberState[types.Length];
        for (int i = 0; i < types.Length; i++)
        {
            TMP_Text text = playerView.ValueLabel(types[i]);
            numbers[i] = new NumberState { stat = types[i], text = text, originalColor = text.color,
                originalScale = text.rectTransform.localScale, shown = DisplayValue(types[i]) };
        }
    }

    private float DisplayValue(StatType stat) => CardEffectFeedback.Read(player, stat);

    private void OnFeedback(CardEffectFeedback change)
    {
        if (ended || pool == null || change.Target == null || change.Target.gameObject.scene != gameObject.scene) return;
        if (change.Phase == CardFeedbackPhase.Removed) return;
        NotificationCount++;
        bool isPlayer = change.Target == player;
        bool cost = change.Phase == CardFeedbackPhase.Cost;
        bool expired = change.Phase == CardFeedbackPhase.Expired;
        float delta = change.After - change.Before;
        Color tint = cost ? costColor : change.Stat == StatType.Heal ? healColor :
            change.Stat == StatType.Elixir || change.Stat == StatType.ElixirRegen ? elixirColor :
            change.Stat == StatType.EnemySlow ? debuffColor : bonusColor;
        if (isPlayer)
        {
            StatType channel = change.Stat == StatType.ElixirRegen ? StatType.Elixir : change.Stat;
            foreach (var number in numbers)
                if (number.stat == channel)
                {
                    number.from = number.shown; number.age = 0;
                    number.highlight = expired ? numberDuration : instantHighlightDuration;
                    number.flashColor = expired ? number.originalColor : tint;
                }
        }
        if (expired) return;

        string value;
        if (cost) value = Signed(delta) + (change.Stat == StatType.Heal ? " HP" : " Elixir");
        else switch (change.Stat)
        {
            case StatType.AttackSpeed:
                if (playerView.DisplaysAttackInterval)
                { value = Signed(delta) + "s Interval"; break; }
                delta = CardEffectFeedback.AttacksPerSecond(change.After) - CardEffectFeedback.AttacksPerSecond(change.Before);
                value = (delta >= 0 ? "+" : "") + delta.ToString("0.00", CultureInfo.InvariantCulture) + " ATK/s"; break;
            case StatType.Heal: value = delta > 0 ? Signed(delta) + " HP" : "Full HP"; break;
            case StatType.Elixir: value = delta > 0 ? Signed(delta) : "Full Elixir"; break;
            case StatType.ExtraHits: value = Signed(delta) + " Hits"; break;
            case StatType.ElixirRegen: value = Signed(delta) + "/s"; break;
            case StatType.EnemySlow: value = "Slowed"; break;
            default: value = Signed(delta); break;
        }
        RectTransform anchor = null;
        if (isPlayer)
        {
            StatType slot = change.Stat == StatType.ExtraHits ? StatType.Damage :
                change.Stat == StatType.ElixirRegen ? StatType.Elixir : change.Stat;
            anchor = playerView.ValueLabel(slot)?.rectTransform;
            if (change.Stat == StatType.Heal || change.Stat == StatType.Elixir)
            {
                var bar = change.Stat == StatType.Heal ? playerView.Health : playerView.Elixir;
                float maximum = change.Stat == StatType.Heal ? player.maxHealth : player.maxElixir;
                bar.FlashChange(change.Before, change.After, maximum, tint, instantHighlightDuration);
            }
        }
        else foreach (var view in targets) if (view.actor == change.Target) { anchor = view.anchor; break; }
        if (anchor != null) Show(anchor, value, tint);
    }

    private void Show(RectTransform anchor, string value, Color tint)
    {
        Popup popup = pool[nextPopup]; nextPopup = (nextPopup + 1) % pool.Length;
        int lane = 0;
        for (int i = 0; i < pool.Length; i++)
            if (pool[i] != popup && pool[i].active && pool[i].anchor == anchor) lane++;
        popup.anchor = anchor; popup.age = 0; popup.color = tint;
        popup.lane = lane % 3; popup.active = true;
        popup.text.text = value; popup.text.color = tint; popup.text.gameObject.SetActive(true);
        Position(popup);
    }

    private void Position(Popup popup)
    {
        Vector3 local = popupLayer.InverseTransformPoint(popup.anchor.TransformPoint(popup.anchor.rect.center));
        float progress = Mathf.Clamp01(popup.age / Mathf.Max(.1f, popupDuration));
        popup.text.rectTransform.anchoredPosition = (Vector2)local + popupOffset +
            new Vector2(popup.lane % 2 * 12, popup.lane * 24 + popupRise * (1 - (1 - progress) * (1 - progress)));
        Color tint = popup.color;
        tint.a *= 1 - Mathf.SmoothStep(0, 1, Mathf.InverseLerp(.5f, 1, progress));
        popup.text.color = tint;
    }

    private void RefreshPopupLayout()
    {
        if (pool == null || popupLayer == null) return;
        foreach (var popup in pool) if (popup.active && popup.anchor != null) Position(popup);
        if (hovered != null && tooltip.gameObject.activeSelf) PositionTooltip();
    }

    private void LateUpdate()
    {
        if (pool == null || player == null) return;
        float delta = TimeTickSystem.Active != null && TimeTickSystem.Active.IsStarted ? Time.deltaTime : 0;
        foreach (var view in targets) foreach (var badge in view.badges) badge.Tick(delta, ended);
        ActivePopupCount = 0;
        foreach (var popup in pool)
        {
            if (!popup.active) continue;
            popup.age += delta;
            if (popup.age >= popupDuration || popup.anchor == null || !popup.anchor.gameObject.activeInHierarchy)
            { popup.active = false; popup.text.gameObject.SetActive(false); continue; }
            ActivePopupCount++; Position(popup);
        }
        foreach (var number in numbers)
        {
            number.age += delta; number.highlight = Mathf.Max(0, number.highlight - delta);
            float target = DisplayValue(number.stat);
            float t = Mathf.Clamp01(number.age / Mathf.Max(.05f, numberDuration));
            number.shown = Mathf.Lerp(number.from, target, Mathf.SmoothStep(0, 1, t));
            // Resource numbers stay exact so cost previews always agree with the spendable value.
            if (number.stat == StatType.Damage || number.stat == StatType.Shield)
                number.text.SetText("{0}", Mathf.Round(number.shown));
            else if (number.stat == StatType.AttackSpeed) playerView.SetAttackSpeedDisplay(number.shown);
            bool active = !ended && HasEffect(number.stat == StatType.Elixir ? StatType.ElixirRegen : number.stat);
            number.text.color = active ? (number.stat == StatType.Elixir ? elixirColor : activeColor) :
                Color.Lerp(number.originalColor, number.flashColor, Mathf.Clamp01(number.highlight / .15f));
            number.text.rectTransform.localScale = number.originalScale * (1 + numberPunch * Mathf.Sin(t * Mathf.PI));
        }
        if (hovered != null)
        {
            tooltipAge += Time.unscaledDeltaTime;
            if (tooltipAge >= .1f) { tooltipAge = 0; RefreshTooltip(); }
        }
    }

    public bool HasEffect(StatType stat)
    {
        var effects = Effect.ActiveEffects;
        for (int i = 0; i < effects.Count; i++)
        {
            var effect = effects[i];
            if (effect != null && effect.IsApplied && effect.target == player && effect.effectType == stat) return true;
        }
        return false;
    }

    public void ShowTooltip(CardBuffBadge badge)
    {
        hovered = badge; tooltipAge = 0; RefreshTooltip();
    }
    public void HideTooltip(CardBuffBadge badge)
    {
        if (hovered != badge) return;
        hovered = null; if (tooltip != null) tooltip.gameObject.SetActive(false);
    }
    private void RefreshTooltip()
    {
        if (hovered == null || hovered.StackCount == 0) return;
        tooltipBuilder.Clear(); tooltipBuilder.Append(Title(hovered.stat));
        int count = 0;
        foreach (var effect in Effect.ActiveEffects)
            if (hovered.Matches(effect))
            {
                count++;
                tooltipBuilder.Append('\n').Append(EffectDescription(effect)).Append("  |  ")
                    .Append(effect.RemainingSeconds.ToString("0.0", CultureInfo.InvariantCulture)).Append('s');
            }
        tooltipText.text = tooltipBuilder.ToString();
        tooltip.sizeDelta = new Vector2(300, 36 + 23 * count);
        PositionTooltip(); tooltip.gameObject.SetActive(true);
    }
    private void PositionTooltip()
    {
        RectTransform root = (RectTransform)tooltip.parent;
        RectTransform row = (RectTransform)hovered.transform.parent;
        Vector2 point = root.InverseTransformPoint(row.TransformPoint(new Vector3(row.rect.xMax, row.rect.yMax)));
        point += new Vector2(12, 0);
        point.x = Mathf.Clamp(point.x, root.rect.xMin + 8, root.rect.xMax - tooltip.sizeDelta.x - 8);
        point.y = Mathf.Clamp(point.y, root.rect.yMin + tooltip.sizeDelta.y + 8, root.rect.yMax - 8);
        tooltip.anchoredPosition = point;
    }
    public static string Title(StatType stat) => stat switch
    {
        StatType.Damage => "Damage", StatType.AttackSpeed => "Attack speed", StatType.ExtraHits => "Extra hits",
        StatType.ElixirRegen => "Elixir regeneration", StatType.EnemySlow => "Slowed", _ => stat.ToString()
    };
    private static string EffectDescription(Effect effect) => effect.effectType switch
    {
        StatType.AttackSpeed => "Interval " + Signed(-effect.amount) + "s",
        StatType.EnemySlow => "Interval " + Signed(effect.amount) + "s",
        StatType.ExtraHits => Signed(effect.amount) + " hits per attack",
        StatType.ElixirRegen => Signed(effect.amount) + " Elixir/s",
        _ => Signed(effect.amount) + " damage"
    };
    private static string Signed(float value) => (value >= 0 ? "+" : "") + value.ToString("0.##", CultureInfo.InvariantCulture);

    private void OnFinished(BattleScript actor, bool victory)
    {
        if (actor != player) return;
        ended = true; Clear();
    }
    private void Clear()
    {
        hovered = null;
        if (tooltip != null) tooltip.gameObject.SetActive(false);
        if (pool != null) foreach (var popup in pool) { popup.active = false; popup.text.gameObject.SetActive(false); }
        ActivePopupCount = 0;
        if (targets != null) foreach (var view in targets) foreach (var badge in view.badges) if (badge != null) badge.Clear();
        if (numbers != null) foreach (var number in numbers)
        {
            number.age = 10; number.highlight = 0;
            if (number.text != null)
            {
                number.text.color = number.originalColor; number.text.rectTransform.localScale = number.originalScale;
                if (player != null)
                {
                    number.shown = number.from = DisplayValue(number.stat);
                    if (number.stat == StatType.Damage || number.stat == StatType.Shield) number.text.SetText("{0}", Mathf.Round(number.shown));
                    else if (number.stat == StatType.AttackSpeed) playerView.SetAttackSpeedDisplay(number.shown);
                }
            }
        }
        if (playerView != null) { playerView.Health?.ClearFeedback(); playerView.Elixir?.ClearFeedback(); }
    }
    private void OnDisable()
    {
        CardEffectFeedback.Changed -= OnFeedback;
        BattleScript.OnBattleFinished -= OnFinished;
        Canvas.willRenderCanvases -= RefreshPopupLayout;
        Clear();
    }
    private void OnDestroy()
    {
        if (pool != null) foreach (var popup in pool) if (popup.text != null) Destroy(popup.text.gameObject);
    }
}
