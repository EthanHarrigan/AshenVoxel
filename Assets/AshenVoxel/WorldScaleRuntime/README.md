# World Explorer and generated surface materials

Open `Assets/Scenes/WorldExplorer.unity`, or use **Ashen Voxel → World → Open
world explorer**, then Play. ForestArena remains the granular/destruction demo
and TreeWorkshop remains the tree authoring scene. Native Unity import,
serialization, shader compilation, player builds and GPU performance remain
unverified in this container.

WASD flies, Q/E or Ctrl/Space moves vertically, RMB looks, Shift accelerates,
Home places the camera above terrain and R regenerates. P opens a separate
finite editable 10 cm region; P again discards its temporary edits and returns
to the vista. This is still a free-flight research scene, not a walking/collision
controller or seamless persistent voxel streaming.

## Source fields and scale

The default analytic mountain source is explicitly labelled and is not model
inference. Assign an AVWF `.bytes` TextAsset for a baked field. **Use Imported
Dimensions** retains physical extent/aspect by default. With it disabled, width
is editable and zero depth retains aspect; positive depth deliberately stretches
it. Horizontal size and elevation scale are independent.

The menus open the authored Ceteviles map and the actual baked neural samples.
Matching `*.climate.bytes` archives are assigned automatically, or cleared when
absent. Climate archives are checksum/grid/origin/extent/height-hash checked;
a different sample's climate is rejected rather than silently attached.
Without one, surface climate is explicitly derived from source biome and
height, not claimed to be measured or model-predicted climate.

## Snow, geology and ground layers

`SurfaceMaterialSampler` is shared by proxy generation and local matter.
Terrain attributes use fixed physical sampling scales, independent of rendered
triangle normals or current LOD. A surface column is evaluated once per proxy
vertex or inspection column and reused for its material layers. Proxy normals
follow neighboring generated heights; they do not choose the surface material.

The rules select coherent bedrock provinces and strata, exposed rock, coarse
slope debris, sandy shores, basaltic black sand, fine deposits, wet organic
cover and biome-colored grass. Rock types include basalt, granite, limestone,
sandstone and shale. The same material can retain coherent generated pigment
variation without changing hardness or behavior. Source biome controls surface
climate and grass palette: the vegetation Forest override does not convert
source desert or glacier ground into temperate forest.

Snow is a physical, quantized thickness above the bare source field. Coldness,
precipitation, slope retention, wind exposure, curvature, aspect and coherent
patches affect its depth and leave exposed rock. Proxy height includes that
same layer; local cells contain Snow rather than merely white rock. Conservative
LOD bounds include the maximum configured snow thickness. This is a static
terrain-attribute heuristic, not a conservative snow transport, avalanche,
meltwater or energy-balance simulation.

Inspector settings apply on R/Rebuild:

- Snow enable/depth, wind strength/direction and climatic sun direction control
  static retention/exposure. The climatic sun direction does not move the light.
- Snowline is a reference for **biome-derived fallback climate**. Actual model
  climate already supplies elevation-adjusted surface temperature; it is not
  cooled by the altitude lapse again. Temperature Bias affects both modes.
- Winter Fraction uses seasonal variability, interpreted in physical Celsius.
- Sea Level is shared by shore rules and the visual water mean plane.
- Inspection Tool Power defaults to 10 so the demonstration can excavate hard
  granite as well as softer sediments; material hardness remains distinct.

`GroundSurface` supplies generated vertex color, continuous proxy roughness and
pixel-filtered cell grain. Proxy UV0 carries roughness/wetness/snow/valid flags;
voxel chunks without that channel use the generic roughness fallback. Rebasing
updates a bounded world-coordinate phase for optional cosmetic grain. Nonzero
grain can visibly change when that phase wraps at4096 m; generated pigment is
unaffected and automatic runtime materials disable extra grain by default.
Generated pigment remains separate from lighting. This is procedural appearance,
not a claim of scanned texture assets or a completed material renderer.
An assigned terrain material remains an inspector template. Each explorer owns
and releases a separate mutable copy, so origin phases cannot modify the asset
or interfere with another explorer.

## Visual water

**Show Visual Water** adds one bounded, camera-centered sea grid with gentle
shader wave normals, bed-depth color hints and a slight underwater color
wash. The underwater test uses the immutable inspection bed while inside a
model-backed local patch; evicting the global model cache cannot change that
local water classification. Remote arrivals invalidate depth hints, and a
failed source rebuild clears previous water state.

This is fake water: no water material cells, buoyancy, flow, connectivity,
river/lake solve or hydraulic erosion. Depressions below the chosen sea plane
can look flooded even when disconnected from an ocean. Water can be expanded
later without confusing its current surface renderer with authoritative matter.

## LOD and local inspection

The planner uses three-dimensional distance, bounded selection and conservative
height queries. Mesh construction has an explicit per-frame tile count; old
complete coverage remains until a new plan is ready. Active plus desired cache
residency is at most two bounded plans. Bounds-aware skirts cover the tested
steep peak/coarse-edge gap; these are not watertight voxel transitions or
collision. Deep skirts can increase draw cost. Unity supplies ordinary mesh
frustum and depth culling; hierarchical GPU occlusion is not implemented here.

P materializes a separate 12.8 × 25.6 × 12.8 m region with actual 10 cm matter,
shared generated ground layers, snow and persistent per-cell tints. LMB digs.
Far proxies are disabled in this mode. The finite protected floor and vertical
limit are intentional test boundaries; steep slopes can clip the region.
Materialization yields after 128 columns, not a guaranteed millisecond budget.
There is no granular stepping or player collision in this flight scene.

The local region now adds source-biome plants and weathered rocks after terrain.
Generate Ground Cover, density and cell budget are Inspector controls. It uses
the same immutable sampler and32 m source halo, a1.7 m interior margin and
aggregate candidate/cell caps. The clutter phase yields before its bounded
synchronous build; it is not a measured millisecond frame budget. P exit discards
the generated plants with the temporary edits. Distant plant rendering/LOD and
persistent region composition remain separate work.

For every small specimen side by side, open `GroundCoverWorkshop.unity` through
the World menu. Tab selects, O frames all, G changes seed and R repeats it;
RMB/MMB/wheel inspect. See [ground-cover implementation](../../../Docs/GROUNDCOVER_UPDATE.md)
and its actual-data gallery for plant/rock methods and verification.

Default proxy spacing starts at 2 m, becoming coarser with distance/altitude.
That does not establish the requested full-resolution 100 m microvoxel ring.
Native 30 m model fields and much coarser authored maps also do not gain real
microtopography merely because a derivative stencil or mesh has finer spacing.
No sea-level-to-summit voxel columns are allocated across the world.

## Live model provider and climate

Enable **Use Remote Diffusion**, configure the local service and rebuild.
The `Tools/DiffusionBridge` provider owns the pinned model and immutable job.
Live physical scale belongs to that job; conflicting client rescaling and
recentered neural crop fallbacks are rejected. A centered whole-map rough field
must correspond to the service conditioning. Far rough terrain stays explicitly
provisional; bounded nearby model regions refine it asynchronously.

No HTTP or inference occurs inside sampling. Readiness requires the complete
32 m material-attribute halo. Local matter uses an immutable height-and-climate
snapshot; rough fallback is not accepted as neural material authority. The
scene allows two requests in flight, the client hard cap is four, and requests
are capped at 262,144 samples. Decoded height plus five climate channels and
categorical data have both a 64-region cap and a 128 MiB payload budget. Retained
snapshots and object metadata are additional memory, not hidden in that count.

Climate encoding is explicit. Legacy `upstream-native-v1` (including old absent
metadata) retains upstream channel-1 centi-C and is converted exactly once.
New `physical-si-v2` uses Celsius directly. Unknown or contradictory encodings,
nonfinite/unsupported physical values, invalid identity, excess size and CRC
failure are rejected. Temperature channel0 already includes upstream altitude
adjustment. All five climate planes survive cache eviction/disposal through
an authoritative snapshot.

## Executed checks

`python3 Tools/run_world_explorer_checks.py` compiles actual runtime sources
with test Unity/color/input/renderer substitutes. It covers mesh data and
roughness, snow-displaced geometry, actual snow cells and tints, LOD bounds,
32 m readiness, memory limits, climate encoding/units and transport failures.

Optional actual-data checks:

```sh
python3 Tools/run_world_explorer_checks.py \
  --baked-height Assets/AshenVoxel/WorldScaleRuntime/Samples/CetevilesNeural.bytes \
  --baked-climate Assets/AshenVoxel/WorldScaleRuntime/Samples/CetevilesNeural.climate.bytes
python3 Tools/run_world_explorer_checks.py --live-url http://127.0.0.1:8765
```

The live check consumes the known cached row608/column1120 native region and
checks physical seasonality as well as height. The baked check binds the actual
5 km Ceteviles learned field to its paired climate archive. Results and exact
source hashes are in `Docs/world-explorer-validation.json`. These are headless
C# integration tests with a JsonUtility substitute, not native Unity tests.
