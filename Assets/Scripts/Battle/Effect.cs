using UnityEngine;
using System.Collections.Generic;


public class Effect : MonoBehaviour
{

    public StatType effectType;
    public float amount;
    public float effectDur;
    public BattleScript target;
    [Tooltip("Ascending HP percentage thresholds; each comparison is strictly below.")]
    public Vector3 healthThresholds = new(50, 75, 100);
    public Vector3 healthBonuses = new(70, 40, 15);
    [Header("Card Trigger")]
    public CardDefinition sourceCard;
    [Min(0)] public float stackIncrement;
    [Min(0)] public int stackLimit;
    [Header("Delayed Resource")]
    [Range(0, 100)] public float bonusHealthThreshold;
    [Min(0)] public float lowHealthBonus;
    public int TriggerStacks { get; private set; }
    public float CurrentMagnitude => amount + TriggerStacks * stackIncrement;
    public bool IsFromCard(CardDefinition card) => sourceCard == card || (sourceCard != null && card != null &&
        !string.IsNullOrEmpty(sourceCard.cardId) && sourceCard.cardId == card.cardId);
    public float PendingResourceAmount => amount + (target != null && target.maxHealth > 0 &&
        100f * target.health / target.maxHealth < bonusHealthThreshold ? lowHealthBonus : 0);
    
    private double elapsed;
    private bool applied;
    private static readonly List<Effect> activeEffects = new();
    public static IReadOnlyList<Effect> ActiveEffects => activeEffects;
    public bool IsApplied => applied;
    public float RemainingSeconds => applied ? Mathf.Max(0, effectDur - (float)elapsed) : 0;
    public float TotalSeconds => effectDur;
    public float CurrentHealthBonus => target == null || target.maxHealth <= 0 ? 0 :
        BonusAtHealth(100f * target.health / target.maxHealth, healthThresholds, healthBonuses);

    public static float BonusAtHealth(float percent, Vector3 thresholds, Vector3 bonuses) =>
        percent < thresholds.x ? bonuses.x : percent < thresholds.y ? bonuses.y : percent < thresholds.z ? bonuses.z : 0;

    private static float HealthBonus(BattleScript actor)
    {
        float bonus = 0;
        foreach (var effect in activeEffects)
            if (effect != null && effect.applied && effect.target == actor && effect.effectType == StatType.Cataclysm)
                bonus = Mathf.Max(bonus, effect.CurrentHealthBonus);
        return bonus;
    }

    public static float DamageMultiplier(BattleScript actor)
    {
        float reduction = 0;
        foreach (var effect in activeEffects)
            if (effect != null && effect.applied && effect.target == actor && effect.effectType == StatType.DamageReduction)
                reduction = Mathf.Max(reduction, effect.amount);
        return (1 + HealthBonus(actor) / 100f) * Mathf.Clamp01(1 - reduction / 100f);
    }

    public static float FrequencyMultiplier(BattleScript actor)
    {
        float frequency = 1 + HealthBonus(actor) / 100f;
        float momentum = 0;
        foreach (var effect in activeEffects)
            if (effect != null && effect.applied && effect.target == actor)
            {
                if (effect.effectType == StatType.FrequencySlow) frequency *= Mathf.Clamp(1 - effect.amount / 100f, .01f, 1);
                if (effect.effectType == StatType.CardMomentum) momentum = Mathf.Max(momentum, effect.CurrentMagnitude);
            }
        return frequency * (1 + momentum / 100f);
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetRegistry() => activeEffects.Clear();

    private void OnEnable()
    {
        TimeTickSystem.OnTick += HandleTick;
        DeckManager.CardPlayed += OnCardPlayed;
        BattleScript.OnBattleFinished += OnBattleFinished;
    }
    private void OnDisable()
    {
        TimeTickSystem.OnTick -= HandleTick;
        DeckManager.CardPlayed -= OnCardPlayed;
        BattleScript.OnBattleFinished -= OnBattleFinished;
        RemoveEffect(CardFeedbackPhase.Removed);
    }
    
    void HandleTick()
    {
        if (!applied || TimeTickSystem.Active == null || !TimeTickSystem.Active.IsStarted) return;
        elapsed += TimeTickSystem.Active.TickDeltaSeconds;
        if (elapsed + .000001f < effectDur) return;
        if (effectType == StatType.DelayedElixir && target != null && target.isActiveAndEnabled && target.health > 0)
            RestoreElixir(target, PendingResourceAmount);
        RemoveEffect(CardFeedbackPhase.Expired);
        Destroy(gameObject);
    }

    public static void RestoreElixir(BattleScript actor, float value)
    {
        float before = actor.elixir;
        actor.elixir = Mathf.Min(actor.maxElixir, before + Mathf.Max(0, value));
        CardEffectFeedback.Publish(new CardEffectFeedback(actor, StatType.Elixir, before, actor.elixir, CardFeedbackPhase.Applied));
    }

    public void Refresh(float value, float duration)
    {
        float before = CardEffectFeedback.Read(target, effectType);
        amount = value; effectDur = Mathf.Max(.01f, duration); elapsed = 0;
        float after = CardEffectFeedback.Read(target, effectType);
        if (!Mathf.Approximately(before, after))
            CardEffectFeedback.Publish(new CardEffectFeedback(target, effectType, before, after, CardFeedbackPhase.Applied, this));
    }

    private void OnCardPlayed(BattleScript actor, CardDefinition card)
    {
        if (!applied || effectType != StatType.CardMomentum || actor != target || card == null || IsFromCard(card) ||
            actor.health <= 0 || TriggerStacks >= stackLimit) return;
        float before = CardEffectFeedback.Read(actor, effectType);
        TriggerStacks++;
        CardEffectFeedback.Publish(new CardEffectFeedback(actor, effectType, before,
            CardEffectFeedback.Read(actor, effectType), CardFeedbackPhase.Applied, this));
    }

    private void OnBattleFinished(BattleScript actor, bool victory)
    {
        if (!applied || actor == null || target == null || actor.gameObject.scene != target.gameObject.scene) return;
        RemoveEffect(CardFeedbackPhase.Removed);
        Destroy(gameObject);
    }

    private void ChangeStat(float direction)
    {
        if (target == null) return;
        switch (effectType)
        {
            case StatType.Damage: target.attackDmg += (int)amount * (int)direction; break;
            case StatType.AttackSpeed: target.attackSpd -= amount * direction; break;
            case StatType.ExtraHits: target.hitsPerAttack += (int)amount * (int)direction; break;
            case StatType.EnemySlow: target.attackSpd += amount * direction; break;
            case StatType.ElixirRegen: target.elixirRegen += amount * direction; break;
        }
    }

    private void RemoveEffect(CardFeedbackPhase phase)
    {
        if (!applied) return;
        float before = target != null ? CardEffectFeedback.Read(target, effectType) : 0;
        applied = false; activeEffects.Remove(this); ChangeStat(-1);
        if (target != null) CardEffectFeedback.Publish(new CardEffectFeedback(target, effectType, before,
            CardEffectFeedback.Read(target, effectType), phase, this));
    }
    public void SetValues(StatType type, BattleScript targ, float amoun, float duration)
    {
        RemoveEffect(CardFeedbackPhase.Removed);
        effectType = type;
        target = targ;
        amount = amoun;
        effectDur = duration;
        elapsed = 0;
        TriggerStacks = 0;
        if (target == null || duration <= 0) { Destroy(gameObject); return; }
        float before = CardEffectFeedback.Read(target, effectType);
        applied = true; ChangeStat(1); activeEffects.Add(this);
        CardEffectFeedback.Publish(new CardEffectFeedback(target, effectType, before,
            CardEffectFeedback.Read(target, effectType), CardFeedbackPhase.Applied, this));
    }
}
