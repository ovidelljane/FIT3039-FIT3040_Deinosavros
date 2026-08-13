using UnityEngine;
using UnityEngine.EventSystems;
using TMPro;

public enum StatType { Damage, AttackSpeed, MaxHealth, Heal }

public class Card : MonoBehaviour, IPointerClickHandler
{
    [SerializeField] StatType stat;
    [SerializeField] int amount = 1;
    [SerializeField] TextMeshProUGUI label;

    BattleScript player;

    void Start()
    {
        player = GameObject.FindWithTag("Player").GetComponent<BattleScript>();
        if (label) label.text = $"+{amount} {stat}";
    }

    public void OnPointerClick(PointerEventData e)
    {
        switch (stat)
        {
            case StatType.Damage:      player.attackDmg += amount; break;
            case StatType.AttackSpeed: player.attackSpd -= amount; break;
            case StatType.MaxHealth:   player.maxHealth += amount;
                player.health += amount; break;
            case StatType.Heal:        player.health = Mathf.Min(player.health + amount, player.maxHealth); break;
        }
        Destroy(gameObject);
    }
}