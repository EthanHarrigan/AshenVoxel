# Rendering experiment before committing the visual stack

Avroid's newest survival discussion explicitly describes a custom path tracer, ray-marched ground, and triangle-based hardware ray tracing for voxel objects. See the [author comment branch](https://www.reddit.com/r/VOXEL/comments/1xddu2o/comment/19rjcztu71d37/?force-legacy-sct=1). This is evidence for separating **geometry representation** from **lighting integration**. A voxel game can ray trace greedily meshed voxel triangles; a voxel appearance does not require every primary ray to traverse an octree.

The current demo is URP rasterization. It does not implement DXR, path tracing, global illumination, denoising, or Avroid's custom engine. Local mesh occlusion and fog help readability but cannot substitute for his light transport. No screenshot comparison has established a match.

## Compare three independently measured paths

| Path | Geometry | Lighting | What it answers |
| --- | --- | --- | --- |
| A: implemented baseline | Greedy material surfaces | URP direct lighting, ambient and local occlusion | Can ordinary Unity rendering satisfy combat response, addon integration and a useful initial look? |
| B: proposed hardware RT spike | The same greedy triangles and conventional actor meshes | Hardware ray lighting/path tracing through a suitable Unity integration | How much of the appearance gap is lighting, without changing the world data? |
| C: proposed terrain-brick spike | Sparse terrain bricks plus triangle/local-volume actors | Explicit ray traversal and the same target light-transport budget | Does ground ray traversal improve edit-heavy performance enough to justify custom render integration? |

Path B might be an isolated HDRP comparison project or a carefully scoped custom DXR integration. Verify the selected Unity/pipeline/platform's actual supported runtime features before choosing it; an Editor reference renderer is not automatically a shippable real-time solution. Keep its test world data identical. Do not migrate the main project simply because a reference screenshot is attractive.

Path C starts with correct exact traversal and a hierarchical occupancy accelerator. Evaluate cached distance bounds, SVO/64-tree, DAG or NAADF only after that baseline. A conservative stale cache must never skip newly placed material. Compression and lighting are separate experiments.

## Shared fixture and capture contract

Use seed714103, generator4,25.6×12.8×25.6m,10cm matter and identical materials. Export/read the exact generated cells rather than recreating a similar height field in another renderer. Use an eye-height camera, a fixed elevated overview and a fixed path crossing a canopy shadow, exposed soil cut and rock face.

Start with the same single sun, sky/ambient energy, exposure, camera FOV, render resolution, shadows and display transform. Disable bloom and depth of field while comparing material/lighting response, then reintroduce them at matched settings. Capture moving-camera sequences and repeated topology changes; static frames conceal temporal accumulation errors.

Run idle, small dig, repeated cross-chunk crater, sand settling and the exact Cairn sphere sequence. Later add one detached tree body, an overhang and a small emissive source. Record absent features as absent rather than silently dropping them from a faster path.

## Required integration work for a ray path

- Write depth that agrees with conventional character/boss meshes and transparent effects. Define reversed-Z, near-plane and world-origin behavior.
- Provide stable material IDs, normals and motion data where temporal denoisers/upscalers require them. A destroyed surface must reject old history.
- Integrate shadows for mesh actors and terrain in both directions. A single sun shadow map is not equivalent to multi-bounce GI.
- Schedule acceleration rebuild/refit work after edits and transformed bodies. Track total and worst single-event update cost, including uploads.
- Establish ownership/fences for buffers used by graphics and asynchronous compute. Declare access correctly in RenderGraph; a group barrier does not synchronize separate dispatches.
- Distinguish static far content, editable nearby ground and dynamic local bodies. LOD transitions must preserve silhouettes and avoid inconsistent collision expectations.
- Measure denoising lag, foliage ghosting, disocclusion, emissive fireflies, thin-branch stability and subpixel shimmer under motion.
- Record GPU, API, ray counts, bounce limits, denoiser, upscaling/render scale, output resolution, resident bytes and update latency. Never transfer another developer's4070/120FPS claim into this project's acceptance result.

## Admission decision

Keep the data core and material semantics stable. Adopt a new path only after the same fixture passes edit correctness, collision expectations, depth/shadow integration, visual motion checks and named hardware timing gates. An improvement in idle GPU time that creates unacceptable boss-impact acceleration rebuilds is insufficient. A beautiful still image with delayed destruction is insufficient for this game.

The current implementation is the control group. The research notebook lists primary sources for Aokana, NAADF, GPU DAG editing, Teardown and Unity RenderGraph; their code and performance claims remain evidence to study, not implemented project features.

The current tree pass also includes a separately labelled actual-data CPU path-tracing study in [the tree gallery](TreeGallery/README.md). It compares lighting on the same generated cells, not an implemented Unity ray-tracing path or benchmark.
