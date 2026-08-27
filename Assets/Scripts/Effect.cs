using UnityEngine;


public class Effect : MonoBehaviour
{

    public StatType effectType;
    public int amount;
    public float effectDur;
    public BattleScript target;
    
    private int _currentTickCount = 0;
    private 
    
    void Start()
    {

        TimeTickSystem.OnTick += HandleTick;
        
    }
    
    void HandleTick()
    {
        if (_currentTickCount == 1)
        {
            switch (effectType)
            {
                case StatType.Damage: target.attackDmg += amount; break;
                case StatType.AttackSpeed: target.attackSpd -= amount; break;
            }
        }
        _currentTickCount++;
        if (_currentTickCount >= effectDur * 10)
        {
            switch (effectType)
            {
                case StatType.Damage: target.attackDmg -= amount; break;
                case StatType.AttackSpeed: target.attackSpd += amount; break;
            }
            Destroy(this.gameObject);
        }
    }

    void OnDestroy()
    {
        TimeTickSystem.OnTick -= HandleTick;
    }
    public void SetValues(StatType type, BattleScript targ, int amoun, float duration)
    {
        effectType = type;
        target = targ;
        amount = amoun;
        effectDur = duration;
    }
}
