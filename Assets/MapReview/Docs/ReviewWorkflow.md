# Map environment and review workflow

As of 2026-09-30, the user has approved synchronizing the v3 environment into the production Map. Only the environment is promoted: production retains its eleven nodes, existing UI and battle chain. The fourteen-node graph and revised offering workflow remain isolated in MapReview. No Git commit is created.

## Open and inspect

- Open `Assets/Scenes/Map.unity` for the v3 environment with the unchanged production UI and gameplay.
- Open `Assets/MapReview/Scenes/MapReview.unity` and enter Play Mode for the map, original card artwork, offerings and player status.
- Open `MapSampleReview.unity` for the earlier representative column, fallen segment, palm, rooted plants and paving junction. These study scenes are not the final v3 ground review.
- Open `MapPortalReview.unity` for the portal frame and footing at the game camera angle.
- `MapEncounterReview.unity` is a deliberately explicit test receiver. It supports delayed readiness, failure/retry, extra enemies, resource spending, rewards and duplicate results. It does not run the real battle.
- `MapBaselineReview.unity` is the captured production-scene copy used for comparison. Do not attach the new review controllers to production scenes.

The four original card definitions are the starting deck. The opt-in verification harness adds duplicate reward instances to test paging and over-capacity display, then resets the run. Uncertain Fates has no completed illustrated face among the existing project card-face assets; its original frame and effect text are retained rather than inventing a new front.

## Safe updates

`Tools > Map Review > Sync Environment to Formal Map` requires the saved production Map to be active. It imports `RuinsV3Review.fbx`, derives rigid placement from the eleven existing anchors, preserves UI/gameplay and nested lighting/Volume objects, validates the result and creates a rollback copy before saving. It does not install review controllers or add production nodes. Do not use the legacy environment importer to update v3: that importer still targets the earlier model.

`Tools > Map Review > Apply Visuals Only` updates review materials, lighting and the dedicated volume from `MapVisualProfile.asset`, without reimporting geometry or moving nodes.

`Tools > Map Review > Reimport Current Environment` uses the explicit source record on the review environment root. The importer replaces only that root; UI, gameplay state objects and node transforms/collider geometry are not replaced.

`Tools > Map Review > Run State Contract Tests` writes the state-test report under `Library/MapReviewEvidence`. The explicit graph asset contains fourteen nodes and nineteen directed edges; there is no nearest-neighbor route inference.

The original start interaction is lower than the Blender start anchor by 0.21 units. This pre-existing height is deliberately preserved; all original eleven XZ centers retain their exact positions. The three additional nodes derive from source terraces 02, 08 and 11.

## Reproducible checks

Use the Unity Editor batch entry points in `Deinosavros.MapReview.Editor.MapReviewBuild`:

- `BatchPrepare`: initial isolated copies, graph, art and review scenes. Do not use this as a daily reimport because it intentionally starts from the production baseline.
- `BatchReimport`: requires a review scene/model path; imports twice and verifies unchanged node transforms/colliders and stable independent mesh identities.
- `BatchRefreshSampleAndBuild`: refreshes sample review scenes, original surface textures and existing card art, then creates the standalone review player.
- `BatchFullReviewBuild`: supply `-reviewModel Assets/MapReview/Art/Models/RuinsV3Review.fbx -reviewScene Assets/MapReview/Scenes/MapReview.unity` explicitly. It combines the repeated import check and review build.

The scene list is supplied to each standalone build directly. The default project build/start scene list is not changed.

Batch reimport requires both explicit model and scene arguments. Existing visual-profile values are preserved during rebuild; defaults are initialized only when the isolated profile is first created.

Run `Builds/MapReview/MapReview.exe` with `-reviewValidation -reviewCaptureScene MapReview -reviewUiTests -reviewBenchmarkSeconds 300 -reviewOutput <absolute-output-directory>` to execute the opt-in verification. Do not use `-batchmode` for the player captures: this machine returned black back-buffer captures in that mode. The harness forces an initial window resize and rejects black images.

The current local verification builds are `Builds/MapReviewRevision/MapEnvironmentRevision.exe` (starts directly in MapReview) and `Builds/MapEnvironmentRevision/MapEnvironmentRevision.exe` (starts in production Map). Start the review tests in the review-first build: loading production first creates a persistent legacy RunSession and correctly fails isolation checks. Production capture uses `-reviewValidation -reviewCaptureOnly -reviewCaptureScene Map -reviewOutput <absolute-output-directory>` and does not exercise offerings or production battle transitions. `-reviewSurfaceDiagnostic` additionally captures reversible normal-off and directional-shadow comparisons without saving runtime changes.

Runtime verification uses queued Input System pointer events inside the test player, not the desktop mouse. It captures 1920x1080, 2560x1440 and 1920x1200, tests UI and actual scene transitions, resets to a neutral map, warms up for fifteen seconds, and records uncapped standalone frame times for the requested duration. Optional CPU/GPU counters explicitly report unavailable data rather than treating missing values as zero.

## Asset authorship and boundaries

Environment geometry and generated surface/foliage textures are original. No CC0 or third-party environment texture was downloaded. Existing project card artwork is reused unchanged.

The current native file is `D:/Maya建模/Blender/MapRebuild_20260924/V2_Review/GroundRevision_20260930/MapScene_Environment_Ruins_v3.blend`. The original source and v2 files remain unchanged. The game export preserves independent objects and all fourteen canonical anchors. The island stores linear normalized COLOR_0 weights (R soil, G rooted moss, B exposed rock); export these as LINEAR, not the Blender FBX default SRGB. Original textures are generated by `ReviewSurfaceTextures.Generate(true)` while retaining texture GUIDs. The legacy run/battle bridge and shared rendering configuration must remain unchanged.

For the encounter contract and a working receiver example, use `MapEncounterHandoff.md` and `Runtime/MapEncounterReviewController.cs`. Real battle integration remains a teammate task. Use the final acceptance report and captured images for completion status; successful compilation alone is not visual acceptance.

The delivered comparison images and verification reports are retained under `Tools/MapReview/Delivery/2026-09-27`, outside Unity's disposable `Library` cache. The acceptance report records measured performance and known engine-package warnings separately from review-code errors.
