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
    [SerializeField] private int playerHealth = 100;
    [SerializeField] private int playerMaxHealth = 100;
    [SerializeField] private int playerDamage = 9;
    [SerializeField] private int playerAttackSpeed = 4;
    [SerializeField] private int playerShield;
    [SerializeField] private float playerElixir = 10f;
    [SerializeField] private float playerMaxElixir = 10f;

    public IReadOnlyList<RunCardInstance> RunDeck => runDeck;
    public bool SacrificeUsed => sacrificeUsed;
    public int PlayerHealth => playerHealth;
    public int PlayerMaxHealth => playerMaxHealth;
    public int PlayerDamage => playerDamage;
    public int PlayerAttackSpeed => playerAttackSpeed;
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

    public void AddCard(CardDefinition definition)
    {
        if (definition == null) return;
        runDeck.Add(new RunCardInstance { definition = definition });
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
        if (!pendingModifier.isValid) return false;
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
        player.attackSpd = Mathf.Max(1, playerAttackSpeed);
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
        playerAttackSpeed = Mathf.Max(1, player.attackSpd);
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
