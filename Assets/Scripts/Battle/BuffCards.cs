using UnityEngine;
using UnityEngine.EventSystems;
using TMPro;

public enum StatType { Damage, AttackSpeed, Heal, Shield, Elixir  }

public class BuffCards : MonoBehaviour, IPointerClickHandler
{
    [SerializeField] StatType stat;
    [SerializeField] float amount = 1;
    [SerializeField] int elixirCost = 1;
    [SerializeField] float effectDuration = 3f;
    [SerializeField] TextMeshProUGUI label;

    BattleScript player;
    public GameObject effectPrefab;
    private GameObject effectSpawn;
    [SerializeField] private AudioSource audioSource;

    private DeckManager owner;
    private CardDefinition definition;
    public CardDefinition Definition => definition;
    public BattleScript Target => player;

    void Start()
    {
        if (player == null) player = owner != null ? owner.Player : BattleHud.FindPlayer(gameObject.scene);
        if (label) label.text = $"+{amount} {stat}";
    }

    public void Initialize(DeckManager deckManager, CardDefinition cardDefinition)
    {
        owner = deckManager;
        definition = cardDefinition;
        player = deckManager != null ? deckManager.Player : BattleHud.FindPlayer(gameObject.scene);
    }

    public void OnPointerClick(PointerEventData e)
    {
        if (player == null || !player.isActiveAndEnabled || player.health <= 0 ||
            (RunSession.Instance?.Progress != null && RunSession.Instance.Progress.Phase != MapProgressPhase.InEncounter)) return;
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
                case StatType.Heal: player.health = Mathf.Min(player.health + (int)amount, player.maxHealth); break;
                case StatType.Shield: player.shield += (int)amount; break;
                case StatType.Elixir: player.elixir = Mathf.Min(player.elixir + amount, player.maxElixir); break;
                
            }
            if (audioSource != null && audioSource.clip != null)
                AudioSource.PlayClipAtPoint(audioSource.clip, new Vector3(0f, 0f, 0f));

            if (owner != null) owner.OnCardPlayed(this, definition);
            else Destroy(gameObject);
        }
    }
    
    
    


}
