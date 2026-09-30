using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using TMPro;

// Append new values at the end: prefabs store these as ints.
public enum StatType { Damage, AttackSpeed, Heal, Shield, Elixir, DamageAllEnemies, ExtraHits, EnemySlow, ElixirRegen }

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
    [SerializeField] private Image artworkImage;
    [SerializeField] private Image borderImage;

    private DeckManager owner;
    private CardDefinition definition;

    void Start()
    {
        player = GameObject.FindWithTag("Player").GetComponent<BattleScript>();
        if (label) label.text = definition != null && !string.IsNullOrEmpty(definition.combatEffectText)
            ? definition.combatEffectText
            : $"+{amount} {stat}";
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
        }
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
        var enemies = new List<BattleScript>();
        foreach (GameObject enemyObject in GameObject.FindGameObjectsWithTag("Enemy"))
        {
            BattleScript enemy = enemyObject.GetComponent<BattleScript>();
            if (enemy != null && enemy.health > 0) enemies.Add(enemy);
        }
        return enemies;
    }
    
    
    


}