using UnityEngine;

[CreateAssetMenu(menuName = "Map/Encounter Routing")]
public sealed class MapEncounterRouting : ScriptableObject
{
    public MapNodeCatalog nodes;
    public CardPool cardPool;
    public string battleScene = "Deinosavros";
    public string opportunityScene = "MapOpportunity";
    public string recoveryScene = "MapRecovery";
    public string SceneFor(MapEncounterKind kind) => kind switch
    {
        MapEncounterKind.Battle or MapEncounterKind.Boss => battleScene,
        MapEncounterKind.Opportunity => opportunityScene,
        MapEncounterKind.Recovery => recoveryScene,
        _ => null
    };
}
