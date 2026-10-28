## Current controls — 2026-10-28

Press Play in the existing Exploration scene. Aim at wood/rock within5m and use
LMB; T is no longer required. All16 baked tree and60 rock recipes are supported,
except adapted grounded-root variants. First preparation runs asynchronously; keep
the target nearby while it loads. Detached sections fall together, then release
weighted debris. Nearby pickup starts enabled; I toggles it, G kicks, O restores.
The HUD counts collected materials for this session.

This is still one active destructible prop: O, leaving24m or stopping Play restores
the object and discards inventory. Terrain excavation and persistent inventory are
unfinished. Large-tree responsiveness and full-scene performance remain unaccepted.
Terrain now predicts travel, requests nearby5m-spaced learned source, refreshes
affected LODs and retires distant detail; full-view5m coverage is not complete.

Details and verification: [current implementation](Docs/CHEAP_MATTER_AND_REFINEMENT_2026-10-27.md).

### Historical beta guide below (current controls above take precedence)

## Beta handoff - 2026-10-27 23:11 UTC

Unity is in **Edit mode** in `Assets/Scenes/Exploration.unity`. Press Play. Your
saved valley spawn and exact saved preferences have been restored. Development,
recording and suction are stopped. No scene, world data or prototype rebake needed.

**Movement:** WASD, Shift faster, Space jump, F walk/fly, Q/E or Ctrl/Space down/up
in flight, N noclip while flying, Escape pointer, F1 settings. Click the Game view
to focus input. Movement fixes are installed; responsive traversal is an F1 opt-in.

**Settings to try:** F1 has Normal / Detailed / Maximum terrain quality, fine-detail
reach, plan allowance and far-mesh allowance. Start with Normal if your saved
Maximum is slow. Normal resets terrain tuning. Most quality, proxy, source-neighborhood,
disk-cache and grounded-base choices need Save then stop/start Play; UI names pending
restart. Larger reach/Maximum can exhaust commit headroom before hitting their mesh
quota. The 32-cell learned-source option expands source coverage, not mesh memory.
Disk cache is installed but native warm-hit speed benefit is not yet established.
Grounded bases and traversal remain experimental; roots default off. Denser far
forest canopies and broad geology/proxy controls are installed. Bee module is
installed but clearing-dependent; no verified bee animation claim.

**Breaking trees:** approach an existing **young birch** within 6 m and press T.
Only complete young birch variants 0/1 fit this beta (3395/3408 voxels); other trees
are refused intact. Aim at the thin trunk and hold LMB to remove actual 10 cm wood
cells. Unsupported upper components fall under gravity, then ground impact releases
individual cube bodies. G explicitly kicks nearby loose matter; I toggles vacuum
(off initially). O restores the original tree and discards ALL session cargo. This
is session-only harvesting, not saved destruction/inventory. Heavy debris can lag
badly; use O to clear the experiment. Native cut/fall/impact and explicit collection
were observed with conservation intact; successful native kick remains unverified.

The snowy valley may have no eligible birch. For a forest visit use
Ashen Voxel > Exploration > Beta > Review original existing forest (Play).
Young birch habitat was found farther northeast around X -16.1 km / Z -8.0 km;
actual live source and population can change which specimens are present.
Review existing natural birch (Play) finds an eligible published specimen within
1.5 km and activates it. Review actions release gameplay input; choose Resume
gameplay input (Play), then click/aim normally. Restart Play returns to saved valley.
Ordinary exploration/T activation needs none of these review actions.

Shrubs are a separate existing beta: U breaks nearby eligible shrub; I vacuum;
O session restore. The old floating placed-stick demo is retired.

**Verified latest source fix:** saved-valley startup reached all 9/9 required
near-front source cores, zero protected missing (Normal smoke profile). Timeout
now honestly reports incomplete fallback; it cannot force source beneath a standing
player. Full-view 5 m coverage, near-instant convergence, cube/proxy seams, snow
aliasing, far materials and mature-tree destruction remain unfinished. Original
saved Maximum was restored after the lighter smoke test; no full-profile FPS claim.

# Ashen Voxel



Wind and opacity patch: **F1 > Wind strength** adjusts branch/leaf movement.

Distant leaves keep their backlighting with much subtler coverage; a small fade

applies close to the camera. [Patch6 evidence and limits](Docs/QualityPatches/patch-6/README.md).



Material and lighting patch: **F1 > Shadow fill** controls passive sky/ground

light. Unity **Ashen Voxel > Appearance > Material transparency and transmission**

opens per-material controls. NVIDIA DLAA/DLSS Quality are available in F1 without

a driver change. [Controls, native evidence and limits](Docs/QualityPatches/patch-5/README.md).



Local world-terrain update: **Enable Voxel Terrain** now renders voxel LODs

throughout the world, with10cm detail nearby and the existing materials. The

high-fidelity profile is demanding; allow the view to refine after Play. Tested

in native DX12 with the full Exploration scene. See

[quality controls, evidence and limits](Docs/WORLD_VOXEL_LODS.md). The older

published ZIP is unchanged. Current integrated development copy: main AshenVoxel; sibling candidates require explicit adoption.



**Forest horizon follow-up:** trees now follow the complete terrain view,

including the optional coarse horizon up to100 km. Distant stands use compute

culling and GPU indirect drawing when supported, with a CPU fallback; near

geometry and gameplay collision are unchanged. [Native GPU checks, capture and controls](Docs/ForestGpuReview/README.md).



Local workspace terrain update: **nearby10cm voxel terrain** is enabled on the

next Exploration Play. It uses the existing material layers/pigment in bounded

pages and retains distant terrain LODs. Toggle `EnableVoxelTerrain` in the

Exploration settings asset to compare. See [voxel terrain](Docs/VOXEL_TERRAIN.md)

for evidence, limits and native acceptance status. The historical download ZIP

predates this local change.



**For a new AI agent/chat:** read [MEMORY.md](MEMORY.md), [TODO.md](TODO.md),

then the [full handoff](Docs/AI_HANDOFF.md). The [file map](Docs/FILE_MAP.md)

routes directly to code, tests and research. [Session history](Docs/SESSION_LOG.md)

records the major checkpoints; [release records](Docs/releases.json) identify

the exact downloaded project. These documents are maintained independently of

the chat history.



A Unity 6.3 LTS foundation with **10 cm cubic voxel props and local matter**. The default scene is now a basic walk/fly exploration prototype of an Azgaar-conditioned InfiniteDiffusion world, approximately **300 ×150 km**. Avroid's newest Reddit posts are the primary appearance reference; Dark Souls informs mood and combat scale.



**Start here:** [Exploration launch, controls, settings and fidelity](Docs/EXPLORATION_GUIDE.md). The project includes actual learned terrain data, 99 procedural tree/plant/rock prototypes, distinct forest habitats and young/mature stands, bounded vegetation streaming, a default30 km terrain view, optional farther coarse coverage, visual water and revised rock lighting. Default Play needs no Python service. The scene now includes a bounded experimental airborne branch interaction; ordinary forest destruction remains pending. The original destruction arena is separate.



**Latest local update:** thin sticks and fern/leaf contact slow walking by voxel

volume without changing appearance. Actual-tree impostors and an approximate

forest horizon improve distant visibility. Native Unity/D3D11 captures on an

RTX3080 Ti and mesh regressions now exist for this local revision; interactive

feel, standalone builds and GPU/FPS acceptance remain open.

[Captures, settings, checks and limits](Docs/NativeLODReview/README.md).

The older downloadable ZIP remains unchanged.



The whole map has a1280 m learned coarse layer; a5.115 km starter region has full-pipeline decoded5 m data. Optional local model refinement extends decoded data ahead of travel. Near terrain is a smooth0.5–1 m height proxy, rather than a fully materialized10 cm volumetric landscape. Props and their solid-material collision retain10 cm resolution. These source/render/matter resolutions are separate.



C# assemblies were compiled against actual pinned Unity/URP APIs, and headless behavior checks were executed. The earlier release's native limitations remain in its [independent review](Docs/EXPLORATION_REVIEW.md); newer local native rendering evidence is linked above. GPU/FPS acceptance remains open. The reference's newest video could not be fetched, so no frame-based Avroid color match is claimed. See [shading evidence](Docs/AVROID_SHADING_EXPLORATION.md).



## Earlier tools retained



The current update adds **ten procedural ground-plant families, five weathered rock habits and habitat-directed placement** inside WorldExplorer's finite inspection region. The new GroundCoverWorkshop lets you inspect every specimen and change its seed. Leaves, wood and berries reuse existing materials; only Fungus and Petal are appended. See the [implementation and controls](Docs/GROUNDCOVER_UPDATE.md), [research](Docs/UNDERSTORY_RESEARCH.md), and [actual-data previews](Docs/GroundCoverGallery/README.md).



**Rock generation2 replaces the shared round footprint with six shape families:**

angular boulders, slabs, wedges, spires, split outcrops and clusters. Shape is

independent of rock material; seeded Auto selection and habitat placement retain

their distinct proportions. The workshop can compare all six in the same

material. See the [rock-shape update](Docs/ROCK_SHAPES_UPDATE.md) and

[same-granite comparisons](Docs/RockShapeGallery/README.md).



WorldExplorer also includes **biome-aware ground materials, coherent geology, physical snow layers and visual sea water**. Mountain snow varies with exposure, slope and climate; grass, organic litter, soil, sediment and rock use one shared surface classifier for distant proxies and local10 cm cells. See the [surface implementation and verification](Docs/SURFACE_MATERIALS_UPDATE.md), [research](Docs/SURFACE_RESEARCH.md), and [five actual-data previews](Docs/SurfaceGallery/README.md).



The **Tree Workshop** has one shared generator and editable birch, oak, redwood and pine profiles, optional oak acorns, and persistent per-voxel material colors. See the [tree guide](Docs/TREES.md), [research](Docs/TREES_RESEARCH.md), and [sample gallery](Docs/TreeGallery/README.md).



The redwood profile now represents a mature75 m coast redwood, with sculpted basal buttresses and irregular vertical bark relief on the same10 cm grid. [Botanical references and design](Docs/REDWOOD_REFERENCE.md) explain the proportions and stylization; [update and samples](Docs/REDWOOD_UPDATE.md) record what changed and the executed verification.



The earlier **WorldExplorer research scene** retains analytic and imported test sources, finite matter inspection and diagnostic controls. The new **Exploration** scene supplies its external learned source and player instead. See the [world-generation plan](Docs/WORLD_GENERATION_PLAN.md), [research controls](Assets/AshenVoxel/WorldScaleRuntime/README.md), [importer](Tools/WorldScaleImport/README.md) and [original model provider](Tools/DiffusionBridge/README.md). The original arena remains the granular/destruction demo.



## Open and play



1. Install **Unity 6000.3.27f1 (6.3 LTS)** through Unity Hub, including your desktop build platform.

2. Add this `AshenVoxel` folder as a project. Allow package resolution and shader import. The initial editor setup creates URP renderer/pipeline assets automatically.

3. The first-open setup selects `Assets/Scenes/Exploration.unity`. You can also choose **Ashen Voxel → Exploration → Open playable world**. If setup is missing, choose **Ashen Voxel → Exploration → Repair project setup**.

4. Press Play. Wait for prototype uploads and nearby terrain/collision to finish. WASD/mouse move/look, Space jumps, Shift sprints, F toggles flight, Q/E or Ctrl/Space move vertically, F1 opens settings, Escape releases the pointer, and a click recaptures it. Left/right click do not edit terrain.



Official Unity Editor reference assemblies were used for C# compilation, but an activated Editor/player was not run. Import, shader compilation, native play mode, add-on compatibility and desktop builds remain pending. Package versions are explicitly pinned; retain Unity's generated package lock after successful resolution.



For the new small mockups, open `Assets/Scenes/GroundCoverWorkshop.unity`, or

choose **Ashen Voxel → World → Open ground-cover workshop**, then Play. Tab

selects a specimen, O frames all, G changes the seed and R repeats it. RMB/MMB

and the wheel inspect the shapes. WorldExplorer's P mode adds source-biome

ground cover in a finite research region; the Exploration scene uses streamed instances instead.



The following editing/boss controls apply **only to `Assets/Scenes/ForestArena.unity`**, which you can open explicitly for the earlier deformation and granular-gravity demo:



| Control | Action |

|---|---|

| WASD / mouse | Move / look |

| Space / Shift | Jump / sprint |

| F; Space / Ctrl in flight | Toggle flight; ascend / descend |

| Left / right mouse | Excavate / place selected material |

| Mouse wheel | Change brush radius |

| 1 / 2 / 3 / 4 | Sand / soil / basalt / wood |

| B | Trigger a telegraphed boss terrain slam at your position |

| P | Pause granular gravity |

| R | Respawn with body clearance |

| F5 / F9 | Save / load a world snapshot |

| Escape | Release / capture pointer |



Dig beneath the sand mound, pour sand into the crater, and compare material behavior. Mud, dirt, litter, grass and sand are granular in this first material model; stone and wood remain rigid static cells. **Disconnected trees and rocks do not yet fall as rigid bodies.** The Cairn is a destruction test actor with a manual slam, not completed boss AI. No player damage system is present yet.



## Foundation



- Sparse 16³-cell chunks: each chunk covers 1.6 m per axis. No GameObject per cell.

- Engine-independent world authority, deterministic meter-space generation, layered material terrain and procedural trees.

- Optional per-chunk intrinsic sRGB tint channels, preserved through gravity and versioned saves.

- Active-cell gravity with sleeping grains, conserved mass, per-tick candidate limits and incremental wake scans after loading.

- Amanatides–Woo picking and grid-based player collision, independent of delayed mesh/collider caches.

- Material-aware greedy surface extraction; bounded chunk rebuild counts, one-cell sampling halos, vertex-color URP lighting/shadows/fog.

- Checksummed, versioned, size-validated material/appearance snapshots with Format1 migration and atomic save replacement.

- Separate assemblies for data, rendering, runtime and editor tooling.



The starting arena is **25.6 × 12.8 × 25.6 m**, 256 × 128 × 256 cells. Allocated bricks are dense material arrays; sparse allocation does not mean empty cells inside a brick cost nothing. Current generation and snapshot I/O are synchronous; count limits are not hard millisecond guarantees. Jobs/Burst, structural fracture, chemistry, fluids, streaming, GPU ray traversal and dynamic GI are explicit roadmap work, not hidden implementation claims.



## Plan, evidence and decisions



- [Playable exploration guide](Docs/EXPLORATION_GUIDE.md)

- [Exploration population, forest types and LODs](Docs/EXPLORATION_POPULATION.md)

- [Optional decoded terrain streaming](Docs/EXPLORATION_LIVE_REFINEMENT.md)

- [Independent exploration verification](Docs/EXPLORATION_REVIEW.md)

- [Rock shading and reference-access limits](Docs/AVROID_SHADING_EXPLORATION.md)

- [Current rock appearance: six shapes, three seeds, offline previews](Docs/ExplorationPreview/README.md)

- [Ground plants, weathered rocks and habitat placement](Docs/GROUNDCOVER_UPDATE.md)

- [Distinct rock silhouettes and shape controls](Docs/ROCK_SHAPES_UPDATE.md)

- [Botanical/ecosystem/stone research](Docs/UNDERSTORY_RESEARCH.md)

- [Ground-cover seed comparisons and floor mockups](Docs/GroundCoverGallery/README.md)

- [Independent ground-cover review](Docs/GROUNDCOVER_REVIEW.md)

- [Surface materials, snow, water and climate integration](Docs/SURFACE_MATERIALS_UPDATE.md)

- [Surface research and scientific limits](Docs/SURFACE_RESEARCH.md)

- [Actual-data surface gallery](Docs/SurfaceGallery/README.md)

- [World-scale implementation and findings](Docs/WORLD_SCALE_UPDATE.md)

- [InfiniteDiffusion, map import and LOD implementation plan](Docs/WORLD_GENERATION_PLAN.md)

- [Tree update verification](Docs/VALIDATION_TREE_UPDATE.md)

- [Full initial foundation plan](Docs/FOUNDATION_PLAN.md)

- [Implemented data flow](Docs/ARCHITECTURE.md)

- [Architecture decisions and alternatives](Docs/DECISIONS.md)

- [Research notebook and primary sources](Docs/RESEARCH.md)

- [Avroid visual/reference research](Docs/AVROID_REFERENCE.md)

- [Next renderer comparison](Docs/RENDERING_EXPERIMENT.md)

- [Performance protocol and memory arithmetic](Docs/PERFORMANCE.md)

- [Assumptions you can revise](Docs/ASSUMPTIONS.md)

- [Unity compatibility evidence](Docs/UNITY_COMPATIBILITY.md)

- [Unity playtest / build checklist](Docs/PLAYTEST.md)

- [Executed core benchmarks](Docs/Benchmark/RESULTS.md) and [independent raycast validation](Docs/VALIDATION_RAYCAST.md)

- [Actual-data offline previews](Docs/Preview/README.md)

- [Core APIs](Assets/AshenVoxel/Core/README.md) and [rendering limits](Assets/AshenVoxel/Rendering/RENDERING.md)



## Verification



With a .NET 8 SDK installed:



```bash

python3 Tools/run_core_checks.py

```



Tree-specific validation: `python3 Tools/run_tree_checks.py` and `python3 Tools/run_authoring_checks.py`. The tree guide links actual-data gallery and optional CPU path-tracing reproduction tools.



The script uses the SDK compiler directly and requires no external NuGet packages. It also checks the core against .NET Standard 2.1 APIs when that SDK reference pack is installed. Run `python3 Tools/run_mesh_checks.py` for the actual mesher plus independent face/AO oracles under a minimal geometry shim (not Unity), `python3 Tools/run_raycast_checks.py` for an independent exhaustive cell-box oracle and `python3 Tools/check_project.py` for asset/reference integrity. `DOTNET_ROOT` may select an SDK installation. With Unity, run **Ashen Voxel → Validation → Run mesh regression checks** and the playtest protocol. Build from **Ashen Voxel → Build**; development builds preserve profiler access. See the playtest document for batch commands.



Save location is `Application.persistentDataPath/ashen-demo.avxl`. It stores material/appearance state and generator identity, not the active frontier or a bit-identical simulation continuation. Load incrementally rescans for loose material. Keep backups before changing the save schema or generator version.

