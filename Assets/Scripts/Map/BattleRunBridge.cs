using UnityEngine;
using UnityEngine.SceneManagement;

public sealed class BattleRunBridge : MonoBehaviour
{
    [SerializeField] private string battleSceneName = "Deinosavros";
    private BattleScript trackedPlayer;

    private void OnEnable()
    {
        SceneManager.sceneLoaded += HandleSceneLoaded;
    }

    private void OnDisable()
    {
        if (RunSession.Instance != null && trackedPlayer != null)
        {
            RunSession.Instance.CapturePlayerStats(trackedPlayer);
        }
        SceneManager.sceneLoaded -= HandleSceneLoaded;
    }

    private void Update()
    {
        if (trackedPlayer != null && RunSession.Instance != null)
        {
            RunSession.Instance.CapturePlayerStats(trackedPlayer);
        }
    }

    private void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (scene.name != battleSceneName || RunSession.Instance == null)
        {
            trackedPlayer = null;
            return;
        }

<<<<<<< Updated upstream
=======
        HideSacrificedCard(RunSession.Instance);
        GameObject playerObject = GameObject.FindGameObjectWithTag("Player");
        trackedPlayer = playerObject != null ? playerObject.GetComponent<BattleScript>() : null;
        RunSession.Instance.ApplyPlayerStats(trackedPlayer);
>>>>>>> Stashed changes
        if (RunSession.Instance.TryConsumePendingModifier(out PendingEncounterModifier modifier))
        {
            ApplyModifier(modifier);
        }
        RunSession.Instance.CapturePlayerStats(trackedPlayer);
    }

    private static void ApplyModifier(PendingEncounterModifier modifier)
    {
        GameObject playerObject = GameObject.FindGameObjectWithTag("Player");
        BattleScript player = playerObject != null ? playerObject.GetComponent<BattleScript>() : null;

        switch (modifier.effectType)
        {
            case OverworldEffectType.ReduceEnemyStartingHealth:
                foreach (GameObject enemyObject in GameObject.FindGameObjectsWithTag("Enemy"))
                {
                    BattleScript enemy = enemyObject.GetComponent<BattleScript>();
                    if (enemy != null)
                    {
                        enemy.health = Mathf.Max(1, Mathf.CeilToInt(enemy.health * modifier.magnitude / 100f));
                    }
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
