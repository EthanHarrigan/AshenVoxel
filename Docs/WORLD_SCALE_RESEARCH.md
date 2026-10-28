# Large mountains, long views and microvoxel residency

Research date:26 October2026. This is an architecture investigation and a proposed
implementation sequence, not a claim that the existing finite arena already
streams an infinite world. The required matter grid remains10 cm. The user's
prior modified Minecraft diffusion conversation is unavailable; that missing
context must not be reconstructed from guesses. The separate diffusion and
Azgaar reports identify their integration requirements.

## Recommended direction

Use **one global terrain source with several derived representations**. Nearby
editable material remains voxel matter. Unmodified buried stone is implicit,
while edits and active near-surface material occupy resident bricks. Beyond the
fine region, height-based meshes represent the same landform with progressively
coarser geometry. This suits the priority on very tall mountains and observation
points, with modest underground depth, better than filling a giant3D array.

Keep the existing ForestArena and TreeWorkshop as regression scenes. Add a
separate WorldExplorer scene that demonstrates wide relief, global sampling,
camera-relative placement and bounded terrain LOD. A first explorer can be a
flight/inspection prototype with clearly stated local editing limits. It must
not be described as completed world streaming simply because its far mesh moves
around the camera.

The interfaces should accept analytic generation, imported Azgaar fields and
baked diffusion tiles interchangeably. Renderer and physics should consume the
canonical terrain source plus saved edits, rather than invoking a neural model
independently for each LOD. A lower LOD must not invent a different mountain.

## Current project limits that matter

`VoxelWorld` is a finite positive-coordinate domain whose axes are capped at4,096
cells. At10 cm that is409.6 m, so an8,000 m mountain cannot fit by changing only a
scene setting. The dictionary is sparse between16³ chunks, but allocated chunks
are dense inside. Calling `Set` from sea level to a tall peak allocates all that
stone. Existing snapshots are capped finite-world snapshots; they are not a
region database. Existing greedy mesh vertices are in finite-domain coordinates,
and the rendering guide currently expects an identity transform.

Introduce global signed64-bit cell/region addresses outside that finite core.
Use proper floor division for negative coordinates. Each loaded finite patch or
brick can still use small local integer indices and the existing material rules.
Do not lift the4,096-cell cap and assume that storage, snapshots, ray queries,
gravity, dirty queues and mesh transforms have become globally safe.

## Why height and volume must be separated

The following are **derived raw-storage estimates**, not measurements. A circular
100 m radius footprint has area31,415.9 m². At10 cm it contains about3.142 million
horizontal cell columns. Fully colored matter costs one material byte plus four
optional tint bytes per allocated cell, before chunk rounding and all metadata.

| Filled depth under that footprint | Cells | Raw material plus tint |
| --- | ---: | ---: |
| 2 m surface band | 62.8 million | 0.293 GiB |
| 10 m | 314.2 million | 1.463 GiB |
| 20 m | 628.3 million | 2.926 GiB |
| 8,000 m to sea level | 251.3 billion | 1,170.3 GiB, about1.143 TiB |

A completely filled100 m radius sphere is about4.189 billion cells and19.51 GiB
raw at5 bytes/cell. Even the top surface alone, triangulated as independent10 cm
height quads throughout that100 m circle, is about6.283 million triangles before
cliff sides, roots, trees or shading/color splits. Greedy merging can help smooth
areas, but the current exact-RGB merge rule can erase much of that benefit.

Therefore a100 m fine rendering radius is a quality target to test, not a safe
default memory allocation. Height itself should be a value, not an instruction
to materialize every cell beneath it.

The base matter query can answer air above the surface; thin organic/soil strata
near it; and stone below it without allocating any brick. Explicit edit bricks
override that base. Excavating deeper allocates only the changed/local active
region. A defined bottom/depth policy can retain stone below the play surface
without promising arbitrarily deep caves. Large exposed cliffs still need their
visible sides, so residency must be based on exposed3D surface and gameplay
envelopes rather than a horizontal slab around the camera's altitude alone.

Implicit matter is still authoritative matter. Converting a base region into an
editable brick must sample that exact base and preserve generator/model/map
identity; unloading an edited region must save the delta before eviction.

## Terrain LOD methods

| Method | Fit here | Important boundary |
| --- | --- | --- |
| Geometry clipmaps | Predictable nested height grids around the observer; incremental cache updates and bounded mesh counts | Height representation cannot display caves/overhangs on its own |
| CDLOD-style quadtree | Adapts terrain patches by3D distance; useful for mountains, where horizontal distance alone is misleading | Needs consistent morph/sampling rules and source-height hierarchy |
| Volumetric octree/brick LOD | Can retain arches, excavated cliffs and volumetric silhouettes farther away | Much more involved material reduction, occupancy, transition and edit contracts |
| Transvoxel | Seam construction for a smooth density/Marching-Cubes path at2:1 resolutions | Not a drop-in stitcher for the current discrete greedy cube faces |
| Height terrain with sparse voxel edit overlays | Efficient first large-world direction matching limited cave priorities | Far height proxies cannot encode every tunnel, overhang or detached body; promote such regions to3D proxies when they matter |

[Losasso and Hoppe's geometry clipmaps](https://hhoppe.com/proj/geomclipmap/)
cache viewer-centered nested regular grids and refill them incrementally. Their
[GPU implementation chapter](https://developer.nvidia.com/gpugems/gpugems2/part-i-geometric-complexity/chapter-2-terrain-rendering-using-gpu-based-geometry)
uses a height pyramid and discusses transitions and incremental updates. These
are foundational height-terrain techniques, not proof of a particular Unity FPS.

[Strugar's CDLOD paper and source](https://github.com/fstrugar/CDLOD) use a
quadtree of regular grids and a common3D-distance LOD function with continuous
transitions. Its primary benefit here is allocating detail by actual observer
distance, including vertical separation. A future implementation should be
labelled “CDLOD-inspired” unless it follows and verifies the full method.

[Lengyel's Transvoxel description](https://transvoxel.org/) addresses seams in
multiresolution volumetric meshes using transition cells between exactly2:1
sample spacing. Adopting it would require a deliberate smooth density-mesh
renderer and material contract; simply adding the lookup tables to the existing
block mesher would not provide correct seams.

### Proposed range policy

Begin with separate adjustable controls for material residency, simulation,
collision, fine geometry, vegetation and distant terrain. Do not overload one
“render distance” setting. Suggested experiments are fine radii of32,64 and100 m,
with increasingly coarse terrain extending20,50 and100 km. These are experiment
configurations, not measured performance tiers.

Choose terrain error in screen pixels as well as distance. For a1080-pixel-high
image with60° vertical FOV, a10 cm feature spans approximately2.92 pixels at32 m,
0.94 pixels at100 m and0.47 pixels at200 m when perpendicular to the view. This
explains why a100 m switch is plausible, while also showing that resolution/FOV,
slopes and silhouettes change the right answer. A large tree or cliff still
occupies many pixels after its individual voxels become subpixel.

Use a min/max height pyramid and an error metric that protects peaks and ridges;
an ordinary average mip chain can visibly shave a summit. Patch selection should
use the minimum3D distance to conservative bounds, not only the tile center or
XZ distance. Maintain a2:1 adjacency constraint where the stitch/morph scheme
requires it, hysteresis to avoid thrashing, and a resident parent fallback while
a refined child is unavailable. Camera teleports must retain coarse coverage
rather than briefly exposing an empty horizon.

In a prototype, height-mesh skirts can hide some cracks, but they are not a
complete watertight transition solution: skirts can become visible from cliffs,
cast incorrect shadows and hide disagreement instead of fixing it. Retain a
specific limitation until exact boundary coordinates, morphing and terrain/edit
handoff are validated from above, below and grazing angles.

LOD chooses a representation; culling decides whether to draw it. No amount of
occlusion culling prevents the mountaintop worst case in which most of the world
is visible. Coarse far geometry is essential even with excellent culling.

## Camera coordinates and depth

Store global positions in double meters or signed64-bit cell addresses; convert
to Unity float coordinates only after subtracting a nearby render origin.
Build distant tile vertices in tile-local coordinates and place each tile using
`float(tileGlobalOrigin - renderOrigin)`. Do not first cast a100 km global vertex
to float and subtract later: the lost precision cannot be recovered.

Derived IEEE754 float spacing is about0.49 mm at8 km,7.81 mm at100 km and6.25 cm
at1,000 km. Thus mountain height of several kilometers is not intrinsically
incompatible with10 cm geometry; large absolute world positions, many transforms,
shadows and physics are the more serious numerical issue. Rebase the active
region before those positions become large. Save coordinates, seeds, map lookup
and procedural pigment remain global and must not change when render origin
changes. Local collider/rigid-body/particle and interpolation histories need an
explicit rebase policy; moving only visible meshes is insufficient.

Use the URP/platform depth convention rather than inventing a separate Z mapping.
Unity documents reversed depth on DirectX, Metal and Vulkan, exposed through
`SystemInfo.usesReversedZBuffer` and `UNITY_REVERSED_Z`; see
[platform differences](https://docs.unity3d.com/6000.0/Documentation/Manual/SL-PlatformDifferences.html).
Floating-point reversed-Z greatly improves the near/far precision distribution,
as explained and reproduced in
[NVIDIA's depth-precision analysis](https://developer.nvidia.com/blog/?p=32034).
Actual target depth format, camera near plane and custom depth/shadow passes must
still be inspected. Raising farClipPlane alone neither builds terrain nor fixes
precision. A separate far camera/composite is a later option, but adds depth,
fog, transparency and shadow integration costs.

Keep detailed shadow distances independently bounded. Extending the fine shadow
cascades to100 km destroys their useful spatial resolution. Distant peaks can
use coarser terrain/horizon-shadow data or static base lighting with near dynamic
shadow overlays, with explicit invalidation around significant edits. Atmospheric
perspective can communicate scale, but fog must remain an art control rather
than hiding a shorter terrain range while claiming a100 km view.

## Visibility and activity culling

The order worth testing is: remove internal voxel faces; select terrain LOD;
frustum-cull patch AABBs; bound small-feature/vegetation draw distance; then test
conservative GPU or hierarchical occlusion for what remains. Tile bounds must
include real min/max elevation and tall trees; a flat or low bounds box can cull
the top of a mountain or redwood while it is visible.

Unity's traditional baked occlusion is poorly matched to arbitrary runtime
terrain destruction. Static occluder assumptions become stale after excavation.
Do not bake the original mountain and allow it to keep hiding a new passage.
The available
[URP GPU occlusion system](https://docs.unity.com/en-us/engine/6000.0/manual/analysis/graphics-performance-profiling/in-urp/gpu-culling)
uses current/previous depth conservatively with GPU Resident Drawer. It can add
cost when little is hidden and its bounding-sphere approximation is weak for
very tall or wide shapes. It requires checking the project's shader/object
compatibility and cannot be claimed operational by toggling a setting.

[GPU Resident Drawer setup](https://docs.unity3d.com/6000.0/Documentation/Manual/urp/gpu-resident-drawer.html)
documents Forward+, SRP Batcher, suitable compute-capable platforms and
MeshRenderer requirements. The project's custom voxel shader and unique mutable
chunk meshes need actual integration/profiling. A custom hierarchical-Z compute
culler is another research path, but previous-frame occlusion needs conservative
disocclusion handling during edits, fast movement and origin rebases. Never hide
a newly exposed crater behind stale depth indefinitely.

Vegetation should have its own hierarchy: detailed nearby trunks/leaves,
simplified whole-tree proxies, then canopy clusters/impostors. A75 m redwood
cannot be culled solely because its base or center is outside100 m; visible crown
bounds matter. Distance proxies must not become gameplay colliders or change the
saved material tree. Hero silhouettes can remain farther than grass detail.

**Rendering invisibility is not permission to discard simulation.** Stable grains
can sleep by their material/support rules. Inactive regions can be serialized
with a documented paused-time or coarse-update policy. A boss attack or structural
event outside the camera still needs authoritative handling if it affects the
player or saved world. Use distance/interest and causal envelopes for simulation,
not just the camera frustum. A camera turn must not create or delete matter.

## Concrete prototype contract

1. Create a global surface-source interface sampled in double meters, with stable
   source ID/version, seed, sea level, horizontal world scale, height range and
   source biome plus effective biome. Forest override changes effective biome
   while retaining imported source metadata. Sampling outside a finite imported
   map has an explicit ocean/clamp/wrap policy; wrapping is never implicit.
2. Add signed global addressing, local-origin conversions and an engine-free LOD
   selector producing bounded tile descriptors. Selection and data residency are
   separate. Cache keys include source identity, region, resolution and edits.
3. Add a separate WorldExplorer scene with a deterministic mountain source and
   height-based far meshes from those descriptors. Show actual height, view
   distance, active tiles, pending builds, triangles and origin in the HUD.
   Use a fixed mesh/build budget, nearest useful work first, cancellation/revision
   checks and parent fallback. Demonstrate4–8 km height variation and20 km-plus
   views without allocating buried columns.
4. Connect a deliberately bounded10 cm local material patch to the same surface
   source, translated relative to its global origin. Retain core `Set` mutation,
   material strata and authoritative grid targeting. Label the local residency
   boundary clearly. A far visual mesh is not proof of infinite voxel simulation.
5. Import normalized Azgaar height/biome fields and expose horizontal landmass
   scale separately from elevation gain. Rebuild/cancel caches when those change;
   do not reinterpret already saved edits under a new map scale silently.
6. Add region edit storage, implicit stone promotion and far-proxy invalidation
   before claiming streamed destructible terrain. Save material/color changes
   before eviction and restore the exact edited region on return.
7. Add diffusion-generated canonical tiles behind the same source interface only
   after exact model/version/seed/conditioning and neighbor consistency are
   specified. Inference runs ahead of residency, never in an unpredictable
   synchronous frame loop. Preserve failed/pending tile states and fallback.

This first implementation isolates the difficult large-coordinate/LOD/source
boundaries while preserving the already tested finite demo. Fully integrated
world persistence, neural generation, seamless voxel/far transitions, wind,
large-scale structural physics and production GPU budgets are substantial work,
not merely final bug fixes.

## Verification gates

- Exact global addressing and floor-division cases across zero; high-coordinate
  sampling agrees before/after multiple origin rebases.
- Same world/source/seed yields the same canonical height at tile edges and all
  representation levels; the LOD mesh does not reseed terrain.
- Terrain selection has bounded tile/work counts; negative coordinates,
  teleports, extreme height ranges and repeated movement do not leak caches.
- Adjacent selected tiles cover the domain without holes or overlapping
  authoritative ownership; required2:1 constraints and shared boundaries hold.
- Peak/valley silhouettes and cliff views remain recognizable through LOD
  changes; native Unity screenshots and motion captures verify the transition.
- Edits crossing local residency, height-proxy and save boundaries survive unload
  and return; old asynchronous results cannot overwrite a newer edit revision.
- Profile valley forests and exposed mountaintops separately. Report CPU/GPU,
  memory, mesh upload, draw calls, shadow work and p99 frame time on named
  hardware. A valley occlusion win is not evidence for a100 km mountaintop.

None of these Unity/runtime gates has been run by this research pass. The
existing standalone core tests remain separate evidence for the finite core.
