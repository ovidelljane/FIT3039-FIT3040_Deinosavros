using UnityEngine;

// Switch authored scenery once, before the first rendered frame. Never move actors or reset combat.
[DefaultExecutionOrder(-19000)]
public sealed class CombatEnvironment : MonoBehaviour
{
    public GameObject forest;
    public GameObject desert;
    [Min(0), Tooltip("Number of actual battles played in the forest before switching to the desert.")]
    public int forestBattleCount = 2;
    [Tooltip("Preview the desert when pressing Play without an active map encounter.")]
    public bool previewDesertWithoutEncounter;

    private void Awake()
    {
        var progress = RunSession.Instance?.Progress;
        bool previewBoss = GetComponent<CombatEncounterRoster>()?.previewBossWithoutEncounter == true;
        Apply(progress, previewBoss);
    }

    public bool UsesDesert(MapProgressState progress, bool previewBoss = false)
    {
        if (progress?.CurrentEncounter == null) return previewDesertWithoutEncounter || previewBoss;
        return progress.CurrentEncounter.IsBoss || progress.CompletedCombatCount >= Mathf.Max(0, forestBattleCount);
    }

    public void Apply(MapProgressState progress, bool previewBoss = false)
    {
        if (forest == null || desert == null || forest == desert)
        {
            Debug.LogError("Combat environment requires distinct forest and desert scene roots.", this);
            return;
        }
        bool useDesert = UsesDesert(progress, previewBoss);
        forest.SetActive(!useDesert);
        desert.SetActive(useDesert);
    }
}
