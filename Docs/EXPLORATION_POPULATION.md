# Exploration population

The exploration scene uses actual 10 cm voxel trees, ground cover and rocks, baked into shared meshes before shipping. Runtime terrain sampling places instances deterministically; it does not grow a 75 m tree on Unity's main thread. This cache is replaceable. The Core generators remain the source of geometry and material ownership.

## Habitats and stand age

`WorldScaleData/ForestBiomes.cs` assigns one dominant species to each stand using source biome and physical climate. Temperate forest contains coherent oak woodland or birch grove regions; moist, mild rainforest supports redwoods; cold rainforest and boreal forest support pine; seasonal broadleaf forest uses oak. Open biomes have sparse or absent trees. The four species are not randomly blended into every forest.

Species subregions use a 5.6 km smooth field, stand age a 3.7 km field, and openness a 1.25 km field. Young stands use smaller separately generated trees at higher density. Mature stands use larger trees with more open spacing. These are deterministic static stand assignments, not a plant competition or ecological succession simulation. `ForestBiomes.Sample` and `ExplorationPopulation.StandAt` expose the assignment for navigation and future tools.

Trees use a global jittered 6 m candidate lattice, owned by 64 m cells. Rock cells are also 64 m; ground-cover cells are 16 m. All keys use world coordinates and independent hashes, so request order does not change population. Ground cover reuses `GroundCoverScatter`'s slope, moisture, climate and biome filters. The baked library includes ferns, shrubs, grass tufts, reeds, herbs, flowers, mushrooms, cacti, succulents and alpine cushions, plus five rock materials in six nonspherical forms and two sizes.

## Meshes and fidelity

`Exploration/Prototypes/ExplorationPrototypes.bytes` contains 99 models and 412 LOD meshes: 16 trees, 23 ground-cover models including one-cell accents, and 60 rocks. Each tree species has two seeds at each of two growth stages. The two mature redwoods measure approximately 73.9 m and 75.5 m, including full buttress/root and bark detail; neither generation was truncated.

All instances have scale 1. Small and large rocks are separately rasterized rather than scaling their physical voxels. Yaw rotates each local voxel object as a whole. Tree visual levels use 0.1, 0.2, 0.4, 1.6 and 6.4 m cells; other models use 0.1, 0.2, 0.4 and 0.8 m cells. Coarse occupancy reduction preserves occupied regions, favors wood over soft matter and is only a visual representation. Very distant 6.4 m crowns are intentionally coarse proxies.

LOD distance uses the three-dimensional distance to an instance's actual bounds, including tree height. A high-flying camera therefore does not request fine geometry simply because it shares a tree's X/Z. Mature redwoods use a narrow 12 m full-detail range by default because their full meshes contain about 1.5 million triangles each. The shared triangle budget can further coarsen instances. Settings expose tree, rock and understory radii and tree transition distances.

The complete pack is 8,579,736 bytes. Its AVPM0002 encoding losslessly stores the actual greedy quads, original triangle pattern, decimeter coordinates, material RGB and original corner AO. Decoding reproduces all 412 original meshes byte for byte. Compact disk size does not imply equally small GPU memory: all shared mesh vertex/index buffers total an estimated 357,554,472 bytes, before driver overhead. The bake report records each model's counts, bounds, generation limits, source hashes and exact round-trip hashes.

## Bounded streaming and rendering

`VegetationStreaming` loads one LOD mesh per initialization step (412 yields, approximately 413 bootstrap ticks), then warms the nearby population behind the loading screen. It owns one instanced material clone. Meshes are shared across instances; there are no per-voxel GameObjects. Unity `RenderMeshInstanced` submits at most 1,023 transforms per batch, using the shader's uniform-scale instancing contract.

Transform pages are spatially local: 128 m for detailed trees, 512 m for distant trees, 64 m for rocks and 32 m for ground cover. The streamer bounds resident cells, combined instance count, cell builds, matrix work and final page copies (at most 32 batches per call). It reserves the nearby collision layers before distant trees. Far cells that cannot fit the instance cap wait without being regenerated every planning tick. A 4,096-instance effective minimum protects the supported startup neighborhood; lower requested values are clamped to that minimum. Dense vistas may stop short of the requested radius when a residency cap is reached.

Complete drawing pages remain active until a replacement is complete. Cell readiness is recorded on the individual cell after publication; unrelated far cells do not temporarily freeze movement. Origin shifts immediately rebase both published and partially built transforms and bounds in the same call. Root calls `SetObserver` and `Tick` once after updating the camera origin. Moving the observer by 4 m schedules an LOD refresh; an idle observer does not rebuild every frame.

Trees cast shadows through visual LOD3, rocks through LOD1 and small plants through LOD0 by default. Nearby off-camera shadow casters remain submitted as shadow-only draws. The bootstrap synchronizes the QualitySettings and URP shadow-distance values, so this manual range agrees with the pipeline. Trees at the coarsest 6.4 m level do not cast shadows.

## Collision and source changes

Collision uses the full-resolution hard-material column spans included in each model, independently of render LOD. Wood, hard shrub stems and rocks block the actor; leaves, fungus and petals do not. Collision and support queries apply the exact inverse of the rendered yaw and use conservative body extents. Terrain collision remains the controller's separate continuous ground query. This avoids stepping over every visible 10 cm surface cube while preserving physical trunks and rock tops.

Movement waits for relevant nearby cells to be loaded and published before entering them. High flight above any possible tree does not wait for ground props, but descending gates ahead of the highest canopy. Fast movement may pause at an unprepared boundary rather than passing through a missing trunk. Runtime density edits preserve the current nearby cached population while farther cells adopt the setting; a full explicit population reset is available when a deliberate restart is desired.

Optional newly learned terrain patches invalidate only affected population cells outside the 512 m safety guard. Current collidable cells remain stable and old complete drawing persists until replacement. This relies on the world-source compositor activating patches outside the same guard.

## Verification and remaining limits

Run `python3 Tools/run_exploration_population_checks.py` and `python3 Tools/run_exploration_streaming_checks.py`. The first verifies real generation/pack/population data, including all 412 lossless mesh round trips. The second executes the actual streaming class with numerical Unity substitutes and the real pack: startup, minimum budgets, publication, 900 walking frames across cell boundaries, asymmetric rotated collision/support, rebasing, invalidation, shadow submissions and disposal. Reports live beside this document. Actual project assemblies also compile against the official Unity 6000.3.27f1 API through the independent project review harness.

`python3 Tools/run_exploration_world_survey.py` evaluates the actual shipped world on a 640 m grid. `exploration-world-survey.json` confirms all five habitats and both growth stages occur, with three dry, gentle, hard-prop-clear example coordinates per habitat/stage. The nearest examples of every combination are within 13 km of the recommended spawn. This survey keeps generation unchanged and records the input/source hashes used.

These checks do not measure native Unity rendering, GPU memory residency, visual quality or FPS. A licensed native Editor/GPU run is still required for those measurements. The library intentionally uses two tree seeds per stage, and static foliage does not bend, fall, grow or deform in this exploration scene. The mature redwood meshes are expensive despite the distance and triangle budgets; a future mesh-streaming cache could upload detailed shared models only when needed. Terrain vistas extend far beyond the vegetation radius, so distant mountains retain terrain silhouettes while distant forests eventually stop rendering.
