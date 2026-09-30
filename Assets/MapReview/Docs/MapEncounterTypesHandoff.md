# Production encounter types

## Layout and scope

The formal Map keeps its fourteen nodes, nineteen authored roads, camera, environment and overlapping left-to-right card layout. The production distribution is seven battles, three opportunities, three recovery visits and one Boss. All nine complete routes contain two to four ordinary battles and one Boss.

| Layer | Upper | Middle | Lower |
| --- | --- | --- | --- |
| 1 | - | L01_01 Battle | - |
| 2 | L02_01 Opportunity | L02_03 Battle | L02_02 Recovery |
| 3 | L03_01 Battle | L03_02 Battle | L03_03 Battle |
| 4 | L04_01 Recovery | L04_03 Opportunity | L04_02 Battle |
| 5 | L05_01 Battle | L05_03 Recovery | L05_02 Opportunity |
| 6 | - | L06_01 Boss | - |

`NodeInformation.asset` owns the node types and descriptions. `EncounterRouting.asset` binds Battle/Boss to Deinosavros, Opportunity to MapOpportunity and Recovery to MapRecovery. Numeric enum values are explicit: Battle=0, Boss=1, Opportunity=2, Recovery=3. The formerly unused Event/Reward slots and atlas artwork were deliberately migrated; the installed catalog is validated against every graph node and the graph's Boss identity.

No combat formulas, original card effects, Blender models, enemy configuration or existing temporary-offering cleanup behavior were redesigned. The separate MapReview simulation remains isolated.

## Noncombat lifecycle

Hover previews the true type; clicking uses the existing fire-seed route animation. A ticket snapshots its type and identities. Receiver readiness is checked after scene initialization; scene activation alone does not commit the player's location. Unavailable scenes or a five-second readiness timeout return to the origin without consuming the pending offering.

MapRecovery and MapOpportunity are independent UI scenes. Their common MapNonCombatController owns presentation, not durable run values. RunSession owns the receipts across scene changes. Continue marks the visit complete and returns to Map; only outgoing graph nodes become available. A failed return keeps its settled receipt and exposes Retry Return.

- Recovery automatically adds ceil(maximumHP * 15 / 100), limited by missing health. The calculation uses integer arithmetic to avoid floating-point over-rounding at exact multiples of fifteen. Its receipt stores before/after HP and the maximum, and repeat initialization reuses that receipt. Full health grants zero and still allows completion. Zero-health players cannot be healed into a new adventure.
- Production opportunity uses EmptyMapOpportunityContent. It never samples CardPool, shows no candidate cards and grants nothing. Its interface explicitly states that content is unavailable. Continue completes the visit without changing cards, stats or the offering.
- Neither noncombat type consumes pending battle effects nor refreshes the sacrifice allowance. Only a confirmed combat receiver consumes effects; only ordinary combat victory restores the allowance.
- New adventures discard receipt and claim records. No disk persistence is added.

## Opportunity extension contract

Implement IMapOpportunityContentProvider.GetOffers(ticket, pool), and assign MapNonCombatController.ContentProvider before receiver preparation. The default is intentionally empty. The receiver snapshots and validates the returned candidates against the configured CardPool. Tests inject one fixed existing card; this does not enable rewards in production.

- RunSession.TryPrepareOpportunity registers eligible offers for the active ticket. Repeat preparation cannot replace a visit's snapshot.
- TryClaimOpportunityCard(encounterId, definition, out reason) checks the run, active encounter kind, candidate membership, current pool membership, capacity and prior claim. One visit can grant at most one card. Repeated or expired requests cannot add cards.
- CompleteNonCombatEncounter marks either recovery or opportunity complete without using the combat victory API. A failed capacity check leaves the visit available to skip.
- TryResolveRecovery(encounterId, out receipt) is the only recovery mutation. Repeated calls during the same visit return the same result rather than healing again.

An opportunity module must use the claim API, not generic AddCard. Generic insertion is rejected during an active opportunity to prevent bypassing its once-only gate.

## Capacity and card instances

Capacity is the sum of nonnegative capacity costs of unsacrificed cards, limited to twenty. The current four definitions each cost five; the original starting deck remains full at 20/20. New cards may be additional instances of those definitions; no new artwork is implied. CanAddCard and TryAddCard expose the shared capacity rule. The legacy void AddCard wrapper uses the same guard; the legacy reward screen does not return as if a denied choice succeeded. Existing over-capacity data is not automatically pruned.

RunCardInstance has a unique runtime instanceId. The map reuses its authored card views where possible and instantiates the definition's existing map prefab for additional instances. It keeps run-deck ordering, size, overlap, flip and detail behavior. TrySacrificeInstance removes exactly the selected instance; the definition-based compatibility method selects the first remaining matching instance. No shared CardDefinition asset is changed by acquiring or sacrificing a card.

## Tools and validation

Tools > Map > Install Encounter Types backs up the saved formal Map and compares protected transforms, colliders, meshes, camera and route payloads. It binds the routing asset and creates the two independent scenes. Reapplication retains existing receiver scenes instead of overwriting them. The new scenes are appended to the build list without reordering the original startup scenes.

The opt-in standalone validation flag is `-travelValidation -encounterValidation -travelOutput <folder>`. It uses synthetic input inside its own process, not the desktop mouse. Graph positions and some attributes are deliberate fixtures; combat victory fixtures set existing enemies to zero health, so these are integration checks, not combat-balance testing.

Final evidence is preserved under `Tools/MapTravel/Delivery/2026-09-30/EncounterTypes`; working scene backups remain under `Library/MapReviewEvidence/2026-09-30/EncounterTypes`. Runtime03 is the final encounter run; BattleRegression is the separate final battle-UI regression run. Both ended with zero recorded runtime errors. Earlier failed working runs are not delivery acceptance.

Passed checks cover all nine complete graph routes and nineteen visual road traversals, exact recovery rounding/capping and once-only receipts, full-deck rejection, candidate membership, duplicate claims, distinct same-name instances, zero-health rejection, new-run resets, delayed/unready receivers, missing destination, return-load failure and retry, and offering preservation through opportunity and recovery before Boss. Production opportunity screenshots show no candidates or grant; `opportunity-development-provider.png` explicitly belongs to the injected fixed-card test only.

At 1920x1080, 2560x1440 and 1920x1200, map framing, receiver panels, rendered text and the recovery bar fraction passed. Actual synthetic clicks exercised candidate claiming, Continue, Retry Return, map departure, battle cards and draw-back behavior. The separate battle regression passed fractional attribute transport, existing timed front-card effects, redraw, sacrifice exclusion and load-failure rollback. Real battle/Boss victory and defeat returns passed; battle outcomes still use controlled health fixtures rather than demonstrating combat balance.

Two installer reapplications and saved reopen retained byte-identical scenes, catalog, routing, atlas and build list. The protected scene comparison covered 892 pre-existing transform, collider, mesh, camera and route payloads. Relative to the start-of-task scene backup, formal Map adds only its routing reference; Deinosavros is unchanged. The two receiver scenes are appended after the original three build entries without changing the startup scene. Four shared settings files were restored exactly to their captured pre-build contents; existing user edits were preserved.

The existing renderer shutdown allocation warnings remain visible in player logs and are not claimed resolved. No new combat-performance benchmark was performed. Production opportunity content remains intentionally unimplemented; the module and capacity-checked claim path are complete.
