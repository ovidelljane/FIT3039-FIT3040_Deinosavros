using System.Collections.Generic;

public sealed class MapNonCombatReceipt
{
    public string RunId { get; internal set; }
    public string EncounterId { get; internal set; }
    public string NodeId { get; internal set; }
    public MapEncounterKind Kind { get; internal set; }
    public bool Resolved { get; internal set; }
    public bool Completed { get; internal set; }
    public int HealthBefore { get; internal set; }
    public int HealthAfter { get; internal set; }
    public int MaxHealth { get; internal set; }
    public int Recovered => HealthAfter - HealthBefore;
    public RunCardInstance GrantedCard { get; internal set; }
    public IReadOnlyList<CardDefinition> Offers { get; internal set; } = System.Array.Empty<CardDefinition>();
    internal CardPool Pool;
}

public interface IMapOpportunityContentProvider
{
    IReadOnlyList<CardDefinition> GetOffers(MapTravelTicket ticket, CardPool pool);
}

// The production placeholder deliberately never samples the pool or grants cards.
public sealed class EmptyMapOpportunityContent : IMapOpportunityContentProvider
{
    public IReadOnlyList<CardDefinition> GetOffers(MapTravelTicket ticket, CardPool pool) => System.Array.Empty<CardDefinition>();
}
