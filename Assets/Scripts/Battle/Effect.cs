using UnityEngine;


public class Effect : MonoBehaviour
{

    public StatType effectType;
    public float amount;
    public float effectDur;
    public BattleScript target;
    
    private double elapsed;
    private bool applied;

    private void OnEnable() => TimeTickSystem.OnTick += HandleTick;
    private void OnDisable()
    {
        TimeTickSystem.OnTick -= HandleTick;
        RemoveEffect();
    }
    
    void HandleTick()
    {
        if (!applied || TimeTickSystem.Active == null) return;
        elapsed += TimeTickSystem.Active.TickDeltaSeconds;
        if (elapsed + .000001f < effectDur) return;
        RemoveEffect();
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

    private void RemoveEffect()
    {
        if (!applied) return;
        applied = false; ChangeStat(-1);
    }
    public void SetValues(StatType type, BattleScript targ, float amoun, float duration)
    {
        RemoveEffect();
        effectType = type;
        target = targ;
        amount = amoun;
        effectDur = duration;
        elapsed = 0;
        if (target == null || duration <= 0) { Destroy(gameObject); return; }
        applied = true; ChangeStat(1);
    }
}
