using System.Collections.Generic;
using UnityEngine;

public sealed class DeckManager : MonoBehaviour
{
    [SerializeField] private int maxHandSize = 5;
    [SerializeField] private int drawDelayTicks = 20;
    [SerializeField] private Transform handContainer;
    [SerializeField] private GameObject fallbackCardPrefab;
    [SerializeField] private GameObject[] legacyCards;

    private readonly List<CardDefinition> drawPile = new();
    private readonly List<CardDefinition> discardPile = new();
    private readonly List<BuffCards> hand = new();
    private readonly List<int> pendingDrawTicksRemaining = new();
    private RunSession initializedSession;
    public BattleScript Player { get; private set; }
    public bool IsReady { get; private set; }
    public IReadOnlyList<BuffCards> Hand => hand;

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
        if (RunSession.Instance != null) TryInitialize(RunSession.Instance, BattleHud.FindPlayer(gameObject.scene));
    }

    public bool TryInitialize(RunSession session, BattleScript player)
    {
        if (IsReady) return initializedSession == session && Player == player;
        if (session == null || player == null || !player.isActiveAndEnabled || handContainer == null ||
            !handContainer.gameObject.activeInHierarchy || player.gameObject.scene != gameObject.scene) return false;
        var definitions = session.GetActiveDeck();
        foreach (var definition in definitions)
        {
            var prefab = definition.combatPrefab != null ? definition.combatPrefab : fallbackCardPrefab;
            if (prefab == null || prefab.GetComponent<BuffCards>() == null) return false;
        }
        initializedSession = session; Player = player;
        foreach (var legacy in legacyCards ?? System.Array.Empty<GameObject>()) if (legacy != null) legacy.SetActive(false);
        drawPile.AddRange(Shuffle(definitions));
        for (int i = 0; i < maxHandSize; i++)
        {
            DrawOneCard();
        }
        IsReady = hand.Count == Mathf.Min(maxHandSize, definitions.Count);
        return IsReady;
    }

    private void HandleTick()
    {
        for (int i = pendingDrawTicksRemaining.Count - 1; i >= 0; i--)
        {
            pendingDrawTicksRemaining[i]--;
            if (pendingDrawTicksRemaining[i] <= 0)
            {
                pendingDrawTicksRemaining.RemoveAt(i);
                DrawOneCard();
            }
        }
    }

    public void OnCardPlayed(BuffCards playedView, CardDefinition definition)
    {
        if (!hand.Contains(playedView)) return;
        if (definition != null) discardPile.Add(definition);
        hand.Remove(playedView);
        Destroy(playedView.gameObject);

        if (hand.Count + pendingDrawTicksRemaining.Count < maxHandSize)
        {
            pendingDrawTicksRemaining.Add(drawDelayTicks);
        }
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
