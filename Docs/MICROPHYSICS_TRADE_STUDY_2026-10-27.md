# Noita and material-physics games: trade study for AshenVoxel

Research date: **October 27, 2026 (America/Denver)**. Status: **research and proposed experiments; no gameplay implementation or new native verification**.

Follow-up: [active-region scheduling experiments](ACTIVE_REGION_SCHEDULING_2026-10-27.md)
now provide isolated CPU measurements for FIFO membership and dormant-debris queries.
Those later results do not turn this initial trade study into a gameplay release.

## Recommendation

Build on the existing 10 cm material world, local work queues and coherent falling sections. The best next gameplay experiment is **a bounded blast that excavates matter, releases supported sections correctly, and starts a small set of material reactions**. The best enabling work is compact activity tracking and an explicit ownership/transaction boundary shared by edits, reactions, debris and eventual persistence.

Use a small reaction vocabulary first: dry fuel ignites, wet fuel resists ignition, water cools, snow/ice melt, and lost support causes collapse. These interactions could create firebreaks, firefighting, excavation, avalanches and environmental chain reactions from a few reusable rules. They are proposals, not present features.

Keep arbitrary terrain editing and durable inventory as explicit integration work. A successful finite test chamber does not make the streamed Exploration world editable. Retain the current renderer and native section bodies while evaluating these additions.

## Evidence and current baseline

Primary evidence consists of developer explanations, official project documentation and source code. Source-described behavior is separated below from my proposed adaptation. I did not play or benchmark the comparison games. The GDC Vault landing page redirected to maintenance, so this study does not claim to have watched that talk. Noita's technical details instead come from its developer interview and official FAQ.

The project entry documents were read in order: MEMORY, TODO, AI_HANDOFF; then README, DECISIONS, FILE_MAP and relevant code. The latest checkpoint supersedes older statements that ordinary trees are wholly indestructible or that the live beta still uses one body per cell. Current whole-scene frame times and mature-tree acceptance remain open.

| AshenVoxel subsystem inspected | What exists | What the study must not assume |
| --- | --- | --- |
| [Simulation.cs](../Assets/AshenVoxel/Core/Simulation.cs) | Single-writer granular queue; sleeping settled cells; 26-neighbor wake; prevents repeated movement within one `Step` | No liquid, gas, thermal or reaction solver; this is not a full gravity/velocity model |
| [VoxelWorld.cs](../Assets/AshenVoxel/Core/VoxelWorld.cs) | Sparse 16³ chunks; byte material; optional tint; `Set` notifications and boundary invalidation | `DirtyChunks` is mesh dirtiness, not a shared simulation schedule; absent finite-world chunks mean air |
| [Materials.cs](../Assets/AshenVoxel/Core/Materials.cs) | 25 append-only IDs, hardness, density cues, flammability and optics | `Flammable` alone does not burn anything; there is no Water/Steam material; density is not a calibrated SI mass contract |
| [WorldEdits.cs](../Assets/AshenVoxel/Core/WorldEdits.cs) | Hardness-filtered spherical carve/place through `Set` | Synchronous sphere traversal is not a budgeted explosion, pressure solver or mass-preserving debris transfer |
| [NaturalTreePhysics.cs](../Assets/AshenVoxel/Exploration/NaturalTreePhysics.cs) and [native bodies](../Assets/AshenVoxel/Exploration/NaturalMatterRigidBodies.cs) | Exact material/tint ownership, local certified support searches, eight section slots, native section motion and weighted loose parcels | Connectivity is not bending strength; parcels do not provide a general colliding sand pile; full original per-cell records remain |
| [ExplorationNaturalTreeMatter.cs](../Assets/AshenVoxel/Exploration/ExplorationNaturalTreeMatter.cs) | Current single-prop activation, retained support work and checked 1.25 ms support slice | Multiple interacting prop sessions, permanent harvesting and arbitrary excavation are unfinished |
| [ImplicitMatter.cs](../Assets/AshenVoxel/WorldScaleData/ImplicitMatter.cs) | Source-derived terrain plus bounded sparse edits as a prototype | Not a complete live terrain edit/persistence adapter; its `Set` is not `VoxelWorld.Set` and does not supply the latter's wake/dirty contract |
| [Persistence.cs](../Assets/AshenVoxel/Core/Persistence.cs) | Checksummed finite material/tint snapshots, Format 2, Format 1 loading | No thermal channels, active queues, body poses or durable Exploration edit/cargo transaction; no exact resumed replay |
| [VisualWater.cs](../Assets/AshenVoxel/WorldScaleRuntime/VisualWater.cs) | Visual sea representation | No conserved, excavatable fluid volume |

The [source manifest](SimulationResearch/2026-10-27/source-manifest.json) records SHA-256 identities for 15 inspected source/scene/package files. This workspace has no Git repository available; hashes identify the reviewed revision. Prior native and CPU evidence remains attached to its original revisions.

End-of-review verification detected a concurrent change to `NaturalMatterRenderer.cs` after the initial manifest capture. This study did not edit it. Its inspected end state still uses derived meshes and structure revisions; no renderer performance claim is made here. Both observed hashes are retained in [verification](SimulationResearch/2026-10-27/verification.json), rather than relabeling the initial snapshot as unchanged.

## What the comparison games contribute

| Reference | Verified lesson | Proposed benefit for AshenVoxel | Transfer limit |
| --- | --- | --- | --- |
| **Noita** | Local material simulation combines with destructible rigid bodies. Its developer describes dirty regions and conflict-avoiding chunk scheduling. | Active-region scheduling; material interactions that cause further edits and collapse | 2D implementation details and frame rates do not establish 3D throughput. [Developer interview](https://80.lv/articles/noita-a-game-based-on-falling-sand-simulation) |
| **The Powder Toy** | Pressure, gravity and wall maps use a coarser grid than material pixels; element definitions include thermal and pressure transitions. | Separate auxiliary fields from exact matter, with reusable material rules | Its simulation constants and 2D maps are not physical parameters for our world. [Configuration](https://github.com/The-Powder-Toy/The-Powder-Toy/blob/c205becb1cf7ef1915293e2c99b2c7901d2887cd/src/SimulationConfig.h), [wood definition](https://github.com/The-Powder-Toy/The-Powder-Toy/blob/c205becb1cf7ef1915293e2c99b2c7901d2887cd/src/simulation/elements/WOOD.cpp) |
| **Sandboxels** | Its published types describe pair reactions, temperature thresholds, burning state, burn products, density and viscosity. | A compact, data-driven reaction catalogue; quick iteration on material behavior | Types document a schema, not proof of conservation, deterministic execution or scalable scheduling. [Published definitions](https://github.com/R74nCom/Sandboxels-Types/blob/761240587bc9d8eae51e9a6c8778dbde3a3dda88/globals.d.ts) |
| **Cortex Command Community Project** | Its settle pass transfers resting particles into terrain after update/deletion work. | Make persistent rubble become cheap terrain again, with clear ownership transfer | Its priority/overwrite rules should not be adopted as our conservation policy. [Settle pass](https://github.com/cortex-command-community/Cortex-Command-Community-Project/blob/4483894de605d03fbe2efa826e7f78fb33d8de65/Source/Managers/MovableMan.cpp#L1543) |
| **Teardown / developer's subsequent engine research** | The developer distinguishes Teardown's dense object grids from a later engine's sparse 8³ chunks. | Measure empty-space and repeated-record costs; compact cold matter while keeping edits exact | The sparse format and new solver described there belong to the subsequent engine, not automatically to shipped Teardown. [Developer report, 2024](https://blog.voxagon.se/2024/12/29/year-summary.html) |
| **Dig or Die** | The developer explicitly centers gameplay on structural collapse, water cycles and water-pressure-powered structures. | Design tests around useful player actions: divert water, build a dam, undermine a support | This is a coarser 2D sandbox reference; the press kit does not reveal its solver. [Official press kit](https://gaddygames.com/press/kit/sheet.php?p=dig_or_die) |
| **Slow Rush development logs** | Moving sand represented by fixed colliders destabilized bodies; the developer explored kinetic particles and grounding classifications. | A concrete regression case for future sand/fluid coupling around native tree bodies | A development experiment, with acknowledged limitations, not a proven production solution. [Failure analysis](https://www.slowrush.dev/news/kinetic-atoms/), [follow-up](https://www.slowrush.dev/news/playing-nice-with-moving-bodies/) |

Noita's interview gives a particularly useful boundary: its described 64×64 dirty regions, per-update tracking and four scheduling passes address a 2D write footprint. It also describes selective explosion-induced collapse rather than universal structural failure of every static rock pixel. These are historical implementation details, not a specification of the current executable. Our 3D scheduling must derive safe write regions independently. The official FAQ describes simplified chemistry, electricity and thermodynamics; it does not claim engineering-grade physical accuracy. [Interview](https://80.lv/articles/noita-a-game-based-on-falling-sand-simulation), [official FAQ](https://noitagame.com/)

The common opportunity is **composing a few comprehensible systems**. A fire can remove support; collapse can open a water path; water can cool burning debris. This is my design inference from the references, not a claim that their engines share one implementation.

## The 3D cost we must budget

At 10 cm there are 1,000 cells per cubic metre. A 16³ chunk spans only 1.6 m per axis. Increasing a dense simulation region's side length twofold increases its cell count eightfold.

| Dense cubic region | Cells | Illustrative cell-channel payload |
| --- | ---: | ---: |
| 16 m per side | 4,096,000 | 35.16 MiB |
| 32 m per side | 32,768,000 | 281.25 MiB |
| 64 m per side | 262,144,000 | 2,250 MiB |

Payload assumes one material byte, four tint bytes, two temperature bytes, one fuel byte and one wetness byte at every site. It excludes arrays/objects, queues, collision, geometry, double buffers and temporary work. It is an intentionally dense scenario, **not current allocation or a forecast**. Sparse optional channels can avoid much of it; assigning fields to every empty site would lose that advantage.

A 16³ activity bitmap costs 512 bytes per channel. That is a useful alternative to per-cell hash membership under dense activity, but a hash/queue can still win for a handful of active cells. A 32 m cube of five float auxiliary fields—pressure, three velocity components and temperature—costs 625 MiB at 10 cm, 9.77 MiB at 40 cm, or 1.22 MiB at 80 cm, before buffering. The reduction is 64× or 512× in field sites, **not a predicted FPS gain**. The Powder Toy establishes the precedent for separate field resolution; these 3D sizes are our proposed experiments. [Source configuration](https://github.com/The-Powder-Toy/The-Powder-Toy/blob/c205becb1cf7ef1915293e2c99b2c7901d2887cd/src/SimulationConfig.h)

An explosion of radius 1, 2 or 5 m encloses approximately 4,189, 33,510 or 523,599 cell-volumes. Illustrative inclusive bounding boxes contain 9,261, 68,921 or 1,030,301 candidates. Actual occupied-cell and sampled-sphere counts depend on alignment and terrain. A larger radius therefore needs resumable traversal, downstream mesh/collider budgets and chain-event limits. See [reproducible arithmetic](SimulationResearch/2026-10-27/analytic-sizing.json).

## Ranked trade study

Benefit and cost are engineering judgments, not measured speedups or calendar estimates. **Low** means extending one established path; **medium** means a new bounded subsystem; **high** means changes across ownership, physics, streaming or persistence. Priorities reflect the current bottlenecks and missing features.

| Priority / option | Benefit | Cost and principal tradeoff | Disposition |
| --- | --- | --- | --- |
| **P0: unified edit/ownership transactions** | Stops reactions, blasts, inventory and streaming from duplicating or resurrecting matter | High for production streaming; medium in a finite chamber | Foundation for integration; prototype finite ownership first |
| **P1: compact per-system active sets** | Less scheduling overhead during dense edits; dormant terrain remains cheap | Medium; may lose to the existing queue for sparse activity; scheduling can change outcomes | Benchmark bitsets/hybrid against the current queue |
| **P1: bounded blast events** | Immediately useful excavation, debris and chain reactions | Medium locally; shielding, support, collision freshness and concurrent hits are real work | First visible prototype |
| **P1: a small reaction/thermal system** | Burning, cooling, phase changes and environment interactions from reusable rules | Medium locally, high with persistent moving matter; needs state channels and mass policy | Add after transaction semantics; start with a few rules |
| **P1/P2: compact dormant matter and support summaries** | Targets large-tree preparation and duplicated per-cell storage | Medium/high; editable interiors and irregular tint reduce compression | Measure on the actual mature fixture; independent of reactions |
| **P2: local conserved water** | Flooding, drainage, fire suppression and later erosion | High; stable flow, capacity, seams, persistence and body coupling | Small finite reservoir first; streamed integration later |
| **P2: settle debris back into matter** | Persistent rubble becomes editable/collidable without permanent body cost | High; rotated geometry, weighted parcels and occupied destinations need exact accounting | Only after authoritative terrain ownership exists |
| **P2/P3: strength beyond connectivity** | Overloaded thin supports can fail; more convincing tree/rock behavior | High; connectivity alone cannot determine stress or bending | Start with explicit gameplay strength rules; defer full structural solver |
| **P3: coarse pressure/air fields** | Smoke transport, blast aftermath and confined-gas gameplay | High; leakage, stability and field/matter coupling | Introduce only for a demonstrated gameplay need |
| **Defer: GPU authority / new renderer / full continuum solver** | Potential throughput or richer visual behavior | Very high integration cost; readback, ownership and debugging complicate edits | No evidence here justifies replacing the current CPU authority or URP |

If the next goal is immediate interaction, choose blasts and a tiny reaction set in a bounded chamber. If it is fixing present large-tree latency, choose memory/support profiling first. Neither path resolves distant-ground mesh residency by itself.

## Implementation proposals

### 1. One commit path, several independent simulations

Keep material writes through `VoxelWorld.Set`. Add an engine-free coordinator around the existing authority with explicit batches: validate inputs and source revision, reserve output storage, commit ownership/material changes, then publish wake and invalidation work. Two-cell moves or reactions must not leave duplicated or half-consumed products when their time slice ends.

Use separate active sets for granular motion, heat, reactions and support. A cell can be mechanically asleep while still hot or burning. A tint-only change must continue to dirty appearance without waking granular motion. New temperature/wetness setters need their own notifications; using `Set` with the same material currently produces no material change event.

Keep coordinate conversion explicit: signed global cell keys outside, local chunk indices inside, metres at gameplay interfaces. The current finite `VoxelWorld` treats out-of-domain samples as air; an unloaded neighboring region must instead be distinguished from known air. Initially retain transfers at that boundary, expose pending work, and load/pin the required region. Never let fluid or blasted matter disappear into an unloaded neighbor.

For streamed terrain, layer edits over a frozen source identity and give picking, collision and all relevant visual LODs the same edit revision. The existing height-column surface path cannot represent an arbitrary tunnel merely by changing its top height. Excavated regions require volumetric surface extraction and revision-safe replacement of the corresponding terrain/contact caches.

### 2. Improve activity storage before adding parallel writers

Compare the existing queue/hash sets with a 4096-bit bitmap per active chunk, a chunk queue and a resumable local cursor. A hybrid can retain explicit indices for sparse activity and scan bitsets for dense activity. A dirty bounding box is cheaper bookkeeping but may sweep mostly inactive space when edits are far apart. Record actual active density before choosing.

Define logical ticks independently of wall-clock slices. In the current granular solver `Step` advances `Tick` and clears the moved set; simply calling it repeatedly to finish one frame changes semantics. A resumable logical tick needs persistent moved/current/next activity state. Probabilistic reactions should use stable cell/event/tick keys, not thread or frame-completion order.

Do not transplant Noita's four passes. Eight parity classes in 3D are only a candidate if the proven read/write halo cannot overlap between simultaneous workers. Fast motion, blasts and support searches violate naive neighbor assumptions. A safer initial design is parallel immutable reads with bounded intents and a deterministic single-writer commit. Native rigid-body motion remains outside any claim of cross-platform exact lockstep.

### 3. Add explosions as queued material events

Use the existing sphere logic as a reference for coordinate inclusion, not as the entire blast implementation. Proposed stages are: enumerate bounded candidates; compute shielding and material-specific damage; reserve debris/reaction results; commit removed cells; queue support updates; apply impulses to surviving/detached section bodies; invalidate surfaces.

Start with a finite-radius gameplay blast and a bounded ray or connected-wave attenuation model. The simpler unoccluded sphere can serve as a test oracle, but it should not imply that stone walls provide protection. Hardness, structural strength, fracture response and blast absorption should be separate properties; current tool hardness is not an engineering strength value.

A blast supplies an impulse; ordinary gravity still acts normally. Preserve angular effects from off-center impacts through the native body adapter. Do not create a Rigidbody per ejected cell. Use exact weighted debris or coherent fragments, with bounded conversion to loose parcels.

Chain detonations need stable IDs, a visited/consumed policy, a retained event queue and a visible oldest-event age. Saturation delays work; it must not silently skip matter or recursively detonate an unbounded chain in one frame. Large edits should trigger support and mesh work in coalesced batches instead of rebuilding once per removed voxel.

### 4. Introduce a compact reaction vocabulary

Extend material definitions with immutable behavior data and allocate state channels only in regions that need them. Candidate state includes quantized temperature, remaining fuel and wetness; ranges and saturation behavior need tests. An authored reaction entry describes reactants, thresholds, rate, products, heat exchange and destination-capacity requirements. Sandboxels supplies a useful schema precedent; our bounded commit rules are a separate design. [Definitions](https://github.com/R74nCom/Sandboxels-Types/blob/761240587bc9d8eae51e9a6c8778dbde3a3dda88/globals.d.ts)

Suggested sequence: heated dry litter/leaves ignite; burning fuel heats nearby matter and is consumed into accounted products; wetness delays ignition and evaporates using energy; water cools hot material; snow/ice become water. Water and combustion products require append-only material IDs. A tiny first fixture can exercise dry fuel and an explicit cooling tool before water transport exists.

Separate light emission and flame/smoke visuals from authoritative matter. Decorative sparks may be GPU particles; anything that damages, ignites, blocks, can be collected or contributes mass needs an authoritative record. A fire should not remesh a solid chunk every time its displayed flame flickers.

Reaction accounting differs from unchanged-material conservation. Existing per-material cell counts cannot remain constant when wood becomes ash/gas. Define product quantities and explicit environmental exchanges. Equal cell counts do not conserve mass across different densities or phase changes. Use conserved amount/fill state or an explicit reservoir for displaced products, including steam expansion; do not simply relabel a full ice cube as an equal-volume gas cube and call it physical conservation. Current density cues and native mass scaling need a documented unit decision before adding calibrated buoyancy or force formulas.

The first fire model may assume ambient oxygen. Label that simplification: sealed-room suffocation and pressure buildup then remain unsupported. Add gas chemistry only when the game needs those outcomes.

### 5. Local water and body interaction

A first water solver should transfer conserved amounts between 10 cm cells, prioritize downward movement, then redistribute laterally with simultaneous/conflict-resolved fluxes. Whole-cell swaps are simpler but visibly pulse and equalize poorly; fill amounts cost storage but improve shallow water and conservation. Track overflow and leave uncommitted transfers pending.

Coarse 40–80 cm air/pressure fields are a later auxiliary layer, not a change to the 10 cm occupancy grid. Fine solid-face barriers or conservative permeability are necessary to keep a 10 cm wall watertight when field cells are larger. Stable timestep and transport limits must be derived for the chosen update rule; no speed benefit is established by lowering resolution alone.

Use ground-connected settled matter for static collision caches. Falling grains and changing liquid surfaces should not be presented to Unity as immovable solids that teleport into bodies. Start with swept occupancy checks, bounded displacement and limited mass-aware forces. Slow Rush's developer reports exactly this category of fixed-collider failure; its later water splashes also lacked full drag/buoyancy at that checkpoint. [Failure](https://www.slowrush.dev/news/kinetic-atoms/), [follow-up](https://www.slowrush.dev/news/playing-nice-with-moving-bodies/)

### 6. Compaction, settling and structural failure

For large props, measure allocated chunk occupancy, duplicated cell arrays, queue scratch and snapshot copies separately. Try uniform-material chunks, palette/tint runs and compact active overlays. Keep buried interiors reconstructible from exact stored matter or pinned provenance; deleting hidden cells changes mass, support and the next exposed cut surface. The existing 16³ sparse layout already captures part of this benefit, so an 8³ conversion needs actual occupancy/rebuild comparisons.

A settled parcel can be deposited only into reserved free destinations. Its representative position is not hundreds of physical cells' worth of storage capacity. Retain unplaced quantity in a bounded pending record and atomically remove it from the parcel ledger only when deposit succeeds. Rotated sections require a defined rasterization/overlap policy before reinsertion. These are requirements for our adaptation of settling, not guarantees supplied by Cortex Command's implementation.

Maintain connectivity as the correctness baseline. Add strength later using local cross-section/support summaries and explicit material rules. For trees, reuse the shared skeleton where available, but reconcile it with actual cuts; do not build a second incompatible tree generator or treat the original skeleton as proof that an edited limb is still attached. A thin connected stump currently holding a crown is a known consequence of connectivity, not necessarily a native-gravity defect.

## Experiments and adoption gates

The following are unexecuted proposals. Use source hashes, fixed seeds, the same camera/edit sequence and the same scene/settings for comparisons. Record both isolated CPU cost and full Exploration cost; neither substitutes for the other.

| Stage | Concrete experiment | Pass conditions / evidence to record |
| --- | --- | --- |
| **A: baseline and scheduler** | Sleeping scene, isolated falling grain, dense sand pile, edit at a chunk corner, and concurrent wake sources; compare queue vs bitset/hybrid | No lost/duplicated matter; no repeated movement within a logical tick; wake across boundaries; idle work proportional to active state; allocations, p50/p95/p99 slice time, maximum work item, queue age and total completion time |
| **B: blast chamber** | 1/2/5 m blasts in empty space, dirt, a shielded cavity, a supported slab, and a chain of charges | Mass/quantity ledger reconciles; wall attenuation matches declared model; eight occupied section slots retain pending detachments; zero-budget/resume works; collision and visible revisions converge; no stale-input replay |
| **C: fire and cooling** | Dry versus wet fuel, a firebreak, nonflammable barrier, depleted fuel, reaction crossing a chunk seam | Stable rule order; bounded spread; heat/product accounting follows declared model; pigment edits do not wake gravity; moving burning fragments retain their state or are explicitly unsupported |
| **D: reservoir** | Sealed 10 cm basin, tunnel/drain, communicating vessels, falling body, unloaded neighbor | Conserved fluid amount; no thin-wall leak; no NaN/negative fill; retained outflow; no body launches from moving static colliders; documented equalization/drag limitations |
| **E: production integration** | Multiple edited trees/rocks plus crater and water; source refinement/rebase; leave/revisit; save/reload during activity | No resurrection, duplicate cargo or invisible collision; all readers use the same committed edit revision; bounded cache retirement; native depth/shadow/normal/edit verification |
| **F: persistence failure cases** | Truncated/corrupt/oversized records, old Format 1/2 snapshots, interrupted transaction and incompatible source versions | Explicit migration/rejection; atomic edit+inventory recovery; no partial installation; replay scope stated honestly |

Suggested benchmark acceptance target—not a measured result or an existing user requirement—is a **2 ms p95 incremental simulation slice** on the user's target PC, with full-frame p95/p99 and worst spikes reported separately. The critical second condition is that a finite event's backlog drains under its chosen budget. Meeting a short per-frame slice while work accumulates indefinitely is failure. Include rendering, collider cooking, preparation and native physics in end-to-end latency; the current 1.25 ms support check does not bound those costs.

After actual core changes run `python3 Tools/run_core_checks.py`; after asset/reference changes run `python3 Tools/check_project.py`. Relevant tree, mesh and authoring work also requires `Tools/run_tree_checks.py`, `Tools/run_mesh_checks.py` and `Tools/run_authoring_checks.py`. Rendering changes require the native mesh regression menu and visual playtest described in [PLAYTEST.md](PLAYTEST.md). Record future Windows/.NET or native measurements with their actual environment; this study ran no simulation benchmarks.

## Adoption order and rejected shortcuts

1. Profile the current large-tree/source baseline and preserve existing acceptance gates. Establish finite edit transactions and activity metrics.
2. Implement a finite blast fixture through those contracts; then add dry fuel/heat/cooling reactions. This yields the earliest reviewable interaction without requiring a global fluid engine.
3. Pursue compact matter/support storage independently if profiling confirms its benefit. Integrate permanent multi-object/terrain ownership before claiming a persistent destructible world.
4. Add a conserved local reservoir, then debris deposition and durable thermal/fluid state. Prototype coarse pressure only if confined blasts, smoke transport or water-pressure gameplay justify it.

Do not shrink the 10 cm matter grid to imitate screen pixels. Do not equate a Noita-style visual effect with a complete physical simulation. Do not rebuild the renderer as a prerequisite for reactions. Do not replace native coherent sections with thousands of per-cell bodies. Do not let visibility control whether matter exists, and do not discard simulation debt at a residency or time-budget boundary.

This pass recommends independent implementations of the documented ideas. No external game source or assets were imported. Open source availability is not an assumption of unrestricted code reuse; any later source adoption needs its repository's actual license reviewed.

The main expected payoff is a **more interactive world per implemented rule**, with performance governed by active matter and bounded events. Actual speedups, visual acceptance and full-scene playability remain experimental gates.
