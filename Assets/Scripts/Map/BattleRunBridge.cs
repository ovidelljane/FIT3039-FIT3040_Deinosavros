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

        HideSacrificedCard(RunSession.Instance);
        if (RunSession.Instance.TryConsumePendingModifier(out PendingEncounterModifier modifier))
        {
            ApplyModifier(modifier);
        }
    }

    private static void HideSacrificedCard(RunSession session)
    {
        string[] cardIds = { "tidal_wave", "fleet_footwork", "solar_shield", "uncertain_fates" };
        string[] battleObjectNames = { "TidalWave", "FleetFootwork", "SolarShield", "UncertainFates" };
        for (int i = 0; i < cardIds.Length; i++)
        {
            if (session.ContainsCard(cardIds[i]))
            {
                continue;
            }

            GameObject cardObject = GameObject.Find(battleObjectNames[i]);
            if (cardObject != null)
            {
                cardObject.SetActive(false);
            }
        }
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
