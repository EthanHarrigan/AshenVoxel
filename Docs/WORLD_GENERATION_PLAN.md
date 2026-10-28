# Large-world terrain implementation plan

This work preserves the existing Generation5 forest arena and10 cm matter. It
adds a separate world-scale data/provider/explorer path. The user wants full
base InfiniteDiffusion terrain generation plus the relevant authored-map changes
from the Minecraft fork. Settlements, roads, buildings and unrelated mod systems
are excluded.

## Data and generation boundaries

```mermaid
flowchart LR
  A[Azgaar Full JSON / Pack Cells JSON] --> B[Physical map transform + source biomes]
  B --> C[Height and climate conditioning]
  C --> D[Original InfiniteDiffusion pipeline]
  D --> E[Bounded asynchronous native-region provider]
  B --> F[Explicit rough terrain preview]
  E --> G[Versioned height / climate cache]
  F --> H[Distance LOD terrain proxies]
  G --> H
  G --> I[Local 10 cm materialization + sparse edits]
  I --> J[Existing matter / meshing contracts]
```

Authored map scale, landmass zoom, elevation scale, sea level and voxel size are
independent controls. Global positions use double meters and signed64-bit cell
keys; Unity vertices remain local floats after subtracting a regional origin.
Unexcavated stone below the surface is implicit. Allocating every underground
cell between sea level and a6 km summit is explicitly rejected.

The model supplies large-scale height and climate, normally at30 m native
spacing for the selected Minecraft-related checkpoint. It does not generate
10 cm volumetric geology. Keep the original lazy overlapping generation and
conditioning controls behind a provider, rather than replacing it with noise
while retaining the name. A clearly labelled analytic terrain field is available
for deterministic tests and an immediately inspectable mountain scene.

## Delivery sequence

1. Identify and inspect the paper, upstream pipeline, modified-mod history and
   terrain-relevant fork changes. Record exact source/model revisions and units.
2. Implement validated Azgaar conversion and a checksummed float-meter height
   atlas interchange. Preserve original biome identity beneath a land-only
   Forest override. Reject ambiguous native.map and unitless GeoJSON inputs.
3. Implement engine-free global coordinates, independent map transforms,
   implicit matter/edit overlay and bounded balanced LOD selection. Verify
   mountain bounds, negative coordinates, corruption and capacity behavior.
4. Build a separate Unity WorldExplorer: long views, camera-relative geometry,
   height-aware refinement, bounded mesh work and complete-plan swaps. A separate
   finite10 cm inspection mode must not be presented as seamless world streaming.
5. Wire the full upstream terrain pipeline through an immutable-config region
   provider, with bounded request queues, deduplication, cache identity and
   explicit failure/readiness. Add asynchronous Unity tile prefetch; never run
   inference or synchronous network requests from per-vertex sampling.
6. Execute real small-window model inference where dependencies/hardware permit.
   Verify repeat/overlap behavior separately from adapter tests. Test Python/C#
   binary interchange and preserve raw source/configuration provenance.
7. Package the implementation and report actual checks, model measurements and
   remaining native Unity/performance gates. Do not equate standalone tests with
   a production-ready continuous editable world.

## Distance and visibility policy

100 m before voxel LOD is a target to measure, not a completed baseline. A100 m
radius footprint filled only20 m deep costs about2.93 GiB raw at5 bytes/cell;
full-height mountain columns are far worse. Fine matter residency, visible
terrain, tree representation, collision and active simulation need separate
budgets. Start from a small explicit voxel region and expand only after actual
profiling. Distant heightfield proxies suit the preference for large relief over
deep caves; undercuts and deep excavation require distinct near-field handling.

Conservative mountain bounds protect culling. Imported atlases need min/max
hierarchies so a narrow peak is not assumed absent merely because a few samples
miss it. Skirts cover prototype LOD cracks; they are not Transvoxel transitions
or proof of watertight hybrid voxel/heightfield seams. Long range also needs
depth precision, shadow ranges, vegetation LOD and atmosphere decisions.

Frustum, distance and occlusion culling can suppress rendering work. Sleeping,
distance-based simulation scheduling and unseen-cache generation are separate:
offscreen matter must not lose edits, mass or support events. A mountaintop can
expose nearly the whole surrounding landscape, so occlusion cannot replace LOD.

## Completion gates

The research/prototype can be reviewed when source-backed findings, executable
import/provider code, actual checks and a separate scene are delivered. Claiming
that only bug fixing remains for the full game requires more: native Unity
import/shader/play-mode testing, measured target-hardware streaming/GPU budgets,
continuous editable-region migration and persistence, hybrid seam tests, tree
LOD/streaming and long-running failure/recovery runs. Those are engineering
gates, not claims that a passing mocked test or an attractive still can satisfy.

Related evidence: [diffusion](INFINITE_DIFFUSION_RESEARCH.md),
[selected mod features](MOD_PARITY.md), [Azgaar](AZGAAR_IMPORT_RESEARCH.md),
[world scale and LOD](WORLD_SCALE_RESEARCH.md),
[recovery checkpoint](RECOVERY_WORLD_SCALE.md).
