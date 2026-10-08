using UnityEngine;
using System.Collections.Generic;


public class Effect : MonoBehaviour
{

    public StatType effectType;
    public float amount;
    public float effectDur;
    public BattleScript target;
    
    private double elapsed;
    private bool applied;
    private static readonly List<Effect> activeEffects = new();
    public static IReadOnlyList<Effect> ActiveEffects => activeEffects;
    public bool IsApplied => applied;
    public float RemainingSeconds => applied ? Mathf.Max(0, effectDur - (float)elapsed) : 0;
    public float TotalSeconds => effectDur;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetRegistry() => activeEffects.Clear();

    private void OnEnable() => TimeTickSystem.OnTick += HandleTick;
    private void OnDisable()
    {
        TimeTickSystem.OnTick -= HandleTick;
        RemoveEffect(CardFeedbackPhase.Removed);
    }
    
    void HandleTick()
    {
        if (!applied || TimeTickSystem.Active == null) return;
        elapsed += TimeTickSystem.Active.TickDeltaSeconds;
        if (elapsed + .000001f < effectDur) return;
        RemoveEffect(CardFeedbackPhase.Expired);
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
        if (target == null || duration <= 0) { Destroy(gameObject); return; }
        float before = CardEffectFeedback.Read(target, effectType);
        applied = true; ChangeStat(1); activeEffects.Add(this);
        CardEffectFeedback.Publish(new CardEffectFeedback(target, effectType, before,
            CardEffectFeedback.Read(target, effectType), CardFeedbackPhase.Applied, this));
    }
}
