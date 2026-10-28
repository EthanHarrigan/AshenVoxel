# Optional decoded terrain extension

The exploration scene opens on bundled actual InfiniteDiffusion terrain. The whole 299.52×149.76km map has a learned coarse-network height/climate layer; the starter region additionally has a 5m decoded layer. Neither source is analytic noise. Fine proxy tessellation does not imply equally fine learned geometry, and this scene does not yet stream 10cm destructible matter throughout the view.

Live refinement is **off by default**. Enable the local InfiniteDiffusion refinement service in F1 only after launching a matching provider. Without it, the baked world remains explorable. CPU decoding may take longer than travel; the player continues through existing learned terrain while decoding runs ahead. Existing patches remain when requests are disabled. A full compatible local provider uses the original latent/decoder pipeline, with versioned physical-climate rectification advertised in its world identity; no synthetic terrain replaces missing or rejected model data.

The provider must advertise the exact bundled `world_id` and 5m world grid. A new seed, conditioning map, model/runtime identity or coordinate/policy change requires matching regenerated archives. Client-side map resizing or vertical rescaling disables live requests: an unchanged baked world ID alone does not establish identical coordinates. The resized immutable baked source remains playable.

## Publication contract

`ExplorationRefinementField` is the shared height/climate authority for terrain rendering, collision and population. `ExplorationLiveRefinement` requests immutable 640m patches ahead, using 128m blending and a 32m terrain-attribute sampling halo. It copies verified response grids into owned float arrays; subsequent HTTP cache eviction cannot invalidate them. It never performs I/O during a sample query.

A patch can activate, be replaced or retire only when its entire influence rectangle plus 96m query/prop guard is more than 512m from the observer. This includes corners and the blend zone. The protection test applies to eviction as well as insertion. If all eviction candidates are protected, publication waits rather than changing the ground beneath the player. Changing flight speed or outrunning generation does not bypass this guard.

Each publication atomically replaces an immutable state. The renderer captures that state once for worker LOD planning and once for every incremental mesh job; a tile cannot contain rows from different revisions. Source changes invalidate intersecting derived terrain and visual-water depth hints while keeping complete old coverage until replacement is available. Separate dirty rectangles are emitted for each affected patch; an enclosing union could accidentally cross the player and is deliberately avoided. Population uses the same regional notifications and its own protected near cells.

Heights blend with normalized weights when patches overlap. Categorical biome identity stays with the imported map. Each physical temperature channel is first adjusted from its layer's elevation to the final blended elevation using that layer's lapse rate; the adjusted temperatures and other physical climate channels then blend. Conservative bounds contain both base and patch extrema, plus numerical margin.

## Work and memory limits

Defaults allow at most 2 in-flight/complete-waiting patch jobs, 16 published patches and 16MiB published height/climate payload. The client separately caps its transport cache at 4 regions / 8MiB. Pending copies, immutable snapshots retained by worker/mesh jobs, CLR array headers and the always-resident bundled atlas are additional memory; 16MiB is not a total process-memory promise. Retry metadata is bounded. A 640m patch's size remains independent of travel distance or world extent.

Terrain mesh construction has a cooperative work cap and a soft millisecond budget, with main-thread mesh uploads capped separately. A single attribute evaluation or mesh upload can exceed the soft time target. LOD selection runs on one worker job, captures its source and discards stale plans. Complete active plus desired plans are separately bounded. These controls are not an FPS measurement.

## Reproduction

Run `python3 Tools/run_exploration_live_checks.py` for guard, immutable state, overlap/climate, cache-cap, optional/offline, world-scale and incremental-render integration checks. Add `--live-url http://127.0.0.1:8787` for exact bundled-vs-provider height/climate verification and the real component's connect/request/publish path using the already-cached starter region. `Docs/exploration-live-validation.json` records the checked source/asset hashes and actual service identity.

Run `python3 Tools/run_world_explorer_checks.py --baked-height Assets/AshenVoxel/WorldScaleRuntime/Samples/CetevilesNeural.bytes --baked-climate Assets/AshenVoxel/WorldScaleRuntime/Samples/CetevilesNeural.climate.bytes` for the existing renderer/remote transport suite. The tests execute actual C# runtime code with Unity API substitutes. Official Unity reference-assembly compilation is a separate check; native Editor/player behavior, URP shaders and target-machine frame time still require the Unity playtest.
