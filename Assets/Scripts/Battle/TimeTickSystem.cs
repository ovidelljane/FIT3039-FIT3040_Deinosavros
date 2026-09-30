using System;
using UnityEngine;

public class TimeTickSystem : MonoBehaviour
{
    public static event Action OnTick;
    public static TimeTickSystem Active { get; private set; }
    public bool IsStarted => isStarted;

    [SerializeField, Min(.01f), Tooltip("Simulation step in seconds. Gameplay durations remain in seconds.")]
    private float tickRateMax = 0.1f;
    public float TickInterval => Mathf.Max(.01f, tickRateMax);
    public float TickDeltaSeconds { get; private set; }
    private double tickTimer;
    private bool isStarted = false;

    private void Awake() => Active = this;
    private void OnDestroy() { if (Active == this) Active = null; }

    public void StartTimer()
    {
        isStarted = true;
    }

    public void StopTimer()
    {
        isStarted = false;
        tickTimer = 0f;
    }

    void Update() => Advance(Time.deltaTime);

    private void Advance(float seconds)
    {
        if (!isStarted || seconds <= 0) return;
        tickTimer += seconds;
        while (isStarted && tickTimer + .000001f >= TickInterval)
        {
            TickDeltaSeconds = TickInterval;
            tickTimer = Math.Max(0, tickTimer - TickDeltaSeconds);
            OnTick?.Invoke();
        }
    }
}
