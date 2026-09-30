# Map review encounter contract

Status: Map-side implementation and test receiver. Production battle integration remains a teammate task. This document does not claim that Unity compilation, visual review or performance acceptance has passed; use the generated verification evidence for those results.

## Isolation

All new state is in `Deinosavros.MapReview`. Only the independent MapReview and MapEncounterReview scenes should use it. Do not enable the production Map controller, RunSession or BattleRunBridge in these scenes. Production scenes, shared CardDefinition assets, front-card combat behavior, reward rules and default build settings are not replaced.

`MapRunHost` owns the in-memory `MapRunState` and survives review scene changes. Loading a map scene does not create a new adventure or restore sacrifice availability. A deliberate `BeginNewRun` creates a new run ID and clears route progression, sacrifice history, permanent offering gains and the host's old fake encounter/result. There is no disk persistence in this iteration.

## Data ownership

| Type | Owner and meaning |
| --- | --- |
| MapRunCard | A unique card instance ID, a shared definition and run-local sacrifice state. Same-name rewards are different instances. |
| MapPlayerState | Immutable persistent HP, maximum HP, damage, attack interval in floating-point seconds, persistent shield, current Elixir and maximum Elixir. |
| MapGraphDefinition | Explicit nodes, source model anchors and directed next-layer edges. A new run snapshots this data. |
| MapEncounterRequest | Immutable encounter ID, run ID, node, active-deck snapshot, pre-start persistent state and pending offering. Card definitions are shared read-only configuration. |
| MapEncounterResult | Run ID, encounter ID, victory flag and persistent-only state. Temporary encounter effects must already be removed. |

Do not edit a CardDefinition to change an individual card instance. The prepared deck is an identity-preserving snapshot; later rewards or sacrifices cannot alter old requests. The active capacity is the sum of non-negative capacity costs of unsacrificed cards. The UI displays `/20` and warns above twenty. Adding a reward is not automatically refused or pruned. The reward system must deduplicate its own delivery before calling `AddCard`; repeated calls intentionally create distinct reward instances.

## Lifecycle

1. `BeginNewRun(graph, deck, optionalPlayer)` validates the graph and opens only the start node.
2. `TrySacrifice(instanceId)` is allowed once while on the map. The exact card is removed from the active deck, the allowance is marked used and one pending effect is recorded atomically. Node selection is not required.
3. `TryPrepareEncounter(nodeId, out request)` accepts only an available node, snapshots the request and enters Prepared. No permanent effect has been applied yet. While Prepared, additional sacrifice, reward insertion and second preparation are rejected.
4. Load and prepare the receiving scene. It may remain in this state as long as necessary. If readiness fails, call `CancelPreparedEncounter(encounterId)`. The card stays sacrificed, the effect stays pending and the allowance stays used. Retry creates a new encounter ID; old callbacks are ignored.
5. Only once the receiver is ready to actually run the encounter, call `AcknowledgeEncounterStarted(encounterId)`. Check the returned bool before constructing encounter state. Success commits permanent Elixir once, consumes the pending effect and sets the request's acknowledgement flag. Duplicate acknowledgements return false.
6. Initialize encounter-local state from `request.StartedPersistentPlayer`, not from the pre-start `request.PersistentPlayer`. Apply the effect to its correct local layer. The provided MapEncounterSimulation rejects construction before acknowledgement.
7. Return one `MapEncounterResult` to `CompleteEncounter`. It must contain persistent-only state. The run checks both IDs, active phase and maximum Elixir consistency. Duplicate, stale, null or out-of-order results return false. The fake receiver seals its first result; late input cannot change it.
8. Normal victory completes the current node, restores one sacrifice allowance and opens exactly the explicit outgoing nodes. Unchosen passed branches are skipped permanently. Boss victory enters Won. Any defeat enters Lost. Ended adventures reject further encounters, offerings and rewards until a new adventure begins.

Example receiver handshake:

```csharp
MapEncounterRequest request = host.State.CurrentEncounter;
if (request != null && host.State.AcknowledgeEncounterStarted(request.EncounterId))
{
    host.Simulation = new MapEncounterSimulation(request);
    host.Simulation.SpawnEnemy("first-wave-enemy-01", 101);
}

if (host.Simulation != null)
{
    MapEncounterResult result = host.Simulation.Result(victory: true);
    if (host.State.CompleteEncounter(result))
    {
        host.LastResult = result;
        host.Simulation = null;
        // Return to MapReview only after successful completion.
    }
}
```

## Offering semantics

| Card | Exact effect | Scope |
| --- | --- | --- |
| Tidal Wave | `max(1, ceil(initialHealth * 0.75))`, once per enemy instance, including later waves and the boss. | Next started encounter only. |
| Fleet Footwork | Effective interval `max(1, persistentInterval - 1)` seconds. | Next started encounter only; persistent interval is unchanged. |
| Solar Shield | Add a separate five-point temporary shield layer. Consume this layer before persistent shield in the test receiver. | Next started encounter only; unused temporary shield is discarded. |
| Uncertain Fates | Add three to maximum Elixir and restore three, capped by the new maximum. | Maximum persists for the adventure. Restoration happens once at successful acknowledgement. Future sacrifices may stack the maximum. |

Never undo a temporary buff by guessing a subtraction from a combined value. Store the temporary shield and effective attack interval separately, and return the remaining persistent shield and original persistent interval. The production receiver is responsible for preserving this separation when combining real combat systems or permanent upgrades. Map-side result validation cannot infer arbitrary temporary buff contamination from a single number.

`StartedPersistentPlayer` is a pure initialization value, not an instruction to add the Elixir bonus again. Do not apply Uncertain Fates both in the run and additively on that initialized value. The result must retain the run's already committed maximum Elixir.

## Route and model contract

The approved graph contains fourteen nodes and nineteen edges, with six encounters per complete path: five ordinary layers followed by one boss. The three added nodes use these exact source references:

| Node | Source terrace |
| --- | --- |
| level_02_03 | Node terrace 02 |
| level_04_03 | Node terrace 08 |
| level_05_03 | Node terrace 11 |

Existing eleven node transforms and colliders must remain unchanged. Use imported source anchors and the existing coordinate conversion for the new three, not guessed Unity coordinates. Node-state visuals must include a non-color cue. Raycasting to map nodes must be blocked by UI and modal confirmation panels.

## Verification entry point

Run `Deinosavros.MapReview.Editor.MapReviewTests.Run` through the editor verification pipeline. It writes `Library/MapReviewEvidence/state-tests.txt` only after all assertions succeed. Coverage includes every approved route and edge, branching locks, six-encounter paths, precise duplicate-card removal, reward identity, over-capacity reporting, delayed preparation, load-failure retry, stale IDs, duplicate acknowledgements/results, late enemy waves, ceiling/minimum health, temporary shield cleanup, floating-point attack interval/floor, permanent Elixir stacking, graph/request snapshots, invalid input and new-run reset.

These tests do not establish visual quality, pointer-hover quality, imported transform accuracy or frame rate. Those require the separate scene/UI/import checks and the warmed standalone performance run.
