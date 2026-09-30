using System;
using UnityEngine;

public enum MapOpportunityOutcome { Card, Recovery, Battle }
public enum MapOpportunityBattleState { None, Loading, Active, Won, Lost }

[CreateAssetMenu(menuName = "Map/Opportunity Profile")]
public sealed class MapOpportunityProfile : ScriptableObject
{
    [Min(0)] public int cardWeight = 40;
    [Min(0)] public int recoveryWeight = 20;
    [Min(0)] public int battleWeight = 40;
    [Range(1, 100)] public int recoveryPercent = 15;
    [Min(1f)] public float revealDuration = 3.5f;
    [Range(3, 12)] public int revealTurns = 7;

    public bool IsValid => cardWeight >= 0 && recoveryWeight >= 0 && battleWeight >= 0 &&
        (long)cardWeight + recoveryWeight + battleWeight > 0 && recoveryPercent > 0 && recoveryPercent <= 100;

    public MapOpportunityOutcome OutcomeAt(double sample)
    {
        if (!IsValid || double.IsNaN(sample) || sample < 0 || sample >= 1)
            throw new ArgumentOutOfRangeException(nameof(sample));
        double roll = sample * ((long)cardWeight + recoveryWeight + battleWeight);
        if (roll < cardWeight) return MapOpportunityOutcome.Card;
        return roll < (long)cardWeight + recoveryWeight ? MapOpportunityOutcome.Recovery : MapOpportunityOutcome.Battle;
    }
}
