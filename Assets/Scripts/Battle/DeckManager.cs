using System.Collections.Generic;
using UnityEngine;

public sealed class DeckManager : MonoBehaviour
{
    [SerializeField] private int maxHandSize = 5;
    [SerializeField] private int drawDelayTicks = 20;
    [SerializeField] private Transform handContainer;
    [SerializeField] private GameObject fallbackCardPrefab;

    private readonly List<CardDefinition> drawPile = new();
    private readonly List<CardDefinition> discardPile = new();
    private readonly List<BuffCards> hand = new();
    private readonly List<int> pendingDrawTicksRemaining = new();

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
        drawPile.AddRange(Shuffle(RunSession.Instance.GetActiveDeck()));
        for (int i = 0; i < maxHandSize; i++)
        {
            DrawOneCard();
        }
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
