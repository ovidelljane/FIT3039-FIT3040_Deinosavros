using UnityEngine;

[DisallowMultipleComponent, RequireComponent(typeof(BattleScript))]
public sealed class BossCombat : MonoBehaviour
{
    [Min(1)] public int doubleStrikeEvery = 3;
    [Min(1)] public int powerIncreaseEvery = 5;
    [Min(0)] public int powerIncrease = 5;
    public int AttackCount { get; private set; }

    public int PrepareAttack()
    {
        var actor = GetComponent<BattleScript>();
        AttackCount++;
        if (AttackCount % Mathf.Max(1, powerIncreaseEvery) == 0) actor.attackDmg += powerIncrease;
        return AttackCount % Mathf.Max(1, doubleStrikeEvery) == 0 ? 2 : Mathf.Max(1, actor.hitsPerAttack);
    }
}
