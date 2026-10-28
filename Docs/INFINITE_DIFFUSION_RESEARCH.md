# InfiniteDiffusion / Terrain Diffusion integration research

Research date:26 October2026. The previously modified Minecraft mod/chat was initially unavailable, and the user was told immediately. The public user fork through blueprint.37, commit`d808aee2b7102917417d3928cb021e0a842f3ec6`, was then identified. The written history and current.41 source snapshot have since been received, with present inventory files verified. [Selected mod reference](MOD_PARITY.md) records the inspected terrain options and scope. The user requests diffusion terrain, Azgaar import, map scaling, biomes, tall mountains and distant LOD/culling; settlements, roads and a full mod port are excluded. Actual pinned pretrained CPU inference has now run; [executed validation](VALIDATION_INFINITE_DIFFUSION.md) records both successful neural output and a failed strict raw cropped-query equality check. No new training or GPU performance has been executed.

## Identity and paper findings

The primary paper is Alexander Goslin's [InfiniteDiffusion: Bridging Learned Fidelity and Procedural Utility for Open-World Terrain Generation](https://arxiv.org/html/2512.08309v4), arXiv2512.08309v4, SIGGRAPH2026, DOI10.1145/3799902.3811080. The older title was *Terrain Diffusion: A Diffusion-Based Successor to Perlin Noise in Infinite, Real-Time Terrain Generation*. The [project site](https://xandergos.github.io/terrain-diffusion/) connects the paper, terrain implementation and Minecraft mod.

The method lazily evaluates overlapping denoising windows and normalizes their weighted contributions. Recursively required context is evaluated rather than generating a fixed complete canvas. Bounded caches accelerate revisits but do not define the world. Seed-consistent initial noise and deterministic dependencies make queries independent of traversal order. Fixed-region random access has bounded cost independent of coordinate, not microsecond latency or zero context work. Few blending phases limit recursive context expansion; terrain uses a trained hierarchical pipeline and stabilized elevation representation. This is learned height/climate synthesis, not heat diffusion or an erosion cellular automaton. It does not provide10 cm volumetric geology, gameplay streaming, mesh LOD, occlusion or destruction. The paper identifies scattered point queries as inefficient and3D voxel synthesis as future work. Its performance observations belong to its tested model/hardware/configuration, not this Unity project.

## Exact inspected source versions

Reference repositories were cloned outside the Unity project, under `/workspace/reference-code`; no copied third-party implementation or weights are bundled with this report.

| Component | Inspected identity |
| --- | --- |
| Python terrain implementation | [xandergos/terrain-diffusion](https://github.com/xandergos/terrain-diffusion/tree/e8dcb4b1a834ab2f6b1a6f5256ed7c9f2f3e8230), commit`e8dcb4b1a834ab2f6b1a6f5256ed7c9f2f3e8230` |
| Minecraft Fabric integration | [xandergos/terrain-diffusion-mc](https://github.com/xandergos/terrain-diffusion-mc/tree/23d3f50e5108882bb88a03c3ab048aa63633a02f), commit`23d3f50e5108882bb88a03c3ab048aa63633a02f` |
| Minecraft model | [terrain-diffusion-30m-onnx](https://huggingface.co/xandergos/terrain-diffusion-30m-onnx/tree/ad2df557eca5645f588766101cf3bc3682455c3e), revision`ad2df557eca5645f588766101cf3bc3682455c3e` pinned in the mod's`build.gradle` |
| Python30 m model metadata | [terrain-diffusion-30m](https://huggingface.co/xandergos/terrain-diffusion-30m/tree/9ef8030cb805b433b98ec25c5dddefbac07a9e26), revision`9ef8030cb805b433b98ec25c5dddefbac07a9e26` observed through the public model API |

Both source repositories have MIT licenses with copyright/notice retention requirements. The inspected30 m model repository metadata also declares MIT. Each selected checkpoint revision, dependencies and original training-data terms should stay recorded with an installed model; a code license alone does not describe every future model or dataset. The Hugging Face30 m and30 m ONNX metadata were inspected without downloading weights.

## What the Minecraft mod actually contributes

The inspected [LocalTerrainProvider](https://github.com/xandergos/terrain-diffusion-mc/blob/23d3f50e5108882bb88a03c3ab048aa63633a02f/src/main/java/com/github/xandergos/terraindiffusionmc/pipeline/LocalTerrainProvider.java) queues inference on one executor, deduplicates pending region requests and caches height/biome regions. It samples the model's native grid, bilinearly upsamples when needed, adds bounded detail and classifies climate with rules. This is a useful provider/scheduler pattern; evaluating the network for every voxel would defeat its design.

The [HeightConverter](https://github.com/xandergos/terrain-diffusion-mc/blob/23d3f50e5108882bb88a03c3ab048aa63633a02f/src/main/java/com/github/xandergos/terraindiffusionmc/world/HeightConverter.java) converts land elevations using native meters-per-pixel divided by configured world scale, anchors sea at MinecraftY63 and compresses negative ocean heights through a square-root curve. **Do not copy that conversion into Unity's physical-meter world.** Unity should retain height in meters and sample the terrain into0.1 m cells locally. Horizontal landmass size and vertical relief need independent controls.

The mod's [README](https://github.com/xandergos/terrain-diffusion-mc/blob/23d3f50e5108882bb88a03c3ab048aa63633a02f/README.md) recommends GPU inference and describes Windows DirectML, CUDA and CPU/macOS CoreML builds, roughly1.5 GB VRAM and2.5 GB RAM, and approximately2.5 GB downloaded assets. Those are upstream requirements, not measured Unity budgets. Its [model manager](https://github.com/xandergos/terrain-diffusion-mc/blob/23d3f50e5108882bb88a03c3ab048aa63633a02f/src/main/java/com/github/xandergos/terraindiffusionmc/pipeline/ModelAssetManager.java) validates downloaded files against a pinned hash manifest. Preserve that principle.

## Public provider contract and precision traps

Inspection of [WorldPipeline](https://github.com/xandergos/terrain-diffusion/blob/e8dcb4b1a834ab2f6b1a6f5256ed7c9f2f3e8230/terrain_diffusion/inference/world_pipeline.py) finds:

- A64-bit world seed, coarse/latent/decoder stages, a bounded cache and`T` restricted to1 or2 in this implementation.
- Model-native resolution is configuration data: constructor default90 m, current API default model30 m. Do not hard-code either into a Unity client.
- `get(i1,j1,i2,j2)` returns meter elevation plus optional five planar climate channels: mean temperature, temperature seasonality, annual precipitation, precipitation variability and a lapse-rate-related beta channel.
- Five coarse conditioning channels can be supplied as finite or periodic rasters. Imports and conditioning/seed changes rebuild cached stages. Such changes create a different generated world and must invalidate downstream products.

The existing [Flask API](https://github.com/xandergos/terrain-diffusion/blob/e8dcb4b1a834ab2f6b1a6f5256ed7c9f2f3e8230/terrain_diffusion/inference/api.py) deliberately serializes only the first four climate channels, interleaved float32 after int16 little-endian elevation. Height is floored to full meters. Its old`API_README` scale examples assume90 m but the code defaults to the30 m checkpoint. A Unity integration must negotiate model identity, native spacing, channel layout and physical units explicitly. For smooth terrain and10 cm matter, use float32 elevation in a project-owned versioned interchange; int16 meter output is an acceptable coarse diagnostic, not sufficient elevation precision for close-up voxel materialization.

Do not silently expose mutable global`POST /seed` to two worlds sharing a service. Use a provider per world or immutable requests keyed by seed/configuration, and reject stale results with a generation identity. The mathematical seed property does not prove bit-identical ONNX/PyTorch outputs across devices, precision modes, kernels or runtime versions. Either validate that operational requirement or freeze/checksum authoritative generated tiles for saves and networking.

## Azgaar conditioning already exists upstream

The inspected [azgaar_to_tiff.py](https://github.com/xandergos/terrain-diffusion/blob/e8dcb4b1a834ab2f6b1a6f5256ed7c9f2f3e8230/terrain_diffusion/inference/utils/azgaar_to_tiff.py) supports **Azgaar Full JSON**, including`info`, map coordinates, settings, grid and packed cells/vertices. It rasterizes Voronoi polygons into elevation and four climate-conditioning rasters. Land height follows`(h−18)^heightExponent` for`h>=20`; ocean depth is a configurable negative power curve. Mean temperature comes from grid data; precipitation is converted by an assumed factor100; variability comes from biome-based heuristics. Those assumptions need provenance and editable controls rather than being disguised as measured climate.

The companion [tiff_export.py](https://github.com/xandergos/terrain-diffusion/blob/e8dcb4b1a834ab2f6b1a6f5256ed7c9f2f3e8230/terrain_diffusion/inference/tiff_export.py) loads`heightmap.tif`, `temperature.tif`, `temperature_std.tif`, `precipitation.tif` and`precipitation_cv.tif`. It pads64 coarse cells, expands256 model pixels per coarse cell and exposes per-channel conditioning strength. Temperature standard deviation TIFF units are Celsius, while internal conditioning uses Celsius×100. Its output export quantizes to int16 meters; prefer a float output for the Unity bridge.

These tools substantially reduce integration work. The received user fork adds selected nonperiodic map/conditioning/coast/summit controls described in [MOD_PARITY.md](MOD_PARITY.md). Neither converter is an excuse to assume that an arbitrary`.map` archive, screenshot or GeoJSON export matches Full JSON.

### Recommended world-map semantics

Treat imported Azgaar data as an authored macro envelope. Store original dimensions, polygon IDs, land/water mask, elevation/climate/biome provenance and an explicit physical width in meters. Horizontal resize maps world coordinates into the same source map; it must not change the0.1 m cell size. Vertical scale, sea level, stone depth and maximum elevation remain separate.

Default outside-map behavior should be explicit ocean or a chosen generated extension. Periodic wrapping is an optional world topology, not an implicit meaning of “infinite.” Neural refinement can move coastlines because conditioning is not a hard constraint; if exact landmass preservation matters, apply a documented coast-distance envelope after refinement, with a smooth transition rather than independently clamping tiles. Preserve imported biome identity separately from the present Forest visual override, so future biome rules can use the original climate without reimporting the map.

## Recommended Unity architecture

The following is a project recommendation, not an upstream implementation claim:

1. **Offline/sidecar-first provider.** Pin a Python upstream revision and model revision; query rectangular tiles on a worker. Bake a finite Azgaar world or stream ahead of traversal. Import a versioned float terrain/climate tile format through an engine-independent provider. This proves terrain quality and coordinate contracts before native inference packaging.
2. **Common authoritative height field.** Define signed global integer tile/cell coordinates, double meter positions, exact row→Z/column→X mapping, global sample-lattice origin, spacing and inclusive/exclusive extent conventions. Add shared halos for bilinear sampling/normals so neighboring requests agree. Keep voxel, mesh and diffusion tile sizes independent.
3. **Local10 cm matter.** Materialize editable surface/near-player bricks only; unmodified deep stone is implicit stratigraphy. Sparse mutation deltas preserve overhangs/cavities and destroyed matter. A3 km mountain must not allocate3 km of dense tinted voxels under every visible surface column.
4. **Distant derived surfaces.** Heightfield quadtree/clipmap LOD can represent many kilometers from the same height provider. Generate data without camera transforms; derive camera-relative float vertices at upload. Bound residency, inference, meshing, upload and collider work independently. Terrain edits update both local matter and coarser visual summaries with revision cancellation.
5. **Native inference only after validation.** Evaluate ONNX Runtime/Unity-compatible inference separately on the actual target OS/GPU. Upstream Java classes/native libraries cannot be dropped into a C# assembly. Validate unsupported operators, tensor layouts, device readbacks, memory contention and licensing before adopting an inference backend. Ship model checksums and cache/configuration IDs.

Pretrained models avoid the training project. Upstream's [training guide](https://github.com/xandergos/terrain-diffusion/blob/e8dcb4b1a834ab2f6b1a6f5256ed7c9f2f3e8230/TRAINING.md) has separate data preparation, autoencoder, base/coarse models and distillation stages, with24 GB-class GPU requirements. There is no reason to begin retraining before testing the released model against the requested map constraints.

### Concrete bridge implementation route

For the inspected source revision, the broad CLI workflow is:

```sh
python -m terrain_diffusion azgaar-to-tiff map-full.json conditioning/ --scale 7.68
python -m terrain_diffusion tiff-export xandergos/terrain-diffusion-30m conditioning/ output.tif --seed 714103 --device cuda --no-compile
```

These are source-verified invocation shapes, **not executed commands** in this environment. Install the pinned upstream dependencies first. The7.68 km conditioning spacing matches256 native30 m samples; a90 m model instead uses23.04 km. Map dimensions may need resampling/rounding. The stock TIFF exporter writes integer meters, so a project float-tile bridge should call the pipeline directly instead.

Recommended direct sequence: download a pinned Hugging Face snapshot, pass its local path to`WorldPipeline.from_pretrained`, select device, bind a bounded direct cache, install all conditioning rasters with`rebuild=False`, rebuild once, then query bounded rectangles inside the pipeline context. Write float32 meter samples plus explicit global origin/spacing and checksums. Keep source climate rasters and original biome IDs in accompanying metadata; a display override to Forest need not throw them away. A project-owned tile format can serve a fully baked authored map without requiring online inference during gameplay.

Do not assume importing finite rasters into an infinite model preserves an arbitrary exact map width automatically: conditioning-cell spacing, source-map physical width, model-native sample spacing and Unity horizontal scale must be reconciled explicitly. Pin source/model/configuration/map/seed identities in the output manifest. Set a maximum tile/sample count before allocation, schedule one inference owner, and write completed tiles atomically so interruption can resume by verifying existing checksums.

## Acceptance gates before calling diffusion integration complete

- Run actual selected pretrained weights; record provider/model/revision/device/precision and source-map hash. A procedural-noise fallback must identify itself clearly.
- Query adjacent, overlapping, negative-coordinate and distant tiles in different orders, after cache eviction, and after restart. Compare shared samples, not screenshots alone.
- Verify units/orientation with an asymmetric synthetic raster and known heights, plus a real Azgaar Full JSON export. Test finite-map boundaries, sea level, scale changes, missing/nonfinite climate and hostile/oversized inputs.
- Test3–8 km summit elevations,0.1 m local edits, steep cliff views, cold teleportation, cache pressure and cancellation of obsolete requests. Cold and cached latency must be separate.
- Confirm far LOD and local materialization consume the same generation identity and terrain field; test edits crossing tile/LOD boundaries and authoritative snapshot/delta reload.
- Profile inference and game GPU rendering together. Inference's upstream traversal-throughput claims say nothing about10 cm meshing, foliage rendering or boss-deformation frame time.

Remaining dependencies for a production neural terrain system include target-hardware profiling, broad pretrained-provider/cache/partition validation, native Unity playtesting and production streaming/edit persistence. The source-task transfer has been received and reviewed within scope; unrelated settlements/roads do not need copying. Research plus a provider interface alone is not an “only bug fixing remains” neural terrain system.
