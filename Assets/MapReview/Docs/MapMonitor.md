# Map Monitor

Open **Tools > Map > Monitor**, then run the formal Map scene. The window attaches to the existing RunSession and follows battle, opportunity, recovery and map transitions. It never initializes another session.

## Pages

- **Overview:** committed node, departure origin and target, encounter type and IDs, input lock, transition state, fire seed position, player attributes and current or last non-combat receipt.
- **Nodes / Roads:** all nodes from the live run's copied graph, explicit outgoing connections, traversed roads and locked/available/completed/skipped states. Locate selects and frames the scene object; it never travels or unlocks a node.
- **Deck / Offering:** capacity points out of 20, every card instance including sacrificed cards, pending effect and allowance. Duplicate card definitions remain separate instances. Ping Asset locates the original definition without editing it.
- **Event History:** run, scene, departure, readiness, cancellation, completion, defeat, deck, offering, receipt and notice changes. Newest events are first; at most 200 are retained. Repeated unchanged samples and per-frame attribute changes do not flood the log.

## Safe testing controls

The window starts read-only. Pause, Resume, Step Frame, Select Session and Locate Current do not change adventure data. Pause uses the editor's pause state, not Time.timeScale.

Start New Test Run is hidden behind an explicit test-controls toggle and a confirmation dialog. It is available only in Play Mode on an idle Map, with no sacrifice modal, no active encounter and no travel/loading transition. It is disabled while paused. The action rechecks the run ID and current state after confirmation and resets through MapController's normal new-run flow, including cards, selection and fire seed presentation. It cannot be undone, but never saves a scene or asset. Test controls disarm when Play Mode changes or scripts reload.

There are intentionally no force-win, arbitrary teleport within an existing adventure, unlock-all or capacity-bypass controls. Test start configuration below is applied only to a fresh run, never to an active encounter.

## Start directly at a node

1. Run the formal Map and open **Tools > Map > Monitor**.
2. Enable **Enable destructive test controls for this window session**.
3. Choose **Start Node**, for example **L02_02 - Recovery**.
4. Enable **Set Starting HP** and enter **50**, or disable it to use the original starting HP. Valid custom values are 1 through the original starting maximum; maximum HP is not changed.
5. Click **Start Test Encounter...**, then confirm **Reset and Enter**.

This replaces the current adventure with a fresh test run, restores the starting deck and attributes, clears offerings and receipts, positions the fire seed at the chosen node and enters using the normal ignition, asynchronous loading and receiver-readiness protocol. It does not draw an invented road from L01_01 to an unconnected destination.

Earlier layers become **Skipped**, not **Completed**; no wins, rewards, capacity changes or prior recovery transactions are simulated. When the chosen encounter completes, only its real outgoing branches open. The overview and JSON reports identify the run with its test start node. **Start New Test Run...** removes the seed and returns to normal L01_01 behavior.

With the current 100 maximum HP, L02_02 entered at 50 HP recovers exactly 15 HP, returns at 65 HP and opens L03_02/L03_03. The same controls can enter opportunity, ordinary battle or Boss nodes. Opportunity remains the real no-grant placeholder; Boss uses the existing battle receiver with the Boss flag.

Missing node/site/routing/scene or invalid HP is rejected before resetting the adventure. Paused, moving, loading, modal and active-encounter states reject the operation. Repeated clicks and a run changing while confirmation is open cannot create duplicate departures. If an asynchronous receiver later fails, normal rollback retains the newly created test start for retry; the replaced adventure is not restored.

## Important distinctions

- Current location is committed only after the receiver confirms readiness. During travel/loading, read the separate origin and target fields.
- Selected and hovered nodes are not authoritative player locations.
- Attack speed displays attacks per second and the actual stored seconds-per-attack value.
- Player values are the session's current captured values, not a separate base-stat versus temporary-buff breakdown.
- Recovery shows before/after HP, actual healing and transaction completion. Opportunity shows offer count and a granted instance only if a reward was really granted. The production placeholder has no reward.
- The observer records during Play Mode even if the window is closed. It samples live values every 0.25 seconds and reacts immediately to progression/deck events.
- After stopping Play Mode the last snapshot is clearly labeled as historical. A new Play session clears the log. Editor SessionState retains diagnostic history across editor assembly reloads, but this is not a gameplay save system and does not preserve a run across exiting Unity.
- Copy Report and Export JSON include the sampled state and recent history, with timestamps. Exports describe only the captured session, not a full replay or performance profile.

## Verification

Run **Tools > Map > Verify Monitor** in Edit Mode. It uses temporary inactive fixtures and does not save gameplay scenes. The result is written to `Library/MapMonitor/verification.txt`. Tests cover all 14 nodes, 19 connections and nine complete routes; read-only snapshots; card identity; pending effects; cancellation; failure; recovery receipts; reset guards; bounded history; and report serialization.

**Tools > Map > Verify Monitor Play Mode** requires the formal Map and no existing Play session. It backs up the live scene as a separate copy, starts a temporary Play session, draws all four pages, exercises the guarded reset, enters the real battle through the normal map entry, exports a report and stops Play Mode. Existing unsaved edits and the original dirty state are preserved. It temporarily enables runtime background frames for this test only, restores that value and never changes PlayerSettings. Results are in `Library/MapMonitor/play-verification.txt` and `Library/MapMonitor/live-report.json`.

**Tools > Map > Verify Monitor Test Starts** additionally checks invalid requests without resetting, duplicate departure protection, L02_02 at 50 HP and its real Continue/return, L02_01 without a granted card, normal new-run restoration and direct Boss entry. Recovery/Boss evidence is exported to `Library/MapMonitor/test-start-recovery-report.json` and `Library/MapMonitor/test-start-boss-report.json`.

Verified on 2026-09-30 in Unity 6000.3.9f1: 314 state/data assertions passed; all four window pages rendered; new-run presentation reset passed; real Map-to-Battle session tracking passed with reset disabled in battle. The four gameplay scene files and build scene list were byte-identical before and after verification. No scene save, Blender change or Git commit was performed.

Test-start extension verified on 2026-09-30: 1,113 assertions passed, including all 14 seeded starting nodes. Real Play Mode checks confirmed L02_02 at 50/100 HP returned at 65/100 HP with only L03_02/L03_03 open; opportunity granted no card; normal restart cleared the seed; Boss entry preserved the Boss ticket. Invalid HP/node, missing scene, pause and duplicate-start protections passed. Existing hidden legacy node parents were not enabled or changed. The latest four scene files and build list were unchanged throughout the Play Mode checks.

All window, polling, history and verification code lives in Editor folders and is excluded from player builds. Runtime changes are limited to read-only state accessors, a shared new-run presentation helper and guarded test reset/start APIs inside UNITY_EDITOR blocks. No test-start seed or custom starting-HP API is included in player builds.
