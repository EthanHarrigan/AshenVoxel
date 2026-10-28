# Decisions and changeable assumptions

## D040 — Progressive cached terrain with bounded admission

2026-10-27 correctness follow-up: render-plan deployment revision
`planner-adjacency-1/1651adfb` uses exact dyadic TileKey adjacency for split
closure. Source generator identity, formats, caps and priorities are unchanged.
The recorded512/30km plan is unchanged;192-cap near detail coarsens at the tested
spawn. This cost was reviewed before guarded integration. Arbitrary-coordinate
rendering is not guaranteed by integer predicate range tests; floating meter
endpoint rounding remains. See [installed evidence and limits](StartupThroughputEvidence/2026-10-27/planner-adjacency/current-status.json).


Reuse the existing one-worker terrain scheduler: publish complete8-column
coverage before escalating visible tiles32→64→128→256. Urgent32m contact work
precedes far detail; outer contact work shares the slot2:1. The immutable source,
10cm matter, renderer and vertex layout are unchanged. Cap total far detail
above proven coarse reserves, reject before vertex expansion/upload, retain
observable deferred demand and back off until capacity/priorities change.
This trades delayed or unattainable pixel targets for bounded mesh payload;
it does not promise maximum detail everywhere. See STARTUP_LOD_PROGRESS.md.

Reserve minimum vegetation LOD cost for every captured snapshot entry before
spending on near detail. Preserve near hard-prop visibility when an impossible
budget cannot fit minima; document that exception. Batch prototype yields and
advance immutable instance snapshots alongside new cell generation, reusing
existing pools. No extra runtime concurrency or persistent cache is justified
by this scheduling bottleneck. Native frame/commit acceptance remains open.

## D039 — Material optics and explicit forest sky fill

Keep ordinary URP meshes, instancing and the authoritative10cm material grid.
Opacity and light transmission are material-wide rendering modifiers, separate
from intrinsic opaque RGB and collision. Dithered coverage is shared across
forward/depth/normals; shadow coverage also responds to transmission. This avoids
per-voxel objects and transparent-face sorting, but does not provide refraction
or physically measured thickness. Distinct-material interfaces retain both faces.

Pack material identity plus2-bit AO into render vertex alpha; terrain retains
its24-byte layout with material ID in UV.w. AVPM4 carries exact IDs and preserves
AVPM3 interaction bytes. Earlier caches map to explicit unknown/opaque material,
not color guesses. Core save Format2 and generator geometry remain unchanged;
append-only Web24 requires new readers when present in saved matter.

The instanced forest cannot depend on absent SH probes. All terrain/prop/impostor
paths receive explicit hemispherical sky/equator/ground light, with bounded AO
attenuation and a user-adjustable shadow-fill strength. This is an inexpensive
ambient approximation, not GI or a claim of real irradiance. Backlighting and
shadows remain directional. See [material/lighting evidence](QualityPatches/patch-5/README.md).


## D038 — Whole-view exposed voxel LODs with compact ordinary URP meshes

Use a25.6m dyadic render hierarchy over the existing implicit10cm material grid.
Near ground tiles resolve individual cells; distant aggregate columns retain
10cm elevation quantization and existing geology/pigment. Source/model identity
is unchanged; voxel LOD cache identity is version1. This does not change finite
Core chunks, snapshot formats, generation5 or authoritative edits.

Keep conventional URP depth/normals/shadows and per-tile frustum culling. Prioritize
a conservative camera cone, retain surrounding coverage for turns/shadows, and
preserve complete old plans until replacements exist. One worker captures immutable
sources; failed requests remain queued. Exact-pigment top/side merging,24-byte
packed vertices and reusable upload buffers reduce waste without reducing nearby
cell geometry. High detail is favored over a low-resolution performance preset.

Limits: face/tile/work caps are not frame-time or total-byte guarantees. Initial
concurrent instances exhausted system memory; revised compact storage passed full
native DX12 fixed views. Long-flight memory admission, motion/FPS, discrete LOD
popping and aliasing remain WV02. [Design/evidence](WORLD_VOXEL_LODS.md).

## D037 — Full-view forests with GPU visibility and indirect drawing

Default forest coverage follows the complete terrain view, including the coarse
horizon, while a fixed sample/tile cap lowers distant detail. Preserve immutable
CPU source/habitat planning and near collision. Supported devices cull individual
stand bounds/ranges in compute, compact indices and supply indirect draw counts;
gameplay performs no readback. Retain CPU instancing as fallback. Source records
are snapshot-relative and GPU rendering applies one render-origin offset.
Native D3D11/D3D12 checks validate the pinned indirect layout, counts, pixels and
rebasing. Approximate stand appearance and GPU/overdraw/FPS admission limits
remain explicit in [the review](ForestGpuReview/README.md).

## D036 — Voxelize source columns into bounded material pages

**Decision:** Exploration uses version1 near10cm material pages derived from the
shared learned source/surface sampler. Retain a one-metre skin and exposed
sidewalls in authoritative VoxelWorld chunks through Set; deep terrain remains
implicit. Keep distant URP height LODs, one worker,49 pages and one upload/Tick.
Only completed pages mask all four proxy render passes. Body support uses the
same cell-centre occupancy independently of render readiness.

**Reason:** The user requested cubic terrain using the existing materials.
Converting underlying authority preserves layers/pigment and avoids stale display
meshes or full sea-to-summit voxel allocation. The old renderer remains available
through EnableVoxelTerrain and serves complete fallback coverage.

**Limits:** This near conversion is not full-world editable/persisted matter,
caves, microgeology or simulated terrain. Native movement/transitions/performance
require acceptance; conversion does not improve learned source resolution.
See [voxel terrain](VOXEL_TERRAIN.md).

## D034 — Separate stick resistance from visual matter

Exploration AVPM0003 separates blocking spans, soft tissue and thin wood. Preserve
original10 cm meshes, material IDs and Format2 saves. Use an offline occupancy
heuristic and retained upright leader; slender trunks must not become passable
branches. Fractional body/span volume controls bounded walking resistance, with
one query per tick. Historical packs retain old policy until rebuilt. This is
not exact branch-diameter metadata. See [evidence](NativeLODReview/README.md).

## D035 — Prototype imagery and bounded approximate stands

Retain ordinary URP near meshes and original LODs in the pack. Reversible far
multi-view impostors feed a capped asynchronous immutable-source stand layer;
distant imagery never registers collision/simulation. Baked lighting and plane
depth/approximate normals are explicit tradeoffs. Fixed native captures support
the clarity change; motion/GPU/overdraw remain acceptance gates. No source terrain
resolution changes. [Review and comparison controls](NativeLODReview/README.md).

These decisions explain what the project is built around and where another choice can be substituted. A provisional answer is a starting point for implementation, not a request for the user to answer questions now. No earlier Unity project conversations were visible in this workspace.

## D001 — Start from a supported Unity LTS release

**Decision:** Pin **Unity 6.3 LTS, 6000.3.27f1**. The official [release notes](https://unity.com/releases/editor/whats-new/6000.3.27f1) list 20 October 2026; the [support policy](https://unity.com/releases/unity-6/support) identifies 6.3 as the latest LTS at this project's research date, with support through December 2027. The selected patch also belongs in `ProjectSettings/ProjectVersion.txt`.

**Reason:** This is a project expected to grow over months, with editor extensions and third-party functionality. A fixed LTS support window and controlled upgrades fit that work. Unity also describes its Update releases as production-ready; choosing LTS does not imply that every newer feature release is unstable.

**Alternatives:** Current non-LTS feature release for a required feature; older supported LTS when a critical native plugin demands it.

**Change condition:** A necessary dependency or measured engine defect justifies migration. Upgrade in a branch, open a copy of the project, record package changes, run core checks, load existing saves, and replay benchmark scenes. Do not upgrade editor and voxel representation simultaneously.

**Important limit:** No Unity version guarantees that arbitrary addons work. Each addon needs a current Unity/pipeline/platform support check. Do not install a collection of unrelated addons as a substitute for designing ownership boundaries.

## D002 — URP first

**Decision:** Conventional Unity rendering through URP for the first prototype; use RenderGraph when adding custom rendering features. Unity's [2026 pipeline strategy](https://unity.com/topics/render-pipelines-strategy-for-2026) and [URP compatibility-mode guidance](https://docs.unity.com/en-us/engine/6000.0/manual/render-pipelines/universal-render-pipeline/customizing-urp/compatibility-mode) support this direction for a new project.

**Reason:** Straightforward lit mesh integration, shadows, broad tooling compatibility, and room for a specialized voxel rendering pass later. This lets the simulation become testable before custom GPU infrastructure becomes the whole project.

**Alternatives:** HDRP for a high-end PC-only presentation; a custom SRP for a specialized renderer; built-in pipeline for legacy dependency requirements.

**Change condition:** A controlled art/performance scene demonstrates that the intended lighting cannot reasonably be delivered through the chosen pipeline. Evaluate shader, depth, motion-vector, and shadow integration costs before changing.

## D003 — Material grid is authoritative

**Decision:** Discrete material cells are the source of truth. Meshes and physics proxies are derived.

**Reason:** Digging, granular gravity, procedural strata, connectivity, saves, and future reactions need persistent matter. Surface triangles alone do not describe material inside the ground.

**Alternatives:** Signed-distance terrain with separate material labels; a hybrid SDF terrain and explicit particulate layer; mesh destruction for isolated props.

**Change condition:** The intended surface style becomes smooth eroded rock rather than visible fine-scale discrete matter. An SDF branch must still define material ownership, granular conversion, and persistence.

## D004 — 16³ sparse chunks first

**Decision:** Dense byte material arrays inside lazily allocated chunks, with absent chunks representing air.

**Reason:** Simple local indexing, small update regions, easy tests, and a useful future brick size experiment. A 16³ material payload is4 KiB before object overhead; an optional packed-color channel adds16 KiB to a tinted chunk.

**Alternatives:** 8³ bricks for finer dirty regions; 32³ chunks for less dictionary overhead and more coherent meshing; sparse voxel octrees; palette-compressed or uniform chunk variants.

**Change condition:** Measure occupancy, mesh rebuild time, dirty amplification, traversal lookup cost, and metadata overhead on real scenes. The CPU chunk size does not have to equal a future GPU brick size.

**Limit:** A material byte alone is not the full memory cost. Dictionary entries, arrays, meshes, collision, active queues, and future channels can dominate.

## D005 — Finite domain before streaming

**Decision:** Begin with a deliberately limited demonstration world and bounded coordinates.

**Reason:** A fixed domain makes generation, editing, persistence, and simulation measurable. It also prevents a first demo from hiding an unbounded residency problem.

**Alternatives:** Infinite signed-coordinate chunk streaming from day one; large hand-authored arena with a voxel destruction layer.

**Change condition:** Core behavior passes tests and a profiler baseline exists. Introduce chunk residency states, asynchronous generation, dependency halos, revision cancellation, and persistent deltas together. Do not merely stop rendering far chunks and call it streaming.

## D006 — One voxel per 10 cm is the current requirement

**Decision:** Use one voxel per **10 cm** (`0.1 m`) for the current world, generation, simulation, collision, and display. This is an explicit user requirement. The core may remain parameterized for experiments, but finer cells are not the production assumption.

**Reason:** The user selected a concrete physical scale. Keeping matter, collision, generation, and presentation on the same 10 cm grid provides an understandable baseline and avoids silently multiplying costs. Halving cell size would increase full-volume cell count eightfold at constant world dimensions.

**Alternatives:** Optional later experiments include finer cells, shader detail below a cell, or selected higher-detail assets. None changes the current 10 cm material-grid requirement without an explicit revised decision.

**Change condition:** Revisit cell size only if the user changes the requirement, then benchmark the same physical scene and camera path. Shader surface detail can improve the image without changing the physical cell grid.

## D007 — Greedy meshing is the reference renderer

**Decision:** Build only exposed faces, merging compatible coplanar regions.

**Reason:** This is understandable, editable, compatible with Unity mesh tools, and useful as an oracle when evaluating a GPU brick renderer.

**Alternatives:** Sparse-brick DDA ray traversal, octree traversal, surface nets, dual contouring, or a hybrid.

**Change condition:** Meshing and upload latency or microdetail triangle count becomes the measured bottleneck. A replacement must match authoritative edits, depth, shadows, material appearance, and camera motion before the baseline is removed.

**Limit:** Greedy meshing does not solve lighting, ambient occlusion, microdetail aliasing, or chunk LOD. Surface variation that changes merge keys reduces merging.

## D008 — Active granular motion before structural physics

**Decision:** Implement local granular gravity as the first material simulation. Keep coherent wood and stone as a separate future support/fracture problem.

**Reason:** A small set of local rules can show excavation and settling without creating thousands of rigid bodies. Separating coherent matter prevents the false claim that sand rules are a full structural solver.

**Alternatives:** Everything falls as grains; all detached regions become rigid islands; position-based particles; continuum simulation.

**Change condition:** The prototype's editable ground is stable and the gameplay needs falling trees, arches, or rock slabs. Add bounded connectivity analysis and simplified rigid islands with separate tests.

**Limit:** A floating tree after its base is removed is a known feature gap until structural support exists. Density and flammability metadata alone do not create physical behavior.

## D009 — Budgeted processing with explicit determinism scope

**Decision:** Cap per-step candidate processing and per-frame chunk rebuilds; record simulation stepping assumptions.

**Reason:** A local edit can activate substantial work. Bounded scheduling protects responsiveness and makes overload visible as backlog.

**Alternatives:** Complete all work synchronously; use a fixed maximum simulation region; strict lockstep with fixed work schedules; parallel domain updates with deterministic conflict resolution.

**Change condition:** Use performance measurements to choose budgets. A time budget measured by a wall clock is useful for responsiveness but is not a deterministic simulation schedule. Replay requires fixed tick/command order and controlled work progression.

**Limit:** A count budget is not a millisecond guarantee. A mesh rebuild can vary enormously in cost. Queue age, worst-item cost, and collision lag remain required metrics.

## D010 — Direct grid traversal for targeting

**Decision:** Use a voxel DDA query for digging and placing.

**Reason:** Grid queries see an edit immediately, even if a mesh or MeshCollider is waiting to rebuild. They also return material identity and the adjacent cell naturally.

**Alternatives:** Unity Physics raycasts with immediate collider updates; a GPU picking buffer; analytic signed-distance intersection.

**Change condition:** A more complex representation demands a different query. Keep an authoritative CPU query for gameplay or define a robust asynchronous picking contract.

## D011 — Snapshot first, journal later

**Decision:** Versioned snapshots are the first persistence format. Validate dimensions and data before installing a loaded world.

**Reason:** Snapshots establish roundtrip correctness and are easier to inspect than incomplete replay logs. An integrity checksum catches accidental damage.

**Alternatives:** Seed plus edit journal; chunk database with checkpoints; append-only command log.

**Change condition:** World size makes full snapshots too costly. A production format must include generator/material versions, chunk checksums, atomic writes, migration rules, and a recovery test.

**Limit:** A checksum is not cryptographic authentication. A deterministic seed does not reproduce a modified world without state or a complete controlled replay.

## D012 — First-person tooling camera is provisional

**Decision:** Use a simple direct-control demo camera to inspect terrain, cuts, materials, and settling.

**Reason:** The first prototype must make foundation defects easy to see. The final combat camera and movement system can be evaluated separately.

**Alternatives:** Third-person Souls-style camera, lock-on combat, controller-first input, cinematic arena camera.

**Change condition:** World interaction is stable enough to evaluate real combat readability. Character movement, camera collision, lock-on, stamina, animation, and enemy behavior need a focused vertical slice.

**Limit:** A first-person demo is not a commitment to a first-person final game.

## D013 — PC and single-player first

**Decision:** Plan the first quality/performance baseline for a desktop PC and a single authoritative simulation.

**Reason:** Target hardware and networking requirements were not supplied. This is the narrowest practical first implementation without hiding future cost.

**Alternatives:** Console-first budgets, Steam Deck-class hardware, mobile, cooperative multiplayer, dedicated servers.

**Change condition:** Before committing to production density and graphics, select a minimum CPU/GPU/RAM target, display resolution, target FPS, and whether networking is required. The code should expose commands and data ownership now, but that does not make multiplayer free later.

## D014 — No one-object-per-voxel representation

**Decision:** Voxels are compact data. Only chunks, coherent moving islands, characters, and bounded visual effects become Unity objects.

**Reason:** Per-cell Transform, Renderer, Collider, and Rigidbody overhead is incompatible with large fine-scale volumes.

**Alternatives:** Individual objects for a tiny editor gizmo or a deliberately bounded visual effect.

**Change condition:** None for the authoritative world. Small temporary effects may use pooled objects within an explicit count cap.

## D015 — Art direction is authored, placement is procedural

**Decision:** Use **Avroid as the primary visual reference** named by the user. Guide generation with an intentional arena layout, terrain composition, material palette, and tree silhouettes; retain the earlier dark fantasy/Souls-like mood as a secondary direction. This is an appearance reference, not a claim that Avroid uses the same implementation.

**Reason:** Unconstrained noise does not create readable combat spaces or a coherent dark fantasy scene. Microvoxels should support the atmosphere rather than become the main visible motif.

**Alternatives:** Fully procedural exploration world; authored landscape with localized voxel destruction; stylized block aesthetic.

**Change condition:** Playtests show whether boss encounters require more authored control. Generation masks and seeds should allow the same material foundation under either workflow.

## D016 — Addons enter through boundaries

**Decision:** Introduce addons one at a time with a stated owner and integration boundary.

**Reason:** An addon for input, camera, animation, vegetation, networking, or world generation can silently duplicate a responsibility already owned by the core. Clear boundaries preserve the foundation.

**Practical checks:** Confirm editor/pipeline/platform compatibility, source availability or vendor support, redistribution license, runtime allocation behavior, build size, and a clean rollback path. Keep third-party code separate from the voxel core.

**Alternatives:** A complete terrain/destruction middleware stack if its source, material model, save format, and performance align with the project.

**Change condition:** A measured need is better met by a supported dependency than by continued in-house development. Do not make the authoritative save format depend on opaque editor-only assets without an export path.

## D017 — Reference behavior before optimized behavior

**Decision:** Keep a small, readable CPU implementation and deterministic fixtures while evaluating jobs or GPU alternatives.

**Reason:** A fast result that sometimes loses matter, writes stale meshes, or changes edit semantics is not a stronger foundation. A reference implementation provides an observable correctness target.

**Alternatives:** GPU-first implementation with a separate offline reference model.

**Change condition:** The reference becomes too slow for development scenarios. Retain it for small regression fixtures even after replacing the interactive backend.

## Decisions intentionally deferred

- Exact minimum hardware, target resolution/FPS, and quality tiers.
- Final first- or third-person combat camera and input scheme.
- Active simulation radius and distant world representation. Cell scale is currently fixed at 10 cm by the user.
- Structural support/fracture model and whether terrain can form long-lived moving islands.
- Water/pressure model, heat transfer model, and material chemistry complexity.
- Final rendering backend and indirect-lighting method.
- Multiplayer authority and determinism requirements.
- Art content production workflow and addon shortlist.

Each deferred choice can be revisited independently. The core material grid, explicit coordinates, tested edits, and versioned persistence are intended to make those revisions manageable.

## D018 — Shared tree genome and persistent pigment

**Decision:** Use a shared bounded branch-skeleton/raster pipeline with species presets and independent intrinsic color channels. Birch, oak, redwood and pine differ through parameters, bark/foliage strategies and palettes. Artist profiles expose those controls explicitly.

**Reason:** Species fidelity comes from branch habit, scale, canopy organization and pigment rather than duplicating four generators or using four unrelated meshes. Saved tint is separate from material behavior and lighting.

**Alternatives:** Space colonization for crown filling, competition-based growth, authored/voxelized tree assets, or hybrids. The reference account's handmade/voxelized assets remain a valid comparison rather than proof that every tree must be generated procedurally.

**Change condition:** Actual-data gallery and player profiling show where constructive rules lack ecological adaptation, desired silhouettes, runtime speed or manageable triangle count. A replacement growth strategy should still emit the same branch/material/appearance contracts.

**Limits:**10 cm represents leaf/needle aggregates and exaggerated acorns. Skeletons do not automatically supply structural collapse or wind. Fine per-cell colors can reduce greedy merging. See [TREES.md](TREES.md).

## D019 — Mature coast redwood scale and physical basal detail

**Decision:** Generation5 replaces the26 m redwood demonstration preset with a nominal75 m mature coast redwood. Keep the shared genome/skeleton/material pipeline and10 cm cells. Separate broad woody root flutes from smaller longitudinal bark relief; both affect occupancy. Make unequal wandering ribs and a high irregular crown rather than copying pine whorls or stretching a small tree vertically.

**Reason:** The user explicitly requests accurate mature height and a beautiful ridged basal outline. [Botanical evidence](REDWOOD_REFERENCE.md) supports the selected scale and distinguishes coast redwoods from giant sequoias. Closely sampled taper and slab-based trunk rasterization preserve the outline without repeatedly scanning large overlapping endcaps.

**Alternatives:** Smaller young redwoods through age/dimensions; exceptional100 m-plus specimens with deliberate work budgets; authored scanned/voxelized hero trunks; reiterated ancient crowns and fire hollows as later shape extensions.

**Limits:** A static stylized specimen does not model centuries of growth. A75 m tree is much more expensive than an arena sapling; generation is synchronous and count budgets do not guarantee frame time. Workshop domain/framing, candidate counts, truncation and actual-data full/base images must be validated together. Existing snapshots retain their original matter and generator provenance rather than silently regenerating trees.

## D020 — Global terrain fields and local matter

**Decision:** Add a separate engine-independent WorldScaleData assembly with double world positions, signed64-bit cell keys, immutable height/biome fields and implicit buried stone. Keep the finite arena Core and its existing snapshots unchanged. Materialize local10 cm matter deliberately; do not allocate columns from sea level to every mountain summit.

**Reason:** The user prioritizes multi-kilometer relief and long observations over deep caves. A100 m-radius footprint filled20 m deep already costs about2.93 GiB raw with current colors. Height, horizontal world size, landmass zoom and voxel size must remain independent.

**Limits:** Implicit sampling and bounded sparse edit data are foundation contracts. The explorer's finite temporary inspection region does not implement persistent continuous editable-region migration. See [world-scale research](WORLD_SCALE_RESEARCH.md).

## D021 — Use the original InfiniteDiffusion pipeline behind a provider

**Decision:** Integrate the pinned upstream pretrained pipeline through a separate local Python service, immutable jobs, bounded single-owner inference scheduling, asynchronous region requests and float-meter interchange. Support the original lazy signed-region generation and finite/periodic conditioning; retain all five climate channels. A separate analytic fallback is never labelled neural output.

**Reason:** This preserves the model and generation method while keeping heavyweight inference dependencies out of Unity assemblies. The user's transferred modified mod supplies terrain/map/cache design lessons; unrelated settlements, roads and buildings are excluded. Keep source/model/statistics/runtime hashes in identity, and require full source readiness before creating local neural matter.

**Evidence-driven adjustment:** Actual raw model crop queries differed by up to0.176 m at their edges, exceeding one10 cm voxel. The provider assigns each pixel a fixed padded owner query and freezes owner outputs in a bounded checksummed cache. Actual canonical overlap and disk replay checks passed; raw-query failure remains recorded. This is not proof of cross-device bit identity or persistent world-save semantics for evictable caches.

## D022 — Height proxies for distant vistas

**Decision:** Use a bounded balanced quadtree with conservative height bounds, tile-local float geometry and a rebased camera frame. Keep old complete coverage until a new plan is ready. Skirts cover prototype proxy cracks; ordinary Unity frustum/depth culling remains available. Model regions refine bounded nearby proxies asynchronously; far rough previews stay explicitly provisional.

**Reason:** The requested vistas need a representation whose cost falls with distance and altitude. Atlas min/max queries preserve narrow peaks that a few sampled heights miss. Transvoxel is not a drop-in seam algorithm for the existing greedy cube surface.

**Limits:** The default proxy's finest sample spacing is2 m; a100 m radius of full10 cm geometry remains a profiling target. Native Unity/GPU testing, persistent near-field streaming, vegetation LOD and watertight hybrid transitions remain distinct gates. See the [implementation plan](WORLD_GENERATION_PLAN.md).

## D023 — Shared static surface columns

**Decision:** One versioned `SurfaceMaterialSampler` supplies surface geometry, material layers and intrinsic pigment to implicit matter, proxy vertices and finite local voxels. Derive terrain attributes at fixed physical scales; reuse each column across its vertical cells. Use source biomes for ground, independently of vegetation overrides. Add coherent bedrock provinces and sediment/organic horizons rather than independently randomized cell materials.

**Reason:** LOD triangle normals and vegetation overrides must not change authoritative geology or snow. A reusable column bounds classification cost and keeps local cutaways consistent with distant appearance.

**Limits:** Static empirical rules informed by research are not erosion, drainage, snow-energy or sediment-transport simulation. Fine sampling of coarse fields does not add measured microtopography. See [surface report](SURFACE_MATERIALS_UPDATE.md).

## D024 — Positive-volume snow and append-only materials

**Decision:** Snow adds quantized10 cm depth above bare terrain and becomes actual Snow cells locally. Climate, slope, curvature, wind shelter, aspect and patch variation control static retention. Surface profile identity includes all settings. Expand the catalog append-only to22 IDs, preserving existing IDs/colors and Format2 snapshots; keep Core Generation5 unchanged and introduce SurfaceProfile version1.

**Reason:** A white coating cannot represent excavatable variable-depth snow or exposed mountain stone. Separating catalog, surface-generation and save-container versions avoids silently changing old worlds or unnecessarily invalidating unrelated tree generation.

**Limits:** No avalanches, melting or conserved transported snow; hardness is a game threshold. Older readers reject new material IDs. Snow maximum depth expands conservative LOD bounds.

## D025 — Explicit physical climate units

**Decision:** New immutable model jobs use `physical-si-v2`; convert upstream seasonality from centi-C exactly once before canonical caching. Retained legacy jobs keep native units and identity, with explicit consumer conversion. Pair baked physical climate with exact height hash/grid/world identity; apply lapse correction only to additional vertical rescaling.

**Reason:** Actual outputs revealed a unit mismatch that transport-only tests could not detect. Source surface temperature already contains its original elevation adjustment. Range guessing and double conversion would make biomes and snow incorrect.

**Evidence:** Fresh model heights and four climate planes were bit-identical; seasonality matched prior output ×0.01. Both legacy and new live paths passed the actual C# consumer checks. See [climate units](CLIMATE_UNITS.md).

## D026 — Visual sea surface before fluids

**Decision:** Keep fake water separate from material authority: one bounded camera-relative sea mesh, URP surface shader and conditional underwater color wash. Reuse immutable local source data for the camera bed, invalidate hints on remote arrivals and own mutable material copies.

**Reason:** The requested visual water can improve ground-level shores without introducing unimplemented flow, pressure or conservation claims into the voxel core.

**Limits:** No ocean connectivity, river/lake solving, buoyancy or water cells; below-sea depressions may appear flooded. Offline gallery water is an optical study, not Unity shader proof.

## D027 — Shared plant organs and separate habitat placement

**Decision:** Add one bounded constructive plant generator with ten morphology presets, shared Leaves/Wood/Fruit and only appended Fungus/Petal materials. Keep global seeded candidate ownership and source-biome habitat rules in WorldScaleData; core shape generation remains engine-free. Nearby finite detail becomes actual10 cm matter through Set.

**Reason:** Organ arrangement and negative space distinguish ferns, shrubs and grasses more reliably than new material IDs. Separating morphology, habitat and renderer allows new species or distant representations without changing material saves.

**Limits:** Static organ construction is not growth, ecology or canopy-light simulation. Candidate ownership is stable across complete region queries, but finite non-overwriting raster composition is not yet persistent cross-region streaming. Center exclusion does not prove non-overlapping leaf envelopes. See [ground-cover update](GROUNDCOVER_UPDATE.md).

## D028 — Weathered shape at the voxel scale

**Decision:** Generate five lithology habits with anisotropic rounded/cut caps, coherent joints/bedding, bounded palettes and connected buried column roots. Keep geometry, material behavior and pigment separate; preserve existing terrain by default.

**Reason:** Readable facets and edge relief must survive10 cm rasterization. Subcell noise and randomly colored cells do not produce a convincing weathered outcrop. A connected constructive reference is easier to validate than an unbounded physical erosion solve.

**Limits:** Solid-column construction cannot make arbitrary overhangs or cavities. Static weathering is not dissolution, fracture or moisture transport. Count budgets expose work, not millisecond guarantees.

## D029 — Soft matter and body collision are separate queries

**Decision:** Leaves, Fungus and Petal remain solid authoritative matter for tools/meshes, but the default GridWalker body policy passes through them. Wood and terrain still block. An Inspector flag restores solid-foliage collision.

**Reason:** Reusing tree Leaves for fern tissue must not turn small ground plants into player-sized walls or require duplicate material identities. A caller-selected body policy keeps picking/destruction semantics intact.

**Limits:** Tree Leaves are also passable by default, while branches remain solid. This is a simple material-level policy, not object-specific collision categories, bending vegetation or native-controller validation.

## D030 — Rock silhouette precedes weathering detail

**Decision:** RockGenerator2 uses six explicit morphology families independent
of rock material, with seeded material-biased Auto selection. Construct
irregular polygonal pieces and cap planes instead of a shared radial envelope.
Retain bounded weathered bevels, coherent pigment and buried connectivity.
GroundCoverProfile2 preserves shape-specific height/width/depth ratios; the
workshop can compare identical material, dimensions and seed across six shapes.

**Reason:** Version1 remained nearly elliptical even after adding joints and
broken shoulders. The user's correction requires primary silhouette variety.
Matched-material pictures and exact footprint/occupancy checks distinguish
geometry changes from color, camera or labels.

**Limits:** Connected buried cap columns do not generate arbitrary undercuts
or arches; exposed split lobes can connect below ground. Zero burial needs a
visible digital connector. Existing snapshots retain saved geometry. See
[rock shapes](ROCK_SHAPES_UPDATE.md).

## D031 — Exploration uses shared immutable terrain and prototype instances

**Decision:** Make Exploration the first-open/default build scene, with bundled
actual learned data and no edit input. Bind one immutable/composited terrain
authority to proxy rendering, continuous player collision and habitat placement.
Stream shared10 cm prop prototypes; retain hard-material spans independently of
visible LOD. Keep full-scale age-specific prototypes instead of scaling voxel
size. Separate forest species, age and density fields.

**Reason:** The requested first walk/fly view needs broad terrain coverage and
coherent forests without allocating a300 km volumetric grid or blocking on
model inference. Smooth field walking avoids individual voxel steps; soft
foliage passes while wood/rock remain solid. The original destruction arena and
matter authority remain available separately.

**Limits:** Exploration terrain is a0.5–1 m near height proxy, not continuous
10 cm destructible matter. Prototype upload, native GPU memory and frame time
still need target-machine profiling. See [guide](EXPLORATION_GUIDE.md).

## D032 — Decode ahead without changing occupied terrain

**Decision:** Composite immutable verified5 m patches outside a512 m observer
guard plus query/prop margins. Apply protection to activation, replacement and
eviction. Snapshot each worker/mesh job's source revision; invalidate intersecting
terrain/population caches after safe publication. Keep the baked source when
the optional local service is off or unavailable.

**Reason:** View distance, movement speed and inference latency must not move
ground beneath the player, splice source revisions into a mesh or invalidate
retained collision samples. Exact model/runtime/coordinate identities matter
more than a visually similar height preview.

**Limits:** CPU generation may lag fast flight. Bounded caches do not retain
decoded detail across the whole authored world or provide persistent terrain
saves. See [live refinement](EXPLORATION_LIVE_REFINEMENT.md).

## D033 — Explicit nonnegative physical-climate publication

**Decision:** Wrap original inference before canonical owner encoding with
`physical-nonnegative-v1`, changing only negative seasonality, precipitation and
precipitation CV to zero. Preserve height, signed temperature, lapse, positive
channels and original source. Give this derived view/cache an explicit identity,
record correction counts/raw hashes, and retain the original native job history.

**Reason:** Actual model dry regions can predict negative physical channels;
strict transport correctly rejected them, which previously stopped optional
whole-world refinement. A hidden repair after cache decoding would be too late
and would violate the cache identity contract.

**Limits:** This is a declared physical publication constraint, not climate
retraining or a new native model execution. Nonfinite and upper-bound invalid
outputs still reject. See [provider evidence](../Tools/ExplorationBake/README.md).

## QP-D01 - Globally anchored procedural surface relief

A derived surface-relief-v1 field adds at most 0.63m displacement above the
unchanged learned source. Explicit seed/version identity, conservative bounds and
immutable worker snapshots; collision and vegetation share the field. Shore/ocean
heights remain unchanged. This avoids a full-world learned bake for small earth
forms; it does not manufacture learned source detail. Contact meshes publish
independently with corresponding depth/normal/shadow masks. Distant terrain stays
until replacements are ready; memory admission retains observable pending work.


## PERF-D01 — Measured CPU improvement before renderer replacement

Keep byte comparison for MaterialId sorts and opt-in two-worker column sampling
in the existing builder. Use immutable source snapshots, disjoint row writes,
one tile in flight and unchanged face order/cancellation. Small/default public
calls stay serial. Native candidate build work fell56.7%; exact retained-source
and runtime checks pass. No geometry version change is needed for identical
output. GPU quad buffers remain a tooling prototype: payload is73.3% smaller,
but draw speed is mixed and integrated lifecycle/material parity is unverified.
Avoid extra runtime packages, tile caches, queues and renderer variants without
measured benefit. See [performance results](WORLDGEN_PERFORMANCE_RESULTS.md).

### Movement slope policy follow-up — 2026-10-27

Keep exact globally anchored10cm contact. Use an optional continuous terrain
slope provider only for walkability/slide classification; legacy custom collision
sources keep their original probes. A failed provider is unavailable, not flat.
This separates the intended hillside angle from the artificial treads/risers.
Increasing the50-degree slope limit or reducing terrain resolution would hide
the quantization error and alter unrelated behavior; neither was adopted.
No speed/acceleration preference was inferred from the broad better-feel request.
No geometry/save/generator change; native interactive acceptance remains open.

Grounding alternatives remain under test: center-only anchoring is fast but leaves
metre-scale downhill root gaps; one guessed global sink buries flat-site models;
per-instance root meshes add generation/collision work. A cached convex hull for
each actual basal-height layer needs at most25 support samples in the current
76 tree/rock prototypes and exactly matches all-point planar seating in2280
cases. It matched16 fixed14fb32 placements, but sampled nonplanar correctness is
not a proof for interior pits. Downward translation increases uphill burial;
cliff rejection and actual exposed-column checks are required. A model top below
center terrain is not proof of complete burial (a tested slab still exposes7/30
columns). This candidate changes population placement/version, not AVPM geometry.

### Shape-aware population seating candidate — 2026-10-27

Cache immutable LOD0 basal Wood/rock hulls by height layer before releasing mesh
data, then lower upright instances using at most 32 sampled locations. Keep X/Z,
yaw, scale and stable keys; rendering and hard collision consume the same seated
instance. Real basal-column centers alone certify remaining exposure, sampled at
the global 10 cm terrain cell centers. Hull corners constrain support but cannot
certify material above terrain: review reproduced a narrow corner dip that would
otherwise admit a fully buried rock. The corrected regression now rejects it.

Reject excessive sink/relief or insufficient exposure rather than modifying the
prototype shape. This increases uphill burial and may conservatively omit a rock.
Current 76 hard prototypes require at most 26 locations; actual-world sample
accepted 12/16, with four exposure rejections. Centers include snow for exposure;
tree support still uses bare terrain. Bounded nonlinear sampling cannot prove
interior support everywhere. No per-instance mesh, geometry rebake or save change.

Use population version 2 with immutable metadata, density, seed, policy and source
identity. Legacy AVPM1–3 lack exact material identity and explicitly retain v1
center placement; they must not report Ready while silently rejecting all trees.
The candidate preserves existing streaming scheduling/budgets and exposes rejected
candidate counts. Native view/collision/LOD and startup-cost gates remain pending.


## Replacement coordination decisions - 2026-10-27

New ecology/art/material/movement requests are proposed backlog, not installed behavior. See latest TODO continuation. Preserve source/version/geometry/appearance boundaries; research is pending from parent ecology/geology/model-reference workers. No asset purchases or copying unlicensed models. Titanfall-inspired movement is a tunable feel experiment, not a fixed mechanic mandate. Subjective fun needs actual Play routes.

The user authorizes measured simplification and recoverable removal. Verify dependency/use, preserve readable hash-backed rollback preimages/inventory and user edits, review narrow diffs, then regression-test before replacement. Unknown concurrent changes require reconciliation. No permanent unrecoverable deletion, broad unreviewed cleanup or silent destruction of substantial working capabilities.

Grounding freeze independently matches7manifest files and71unique CPU/API dependencies. One-time metadata CPU cost26.29ms across76models, worst13.14ms observed record; this is cooperative startup work, not a strict2ms guarantee. Native fixture remains unexecuted. Fresh09:45UTC commit headroom4.518GiB is below its proposed5.5GiB initial guard; no launch authorized from stale/free-physical-RAM observations.


## Art/ecology integration brief - proposed, 2026-10-27

These are parent-reviewed design choices and research inputs, not implementation or independent native acceptance. Improve the current conifer/rock/grass set before multiplying species. Initial communities: Rocky spruce/fir with aspen disturbance patches; cool maritime Sitka/hemlock with fern/moss; warm beach/palm hinterland only where climate supports it. Blend habitat suitability and patch occupancy at ecotones, not species geometry or indiscriminate RGB values.

Seed hierarchy: world -> stable region/island -> stand -> individual. Never key it by request/load order. Region shifts correlated growth/form distributions, stand controls age/crowding, individual residual stays modest. Derive height, breast-height diameter (DBH), crown width/base and root flare jointly. DBH is not trunk-base diameter. Distinguish common, rare old-growth and exposed/krummholz cohorts. At10cm resolution,30cm DBH is only3cells: preserve connected proportion and chunk-seam determinism. Do not combine separate tallest/thickest records into one routine tree.

Parent-supplied primary size evidence (observed extremes are NOT proven biological maxima): Engelmann favorable dominants14-40m, DBH.38-.76m; separate recorded individuals59.9m/1.24m and38.8m/2.27m. Subalpine fir usual14-30m/DBH.30-.61m; records50.3m/.81m and42.1m/.66m. Sitka records96m/3.05m and separate77.8m/4.36m. Aspen20-25m typical with36.5m potential reference. Coconut25m typical/30m exceptional and4-6m fronds remains a research input requiring its primary source verification before parameter adoption.

Primary references: [Engelmann spruce](https://research.fs.usda.gov/silvics/engelmann-spruce), [subalpine fir](https://research.fs.usda.gov/silvics/subalpine-fir), [Sitka spruce](https://research.fs.usda.gov/silvics/sitka-spruce), [aspen](https://research.fs.usda.gov/silvics/quaking-aspen), [UBC measured trees](https://bigtrees.forestry.ubc.ca/bc-bigtree-registry/top-30/).

Grounding gate: measure maximum support gap AND uphill burial against actual connected basal geometry. Bounded seating/root-flare correction must respect species/shape tolerance; impossible placement should reject/reposition deterministically. No giant flat earth pad. Trees generally upright; support-oriented rocks are a separate decision. Existing frozen candidate remains CPU/API-tested, not adopted by this brief. Coordinator requested full-footprint gap/burial distributions before main acceptance.

Mountain classification: coherent lithology volumes (gray granite, darker gneiss, localized dikes) followed by distinct weathering/soil/vegetation/snow masks. Avoid global altitude stripes. Talus aprons/fans belong below source cliffs and match source lithology. Distant talus stays stable geometry/instances; only a bounded disturbed nearby subset tumbles. Snow caps upward/exposed canopy surfaces with sheltered green undersides; latitude biases climate rather than blanket white shading. Require seam/reload determinism. [Tetons geology](https://www.nps.gov/grte/learn/nature/geology.htm), [Tetons plants](https://www.nps.gov/grte/learn/nature/plants.htm).

Art grammar: connected tapered trunk, primary forks, secondary branches and asymmetric foliage shelves/lobes with negative space. Preserve silhouette at LOD rather than just triangle ratios. Moss/snow reuse base species identity. Use bounded coherent palette patches, not independent rainbow noise. New materials require stable IDs and backward compatibility.

Visually inspected references supplied by parent, reference only: [mountain forest](https://starvingfoxstudio.itch.io/voxel-mountain-forest) exposed trunks/irregular shelves; [meadow](https://starvingfoxstudio.itch.io/voxel-meadow) broad/white-bark contrast; [island](https://maxparata.itch.io/virginisland) arching palm fronds; [cold biome](https://makovice.itch.io/cold-biome-voxel-asset-pack) broken snow clumps; [plants](https://mrmgames.itch.io/plants-pack-3d-voxel-low-poly-models) understory leaf architecture. Conflicting license labels/terms prohibit assuming reuse rights; no purchase/download/reuse is authorized.

Finite budgets cannot promise maximum detail everywhere. Acceptance is no persistently incorrectly coarse visible demand within required error tolerance when capacity is available, plus graceful error/distance allocation under genuine pressure. Test net capacity release independently of cap increases. For32M/40M compare native frame-time percentiles, VRAM and stop-to-detail;388->6063D trees is potential visual benefit, not generation speedup. Foliage and dense destruction share a measured frame budget.
