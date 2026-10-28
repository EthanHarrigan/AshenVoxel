# Azgaar, procedural terrain and InfiniteDiffusion feasibility

October 27, 2026, America/Denver. Source review and design assessment only.
No new generation, gameplay implementation, Unity run or performance benchmark.
Inspected identities are in [the source manifest](WorldgenFeasibility/2026-10-27/source-sha256.json).

## Verdict

A convincing procedural terrain layer followed by selective learned refinement
is feasible within this project. It should improve the appearance of terrain
before decoded data arrives. Substantially cheaper long-distance rendering also
requires controlling mesh generation, resident geometry and replacement work.
Better input terrain alone does not reduce the cost of drawing the same meshes.

Interpret "80% as good" as a visual target: plausible mountain silhouettes,
valleys, drainage and biome transitions at the intended viewing distances. It
is neither a measured score nor an 80% match to unseen neural terrain, and it
does not imply 80% less inference. Quality must be judged in matched views.

## What is already present

- The current dataset is already Azgaar-conditioned InfiniteDiffusion, world ID
  `14fb32f26ff725abaf9f1e04668a1f46c5b4783a66f91d23253676f595f613e4`.
  Its extent is 299,520 x 149,760 meters. The complete learned macro has
  235 x 118 samples at 1,280 m spacing. The bundled decoded crop has 5 m
  spacing. This horizontally rescales the native 30 m model; it does not train
  a 5 m model or supply 10 cm source detail.
- [ExplorationAtlasField](../Assets/AshenVoxel/WorldScaleData/ExplorationAtlasField.cs)
  composes macro and fine height/climate fields. The optional live service adds
  bounded decoded patches through
  [ExplorationLiveRefinement](../Assets/AshenVoxel/Exploration/ExplorationLiveRefinement.cs),
  with 16 resident patches by default or 32 in the expanded option. Grounded
  publication and active matter have safety holds.
- [TerrainDetailField](../Assets/AshenVoxel/WorldScaleData/TerrainDetailField.cs)
  adds only 0.63 m maximum displacement at approximately 9.7, 2.9 and 0.83 m
  wavelengths. This supplies surface relief, not missing valley-scale structure.
  The separate `ProceduralMountainField` is an analytic research fallback;
  it does not reproduce the imported Azgaar geography.
- [WorldFields](../Assets/AshenVoxel/WorldScaleData/WorldFields.cs) already separates
  sampling and conservative bounds from rendering. Source snapshots, source
  identity and regional invalidation provide useful integration boundaries.
- [HeightProxySurface](../Assets/AshenVoxel/WorldScaleData/HeightProxySurface.cs)
  already supplies optional shared-vertex distant grids. The current
  [worker](../Assets/AshenVoxel/WorldScaleRuntime/WorldExplorerVoxels.cs) still
  constructs and uploads separate tile meshes. The proxy path also evaluates
  surface columns; it is not simply displaying free height samples.

The latest documented startup change earns a small complete distant plan before
full vegetation and then restores requested detail. Do not propose coarse-first
startup as entirely missing. Its final full-scene acceptance remains open in
[the loading evidence](TerrainRefinementEvidence/2026-10-27-loading-integration/README.md).

## Two different meanings of "diffusion on top"

**A procedural preview while the existing model runs:** comparatively direct.
Generate an intermediate field from the same map and macro anchors, then safely
publish canonical learned patches. It avoids waiting for inference to show
plausible terrain. The actual neural queries retain their inference cost, and
their results may change valleys, elevations and coasts. A convincing preview
does not guarantee a close match to the final geometry.

**A model that only supplies the missing detail while preserving that preview:**
a research task. The pinned pipeline accepts five coarse conditioning rasters;
the bridge requires their native 7,680 m lattice, corresponding to 1,280 world
meters here. Providing a high-resolution procedural map through that interface
would lose its intermediate detail when resampled. Conditioning strength is
not a control that directly trades visual quality for a proportional amount of
inference work.

Direct inspection of pinned `WorldPipeline` shows coarse -> latent -> decoder
stages. Its decoder residual is coupled to learned latents and a transformed
elevation representation; it is not an arbitrary meter-valued detail texture
that can be pasted onto any heightmap. The current GPU job uses `T=2`; the latent
path has two evaluations and the decoder uses one. There is no assumed long
denoising loop to cut by 80%. A procedural base does not automatically skip them.

Possible experiments include constrained postprocessing of complete learned
output, a bounded high-frequency residual, or a model adapter/fine-tuning for
the new input. Postprocessing can damage drainage or slope coherence and still
pays for generating the learned tile. None is implemented or quality-validated.
The upstream [conditioning documentation](https://github.com/xandergos/terrain-diffusion)
supports coarse input maps; the [project explanation](https://xandergos.github.io/terrain-diffusion/)
describes lazy generation, not a rendering or LOD system.

## Recommended first design

1. Reuse the existing learned macro and Azgaar biome/coast controls. The macro is
   already baked and cheap to read during Play. Replacing it with pure procedural
   generation mainly changes new-world preparation and model dependence; it
   offers little loading benefit for these already resident macro samples.
2. Add a versioned procedural intermediate field with regional ridge directions,
   valley/drainage constraints and biome-dependent relief. Evaluate only the
   spatial frequencies a requested source level needs. A finite map can bake
   regional drainage once; an infinite extension needs its own coherent boundary
   rules. Avoid independent per-chunk erosion and unconstrained stacked noise.
3. Keep the existing distant proxy path for the first comparison. Cache bounded
   intermediate samples and conservative extrema. Independently assess reusable
   grid/clipmap data updates to reduce repeated geometry work. Geometry clipmaps
   are an established pattern for nested grids and incremental refill; see
   [Hoppe's research](https://hhoppe.com/proj/geomclipmap/) and
   [the GPU implementation](https://developer.nvidia.com/gpugems/gpugems2/part-i-geometric-complexity/chapter-2-terrain-rendering-using-gpu-based-geometry).
4. Request learned detail for likely destinations and close or visually important
   regions with bounded queues. Freeze final nearby terrain before gameplay
   occupancy. Keep explicit pending status when inference cannot keep up.
5. Preserve local 10 cm authoritative matter, shared material/collision sampling
   and sparse edit ownership. Rendering distance must not cause a second terrain
   truth. Changes to procedural or learned composition require a new generator
   identity, affected cache invalidation and a migration decision for old worlds.

For a genuinely stable finishing pass, define one final field as a fixed base plus
a bounded detail contribution with shared world-coordinate ownership. Do not use
camera distance to change gameplay height. Preserve broad shape and drainage
anchors across levels; constrain source transitions, near/far edges and climate
adjustments. A smooth visual fade cannot justify moving collision or buried edits.

## Where performance would improve

| Change | Likely benefit | What it does not establish |
| --- | --- | --- |
| Procedural intermediate source | Better first appearance; fewer regions need immediate inference | Cheaper identical final meshes or exact neural fidelity |
| Selective learned refinement | Less total inference when fewer owner blocks are requested | Lower inference cost per identical owner |
| Cached intermediate samples | Less repeated terrain/material evaluation | Free storage, or lower GPU draw cost by itself |
| Reusable distant grids and controlled LOD density | Lower mesh rebuild/upload/residency cost; plausible path to longer views | Proven FPS or seamless cubic transitions |
| Disk caches | Faster repeat visits when identities hit | Lower active mesh residency or faster cold generation |

Historical native evidence already separates these costs. The source-residency
checkpoint recorded about 2.947 GB of far mesh payload, 514.7 MB of contact meshes
and 2,609 outstanding refinements despite working nearby source. See
[the dated record](CoordinatorEvidence/2026-10-27-content-release/source-residency-beta-1/installed-native-checkpoint.json).
These are prior observations, not fresh measurements of the inspected revision.
[Earlier profiling](WORLDGEN_PERFORMANCE_RESULTS.md) also found large face-sorting
and allocation costs, and retained a 2.31x tile-builder improvement. That is not
a 2.31x whole-game gain or a new result from this assessment.

Even the current grid proxy can be expensive at high counts: its source formula
allocates 829,656 bytes of vertex/index payload for a 128 x 128 quad tile and
3,231,960 bytes for a 256 x 256 tile, including skirts. This is static arithmetic,
not observed residency; source arrays, Unity copies, transitions and other scene
resources are additional. Improving the source cannot remove that payload.

## Smallest useful experiment and remaining gates

First compare current source against macro plus procedural intermediate detail
using the same renderer, camera route and budgets. Include the saved mountain
valley, a forest, coastline and a source boundary. Judge silhouette, drainage,
slope, material scale and transition motion. This tests whether "80%" is a useful
visual target without confounding it with a new renderer.

Then compare current proxy meshes with a bounded reusable-grid candidate on the
same frozen source. Record time to complete coverage, time to agreed visual
quality, p50/p95/p99 frame times, worker and upload time, process commit, mesh
payload, VRAM and retained queue counts. Include cold start, warm revisit, fast
flight, turn, teleport and source publication. Profile GPU inference alongside
rendering because both can compete for GPU time and memory.

Finally compare the procedural field with safely integrated learned detail.
Require repeatable seams, conservatively bounded peaks, correct depth/shadows/
normals, collision and population agreement, cancellation, source/edit invalidation
and immutable identity. Existing persistent terrain excavation remains separate
unfinished work. No universal speedup, achieved quality score, new render distance
or whole-scene FPS is established by this review.

Static review verification: all 19 inspected source/data/job files retained their
recorded hashes, and all four bundled height/climate archives match metadata.
See [verification](WorldgenFeasibility/2026-10-27/review-validation.json).
