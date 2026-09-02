using System.Collections.Generic;
using UnityEngine;

public sealed class RunSession : MonoBehaviour
{
    public static RunSession Instance { get; private set; }
    [SerializeField] private CardDefinition[] startingDeck;
    [SerializeField] private List<RunCardInstance> runDeck = new();
    [SerializeField] private string selectedNodeId;
    [SerializeField] private bool sacrificeUsed;
    [SerializeField] private PendingEncounterModifier pendingModifier;

    public IReadOnlyList<RunCardInstance> RunDeck => runDeck;
    public bool SacrificeUsed => sacrificeUsed;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(this);
            return;
        }
        Instance = this;
        transform.SetParent(null, true);
        DontDestroyOnLoad(gameObject);
        if (runDeck.Count != 0) return;
        foreach (CardDefinition definition in startingDeck)
        {
            if (definition != null) runDeck.Add(new RunCardInstance { definition = definition });
        }
    }

    public void SelectNode(string nodeId) => selectedNodeId = nodeId;

    public bool TrySacrifice(CardDefinition definition)
    {
        if (sacrificeUsed || definition == null) return false;
        RunCardInstance instance = runDeck.Find(card => card.definition == definition && !card.sacrificed);
        if (instance == null) return false;
        instance.sacrificed = true;
        sacrificeUsed = true;
        pendingModifier = new PendingEncounterModifier
        {
            sourceCardId = definition.cardId,
            effectType = definition.overworldEffect.effectType,
            magnitude = definition.overworldEffect.magnitude,
            isValid = true
        };
        return true;
    }

    public bool ContainsCard(string cardId)
    {
        return runDeck.Exists(card => card.definition != null && card.definition.cardId == cardId && !card.sacrificed);
    }

    public bool TryConsumePendingModifier(out PendingEncounterModifier modifier)
    {
        modifier = pendingModifier;
        if (!pendingModifier.isValid) return false;
        pendingModifier.isValid = false;
        return true;
    }
}
