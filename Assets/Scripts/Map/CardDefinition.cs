using System;
using UnityEngine;

public enum MapCardType { Attack, Buff, Defense, Elixir }
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
    public string combatEffectText;
    public MapCardType cardType;
    public int capacityCost;
    public GameObject combatPrefab;
    public GameObject mapPrefab;
    public GameObject backPrefab;
    public Sprite frontArtwork;
    public Sprite backArtwork;
    public OverworldEffectDefinition overworldEffect;
}

[Serializable]
public sealed class RunCardInstance
{
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
