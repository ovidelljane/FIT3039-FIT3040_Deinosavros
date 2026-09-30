using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

public sealed class BattleRunBridge : MonoBehaviour
{
    [SerializeField] private string battleSceneName = "Deinosavros";

    private void OnEnable()
    {
        SceneManager.sceneLoaded += HandleSceneLoaded;
    }

    private void OnDisable()
    {
        SceneManager.sceneLoaded -= HandleSceneLoaded;
    }

    private void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (scene.name != battleSceneName || RunSession.Instance == null)
        {
            return;
        }

        if (RunSession.Instance.TryConsumePendingModifier(out PendingEncounterModifier modifier))
        {
            ApplyModifier(modifier);
        }
    }

    private static void ApplyModifier(PendingEncounterModifier modifier)
    {
        List<BattleScript> players = BattleScript.FindFighters("Player");
        BattleScript player = players.Count > 0 ? players[0] : null;

        switch (modifier.effectType)
        {
            case OverworldEffectType.ReduceEnemyStartingHealth:
                foreach (BattleScript enemy in BattleScript.FindFighters("Enemy"))
                {
                    enemy.health = Mathf.Max(1, Mathf.CeilToInt(enemy.health * modifier.magnitude / 100f));
                }
                break;
            case OverworldEffectType.ImprovePlayerAttackSpeed:
                if (player != null) player.attackSpd = Mathf.Max(1, player.attackSpd - modifier.magnitude);
                break;
            case OverworldEffectType.GrantStartingShield:
                if (player != null) player.shield += modifier.magnitude;
                break;
            case OverworldEffectType.IncreaseStartingElixir:
                if (player != null)
                {
                    player.maxElixir += modifier.magnitude;
                    player.elixir = Mathf.Min(player.maxElixir, player.elixir + modifier.magnitude);
                }
                break;
        }
    }
}
