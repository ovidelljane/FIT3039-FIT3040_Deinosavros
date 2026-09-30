using UnityEngine;
using UnityEngine.SceneManagement;

public sealed class BattleRunBridge : MonoBehaviour
{
    [SerializeField] private string battleSceneName = "Deinosavros";
    private BattleScript trackedPlayer;
    private string initializedEncounterId;

    private void OnEnable()
    {
        var owner = GetComponent<RunSession>();
        if (owner != null && RunSession.Instance != null && RunSession.Instance != owner)
        { enabled = false; return; }
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
        // The travel coordinator confirms readiness after Start, before consuming an offering.
        if (RunSession.Instance.Progress != null) { trackedPlayer = null; return; }

        trackedPlayer = BattleHud.FindPlayer(scene);
        RunSession.Instance.ApplyPlayerStats(trackedPlayer);
        if (RunSession.Instance.TryConsumePendingModifier(out PendingEncounterModifier modifier))
        {
            ApplyModifier(modifier);
        }
        RunSession.Instance.CapturePlayerStats(trackedPlayer);
    }
    public bool InitializeConfirmedEncounter(string id, BattleScript player)
    {
        var session = RunSession.Instance;
        if (session?.Progress?.Phase != MapProgressPhase.InEncounter || session.Progress.CurrentEncounter?.EncounterId != id ||
            !session.Progress.CurrentEncounter.IsCombat || initializedEncounterId == id || player == null) return false;
        initializedEncounterId = id; trackedPlayer = player;
        session.ApplyPlayerStats(player);
        if (session.TryConsumePendingModifier(out PendingEncounterModifier modifier)) ApplyModifier(modifier, player);
        session.CapturePlayerStats(trackedPlayer); return true;
    }

    private static void ApplyModifier(PendingEncounterModifier modifier, BattleScript confirmedPlayer = null)
    {
        BattleScript player = confirmedPlayer;
        if (player == null) player = MapTravelCoordinator.FindReadyPlayer(SceneManager.GetActiveScene().name);

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
