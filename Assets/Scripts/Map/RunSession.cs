using System.Collections.Generic;
using UnityEngine;
using Deinosavros.MapReview;

public sealed class RunSession : MonoBehaviour
{
    public static RunSession Instance { get; private set; }
    [SerializeField] private CardDefinition[] startingDeck;
    [SerializeField] private List<RunCardInstance> runDeck = new();
    [SerializeField] private string selectedNodeId;
    [SerializeField] private bool sacrificeUsed;
    [SerializeField] private PendingEncounterModifier pendingModifier;
    [SerializeField] private int playerHealth = 100;
    [SerializeField] private int playerMaxHealth = 100;
    [SerializeField] private int playerDamage = 9;
    [SerializeField] private float playerAttackSpeed = 4f;
    [SerializeField] private int playerShield;
    [SerializeField] private float playerElixir = 10f;
    [SerializeField] private float playerMaxElixir = 10f;
    private MapGraphDefinition routeGraph;
    private MapNodeCatalog nodeCatalog;
    private readonly Dictionary<string, MapNonCombatReceipt> nonCombat = new();
    public MapNonCombatReceipt LastNonCombatReceipt { get; private set; }
    public const int CapacityLimit = 20;
    public int DeckCapacity
    {
        get
        {
            int value = 0;
            foreach (var card in runDeck)
                if (!card.sacrificed && card.definition != null) value += Mathf.Max(0, card.definition.capacityCost);
            return value;
        }
    }
    public bool HasPendingModifier => pendingModifier.isValid;
    public PendingEncounterModifier PendingModifier => pendingModifier;
    public event System.Action DeckChanged;
    private int initialHealth, initialMaxHealth, initialDamage, initialShield;
    private float initialAttackSpeed, initialElixir, initialMaxElixir;
    public MapProgressState Progress { get; private set; }
    public string SelectedNodeId => selectedNodeId;
    public string CurrentEncounterNodeId => Progress?.CurrentEncounter?.NodeId;
    public bool CurrentEncounterIsBoss => Progress?.CurrentEncounter?.IsBoss ?? false;

    public IReadOnlyList<RunCardInstance> RunDeck => runDeck;
    public bool SacrificeUsed => sacrificeUsed;
    public int PlayerHealth => playerHealth;
    public int PlayerMaxHealth => playerMaxHealth;
    public int PlayerDamage => playerDamage;
    public float PlayerAttackSpeed => playerAttackSpeed;
    public int PlayerShield => playerShield;
    public float PlayerElixir => playerElixir;
    public float PlayerMaxElixir => playerMaxElixir;

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
        EnsurePlayerStatsInitialized();
        initialHealth = playerHealth; initialMaxHealth = playerMaxHealth; initialDamage = playerDamage;
        initialAttackSpeed = playerAttackSpeed; initialShield = playerShield;
        initialElixir = playerElixir; initialMaxElixir = playerMaxElixir;
        var instanceIds = new HashSet<string>();
        foreach (var card in runDeck)
            if (string.IsNullOrEmpty(card.instanceId) || !instanceIds.Add(card.instanceId))
            { card.instanceId = System.Guid.NewGuid().ToString("N"); instanceIds.Add(card.instanceId); }
        if (runDeck.Count != 0) return;
        foreach (CardDefinition definition in startingDeck ?? System.Array.Empty<CardDefinition>())
        {
            if (definition != null) runDeck.Add(new RunCardInstance { definition = definition });
        }
    }

    public void SelectNode(string nodeId) => selectedNodeId = nodeId;

#if UNITY_EDITOR
    public int StartingMaxHealthForTesting => initialMaxHealth;

    internal bool TryBeginTestRunAt(string nodeId, int? startingHealth, out string reason)
    {
        reason = null;
        if (!Application.isPlaying || Instance != this || Progress == null || routeGraph == null ||
            Progress.CurrentEncounter != null || GetComponent<MapTravelCoordinator>()?.IsBusy == true ||
            (Progress.Phase != MapProgressPhase.OnMap && Progress.Phase != MapProgressPhase.Won && Progress.Phase != MapProgressPhase.Lost))
        { reason = "A test run can only replace an idle map session."; return false; }
        if (string.IsNullOrEmpty(nodeId) || routeGraph.Find(nodeId) == null)
        { reason = "The requested start node does not exist."; return false; }
        if (initialMaxHealth < 1 || (startingHealth.HasValue && (startingHealth.Value < 1 || startingHealth.Value > initialMaxHealth)))
        { reason = "Starting HP must be between 1 and the starting maximum health."; return false; }

        BeginNewRun();
        if (startingHealth.HasValue) playerHealth = startingHealth.Value;
        if (!Progress.TrySetTestStartNode(nodeId))
        { reason = "The new test run could not be positioned at the requested node."; return false; }
        return true;
    }
#endif

    public void InitializeProgress(MapGraphDefinition graph, MapNodeCatalog catalog = null)
    {
        routeGraph = graph; nodeCatalog = catalog;
        if (Progress == null) Progress = new MapProgressState(graph, catalog);
    }
    public void BeginNewRun()
    {
        if (routeGraph == null) return;
        runDeck.Clear();
        foreach (var definition in startingDeck ?? System.Array.Empty<CardDefinition>())
            if (definition != null) runDeck.Add(new RunCardInstance { definition = definition });
        selectedNodeId = null; sacrificeUsed = false; pendingModifier = default;
        nonCombat.Clear(); LastNonCombatReceipt = null;
        GetComponent<MapTravelCoordinator>()?.ClearNotice();
        playerHealth = initialHealth; playerMaxHealth = initialMaxHealth; playerDamage = initialDamage;
        playerAttackSpeed = initialAttackSpeed; playerShield = initialShield;
        playerElixir = initialElixir; playerMaxElixir = initialMaxElixir;
        Progress.BeginNewRun(routeGraph, nodeCatalog);
        DeckChanged?.Invoke();
    }
    public bool TryBeginTravel(string id, out MapTravelTicket ticket)
    {
        ticket = null; return Progress != null && Progress.TryBeginTravel(id, out ticket);
    }
    public bool ConfirmEncounterStarted(string id) => Progress?.ConfirmEncounterStarted(id) ?? false;
    public bool CancelPreparedTravel(string id)
    {
        if (!(Progress?.CancelPreparedTravel(id) ?? false)) return false;
        nonCombat.Remove(id); return true;
    }
    public bool CompleteEncounter(string id, bool victory)
    {
        if (Progress?.CurrentEncounter == null || !Progress.CurrentEncounter.IsCombat || !Progress.CompleteEncounter(id, victory)) return false;
        if (victory && Progress.Phase == MapProgressPhase.OnMap) sacrificeUsed = false;
        return true;
    }

    public bool TrySacrifice(CardDefinition definition)
    {
        RunCardInstance instance = runDeck.Find(card => card.definition == definition && !card.sacrificed);
        return instance != null && TrySacrificeInstance(instance.instanceId);
    }
    public bool TrySacrificeInstance(string instanceId)
    {
        if (sacrificeUsed || string.IsNullOrEmpty(instanceId) || (Progress != null && Progress.Phase != MapProgressPhase.OnMap)) return false;
        RunCardInstance instance = runDeck.Find(card => card.instanceId == instanceId && !card.sacrificed && card.definition != null);
        if (instance == null) return false;
        var definition = instance.definition;
        instance.sacrificed = true;
        sacrificeUsed = true;
        pendingModifier = new PendingEncounterModifier
        {
            sourceCardId = definition.cardId,
            effectType = definition.overworldEffect.effectType,
            magnitude = definition.overworldEffect.magnitude,
            isValid = true
        };
        DeckChanged?.Invoke();
        return true;
    }

    public bool ContainsCard(string cardId)
    {
        return runDeck.Exists(card => card.definition != null && card.definition.cardId == cardId && !card.sacrificed);
    }

    public bool CanAddCard(CardDefinition definition) => definition != null && definition.capacityCost >= 0 &&
        DeckCapacity <= CapacityLimit && definition.capacityCost <= CapacityLimit - DeckCapacity &&
        (Progress == null || Progress.Phase == MapProgressPhase.OnMap || Progress.Phase == MapProgressPhase.InEncounter);

    public void AddCard(CardDefinition definition) => TryAddCard(definition, out _);

    public bool TryAddCard(CardDefinition definition, out RunCardInstance added)
    {
        added = null;
        // Opportunity rewards must pass the encounter-specific one-claim gate.
        if (Progress?.CurrentEncounter?.Kind == MapEncounterKind.Opportunity) return false;
        return AddWithinCapacity(definition, out added);
    }
    private bool AddWithinCapacity(CardDefinition definition, out RunCardInstance added)
    {
        added = null; if (!CanAddCard(definition)) return false;
        added = new RunCardInstance { definition = definition }; runDeck.Add(added); DeckChanged?.Invoke(); return true;
    }
    private bool IsCurrentNonCombat(string id, MapEncounterKind kind, bool preparing = false)
    {
        var ticket = Progress?.CurrentEncounter;
        return ticket != null && ticket.RunId == Progress.RunId && ticket.EncounterId == id && ticket.Kind == kind &&
            (Progress.Phase == MapProgressPhase.InEncounter || (preparing && Progress.Phase == MapProgressPhase.Loading));
    }
    public MapNonCombatReceipt GetNonCombatReceipt(string id)
    {
        return id != null && nonCombat.TryGetValue(id, out var receipt) && receipt.RunId == Progress?.RunId ? receipt : null;
    }
    private MapNonCombatReceipt NewReceipt()
    {
        var ticket = Progress.CurrentEncounter;
        var receipt = new MapNonCombatReceipt { RunId = ticket.RunId, EncounterId = ticket.EncounterId,
            NodeId = ticket.NodeId, Kind = ticket.Kind };
        nonCombat.Add(ticket.EncounterId, receipt); return receipt;
    }
    public bool TryPrepareOpportunity(string id, CardPool pool, IReadOnlyList<CardDefinition> offers)
    {
        if (!IsCurrentNonCombat(id, MapEncounterKind.Opportunity, true) || pool == null || pool.Cards == null || offers == null) return false;
        if (GetNonCombatReceipt(id) != null) return true;
        var unique = new HashSet<CardDefinition>();
        foreach (var offer in offers)
            if (offer == null || !PoolContains(pool, offer) || !unique.Add(offer)) return false;
        var receipt = NewReceipt(); receipt.Pool = pool;
        receipt.Offers = new List<CardDefinition>(offers).AsReadOnly(); return true;
    }
    public bool TryResolveRecovery(string id, out MapNonCombatReceipt receipt)
    {
        receipt = null;
        if (!IsCurrentNonCombat(id, MapEncounterKind.Recovery) || playerHealth <= 0 || playerMaxHealth <= 0) return false;
        receipt = GetNonCombatReceipt(id) ?? NewReceipt();
        if (receipt.Resolved) return true;
        receipt.HealthBefore = playerHealth; receipt.MaxHealth = playerMaxHealth;
        // Integer arithmetic avoids float error turning an exact 15 HP into 16.
        int recovery = (int)(((long)playerMaxHealth * 15 + 99) / 100);
        playerHealth += Mathf.Min(Mathf.Max(0, playerMaxHealth - playerHealth), recovery);
        receipt.HealthAfter = playerHealth; receipt.Resolved = true; return true;
    }
    public bool TryClaimOpportunityCard(string id, CardDefinition definition, out string reason)
    {
        reason = null;
        var receipt = GetNonCombatReceipt(id);
        if (!IsCurrentNonCombat(id, MapEncounterKind.Opportunity) || receipt == null || receipt.Completed)
        { reason = "This opportunity is no longer active."; return false; }
        if (receipt.GrantedCard != null) { reason = "A card has already been claimed."; return false; }
        bool offered = false; foreach (var candidate in receipt.Offers) if (candidate == definition) offered = true;
        if (definition == null || !offered || !PoolContains(receipt.Pool, definition))
        { reason = "This card is not offered by this opportunity."; return false; }
        if (!AddWithinCapacity(definition, out var card))
        { reason = "Deck capacity exceeded. You may leave without taking a card."; return false; }
        receipt.GrantedCard = card; receipt.Resolved = true; return true;
    }
    public bool CompleteNonCombatEncounter(string id)
    {
        var receipt = GetNonCombatReceipt(id);
        if (receipt == null || receipt.Completed || !IsCurrentNonCombat(id, receipt.Kind) ||
            (receipt.Kind == MapEncounterKind.Recovery && !receipt.Resolved)) return false;
        if (!Progress.CompleteEncounter(id, true)) return false;
        receipt.Completed = true; LastNonCombatReceipt = receipt; return true;
    }
    private static bool PoolContains(CardPool pool, CardDefinition card)
    {
        if (pool?.Cards == null) return false;
        foreach (var candidate in pool.Cards) if (candidate == card) return true;
        return false;
    }

    public List<CardDefinition> GetActiveDeck()
    {
        var list = new List<CardDefinition>();
        foreach (RunCardInstance card in runDeck)
        {
            if (!card.sacrificed && card.definition != null) list.Add(card.definition);
        }
        return list;
    }

    public bool TryConsumePendingModifier(out PendingEncounterModifier modifier)
    {
        modifier = pendingModifier;
        if (!pendingModifier.isValid || (Progress != null &&
            (Progress.Phase != MapProgressPhase.InEncounter || Progress.CurrentEncounter == null || !Progress.CurrentEncounter.IsCombat))) return false;
        pendingModifier.isValid = false;
        return true;
    }

    public void ApplyPlayerStats(BattleScript player)
    {
        if (player == null)
        {
            return;
        }

        player.maxHealth = Mathf.Max(1, playerMaxHealth);
        player.health = Mathf.Clamp(playerHealth, 0, player.maxHealth);
        player.attackDmg = Mathf.Max(0, playerDamage);
        player.attackSpd = playerAttackSpeed;
        player.shield = Mathf.Max(0, playerShield);
        player.maxElixir = Mathf.Max(0f, playerMaxElixir);
        player.elixir = Mathf.Clamp(playerElixir, 0f, player.maxElixir);
    }

    public void CapturePlayerStats(BattleScript player)
    {
        if (player == null)
        {
            return;
        }

        playerMaxHealth = Mathf.Max(1, player.maxHealth);
        playerHealth = Mathf.Clamp(player.health, 0, playerMaxHealth);
        playerDamage = Mathf.Max(0, player.attackDmg);
        playerAttackSpeed = player.attackSpd;
        playerShield = Mathf.Max(0, player.shield);
        playerMaxElixir = Mathf.Max(0f, player.maxElixir);
        playerElixir = Mathf.Clamp(player.elixir, 0f, playerMaxElixir);
    }

    private void EnsurePlayerStatsInitialized()
    {
        if (playerMaxHealth > 0)
        {
            return;
        }

        playerHealth = 100;
        playerMaxHealth = 100;
        playerDamage = 9;
        playerAttackSpeed = 4;
        playerShield = 0;
        playerElixir = 10f;
        playerMaxElixir = 10f;
    }
}
