using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;

public sealed partial class MapTravelHarness
{
    private bool interceptNonCombat;
    private bool supplyOpportunityTestCard;
    private float nonCombatDelay;
    private sealed class FixedOpportunityTestContent : IMapOpportunityContentProvider
    {
        public IReadOnlyList<CardDefinition> GetOffers(MapTravelTicket ticket, CardPool pool) =>
            new[] { pool.Cards.Single(c => c.overworldEffect.effectType == OverworldEffectType.GrantStartingShield) };
    }
    private void EncounterSceneLoaded(Scene scene)
    {
        if (supplyOpportunityTestCard && scene.name == "MapOpportunity")
        {
            supplyOpportunityTestCard = false;
            MapNonCombatController.Find(scene).ContentProvider = new FixedOpportunityTestContent();
        }
        if (!interceptNonCombat || (scene.name != "MapOpportunity" && scene.name != "MapRecovery")) return;
        interceptNonCombat = false;
        var receiver = MapNonCombatController.Find(scene);
        if (receiver == null) return;
        receiver.enabled = false;
        if (nonCombatDelay > 0) StartCoroutine(ReleaseNonCombat(receiver, nonCombatDelay));
    }
    private IEnumerator ReleaseNonCombat(MapNonCombatController receiver, float delay)
    { yield return new WaitForSecondsRealtime(delay); if (receiver != null) receiver.enabled = true; }
    private static bool FinishFixture(RunSession session, MapTravelTicket ticket, MapEncounterRouting routing)
    {
        if (ticket.IsCombat) return session.CompleteEncounter(ticket.EncounterId, true);
        if (ticket.Kind == MapEncounterKind.Recovery)
        { if (!session.TryResolveRecovery(ticket.EncounterId, out _)) return false; }
        else if (!session.TryPrepareOpportunity(ticket.EncounterId, routing.cardPool, Array.Empty<CardDefinition>())) return false;
        return session.CompleteNonCombatEncounter(ticket.EncounterId);
    }
    private MapTravelTicket FixtureStart(RunSession session, string node)
    {
        Check(session.TryBeginTravel(node, out var ticket) && session.Progress.MarkLoading(ticket.EncounterId) &&
            session.ConfirmEncounterStarted(ticket.EncounterId), "Fixture starts only an explicit available destination: " + node);
        return ticket;
    }
    private void EncounterStateChecks(MapController ui)
    {
        var catalog = ui.EncounterRouting.nodes; var session = ui.Session;
        catalog.Validate(graph);
        Check(catalog.nodes.Count(n => n.kind == MapEncounterKind.Battle) == 7 &&
            catalog.nodes.Count(n => n.kind == MapEncounterKind.Opportunity) == 3 &&
            catalog.nodes.Count(n => n.kind == MapEncounterKind.Recovery) == 3 &&
            catalog.nodes.Count(n => n.kind == MapEncounterKind.Boss) == 1, "Production distribution is 7 battle, 3 opportunity, 3 recovery and 1 Boss");
        var paths = new List<List<string>>();
        void Visit(string id, List<string> route)
        {
            var next = new List<string>(route) { id };
            if (graph.Find(id).boss) paths.Add(next); else foreach (var child in graph.Find(id).next) Visit(child, next);
        }
        Visit("level_01_01", new List<string>()); var edges = new HashSet<string>();
        foreach (var path in paths)
        {
            var state = new MapProgressState(graph, catalog);
            int combat = path.Count(id => catalog.Find(id).kind == MapEncounterKind.Battle);
            Check(combat >= 2 && combat <= 4 && path.Count == 6, "Complete route contains two to four normal battles and six total visits");
            for (int i = 0; i < path.Count; i++)
            {
                string id = path[i]; Check(state.TryBeginTravel(id, out var ticket), "Typed route is reachable: " + id);
                Check(ticket.Kind == catalog.Find(id).kind && ticket.IsBoss == graph.Find(id).boss, "Ticket kind matches its icon/catalog and graph Boss flag");
                Check(state.MarkLoading(ticket.EncounterId) && state.ConfirmEncounterStarted(ticket.EncounterId) &&
                    state.CompleteEncounter(ticket.EncounterId, true), "Typed route completes one destination");
                Check(!state.CompleteEncounter(ticket.EncounterId, true), "A duplicate result cannot advance the typed route");
                if (i > 0) edges.Add(path[i - 1] + ">" + id);
            }
            Check(state.Phase == MapProgressPhase.Won, "Every typed route ends at Boss victory");
        }
        Check(paths.Count == 9 && edges.Count == 19, "All nine complete routes cover all nineteen connections");
        var card = session.GetActiveDeck()[0];
        foreach (var sample in new[] { (50, 100, 65), (99, 100, 100), (100, 100, 100), (30, 101, 46), (1, 1, 1) })
        {
            AdvanceFixtureTo("level_01_01", ui); Set(session, "playerHealth", sample.Item1); Set(session, "playerMaxHealth", sample.Item2);
            Check(session.TrySacrifice(card), "An offering is staged before recovery");
            var ticket = FixtureStart(session, "level_02_02");
            Check(!session.TryConsumePendingModifier(out _), "Recovery cannot consume a pending battle offering");
            Check(!session.CompleteEncounter(ticket.EncounterId, true), "Noncombat cannot use the combat result API");
            Check(session.TryResolveRecovery(ticket.EncounterId, out var receipt) && session.PlayerHealth == sample.Item3,
                $"Recovery clamps and rounds correctly: {sample.Item1}/{sample.Item2} becomes {sample.Item3}");
            Check(session.TryResolveRecovery(ticket.EncounterId, out var repeated) && ReferenceEquals(receipt, repeated) &&
                session.PlayerHealth == sample.Item3, "Repeated recovery returns the same immutable result values without another heal");
            Check(!session.TryResolveRecovery("stale", out _), "An expired recovery identifier cannot heal");
            Check(session.CompleteNonCombatEncounter(ticket.EncounterId) && !session.CompleteNonCombatEncounter(ticket.EncounterId), "Recovery completes once");
            Check(session.HasPendingModifier && session.SacrificeUsed, "Recovery completion preserves the offering and its used allowance");
            string previous = ticket.EncounterId; session.BeginNewRun();
            Check(session.GetNonCombatReceipt(previous) == null && !session.HasPendingModifier && !session.SacrificeUsed &&
                !session.TryResolveRecovery(previous, out _), "New run discards prior recovery and offering records");
        }
        AdvanceFixtureTo("level_01_01", ui); Set(session, "playerHealth", 0);
        var dead = FixtureStart(session, "level_02_02");
        Check(!session.TryResolveRecovery(dead.EncounterId, out _), "Recovery cannot resurrect a zero-health player");
        session.BeginNewRun();
        Check(session.DeckCapacity == 20 && !session.TryAddCard(card, out _), "A full starting deck rejects extra capacity");
        var invalid = Instantiate(card); invalid.capacityCost = -1;
        Check(!session.CanAddCard(invalid), "Negative capacity definitions are rejected"); Destroy(invalid);
        var source = session.RunDeck.First(c => c.definition == card);
        var other = session.RunDeck.First(c => c.definition != card);
        RunCardInstance duplicate = null;
        Check(session.TrySacrificeInstance(source.instanceId) && session.TryAddCard(other.definition, out duplicate) &&
            session.DeckCapacity == 20 && duplicate.instanceId != other.instanceId, "A freed slot accepts a distinct same-definition instance up to exactly twenty");
        var initial = FixtureStart(session, "level_01_01");
        Check(session.CompleteEncounter(initial.EncounterId, true), "Only combat victory restores the offering allowance");
        Check(session.TrySacrificeInstance(duplicate.instanceId) && duplicate.sacrificed && !other.sacrificed,
            "Sacrificing the duplicate instance leaves the original identical card intact");
        AdvanceFixtureTo("level_01_01", ui);
        Check(session.TrySacrifice(card), "Opportunity fixture frees capacity through an offering");
        var opportunity = FixtureStart(session, "level_02_01");
        Check(session.TryPrepareOpportunity(opportunity.EncounterId, ui.EncounterRouting.cardPool, new[] { other.definition }), "Fixed test provider supplies an existing pool card");
        Check(!session.TryAddCard(other.definition, out _), "Generic insertion cannot bypass the active opportunity claim gate");
        Check(!session.TryClaimOpportunityCard(opportunity.EncounterId, card, out _), "A pool card not in this visit's offers is rejected");
        Check(session.TryClaimOpportunityCard(opportunity.EncounterId, other.definition, out _) && session.DeckCapacity == 20, "Opportunity claim adds exactly one instance within capacity");
        Check(!session.TryClaimOpportunityCard(opportunity.EncounterId, other.definition, out _), "Repeated claim is rejected even for the same card");
        Check(session.CompleteNonCombatEncounter(opportunity.EncounterId) && session.HasPendingModifier && session.SacrificeUsed, "Claim and completion do not consume or refresh the battle offering");
        Check(!session.TryClaimOpportunityCard(opportunity.EncounterId, other.definition, out _), "Completed opportunity cannot grant again");
        session.BeginNewRun();
        Check(!session.TryClaimOpportunityCard(opportunity.EncounterId, other.definition, out _), "Prior-adventure opportunity cannot grant cards");
        AdvanceFixtureTo("level_01_01", ui); opportunity = FixtureStart(session, "level_02_01");
        Check(session.TryPrepareOpportunity(opportunity.EncounterId, ui.EncounterRouting.cardPool, new[] { card }) &&
            !session.TryClaimOpportunityCard(opportunity.EncounterId, card, out var reason) && !string.IsNullOrEmpty(reason) && session.DeckCapacity == 20,
            "A capacity-denied claim leaves the deck unchanged and explains why");
        Check(session.CompleteNonCombatEncounter(opportunity.EncounterId) && session.DeckCapacity == 20, "A full deck may leave without a card");
        session.BeginNewRun(); ui.TravelView.RestorePosition(session.Progress.CurrentNodeId);
    }
    private IEnumerator EncounterTypeChecks(MapController ui)
    {
        EncounterStateChecks(ui); yield return null; yield return null;
        foreach (var size in new[] { new Vector2Int(1920,1080), new Vector2Int(2560,1440), new Vector2Int(1920,1200) })
        {
            Screen.SetResolution(size.x, size.y, FullScreenMode.Windowed); yield return new WaitForSecondsRealtime(1);
            ValidateFrame(ui, size); yield return Capture($"map-types-{size.x}x{size.y}");
        }
        Screen.SetResolution(1920,1080,FullScreenMode.Windowed); yield return new WaitForSecondsRealtime(1);
        yield return Click(ui.TravelView.IconScreenRect("level_01_01").center);
        var session = ui.Session;
        yield return Wait(() => session.Progress.Phase == MapProgressPhase.InEncounter && !session.GetComponent<MapTravelCoordinator>().IsBusy, 15, "Battle type still enters the ready real combat scene");
        ValidateBattleHud(BattleHud.Find(SceneManager.GetActiveScene()),session); yield return Capture("battle-regression"); yield return VictoryAndReturn("level_01_01", 3);
        ui = FindFirstObjectByType<MapController>();
        yield return NonCombatFailures(ui);
        ui = FindFirstObjectByType<MapController>();
        yield return OpportunityProviderChecks(ui);
        ui = FindFirstObjectByType<MapController>();
        AdvanceFixtureTo("level_03_01", ui); Set(session, "playerHealth", 53); Set(session, "playerMaxHealth", 101);
        Check(session.TrySacrifice(session.GetActiveDeck().First(c => c.overworldEffect.effectType == OverworldEffectType.ReduceEnemyStartingHealth)), "Offering staged before opportunity then recovery then battle");
        yield return null; yield return null;
        yield return EnterNonCombat(ui, "level_04_03", MapEncounterKind.Opportunity);
        var receiver = FindFirstObjectByType<MapNonCombatController>();
        int deck = session.RunDeck.Count;
        Check(receiver.Receipt.Offers.Count == 0 && receiver.Receipt.GrantedCard == null && session.PlayerHealth == 53 && session.HasPendingModifier,
            "Production opportunity has no candidates, no grant and no resource changes");
        yield return NonCombatFrames(receiver, "opportunity");
        yield return Click(Center((RectTransform)receiver.ContinueButton.transform));
        yield return WaitForMap("level_04_03");
        Check(session.RunDeck.Count == deck && session.SacrificeUsed && session.HasPendingModifier, "Opportunity return preserves cards and offering");
        ui = FindFirstObjectByType<MapController>(); yield return EnterNonCombat(ui, "level_05_03", MapEncounterKind.Recovery);
        receiver = FindFirstObjectByType<MapNonCombatController>();
        Check(session.PlayerHealth == 69 && receiver.Receipt.Recovered == 16 && session.SacrificeUsed && session.HasPendingModifier,
            "Recovery grants sixteen of maximum101 once and retains the pending offering");
        string healId = receiver.EncounterId;
        yield return NonCombatFrames(receiver, "recovery");
        yield return SceneManager.LoadSceneAsync("MapRecovery"); yield return new WaitForSecondsRealtime(.8f);
        receiver = FindFirstObjectByType<MapNonCombatController>();
        Check(receiver.EncounterId == healId && session.PlayerHealth == 69 && receiver.ContinueButton.interactable,
            "Reloaded recovery interface reuses its receipt instead of healing twice");
        var coordinator = MapTravelCoordinator.Ensure(session); string map = Read<string>(coordinator,"mapScene"); Set(coordinator,"mapScene","MissingMapScene");
        yield return Click(Center((RectTransform)receiver.ContinueButton.transform));
        yield return Wait(() => !coordinator.IsBusy && receiver.ContinueButton.interactable, 4, "Failed return exposes a retry without losing settlement");
        Check(session.PlayerHealth == 69 && receiver.Receipt.Completed && session.Progress.CurrentNodeId == "level_05_03", "Return failure preserves the completed recovery position and result");
        Set(coordinator,"mapScene",map); yield return Capture("recovery-return-retry");
        yield return Click(Center((RectTransform)receiver.ContinueButton.transform)); yield return WaitForMap("level_05_03");
        ui = FindFirstObjectByType<MapController>();
        yield return Click(ui.TravelView.IconScreenRect("level_06_01").center);
        yield return Wait(() => session.Progress.Phase == MapProgressPhase.InEncounter && !coordinator.IsBusy, 15, "Boss receives the pending offering after two noncombat visits");
        Check(session.CurrentEncounterIsBoss && !session.HasPendingModifier && session.PlayerHealth == 69, "Only confirmed battle consumes the offering and restores recovered health");
        ValidateBattleHud(BattleHud.Find(SceneManager.GetActiveScene()),session); yield return Capture("boss-after-recovery");
        yield return VictoryAndReturn("level_06_01", 0);
        ui = FindFirstObjectByType<MapController>(); Check(session.Progress.Phase == MapProgressPhase.Won, "Boss victory still completes the run");
        ui.EnterButton.onClick.Invoke(); yield return null;
        Check(!session.SacrificeUsed && session.DeckCapacity == 20 && session.LastNonCombatReceipt == null && session.PlayerHealth == 100, "New run resets recovery, cards and offering");
        // Exercise all saved roads independently of encounter content.
        yield return VisualRoutes(ui, false);
        session.BeginNewRun(); ui.TravelView.RestorePosition(session.Progress.CurrentNodeId); yield return null;
        yield return Click(ui.TravelView.IconScreenRect("level_01_01").center);
        yield return Wait(() => session.Progress.Phase == MapProgressPhase.InEncounter && !coordinator.IsBusy, 15, "Defeat regression enters a real battle");
        MapTravelCoordinator.FindReadyPlayer("Deinosavros").health = 0;
        yield return Wait(() => SceneManager.GetActiveScene().name == "Map" && session.Progress.Phase == MapProgressPhase.Lost, 12, "Defeat still ends the adventure and returns to Map");
        yield return new WaitForSecondsRealtime(.7f); ui = FindFirstObjectByType<MapController>(); ui.EnterButton.onClick.Invoke(); yield return null;
        yield return Capture("final-types-map");
    }
    private IEnumerator EnterNonCombat(MapController ui, string node, MapEncounterKind kind)
    {
        Move(ui.TravelView.IconScreenRect(node).center); yield return new WaitForSecondsRealtime(.3f);
        Check(ui.NodeInteraction.DetailVisible && ui.NodeInteraction.DetailPanel.GetComponentsInChildren<TMP_Text>().Any(t => t.text.Contains(kind.ToString().ToUpperInvariant())), "Hover explains the actual receiver kind: " + kind);
        yield return Capture("hover-" + kind, false);
        yield return Click(ui.TravelView.IconScreenRect(node).center);
        var session = ui.Session;
        yield return Wait(() => session.Progress.Phase == MapProgressPhase.InEncounter && !session.GetComponent<MapTravelCoordinator>().IsBusy, 15, "Movement enters the independent " + kind + " interface");
        var receiver = FindFirstObjectByType<MapNonCombatController>();
        Check(receiver != null && receiver.kind == kind && receiver.IsActivated && session.Progress.CurrentNodeId == node,
            "Receiver, ticket and committed node agree");
        yield return Wait(() => receiver.ContinueButton.interactable, 2, "Settlement enables Continue");
        Check(FindFirstObjectByType<BattleHud>() == null && FindFirstObjectByType<DeckManager>() == null,
            "Noncombat scene contains no copied battle HUD or battle hand");
    }
    private IEnumerator NonCombatFailures(MapController ui)
    {
        var session = ui.Session;
        Check(session.TrySacrifice(session.GetActiveDeck()[0]), "Failure test stages an offering before noncombat entry");
        string original = ui.EncounterRouting.opportunityScene;
        ui.EncounterRouting.opportunityScene = "MissingOpportunity";
        ui.SelectNode(ui.Nodes.Single(n => n.NodeId == "level_02_01"));
        Check(session.Progress.Phase == MapProgressPhase.OnMap && session.Progress.CurrentNodeId == "level_01_01" && session.HasPendingModifier,
            "Missing noncombat scene preserves location, cards and offering");
        ui.EncounterRouting.opportunityScene = original;
        interceptNonCombat = true; nonCombatDelay = 0;
        ui.SelectNode(ui.Nodes.Single(n => n.NodeId == "level_02_01"));
        yield return Wait(() => SceneManager.GetActiveScene().name == "MapOpportunity", 12, "Unready opportunity test activates its scene");
        yield return Wait(() => SceneManager.GetActiveScene().name == "Map" && !session.GetComponent<MapTravelCoordinator>().IsBusy, 12, "Noncombat readiness timeout rolls back to Map");
        yield return null; yield return null; ui = FindFirstObjectByType<MapController>();
        Check(ui.CanInteract && session.HasPendingModifier && session.SacrificeUsed && session.Progress.CurrentNodeId == "level_01_01", "Failed noncombat entry preserves its offering and original choice");
        interceptNonCombat = true; nonCombatDelay = 1.1f;
        ui.SelectNode(ui.Nodes.Single(n => n.NodeId == "level_02_01"));
        yield return Wait(() => SceneManager.GetActiveScene().name == "MapOpportunity", 12, "Delayed opportunity scene activates");
        yield return new WaitForSecondsRealtime(.3f);
        Check(session.Progress.Phase == MapProgressPhase.Loading && session.Progress.CurrentNodeId == "level_01_01", "Scene activation alone does not commit a noncombat visit");
        yield return Wait(() => session.Progress.Phase == MapProgressPhase.InEncounter && !session.GetComponent<MapTravelCoordinator>().IsBusy, 5, "Delayed noncombat receiver can become ready before timeout");
        var receiver = FindFirstObjectByType<MapNonCombatController>();
        Check(receiver.IsActivated && session.HasPendingModifier, "Delayed placeholder starts without consuming the offering");
        yield return Click(Center((RectTransform)receiver.ContinueButton.transform)); yield return WaitForMap("level_02_01");
    }
    private IEnumerator OpportunityProviderChecks(MapController ui)
    {
        AdvanceFixtureTo("level_01_01", ui);
        var session = ui.Session;
        Check(session.TrySacrifice(session.GetActiveDeck().Single(c => c.overworldEffect.effectType == OverworldEffectType.ReduceEnemyStartingHealth)),
            "Development provider test frees a real deck slot");
        yield return null; yield return null; supplyOpportunityTestCard = true;
        yield return EnterNonCombat(ui, "level_02_01", MapEncounterKind.Opportunity);
        var receiver = FindFirstObjectByType<MapNonCombatController>();
        var choice = receiver.GetComponentsInChildren<UnityEngine.UI.Button>().Single(b => b.name == "Choice");
        yield return Capture("opportunity-development-provider");
        yield return Click(Center((RectTransform)choice.transform));
        Check(receiver.Receipt.GrantedCard != null && session.DeckCapacity == 20 && !choice.gameObject.activeInHierarchy,
            "Actual candidate click grants one card and removes the choices");
        string instance = receiver.Receipt.GrantedCard.instanceId; var definition = receiver.Receipt.GrantedCard.definition;
        yield return Click(Center((RectTransform)receiver.ContinueButton.transform)); yield return WaitForMap("level_02_01");
        var views = FindObjectsByType<MapCardView>(FindObjectsSortMode.None).Where(v => v.Definition == definition).ToArray();
        Check(views.Length == 2 && views.Select(v => v.InstanceId).Distinct().Count() == 2 && views.Any(v => v.InstanceId == instance),
            "Both same-name instances appear independently in the existing map card layout");
        Check(FindObjectsByType<MapCardView>(FindObjectsSortMode.None).Length == 4, "A capacity-valid reward still displays exactly four cards");
        yield return Capture("map-same-name-reward");
    }
    private IEnumerator WaitForMap(string node)
    {
        yield return Wait(() => SceneManager.GetActiveScene().name == "Map" && FindFirstObjectByType<MapController>() != null &&
            !RunSession.Instance.GetComponent<MapTravelCoordinator>().IsBusy, 12, "Noncombat returns to the ready Map");
        yield return null; yield return null;
        var ui = FindFirstObjectByType<MapController>();
        Check(ui.CanInteract && ui.Session.Progress.CurrentNodeId == node && ui.Session.Progress.NodeState(node) == MapLocationState.Completed,
            "Return keeps the completed node and restores map/card interaction");
        Check(ui.Nodes.Count(n => n.IsAvailable) == graph.Find(node).next.Length &&
            Vector3.Distance(ui.TravelView.FloorPosition, ui.TravelView.SitePoint(node)) < .001f,
            "Fire seed remains at the completed platform and only its outgoing choices open");
    }
    private IEnumerator NonCombatFrames(MapNonCombatController receiver, string prefix)
    {
        foreach (var size in new[] { new Vector2Int(1920,1080), new Vector2Int(2560,1440), new Vector2Int(1920,1200) })
        {
            Screen.SetResolution(size.x,size.y,FullScreenMode.Windowed); yield return new WaitForSecondsRealtime(1);
            receiver.Panel.GetWorldCorners(corners);
            Check(corners[0].x >= 0 && corners[0].y >= 0 && corners[2].x <= Screen.width && corners[2].y <= Screen.height,
                "Independent interface stays inside the window at " + size);
            foreach (var text in receiver.GetComponentsInChildren<TMP_Text>()) if (!string.IsNullOrEmpty(text.text)) ValidateTextVisible(text);
            if (receiver.kind == MapEncounterKind.Recovery)
            {
                var fill = Read<UnityEngine.UI.Image>(receiver, "healthFill").rectTransform;
                float fraction = fill.rect.width / ((RectTransform)fill.parent).rect.width;
                Check(Mathf.Abs(fraction - (float)receiver.Receipt.HealthAfter / receiver.Receipt.MaxHealth) < .001f,
                    "The visible recovery bar matches the recorded health fraction");
            }
            yield return Capture(prefix + "-" + size.x + "x" + size.y);
        }
        Screen.SetResolution(1920,1080,FullScreenMode.Windowed); yield return new WaitForSecondsRealtime(1);
    }
}
