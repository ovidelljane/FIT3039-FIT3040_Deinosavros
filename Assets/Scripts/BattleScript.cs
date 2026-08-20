using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class BattleScript : MonoBehaviour
{
    public int attackDmg = 1;
    public int attackSpd = 5;
    public int health = 10;
    public int maxHealth = 10;
    public float elixir = 10f;
    public float maxElixir = 10f;
    public float elixirRegen = 0.05f;
    
    private int _currentTickCount;
    private int _attackTick;
    
    List<GameObject> _OpponentList;
    public BattleScript opponentScript;
    private Renderer _renderer;
    
    private Vector3 startPosition;
    
    
    void OnEnable()
    {
        _renderer = gameObject.GetComponent<Renderer>();
        _OpponentList = new List<GameObject>();
        startPosition = transform.position;
        
        if (gameObject.CompareTag("Player"))
        {
            _OpponentList.AddRange(GameObject.FindGameObjectsWithTag("Enemy"));
            Debug.unityLogger.Log("Player Opponent List", _OpponentList);
        }
        else
        {
            _OpponentList.AddRange(GameObject.FindGameObjectsWithTag("Player"));
            Debug.unityLogger.Log("Enemy Opponent List", _OpponentList);
        }

        TimeTickSystem.OnTick += HandleTick;
        
    }
    
    private void HandleTick()
    {
        _currentTickCount++;
        _attackTick = attackSpd * 10;

        elixir = Mathf.Min(elixir + elixirRegen, maxElixir);

        if (_currentTickCount == _attackTick && _OpponentList.Count > 0)
        {
            Attack();
            StartCoroutine(Bounce());
            _currentTickCount = 0;
        }

        if (enemyListManager() == 0)
        {
            _renderer.material.color = Color.lawnGreen;
            gameObject.GetComponent<BattleScript>().enabled = false;
        }
        
        if (health <= 0)
        {
            health = 0;
            _renderer.material.color = Color.red;
            _currentTickCount = 0;
            attackDmg = 0;

            Invoke("Disable", 5f);
        }
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

        opponentScript.health -= attackDmg;
    }

    private int enemyListManager()
    {
        foreach (GameObject opponent in _OpponentList.ToList())
        {
            opponentScript = opponent.GetComponent<BattleScript>();

            if (opponentScript.health <= 0)
            {
                _OpponentList.Remove(opponent);
            }
        }
        
        return _OpponentList.Count;
    } 
    
    IEnumerator Bounce()
    {
        Vector3 a = transform.position, b = a - transform.forward * 1f;
        for (float t = 0; t < 1; t += Time.deltaTime * 8) { transform.position = Vector3.Lerp(a, b, t); yield return null; }
        for (float t = 0; t < 1; t += Time.deltaTime * 5) { transform.position = Vector3.Lerp(b, a, t); yield return null; }
        transform.position = a;
    }
}