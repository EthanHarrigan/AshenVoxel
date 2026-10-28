# Microvoxel foundation: research and decisions

Research date: 26 October 2026. User-selected production baseline: **10 cm voxels**; finer sizes discussed below are optional research alternatives and memory-scaling examples. Primary reference: [Avroid newest posts and author comments](https://www.reddit.com/user/Avroid/submitted/?sort=new); project-specific evidence is in `Docs/AVROID_REFERENCE.md`. This is a technical decision notebook, not a claim that the prototype implements every technique below. Sources were searched and the linked abstracts, author pages, documentation or developer comments inspected. Published performance numbers describe those authors' scenes and hardware; they are not predictions for this game. No benchmark video was reproduced here.

## Recommended direction

### Editor and graphics baseline verified for this date

Pin **Unity 6.3 LTS, 6000.3.27f1**, whose [official release notes](https://unity.com/releases/editor/whats-new/6000.3.27f1) give 20 October 2026 as its release date. The [official support policy](https://unity.com/releases/unity-6/support) identifies 6.3 as the latest LTS and gives support through December 2027. Unity also describes Update releases as production-ready; LTS is a choice for a longer fixed support window and dependency stability, not proof that every Update release is unstable. The newest visible update at research time was 6000.6.7f1 in the [official archive](https://unity.com/releases/editor/archive).

Choose **URP with RenderGraph** as the initial rendering integration. Unity's [2026 pipeline strategy](https://unity.com/topics/render-pipelines-strategy-for-2026) advances URP, maintains HDRP, and starts Built-In deprecation in 6.5; it explicitly discourages Built-In for new titles. [Unity's compatibility documentation](https://docs.unity.com/en-us/engine/6000.0/manual/render-pipelines/universal-render-pipeline/customizing-urp/compatibility-mode) recommends RenderGraph for new graphics features. Keep that direction even though some impressive older Unity voxel demos used Built-In. Verify add-on versions individually; no Unity release guarantees every third-party package.

Use a hybrid world: sparse, mutable material bricks for the authoritative simulation; a replaceable surface renderer; coarser collision and structural graphs; explicit damage commands; local active simulation; and a versioned edit journal over reproducible generation. Start with a small forest arena and prove that destruction conserves material and stays within budgets before multiplying world size or adding a boss moveset. The shipping renderer is a measured decision, not a promise made from a screenshot.

For the first playable prototype, exposed-face/greedy meshes are useful because they integrate with ordinary Unity lighting, shadows, cameras and add-ons. They are not the final answer to dense centimeter-scale detail under constant edits. Keep mesh output disposable. Prototype sparse-brick ray traversal alongside that renderer and compare the same camera path, material volume and destruction recording. A custom renderer must write correct depth, normals, motion vectors and shadow data if Unity effects are expected to work.

Microvoxels describe a scale, not a specific algorithm. At 5 cm, a 100 × 100 × 25 m volume contains two billion cells before metadata. A 1 cm version contains 250 billion. Full-world per-cell updates and GameObjects per cell are untenable. Visible fidelity, authoritative simulation resolution and collision resolution should be independent.

## Modern papers that materially change the options

### NAADF — Eurographics / Computer Graphics Forum, May 2026

[Author publication page and paper](https://www.cg.tuwien.ac.at/research/publications/2026/ulschmid-2026-naadf/) · [reference implementation](https://github.com/cg-tuwien/NAADF)

Nested cells plus cached axis-aligned distance fields accelerate traversal. The authors report 3–5× improvements from nesting relative to their dense comparison structures, and around 10× with the distance caches. The work supports editing, background acceleration updates and overlays of transformed dynamic geometry. It also studies retaining 32 frames of quantized ray information for antialiasing, and sample reuse for global illumination.

Decision: the strongest recent candidate for a separate renderer experiment. Test stale-cache invalidation after explosions, memory pressure from history, disocclusion around falling debris, and camera motion. Treat the reported factors as benchmark-specific. Do not copy a complete experimental pipeline into the foundation without correctness comparisons.

### Aokana — PACMCGIT / I3D, May 2025

[Paper](https://arxiv.org/abs/2505.02017) · [publication](https://doi.org/10.1145/3728299)

A GPU-driven framework combines shallow SVDAGs, LOD, streaming, Hi-Z culling and a visibility buffer. The paper describes integrating voxel and mesh rendering, and an implementation using Unity 6 RenderGraph with Vulkan, DXC and IL2CPP. Its evaluations show substantial memory and rendering improvements over comparison approaches at high scene resolutions.

Decision: especially relevant for distant mostly static terrain and Unity integration. Do not infer that it solves continuous powder, fluids or frequent topology edits: those are different workloads. Separate compressed background geometry from hot mutable simulation bricks until benchmarking justifies unification.

### Editing Compact Voxel Representations on the GPU — Pacific Graphics 2024

[Author page](https://publications.graphics.tudelft.nl/papers/13) · [implementation](https://github.com/mathijs727/GPU-SVDAG-Editing)

The work moves compressed DAG editing to the GPU with dynamic hash tables. Its source requires a CUDA toolchain and extends HashDAG; it is not a Unity package. The implementation exposes memory reservation and allocation concerns explicitly.

Decision: compressed representations are not inherently uneditable. Their complexity buys something only if measured against plain mutable bricks for this workload. Track allocation, garbage collection, attribute edits and worst-case repeated damage, rather than only a single large subtraction.

### Encoding Occupancy in Memory Location — Computer Graphics Forum, November 2025

[Paper](https://onlinelibrary.wiley.com/doi/10.1111/cgf.70292)

An alternative SVDAG encoding uses data placement to encode occupancy. The study includes traversal, dynamic editing and voxel attribute evaluations; its disclosed platform includes an RTX 4080 Super.

Decision: useful reading for compressed renderer storage. It is not evidence that every platform will sustain the same results, nor that compression is the immediate bottleneck in a small arena.

### Dynamic Voxel-Based Global Illumination — 2024/2025

[Eurographics record](https://diglib.eg.org/items/e24298cc-a0dc-415c-adeb-7426f6bc0dc2) · [journal](https://onlinelibrary.wiley.com/doi/full/10.1111/cgf.15262)

Caches per-voxel-face visibility and separates static and dynamic geometry contributions to update irradiance efficiently. This is a lighting representation, not automatically the same grid as gameplay materials.

Decision: evaluate a coarser lighting volume separately from primary geometry. Destruction must invalidate occlusion and radiance together or a beautiful static scene becomes visibly wrong during a fight.

### Sparse Voxels Rasterization — CVPR 2025

[Paper and author resources](https://research.nvidia.com/labs/twn/publication/cvpr_2025_svraster/)

Uses direction-dependent Morton ordering and differentiable sparse voxel rasterization for high quality radiance-field reconstruction. It addresses view synthesis, learned density and radiance parameters.

Decision: relevant to visibility ordering and research awareness, but not an appropriate default representation for discrete editable wood, soil, powder and collision. A paper containing “voxels” and high FPS may target an entirely different problem.

### Future-dated watchlist, not a settled dependency

[Dynamic GPU Sparse Grids and Voxel-sequential Processing in NanoVDB](https://research.nvidia.com/labs/prl/publication/sifakis2026dynamic/) was publicly accessible during research, but its author page labels it **December 2026**, later than this project's research date. It proposes GPU-native topology changes and flat active-voxel iteration. Retain as a research lead; do not describe it as an already completed December publication or a proven production dependency as of October.

## Primary game/developer references and Reddit leads

### Noita: behavior categories, not universal sand

[Official FAQ](https://www.noitagame.com/) distinguishes liquids, rising gases, piling sand, rigid bodies and simplified chemical/thermal/electrical reactions. [Petri Purho's GDC 2019 talk](https://www.gdcvault.com/play/1025695/Exploring-the-Tech-and-Design) is the canonical scaling reference. The GDC URL returned maintenance during this session, so detailed talk claims should be checked against the recording before treating them as newly verified.

Design inference: stone should remain coherent until damaged or disconnected; loose soil should fall and pile; wood should preserve branch structure; leaves should shed. Converting everything unsupported into individual falling sand destroys both forest silhouettes and performance. Active regions and update budgets are necessary in 3D, where an extra dimension multiplies cost.

### Teardown and the newer Tuxedo renderer

[Dennis Gustafsson's 2018 technical post](https://blog.voxagon.se/2018/10/17/from-screen-space-to-voxel-space.html) describes transformed local object volumes, a packed world shadow occupancy texture with mip levels, clustered dynamic uploads, and combining near screen-space tracing with voxel tracing. It explicitly warns that naive fixed steps can miss cells and leak light.

[Official Graphics Programming Conference 2025 archive](https://graphicsprogrammingconference.com/archive/2025/) contains **Raytracing Voxels in Teardown and Beyond**, by Gabe Rundlett and Dennis Gustafsson, with slides. It distinguishes the original older-GPU technique from the next-generation renderer for an upcoming game. Do not blend claims about the new engine into claims about shipped Teardown. The archive itself has an inconsistent event date in its introductory text; use the archive title and talk identity rather than reproducing the typo.

Design inference: moving debris should own local voxel volumes with transforms. Maintain a separate world-space acceleration structure for shadowing/collision where useful. Re-rasterizing every moving microvoxel into one monolithic volume is not free.

### Voxtopolis: a useful Unity-specific reference

[Developer discussion, March 2025](https://www.reddit.com/r/Unity3D/comments/1jay1yk/my_tiny_voxel_game_is_fully_destructable_rendered/) states that custom sparse octree traversal runs in fragment shaders, with chunk bounding meshes writing G-buffers, pooled data and Burst/background work. It uses the built-in deferred pipeline. The developer explicitly says the impressive emissive particles use bloom and lights.

Evidence boundary: the title's “4K 120 FPS” is the creator's claim, not our benchmark; scene size, hardware and simultaneous edits matter. The useful lesson is system organization, shader traversal and pooling. It is not a reason to choose the legacy pipeline for a new 2026 project.

### Voxile: lighting creates much of the look

[Developer discussion, January 2025](https://www.reddit.com/r/VoxelGameDev/comments/1i0jd4x/our_voxel_game_with_ray_traced_lighting_on_a/) describes a fully raytraced voxel scene pass feeding deferred data, supplemented by sun shadows, reflections and light propagation volumes; the comments confirm SVO use. Assets come from MagicaVoxel plus custom composition tools. The developers discuss distant noise and use of LOD.

Design inference: the desired dark fantasy look depends on light transport, restrained color, roughness, atmospheric depth and authored composition. Smaller blocks alone do not achieve it. Retain material variation at multiple spatial scales; avoid random color on every cell that shimmers at distance.

### Additional leads worth reproducing later

[re-flora developer announcement](https://www.reddit.com/r/VoxelGameDev/comments/1s9rwez/im_officially_opensourcing_my_workinprogress_rust/) points to an open Rust/Vulkan 64-tree path tracer with GPU edits. Treat this as a source lead, not a verified performance comparison. [Microvoxel FPS discussion](https://www.reddit.com/r/GraphicsProgramming/comments/1kckfxx/) illustrates why a photorealistic-looking “raytraced” demo must still be checked for actual bounce count, shadows and hardware assumptions. Neither should establish the game's minimum requirements from a video.

## Older algorithms still essential

- [Efficient Sparse Voxel Octrees, Laine and Karras, 2010](https://research.nvidia.com/publication/2010-02_efficient-sparse-voxel-octrees): foundational compact storage, traversal and filtering work. Age is not a reason to discard a proven baseline.
- [A Ray-Box Intersection Algorithm and Efficient Dynamic Voxel Rendering, JCGT 2018](https://jcgt.org/published/0007/03/04/): compare efficient box-proxy traversal against meshing for dynamic small volumes. Its published speedups are particular comparisons, not universal ratios.
- [Transvoxel, Eric Lengyel](https://transvoxel.org/): transition cells stitch smooth terrain meshes across different resolutions. Applicable if the visual decision changes to smooth density surfaces; not required merely to render hard cubic microvoxels.
- [Modeling Trees with a Space Colonization Algorithm, 2007](https://www.algorithmicbotany.org/papers/colonization.egwnp2007.html): generate a branch skeleton from attraction points, then voxelize tapered branches and foliage clusters. Preserve the skeleton for health, support and falling limbs instead of reconstructing it from every leaf cell.

## Generation direction

[Physically-based Analytical Erosion, Eurographics 2024](https://www-sop.inria.fr/reves/Basilic/2024/TGSC24/) explores fast procedural terrain through analytical stream-power models. [Controlled Procedural Patterns, CGF 2024](https://diglib.eg.org/items/43319e3d-ba25-4762-971b-ac838ec21fb8) adds oriented erosion-like detail to coarse terrain. [Terrain Amplification using Multi-scale Erosion, SIGGRAPH 2024](https://research.adobe.com/publication/terrain-amplification-using-multi-scale-erosion/) combines artist control with consistent terrain detail across scales.

Recommended pipeline (project design, not a claim copied from one paper):

1. Author an arena-scale traversability and composition mask: spawn clearance, boss footprint, sight lines, elevations and escape routes. Preserve gameplay intent before adding noise.
2. Build mountains from low-frequency ridged fields and directional features; use domain warping sparingly so geology retains coherent orientation.
3. Add catchment/erosion structure and slope-dependent deposits offline or at region generation, with halos large enough to prevent chunk seams.
4. Convert to a solid field with overhang/cave operators where required. Keep geological layers referenced to bedrock and deposition fields rather than blindly assigning every material by world height.
5. Place stone, mud, dirt, litter and grass from slope, moisture, exposure, depth and vegetation influence. Organic litter below a tree should be a spatial consequence of the forest.
6. Distribute tree species and age using deterministic spatial sampling. Generate roots and branch skeletons, voxelize tapered segments, then add clustered leaves with meaningful empty space.
7. Bake lower-resolution render/collision/light representations. Save user damage separately from the seed, with generator version included.

## Simulation and GPU update hazards

These are engineering recommendations inferred from the workload. They require explicit tests in this project.

- **Competing writes:** two grains targeting one empty cell can duplicate, overwrite or lose material. Resolve destinations with deterministic ordering/reservations or an explicit conflict pass. Double buffering alone does not resolve many-to-one writes.
- **Repeated motion:** an in-place update may process one grain multiple times per tick. Track moved/visited state, use a correct traversal order, or use staged updates. Do not claim physical speed from cells per frame.
- **Cross-brick motion:** update neighboring halos and activate both source and destination. Sleeping bricks must wake from edits, neighbor moves, heat and structural changes.
- **GPU synchronization:** a group barrier is not a global dispatch barrier. Separate globally dependent passes; declare RenderGraph read/write resources and handle asynchronous queue fences. Never reuse a staging buffer still being read by the GPU.
- **Stale acceleration:** cached empty space, distance bounds, LODs or occupancy mips may skip newly added material. Invalidate conservatively and use exact local traversal until rebuilding finishes. Removing cells and adding cells have different conservative behavior.
- **Stale lighting:** damage invalidates normals, depth, shadows, GI and temporal history. Use edit-region masks and motion information; a destroyed wall must not remain in lighting history.
- **Readback stalls:** avoid full-world synchronous GPU-to-CPU copies for collision. Keep an authoritative CPU simulation initially or design explicit asynchronous sparse change streams.
- **Structural ambiguity:** touching a wall at one corner should not necessarily support a mountain. Define connectivity (usually face adjacency), anchored bedrock and material strength separately. Local flood fills need a strategy for components escaping the search region.
- **Budget overflow:** defer work with observable queues, retain state and show counters. Never silently discard unfinished grains because a frame budget expired.

## Evaluation matrix before committing the production renderer

| Candidate | Strength | Cost/risk | Decision trigger |
|---|---|---|---|
| Greedy exposed-face meshes | Ordinary Unity graphics, shadows and add-ons; simple inspection | Geometry and collider rebuild spikes under edits | Keep if worst-case arena geometry and changes meet budget |
| Sparse bricks + hierarchical DDA | Cheap local changes; direct material lookup; empty-space skips | Custom depth/shadows/motion and lighting integration | Adopt if edit-heavy benchmark clearly wins at target visual quality |
| SVO / 64-tree | Sparse traversal and LOD; demonstrated independent engines | Traversal divergence, updates, allocation and filtering | Compare on exactly the same volume and edit recording |
| Compressed DAG | Repeated static geometry and huge backgrounds | Mutation complexity and attribute storage | Use when memory savings exceed measured editing costs |
| NAADF | Recent acceleration and dynamic overlay research | Cache invalidation, integration and history complexity | Separate experiment after robust ray baseline |
| Smooth SDF + contouring | Natural silhouette; straightforward smooth terrain shading | Can erase intended cubic detail; sharp materials/LOD require work | Alternative art direction, not an automatic upgrade |

Record hardware, build type, render scale, cell size, resident cells, active cells, triangles, draw calls, bytes uploaded, edit queue length, collider latency and p50/p95/p99 CPU/GPU time. Reproduce idle forest, high camera motion, repeated crater, unsupported canopy, pile settling and boss-sized sweep. A screenshot proves appearance; only this sequence reveals whether the foundation survives combat.
