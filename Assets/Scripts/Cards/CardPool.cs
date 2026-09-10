using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(menuName = "Deinosavros/Card Pool", fileName = "CardPool")]
public sealed class CardPool : ScriptableObject
{
    [SerializeField] private CardDefinition[] cards;

    public IReadOnlyList<CardDefinition> Cards => cards;

    public List<CardDefinition> GetRandomCards(int count)
    {
        var pool = new List<CardDefinition>(cards);
        var result = new List<CardDefinition>();
        count = Mathf.Min(count, pool.Count);

        for (int i = 0; i < count; i++)
        {
            int index = Random.Range(0, pool.Count);
            result.Add(pool[index]);
            pool.RemoveAt(index);
        }

        return result;
    }
}
