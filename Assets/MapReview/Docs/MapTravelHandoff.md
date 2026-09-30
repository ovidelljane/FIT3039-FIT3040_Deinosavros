# Formal Map travel handoff

Date: 2026-09-30. This delivery adds travel and progression to `Assets/Scenes/Map.unity`. It does not replace the card layout, Blender source, combat formulas, reward rules or the separate MapReview encounter simulation.

Subsequent production revision: see `MapEncounterTypesHandoff.md` for the implemented 7 Battle / 3 Opportunity / 3 Recovery / 1 Boss layout, independent noncombat receivers, capacity checks and instance-aware map cards. That revision supersedes this document's earlier all-combat catalog and destination assumptions; the travel geometry and battle-UI records below remain historical verification evidence.

## Delivered behavior

The formal Map uses fourteen nodes and nineteen explicit directed roads. Hovering an available level icon or its platform previews its route and opens an information panel after 0.15 seconds. A single click (press and release on the same level without dragging) starts departure. The old enter button is hidden during normal play and appears only as `Start New Run` after an end state. One large amber flame pawn marks the current location. The old start marker's renderers are disabled, while its hierarchy and collider data remain intact.

Departure gathers light for 0.3 seconds, follows the saved road center at an arc-length-based speed for 1.2-2 seconds, and settles for 0.3 seconds. The first encounter uses an approximately one-second ignition instead of road travel. Boss travel follows the existing stairs, then gathers into the portal center over 0.8 seconds. The camera does not move. Hover never changes session location or spends an offering.

All UI raycasts block map selection. Offering confirmation, departure and loading lock map and card interaction. Normal victory restores the seed at the completed platform, retains traveled road markings, skips passed alternative branches and opens only the completed node's outgoing edges. Defeat and Boss victory end the run; the existing enter button displays `Start New Run`.

## Ownership and lifecycle

`RunSession` remains the owner of the original card flow and player fields. Its new `MapProgressState` snapshots the graph and owns progression independently. `MapTravelCoordinator` lives on the persistent session object and owns travel, preload, readiness, result deduplication and the transition curtain. `MapTravelView` is scene-owned and renders the seed, node marks, route ribbons and pooled motes.

| API | Contract |
| --- | --- |
| `RunSession.InitializeProgress(graph)` | Create progression once; returning to Map does not reset it. |
| `RunSession.BeginNewRun()` | Reset progression, starting deck, offering state and captured initial attributes. |
| `RunSession.TryBeginTravel(nodeId, out ticket)` | Accept only one available destination; issue unique run/encounter identifiers and lock departure. |
| `MapProgressState.MarkLoading(encounterId)` | Complete visual travel without committing the gameplay location. |
| `RunSession.ConfirmEncounterStarted(encounterId)` | Commit location only for the active loading ticket; duplicate acknowledgements fail. |
| `RunSession.CancelPreparedTravel(encounterId)` | Restore the origin before acknowledgement; retain the sacrificed card and pending offering. |
| `RunSession.CompleteEncounter(encounterId, victory)` | Apply exactly one active result, open explicit successors or end the run. |
| `BattleRunBridge.InitializeConfirmedEncounter(encounterId, player)` | Restore the persistent player values, then apply the existing pending-effect formula once after receiver readiness. |

`MapTravelTicket` carries `RunId`, `EncounterId`, `FromNodeId`, `NodeId`, `IsInitial` and `IsBoss`. The current node and Boss flag are available as `RunSession.CurrentEncounterNodeId` and `CurrentEncounterIsBoss`. Old, duplicated and cancelled encounter identifiers cannot advance a new encounter or adventure.

The loading operation begins during travel with scene activation held. A preload progress of 0.9 means that the scene is waiting for activation, not that the receiver is ready. After activation, the coordinator yields through initialization and waits up to five seconds for the active, tagged combat player in the destination scene. Disabled shadow objects sharing the Player tag are excluded. The pending offering is consumed only after this handshake. These distinctions follow Unity's [asynchronous scene progress](https://docs.unity3d.com/6000.3/Documentation/ScriptReference/AsyncOperation-progress.html) and [sceneLoaded lifecycle](https://docs.unity3d.com/6000.3/Documentation/ScriptReference/SceneManagement.SceneManager-sceneLoaded.html).

The map-side bridge listens to `BattleScript.OnAllEnemiesDefeated` and checks the confirmed player's health for defeat. A return to Map alone never counts as victory. The current `Deinosavros` scene has no active reward-screen receiver, so the coordinator returns automatically without generating rewards. If an active `RewardScreenController` is present, its existing victory/reward return flow retains ownership; the new fallback does not bypass it.

## Graph and roads

The shared explicit graph asset is `Assets/MapReview/MapGraph.asset`. Its original eleven production node transforms and collider payloads are retained. The added nodes are:

| Node | Existing platform |
| --- | --- |
| `level_02_03` | `Platform_03`, source terrace 02 |
| `level_04_03` | `Platform_09`, source terrace 08 |
| `level_05_03` | `Platform_12`, source terrace 11 |

Every complete graph route contains five normal encounters followed by one Boss encounter. Road bindings are authored, not guessed from node distance. `MapTravelPath` saves floor samples in environment-local coordinates and caches world samples and cumulative lengths when bound. There is no per-frame terrain search.

The two outer Boss roads, `Path_17` and `Path_19`, originally joined the sanctuary side walls. The user-approved Unity-only overrides bend their existing stone mesh ends into the current stair foot. They reuse the original vertices, indices and scene objects; they add no slabs and do not modify Blender, the Boss node, the portal or UI. Original source meshes remain referenced on the corresponding paths. Overrides are `Assets/Art/MapTravel/R17Road.asset` and `R19Road.asset`.

The three Boss paths each have twelve sampled stair intersections. Maximum adjacent sample height changes are 0.1972, 0.1725 and 0.1725 world units for R17, R18 and R19 respectively. Arrival rises from the platform landing to the existing portal energy mesh's center.

## Editing and reimport

Travel assets are under `Assets/Art/MapTravel`. `MapTravelProfile.asset` controls amber/cream colors, a 56x74-pixel flame quad (roughly 45x65 pixels of visible artwork), an 88-pixel halo quad, 44-pixel encounter badge quads and a 0.85-unit hover height. Dimensions use a 1080-pixel reference height, so they scale proportionally at higher resolutions. Idle docking shifts only the visible pawn left of the platform icon; route floors, node anchors and colliders do not move. Docking eases out before road travel and back in on normal arrival. `TravelSymbols.png` is original deterministic 4x4 atlas artwork generated by `Assets/Editor/MapTravelArt.cs`; no downloaded textures are used. `MapTravelFX.shader` uses scene depth and fog, preserves the dark outline, and accepts normal occlusion. Selected routes include a readable thread under the meander instead of relying on subpixel ornament alone.

`NodeInformation.asset` is the explicit per-node presentation catalog: stable node ID, kind, title, summary, optional enemy text and optional reward text. It is independent of graph topology and is preserved by reinstallation. The current formal receiver only implements combat, so the supplied entries truthfully use Battle/Boss; unknown enemies and rewards are left blank rather than invented. Event/Reward artwork is prepared, but changing a presentation kind does not implement or redirect an encounter receiver.

`MapNodeInteraction` owns one stable pointer picker for platform colliders and fixed icon rectangles. Icon enlargement never changes the hit region. The popup uses the existing canvas font/scale, stays in the camera's UI-safe area, allows a 0.18-second crossing grace and remains visible while its panel is hovered. Panel clicks, other HUD graphics, dragging off a node, locked levels and offering confirmation never trigger travel. `MapController.SelectNode` now means choose and depart; visual-only callers must use `MapTravelView.Preview`.

The `Travel` root is outside the replaceable environment subtree and is excluded from camera-subject framing. Its short names include `Core`, `Halo`, `Sigil`, `Trail` and `Routes/R01` through `R19`. Node marks and dynamic ribbon/mote meshes are created at runtime and cleaned up with the scene.

- `Tools > Map > Install Fire Seed Travel` backs up the live Map before applying/rebinding. It requires the formal Map in Edit Mode and preserves existing scene layout.
- Select a route object to inspect its saved points. Scene handles mark edited points as manual; subsequent rebinds retain them. `Check Road Height` reports unsupported or embedded samples.
- `Tools > Map > Verify Travel Reapply And Reload` requires saved scenes, applies twice, reopens Map, and compares canonical scene data, mesh streams and references.
- The existing explicit environment publish workflow invokes `MapTravelBuild.Rebind` using node IDs, source anchors and short-name aliases. It does not depend on old verbose model names.

Do not enable the isolated `MapReviewController` or its simulation host in the formal Map. They remain separate test workflows.

## Revised interaction verification

The final `Interaction02` standalone run is preserved at `Tools/MapTravel/Delivery/2026-09-30/Interaction`:

- Hover delay, stationary hover, crossing into the detail panel, panel click blocking, dragging off a node, card UI blocking, offering confirmation, three-way hover switching and locked-level rejection passed.
- A single pointer click on a badge or platform starts the encounter; repeated calls cannot issue a second ticket. Preview never commits location.
- All nineteen authored routes reached their saved landing; all three Boss routes reached the portal center. Real-battle initial entry, ordinary victory/return, Boss completion, defeat, new run, missing scene, delayed readiness and timeout rollback passed.
- 1920x1080, 2560x1440 and 1920x1200 passed scaled pawn size, fourteen-node framing, hover targeting and information-panel/card-bar separation checks. Corresponding idle and hover screenshots are included.
- Two reapplications and a saved reload passed; the original eleven node payloads, all fourteen current node transforms, fifteen colliders, 100 UI rectangles and 279 independent environment mesh bindings/layouts were preserved.
- The final 1080p uncapped movement sample recorded 10,940 frames over 30.72 seconds: mean 2.808 ms (356.1 FPS), p95 3.149 ms, p99 3.286 ms, max 4.846 ms and no frames over 16.67 ms. This is a short movement sample, not a five-minute soak or combat benchmark.
- Warmed Travel updates recorded zero managed bytes over 10,940 calls. Whole-process measured allocation was 593,540 bytes; peak live motes were 49. Runtime errors were zero. The pre-existing renderer shutdown leak warnings documented below still occur and are not suppressed.

`03-choice-tooltip.png` shows the final hover UI, `R01-moving.png` shows the readable moving pawn, and `Travel.mp4` is a roughly two-second ordinary-route sample. No unrelated card artwork, environment models or battle formulas were changed for this revision.

## Original implementation verification record

The table below is the earlier twenty-pixel/click-confirm baseline, not acceptance of the revised hover/click interaction. See `Tools/MapTravel/Delivery/2026-09-30/Interaction` for the revised screenshots and verification report.

Evidence is preserved under `Tools/MapTravel/Delivery/2026-09-30`. Working captures and exact live-scene backups remain under `Library/MapReviewEvidence/2026-09-30/Travel`.

| Check | Recorded result |
| --- | --- |
| Pure progression tests | 1,678 assertions; all nine complete graph routes and nineteen edges. |
| Protected production data | Eleven original node transforms/collider payloads and 100 UI rectangles match the live pre-install snapshot; 279 independent environment meshes remain. |
| Nineteen visual routes | Every saved route reaches its landing; all three Boss routes reach the portal center. |
| Interaction | Pointer preview, UI blocking, confirmation freeze/cancel, repeated confirm and movement lock passed. |
| Real battle round trips | Initial entry, normal entry, victory return, Boss flag/victory and health-zero defeat passed against the current battle scene. |
| Readiness and rollback | Missing scene, five-second unavailable receiver, delayed initialization, retained offering and duplicate acknowledgement/result passed. |
| Resolution | 1920x1080, 2560x1440 and 1920x1200 passed node/UI-safe framing and approximately 20-pixel seed-size checks. |
| Runtime errors | Zero logged runtime errors during the final Runtime03 validation sequence. Shutdown native-memory warnings are tracked separately. |
| Reapply and saved reload | Passed two consecutive installations and saved reload; nineteen paths, short names, node/collider data, UI payloads, asset GUIDs and both Boss override mesh streams remain identical. |
| Movement performance | 60.66 seconds, 22,032 rendered 1080p frames, mean 2.753 ms / 363.2 FPS, p95 3.258 ms, p99 3.579 ms, max 10.417 ms, zero frames above 16.67 ms. |
| Travel hot-path allocation | 22,032 warmed `MapTravelView.LateUpdate` calls, zero managed bytes; setup and per-departure coroutine creation are excluded. |
| Particle budget | Observed peak 49 motes, below the 96-particle arrival limit; continuous emission is capped at 48 slots. |

Performance was measured in the uncapped development standalone player on an NVIDIA GeForce RTX 5060 Laptop GPU and AMD Ryzen 9 8945HX. This is a one-minute movement measurement, not a five-minute soak or a combat-performance claim. Whole-process GC allocation was 1,199,250 bytes across the sample, including existing scene/UI behavior and verification instrumentation. A separate current-thread allocation counter around the warmed Travel update recorded zero managed bytes; this does not establish zero allocation for the whole game.

Shutdown diagnostics reproduce the pre-travel `FormalFinal/player.log` warnings: one TempJob allocation, 119 Persistent allocations and 1,240,348 bytes in the NativeArray category. Enabled leak call stacks identify Unity rendering's `GPUResidentDrawer`, `InstanceCullingBatcher` and `CPUInstanceData` allocations. These pre-existing renderer shutdown warnings are not counted as passed leak acceptance and are not suppressed. No shared renderer setting is changed to hide them. See the preserved baseline log and `NativeDiagnostic/player.log` for the comparison.

The native diagnostic uses a capture-only subset through R18; its generic traversal-summary line is not nineteen-route acceptance. Only `Runtime03/verification.txt` is the complete final route/battle/performance record. The four shared settings files were restored to their captured pre-build contents, including existing user modifications; see `settings-preservation.txt`.

`MapTravelHarness` is inactive in normal play. Running the development player with `-travelValidation -travelOutput <folder> -travelBenchmarkSeconds 60` enables isolated, process-only pointer events and deterministic tests. It does not operate the desktop mouse. Real-battle victory tests set existing opponents' health to zero to exercise the existing result event; they do not test combat balance. Some later graph positions are set up as fixtures rather than claimed as a full six-fight playthrough.

## Remaining battle-module responsibilities

The current scene receives the node identifier and Boss flag but uses its existing combat content. Per-level enemy configurations and an actual Boss encounter still belong to the battle module. Front-card effect formulas and offering formulas are not replaced. The subsequent battle-UI repair restores attribute transfer, preserves fractional attack intervals and binds the existing deck manager to the run deck. Later-wave offering support, temporary-shield removal and new reward rules remain outside this repair. The separate review encounter contract must not be read as proof of those features in real combat.

## Battle UI repair

The earlier route-only acceptance did not verify visible combat UI. The saved battle scene had an inactive `CardHolder` and no `DeckManager`. Health/stat labels also searched for any Canvas, which could select the persistent transition layer. Player transfer omitted `ApplyPlayerStats`, and capture overwrote the attack interval with one.

The repair activates the existing hand container and adds one `DeckManager` and one scene-owned `BattleHud`. Original placeholder cards are retained for direct-scene preview, but hidden when a run deck is initialized; they do not duplicate the generated hand. `DeckManager.TryInitialize` is idempotent and uses only unsacrificed definitions and their existing combat prefabs. Card views bind to the active player rather than the disabled Player-tagged shadow. No effect magnitudes, costs, durations, draw delays, card artwork or layout settings are changed.

Both label scripts have an explicit battle Canvas reference. Their compatibility fallback searches only their own scene, never the persistent transition scene. Label geometry is clamped inside the game window using its rendered text bounds, without moving actors or cards. Text does not intercept card clicks. Departure readiness now requires the hand and player/enemy HUD in addition to a valid player; readiness failure retains the previous position and offering.

`RunSession` stores the attack interval as a float and captures the actual value. The confirmed encounter restores HP/max HP, damage, attack interval, shield, Elixir and its maximum before applying an offering. Map status formatting preserves fractional seconds. This is value transport, not a combat-formula redesign.

The scoped scene installer is `Tools > Map > Repair Battle UI Bindings`. It backs up the saved combat scene, binds the existing objects, checks all pre-existing transforms/colliders/combat component data, and restores the previous editor scene. The opt-in standalone check is `-travelValidation -battleUIValidation -travelOutput <folder>`; it uses process-local input, not the desktop pointer.

### Verified repair and limits

The final focused standalone run is preserved at `Tools/MapTravel/Delivery/2026-09-30/BattleUI`. `verification.txt` records zero runtime errors during the checks, with renderer shutdown warnings retained separately in `player.log`.

- Four unsacrificed cards were visible, bound to the actual player and clickable. The existing shield card cost/effect, timed attack-speed card expiration and delayed redraw passed. The timed front-card effect restored a 2.75-second attack interval.
- The player attribute block and all four active health labels rendered inside the game window at 1920x1080, 2560x1440 and 1920x1200. No combat text was parented to the hidden transition Canvas.
- HP/max HP, damage, fractional attack interval, shield and current/maximum Elixir survived the tested victory return and next encounter. A sacrificed definition was excluded from the next hand; duplicate receiver initialization did not reset values or reapply the offering.
- Missing-scene rejection, delayed readiness, five-second readiness failure, preserved pending offering and restored Map interaction passed.
- Reapplying the installer retained exactly one HUD and one deck manager. All 367 pre-existing transforms, 11 colliders and eight combat component payloads remained unchanged.
- The screenshots use deliberate test-fixture attributes, not changes to production starting values. This is functional UI verification, not a new combat-performance benchmark or combat-balance test.

Known boundary: the existing bridge still captures the player's live modified values. Encounter-only offering bonuses are not yet separated from persistent state; for example, the final failure-recovery test's Fleet Footwork offering leaves a 1.75-second interval on return. Remaining temporary shield has the same attribution problem. The timed front-card expiry check above does not establish correct cleanup of these separate offering effects. Proper encounter-effect ownership and cleanup remain a battle-integration task; this repair does not silently subtract bonuses or change combat formulas.

No disk save system, audio, camera controls, Blender changes or Git commits are part of this delivery.
