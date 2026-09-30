using System;
using System.Globalization;
using UnityEngine;

public enum MapCardType { Attack, Buff, Defense, Elixir, Debuff }
public enum OverworldEffectType { ReduceEnemyStartingHealth, ImprovePlayerAttackSpeed, GrantStartingShield, IncreaseStartingElixir }

[Serializable]
public struct OverworldEffectDefinition
{
    public OverworldEffectType effectType;
    public int magnitude;
    public string title;
    [TextArea] public string description;
    public string scopeLabel;
}

[CreateAssetMenu(menuName = "Deinosavros/Card Definition", fileName = "CardDefinition")]
public sealed class CardDefinition : ScriptableObject
{
    public string cardId;
    public string displayName;
    [TextArea, Tooltip("Dynamic tokens: {amount}, {duration}, {cost}, {capacity}, {costResource}.")]
    public string combatEffectText;
    public MapCardType cardType;
    public int capacityCost;
    public GameObject combatPrefab;
    public GameObject mapPrefab;
    public GameObject backPrefab;
    public Sprite frontArtwork;
    [Tooltip("Normalized illustration area. Exclude a baked-in frame before applying the shared card border.")]
    public Rect frontArtworkRegion = new Rect(0f, 0f, 1f, 1f);
    public Sprite frontBorder;
    public Sprite backArtwork;
    public OverworldEffectDefinition overworldEffect;

    private Sprite croppedArtwork;
    private Sprite croppedSource;
    private Rect croppedRegion;

    public Sprite FrontIllustration
    {
        get
        {
            var region = frontArtworkRegion;
            if (frontArtwork == null || region == new Rect(0f, 0f, 1f, 1f) || region.width <= 0f || region.height <= 0f)
                return frontArtwork;
            if (croppedArtwork != null && croppedSource == frontArtwork && croppedRegion == region)
                return croppedArtwork;
            ReleaseCroppedArtwork();
            var source = frontArtwork.rect;
            float left = Mathf.Clamp01(region.xMin), bottom = Mathf.Clamp01(region.yMin);
            float right = Mathf.Clamp01(region.xMax), top = Mathf.Clamp01(region.yMax);
            if (right <= left || top <= bottom) return frontArtwork;
            var rect = new Rect(source.x + source.width * left, source.y + source.height * bottom,
                source.width * (right - left), source.height * (top - bottom));
            croppedArtwork = Sprite.Create(frontArtwork.texture, rect, new Vector2(.5f, .5f),
                frontArtwork.pixelsPerUnit, 0, SpriteMeshType.FullRect);
            croppedArtwork.name = frontArtwork.name + " Illustration";
            croppedArtwork.hideFlags = HideFlags.DontSave;
            croppedSource = frontArtwork; croppedRegion = region;
            return croppedArtwork;
        }
    }

    private void OnDisable() => ReleaseCroppedArtwork();

    private void ReleaseCroppedArtwork()
    {
        if (croppedArtwork != null)
        {
            if (Application.isPlaying) Destroy(croppedArtwork);
            else DestroyImmediate(croppedArtwork);
        }
        croppedArtwork = null; croppedSource = null;
    }

    public CardEffectValues BaseValues => combatPrefab != null && combatPrefab.TryGetComponent<BuffCards>(out var card)
        ? card.Values : default;

    public string GetCombatDescription(CardEffectValues? current = null)
    {
        var baseline = BaseValues;
        var values = current ?? baseline;
        return (combatEffectText ?? "")
            .Replace("{amount}", Number(values.Amount, baseline.Amount))
            .Replace("{duration}", Number(values.Duration, baseline.Duration))
            .Replace("{cost}", Number(values.Cost, baseline.Cost, true))
            .Replace("{capacity}", Number(capacityCost, capacityCost))
            .Replace("{costResource}", values.Stat == StatType.Elixir ? "Health" : "Elixir");
    }

    public string GetSacrificeDescription(RunSession session = null)
    {
        float minimum = session != null ? session.MinimumOfferingAttackInterval :
            Mathf.Max(.01f, RunBalance.Default.minimumOfferingAttackInterval);
        return (overworldEffect.description ?? "")
            .Replace("{magnitude}", Number(overworldEffect.magnitude, overworldEffect.magnitude))
            .Replace("{minInterval}", Number(minimum, minimum));
    }

    private static string Number(float value, float baseline, bool lowerIsBetter = false)
    {
        string text = value.ToString("0.##", CultureInfo.InvariantCulture);
        if (Mathf.Approximately(value, baseline)) return text;
        bool improved = lowerIsBetter ? value < baseline : value > baseline;
        return "<color=" + (improved ? "#9CD889" : "#EF9987") + ">" + text + "</color>";
    }
}

// Base data lives on the authored prefab; a live card supplies its current values.
public readonly struct CardEffectValues
{
    public readonly StatType Stat;
    public readonly float Amount, Duration;
    public readonly int Cost;
    public CardEffectValues(StatType stat, float amount, int cost, float duration)
    {
        Stat = stat; Cost = Mathf.Max(0, cost); Duration = Mathf.Max(0, duration);
        Amount = stat == StatType.AttackSpeed || stat == StatType.EnemySlow || stat == StatType.Elixir ||
            stat == StatType.ElixirRegen ? amount : (int)amount;
    }
}

[Serializable]
public sealed class RunCardInstance
{
    public string instanceId = Guid.NewGuid().ToString("N");
    public CardDefinition definition;
    public bool sacrificed;
}

[Serializable]
public struct PendingEncounterModifier
{
    public string sourceCardId;
    public OverworldEffectType effectType;
    public int magnitude;
    public bool isValid;
}
