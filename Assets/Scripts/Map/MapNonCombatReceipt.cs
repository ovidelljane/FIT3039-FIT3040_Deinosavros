using System.Collections.Generic;

public sealed class MapNonCombatReceipt
{
    public string RunId { get; internal set; }
    public string EncounterId { get; internal set; }
    public string NodeId { get; internal set; }
    public MapEncounterKind Kind { get; internal set; }
    public bool Resolved { get; internal set; }
    public bool Completed { get; internal set; }
    public MapOpportunityOutcome? Outcome { get; internal set; }
    public bool PresentationRevealed { get; internal set; }
    public MapOpportunityBattleState BattleState { get; internal set; }
    public bool Skipped { get; internal set; }
    public int RecoveryPercent { get; internal set; } = 15;
    public int HealthBefore { get; internal set; }
    public int HealthAfter { get; internal set; }
    public int MaxHealth { get; internal set; }
    public int Recovered => HealthAfter - HealthBefore;
    public RunCardInstance GrantedCard { get; internal set; }
    public IReadOnlyList<CardDefinition> Offers { get; internal set; } = System.Array.Empty<CardDefinition>();
    internal CardPool Pool;
}

