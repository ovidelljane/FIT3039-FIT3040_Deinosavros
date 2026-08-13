using System;
using UnityEngine;

public class TimeTickSystem : MonoBehaviour
{
    public static event Action OnTick;

    [SerializeField] private float tickRateMax = 0.1f; // 10 Ticks per second
    private float tickTimer;

    void Update()
    {
        tickTimer += Time.deltaTime;
        
        if (tickTimer >= tickRateMax)
        {
            tickTimer -= tickRateMax; // Maintain accurate pacing
            OnTick?.Invoke();        // Fire the event to all listeners
        }
    }
}
