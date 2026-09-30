using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using TMPro;

// Append new values at the end: prefabs store these as ints.
public enum StatType { Damage, AttackSpeed, Heal, Shield, Elixir, DamageAllEnemies, ExtraHits, EnemySlow, ElixirRegen }

public class BuffCards : MonoBehaviour, IPointerClickHandler, IPointerEnterHandler, IPointerExitHandler
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
    [SerializeField] private Image artworkImage;
    [SerializeField] private Image borderImage;

    private DeckManager owner;
    private CardDefinition definition;
    private TextMeshProUGUI costText;

    void Start()
    {
        player = BattleScript.FindFighters("Player")[0];
        // Card details show in the hover panel instead of on the card face.
        if (label) label.gameObject.SetActive(false);
    }

    void OnDisable()
    {
        CardInfoPanel.Hide(this);
    }

    public void OnPointerEnter(PointerEventData e)
    {
        string title = definition != null ? definition.displayName : name;
        string effect = definition != null && !string.IsNullOrEmpty(definition.combatEffectText)
            ? definition.combatEffectText
            : $"+{amount} {stat}";
        CardInfoPanel.Show(this, GetComponentInParent<Canvas>().rootCanvas, title, elixirCost, effect);
    }

    public void OnPointerExit(PointerEventData e)
    {
        CardInfoPanel.Hide(this);
    }

    public void Initialize(DeckManager deckManager, CardDefinition cardDefinition)
    {
        owner = deckManager;
        definition = cardDefinition;
        ApplyArt(cardDefinition);
    }

    public void ApplyArt(CardDefinition cardDefinition)
    {
        if (cardDefinition == null) return;
        if (artworkImage != null)
        {
            artworkImage.sprite = cardDefinition.frontArtwork;
            artworkImage.enabled = cardDefinition.frontArtwork != null;
        }
        if (borderImage != null)
        {
            borderImage.sprite = cardDefinition.frontBorder;
            borderImage.enabled = cardDefinition.frontBorder != null;
            ShowCost();
        }
    }

    private void ShowCost()
    {
        if (costText == null)
        {
            var costObject = new GameObject("CostText", typeof(RectTransform));
            costObject.transform.SetParent(borderImage.transform, false);
            RectTransform rect = (RectTransform)costObject.transform;
            // Centre of the small circle in the top-left corner of every border sprite.
            rect.anchorMin = rect.anchorMax = new Vector2(0.209f, 0.855f);
            rect.sizeDelta = new Vector2(26f, 26f);

            costText = costObject.AddComponent<TextMeshProUGUI>();
            GameFonts.Apply(costText);
            costText.alignment = TextAlignmentOptions.Center;
            costText.enableAutoSizing = true;
            costText.fontSizeMin = 8f;
            costText.fontSizeMax = 22f;
            costText.color = Color.white;
            costText.raycastTarget = false;
        }
        costText.text = elixirCost.ToString();
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
                case StatType.AttackSpeed:
                case StatType.ExtraHits:
                case StatType.ElixirRegen: SpawnEffect(player); break;
                case StatType.EnemySlow:
                    foreach (BattleScript enemy in LivingEnemies()) SpawnEffect(enemy);
                    break;
                case StatType.DamageAllEnemies:
                    foreach (BattleScript enemy in LivingEnemies()) enemy.TakeDamage((int)amount);
                    break;
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

    private void SpawnEffect(BattleScript target)
    {
        effectSpawn = Instantiate(effectPrefab);
        effectSpawn.GetComponent<Effect>().SetValues(stat, target, amount, effectDuration);
    }

    private static List<BattleScript> LivingEnemies()
    {
        return BattleScript.FindFighters("Enemy").FindAll(enemy => enemy.health > 0);
    }
    
    
    


}