using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Deinosavros.MapTools.Editor
{
    // Value-only snapshots cannot write back into the live run or expose mutable route arrays.
    [Serializable]
    public sealed class MapMonitorSnapshot
    {
        public string capturedUtc, scene, runId, currentNode, selectedNode, hoveredNode;
        public string fromNode, targetNode, encounterId, kind, receiverKind, notice;
        public string testStartNode;
        public bool hasSession, hasProgress, busy, hasMap, canInteract, moving, hasCore;
        public MapProgressPhase phase;
        public int completed, capacity, health, maxHealth, damage, shield, particles;
        public float attackInterval, elixir, maxElixir;
        public bool sacrificeUsed;
        public PendingEncounterModifier pending;
        public Vector3 corePosition;
        public MapMonitorNode[] nodes = Array.Empty<MapMonitorNode>();
        public MapMonitorCard[] cards = Array.Empty<MapMonitorCard>();
        public MapMonitorReceipt receipt;
    }

    [Serializable]
    public sealed class MapMonitorNode
    {
        public string id;
        public MapEncounterKind kind;
        public MapLocationState state;
        public string[] next, traveled;
    }

    [Serializable]
    public sealed class MapMonitorCard
    {
        public string instanceId, cardId, name, assetPath;
        public int cost;
        public bool sacrificed, missing;
    }

    [Serializable]
    public sealed class MapMonitorReceipt
    {
        public string encounterId, nodeId, kind, grantedInstance, outcome, battleState;
        public int before, after, maxHealth, recovered, offers;
        public bool resolved, completed;
    }

    public static class MapMonitorData
    {
        public static MapMonitorSnapshot Capture(RunSession session, string scene,
            MapController map = null, MapTravelCoordinator travel = null)
        {
            var result = new MapMonitorSnapshot
            {
                capturedUtc = DateTime.UtcNow.ToString("O"), scene = scene,
                hasSession = session != null, hasMap = map != null
            };
            if (session == null) return result;
            var progress = session.Progress;
            result.hasProgress = progress != null;
            result.health = session.PlayerHealth; result.maxHealth = session.PlayerMaxHealth;
            result.damage = session.PlayerDamage; result.shield = session.PlayerShield;
            result.attackInterval = session.PlayerAttackSpeed;
            result.elixir = session.PlayerElixir; result.maxElixir = session.PlayerMaxElixir;
            result.capacity = session.DeckCapacity; result.sacrificeUsed = session.SacrificeUsed;
            result.pending = session.PendingModifier;
            result.selectedNode = session.SelectedNodeId;
            result.busy = travel != null && travel.IsBusy;
            result.notice = travel != null ? travel.LastNotice : null;
            result.cards = session.RunDeck.Select(c => new MapMonitorCard
            {
                instanceId = c.instanceId, sacrificed = c.sacrificed,
                missing = c.definition == null, cardId = c.definition != null ? c.definition.cardId : null,
                name = c.definition != null ? c.definition.displayName : "Missing definition",
                cost = c.definition != null ? c.definition.capacityCost : 0,
                assetPath = UnityEditor.AssetDatabase.GetAssetPath(c.definition)
            }).ToArray();
            if (map != null)
            {
                result.canInteract = map.CanInteract;
                result.hoveredNode = map.NodeInteraction != null ? map.NodeInteraction.HoveredNodeId : null;
                var view = map.TravelView;
                if (view != null)
                {
                    result.hasCore = true; result.corePosition = view.CorePosition;
                    result.moving = view.IsMoving; result.particles = view.LiveParticles;
                }
            }
            if (progress == null) return result;
            result.runId = progress.RunId; result.currentNode = progress.CurrentNodeId;
            result.testStartNode = progress.TestStartNodeId;
            result.phase = progress.Phase; result.completed = progress.CompletedCount;
            var ticket = progress.CurrentEncounter;
            if (ticket != null)
            {
                result.encounterId = ticket.EncounterId; result.fromNode = ticket.FromNodeId;
                result.targetNode = ticket.NodeId; result.kind = ticket.Kind.ToString();
                result.receiverKind = ticket.ReceiverKind.ToString();
            }
            result.nodes = progress.NodeIds.OrderBy(id => id, StringComparer.Ordinal).Select(id => new MapMonitorNode
            {
                id = id, kind = progress.KindOf(id), state = progress.NodeState(id),
                next = progress.NextNodes(id).ToArray(),
                traveled = progress.NextNodes(id).Where(next => progress.HasTraveled(id, next)).ToArray()
            }).ToArray();
            var receipt = session.GetNonCombatReceipt(ticket?.EncounterId) ?? session.LastNonCombatReceipt;
            if (receipt != null) result.receipt = new MapMonitorReceipt
            {
                encounterId = receipt.EncounterId, nodeId = receipt.NodeId, kind = receipt.Kind.ToString(),
                before = receipt.HealthBefore, after = receipt.HealthAfter, maxHealth = receipt.MaxHealth,
                recovered = receipt.Recovered, offers = receipt.Offers.Count,
                resolved = receipt.Resolved, completed = receipt.Completed,
                grantedInstance = receipt.GrantedCard?.instanceId,
                outcome = receipt.Outcome?.ToString(), battleState = receipt.BattleState.ToString()
            };
            return result;
        }

        public static bool CanRestart(MapMonitorSnapshot value, bool playing, bool paused, bool liveBinding)
        {
            return playing && !paused && liveBinding && value != null && value.hasProgress && value.hasMap &&
                !value.busy && (value.phase == MapProgressPhase.OnMap ? value.canInteract :
                    value.phase == MapProgressPhase.Won || value.phase == MapProgressPhase.Lost);
        }
    }

    [Serializable]
    public sealed class MapMonitorEntry
    {
        public string utc, category, message;
    }

    [Serializable]
    public sealed class MapMonitorHistory
    {
        public const int Limit = 200;
        public List<MapMonitorEntry> entries = new();
        [NonSerialized] private MapMonitorSnapshot previous;

        public void Add(string category, string message)
        {
            entries.Add(new MapMonitorEntry { utc = DateTime.UtcNow.ToString("O"), category = category, message = message });
            if (entries.Count > Limit) entries.RemoveRange(0, entries.Count - Limit);
        }

        public void Observe(MapMonitorSnapshot value)
        {
            if (value == null) return;
            var old = previous;
            previous = value;
            if (old == null || old.hasSession != value.hasSession)
                Add("Session", value.hasSession ? "Session attached." : "No live run session.");
            if (old == null || old.scene != value.scene) Add("Scene", value.scene ?? "No active scene");
            if (!value.hasProgress) return;
            if (old == null || !old.hasProgress || old.runId != value.runId)
                Add("Run", "Observing run " + value.runId + " at " + value.currentNode + " (" + value.phase + ").");
            else
            {
                if (old.phase != value.phase)
                {
                    string message = old.phase + " -> " + value.phase;
                    if (value.phase == MapProgressPhase.Traveling)
                        message += ": " + value.fromNode + " -> " + value.targetNode + " (" + value.kind + "), encounter " + value.encounterId;
                    else if (value.phase == MapProgressPhase.InEncounter) message += ": receiver confirmed " + value.currentNode;
                    else if (value.phase == MapProgressPhase.Lost) message += ": adventure failed";
                    else if (value.phase == MapProgressPhase.Won) message += ": Boss completed";
                    else if (value.phase == MapProgressPhase.OnMap && old.phase == MapProgressPhase.InEncounter)
                        message += ": completed " + old.targetNode;
                    else if (value.phase == MapProgressPhase.OnMap &&
                        (old.phase == MapProgressPhase.Traveling || old.phase == MapProgressPhase.Loading))
                        message += ": prepared travel canceled; origin retained";
                    Add("Progress", message);
                }
                if (old.currentNode != value.currentNode) Add("Location", old.currentNode + " -> " + value.currentNode);
            }
            if (!string.IsNullOrEmpty(value.testStartNode) && (old?.testStartNode != value.testStartNode || old.runId != value.runId))
                Add("Test Run", "Start seeded at " + value.testStartNode + "; earlier layers skipped without victories or rewards.");
            if (old == null || CardSignature(old) != CardSignature(value))
                Add("Deck", $"{value.cards.Count(c => !c.sacrificed)} active instances; capacity {value.capacity}/{RunSession.CapacityLimit}.");
            if (old == null || old.sacrificeUsed != value.sacrificeUsed || !old.pending.Equals(value.pending))
                Add("Offering", $"Used: {value.sacrificeUsed}; pending: " +
                    (value.pending.isValid ? value.pending.sourceCardId + " / " + value.pending.effectType + " / " + value.pending.magnitude : "None"));
            if (ReceiptSignature(old?.receipt) != ReceiptSignature(value.receipt) && value.receipt != null)
                Add("Receipt", $"{value.receipt.kind} {value.receipt.nodeId}: resolved {value.receipt.resolved}, completed {value.receipt.completed}, " +
                    $"outcome {value.receipt.outcome ?? "Recovery"}, battle {value.receipt.battleState}, " +
                    $"recovered {value.receipt.recovered}, granted instance {value.receipt.grantedInstance ?? "None"}.");
            if (!string.IsNullOrEmpty(value.notice) && old?.notice != value.notice) Add("Notice", value.notice);
        }

        private static string CardSignature(MapMonitorSnapshot value) => string.Join("|",
            value.cards.Select(c => c.instanceId + ":" + c.sacrificed + ":" + c.cardId + ":" + c.cost));
        private static string ReceiptSignature(MapMonitorReceipt r) => r == null ? "" :
            $"{r.encounterId}|{r.outcome}|{r.battleState}|{r.resolved}|{r.completed}|{r.after}|{r.grantedInstance}";
    }
}
