using System;
using System.Collections.Generic;
using System.Linq;

namespace Deinosavros.MapReview
{
    public enum MapRunPhase { OnMap, Prepared, InEncounter, Won, Lost }
    public enum MapNodeState { Locked, Available, Completed, Skipped }

    public sealed class MapPlayerState
    {
        public int Health { get; }
        public int MaxHealth { get; }
        public int Damage { get; }
        public float AttackInterval { get; }
        public int Shield { get; }
        public float Elixir { get; }
        public float MaxElixir { get; }
        public MapPlayerState(int health = 100, int maxHealth = 100, int damage = 9,
            float attackInterval = 4, int shield = 0, float elixir = 10, float maxElixir = 10)
        {
            if (maxHealth < 1 || health < 0 || health > maxHealth || damage < 0 || shield < 0 ||
                !Finite(attackInterval) || attackInterval <= 0 || !Finite(elixir) || !Finite(maxElixir) ||
                elixir < 0 || maxElixir < 0 || elixir > maxElixir)
                throw new ArgumentOutOfRangeException(nameof(health), "Invalid persistent player state.");
            Health = health; MaxHealth = maxHealth; Damage = damage; AttackInterval = attackInterval;
            Shield = shield; Elixir = elixir; MaxElixir = maxElixir;
        }
        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        public MapPlayerState WithElixirBonus() => new(Health, MaxHealth, Damage, AttackInterval, Shield,
            Math.Min(Elixir + 3, MaxElixir + 3), MaxElixir + 3);
    }

    public sealed class MapRunCard
    {
        public string InstanceId { get; }
        public CardDefinition Definition { get; }
        public bool Sacrificed { get; private set; }
        internal MapRunCard(CardDefinition definition)
        {
            InstanceId = Guid.NewGuid().ToString("N"); Definition = definition;
        }
        private MapRunCard(MapRunCard source)
        {
            InstanceId = source.InstanceId; Definition = source.Definition; Sacrificed = source.Sacrificed;
        }
        internal MapRunCard Snapshot() => new(this);
        internal void Sacrifice() => Sacrificed = true;
    }

    public readonly struct MapSacrificeEffect
    {
        public readonly string SourceInstanceId;
        public readonly OverworldEffectType Type;
        public bool IsValid => !string.IsNullOrEmpty(SourceInstanceId);
        public MapSacrificeEffect(string id, OverworldEffectType type) { SourceInstanceId = id; Type = type; }
        public string Description => !IsValid ? "No pending offering" : Type switch
        {
            OverworldEffectType.ReduceEnemyStartingHealth => "Enemies enter at 75% initial HP, including later waves.",
            OverworldEffectType.ImprovePlayerAttackSpeed => "Attack interval -1 second (minimum 1 second), next encounter only.",
            OverworldEffectType.GrantStartingShield => "+5 temporary shield, next encounter only.",
            OverworldEffectType.IncreaseStartingElixir => "+3 maximum Elixir for this run; restore 3 once on encounter start.",
            _ => "Unsupported offering"
        };
    }

    public sealed class MapEncounterRequest
    {
        public string RunId { get; }
        public string EncounterId { get; }
        public string NodeId { get; }
        public IReadOnlyList<MapRunCard> Deck { get; }
        public MapPlayerState PersistentPlayer { get; }
        public MapSacrificeEffect Effect { get; }
        public bool IsAcknowledged { get; private set; }
        internal MapEncounterRequest(string runId, string nodeId, IEnumerable<MapRunCard> deck,
            MapPlayerState player, MapSacrificeEffect effect)
        {
            RunId = runId; EncounterId = Guid.NewGuid().ToString("N"); NodeId = nodeId;
            // Later rewards and offerings must never rewrite a prepared encounter's deck.
            Deck = Array.AsReadOnly(deck.Select(card => card.Snapshot()).ToArray());
            PersistentPlayer = player; Effect = effect;
        }
        internal void Acknowledge() => IsAcknowledged = true;
        public MapPlayerState StartedPersistentPlayer => Effect.IsValid &&
            Effect.Type == OverworldEffectType.IncreaseStartingElixir ? PersistentPlayer.WithElixirBonus() : PersistentPlayer;
    }

    public sealed class MapEncounterResult
    {
        public string RunId { get; }
        public string EncounterId { get; }
        public bool Victory { get; }
        public MapPlayerState PersistentPlayer { get; }
        public MapEncounterResult(string runId, string encounterId, bool victory, MapPlayerState player)
        { RunId = runId; EncounterId = encounterId; Victory = victory; PersistentPlayer = player; }
    }

    public sealed class MapRunState
    {
        private readonly List<MapRunCard> cards = new();
        private readonly HashSet<string> available = new();
        private readonly HashSet<string> completed = new();
        private readonly Dictionary<string, MapGraphNode> nodes = new();
        private int passedLayer;
        public string RunId { get; private set; }
        public MapRunPhase Phase { get; private set; }
        public MapPlayerState Player { get; private set; }
        public bool SacrificeUsed { get; private set; }
        public MapSacrificeEffect PendingEffect { get; private set; }
        public MapEncounterRequest CurrentEncounter { get; private set; }
        public IReadOnlyList<MapRunCard> Cards => cards.AsReadOnly();
        public int ActiveCapacity => (int)Math.Min(int.MaxValue, cards.Where(c => !c.Sacrificed && c.Definition != null)
            .Sum(c => (long)Math.Max(0, c.Definition.capacityCost)));
        public event Action Changed;

        public void BeginNewRun(MapGraphDefinition graph, IEnumerable<CardDefinition> deck, MapPlayerState player = null)
        {
            if (graph == null) throw new ArgumentNullException(nameof(graph));
            if (deck == null) throw new ArgumentNullException(nameof(deck));
            graph.Validate();
            var definitions = deck.Where(c => c != null).ToArray();
            nodes.Clear();
            foreach (var node in graph.nodes)
                nodes.Add(node.id, new MapGraphNode { id = node.id, layer = node.layer, boss = node.boss,
                    anchor = node.anchor, terrace = node.terrace, next = (string[])node.next.Clone() });
            RunId = Guid.NewGuid().ToString("N"); Phase = MapRunPhase.OnMap;
            Player = player ?? new MapPlayerState(); CurrentEncounter = null;
            SacrificeUsed = false; PendingEffect = default; passedLayer = 0;
            completed.Clear(); available.Clear(); available.Add(nodes.Values.Single(n => n.layer == 1).id);
            cards.Clear(); foreach (var definition in definitions) cards.Add(new MapRunCard(definition));
            Changed?.Invoke();
        }

        public MapNodeState NodeState(string id)
        {
            if (string.IsNullOrEmpty(id) || !nodes.TryGetValue(id, out var node)) return MapNodeState.Locked;
            if (completed.Contains(id)) return MapNodeState.Completed;
            if (available.Contains(id) && Phase != MapRunPhase.Won && Phase != MapRunPhase.Lost) return MapNodeState.Available;
            return node.layer <= passedLayer ? MapNodeState.Skipped : MapNodeState.Locked;
        }

        public bool TrySacrifice(string instanceId)
        {
            if (Phase != MapRunPhase.OnMap || SacrificeUsed || Player == null) return false;
            var card = cards.Find(c => c.InstanceId == instanceId && !c.Sacrificed);
            if (card == null || card.Definition == null ||
                !Enum.IsDefined(typeof(OverworldEffectType), card.Definition.overworldEffect.effectType)) return false;
            var effect = new MapSacrificeEffect(card.InstanceId, card.Definition.overworldEffect.effectType);
            card.Sacrifice(); SacrificeUsed = true; PendingEffect = effect;
            Changed?.Invoke(); return true;
        }

        public bool TryPrepareEncounter(string nodeId, out MapEncounterRequest request)
        {
            request = null;
            if (Phase != MapRunPhase.OnMap || Player == null || string.IsNullOrEmpty(nodeId) || !available.Contains(nodeId)) return false;
            request = new MapEncounterRequest(RunId, nodeId, cards.Where(c => !c.Sacrificed), Player, PendingEffect);
            CurrentEncounter = request; Phase = MapRunPhase.Prepared;
            Changed?.Invoke(); return true;
        }

        public bool AcknowledgeEncounterStarted(string encounterId)
        {
            if (Phase != MapRunPhase.Prepared || CurrentEncounter == null || CurrentEncounter.EncounterId != encounterId) return false;
            Player = CurrentEncounter.StartedPersistentPlayer;
            CurrentEncounter.Acknowledge();
            PendingEffect = default; Phase = MapRunPhase.InEncounter;
            Changed?.Invoke(); return true;
        }

        public bool CancelPreparedEncounter(string encounterId)
        {
            if (Phase != MapRunPhase.Prepared || CurrentEncounter == null || CurrentEncounter.EncounterId != encounterId) return false;
            CurrentEncounter = null; Phase = MapRunPhase.OnMap;
            Changed?.Invoke(); return true;
        }

        public bool CompleteEncounter(MapEncounterResult result)
        {
            if (result == null || Phase != MapRunPhase.InEncounter || CurrentEncounter == null || result.RunId != RunId ||
                result.EncounterId != CurrentEncounter?.EncounterId || result.PersistentPlayer == null) return false;
            // A receiver cannot accidentally roll back or reapply the permanent offering.
            if (Math.Abs(result.PersistentPlayer.MaxElixir - Player.MaxElixir) > .0001f) return false;
            var node = nodes[CurrentEncounter.NodeId]; Player = result.PersistentPlayer;
            available.Clear(); CurrentEncounter = null;
            if (!result.Victory) Phase = MapRunPhase.Lost;
            else
            {
                completed.Add(node.id); passedLayer = node.layer;
                if (node.boss) Phase = MapRunPhase.Won;
                else
                {
                    Phase = MapRunPhase.OnMap; SacrificeUsed = false;
                    foreach (string next in node.next) available.Add(next);
                }
            }
            Changed?.Invoke(); return true;
        }

        public MapRunCard AddCard(CardDefinition definition)
        {
            if (definition == null || Player == null || Phase == MapRunPhase.Prepared || Phase == MapRunPhase.Won || Phase == MapRunPhase.Lost) return null;
            var card = new MapRunCard(definition); cards.Add(card); Changed?.Invoke(); return card;
        }
    }
}
