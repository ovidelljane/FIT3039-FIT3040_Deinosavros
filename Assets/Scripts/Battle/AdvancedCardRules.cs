using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

// Authored on card prefabs. Values and description tokens share this source.
public sealed class AdvancedCardRules : MonoBehaviour
{
    [Header("Judgement Day")]
    [Min(0)] public int playerHealthLoss = 20;
    [Range(0, 99)] public float frequencyReductionPercent = 50;
    [Header("Cataclysm")]
    public Vector3 healthThresholds = new(50, 75, 100);
    public Vector3 healthBonuses = new(70, 40, 15);
    [Header("Carnage")]
    [Min(0)] public int shieldPerDefeat;
    [Header("Divine Intoxication")]
    [Range(0, 99)] public float intoxicationSlowPercent;
    [Header("Card-triggered Frequency")]
    [Min(0)] public float stackIncrement;
    [Min(0)] public int stackLimit;
    [Header("Outgoing Damage Reduction")]
    [Range(0, 99)] public float damageReductionPercent;
    [Header("Delayed Resource")]
    [Min(0)] public float delayedElixir;
    [Range(0, 100)] public float bonusHealthThreshold;
    [Min(0)] public float lowHealthBonus;

    public void PlayJudgement(BattleScript player, float damage, float duration)
    {
        var candidates = new List<BattleScript> { player };
        foreach (var enemy in BattleScript.FindFighters("Enemy"))
            if (enemy.health > 0 && enemy.gameObject.scene == player.gameObject.scene) candidates.Add(enemy);
        ResolveJudgement(player, candidates[Random.Range(0, candidates.Count)], candidates, Mathf.RoundToInt(damage), duration);
    }

    public void ResolveJudgement(BattleScript player, BattleScript selected, IReadOnlyList<BattleScript> combatants, int damage, float duration)
    {
        // Publish the actual selected victim before damage, including lethal hits.
        if (selected != null)
            CardEffectFeedback.Publish(new CardEffectFeedback(selected, StatType.JudgementDay, 0, damage, CardFeedbackPhase.Applied));
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

    public void PlayAegisOfLight(BattleScript player, int amount)
    {
        int before = player.health;
        int restored = Mathf.Clamp(player.maxHealth - before, 0, Mathf.Max(0, amount));
        player.health += restored;
        if (restored > 0)
            CardEffectFeedback.Publish(new CardEffectFeedback(player, StatType.Heal, before, player.health, CardFeedbackPhase.Applied));
        GrantShield(player, amount - restored);
    }

    public void PlayCarnage(BattleScript player, int damage)
    {
        var enemies = LivingEnemies(player);
        int defeats = 0;
        damage = Mathf.Max(0, damage);
        CardEffectFeedback.Publish(new CardEffectFeedback(player, StatType.DamageAllEnemies, 0, damage, CardFeedbackPhase.Applied));
        foreach (var enemy in enemies)
        {
            if (enemy.health <= 0) continue;
            // A separate on-hit effect can also defeat an enemy during TakeDamage.
            if (damage - Mathf.Max(0, enemy.shield) >= enemy.health) defeats++;
            enemy.TakeDamage(damage, player);
        }
        GrantShield(player, defeats * shieldPerDefeat);
    }

    public void PlayDivineIntoxication(BattleScript player, float elixirPerEnemy, float duration)
    {
        float before = player.elixir;
        float gain = Mathf.Max(0, elixirPerEnemy) * LivingEnemies(player).Count;
        player.elixir += Mathf.Min(gain, Mathf.Max(0, player.maxElixir - before));
        if (gain > 0)
            CardEffectFeedback.Publish(new CardEffectFeedback(player, StatType.Elixir, before, player.elixir, CardFeedbackPhase.Applied));
        if (intoxicationSlowPercent > 0 && duration > 0)
            CreateEffect(player, StatType.FrequencySlow, intoxicationSlowPercent, duration);
    }

    public void PlayMomentum(BattleScript player, CardDefinition source, float percent, float duration)
    {
        var effect = FindEffect(player, StatType.CardMomentum, source);
        bool refresh = effect != null;
        if (!refresh) effect = NewEffect(player);
        effect.sourceCard = source;
        effect.stackIncrement = stackIncrement; effect.stackLimit = stackLimit;
        if (refresh) effect.Refresh(percent, duration);
        else effect.SetValues(StatType.CardMomentum, player, percent, duration);
    }

    public void PlayDamageAndWeaken(BattleScript player, CardDefinition source, int damage, float duration)
    {
        var enemies = LivingEnemies(player);
        // Install the debuff first so a lethal final hit cannot leave a delayed application behind.
        foreach (var enemy in enemies)
        {
            var effect = FindEffect(enemy, StatType.DamageReduction, source);
            if (effect != null) effect.Refresh(damageReductionPercent, duration);
            else
            {
                effect = NewEffect(enemy); effect.sourceCard = source;
                effect.SetValues(StatType.DamageReduction, enemy, damageReductionPercent, duration);
            }
        }
        foreach (var enemy in enemies)
            if (enemy.health > 0)
            {
                CardEffectFeedback.Publish(new CardEffectFeedback(enemy, StatType.DamageAndWeaken, 0, damage, CardFeedbackPhase.Applied));
                enemy.TakeDamage(Mathf.Max(0, damage), player);
            }
    }

    public void PlayDelayedElixir(BattleScript player, float immediate, float delay)
    {
        Effect.RestoreElixir(player, immediate);
        var effect = NewEffect(player);
        effect.bonusHealthThreshold = bonusHealthThreshold; effect.lowHealthBonus = lowHealthBonus;
        effect.SetValues(StatType.DelayedElixir, player, delayedElixir, delay);
    }

    private static Effect FindEffect(BattleScript actor, StatType type, CardDefinition source)
    {
        foreach (var effect in Effect.ActiveEffects)
            if (effect != null && effect.IsApplied && effect.target == actor && effect.effectType == type && effect.IsFromCard(source)) return effect;
        return null;
    }

    private static List<BattleScript> LivingEnemies(BattleScript player) =>
        BattleScript.FindFighters("Enemy").FindAll(enemy => enemy.health > 0 && enemy.gameObject.scene == player.gameObject.scene);

    private static void GrantShield(BattleScript player, int amount)
    {
        if (amount <= 0) return;
        int before = player.shield;
        player.shield += amount;
        CardEffectFeedback.Publish(new CardEffectFeedback(player, StatType.Shield, before, player.shield, CardFeedbackPhase.Applied));
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
    public string Expand(string text, float? currentAmount = null) => text
        .Replace("{shieldPerDefeat}", Number(shieldPerDefeat))
        .Replace("{intoxicationSlow}", Number(intoxicationSlowPercent))
        .Replace("{stackIncrement}", Number(stackIncrement))
        .Replace("{stackLimit}", stackLimit.ToString())
        .Replace("{maximumFrequencyBonus}", Number((currentAmount ?? (GetComponent<BuffCards>() != null ? GetComponent<BuffCards>().Values.Amount : 0)) + stackIncrement * stackLimit))
        .Replace("{damageReduction}", Number(damageReductionPercent))
        .Replace("{delayedElixir}", Number(delayedElixir))
        .Replace("{bonusHealthThreshold}", Number(bonusHealthThreshold))
        .Replace("{lowHealthBonus}", Number(lowHealthBonus))
        .Replace("{selfDamage}", playerHealthLoss.ToString())
        .Replace("{slowPercent}", Number(frequencyReductionPercent))
        .Replace("{lowThreshold}", Number(healthThresholds.x))
        .Replace("{midThreshold}", Number(healthThresholds.y))
        .Replace("{highThreshold}", Number(healthThresholds.z))
        .Replace("{lowBonus}", Number(healthBonuses.x))
        .Replace("{midBonus}", Number(healthBonuses.y))
        .Replace("{highBonus}", Number(healthBonuses.z));
}
