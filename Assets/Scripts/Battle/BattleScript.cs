using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class BattleScript : MonoBehaviour
{
    public static event Action OnAllEnemiesDefeated;
    public static event Action<BattleScript, bool> OnBattleFinished;
    public static event Action<BattleScript, BattleScript> OnAttackPerformed;
    public static event Action<BattleScript, BattleScript, int, int> OnDamageReceived;

    public int attackDmg = 1;
    public float attackSpd = 5f;
    public int health = 20;
    public int maxHealth = 20;
    public float elixir = 10f;
    public float maxElixir = 10f;
    [Tooltip("Elixir restored per second, independent of the simulation step.")]
    public float elixirRegen = 0.5f;
    public int shield = 0;
    public int hitsPerAttack = 1;

    private double attackElapsed;
    // Read-only presentation values; the tick loop remains the only attack scheduler.
    public float AttackIntervalSeconds => Mathf.Max(.01f, attackSpd);
    public float SecondsUntilNextAttack => Mathf.Max(0, AttackIntervalSeconds - (float)attackElapsed);
    private bool _victoryFired;
    
    List<GameObject> _OpponentList;
    public BattleScript opponentScript;
    private Renderer _renderer;
    [SerializeField] private AudioSource audioSource;
    
    private Vector3 startPosition;

    private void Start()
    {
        if (CompareTag("Player") && RunSession.Instance == null)
            health = maxHealth = Mathf.Max(1, RunSettings.ForScene(gameObject.scene).startingMaxHealth);
    }
    
    
    void OnEnable()
    {
        _renderer = gameObject.GetComponent<Renderer>();
        _OpponentList = new List<GameObject>();
        startPosition = transform.position;
        
        if (gameObject.CompareTag("Player"))
        {
            _OpponentList.AddRange(FindFighters("Enemy").Select(fighter => fighter.gameObject));
            Debug.unityLogger.Log("Player Opponent List", _OpponentList);
        }
        else
        {
            _OpponentList.AddRange(FindFighters("Player").Select(fighter => fighter.gameObject));
            Debug.unityLogger.Log("Enemy Opponent List", _OpponentList);
        }

        TimeTickSystem.OnTick += HandleTick;

    }

    // Shadow copies share the Player/Enemy tag but have their BattleScript disabled, so skip them.
    public static List<BattleScript> FindFighters(string tag)
    {
        var fighters = new List<BattleScript>();
        foreach (GameObject candidate in GameObject.FindGameObjectsWithTag(tag))
        {
            BattleScript fighter = candidate.GetComponent<BattleScript>();
            if (fighter != null && fighter.enabled) fighters.Add(fighter);
        }
        return fighters;
    }
    
    private void HandleTick()
    {
        if (!isActiveAndEnabled || TimeTickSystem.Active == null || !TimeTickSystem.Active.IsStarted) return;
        if (health <= 0) { FinishDeath(); return; }
        enemyListManager();
        float seconds = TimeTickSystem.Active.TickDeltaSeconds;
        attackElapsed += seconds;
        elixir = Mathf.Min(elixir + elixirRegen * seconds, maxElixir);

        while (attackElapsed + .000001f >= Mathf.Max(.01f, attackSpd) && enemyListManager() > 0)
        {
            attackElapsed = Math.Max(0, attackElapsed - Mathf.Max(.01f, attackSpd));
            Attack();
            if (!CombatFeedback.IsPresent(gameObject.scene)) StartCoroutine(Bounce());
            if (audioSource != null) audioSource.Play();
        }

        if (enemyListManager() == 0)
        {
            if (_renderer != null && !CombatFeedback.IsPresent(gameObject.scene)) _renderer.material.color = Color.lawnGreen;
            gameObject.GetComponent<BattleScript>().enabled = false;

            if (CompareTag("Player") && !_victoryFired)
            {
                _victoryFired = true;
                OnBattleFinished?.Invoke(this, health > 0);
                OnAllEnemiesDefeated?.Invoke();
            }
        }
        
        if (health <= 0) FinishDeath();
    }

    private void FinishDeath()
    {
        health = 0;
        if (_renderer != null && !CombatFeedback.IsPresent(gameObject.scene)) _renderer.material.color = Color.red;
        attackElapsed = 0;
        if (CompareTag("Player") && !_victoryFired)
        {
            _victoryFired = true;
            OnBattleFinished?.Invoke(this, false);
        }
        enabled = false;
        Invoke(nameof(Disable), 5f);
    }

    private void Disable()
    {
        gameObject.SetActive(false);
    }
    
    void OnDisable()
    {
        TimeTickSystem.OnTick -= HandleTick;
    }

    private void Attack()
    {
        GameObject opponent = _OpponentList[0];
        opponentScript = opponent.GetComponent<BattleScript>();
        OnAttackPerformed?.Invoke(this, opponentScript);
        for (int i = 0; i < Mathf.Max(1, hitsPerAttack); i++)
        {
            opponentScript.TakeDamage(attackDmg, this);
        }
    }

    public void TakeDamage(int damage) => TakeDamage(damage, null);

    public void TakeDamage(int damage, BattleScript source)
    {
        int beforeHealth = Mathf.Max(0, health), beforeShield = Mathf.Max(0, shield);
        if (shield > 0)
        {
            if (shield <= damage)
            {
                int tempAttack = damage - shield;
                shield = 0;
                health -= tempAttack;
            }
            else
            {
                shield -= damage;
            }
        }
        else
        {
            health -= damage;
        }
        int healthLost = Mathf.Max(0, beforeHealth - Mathf.Max(0, health));
        int shieldLost = Mathf.Max(0, beforeShield - Mathf.Max(0, shield));
        if (beforeHealth > 0 && (healthLost > 0 || shieldLost > 0))
            OnDamageReceived?.Invoke(this, source, healthLost, shieldLost);
        if (health <= 0) FinishDeath();
    }

    private int enemyListManager()
    {
        foreach (GameObject opponent in _OpponentList.ToList())
        {
            if (opponent == null) { _OpponentList.Remove(opponent); continue; }
            opponentScript = opponent.GetComponent<BattleScript>();

            if (opponentScript == null || opponentScript.health <= 0 || !opponentScript.isActiveAndEnabled)
            {
                _OpponentList.Remove(opponent);
            }
        }
        
        return _OpponentList.Count;
    } 
    
    IEnumerator Bounce()
    {
        Vector3 a = transform.position, b = a - transform.right * 1f;
        for (float t = 0; t < 1; t += Time.deltaTime * 8) { transform.position = Vector3.Lerp(a, b, t); yield return null; }
        for (float t = 0; t < 1; t += Time.deltaTime * 5) { transform.position = Vector3.Lerp(b, a, t); yield return null; }
        transform.position = a;
    }
}
