using UnityEngine;
using UnityEngine.EventSystems;
using TMPro;

public enum StatType { Damage, AttackSpeed, Heal, Shield, Elixir  }

public class BuffCards : MonoBehaviour, IPointerClickHandler
{
    [SerializeField] StatType stat;
    [SerializeField] int amount = 1;
    [SerializeField] int elixirCost = 1;
    [SerializeField] TextMeshProUGUI label;

    BattleScript player;

    void Start()
    {
        player = GameObject.FindWithTag("Player").GetComponent<BattleScript>();
        if (label) label.text = $"+{amount} {stat}";
    }

    public void OnPointerClick(PointerEventData e)
    {
        if (player.elixir >= elixirCost)
        {
            player.elixir -= elixirCost;
            Debug.Log(player.elixir);
            
            switch (stat)
            {
                case StatType.Damage: player.attackDmg += amount; break;
                case StatType.AttackSpeed: player.attackSpd -= amount; break;
                case StatType.Heal: player.health = Mathf.Min(player.health + amount, player.maxHealth); break;
            }

            Destroy(gameObject);
        }
    }
}