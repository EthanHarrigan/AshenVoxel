# First world exploration prototype

Local update: sticks/soft foliage now slow walking, and far tree imagery plus
an approximate stand horizon extend forest visibility. The new settings,
native Windows captures, verification and explicit limits are in
[the current LOD review](NativeLODReview/README.md). The release-era details below
remain useful, but their blanket native-unverified status and all-wood-blocking
policy are superseded for the local revision. The old ZIP remains unchanged.

## Download and open

Extract the complete project archive. Add the extracted **AshenVoxel** folder
in Unity Hub and open it using **Unity6000.3.27f1 /6.3 LTS**. Do not place its
Assets folder inside another Unity project. Let Package Manager and asset import
finish; the pinned packages include URP17.3.0 and Input System1.17.0.

First-open setup selects `Assets/Scenes/Exploration.unity` and creates its URP
pipeline and SSAO renderer. On later opens, your current scene is preserved.
Choose **Ashen Voxel → Exploration → Open playable world** to return. If a pipeline
or setup asset is missing, leave Play and choose **Repair project setup** from
the same menu. Do not run the older arena repair menu to configure this scene.

Press Play and wait for the loading message to finish. The real99-model pack
uploads over hundreds of frames; startup is deliberately gated until nearby
terrain, tree/rock collision and a clear starting position are ready. Python,
model downloads and a running service are unnecessary for this first Play.

## Move and inspect

| Input | Action |
| --- | --- |
| Mouse / WASD | Look / move |
| Space / Shift on foot | Jump / sprint |
| F | Toggle walking and flight |
| Q/E or Ctrl/Space in flight | Descend / ascend |
| Shift in flight | Flight boost |
| N | Explicit collision-free inspection flight, if allowed in settings |
| F1 | Open settings; movement pauses and the pointer releases |
| Escape | Release pointer or close the settings panel |
| Click | Recapture pointer; no terrain editing |
| H | Hide/show the short controls display |

Flight normally collides with terrain, wood and rock. Soft leaf tissue, ferns,
petals and mushrooms are passable; woody bush stems and branches remain solid.
Ground movement samples a continuous terrain field to avoid voxel stair jitter.
Hard-object collision uses retained10 cm material spans independently of visible
tree/rock LOD. Movement waits for unknown nearby collision instead of treating
an unloaded cell as empty. Noclip is an explicit inspection option.

There is no player body model, combat, break/place, inventory, terrain-edit save
or world destruction in this exploration scene. For the separate earlier
deformation/granular-gravity experiment, open `Assets/Scenes/ForestArena.unity`.
Neither scene currently simulates disconnected trees as falling rigid islands.

## Settings

Local update: `EnableVoxelTerrain` in the settings asset enables streamed10cm
cubic terrain near the observer on the next Play (default on). F1 shows its page,
cell, triangle and remaining queue counts. Distant mesh LODs continue, and old
coverage remains while pages build. The same existing surface material layers
and pigment populate real bounded voxel volumes. This does not enable digging
or terrain physics. See [voxel terrain](VOXEL_TERRAIN.md) for exact scope and tests.

F1 exposes walking/flight speed, flight boost, field of view,20–40 km terrain
coverage, optional0–100 km coarse horizon, tree/small-plant distance, fog,
sun elevation and optional local decoding. Apply requests a new view plan;
Save writes numeric options to
`Application.persistentDataPath/exploration-options.json`. Return to starting
clearing is useful after long flights. Hiding the HUD does not disable F1.

The asset `Assets/AshenVoxel/Settings/ExplorationSettings.asset` exposes source
datasets, world dimensions, vertical scale, spawn, density, geometry/work limits,
tree LOD distances, collision dimensions, atmosphere, snow and water. Use
**Ashen Voxel → Exploration → Select world and movement settings** to locate it.
Play clones this asset, so in-session edits do not rewrite your authoring asset.
World source, surface generation and starting-position changes take effect on
the next Play; movement/view/population controls update during Play. The AO flag
is applied by Editor setup/repair. The terrain seed belongs to the immutable
model job; the exposed Seed changes surface and population variation.

Defaults are30 km terrain coverage,512 terrain tiles,0.5 m finest proxy grid,
1.4 km trees,32 m small plants,220 m rocks,8 million submitted vegetation
triangles and180 m shadows. Full10 cm mature redwoods are limited to a12 m
vicinity; larger-distance LODs reduce cost. These are configurable starting
budgets, not a demonstrated FPS guarantee. A single upload or generation step
can exceed a cooperative time target. The full pack's estimated native mesh
payload is about341 MiB, before terrain, textures and engine overhead.

## What the world contains

The authored map is exactly **299,520 ×149,760 m**, centered at zero. Its horizontal
mapping follows the handed-off Minecraft scale5: native30 m model pixels become
5 m world samples. Elevation remains physical metres independently, allowing
multi-kilometre mountains. This is a finite imported map generated with the
original InfiniteDiffusion method, rather than an endless repetition of this map.

Everywhere has **actual pretrained coarse-network terrain/climate at1280 m
spacing**. Around X85,760 / Z15,330 m, a **5,115 ×5,115 m region** has actual
full-pipeline decoded data at5 m spacing, with a static512 m boundary blend.
The full original latent/decoder pipeline executed16 canonical owners for this
starter region. Far-away land remains the learned coarse stage until optional
decoded data is available. F1 reports the source stage at your position.

Source sampling, render tessellation and matter cells have distinct scales.
Near terrain uses0.5–1 m smooth height proxies and a shared geology/snow/material
field; it is not a fully allocated10 cm volumetric landscape. Trees, plants,
rocks and their hard collision retain10 cm source cells. Distant terrain LODs
and optional coarse horizon reduce geometry with distance. Sampling a coarse
field more finely does not invent measured neural detail.

Five forest habitats choose regionally dominant species: oak woodland, birch
grove, pine taiga, wet redwood forest and seasonal broadleaf woodland. Independent
coherent fields choose young dense, mature open and varying-density areas.
Arid, snowbound or unsuitable ground is not forced into mixed forest. The pack
contains16 tree prototypes,23 small-plant prototypes and60 rock prototypes;
shared instances keep fixed physical voxel size. Mature redwoods reach roughly
74–75 m and retain actual ridged/buttressed bases.

Surface generation includes coherent lithology, weathered stone, gravel, sand,
black sand, soil, mud, biome-colored cover and variable-thickness patchy snow.
Visual sea water has a small underwater color effect; it does not implement
flow, buoyancy, drainage connectivity, rivers or a water-cell simulation.

### Places to compare forests

The [actual-world survey](exploration-world-survey.json) sampled109,512 locations
and confirmed all five habitats and both stages, including nearby matching tree
instances and clear body space. These are global metres, with feet Y. To start
at a sample, stop Play, disable **Use Recommended Spawn**, and set **Spawn X/Y/Z**
in the settings asset; add roughly2 m to listed Y. Keep default world dimensions,
vertical scale and surface settings for these recorded coordinates. Re-enable
Use Recommended Spawn to restore the normal clearing. Most examples are outside
the bundled decoded crop and therefore use learned coarse terrain by default.

| Habitat | Stage | X | Feet Y | Z | Distance from start |
| --- | --- | ---: | ---: | ---: | ---: |
| Oak woodland | Young dense |83520 |291.32 |20160 |5.32 km |
| Oak woodland | Mature open |86720 |256.29 |13760 |1.84 km |
| Birch grove | Young dense |86720 |519.80 |11840 |3.62 km |
| Birch grove | Mature open |85440 |792.09 |12480 |2.87 km |
| Pine taiga | Young dense |82240 |1994.98 |27840 |13.00 km |
| Pine taiga | Mature open |88640 |1403.90 |2880 |12.78 km |
| Redwood forest | Young dense |89920 |446.95 |12480 |5.04 km |
| Redwood forest | Mature open |90560 |670.46 |12480 |5.58 km |
| Seasonal broadleaf | Young dense |88000 |222.25 |12480 |3.62 km |
| Seasonal broadleaf | Mature open |86080 |147.94 |15040 |0.43 km |

[Current rock shape/pigment previews](ExplorationPreview/README.md) show six
shape families with three seeds each. They render exact current voxel data
offline and are explicitly labelled **not Unity screenshots**.

## Extend learned detail or import another map

The complete original-model integration remains in the project. See
[the provider instructions](../Tools/ExplorationBake/README.md) and
[safe decoded-region publication](EXPLORATION_LIVE_REFINEMENT.md).

For optional Windows local generation, install Python3.12 and Git, stop Play,
and run `Tools/ExplorationBake/Start-InfiniteDiffusion.cmd`. It downloads and
verifies the pinned model/source, prepares the map and rebakes matching samples
for the local runtime before serving port8787. Refresh Unity after the bake,
then enable the local service in F1. Windows launcher execution and GPU inference
were not tested here. CPU inference can be slower than boosted flight.

**Ashen Voxel → Exploration → Prepare another Azgaar map** accepts Azgaar Full
JSON exports. The launcher exposes horizontal dimensions and metres per sample;
native model spacing stays30 m. Client-only horizontal/vertical resizes can
also inspect the baked map, but disable live decoding until matching data is
rebaked. New source/model/runtime/policy identities must match exactly.

Decoded patches are bounded, immutable and shared by terrain, collision and
population. They activate or retire outside a512 m player guard plus query/prop
margins, rather than moving occupied ground. Disabled or absent services retain
the playable baked world. Negative predicted physical precipitation/seasonality
is handled by an explicit versioned publication policy; no analytic fallback
is passed off as generated model output.

## Evidence and remaining checks

[Independent review](EXPLORATION_REVIEW.md),
[population/streaming evidence](EXPLORATION_POPULATION.md), and
[rock/reference evidence](AVROID_SHADING_EXPLORATION.md) distinguish the executed
tests and their limits. Runtime and Editor C# compiled against the pinned actual
Unity/URP API assemblies. Headless tests exercise real movement, streaming,
model data and startup logic, using numerical Unity substitutes for engine calls.

The end-to-end startup smoke suite passes31 checks with the actual serialized
settings and all bundled assets: loading/input gates, clear spawn, no HTTP client
by default, walk/fly, no editing, F1/settings persistence, same-frame origin
rebasing and resource cleanup. Its test clock is simulated. The actual-world
walk covered8 m in120 input frames with zero collision-readiness holds; it is
evidence for the source logic rather than a native motion/FPS measurement.

`python3 Tools/check_exploration_release.py` verifies recorded source/asset hashes
and coverage of all60 current C# files by the official-API compilation reports.
Packaging runs that freshness check as well as project/documentation integrity.
It does not rerun or substitute for native playtesting.

An activated native Editor was not run. **Shader import/compilation, actual
visual appearance, native movement, builds, target hardware FPS and VRAM use
still require the local Play test.** No exact appearance match to Avroid is
claimed: the requested newest video/media remained inaccessible. Revised rocks
use restrained intrinsic pigment, a dielectric lighting model, vertex AO and
SSAO support, but this is not path tracing. Start with the exploration checks
in [PLAYTEST.md](PLAYTEST.md) when opening the archive.
