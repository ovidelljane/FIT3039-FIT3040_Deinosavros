using UnityEngine;

// One encounter-local passive. Damage from its own punishment cannot charge another punishment.
public sealed class DivinePunishment : MonoBehaviour
{
    [Min(1)] public int damageInstancesPerStrike = 4;
    [Min(0)] public int strikeDamage = 70;
    public int DamageInstances { get; private set; }
    public int Strikes { get; private set; }
    private BattleScript player;
    private bool resolving;
    private void OnEnable() { player = GetComponent<BattleScript>(); BattleScript.OnDamageReceived += OnDamage; }
    private void OnDisable() => BattleScript.OnDamageReceived -= OnDamage;

    private void OnDamage(BattleScript target, BattleScript source, int healthLost, int shieldLost)
    {
        if (resolving || source != player || player == null || player.health <= 0 || target == null ||
            !target.CompareTag("Enemy") || target.gameObject.scene != gameObject.scene || healthLost + shieldLost <= 0) return;
        if (++DamageInstances < Mathf.Max(1, damageInstancesPerStrike)) return;
        DamageInstances = 0;
        var candidates = BattleScript.FindFighters("Enemy");
        candidates.RemoveAll(enemy => enemy.health <= 0 || enemy.gameObject.scene != gameObject.scene);
        if (candidates.Count == 0) return;
        resolving = true;
        try { Strikes++; candidates[Random.Range(0, candidates.Count)].TakeDamage(strikeDamage, player); }
        finally { resolving = false; }
    }
}
