# World-generation and LOD processing research

Follow-up (2026-10-27): [profiling and experiments](WORLDGEN_PERFORMANCE_RESULTS.md)
found enum boxing/side sorting dominates more than this proposal assumed.
Unboxed sorting and two-worker sampling are now retained; native candidate tile
generation2.31x faster. GPU quads were tested but remain a prototype because draw
timings are mixed and full integration is unverified. The original proposal and
its conditional estimates below are preserved, not measured outcomes.

Research/source inspection: 2026-10-26, America/Denver. This is a proposal report,
not an implementation or a new Unity performance test. No Editor, inference job,
world asset or gameplay setting was changed. Read MEMORY.md first for subsequent
concurrent changes. Sources were inspected directly, rather than inferred from
older roadmap descriptions.

## Recommendation

The strongest first experiment is **compact GPU quad rendering plus persistent,
incrementally updated LOD data**, retaining the current 10 cm authority. Replace
the repeated face-to-CPU-vertex expansion/upload operation, then stop treating a
camera-dependent mesh plan as the only reusable terrain representation.

Start with existing face generation and ordinary URP lighting semantics. Upload
one compact record per rectangle; reconstruct its corners in the vertex shader
(vertex pulling). Next, cache source columns/material attributes independently
of the selected render LOD, and update only newly exposed or invalidated regions.
A GPU column/clipmap path is a subsequent experiment, rather than a prerequisite
for the first comparison. A full sparse-voxel ray caster has higher integration
risk and should compete against that baseline before adoption.

There is a credible route to several-fold shorter terrain refinement, but no
defensible measured whole-game speedup yet. A **2–5x total terrain-refinement
target** is a useful experimental goal only if profiling shows representation,
expansion, upload and repeated work dominate. It is not an expected result with
a statistical confidence interval, and does not apply to learned-model baking.

## What the code actually does now

Entry documents read: MEMORY.md, TODO.md, AI_HANDOFF.md, README.md, DECISIONS.md
and FILE_MAP.md; current voxel design/evidence, quality-patch notes, generation
setup and relevant source were then inspected.

1. **Learned source preparation** runs outside Unity through the original model.
   The present metadata world ID starts `14fb32f2`; older handoff ID `779b530a`
   describes an earlier source. Current metadata retains 299,520 x 149,760 m,
   1280 m macro spacing and 5 m decoded spacing. Source sampling, render spacing
   and 10 cm matter are separate. LOD changes do not accelerate model inference.
2. **Runtime terrain** uses 25.6 m dyadic tiles and implicit surface columns.
   `VoxelLodSurface.Build` directly creates exposed tops/risers. It already avoids
   dense mountain interiors, already omits internal faces and already merges
   compatible rectangles. Simply recommending those techniques adds nothing.
3. **Contact terrain** now has a separate 100 m target stream and default 1 GiB
   mesh admission. Completed contact tiles publish immediately and mask the
   underlying far terrain. It shares the one geometry worker/current build with
   distant work, and contact requests take priority. Publication is independent;
   processing capacity is shared.
4. **Geometry production** runs one captured-source Task at a time. For each
   requested tile, the worker evaluates columns, samples materials, emits faces
   and performs two list-sorting side-merge passes. A 256-column tile samples a
   258 x 258 halo grid. `EvaluateColumn` has an upper contract of 15 height queries;
   a full tile can therefore approach one million height queries before side
   material evaluation. The current procedural relief adds noise work to those
   source queries. This is a call-count bound, not a timing profile.
5. **Main-thread processing** expands each returned rectangle to four 24-byte
   vertices and six 32-bit indices, yielding every 128 faces. It then creates and
   uploads one complete Mesh. The configured 2 ms cooperative budget cannot
   interrupt an individual native upload. Bootstrap permits one upload per frame.
6. **Far coverage** still waits for all desired keys before switching topology.
   The contact fix does not remove that far-plan activation barrier. Existing
   active keys can update earlier. Source revision also causes contact replanning
   broadly, although far invalidation checks regional intersections.
7. **Appearance constrains merging.** Tops must agree on elevation, exact tint,
   material, wetness and AO compatibility. Moving pigment into a separately
   sampled attribute cache could merge more geometry without discarding RGB,
   but different heights still cannot merge into a single flat rectangle.

Key sources: [voxel worker/upload](../Assets/AshenVoxel/WorldScaleRuntime/WorldExplorerVoxels.cs),
[mesher](../Assets/AshenVoxel/WorldScaleData/VoxelLodSurface.cs),
[frame scheduler](../Assets/AshenVoxel/WorldScaleRuntime/WorldExplorerIntegration.cs),
[plan activation](../Assets/AshenVoxel/WorldScaleRuntime/WorldExplorer.cs),
[surface sampler](../Assets/AshenVoxel/WorldScaleData/SurfaceMaterialSampler.cs),
[relief field](../Assets/AshenVoxel/WorldScaleData/TerrainDetailField.cs).

### Documentation gaps that affect this question

The whole-world voxel guide still says default 32 m contact detail; code/settings
now request 100 m. Its statement about missing byte admission needs qualification:
contact has admission, while the whole terrain residency still lacks a unified
byte policy. Older intro/handoff tables describe smooth near proxies and absent
native validation; the latest overlays supersede them. FILE_MAP's near-page
implementation is historical when whole-view voxel terrain is enabled.

Native evidence is real but revision-specific. The earlier full-scene DX12 record
reports 38.54–48.04 million resident triangles, 2.31–2.88 GB vertex/index payload,
and a worst observed build/upload slice of 79.2 ms. Its last-worker samples near
3.3–3.6 ms are not a worker distribution. Its elapsed timestamps are not clean
per-stage generation benchmarks. The latest memory/session note reports 96
contact tiles, about 935.5 MiB, and a large remaining far queue. Those observations
cannot be silently combined into a benchmark of the current imported world.
See [native record](WorldVoxelEvidence/native-final/validation.json) and
[current design](WORLD_VOXEL_LODS.md).

## Candidate processing changes

### 1. Compact quad records and GPU corner reconstruction — first choice

Keep the current terrain shape/material decisions, but send rectangles instead
of four expanded vertices and six indices. Store tile-relative position, two
extents, axis/sign, tint, four AO values and surface data in a proposed 16–32-byte
record, with tile scale/origin in separate metadata. Confirm the actual layout
against tall aprons, range and exact-color requirements before claiming 16 bytes.

The [binary greedy meshing reference](https://github.com/cgerikj/binary-greedy-meshing)
demonstrates vertex pulling, an 8-byte restricted-format quad and author-reported
50–200 microsecond chunk meshing. Its small coordinate/material encoding is not
a drop-in format for this project's richer attributes. Those timings omit our
learned/surface sampling and are not an AshenVoxel forecast.

**Quantifiable gain:** current quad payload is 4 x 24 + 6 x 4 = 120 bytes.
At 16–32 bytes, identical faces require **3.75–7.5x less geometry payload**.
Applied only to the historical 2.31–2.88 GB payload, that is approximately
0.31–0.77 GB before metadata, attributes and allocator slack. This comparison
does not include all application memory or assume a triangle-count reduction.

**Downsides:** custom URP forward, depth, normals and shadow support; vertex-ID
decoding work; indirect-buffer lifetime/culling complexity; GPU captures and
precision testing. Keep a spatial draw hierarchy rather than one world-sized
draw that defeats culling. This removes CPU expansion and reduces transfer, but
does not by itself accelerate column classification or reduce rasterized faces.

### 2. Persistent column hierarchy / clipmaps — strongest complementary change

Keep a reusable height/material hierarchy independent of camera heading and
render mesh choice. Retain coarse parents until individual children are ready;
use regional invalidation, cached sampling variants and bounded byte residency.
On motion, fill newly exposed strips/tiles rather than regenerate an equivalent
representation. Current code already reuses matching tiles, so reuse is an
extension of existing caching, not a newly discovered missing feature.

[Geometry clipmaps](https://hhoppe.com/proj/geomclipmap/) and
[GPU-based geometry clipmaps](https://developer.nvidia.com/gpugems/gpugems2/part-i-geometric-complexity/chapter-2-terrain-rendering-using-gpu-based-geometry)
provide the primary design reference: nested grids, incremental movement and
mostly GPU updates. For AshenVoxel, reuse the data-update principle while
retaining quantized tops and vertical risers; a classic displaced triangle grid
alone would smooth away the cubic silhouette.

**Quantifiable gain:** a 256 x 256 window moved one sample in both axes needs
511 new samples versus 65,536 for a complete refill: **128x fewer updated samples
in that ideal operation** (256x for one-axis motion). This is not a speedup over
our entire existing tile streamer, which already keeps overlapping tiles. A
teleport/cold start loses this advantage. The actual benefit depends on current
cache misses, turn-induced rebuilds, tile size and invalidation frequency.

**Downsides:** seams, filtering and LOD migration become explicit engineering.
Using averaged heights alone can erase narrow ridges; retain extrema/error data
and test cliffs. Coarse data must not move collision. A height-column cache still
cannot describe general caves; future edits require local 3D bricks/overrides.
Extra cached levels consume memory, and unrestricted precomputation is infeasible.

### 3. GPU column sampling / face emission — potentially large, stage-specific

Upload bounded source atlases and evaluate render columns/visibility in compute,
then compact faces into an indirect draw buffer. Persistent data and quad pulling
avoid generating a huge CPU mesh after the GPU has done the work. Port relief
and appearance selectively; keep CPU collision and Core authoritative.

**Planning scenario:** test a **5–20x reduction in the migrated parallel stage**,
not total loading time. This range is an engineering hypothesis to justify a
prototype, not a result from a matched paper or this PC. The GPU competes with
forest rendering/shadows, so the actual result can be worse than CPU processing.

**Downsides:** float/double and hash differences can change cell occupancy at a
10 cm threshold. CPU/GPU parity or CPU-authored quantized height input is required
near gameplay. Avoid GPU readback in movement; reserve GPU work/buffer capacity,
retain overflow queues, and test cancellation/rebasing/depth/shadows. Compute
alone does not fix bad LOD selection, cache churn or a one-tile upload gate.

### 4. Jobs/Burst and direct MeshData — useful bridge, smaller architectural step

Flatten managed interfaces and lists into immutable native arrays. Parallelize
column rows or a few bounded tiles, then write final mesh buffers in workers
through [Unity's writable MeshData API](https://docs.unity3d.com/6000.3/Documentation/ScriptReference/Mesh.AllocateWritableMeshData.html).
Unity documents thread access and lower overhead from batched allocations.
The inspected package manifest does not currently pin Burst/Collections; adding
them requires compatible pinned packages and validation.

**Planning scenario:** 2–6x faster CPU production is a prototype target if it
vectorizes and has spare cores; memory bandwidth, sorting and main-thread apply
can dominate. It is not a measured Burst multiplier for this code. The single
worker's final timing sample is insufficient to predict the gain.

**Downsides:** a substantial data-layout rewrite, more in-flight memory, immutable
snapshot/lifetime management and continued expanded mesh bandwidth. Raising
thread count alone could amplify the project's known memory pressure.

### 5. Sparse voxel ray casting — largest renderer change, second experiment

Store surface bricks/occupancy and trace rays through a hierarchy instead of
extracting terrain meshes. Learn traversal from
[Laine and Karras](https://research.nvidia.com/publication/2010-02_efficient-sparse-voxel-octrees).
[Aokana](https://arxiv.org/html/2505.02017v1) is particularly relevant: Unity 6,
streamed LODs and GPU rendering. It reports up to 4.8x faster rendering and 9x
lower memory than its tested predecessors, with roughly 6 ms rendering at 64K
in its experiments. These compare other voxel structures, not AshenVoxel URP
meshes, and do not measure procedural world generation. Its LOD/color filtering
and transparent-voxel limitations matter here.

The developers' [Teardown conference talk on YouTube](https://www.youtube.com/watch?v=IM1Dr98f3xU)
and [official conference entry](https://graphicsprogrammingconference.com/archive/2025/)
are production references for voxel ray tracing and renderer tradeoffs. The
indexed description/metadata were accessible; no full audiovisual viewing or
transcript-based technical audit was performed. Teardown is evidence that this
class of rendering can ship, not evidence of a 300 x 150 km generation speedup.

**Potential:** mesh extraction/upload stages disappear, but are replaced by brick
production, acceleration updates, streaming and per-pixel traversal. No honest
whole-project multiplier can be assigned before a matched implementation.

**Downsides:** custom depth/normals, raster integration, shadows and temporal
stability; traversal and lighting cost scale with pixels/rays and scene shape.
High-entropy pigment weakens compression. Static DAG deduplication complicates
frequent edits; local mutable bricks are a safer starting point for destruction.
Hardware RT is an option, not a requirement or an automatic speed improvement.

### 6. Offline hierarchy/cache — faster repeat visits, displaced cost

Persist bounded source-column or compact-quad LOD caches with world/profile/
sampler/renderer versions in their keys. A revisit can skip most classification
and meshing, leaving disk/decompression/upload. This does not accelerate a cold
first-time bake; it moves work to preparation and trades disk space for latency.
[Distant Horizons](https://gitlab.com/distant-horizons-team/distant-horizons)
is a useful shipped-game-mod comparison for rendering simplified distant chunks.
It does not supply a transferable timing estimate.

Do not bake every 10 cm column across the whole map: the authored area contains
about 4.486 trillion horizontal cells. Even 16 bytes per column is about 71.8 TB
before hierarchy, compression or caves. Bake coarse levels broadly, local detail
on demand, and retain immutable cache identity/corruption checks.

### Options that do not address the present bottleneck well

- Binary occupancy masks help arbitrary 3D chunk face discovery, but this terrain
  already uses exposed columns. Material sampling, side segmentation and upload
  remain. Replacing the whole path with dense 3D occupancy could add work.
- [Transvoxel](https://transvoxel.org/) solves resolution-boundary cracks for
  volumetric isosurfaces. It is relevant to future smooth caves, not an automatic
  speed improvement for current cubic top/side geometry.
- Smaller contact radius or looser target pixels reduce work at a visible quality
  cost. Halving samples per axis gives approximately 4x fewer columns; that is
  arithmetic, not a new processing method. Keep this as a comparison, not the
  main response to a request for preserved-quality speed.
- Upscaling/frame generation changes display cost; it does not accelerate source
  decoding, column generation or the worker queue.

## Reddit findings and evidence boundaries

The [author discussion on ray-cast heightmap LODs](https://www.reddit.com/r/VoxelGameDev/comments/1kae205/ray_traced_256k2_heightmap_terrain_powered_by_lod/)
reports surface-only sparse storage and a small memory footprint, but explicitly
lacks streaming in that demo. That is a useful warning when evaluating impressive
stationary views. Its hardware/FPS claims are uncontrolled observations.

The [LOD-generation discussion](https://www.reddit.com/r/VoxelGameDev/comments/1kl7qkt/finally_got_lod_and_large_distance_generation/)
separates relatively cheap generation from more expensive mesh/draw preparation
and describes jarring material transitions across LODs. It supports testing those
failure modes, not adopting its reported speedups as predictions here. Both are
first-person developer reports; research/official code carry the technical case.

## Interpreting speedup numbers

Do not multiply the candidate ranges together. They overlap and can move the
bottleneck. If a changed stage is fraction f of total time and becomes s times
faster, total speedup is 1 / ((1-f) + f/s).

| Fraction actually accelerated | Stage acceleration | Whole-operation gain |
| --- | --- | --- |
| 50% | 10x | 1.82x |
| 80% | 5x | 2.78x |
| 90% | 10x | 5.26x |

For example, a genuinely comparable 120-second refinement could fall to about
43 seconds in the middle scenario, or 23 seconds in the last. Those are examples,
not predictions from the historical native elapsed timestamps. A new renderer
can make first useful local detail much earlier without emptying the far queue.

At one upload per rendered frame, 512 completed uploads alone require at least
512 frames: 8.53 seconds at 60 FPS or 17.07 seconds at 30 FPS, even with free
generation. This excludes contact, startup/refinement replacements, sampling and
all other costs. A producer/consumer pipeline and bounded upload bytes are
therefore part of the redesign, not merely more worker threads.

## Concrete comparison to run before adoption

Use the actual current imported world, fixed settings/camera resolution, one
Editor/player, and the same spawn/turn/flight/edit-invalidation sequence. Preserve
the old renderer switch. Record source hashes/world ID for each run.

Measure first visible world, collision-ready local area, full admitted 10 cm
radius and far coverage separately. Time planning, column sampling, material
classification, face emission/merge, CPU expansion, upload and GPU frame work.
Record p50/p95/p99, peak commit/VRAM, in-flight bytes, retries, admission-deferred
requests and discarded work. Separate cold cache, warm cache and teleport cases.
Budget-limited contact requests mean queue-empty is not always attainable.

Compare (A) current path, (B) same faces with compact quad pulling, (C) B plus
persistent regional caches/progressive parent-child publication, (D) GPU column
production if CPU sampling still dominates. Test ray casting as a separate
competitor rather than coupling all changes into one unverifiable rewrite.
Require matching cubic silhouettes, pigments, depth, shadows, AO, collision,
source changes and rebasing. Existing headless checks and actual Unity mesh/
visual acceptance remain required for implementation, not for this report.

## Inspected source SHA256 identities

These identify this inspection; they are not native test coverage of these files.

| Source | SHA256 |
| --- | --- |
| WorldExplorerVoxels.cs | `101a140abf8a18f292cb1c53ff9f43e398c81f3e047a63efb46fa7f2d3bf61d7` |
| WorldExplorerIntegration.cs | `2a22ed80d8219ae473fe9cd173559542a595ff7376ef1c71d2e3c7d477314ab5` |
| WorldExplorer.cs | `1fc28e882e6cf69db38c5f128094882705504de907a6413137c3e9714ed7cb36` |
| VoxelLodSurface.cs | `7594f012e2a491f2e1f7869b0a7186f4cb33dc3db38ac48eb1a795939dc6b672` |
| SurfaceMaterialSampler.cs | `4e873a75acd0f67f22a2b85e2caf407d6851cc3fd870441947fee5a9629bed90` |
| TerrainDetailField.cs | `8393d97dd95a2d4deffda3db93d8b077cfa7223bb98f1e2c55b97f8242d17ac1b` |
| ExplorationSettings.asset | `d731e54ffa865834591c55ce49c54268f4b44d224666fa4d9eb615db0bd633cf` |
