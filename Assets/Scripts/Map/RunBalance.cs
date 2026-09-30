using UnityEngine;

[CreateAssetMenu(menuName = "Deinosavros/Run Balance")]
public sealed class RunBalance : ScriptableObject
{
    [Min(1)] public int startingMaxHealth = 100;
    [Min(1)] public int deckCapacity = 30;
    [Range(0, 100)] public int recoveryPercent = 15;
    [Min(.01f)] public float minimumOfferingAttackInterval = 1f;

    [Header("Encounter Difficulty")]
    public bool scaleEnemyStatsByLayer = true;
    [Tooltip("Added percent of authored enemy maximum HP per map layer after the first. Linear, not compounded.")]
    [Min(0)] public float enemyHealthGrowthPercent = 10f;
    [Tooltip("Added percent of authored enemy attack per map layer after the first. Rounded to the nearest integer.")]
    [Min(0)] public float enemyDamageGrowthPercent = 8f;
    private static RunBalance fallback;

    public int EnemyMaxHealthAtLayer(int baseHealth, int layer) =>
        Mathf.Max(1, ScaleEnemyStat(baseHealth, layer, enemyHealthGrowthPercent));

    public int EnemyDamageAtLayer(int baseDamage, int layer) =>
        ScaleEnemyStat(baseDamage, layer, enemyDamageGrowthPercent);

    private int ScaleEnemyStat(int baseValue, int layer, float growthPercent)
    {
        float multiplier = scaleEnemyStatsByLayer
            ? 1f + Mathf.Max(0, layer - 1) * Mathf.Max(0f, growthPercent) / 100f : 1f;
        return Mathf.Max(0, Mathf.RoundToInt(Mathf.Max(0, baseValue) * multiplier));
    }

    public static RunBalance Default
    {
        get
        {
            var asset = Resources.Load<RunBalance>("RunBalance");
            if (asset != null) return asset;
            if (fallback == null)
            {
                fallback = CreateInstance<RunBalance>();
                fallback.hideFlags = HideFlags.HideAndDontSave;
            }
            return fallback;
        }
    }
}
