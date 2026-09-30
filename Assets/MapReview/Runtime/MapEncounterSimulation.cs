using System;
using System.Collections.Generic;

namespace Deinosavros.MapReview
{
    // Deliberately independent of BattleScript. Temporary layers never enter the result DTO.
    public sealed class MapEncounterSimulation
    {
        private readonly MapEncounterRequest request;
        private readonly Dictionary<string, int> enemyInitialHealth = new();
        private MapPlayerState persistent;
        private MapEncounterResult completedResult;
        public int TemporaryShield { get; private set; }
        public MapPlayerState PersistentPlayer => persistent;
        public float EffectiveAttackInterval { get; }
        public IReadOnlyDictionary<string, int> Enemies => enemyInitialHealth;
        public bool IsCompleted => completedResult != null;
        public MapEncounterSimulation(MapEncounterRequest startedRequest)
        {
            request = startedRequest ?? throw new ArgumentNullException(nameof(startedRequest));
            if (!request.IsAcknowledged)
                throw new InvalidOperationException("The encounter must be acknowledged before the receiver applies effects.");
            persistent = request.StartedPersistentPlayer;
            TemporaryShield = Has(OverworldEffectType.GrantStartingShield) ? 5 : 0;
            EffectiveAttackInterval = Has(OverworldEffectType.ImprovePlayerAttackSpeed)
                ? Math.Max(1, persistent.AttackInterval - 1) : persistent.AttackInterval;
        }
        private bool Has(OverworldEffectType type) => request.Effect.IsValid && request.Effect.Type == type;
        public int SpawnEnemy(string instanceId, int initialHealth)
        {
            if (string.IsNullOrEmpty(instanceId)) throw new ArgumentException("Enemy instance ID is required.");
            if (enemyInitialHealth.TryGetValue(instanceId, out int health)) return health;
            if (IsCompleted) throw new InvalidOperationException("A completed encounter cannot spawn new enemies.");
            int initial = Math.Max(1, initialHealth);
            health = Has(OverworldEffectType.ReduceEnemyStartingHealth) ? (int)Math.Max(1, (initial * 3L + 3) / 4) : initial;
            enemyInitialHealth.Add(instanceId, health); return health;
        }
        public void TakeDamage(int amount)
        {
            if (IsCompleted) return;
            int remaining = Math.Max(0, amount);
            int temporary = Math.Min(remaining, TemporaryShield); TemporaryShield -= temporary; remaining -= temporary;
            int shield = Math.Min(remaining, persistent.Shield); remaining -= shield;
            persistent = new MapPlayerState(Math.Max(0, persistent.Health - remaining), persistent.MaxHealth,
                persistent.Damage, persistent.AttackInterval, persistent.Shield - shield, persistent.Elixir, persistent.MaxElixir);
        }
        public bool SpendElixir(float amount)
        {
            if (IsCompleted || float.IsNaN(amount) || float.IsInfinity(amount) || amount < 0 || amount > persistent.Elixir) return false;
            persistent = new MapPlayerState(persistent.Health, persistent.MaxHealth, persistent.Damage,
                persistent.AttackInterval, persistent.Shield, persistent.Elixir - amount, persistent.MaxElixir);
            return true;
        }
        public MapEncounterResult Result(bool victory)
        {
            if (completedResult != null) return completedResult;
            TemporaryShield = 0;
            completedResult = new MapEncounterResult(request.RunId, request.EncounterId, victory, persistent);
            return completedResult;
        }
    }
}
