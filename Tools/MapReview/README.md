# Map review implementation

This workspace is isolated from the production Map and real battle pipeline. No commit is created by these tools.

## Delivery gates

1. Record source hashes, the working tree, node transforms and colliders, and a production camera baseline.
2. Build an editable geometry study in `MapScene_Environment_Ruins_v2.blend` from the preserved ruins file.
3. Review neutral clay, reverse geometry, materials, game camera, and actual card UI together. Do not promote unapproved samples across the environment.
4. Export reviewed environment assets into `Assets/MapReview`, never into the production environment asset directory.
5. Exercise the isolated graph, instance-based sacrifice state, and fake encounter receiver.
6. Check reimport, resolutions, persistence, and measured standalone performance before claiming completion.

The original eleven anchors and portal transform are immutable. Added anchors derive from terraces 02, 08 and 11. Runtime review code must not alter `RunSession`, `BattleRunBridge`, front-card behavior, or reward policy.

## Status

- Source inspection: complete; existing nineteen roads match the requested explicit graph.
- Representative geometry gate: passed after neutral/reverse/native and actual Unity camera with illustrated card UI review. This is a promotion gate, not final visual acceptance.
- Full Blender geometry and runtime foliage candidate: saved, reopened, audited, and exported. Final Unity import, three-resolution camera/UI checks and isolated receiver tests pass. The final standalone run measured 300.03 seconds at 1920x1080, mean 393.8 FPS and 2.825 ms p99, with no sampled frame above 16.67 ms. Art approval remains a user review, not an automated-test claim.
- Preserved delivery: `Tools/MapReview/Delivery/2026-09-27`; detailed status and limitations: `Assets/MapReview/Docs/AcceptanceReport.md`.
- Real battle integration: intentionally excluded; teammate handoff required.

All generated materials and geometry are original. No third-party texture downloads are used.

## Reproducible Blender pipeline

The editable review file is [MapScene_Environment_Ruins_v2.blend](D:/Maya建模/Blender/MapRebuild_20260924/MapScene_Environment_Ruins_v2.blend).
The evidence and generation output directory is [V2_Review](D:/Maya建模/Blender/MapRebuild_20260924/V2_Review).
Blender executable: `D:/blender.exe`, tested with Blender 5.2.1 LTS.

Run from the repository root. Supply the `V2_Review` directory above as `ReviewDirectory`. The V2 file retains the original source scene internally; `full-revise` rebuilds only the owned `V2 Environment Review` scene. It does not overwrite the original ruins, original MapScene, or Unity production assets.

```powershell
param([Parameter(Mandatory = $true)][string]$ReviewDirectory)

$reviewRoot = (Resolve-Path -LiteralPath $ReviewDirectory).Path
$nativeRoot = Split-Path -Parent $reviewRoot
$nativeReview = Join-Path $nativeRoot 'MapScene_Environment_Ruins_v2.blend'
$nativeSource = Join-Path $nativeRoot 'MapScene_Environment_Ruins.blend'
$staging = Join-Path $reviewRoot 'Export_Staging'
$reopenReport = Join-Path $reviewRoot 'native-reopen-check.json'

& 'D:/blender.exe' --background --disable-autoexec $nativeReview --python 'Tools/MapReview/build_ruins_v2.py' -- $reviewRoot full-revise
& 'D:/blender.exe' --background --disable-autoexec $nativeReview --python 'Tools/MapReview/refine_runtime_foliage.py' -- $reviewRoot
& 'D:/blender.exe' --background --disable-autoexec $nativeReview --python 'Tools/MapReview/export_review.py' -- $staging
& 'D:/blender.exe' --background --disable-autoexec --python 'Tools/MapReview/verify_native_review.py' -- $nativeSource $nativeReview $reopenReport
```

Inspect each Blender log for a traceback; a Python failure may still leave Blender with process exit code zero. Do not publish files after a failed stage.

1. `build_ruins_v2.py`: deterministic authored island, embedded courts, nineteen routes, entrance remnants, varied colonnade bays, sanctuary connections, connected source foliage, and architectural UVs. Requires `sample-gate.json` and its actual game/UI capture to exist. A first revision backup is kept as `geometry-pass01.blend`. Source/sample scenes are retained.
2. `refine_runtime_foliage.py`: moves tall foreground crowns away from court centers using actual game-camera feedback; adds connected off-route growth; replaces only the review copy of the inherited degenerate portal rim; creates the original leaf atlas and three-quad curved cards per pinna. Trunks, petioles, and rachises remain geometric. The high-detail source pinnae remain in `V2 Editable High Detail Leaf Sources`, hidden rather than deleted. Atlas specimen geometry remains in the separate `V2 Original Leaf Atlas Source` scene.
3. `export_review.py`: exports independent objects and canonical names into source/sample/portal/full FBX files. Canonical renaming and triangulation happen only in the temporary export process; they are not saved into the native file. The full model is `RuinsV2Review.fbx`; `RuinsManifest.json` includes all source and V2 materials, object mapping, geometry checks, UV channels, backgrounds, and anchors.
4. `verify_native_review.py`: reopens the original and V2 files, compares retained source geometry/anchors/portal, then verifies all original eleven canonical full-review anchor matrices, fourteen total full-review anchors, and the original portal matrix.

Publish only when Unity is closed or the coordinating build has completed. Copy the reviewed staging FBX files and manifest into `Assets/MapReview/Art/Models`. Copy the three original atlas PNGs from `V2_Review/Original_Foliage_Atlas` into `Assets/MapReview/Art/Textures/OriginalFoliage`. Do not delete or regenerate existing Unity `.meta` files. Publishing the model files alone does not apply or validate a Unity scene.

## Original foliage atlas contract

- All maps are 2048 by 2048, generated by raster-baking the authored source leaf triangles, vertex normals in a leaf-local tangent basis, and original material colors. No external image inputs are used.
- `RuinsV2_Leaf_BaseColor.png`: sRGB RGBA, with alpha clip silhouette and padded RGB for mip filtering.
- `RuinsV2_Leaf_Normal.png`: linear OpenGL tangent-space normal, positive Y along the blade.
- `RuinsV2_Leaf_Roughness.png`: linear grayscale roughness.
- Material: `V2 Runtime Original Leaf Atlas`. Use white base-color tint and alpha clip `0.35`; do not multiply the baked albedo by the manifest's viewport preview color.
- UV cells, using Blender's lower-left origin: mature palm bottom-left, new palm bottom-right, old palm top-left, fern top-right. Each pinna runs in positive V. Card root/tip UVs are inset two pixels into the opaque blade region, not the cell's transparent padding.
- Runtime cards are deliberately open, two-sided surfaces. Their intentional boundaries must not be confused with structural mesh failures. Shared world-space wind on the cards and geometric axes is required to preserve moving attachment points.
- `runtime-foliage-manifest.json` records the source/runtime object mapping, leaf counts, atlas provenance, root sites, and texture paths.

## Latest native and export audit

Measured on the published full candidate after the atlas, growth, and rim cleanup pass:

| Check | Result |
| --- | --- |
| Independent exported meshes | 279 |
| Evaluated full export triangles | 318,642; previous candidate 452,986 |
| Runtime leaf objects | 75 |
| Runtime leaf triangles | 66,696 |
| Degenerate export triangles | 0 |
| Missing material slots | 0 |
| Architectural meshes with unique UV channels | 23 |
| Canonical anchors | 14; original 11 matrices unchanged |
| Explicit route meshes | 19 |
| Portal | Original exact matrix retained; clear aperture radius 2.09 |
| Background meshes | 16: six karsts, eight fossil components, two continuous mist/floor surfaces |
| Open non-card geometry | Only original portal energy veil: intentional 126-triangle sheet |
| Source scene preservation | Retained source names and geometry counts, anchors, and portal match original after reopening |

Original ruins source SHA-256: `49af57d0179372ea0e2c91409da37058f83aeee00c76c0da1dc9d15452a5473d`.

Recent native review images:

- `13_V2_Growth_And_Runtime_Leaves.png`: full material candidate without background obstruction.
- `14_V2_Growth_Reverse.png`: reverse geometry and planted-zone check.
- `15_V2_Clipped_Leaf_Attachments.png`: opaque clipped leaf roots meeting geometric rachises.
- `16_V2_Fixed_Game_Axis.png`: approximate fixed-axis native camera showing clear start and lower-row courts after canopy relocation. This is not a substitute for the actual Unity/UI capture.

These native audit results establish source preservation and structural/export validity, not final art approval. Blender materials remain broad original procedural surfaces. The actual Unity material, lighting, camera/UI, wind, alpha sorting and five-minute standalone checks are recorded separately in the delivered acceptance report, together with measured performance, comparison images and known engine-package diagnostics.
