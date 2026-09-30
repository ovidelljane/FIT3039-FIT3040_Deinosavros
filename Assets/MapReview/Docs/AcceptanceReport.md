# Map review acceptance record

Date: 2026-09-27. Scope: isolated stylized environment and Map-side offering workflow. Production battle integration is not part of this delivery.

## Deliverables

- Editable native model: `D:/Maya建模/Blender/MapRebuild_20260924/MapScene_Environment_Ruins_v2.blend`.
- Review environment: `Assets/MapReview/Art/Models/RuinsV2Review.fbx`.
- Main and receiver scenes: `Assets/MapReview/Scenes/MapReview.unity` and `MapEncounterReview.unity`.
- Representative studies: `MapSampleReview.unity` and `MapPortalReview.unity`.
- Dedicated graph, visual profile, volume, materials, original surface maps and original clipped-leaf atlas under `Assets/MapReview`.
- Receiver contract: `MapEncounterHandoff.md`. Reproduction: `ReviewWorkflow.md` and `Tools/MapReview/README.md`.

## Geometry and authorship

The full exported environment contains 279 independently editable mesh objects and 318,642 evaluated triangles. Runtime foliage accounts for 75 mesh objects and 66,696 triangles. High-detail authored foliage remains editable in the native file. The previous full candidate had 452,986 triangles; the current candidate adds rooted growth while reducing the triangle count by approximately 30 percent.

The native reopen audit reports zero degenerate faces, no missing material slots, 23 architecture meshes with UVs, and 16 background meshes. Alpha-clipped leaf cards and the energy-veil sheet are intentionally open surfaces; structural solids are checked separately. The original eleven anchor matrices and Boss portal matrix remain unchanged. Fourteen anchors and nineteen directed routes are present. The three new anchors derive from source terraces 02, 08 and 11.

Material sources are original. The nine 2048 surface maps are deterministically generated; the three 2048 foliage maps are derived from authored source leaf geometry. No CC0 or third-party environment textures were downloaded. Existing project card artwork is reused without changing the shared originals.

## Verification status

| Check | Result / evidence |
| --- | --- |
| Native file saved and reopened | Passed; `V2_Review/native-reopen-check.json` |
| Explicit graph and offering state contract | 580 assertions and all nine complete routes passed; `Library/MapReviewEvidence/state-tests.txt` |
| Full-model repeated import and scene reopen | Passed twice, with stable mesh identities, transforms and colliders; `reimport-verification.txt` |
| Three-resolution framing and original colliders | Passed at 1920x1080, 2560x1440 and 1920x1200; full framed bounds and all fourteen label/collider checks |
| Hover, reversal, modal blocking and receiver scene transitions | Passed on the final atlas build; 2,434 runtime assertions including sampled animation frames; `FinalDelivery/verification.txt` |
| Final shader compilation and material references | Review shaders compiled; original map references and five persistent Volume subassets passed verification |
| Five-minute warmed standalone performance | Passed the 60 FPS frame-time target for this run: 300.03 seconds, 118,149 samples, no frame above 16.67 ms |
| Protected production files and shared settings | Six protected SHA256 values match baseline; the five build-generated shared-settings diffs were restored |

The earlier `UiLifecycle` run passed 899 runtime checks against the earlier environment. Final-model verification repeats the pointer, framing and receiver checks rather than substituting that older result. Earlier failed runs remain under separate evidence folders: a stripped runtime-only outline shader was replaced with a serialized material; large-frame detail timing was corrected; and the hidden-player synthetic-pointer focus issue was isolated to the opt-in test harness. No failed or black-image capture is counted as acceptance.

The repeated-import scene totals are 395 transforms and 312 mesh objects: 279 environment meshes plus 33 retained, disabled legacy-node visual meshes. Those disabled node children remain to preserve the original interaction hierarchy, not as duplicate environment geometry. The importer replaces only the explicit environment root. Rebuild preserves the existing visual-profile bytes (SHA256 `3CCA996D816DD849687454C49ED2F5A323E7FAB2BE636BA1D486B15E91DACA01`) and now requires explicit model and scene arguments for batch reimport.

## Visual review and comparison

The preserved delivery folder is `Tools/MapReview/Delivery/2026-09-27`, outside the disposable Unity Library cache. Its `00-baseline-1920x1080.png` is the unchanged production-scene copy captured by the standalone player; `01-1920x1080-ui.png` is the rebuilt review scene. Native clay, reverse, plant, broken-column and portal views are in `D:/Maya建模/Blender/MapRebuild_20260924/V2_Review`.

Actual final game captures are under `Library/MapReviewEvidence/FinalDelivery`: `01-1920x1080-ui.png`, `02-2560x1440-layout.png`, `02-1920x1200-layout.png`, `03-hover-back-detail.png`, `04-sacrifice-confirmation.png` and the receiver/progression captures. The neutral image after the full interaction sequence is `10-fresh-map-after-integration-tests.png`. The Boss state cue is generated above the imported portal bounds; its node, collider and portal aperture remain unchanged.

The main comparison, three-resolution, hover, confirmation, receiver and Boss-result images are also copied to the preserved delivery folder, together with the state/import/alignment/build reports and standalone performance evidence.

Blender images demonstrate editable geometry but are not presented as Unity game output. Visual direction remains subject to the user's art review; automated tests cannot establish subjective visual approval.

## Measured standalone performance

Hardware: NVIDIA GeForce RTX 5060 Laptop GPU; AMD Ryzen 9 8945HX. Windows development standalone, D3D11, 1920x1080, full card UI, uncapped frame rate, VSync off. The final run resets after interaction tests, warms up for fifteen seconds and then samples for 300.03 seconds. Unity Editor and Blender were closed. These are measurements on this computer, not a promise for every PC or for the real battle scene.

| Metric | Final review, 300 seconds | Unchanged baseline reference, 30 seconds |
| --- | --- | --- |
| Mean frame time | 2.539 ms | 3.753 ms |
| Median frame time | 2.523 ms | 3.734 ms |
| 95th percentile | 2.725 ms | 3.961 ms |
| 99th percentile | 2.825 ms | 4.184 ms |
| Maximum frame time | 5.863 ms | 5.834 ms |
| Mean uncapped FPS | 393.8 | 266.4 |
| Frames over 16.67 ms | 0 / 118,149 | 0 / 7,994 |
| Mean GPU work | 2.503 ms | 3.721 ms |
| Mean draw calls | 650.4 | 2,598.4 |
| Mean rendered triangles, including extra passes | 612,212.5 | 4,411,451.6 |
| Mean total used memory | 540.7 MiB | 586.9 MiB |

FrameTimingManager supplied 118,130 unique positive GPU samples. Paired CPU main/render means were 2.532/0.776 ms; mean present wait was 0.001 ms. Aggregate CPU and GPU times are close, so the likely limit is balanced frame work rather than a demonstrated isolated bottleneck. A separate `Render Thread` ProfilerRecorder counter was unavailable; the table does not invent it. The baseline interval is shorter and is contextual, not an equal-duration controlled performance comparison.

The final runtime reported zero errors and completed all six-encounter, offering, duplicate-delivery, failure/reset, paging and pointer checks. Raw chronological frame samples are in `Tools/MapReview/Delivery/2026-09-27/frame-times-ms.csv`; `verification.txt` contains runtime checks and counters. Native audits, build/import reports, protected hashes and before/after images are kept alongside it. The five-minute measured frame-time target is met; aesthetic approval remains a separate user review.

## Known boundaries

- Uncertain Fates has no completed illustrated face in the existing project face-art folder. Its original colored frame and `+3 Elixir` presentation are retained; no replacement front was invented.
- The original start collider is 0.21 world units below its Blender anchor. This pre-existing Y difference is preserved rather than silently moving gameplay data. XZ alignment is verified.
- Player state is in memory across review scene changes. Disk saves and real battle integration remain out of scope.
- Exit-time Unity/URP buffer-disposal and native-allocation diagnostics also occur in the unchanged baseline player. They are not counted as an in-run gameplay pass and are not hidden by changing the shared rendering pipeline.
- The engine package shader `Hidden/Core/DebugOccluder` reports an implicit vector-truncation warning on D3D11. The review shaders have no reported compilation errors; package code is intentionally not changed.
- Whole-worktree `git diff --check` still reports pre-existing production-scene whitespace and a conflict-marker line inside a block comment in the legacy BattleRunBridge. Both files match their recorded baseline hashes. Those unrelated user changes were not repaired as part of this isolated review work.
- No Git commit is created. User-owned dirty changes, the original Blender file, production Map, legacy battle bridge, and default build scene list are preserved.

Map-side validation is complete. Real battle integration remains for the teammate receiving `MapEncounterHandoff.md`.
