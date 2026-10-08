using System;
using UnityEngine;

public enum CardFeedbackPhase { Cost, Applied, Expired, Removed }

// Values are captured at the mutation site, never inferred from unrelated HUD changes.
public readonly struct CardEffectFeedback
{
    public readonly BattleScript Target;
    public readonly StatType Stat;
    public readonly float Before, After;
    public readonly CardFeedbackPhase Phase;
    public readonly Effect Instance;

    public CardEffectFeedback(BattleScript target, StatType stat, float before, float after,
        CardFeedbackPhase phase, Effect instance = null)
    {
        Target = target; Stat = stat; Before = before; After = after; Phase = phase; Instance = instance;
    }

    public static event Action<CardEffectFeedback> Changed;
    public static void Publish(CardEffectFeedback feedback) => Changed?.Invoke(feedback);

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Reset() => Changed = null;

    public static float Read(BattleScript target, StatType stat) => stat switch
    {
        StatType.Damage => target.attackDmg,
        StatType.AttackSpeed or StatType.EnemySlow => target.attackSpd,
        StatType.Heal => target.health,
        StatType.Shield => target.shield,
        StatType.Elixir => target.elixir,
        StatType.ExtraHits => target.hitsPerAttack,
        StatType.ElixirRegen => target.elixirRegen,
        _ => 0
    };

    public static float AttacksPerSecond(float interval) => 1f / Mathf.Max(.01f, interval);
}
