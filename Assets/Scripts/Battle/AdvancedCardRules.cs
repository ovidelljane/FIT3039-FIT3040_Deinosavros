using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

// Authored on the two card prefabs. Values and description tokens share this source.
public sealed class AdvancedCardRules : MonoBehaviour
{
    [Header("Judgement Day")]
    [Min(0)] public int playerHealthLoss = 20;
    [Range(0, 99)] public float frequencyReductionPercent = 50;
    [Header("Cataclysm")]
    public Vector3 healthThresholds = new(50, 75, 100);
    public Vector3 healthBonuses = new(70, 40, 15);

    public void PlayJudgement(BattleScript player, float damage, float duration)
    {
        var candidates = new List<BattleScript> { player };
        foreach (var enemy in BattleScript.FindFighters("Enemy"))
            if (enemy.health > 0 && enemy.gameObject.scene == player.gameObject.scene) candidates.Add(enemy);
        ResolveJudgement(player, candidates[Random.Range(0, candidates.Count)], candidates, Mathf.RoundToInt(damage), duration);
    }

    public void ResolveJudgement(BattleScript player, BattleScript selected, IReadOnlyList<BattleScript> combatants, int damage, float duration)
    {
        // Slow is installed before damage: lethal outcomes must not leave half-applied card effects.
        foreach (var actor in combatants)
            if (actor != null && actor.health > 0 && actor.isActiveAndEnabled)
                CreateEffect(actor, StatType.FrequencySlow, frequencyReductionPercent, duration);
        if (selected == player) player.LoseHealth(playerHealthLoss, player);
        else if (selected != null) selected.TakeDamage(damage, player);
    }

    public void PlayCataclysm(BattleScript player, float duration)
    {
        Effect active = null;
        foreach (var effect in Effect.ActiveEffects)
            if (effect != null && effect.IsApplied && effect.target == player && effect.effectType == StatType.Cataclysm)
            { active = effect; break; }
        if (active == null) active = NewEffect(player);
        active.healthThresholds = healthThresholds;
        active.healthBonuses = healthBonuses;
        // Replaying refreshes this one effect; it never multiplies the health bonus twice.
        active.SetValues(StatType.Cataclysm, player, 0, duration);
    }

    private static Effect NewEffect(BattleScript actor)
    {
        var root = new GameObject("CardEffect");
        root.transform.SetParent(actor.transform, false);
        return root.AddComponent<Effect>();
    }

    private static void CreateEffect(BattleScript actor, StatType stat, float amount, float duration) =>
        NewEffect(actor).SetValues(stat, actor, amount, duration);

    private static string Number(float value) => value.ToString("0.##", CultureInfo.InvariantCulture);
    public string Expand(string text) => text
        .Replace("{selfDamage}", playerHealthLoss.ToString())
        .Replace("{slowPercent}", Number(frequencyReductionPercent))
        .Replace("{lowThreshold}", Number(healthThresholds.x))
        .Replace("{midThreshold}", Number(healthThresholds.y))
        .Replace("{highThreshold}", Number(healthThresholds.z))
        .Replace("{lowBonus}", Number(healthBonuses.x))
        .Replace("{midBonus}", Number(healthBonuses.y))
        .Replace("{highBonus}", Number(healthBonuses.z));
}
