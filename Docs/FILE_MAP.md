# Code, evidence and research map

Active-region scheduling: [measured feasibility](ACTIVE_REGION_SCHEDULING_2026-10-27.md),
[isolated harness](../Tools/ActiveRegionStudy/README.md) and
[run ledger](SimulationResearch/2026-10-27/ACTIVE_REGION_RUNS.md).
FIFO membership, sleeping debris and spatial queries; no gameplay installation.

Material simulation trade study: [Noita and related games](MICROPHYSICS_TRADE_STUDY_2026-10-27.md).
Primary references, current-code gap analysis, reactions/blasts/fluids/settling,
3D memory arithmetic and proposed adoption gates. Source identities and analytic
data: [SimulationResearch/2026-10-27](SimulationResearch/2026-10-27).

Startup/continuous LOD quality: [STARTUP_LOD_PROGRESS.md](STARTUP_LOD_PROGRESS.md).
Tools/StartupThroughput contains bounded actual-world probes; Tools/ProgressiveLod
contains lifecycle/admission/fairness checks. VegetationStreaming owns snapshot
publication and fallback reservation; WorldExplorer partials own progressive
coverage/refinement. Source-matched records: Docs/StartupThroughputEvidence/2026-10-27.

Measured terrain optimization: [WORLDGEN_PERFORMANCE_RESULTS.md](WORLDGEN_PERFORMANCE_RESULTS.md).
Retained CPU changes in VoxelLodSurface/WorldExplorerVoxels; profiling and isolated
GPU quad prototype under Tools/TerrainPerformance. Frozen native/.NET reports and
retained-source checks: Docs/TerrainPerformanceEvidence/2026-10-27.

World-generation/LOD processing research and current-source corrections:
[WORLDGEN_SPEED_RESEARCH.md](WORLDGEN_SPEED_RESEARCH.md). Includes alternatives,
downsides, conditional speedup arithmetic and inspected source hashes.

Whole-world terrain: [WORLD_VOXEL_LODS.md](WORLD_VOXEL_LODS.md).
`WorldScaleData/VoxelLodSurface.cs` builds material-bearing exposed faces and
selects projected quality; `WorldScaleRuntime/WorldExplorerVoxels.cs` owns worker,
packed upload buffers and view priority. Existing WorldExplorer partials retain
planning, coverage and rebasing. Tools/run_voxel_lod_checks.py and
Tools/run_voxel_lod_runtime_checks.py exercise the new path. The near overlay
below is retained historical/comparison code.

Near voxel overlay: [VOXEL_TERRAIN.md](VOXEL_TERRAIN.md). Conversion and contact
are in `WorldScaleData/VoxelTerrainPage.cs`; worker/page residency and uploads
are in `Exploration/ExplorationVoxelTerrain.cs`. Bootstrap/Settings enable it;
WorldExplorerIntegration and GroundSurface.shader share completed-page masks.
Tests, native fixture and offline previews live under `Tools/VoxelTerrainChecks`.

Latest overlay: [NativeLODReview](NativeLODReview/README.md) covers native
captures, stick resistance and distant imagery. New code: Exploration/
`ForestBillboard.cs`, `ForestHorizon.cs`, `ForestImpostor.shader`, Editor/
`ForestAtlasBaker.cs`, `ExplorationCapture.cs`; prototype conversion is
`Tools/ExplorationPrototypes/bake_interactions.py`. Ordinary movement/prototype/
streaming files in the table below contain the interaction integration.

Current checkpoint:2026-10-27. Paths below are relative to the project root.
Read [MEMORY.md](../MEMORY.md) before historical research/update documents.

## Runtime responsibilities

| Work area | Read/edit here | Contract |
| --- | --- | --- |
| Startup and scene glue | [ExplorationBootstrap.cs](../Assets/AshenVoxel/Exploration/ExplorationBootstrap.cs) | Validates bundled data, binds one source, loads prototypes, gates input, applies lighting/UI and owns cleanup |
| Exposed authoring/session controls | [ExplorationSettings.cs](../Assets/AshenVoxel/Exploration/ExplorationSettings.cs), [settings asset](../Assets/AshenVoxel/Settings/ExplorationSettings.asset) | Runtime clone; many generation/source settings need next Play; view/movement controls apply during Play |
| Sky, atmosphere and post | [controls/ownership](ATMOSPHERE.md), [evidence](AtmosphereEvidence/README.md) | Cloud-authored finite clouds/noise, depth composite and shared fog; owned settings/presets/URP volume, migrated options, rebase-aware phase; bounded native checks pass, full-scene acceptance open |
| First-open/repair/build | [ExplorationProjectSetup.cs](../Assets/AshenVoxel/Exploration/Editor/ExplorationProjectSetup.cs) | Preserves later scene choices, configures URP/SSAO; Exploration is first build scene |
| Actual baked source and source categories | [ExplorationAtlasField.cs](../Assets/AshenVoxel/WorldScaleData/ExplorationAtlasField.cs) | Global crop positioning, exact pairing, transforms, fine blend, categorical biome atlas, conservative bounds |
| Source interfaces and coordinates | [WorldFields.cs](../Assets/AshenVoxel/WorldScaleData/WorldFields.cs), [WorldCoordinates.cs](../Assets/AshenVoxel/WorldScaleData/WorldCoordinates.cs) | Metres/doubles globally; renderer floats relative to origin; analytic fixture is explicitly separate |
| Atlas serialization | [HeightAtlas.cs](../Assets/AshenVoxel/WorldScaleData/HeightAtlas.cs), [ClimateArchive.cs](../Assets/AshenVoxel/WorldScaleData/ClimateArchive.cs) | Checksummed bounded grids, exact height/climate pairing and physical units |
| Surface geology, cover and snow | [SurfaceMaterialSampler.cs](../Assets/AshenVoxel/WorldScaleData/SurfaceMaterialSampler.cs), [SurfaceProfile.cs](../Assets/AshenVoxel/WorldScaleData/SurfaceProfile.cs), [SurfaceWorldField.cs](../Assets/AshenVoxel/WorldScaleData/SurfaceWorldField.cs) | Shared terrain/material authority; snow adds physical depth; immutable profile identity |
| Local sparse matter contract | [ImplicitMatter.cs](../Assets/AshenVoxel/WorldScaleData/ImplicitMatter.cs) | Implicit buried stone and bounded edits; not complete continuously streamed persistent matter |
| Terrain planning/render integration | [LodPlanner.cs](../Assets/AshenVoxel/WorldScaleData/LodPlanner.cs), [WorldExplorer.cs](../Assets/AshenVoxel/WorldScaleRuntime/WorldExplorer.cs), [WorldExplorerIntegration.cs](../Assets/AshenVoxel/WorldScaleRuntime/WorldExplorerIntegration.cs) | Bounded quadtree, conservative height bounds, async plan snapshots, incremental mesh builds, complete coverage, external camera API |
| HTTP transport | [RemoteTerrainField.cs](../Assets/AshenVoxel/WorldScaleRuntime/RemoteTerrainField.cs) | Async requests/cache; no HTTP during terrain sampling; strict identity, payload/error checks |
| Safe decoded-source composition | [ExplorationRefinementField.cs](../Assets/AshenVoxel/WorldScaleData/ExplorationRefinementField.cs), [ExplorationLiveRefinement.cs](../Assets/AshenVoxel/Exploration/ExplorationLiveRefinement.cs) | Owned immutable patches, protected insert/evict, snapshots, bounded ahead work and regional invalidation |
| Movement and prop collision interface | [ExplorationMovement.cs](../Assets/AshenVoxel/WorldScaleData/ExplorationMovement.cs), [ExplorationController.cs](../Assets/AshenVoxel/Exploration/ExplorationController.cs) | Double-position motor, continuous terrain, hard span queries, bounded sweeps, loading gates; no editing |
| Forest species/age/density | [ForestBiomes.cs](../Assets/AshenVoxel/WorldScaleData/ForestBiomes.cs), [ExplorationPopulation.cs](../Assets/AshenVoxel/WorldScaleData/ExplorationPopulation.cs) | Globally keyed candidates; dominant species; static coherent stand fields |
| Plants and rock placement | [GroundCoverScatter.cs](../Assets/AshenVoxel/WorldScaleData/GroundCoverScatter.cs) | Habitat/substrate rules; global ownership; rock shape/appearance identity |
| Prototype serialization/collision | [ExplorationPrototypePack.cs](../Assets/AshenVoxel/WorldScaleData/ExplorationPrototypePack.cs), [pack](../Assets/AshenVoxel/Exploration/Prototypes/ExplorationPrototypes.bytes) | Lossless quad palette/AO encoding,99 models/412 LODs, full-resolution hard spans |
| Prop residency and drawing | [VegetationStreaming.cs](../Assets/AshenVoxel/Exploration/VegetationStreaming.cs) | Shared instancing/pages, caps, per-cell readiness, incremental matrices, full-scale geometry and immediate rebase |
| Procedural tree geometry | [Core/Trees](../Assets/AshenVoxel/Core/Trees), [TREES.md](TREES.md), [REDWOOD_REFERENCE.md](REDWOOD_REFERENCE.md) | One genome/skeleton/raster path; height/bark/branch/leaf profiles and persistent pigment |
| Procedural plants/rocks | [Core/Details](../Assets/AshenVoxel/Core/Details), [Core/Rocks](../Assets/AshenVoxel/Core/Rocks) | Plant1, rock geometry2/appearance2; separate shape, material and tint |
| Authoritative finite matter | [Core](../Assets/AshenVoxel/Core), [ARCHITECTURE.md](ARCHITECTURE.md) | Engine-free VoxelWorld, Set, granular simulation, edits, picking and snapshots |
| Original destruction demo | [Runtime](../Assets/AshenVoxel/Runtime), [ForestArena.unity](../Assets/Scenes/ForestArena.unity) | Finite arena/Cairn tool harness; separate from Exploration |
| Object shading | [WorldSurface.shader](../Assets/AshenVoxel/Rendering/WorldSurface.shader), [VoxelSurface.mat](../Assets/AshenVoxel/Rendering/Resources/VoxelSurface.mat) | Shared vertex RGB/AO, instancing, URP dielectric lighting; four passes |
| Ground and fake water | [GroundSurface.shader](../Assets/AshenVoxel/WorldScaleRuntime/GroundSurface.shader), [VisualWater.cs](../Assets/AshenVoxel/WorldScaleRuntime/VisualWater.cs), [VisualWater.shader](../Assets/AshenVoxel/WorldScaleRuntime/VisualWater.shader) | Height-proxy surface shading, SSAO support; bounded sea plane and underwater wash, not fluids |

## Model/import tools

- [Tools/ExplorationBake](../Tools/ExplorationBake): current bake, coarse-registration
  correction, publication/finalization, coordinate-view service, physical climate
  policy, optional Windows launcher and provider checks. Start with its README.
- [Tools/DiffusionBridge](../Tools/DiffusionBridge): original upstream-model bridge,
  immutable jobs/canonical owners/transport, Azgaar conditioning and dependency pins.
- [Tools/WorldScaleImport](../Tools/WorldScaleImport): offline map/TIFF import and
  validation; do not confuse imported authored height with learned decoded output.
- [Tools/ExplorationPrototypes](../Tools/ExplorationPrototypes): current full99-model
  bake and exact lossless mesh round-trip verification.
- [Samples metadata](../Assets/AshenVoxel/Exploration/Samples/exploration-world.json):
  definitive shipped dataset identity, source hashes, transforms and spawn.

## Evidence lookup

| Question | Start with |
| --- | --- |
| What is actually checked? | [EXPLORATION_REVIEW.md](EXPLORATION_REVIEW.md), [release freshness report](exploration-release-validation.json) |
| Does startup logic work with the real assets? | [smoke report](exploration-smoke-validation.json), [smoke harness](../Tools/ExplorationSmokeChecks/README.md) |
| Was real Unity API compilation performed? | [runtime report](ExplorationEvidence/real-unity-api-validation.json), [Editor report](ExplorationEvidence/real-unity-editor-api-validation.json) |
| Where are the model execution/negative-output proofs? | [ExplorationEvidence](ExplorationEvidence), [VALIDATION_INFINITE_DIFFUSION.md](VALIDATION_INFINITE_DIFFUSION.md) |
| Which exact geometry/palette was previewed? | [ExplorationPreview](ExplorationPreview/README.md), [prototype bake report](exploration-prototype-bake.json) |
| Where can each forest stage be found? | [guide coordinates](EXPLORATION_GUIDE.md), [actual-world survey](exploration-world-survey.json) |
| What must be tested natively? | [PLAYTEST.md](PLAYTEST.md), [TODO.md](../TODO.md) |
| What was received from the Minecraft fork? | [MOD_PARITY.md](MOD_PARITY.md), [AI_HANDOFF.md](AI_HANDOFF.md) |

## Document precedence and research routes

Current status: **MEMORY → TODO → AI_HANDOFF → current code/checked reports**.
README/EXPLORATION_GUIDE are launch instructions. DECISIONS and AGENTS define
contracts. Earlier milestone/recovery files retain chronological evidence; their
old pending statements, default scenes and check counts can be superseded.

Research by topic: [InfiniteDiffusion](INFINITE_DIFFUSION_RESEARCH.md),
[world scale/LOD](WORLD_SCALE_RESEARCH.md), [Azgaar](AZGAAR_IMPORT_RESEARCH.md),
[surfaces/snow](SURFACE_RESEARCH.md), [trees](TREES_RESEARCH.md),
[understory/rocks](UNDERSTORY_RESEARCH.md), [Avroid](AVROID_REFERENCE.md) and
[latest shading/access limits](AVROID_SHADING_EXPLORATION.md).
The [foundation plan](FOUNDATION_PLAN.md) describes goals, not proof they all exist.
