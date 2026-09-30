using System;
using System.IO;
using System.Linq;
using Deinosavros.MapReview;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

public static class RunIntegrationTests
{
    private static int assertions;

    [MenuItem("Tools/Map/Verify Cards and Run Transactions")]
    public static void Run()
    {
        assertions = 0;
        var existing = RunSession.Instance;
        var graph = AssetDatabase.LoadAssetAtPath<MapGraphDefinition>("Assets/Art/Map/Settings/Graph.asset");
        var catalog = AssetDatabase.LoadAssetAtPath<MapNodeCatalog>("Assets/Art/Map/Settings/Nodes.asset");
        var pool = AssetDatabase.LoadAssetAtPath<CardPool>("Assets/Data/CardPool.asset");
        VerifyCardBalance(pool);
        var card = pool.Cards.OrderByDescending(definition => definition.capacityCost).First();
        const int fullCount = 6;
        var root = new GameObject("Run Transaction Fixture") { hideFlags = HideFlags.HideAndDontSave };
        root.SetActive(false);
        var session = root.AddComponent<RunSession>();
        var rules = SetFixtureRules(session, fullCount * card.capacityCost);
        var player = root.AddComponent<BattleScript>();
        session.InitializeProgress(graph, catalog);
        try
        {
            for (int i = 0; i < fullCount; i++) Check(session.TryAddCard(card, out _), "Maximum-cost cards fill the capacity exactly.");
            Check(session.DeckCapacity == session.DeckCapacityLimit && !session.TryAddCard(card, out _), "Configured capacity is enforced.");
            Check(session.RunDeck.Select(instance => instance.instanceId).Distinct().Count() == fullCount, "Duplicate definitions retain unique IDs.");
            string sacrificed = session.RunDeck[2].instanceId;
            Check(session.TrySacrificeInstance(sacrificed) && session.DeckCapacity == session.DeckCapacityLimit - card.capacityCost, "Sacrifice removes only the selected instance from capacity.");
            Check(!session.TrySacrifice(card), "A second sacrifice before combat is rejected.");
            Check(session.TryBeginTravel("level_01_01", out var canceled), "The introductory combat uses the entrance ticket.");
            Check(session.CancelPreparedTravel(canceled.EncounterId) && session.HasPendingModifier, "Canceled entry preserves the offering.");
            var first = Start(session, "level_01_01");
            Check(session.TryConsumePendingModifier(out _) && !session.TryConsumePendingModifier(out _), "An offering is consumed once in actual combat.");
            Check(session.TryPrepareBattleReward(first, pool, 3, out var offers) && offers.Count == 3, "Victory prepares three fixed offers.");
            int expectedCapacity = session.DeckCapacity + offers[0].capacityCost;
            Check(session.TryResolveBattleReward(first, offers[0], null, out _) && session.DeckCapacity == expectedCapacity, "A freed slot accepts a fitting reward.");
            Check(!session.TryResolveBattleReward(first, offers[1], null, out _), "Duplicate reward claims are rejected.");
            Check(session.CompleteEncounter(first, true), "Claim completion advances the entrance.");
            Check(graph.Find("level_01_01").next.All(id => session.Progress.NodeState(id) == MapLocationState.Available) &&
                session.Progress.CompletedCount == 1, "Exactly the entrance's three successors open.");
            Check(!session.HasPendingModifier && !session.SacrificeUsed, "Combat completion clears the modifier and resets the offering allowance.");

            var second = Start(session, "level_02_03");
            session.TryPrepareBattleReward(second, pool, 3, out offers);
            string[] before = session.GetActiveDeck().Select(definition => definition.cardId).ToArray();
            Check(!session.TryResolveBattleReward(second, offers[0], "missing-instance", out _), "Invalid replacement is rejected atomically.");
            Check(before.SequenceEqual(session.GetActiveDeck().Select(definition => definition.cardId)), "Failed replacement leaves all cards unchanged.");
            var outgoing = session.RunDeck.First(instance => !instance.sacrificed);
            expectedCapacity = session.DeckCapacity - outgoing.definition.capacityCost + offers[0].capacityCost;
            Check(session.TryResolveBattleReward(second, offers[0], outgoing.instanceId, out _) &&
                !session.RunDeck.Any(instance => instance.instanceId == outgoing.instanceId) && session.DeckCapacity == expectedCapacity,
                "Replacement removes one exact instance without exceeding capacity.");
            Check(!session.SacrificeUsed && !session.HasPendingModifier, "Ordinary replacement never creates an offering.");
            Check(!session.TryResolveBattleReward(first, null, null, out _), "A previous encounter cannot claim the current reward.");
            session.CompleteEncounter(second, true);

            session.Progress.BeginNewRun(graph, catalog);
            string entrance = Start(session, "level_01_01"); session.CompleteEncounter(entrance, true);
            Check(session.TrySacrifice(card), "A new combat interval permits a new offering.");
            Check(session.TryBeginTravel("level_02_01", out var chance) && session.Progress.MarkLoading(chance.EncounterId), "Opportunity entry is routed normally.");
            Check(session.TryPrepareOpportunity(chance.EncounterId, pool, Array.Empty<CardDefinition>()), "The opportunity placeholder prepares an empty receipt.");
            session.ConfirmEncounterStarted(chance.EncounterId);
            Check(session.GetNonCombatReceipt(chance.EncounterId).Offers.Count == 0 && !session.TryConsumePendingModifier(out _),
                "An empty test reward consumes no offering.");
            Check(session.TryResolveOpportunityReward(chance.EncounterId, null, null, out _), "A test reward can be skipped explicitly.");
            Check(session.CompleteNonCombatEncounter(chance.EncounterId) && session.HasPendingModifier && session.SacrificeUsed,
                "Non-combat completion retains the pending offering and its usage limit.");
            Check(!session.CompleteNonCombatEncounter(chance.EncounterId), "Non-combat completion is idempotent.");

            session.Progress.BeginNewRun(graph, catalog);
            entrance = Start(session, "level_01_01"); session.CompleteEncounter(entrance, true);
            player.maxHealth = 100; player.health = 40; player.attackDmg = 9; player.attackSpd = 5;
            player.maxElixir = 10; player.elixir = 10; player.elixirRegen = .5f; player.hitsPerAttack = 1;
            session.ConfigureBattleDefaults(player); session.CapturePlayerStats(player);
            string recovery = Start(session, "level_02_02");
            Check(session.TryResolveRecovery(recovery, out var receipt) && receipt.HealthBefore == 40 && receipt.HealthAfter == 55,
                "Recovery restores exactly fifteen percent of maximum HP.");
            Check(session.TryResolveRecovery(recovery, out _) && session.PlayerHealth == 55, "Repeated recovery does not heal again.");
            session.CompleteNonCombatEncounter(recovery);
            player.health = 55; player.attackDmg = 99; player.attackSpd = 1; player.shield = 35;
            player.maxElixir = 50; player.elixir = 2; player.hitsPerAttack = 7; player.elixirRegen = 9;
            session.CapturePlayerStats(player); session.ApplyPlayerStats(player);
            Check(player.health == 55 && player.attackDmg == 9 && Mathf.Approximately(player.attackSpd, 5) &&
                player.shield == 0 && player.elixir == 10 && player.maxElixir == 10 && player.hitsPerAttack == 1 &&
                Mathf.Approximately(player.elixirRegen, .5f), "Only HP survives between battles; all combat resources and temporary attributes reset.");
            player.health = 100; session.CapturePlayerStats(player);
            session.Progress.BeginNewRun(graph, catalog);
            entrance = Start(session, "level_01_01"); session.CompleteEncounter(entrance, true);
            recovery = Start(session, "level_02_02"); session.TryResolveRecovery(recovery, out receipt);
            Check(receipt.Recovered == 0 && session.PlayerHealth == 100, "Full-health recovery does not overflow.");
            Check(RunSession.Instance == existing, "Inactive fixtures never replace the live singleton.");
            Directory.CreateDirectory(RunIntegrationSetup.ResultDirectory);
            File.WriteAllText(RunIntegrationSetup.ResultDirectory + "/transaction-tests.txt", "PASS: " + assertions + " card, capacity, offering, recovery, identity and persistence assertions.\n");
        }
        finally { Object.DestroyImmediate(root); Object.DestroyImmediate(rules); }
    }

    private static void VerifyCardBalance(CardPool pool)
    {
        Check(RunBalance.Default.deckCapacity > 0, "Map capacity is configurable and positive.");
        foreach (var card in pool.Cards)
        {
            using var data = new SerializedObject(card.combatPrefab.GetComponent<BuffCards>());
            Check(card.capacityCost >= 0 && data.FindProperty("elixirCost").intValue >= 0 &&
                data.FindProperty("effectDuration").floatValue >= 0 && card.overworldEffect.magnitude >= 0,
                "Card values have valid ranges: " + card.name);
            Check(!string.IsNullOrWhiteSpace(card.GetCombatDescription()) && !card.GetCombatDescription().Contains("{") &&
                !string.IsNullOrWhiteSpace(card.GetSacrificeDescription()) && !card.GetSacrificeDescription().Contains("{"),
                "Dynamic combat and offering descriptions resolve for " + card.name);
        }
        VerifyUnequalCapacity(pool);
    }

    private static void VerifyUnequalCapacity(CardPool pool)
    {
        var root = new GameObject("Variable Capacity Fixture") { hideFlags = HideFlags.HideAndDontSave };
        root.SetActive(false);
        var session = root.AddComponent<RunSession>();
        var rules = SetFixtureRules(session, 30);
        var graph = AssetDatabase.LoadAssetAtPath<MapGraphDefinition>("Assets/Art/Map/Settings/Graph.asset");
        var catalog = AssetDatabase.LoadAssetAtPath<MapNodeCatalog>("Assets/Art/Map/Settings/Nodes.asset");
        session.InitializeProgress(graph, catalog);
        var small = Object.Instantiate(pool.Cards[0]); small.capacityCost = 2;
        var large = Object.Instantiate(pool.Cards[1]); large.capacityCost = 5;
        var testPool = ScriptableObject.CreateInstance<CardPool>();
        using (var data = new SerializedObject(testPool))
        {
            var cards = data.FindProperty("cards"); cards.arraySize = 2;
            cards.GetArrayElementAtIndex(0).objectReferenceValue = small;
            cards.GetArrayElementAtIndex(1).objectReferenceValue = large;
            data.ApplyModifiedPropertiesWithoutUndo();
        }
        try
        {
            for (int i = 0; i < session.DeckCapacityLimit / small.capacityCost; i++)
                Check(session.TryAddCard(small, out _), "Small cards fill the capacity by points, not card count.");
            string outgoing = session.RunDeck[0].instanceId;
            string[] ids = session.RunDeck.Select(c => c.instanceId).ToArray();
            string battle = Start(session, "level_01_01");
            session.TryPrepareBattleReward(battle, testPool, testPool.Cards.Count, out _);
            Check(!session.TryResolveBattleReward(battle, large, outgoing, out _) && session.HasBattleReward &&
                session.DeckCapacity == 30 && ids.SequenceEqual(session.RunDeck.Select(c => c.instanceId)),
                "A larger battle reward cannot replace a smaller card at full capacity; rejection is atomic.");
            session.TryResolveBattleReward(battle, null, null, out _);
            session.CompleteEncounter(battle, true);
            session.TryBeginTravel("level_02_01", out var ticket);
            session.Progress.MarkLoading(ticket.EncounterId);
            session.TryPrepareOpportunity(ticket.EncounterId, testPool, new[] { large });
            session.ConfirmEncounterStarted(ticket.EncounterId);
            Check(!session.TryResolveOpportunityReward(ticket.EncounterId, large, outgoing, out _) &&
                !session.GetNonCombatReceipt(ticket.EncounterId).Resolved &&
                ids.SequenceEqual(session.RunDeck.Select(c => c.instanceId)),
                "Opportunity rewards enforce unequal replacement costs without deleting a card.");
            Check(session.TryResolveOpportunityReward(ticket.EncounterId, null, null, out _) &&
                session.DeckCapacity == 30, "A blocked reward can still be skipped without changing the deck.");
        }
        finally
        {
            Object.DestroyImmediate(root); Object.DestroyImmediate(rules);
            Object.DestroyImmediate(small); Object.DestroyImmediate(large); Object.DestroyImmediate(testPool);
        }
    }

    public static RunBalance SetFixtureRules(RunSession session, int capacity)
    {
        var rules = ScriptableObject.CreateInstance<RunBalance>(); rules.deckCapacity = capacity;
        using var data = new SerializedObject(session);
        data.FindProperty("balance").objectReferenceValue = rules; data.ApplyModifiedPropertiesWithoutUndo();
        return rules;
    }

    private static string Start(RunSession session, string node)
    {
        Check(session.TryBeginTravel(node, out var ticket), "Available route entry accepted: " + node);
        Check(session.Progress.MarkLoading(ticket.EncounterId) && session.ConfirmEncounterStarted(ticket.EncounterId), "Receiver acknowledgement commits the encounter.");
        return ticket.EncounterId;
    }
    private static void Check(bool value, string message)
    {
        assertions++; if (!value) throw new InvalidOperationException(message);
    }
}
