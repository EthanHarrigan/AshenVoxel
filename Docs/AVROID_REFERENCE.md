# Avroid reference: newest posts and author comments

Accessed 26 October 2026. The user selected [u/Avroid](https://www.reddit.com/u/Avroid/s/O7Jva5pV52) as the primary appearance and technique reference and fixed the project baseline at **10 cm per voxel**. This appendix records what was actually retrieved, what the author claims, and what remains unverified.

## Sources inspected

The [new-sorted submissions page](https://www.reddit.com/user/Avroid/submitted/?sort=new) listed **v1.7 — survival**, **v1.6 — tropics**, and **v1.5 — procedural generation** first during retrieval. Below these were engine rewrite, birch forest, oak/cherry forest, desert and physics posts. “Newest” here means the order exposed by that page during this session, not a guarantee that every Reddit post was returned. Relative post ages and search crawl ages were inconsistent; exact publication dates are not reconstructed from them.

### v1.7 — survival: direct author implementation details

[Post](https://www.reddit.com/r/VOXEL/comments/1xddu2o/) · [author comment branch](https://www.reddit.com/r/VOXEL/comments/1xddu2o/comment/19rjcztu71d37/?force-legacy-sct=1)

In comments attributed to **Avroid**, the author describes 10 cm voxels, a 20 km view using LOD, and a 100 m range without LOD. The reported performance is 120 FPS on an RTX 4070; resolution, complete settings, scene activity and frame-time distribution are unspecified here.

The author says lighting uses path tracing. Ground is ray marched; other voxel models become greedy-meshed triangles traced through DXR. Each model has its own bottom-level acceleration structure (BLAS), instanced into one top-level acceleration structure (TLAS). Distant grass is grouped per chunk; nearby blades use individual instances whose transforms change for wind. A felled tree is remeshed into a separate BLAS and becomes a PhysX rigid body. Models originate from polygon voxelization.

Confidence: high that these are the retrieved author's statements; implementation and performance have not been independently audited. Do not translate “path tracing only” into “every object uses voxel-grid traversal”: the same author explicitly describes triangle acceleration structures for objects.

### v1.6 — tropics: editable terrain confirmed

[Post](https://www.reddit.com/r/VOXEL/comments/1x1lzmo/v16_tropics/) · [author reply](https://www.reddit.com/r/VOXEL/comments/1x1lzmo/comment/19ri5kuwi4pb5/)

Avroid answers affirmatively when asked whether terrain is editable. The page's media description mentions a tropical island, mountain and dusk-to-dawn transition. That description is page text; we did not visually verify the video. The author reply does not disclose the terrain's storage format, generation algorithm, edit latency, simulation scale or worst-case destruction workload.

Confidence: high for the affirmative author reply; insufficient evidence for detailed terrain implementation conclusions.

### v1.5 — procedural generation: animated model voxelization

[Post](https://www.reddit.com/r/VOXEL/comments/1wvsuwl/v15_procedural_generation/) · [author reply](https://www.reddit.com/r/VOXEL/comments/1wvsuwl/comment/pdj49lt/)

Avroid confirms voxelizing rigged polygon models in response to a question about the animals. The inspected comments do not specify a terrain noise, erosion or botanical generation algorithm. The title is evidence of a generation showcase, not evidence of a particular technical method.

Confidence: high for rigged-model voxelization as the author's stated method; low for any inferred world-generation algorithm.

## Consequences for this project's foundation

These are our engineering decisions drawn from the reference, not claims made by Avroid:

- **Keep 10 cm as the required baseline.** A lower optional cell size is an experiment with explicit memory and frame budgets, not the default requirement.
- **Separate material truth from rendering output.** The reference demonstrates that a voxel aesthetic can use greedy triangle geometry and hardware ray tracing for objects while terrain uses a different intersection path.
- **Treat detached trees as coherent objects.** Retain branch/component structure, local voxel data and transforms so a future rigid-body renderer need not turn an entire canopy into independent grains.
- **Allow distance-dependent foliage representation.** Near individual instances and distant grouped geometry are an informed roadmap option. The prototype does not claim that exact acceleration pipeline.
- **Budget for acceleration updates.** Meshing, BLAS rebuild/refit, TLAS updates and tree detachment are part of combat cost. Rendering an idle forest does not validate a boss tearing through it.
- **Keep a hardware tier decision explicit.** Avroid's DXR path implies a different GPU requirement from software traversal. The current Unity raster demo is a correctness and generation baseline; matching that path-traced appearance requires a separately profiled renderer experiment.
- **Keep Unity lighting compatibility visible.** A future custom intersection path needs correct depth, normals, shadows, motion vectors and temporal invalidation, or equivalent custom solutions.

## Appearance evidence and retrieval limits

We read the post pages and author comments. **We have not watched the videos or visually inspected an actual Avroid image frame in this session.** Clicking the v1.7 poster through the web tool redirected to a Reddit media page rather than returning an image. Parent-agent CDN requests returned HTTP 403. Thus no claim is made that a generated project preview visually matches Avroid, and page media descriptions are not presented as visual inspection.

Search queries for the exact username were less useful than opening the profile and post URLs directly. An earlier combined web/shell network attempt was interrupted after blocking; no outputs from that attempt were used as evidence. The successful evidence above came from subsequent direct web-tool opens, including author comment permalinks.

The remaining review task is a side-by-side comparison using accessible video frames on the same display. Evaluate silhouette, foliage scale and gaps, terrain color coherence, sky-to-ground contrast, indirect illumination, shadow softness, distance stability and camera motion. Until then, the project's dark fantasy art choices are documented original choices and the user-selected reference remains the visual target.

## Package verification boundary

Official Unity release/support pages were successfully inspected for Unity 6.3 LTS 6000.3.27f1; see [research](RESEARCH.md). Direct web opens of Unity's public package registries succeeded, but the unqualified URP registry response exposed an older version subset. That response does **not** establish 17.3.0 compatibility or resolution in this project. Input System metadata was accessible, but package resolution in Unity was not run here. Pinned versions must remain labelled unverified until the actual editor resolves the manifest and produces a lockfile. A manifest pin is not a successful package restore or Unity compile.

The [Input System 1.17 manual](https://docs.unity3d.com/Packages/com.unity.inputsystem@1.17/manual/index.html) was subsequently opened and its metadata explicitly identifies **1.17.0**. The [URP 17.3 manual link](https://docs.unity3d.com/Packages/com.unity.render-pipelines.universal@17.3/manual/index.html) redirected to the Unity **6000.3** URP manual. These are useful documentation checks, still not an editor restore or exact URP patch compatibility test.

The [URP17.3 API documentation](https://docs.unity3d.com/Packages/com.unity.render-pipelines.universal@17.3/api/UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset.html) was subsequently inspected and its metadata confirms17.3.0. This establishes documentation availability for the pin, while actual package restore and Editor compilation remain pending. See [Unity compatibility evidence](UNITY_COMPATIBILITY.md).

## Tree-focused follow-up

The new pass revisited newest post/comment URLs and the older birch/oak/pine entries. Ordinary latest-video/CDN requests still returned403 or login redirects. The author-linked official website did serve its exact linked home image and short landing video; the home image was viewed and depicts a small voxel apple on gray, so it does not resolve forest appearance. No recent forest-frame comparison is claimed. The older oak/cherry author reply describes handmade or voxelized assets alongside procedural terrain; it is not evidence that Avroid uses our procedural tree strategy. [Tree research and retrieval details](TREES_RESEARCH.md).

## Supplied video inspected -2026-10-26 local

Eight frames from the user-supplied220.97s,480x250 local MP4 were decoded and
visually inspected. Evidence/SHA256: Docs/ReferenceVideoReview in the isolated
copy. Targets: fine cubic earth forms, dense shrubs/foliage, bright cloudy sky,
warm sun and readable forest shadows. This is appearance evidence, not the
video's exact renderer settings or hardware FPS. Earlier access limits remain
historical evidence.
