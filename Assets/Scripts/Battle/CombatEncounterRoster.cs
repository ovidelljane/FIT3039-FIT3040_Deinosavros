using UnityEngine;

// Select the authored lineup before actor initialization, portrait randomization and HUD binding.
[DefaultExecutionOrder(-20000)]
public sealed class CombatEncounterRoster : MonoBehaviour
{
    public GameObject boss;
    public GameObject[] ordinaryEnemies;
    [Tooltip("Used only when pressing Play directly in Combat without a map encounter.")]
    public bool previewBossWithoutEncounter;

    private void Awake()
    {
        var encounter = RunSession.Instance?.Progress?.CurrentEncounter;
        bool isBoss = encounter != null ? encounter.IsBoss : previewBossWithoutEncounter;
        if (boss != null) { boss.SetActive(isBoss); boss.GetComponent<HealthLabel>()?.SetVisible(isBoss); }
        if (ordinaryEnemies != null)
            foreach (var enemy in ordinaryEnemies)
                if (enemy != null) { enemy.SetActive(!isBoss); enemy.GetComponent<HealthLabel>()?.SetVisible(!isBoss); }
    }
}
