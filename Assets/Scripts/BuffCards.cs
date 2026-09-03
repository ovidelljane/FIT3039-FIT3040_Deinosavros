using UnityEngine;
using UnityEngine.EventSystems;
using TMPro;

public enum StatType { Damage, AttackSpeed, Heal, Shield, Elixir  }

public class BuffCards : MonoBehaviour, IPointerClickHandler
{
    [SerializeField] StatType stat;
    [SerializeField] int amount = 1;
    [SerializeField] int elixirCost = 1;
    [SerializeField] float effectDuration = 3f;
    [SerializeField] TextMeshProUGUI label;

    BattleScript player;
    public GameObject effectPrefab;
    private GameObject effectSpawn;
    [SerializeField] private AudioSource audioSource;

    void Start()
    {
        player = GameObject.FindWithTag("Player").GetComponent<BattleScript>();
        if (label) label.text = $"+{amount} {stat}";
    }

    public void OnPointerClick(PointerEventData e)
    {
        if (player.elixir >= elixirCost)
        {
            if (stat != StatType.Elixir)
            {
                player.elixir -= elixirCost;
            }
            else
            {
                player.health -= elixirCost;
            }

            Debug.Log(player.elixir);
            
            switch (stat)
            {
                case StatType.Damage:
                case StatType.AttackSpeed: effectSpawn = Instantiate(effectPrefab);
                    effectSpawn.GetComponent<Effect>().SetValues(stat, player, amount, effectDuration); break;
                case StatType.Heal: player.health = Mathf.Min(player.health + amount, player.maxHealth); break;
                case StatType.Shield: player.shield += amount; break;
                case StatType.Elixir: player.elixir = Mathf.Min(player.elixir + amount, player.maxElixir); break;
                
            }
            AudioSource.PlayClipAtPoint(audioSource.clip, new Vector3(0f, 0f, 0f));
            Destroy(gameObject);
        }
    }
    
    
    


}