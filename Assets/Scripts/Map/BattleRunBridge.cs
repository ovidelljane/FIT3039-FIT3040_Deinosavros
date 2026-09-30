using UnityEngine;

// Applies run health, layer difficulty and the next-combat offering after receiver readiness.
public sealed class BattleRunBridge : MonoBehaviour
{
    private RunSession owner;
    private BattleScript trackedPlayer;
    private string initializedEncounterId;

    private void OnEnable()
    {
        owner = GetComponent<RunSession>();
        if (owner != null && RunSession.Instance != null && RunSession.Instance != owner) enabled = false;
    }

    private void Update()
    {
        if (owner != null && owner == RunSession.Instance && trackedPlayer != null &&
            owner.Progress?.Phase == MapProgressPhase.InEncounter)
            owner.CapturePlayerStats(trackedPlayer);
    }

    public bool InitializeConfirmedEncounter(string id, BattleScript player)
    {
        var session = RunSession.Instance;
        if (owner != session || session?.Progress?.Phase != MapProgressPhase.InEncounter ||
            session.Progress.CurrentEncounter?.EncounterId != id || !session.Progress.CurrentEncounter.IsCombat ||
            initializedEncounterId == id || player == null || !player.isActiveAndEnabled) return false;
        initializedEncounterId = id;
        trackedPlayer = player;
        session.ConfigureBattleDefaults(player);
        session.ApplyPlayerStats(player);
        ApplyEnemyDifficulty(session.Rules, session.Progress.CurrentEncounterLayer, player);
        if (session.TryConsumePendingModifier(out var modifier)) ApplyModifier(modifier, player);
        session.CapturePlayerStats(player);
        return true;
    }

    private static void ApplyEnemyDifficulty(RunBalance rules, int layer, BattleScript player)
    {
        foreach (var enemy in BattleScript.FindFighters("Enemy"))
        {
            if (enemy.gameObject.scene != player.gameObject.scene || enemy.health <= 0) continue;
            int baseMaxHealth = Mathf.Max(1, enemy.maxHealth);
            float healthFraction = Mathf.Clamp01((float)enemy.health / baseMaxHealth);
            enemy.maxHealth = rules.EnemyMaxHealthAtLayer(baseMaxHealth, layer);
            enemy.health = Mathf.Clamp(Mathf.RoundToInt(enemy.maxHealth * healthFraction), 1, enemy.maxHealth);
            enemy.attackDmg = rules.EnemyDamageAtLayer(enemy.attackDmg, layer);
        }
    }

    private void ApplyModifier(PendingEncounterModifier modifier, BattleScript player)
    {
        switch (modifier.effectType)
        {
            case OverworldEffectType.ReduceEnemyStartingHealth:
                foreach (var enemy in BattleScript.FindFighters("Enemy"))
                    if (enemy.gameObject.scene == player.gameObject.scene)
                        enemy.health = Mathf.Max(1, Mathf.CeilToInt(enemy.health * modifier.magnitude / 100f));
                break;
            case OverworldEffectType.ImprovePlayerAttackSpeed:
                player.attackSpd = Mathf.Max(owner.MinimumOfferingAttackInterval, player.attackSpd - modifier.magnitude);
                break;
            case OverworldEffectType.GrantStartingShield:
                player.shield += modifier.magnitude;
                break;
            case OverworldEffectType.IncreaseStartingElixir:
                player.maxElixir += modifier.magnitude;
                player.elixir = Mathf.Min(player.maxElixir, player.elixir + modifier.magnitude);
                break;
        }
    }
}
