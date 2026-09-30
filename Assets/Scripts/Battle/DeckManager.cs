using System.Collections.Generic;
using UnityEngine;

public sealed class DeckManager : MonoBehaviour
{
    [SerializeField] private int maxHandSize = 5;
    [SerializeField, Min(0)] private float drawDelaySeconds = 2f;
    [SerializeField] private Transform handContainer;
    [SerializeField] private GameObject fallbackCardPrefab;
    [SerializeField] private CardDefinition[] testDeck;

    private readonly List<CardDefinition> drawPile = new();
    private readonly List<CardDefinition> discardPile = new();
    private readonly List<BuffCards> hand = new();
    private readonly List<double> pendingDrawSecondsRemaining = new();
    private CardPileDisplay drawPileDisplay;
    private CardPileDisplay discardPileDisplay;
    private RunSession initializedSession;
    public BattleScript Player { get; private set; }
    public bool IsReady { get; private set; }
    public bool CombatEnabled { get; private set; }
    public IReadOnlyList<BuffCards> Hand => hand;
    public bool CanPlay => IsReady && CombatEnabled && Player != null && Player.health > 0 &&
        TimeTickSystem.Active != null && TimeTickSystem.Active.IsStarted;
    public void SetCombatEnabled(bool value) => CombatEnabled = value;

    private void OnEnable()
    {
        TimeTickSystem.OnTick += HandleTick;
    }

    private void OnDisable()
    {
        TimeTickSystem.OnTick -= HandleTick;
    }

    private void Start()
    {
        TryInitialize(RunSession.Instance, BattleHud.FindPlayer(gameObject.scene));
    }

    public bool TryInitialize(RunSession session, BattleScript player)
    {
        if (IsReady) return initializedSession == session && Player == player;
        if (player == null || !player.isActiveAndEnabled || player.gameObject.scene != gameObject.scene ||
            handContainer == null || !handContainer.gameObject.activeInHierarchy) return false;
        List<CardDefinition> activeDeck = session != null
            ? session.GetActiveDeck()
            : new List<CardDefinition>(testDeck ?? System.Array.Empty<CardDefinition>());
        foreach (var definition in activeDeck)
        {
            if (definition == null) return false;
            var prefab = definition.combatPrefab != null ? definition.combatPrefab : fallbackCardPrefab;
            if (prefab == null || prefab.GetComponent<BuffCards>() == null) return false;
        }
        initializedSession = session;
        Player = player;
        drawPile.AddRange(Shuffle(activeDeck));

        // Piles sit just before the hand so the hand and reward screen draw on top of them.
        int pileIndex = handContainer.GetSiblingIndex();
        drawPileDisplay = CardPileDisplay.Create(handContainer.parent, pileIndex, "Draw", true);
        discardPileDisplay = CardPileDisplay.Create(handContainer.parent, pileIndex, "Discard", false);

        for (int i = 0; i < maxHandSize; i++)
        {
            DrawOneCard();
        }
        RefreshPileCounts();
        IsReady = hand.Count == Mathf.Min(maxHandSize, activeDeck.Count);
        return IsReady;
    }

    private void RefreshPileCounts()
    {
        if (drawPileDisplay != null) drawPileDisplay.SetCount(drawPile.Count);
        if (discardPileDisplay != null) discardPileDisplay.SetCount(discardPile.Count);
    }

    private void HandleTick()
    {
        if (!CanPlay) return;
        for (int i = pendingDrawSecondsRemaining.Count - 1; i >= 0; i--)
        {
            pendingDrawSecondsRemaining[i] -= TimeTickSystem.Active.TickDeltaSeconds;
            if (pendingDrawSecondsRemaining[i] <= .000001f)
            {
                pendingDrawSecondsRemaining.RemoveAt(i);
                DrawOneCard();
            }
        }
    }

    public void OnCardPlayed(BuffCards playedView, CardDefinition definition)
    {
        if (!hand.Contains(playedView)) return;
        GameAudio.PlayCard(definition, playedView.LegacyPlayClip);
        if (definition != null) discardPile.Add(definition);
        hand.Remove(playedView);
        Destroy(playedView.gameObject);

        if (hand.Count + pendingDrawSecondsRemaining.Count < maxHandSize)
        {
            pendingDrawSecondsRemaining.Add(drawDelaySeconds);
        }
        RefreshPileCounts();
    }

    private void DrawOneCard()
    {
        if (drawPile.Count == 0)
        {
            if (discardPile.Count == 0) return;
            drawPile.AddRange(Shuffle(discardPile));
            discardPile.Clear();
        }

        int lastIndex = drawPile.Count - 1;
        CardDefinition definition = drawPile[lastIndex];
        drawPile.RemoveAt(lastIndex);
        RefreshPileCounts();

        GameObject prefab = definition.combatPrefab != null ? definition.combatPrefab : fallbackCardPrefab;
        if (prefab == null) return;

        GameObject instance = Instantiate(prefab, handContainer);
        BuffCards cardView = instance.GetComponent<BuffCards>();
        if (cardView != null)
        {
            cardView.Initialize(this, definition);
            hand.Add(cardView);
        }
    }

    private static List<CardDefinition> Shuffle(IReadOnlyList<CardDefinition> source)
    {
        var list = new List<CardDefinition>(source);
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = Random.Range(0, i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
        return list;
    }
}
