# InfiniteDiffusion exploration data and optional local model

For the separate 320 m baked overview experiment, see
[method, result and validation](../../Docs/Coarse320Evidence/2026-10-27/README.md).
`bake_coarse_overview.py` runs the learned coarse/base stages without the fine
decoder and exports a resumable source atlas outside Assets. It does not change
the bundled world described below or generate render LODs.

The downloadable Unity project includes a complete actual-neural macro world
and a decoded spawn region. **Python, model weights and a running service are
not required to launch that bundled world.** The full map remains explorable
using its learned coarse stage when decoded fine data is unavailable.

## Exact scale and physical units

The transferred Minecraft handoff states that scale5 with a native authored
width around1800 km produced299520×149760 blocks. Its coarse map is234×117
cells, with256 native samples per cell. This prototype uses the same horizontal
mapping: one native30 m model sample occupies5 Unity meters. Therefore a native
7680 m coarse cell occupies1280 Unity meters.

The shipped extent is exactly **299520×149760 m** (roughly300×150 km), centered
at zero. Model height remains real meters, independent of horizontal scale.
Minecraft's positive-height compression and negative sea-level offset are not
applied: they would conflict with the requested multi-kilometer Unity mountains.
The authoritative material cell remains0.1 m. Sampling the height field more
finely does not create measured sub5 m or sub1280 m model relief.

## What actually ran

- Original source commit `e8dcb4b1a834ab2f6b1a6f5256ed7c9f2f3e8230`.
- Original30 m model revision `9ef8030cb805b433b98ec25c5dddefbac07a9e26`.
- Source/model/statistics checksums validated by the existing bridge.
- Authored input: bundled `Ceteviles_Source.json`, exported Full Azgaar JSON.
- Complete learned **coarse network** output:235×118 height/climate nodes across
  the exact world extent, stored at1280 m node spacing.
- Original full latent+decoder pipeline:1024² canonical pixels around spawn,
  displayed at5 m spacing, a5115×5115 m region. Sixteen fixed256² owners retain
  their original native sampling/halo contract and checksummed cache.
- Static512 m fade from decoded fine to the learned macro, based only on world
  position. Camera motion never changes this blend.

The fine execution took644.33 s on two CPU threads in this container. A later
78.26 s original-coarse-network rerun corrected a real half-cell registration
error found by independent review. The precise feature coordinate is
`(native_pixel + 0.5) / 256 - 0.5`; attaching coarse cells to nativepixel/256
would misregister them by637.5 world meters at scale5. Categorical imported
biome cells retain their distinct half-open cell origin and are not shifted.
The decoded fine outputs and final spawn did not change during that correction.

The optional full-world provider also needs an explicit physical publication
policy: original full-pipeline climate can contain negative seasonality,
precipitation or precipitation CV. `physical-nonnegative-v1` changes only those
negative values to zero **before canonical owner encoding**. Heights, signed
temperature, lapse rate, all nonnegative values, categories and original model
source remain unchanged. Nonfinite predictions and upper-bound violations are
still rejected. Raw/published climate hashes and exact correction counts are
recorded in a bounded provenance summary and exposed in service status.

This is a versioned derived view of the original model, not retraining or a
new native model identity. Its policy is part of the view hash and backend
identity. Rectified owners use a separate `owners/physical-nonnegative-v1/`
cache namespace. Valid historical owners migrate with identical array bytes;
the historical cache is preserved. Bundled macro/fine values need zero
additional policy corrections, so their geometry and climate values did not
change when their derived policy identity was published.

A targeted actual cold dry-land owner contained36,419 negative precipitation
predictions in its320² input query. The service published physically valid
zero precipitation and remained ready; original terrain height stayed under
the unchanged model path. That first distant owner took212.83 s on two CPU
threads here. This validates correctness, **not real-time CPU fine streaming**.
The bundled region is instant data; optional expansion throughput must be
measured on the target machine. The report distinguishes pre-policy physical
hashes and the exact executed source from later status-lock bookkeeping.

Physical climate archives normalize upstream centi-C seasonality exactly once.
The coarse model can emit physically negative precipitation/CV; the published
coarse climate applies explicitly recorded nonnegative rectification. The bundled fine-pipeline physical values already satisfy this policy and
remain bit exact to their executed native inputs. Coarse temperature uses
upstream's local sea-level/lapse estimator, and field blending adjusts each
layer only for the additional change to final elevation.

Current files are under `Assets/AshenVoxel/Exploration/Samples`. The default
spawn is inside fully decoded warm seasonal forest: X85760/Z15330 m, ground
135.905899 m, temperature24.386 C, source biome5 and0.179-degree local slope.
Metadata supplies `recommended_spawn` with eye/body start Y137.905899 m.

## Portable source field

`ExplorationAtlasField` reads paired `HeightAtlas` / `ClimateAtlas` archives
and an optional `ExplorationBiomeAtlas` cell raster. It honors each fine
atlas's actual global origin; decoded crops are not silently recentered.
Height/climate pairing uses exact height SHA256, grid, origin and extent.
Macro/fine/biome view identities must match. The biome identity also contains
a SHA256 of its category payload, preventing material/forest cache reuse after
same-grid category edits. Queries perform no HTTP, model inference or I/O.

Constructor `worldWidthMeters` / `worldDepthMeters` deliberately resize the
client map; `originalWidthMeters` / `originalDepthMeters` identify the bake's
original centered domain. Pass the imported macro extents for original values.
VerticalScale adjusts height independently and applies climate lapse only for
that additional elevation. `QueryBounds` returns conservative unions plus a
small floating-point rounding margin. `FineWeightAt` and
`ModelResolutionMeters` make the current source stage observable.

## Optional Windows local generation

Stop Play before regenerating data. Double-click **Start-InfiniteDiffusion.cmd**
from this folder. Optional live generation requires **Python3.12 and Git**;
the script uses a project-local virtual environment and `.terrain-cache`.
It installs inference dependencies, checks out the pinned original source,
downloads verified weights/statistics, prepares the Azgaar conditioning and a
runtime-specific immutable job, rebakes matching Unity samples, and starts a
localhost5 m-coordinate service on port8787. Existing downloaded samples are
backed up once under `.terrain-cache/original-bundled-samples`.

Refresh Unity after a bake, then Play again to load the matching files. A
Windows/GPU runtime can have a different world identity from the downloaded
Linux/CPU bake. **The client must not merge those identities.** Rebuilding the
local macro/fine assets before enabling that local live world is intentional.
The server never silently treats a native30 m model as a native5 m checkpoint.
`serve_exploration.py` wraps the original canonical provider, leaves its pixel
indices and arrays untouched, and explicitly transforms spacing/origins into a
separately hashed coordinate view. Climate and height meters are not scaled.

Parameters are exposed on the PowerShell script, for example:

```powershell
.\Start-InfiniteDiffusion.ps1 -AzgaarJson "D:\Maps\World Full.json" `
  -WorldWidthMeters 299520 -WorldDepthMeters 149760 `
  -ModelMetersPerSample 5 -Device cpu -Threads 4 -FineSide 1024
```

Use a platform-matched CUDA-enabled Torch/Torchvision installation before
requesting `-Device cuda`; this Linux authoring run does not verify Windows
package installation or GPU throughput. `-SkipBake` is an advanced service-only
option and cannot make an old/different dataset match a new runtime identity.
Map inputs must be Azgaar **Full JSON** with original grid/pack geometry and
climate. Native `.map` files must first be exported from Azgaar.

## Reproduce and verify

`bake_exploration.py` takes a prepared native job and writes actual macro/fine
outputs. `finalize_samples.py` publishes the explicit world-coordinate view,
paired Unity bytes, biome cells, metadata and stable `.meta` GUIDs. Extent and
world spacing are CLI controls; original model spacing remains30 m.

```sh
python Tools/ExplorationBake/bake_exploration.py --job world-job.json \
  --output .terrain-cache/exploration-bake --fine-side 1024 --allow-download
python Tools/ExplorationBake/finalize_samples.py \
  --input .terrain-cache/exploration-bake \
  --output Assets/AshenVoxel/Exploration/Samples --source-map "World Full.json"
python Tools/run_exploration_field_checks.py --samples Assets/AshenVoxel/Exploration/Samples
python Tools/ExplorationBake/check_provider.py --job world-job.json
python Tools/ExplorationBake/check_physical_policy.py --job world-job.json
python Tools/ExplorationBake/check_coarse_registration.py --upstream-source /path/to/pinned/terrain-diffusion
```

The field suite passes28 checks, including actual baked files, off-center crop
registration,5000 conservative-bound samples, climate/vertical transforms and
category identity. Coordinate-view tests pass14 checks and negative-climate/cache regression tests
pass18 checks using explicitly fake backends; the separate actual localhost probe is bit-identical to the
bundled decoded height/climate at spawn. `check_live_view.py` reproduces that
actual HTTP comparison with six checks against the running optional service. The registration test calls the pinned
upstream climate method with a test ramp; it is not terrain-generation evidence.
Executed model reports, historical executed-source snapshot, corrected macro
proof and live-view probe live in `Docs/ExplorationEvidence`.

This implements and verifies data/provider behavior. Native Unity import,
shader compilation, player movement, Windows setup and GPU/player performance
are separate tests and are not established by these headless checks.
