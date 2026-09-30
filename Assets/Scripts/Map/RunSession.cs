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
    [SerializeField] private float playerAttackSpeed = 5f;
    [SerializeField] private int playerShield;
    [SerializeField] private float playerElixir = 10f;
    [SerializeField] private float playerMaxElixir = 10f;
    [SerializeField] private RunBalance balance;
    public RunBalance Rules => balance != null ? balance : RunBalance.Default;
    public int DeckCapacityLimit => Mathf.Max(1, Rules.deckCapacity);
    public int RecoveryPercent => Mathf.Clamp(Rules.recoveryPercent, 0, 100);
    public float MinimumOfferingAttackInterval => Mathf.Max(.01f, Rules.minimumOfferingAttackInterval);
    private MapGraphDefinition routeGraph;
    private MapNodeCatalog nodeCatalog;
    private readonly Dictionary<string, MapNonCombatReceipt> nonCombat = new();
    public MapNonCombatReceipt LastNonCombatReceipt { get; private set; }
    public static int CapacityLimit => Instance != null ? Instance.DeckCapacityLimit : Mathf.Max(1, RunBalance.Default.deckCapacity);
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
    private int initialHealth = 100, initialMaxHealth = 100, initialDamage = 9, initialShield;
    private float initialAttackSpeed = 5f, initialElixir = 10f, initialMaxElixir = 10f;
    private float initialElixirRegen = .5f;
    private int initialHitsPerAttack = 1;
    private bool battleDefaultsConfigured;
    private string rewardRunId, rewardEncounterId;
    private IReadOnlyList<CardDefinition> rewardOffers = System.Array.Empty<CardDefinition>();
    private bool rewardResolved;
    public IReadOnlyList<CardDefinition> BattleRewardOffers => rewardOffers;
    public bool HasBattleReward => rewardEncounterId != null && Progress?.CurrentEncounter?.EncounterId == rewardEncounterId &&
        Progress.RunId == rewardRunId && !rewardResolved;
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
        if (balance == null) balance = RunSettings.ForScene(gameObject.scene);
        transform.SetParent(null, true);
        DontDestroyOnLoad(gameObject);
        EnsurePlayerStatsInitialized();
        playerMaxHealth = Mathf.Max(1, Rules.startingMaxHealth);
        playerHealth = playerMaxHealth;
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

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    public static RunSession StartNewRun(IEnumerable<CardDefinition> deck, MapGraphDefinition graph = null, MapNodeCatalog catalog = null, RunBalance rules = null)
    {
        var definitions = new List<CardDefinition>(deck ?? System.Array.Empty<CardDefinition>());
        int capacity = 0;
        foreach (var card in definitions)
        {
            if (card == null || card.capacityCost < 0) throw new System.ArgumentException("Invalid starting card.");
            capacity += card.capacityCost;
        }
        rules = rules != null ? rules : RunBalance.Default;
        if (capacity > Mathf.Max(1, rules.deckCapacity)) throw new System.ArgumentException("Starting deck exceeds capacity.");
        if (Instance != null) Destroy(Instance.gameObject);
        Instance = null;
        var root = new GameObject("RunSession");
        root.SetActive(false);
        var session = root.AddComponent<RunSession>();
        session.balance = rules;
        session.startingDeck = definitions.ToArray();
        root.AddComponent<BattleRunBridge>();
        root.SetActive(true);
        if (graph != null) session.InitializeProgress(graph, catalog);
        return session;
    }

    public void SelectNode(string nodeId) => selectedNodeId = nodeId;

#if UNITY_EDITOR
    public int StartingMaxHealthForTesting => Mathf.Max(1, Rules.startingMaxHealth);
    public MapOpportunityOutcome? NextOpportunityForTesting { get; private set; }
    public bool SetNextOpportunityForTesting(MapOpportunityOutcome? outcome)
    {
        if (Progress?.Phase != MapProgressPhase.OnMap || Progress.CurrentEncounter != null ||
            (outcome.HasValue && !System.Enum.IsDefined(typeof(MapOpportunityOutcome), outcome.Value))) return false;
        NextOpportunityForTesting = outcome; return true;
    }

    internal bool TryBeginTestRunAt(string nodeId, int? startingHealth, out string reason)
    {
        reason = null;
        if (!Application.isPlaying || Instance != this || Progress == null || routeGraph == null ||
            Progress.CurrentEncounter != null || GetComponent<MapTravelCoordinator>()?.IsBusy == true ||
            (Progress.Phase != MapProgressPhase.OnMap && Progress.Phase != MapProgressPhase.Won && Progress.Phase != MapProgressPhase.Lost))
        { reason = "A test run can only replace an idle map session."; return false; }
        if (string.IsNullOrEmpty(nodeId) || routeGraph.Find(nodeId) == null)
        { reason = "The requested start node does not exist."; return false; }
        if (startingHealth.HasValue && (startingHealth.Value < 1 || startingHealth.Value > StartingMaxHealthForTesting))
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
        initialHealth = initialMaxHealth = Mathf.Max(1, Rules.startingMaxHealth);
        runDeck.Clear();
        foreach (var definition in startingDeck ?? System.Array.Empty<CardDefinition>())
            if (definition != null) runDeck.Add(new RunCardInstance { definition = definition });
        selectedNodeId = null; sacrificeUsed = false; pendingModifier = default;
        nonCombat.Clear(); LastNonCombatReceipt = null; ClearBattleReward();
#if UNITY_EDITOR
        NextOpportunityForTesting = null;
#endif
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
    public bool ConfirmEncounterStarted(string id)
    {
        if (!(Progress?.ConfirmEncounterStarted(id) ?? false)) return false;
        var receipt = GetNonCombatReceipt(id);
        if (Progress.CurrentEncounter.IsCombat && receipt?.Outcome == MapOpportunityOutcome.Battle)
            receipt.BattleState = MapOpportunityBattleState.Active;
        return true;
    }
    public bool CancelPreparedTravel(string id)
    {
        if (!(Progress?.CancelPreparedTravel(id) ?? false)) return false;
        nonCombat.Remove(id); return true;
    }
    public bool CompleteEncounter(string id, bool victory)
    {
        if (Progress?.CurrentEncounter == null || !Progress.CurrentEncounter.IsCombat || !Progress.CompleteEncounter(id, victory)) return false;
        var receipt = GetNonCombatReceipt(id);
        if (receipt?.Outcome == MapOpportunityOutcome.Battle)
        {
            receipt.Resolved = true; receipt.Completed = true;
            receipt.BattleState = victory ? MapOpportunityBattleState.Won : MapOpportunityBattleState.Lost;
            LastNonCombatReceipt = receipt;
        }
        pendingModifier = default;
        ClearBattleReward();
        ResetCombatStats();
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
        DeckCapacity <= DeckCapacityLimit && definition.capacityCost <= DeckCapacityLimit - DeckCapacity &&
        (Progress == null || Progress.Phase == MapProgressPhase.OnMap || Progress.Phase == MapProgressPhase.InEncounter);

    public void AddCard(CardDefinition definition) => TryAddCard(definition, out _);

    public bool TryAddCard(CardDefinition definition, out RunCardInstance added)
    {
        added = null;
        // Opportunity rewards must pass the encounter-specific one-claim gate.
        if (Progress?.CurrentEncounter?.Kind == MapEncounterKind.Opportunity || HasBattleReward) return false;
        return AddWithinCapacity(definition, out added);
    }
    private bool AddWithinCapacity(CardDefinition definition, out RunCardInstance added)
    {
        added = null; if (!CanAddCard(definition)) return false;
        added = new RunCardInstance { definition = definition }; runDeck.Add(added); DeckChanged?.Invoke(); return true;
    }
    public bool TryPrepareBattleReward(string id, CardPool pool, int count, out IReadOnlyList<CardDefinition> offers)
    {
        offers = rewardOffers;
        if (!IsCurrentCombat(id) || playerHealth <= 0) return false;
        if (rewardEncounterId != null)
            return rewardEncounterId == id && rewardRunId == Progress.RunId && !rewardResolved;
        rewardEncounterId = id; rewardRunId = Progress.RunId; rewardResolved = false;
        var unique = new List<CardDefinition>();
        if (pool != null)
            foreach (var card in pool.GetRandomCards(Mathf.Max(0, count)))
                if (card != null && card.capacityCost >= 0 && !unique.Contains(card)) unique.Add(card);
        rewardOffers = unique.AsReadOnly(); offers = rewardOffers;
        return true;
    }

    public bool TryResolveBattleReward(string id, CardDefinition incoming, string outgoingInstanceId, out string reason)
    {
        reason = null;
        if (!IsCurrentCombat(id) || !HasBattleReward || rewardEncounterId != id)
        { reason = "This reward is no longer active."; return false; }
        RunCardInstance outgoing = null;
        if (incoming == null)
        {
            if (!string.IsNullOrEmpty(outgoingInstanceId)) { reason = "A skipped reward cannot remove a card."; return false; }
        }
        else
        {
            bool offered = false;
            foreach (var offer in rewardOffers) if (offer == incoming) offered = true;
            if (!offered || incoming.capacityCost < 0) { reason = "This card is not offered."; return false; }
            if (!string.IsNullOrEmpty(outgoingInstanceId))
                outgoing = runDeck.Find(card => card.instanceId == outgoingInstanceId && !card.sacrificed && card.definition != null);
            if (!string.IsNullOrEmpty(outgoingInstanceId) && outgoing == null)
            { reason = "The selected card is no longer in the deck."; return false; }
            int capacity = DeckCapacity - (outgoing != null ? outgoing.definition.capacityCost : 0) + incoming.capacityCost;
            if (capacity > DeckCapacityLimit) { reason = "Select a card to replace or skip this reward."; return false; }
        }
        // Validate everything before mutating either card. Replacement is not a sacrifice.
        if (outgoing != null) runDeck.Remove(outgoing);
        if (incoming != null) runDeck.Add(new RunCardInstance { definition = incoming });
        rewardResolved = true;
        if (incoming != null) DeckChanged?.Invoke();
        return true;
    }

    public bool IsBattleRewardResolved(string id) => IsCurrentCombat(id) && rewardEncounterId == id &&
        rewardRunId == Progress.RunId && rewardResolved;

    private bool IsCurrentCombat(string id) => Progress?.Phase == MapProgressPhase.InEncounter &&
        Progress.CurrentEncounter != null && Progress.CurrentEncounter.IsCombat &&
        Progress.CurrentEncounter.RunId == Progress.RunId && Progress.CurrentEncounter.EncounterId == id;

    private void ClearBattleReward()
    {
        rewardRunId = null; rewardEncounterId = null; rewardResolved = false;
        rewardOffers = System.Array.Empty<CardDefinition>();
    }

    private bool IsCurrentNonCombat(string id, MapEncounterKind kind, bool preparing = false)
    {
        var ticket = Progress?.CurrentEncounter;
        return ticket != null && ticket.RunId == Progress.RunId && ticket.EncounterId == id && ticket.Kind == kind &&
            !ticket.IsCombat && playerHealth > 0 &&
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
        receipt.Outcome = MapOpportunityOutcome.Card;
        receipt.Offers = new List<CardDefinition>(offers).AsReadOnly(); return true;
    }
    public bool TryPrepareOpportunityEvent(string id, MapOpportunityProfile profile, CardPool pool)
    {
        if (!IsCurrentNonCombat(id, MapEncounterKind.Opportunity, true)) return false;
        if (GetNonCombatReceipt(id) != null) return true;
        if (profile == null || !profile.IsValid || pool?.Cards == null) return false;
        var candidates = new List<CardDefinition>();
        foreach (var card in pool.Cards)
            if (card != null && card.capacityCost >= 0 && !candidates.Contains(card)) candidates.Add(card);
        if (candidates.Count == 0) return false;
        var outcome = profile.OutcomeAt(Random.Range(0, 1000000) / 1000000d);
#if UNITY_EDITOR
        if (NextOpportunityForTesting.HasValue) outcome = NextOpportunityForTesting.Value;
        NextOpportunityForTesting = null;
#endif
        var receipt = NewReceipt(); receipt.Pool = pool; receipt.Outcome = outcome;
        receipt.RecoveryPercent = profile.recoveryPercent;
        if (outcome == MapOpportunityOutcome.Card)
            receipt.Offers = System.Array.AsReadOnly(new[] { candidates[Random.Range(0, candidates.Count)] });
        return true;
    }
    public bool TryResolveRecovery(string id, out MapNonCombatReceipt receipt)
    {
        receipt = null;
        if (!IsCurrentNonCombat(id, MapEncounterKind.Recovery) || playerHealth <= 0 || playerMaxHealth <= 0) return false;
        receipt = GetNonCombatReceipt(id) ?? NewReceipt();
        ApplyRecovery(receipt, RecoveryPercent); return true;
    }
    private void ApplyRecovery(MapNonCombatReceipt receipt, int percent)
    {
        if (receipt.Resolved) return;
        receipt.RecoveryPercent = percent;
        receipt.HealthBefore = playerHealth; receipt.MaxHealth = playerMaxHealth;
        int recovery = (int)(((long)playerMaxHealth * percent + 99) / 100);
        playerHealth += Mathf.Min(Mathf.Max(0, playerMaxHealth - playerHealth), recovery);
        receipt.HealthAfter = playerHealth; receipt.Resolved = true;
    }
    public bool TryClaimOpportunityCard(string id, CardDefinition definition, out string reason)
    {
        if (definition == null) { reason = "No card was selected."; return false; }
        return TryResolveOpportunityReward(id, definition, null, out reason);
    }

    public bool TryResolveOpportunityReward(string id, CardDefinition definition, string outgoingId, out string reason)
    {
        reason = null;
        var receipt = GetNonCombatReceipt(id);
        if (!IsCurrentNonCombat(id, MapEncounterKind.Opportunity) || receipt == null || receipt.Completed ||
            receipt.Resolved || receipt.Outcome != MapOpportunityOutcome.Card)
        { reason = "This opportunity is no longer active."; return false; }
        if (definition == null)
        {
            if (!string.IsNullOrEmpty(outgoingId)) { reason = "Skipping cannot remove a card."; return false; }
            receipt.Skipped = true; receipt.Resolved = true; return true;
        }
        bool offered = false; foreach (var candidate in receipt.Offers) if (candidate == definition) offered = true;
        if (!offered || definition.capacityCost < 0 || !PoolContains(receipt.Pool, definition))
        { reason = "This card is not offered by this opportunity."; return false; }
        var outgoing = string.IsNullOrEmpty(outgoingId) ? null :
            runDeck.Find(card => card.instanceId == outgoingId && !card.sacrificed && card.definition != null);
        if (!string.IsNullOrEmpty(outgoingId) && outgoing == null)
        { reason = "The selected card is no longer in the deck."; return false; }
        if (DeckCapacity - (outgoing != null ? outgoing.definition.capacityCost : 0) + definition.capacityCost > DeckCapacityLimit)
        { reason = "Replace a card or skip this reward."; return false; }
        if (outgoing != null) runDeck.Remove(outgoing);
        var added = new RunCardInstance { definition = definition }; runDeck.Add(added);
        receipt.GrantedCard = added; receipt.Resolved = true;
        DeckChanged?.Invoke(); return true;
    }
    public bool TryResolveOpportunityRecovery(string id)
    {
        var receipt = GetNonCombatReceipt(id);
        if (!IsCurrentNonCombat(id, MapEncounterKind.Opportunity) || receipt?.Outcome != MapOpportunityOutcome.Recovery ||
            receipt.Completed || playerMaxHealth <= 0) return false;
        ApplyRecovery(receipt, receipt.RecoveryPercent); return true;
    }
    public bool TryPrepareOpportunityBattle(string id)
    {
        var receipt = GetNonCombatReceipt(id);
        if (!IsCurrentNonCombat(id, MapEncounterKind.Opportunity) || receipt?.Outcome != MapOpportunityOutcome.Battle ||
            receipt.Resolved || receipt.Completed || !Progress.PrepareOpportunityBattle(id)) return false;
        receipt.BattleState = MapOpportunityBattleState.Loading; return true;
    }
    public bool CancelOpportunityBattle(string id)
    {
        if (!(Progress?.CancelOpportunityBattle(id) ?? false)) return false;
        var receipt = GetNonCombatReceipt(id);
        if (receipt != null) receipt.BattleState = MapOpportunityBattleState.None;
        return true;
    }
    public bool CompleteNonCombatEncounter(string id)
    {
        var receipt = GetNonCombatReceipt(id);
        if (receipt == null || receipt.Completed || !IsCurrentNonCombat(id, receipt.Kind) ||
            !receipt.Resolved || receipt.Outcome == MapOpportunityOutcome.Battle) return false;
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

    // Capture the authored main battle defaults once, before combat ticks or modifiers.
    public void ConfigureBattleDefaults(BattleScript player)
    {
        if (battleDefaultsConfigured || player == null) return;
        initialHealth = initialMaxHealth = Mathf.Max(1, Rules.startingMaxHealth);
        initialDamage = Mathf.Max(0, player.attackDmg);
        initialAttackSpeed = player.attackSpd;
        initialShield = 0;
        initialElixir = Mathf.Clamp(player.elixir, 0, player.maxElixir);
        initialMaxElixir = Mathf.Max(0, player.maxElixir);
        initialElixirRegen = player.elixirRegen;
        initialHitsPerAttack = player.hitsPerAttack;
        battleDefaultsConfigured = true;
        ResetCombatStats();
    }

    private void ResetCombatStats()
    {
        playerDamage = initialDamage;
        playerAttackSpeed = initialAttackSpeed;
        playerShield = 0;
        playerElixir = initialElixir;
        playerMaxElixir = initialMaxElixir;
    }

    public void ApplyPlayerStats(BattleScript player)
    {
        if (player == null) return;
        player.maxHealth = Mathf.Max(1, playerMaxHealth);
        player.health = Mathf.Clamp(playerHealth, 0, player.maxHealth);
        player.attackDmg = Mathf.Max(0, playerDamage);
        player.attackSpd = playerAttackSpeed;
        player.shield = 0;
        player.maxElixir = Mathf.Max(0f, playerMaxElixir);
        player.elixir = Mathf.Clamp(playerElixir, 0f, player.maxElixir);
        player.hitsPerAttack = initialHitsPerAttack;
        player.elixirRegen = initialElixirRegen;
    }

    public void CapturePlayerStats(BattleScript player)
    {
        if (player == null) return;
        // Health and maximum-health gains persist; temporary combat resources do not.
        playerMaxHealth = Mathf.Max(1, player.maxHealth);
        playerHealth = Mathf.Clamp(player.health, 0, playerMaxHealth);
    }

    public bool IncreaseMaxHealth(int amount, bool restoreAddedHealth = false)
    {
        if (amount <= 0 || Progress?.Phase == MapProgressPhase.Lost || Progress?.Phase == MapProgressPhase.Won) return false;
        var player = Progress?.CurrentEncounter?.IsCombat == true ? BattleHud.FindPlayer(UnityEngine.SceneManagement.SceneManager.GetActiveScene()) : null;
        if (player != null) CapturePlayerStats(player);
        if (playerHealth <= 0) return false;
        int added = Mathf.Min(amount, int.MaxValue - playerMaxHealth);
        playerMaxHealth += added;
        if (restoreAddedHealth) playerHealth += added;
        if (player != null) { player.maxHealth = playerMaxHealth; player.health = playerHealth; }
        return added > 0;
    }

    private void EnsurePlayerStatsInitialized()
    {
        if (playerMaxHealth > 0) return;
        playerHealth = 100;
        playerMaxHealth = 100;
        playerDamage = 9;
        playerAttackSpeed = 5f;
        playerShield = 0;
        playerElixir = 10f;
        playerMaxElixir = 10f;
    }
}
