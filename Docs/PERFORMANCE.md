# Performance, scale, and validation contract

Current exploration-specific budgets and limitations are in
[AI_HANDOFF.md](AI_HANDOFF.md) and [TODO.md](../TODO.md). Official-API C# compilation
has since passed; native Editor/HLSL/play/GPU/FPS remains unexecuted. The volume
arithmetic below applies to matter storage, not the exploration height proxies.

No FPS or hardware capacity claim is established by the repository alone. The initial environment did not contain a Unity Editor, so Unity compilation, GPU profiling, visual quality, and gameplay feel require a later editor/player run. This document defines what to measure and what must be true before the foundation is called production-ready.

## 1. Required 10 cm scale and optional comparisons

The current requirement is **one voxel per 10 cm**, shared by generation, matter, simulation, collision, and display. The 5 cm and 2 cm rows below illustrate the cost of optional future changes; they are not current targets.

For a completely populated volume of side lengths `Lx`, `Ly`, `Lz` meters and voxel size `s` meters:

`cellCount = ceil(Lx/s) * ceil(Ly/s) * ceil(Lz/s)`

At one byte of material per cell, material bytes equal cell count. This excludes all metadata and derived data.

| Cell width | Cells per cubic meter | Material for 1 m³ | Material for 32 × 16 × 32 m | Relative to 10 cm |
| --- | ---: | ---: | ---: | ---: |
| 10 cm | 1,000 | 1,000 B | 16,384,000 B = 15.625 MiB | 1× |
| 5 cm | 8,000 | 8,000 B | 131,072,000 B = 125 MiB | 8× |
| 2 cm | 125,000 | 125,000 B | 2,048,000,000 B ≈ 1.907 GiB | 125× |

The table uses a volume whose dimensions divide evenly at all three scales. It is not a claim that every cell is non-air in a landscape. Sparse storage can omit empty chunks, but the ground itself is largely occupied.

For a 100 × 32 × 100 m domain, raw material payload is 320,000,000 bytes at 10 cm, 2,560,000,000 bytes at 5 cm, and 40,000,000,000 bytes at 2 cm. A full 2 cm volume of that size is about 37.25 GiB before meshes, simulation, or collision. Uniform fine resolution across a large solid domain needs compression, streaming, or a multiscale representation.

Surface costs scale differently from volume costs. For a smooth fixed physical surface, halving cell width can roughly quadruple the number of exposed unit faces before greedy merging. Highly fragmented destruction can expose interior surfaces and destroy that favorable relationship. Do not extrapolate terrain triangle counts from an undamaged flat plane.

## 2. Chunk payload and real memory

A 16³ chunk holds 4,096 cells. At 10 cm it spans 1.6 m; at 5 cm, 0.8 m; at 2 cm, 0.32 m. Keeping cell count fixed makes physical dirty regions smaller as fidelity increases, but increases the number of chunks needed for the same world.

| Item | Lower-bound payload | Additional cost |
| --- | --- | --- |
| Material chunk | 4,096 bytes at 8-bit ID | Array/object headers, dictionary entry, coordinate, counters |
| Active candidate | Coordinate plus queue entry | Deduplication set, movement stamps, backing capacity |
| Mesh vertex | Position, normal, color, optional UV | Alignment, Unity CPU copy, GPU buffer, upload staging |
| Mesh triangle | Three indices | Index width, submesh/mesh metadata |
| Collider | Not represented by material payload | Cooked physics data, mesh access, broadphase structures |
| Thermal channel | At least one value per active cell | Sparse channel metadata and scheduling |
| Support/island state | Depends on graph/label representation | Component frontier, labels, rigid collision |

Measure actual retained memory rather than summing only material arrays. Memory Profiler should separate managed arrays, native Unity meshes, GPU buffers, physics data, and temporary allocation peaks.

A pathological chunk can contain many disconnected surfaces. A naive exposed-face mesh can approach `6 * 4096` quads in an upper-bound allocation model; ordinary occupancy reduces the achievable count, but the conservative bound is useful for buffer safety. Four unshared vertices per quad exceed 65,535 vertices at that bound, so meshing must choose 32-bit indices or split geometry safely. Never rely on a pleasant terrain scene to prove index safety.

## 3. Frame budget targets

A target is a gate, not a present result. Choose target hardware before treating these allocations as commitments.

| Display target | Total frame time | Implication |
| --- | ---: | --- |
| 30 FPS | 33.33 ms | More simulation/rendering headroom, higher input latency |
| 60 FPS | 16.67 ms | Sensible first desktop combat target |
| 120 FPS | 8.33 ms | Requires much tighter CPU and GPU costs |

For an initial 60 FPS profiling target, reserve several milliseconds of headroom rather than allocating all 16.67 ms on paper. A provisional CPU allocation might reserve roughly 1–2 ms for granular processing and 1–3 ms for meshing/uploads/collider work during typical edits, while leaving room for characters, boss logic, animation, physics, and the render thread. Those numbers must be replaced with measured values and worst-case behavior on the minimum target hardware.

Count budgets only bound the number of items processed. They do not guarantee a duration. Rebuilding one intricate chunk or cooking one collider can cost more than rebuilding several smooth chunks. If one item exceeds the budget, the production design needs a finer work unit, background jobs, simpler collision, or a fallback representation.

## 4. Metrics that must be visible

Capture at least:

- Main-thread, render-thread, and GPU frame times, separately.
- Frame-time p50, p95, p99, and worst frame over the test interval.
- Allocated bytes per frame and GC events during steady play and destructive bursts.
- Resident chunk count, occupied cell count, material payload, and total memory.
- Dirty render chunks, dirty collision chunks, and age of the oldest pending update.
- Cells considered, grains moved, active frontier size, and time until a disturbed pile sleeps.
- Meshing time, upload time, collider cooking time, vertices, triangles, and visible draw count.
- Authoritative edit duration and edited cell/chunk counts per attack.
- Visible latency from edit to mesh and from edit to collision.
- Number of active rigid islands and total dynamic collision complexity when islands arrive.
- Streaming requests, pending jobs, cancellations, and persistence I/O when those systems arrive.

An FPS counter alone hides the distinction between a smooth idle scene and a stuttering boss attack. Queue lengths alone hide starvation. A stable average hides intermittent 100 ms collider cooking spikes.

## 5. Reproducible benchmark protocol

1. Record Unity editor version, package versions, Git revision or archive hash, OS, CPU, GPU, driver, RAM, graphics API, display resolution, quality settings, and whether VSync/frame caps are enabled.
2. Use a standalone development player for profiler capture, then a release player for end-user frame timing. Editor overhead is useful for debugging but not a final performance claim.
3. Fix the world seed, physical world dimensions, voxel size, generation profile, attack sequence, simulation step policy, and camera route. The required scale is 10 cm. For optional resolution comparisons, keep the same physical domain and meter-valued GenerationProfile; generation v5 shares a continuous height field across those grids, with expected rasterization differences.
4. Separate cold start/generation from warm steady play. Capture both when reporting startup behavior.
5. Warm shader variants and relevant code paths deliberately; record whether a test includes compilation or first-use upload.
6. Run a sufficiently long interval to include multiple destructive events and settling periods. Keep raw captures and a machine-readable summary.
7. Compare the same scene and camera route when changing resolution or renderer. Do not lower draw distance silently.
8. Inspect visuals alongside timing. A faster path that loses shadows, material response, or edit correctness is a different quality level.
9. Change one major variable at a time and rerun only the affected scenarios plus core checks.

## 6. Baseline scenes

| Scenario | Purpose | Required observations |
| --- | --- | --- |
| Idle forest | Steady presentation cost | No growing frontier, no recurring rebuilds, bounded allocations |
| Single small dig | Response latency | Authoritative hit updates immediately; bounded mesh/collision delay |
| Cross-chunk crater | Neighbor invalidation | No hidden faces, holes, duplicate seams, or lost boundary edits |
| Large boss slam | Burst workload | p99 frame time, edited volume, dirty queue age, collider spike |
| Repeated slams | Sustained overload | Backlog growth/decay; explicit saturation behavior |
| Granular shelf | Active simulation | Conservation, movement count, queue decay, settling pattern |
| Tree grove cut | Structural work when available | Bounded component scans and rigid bodies |
| Fine silhouette walk | Rendering stability | Shimmer, aliasing, shadow stability, LOD transitions |
| Save/load | Persistence | Roundtrip equality, load memory peak, safe failure on corruption |
| Streaming return | Residency when available | Exact edits restored and no chunk-border generation seams |

## 7. Correctness checks before performance optimization

Core checks should cover material ID validity; chunk addressing; allocation/removal of empty chunks; dirty neighbor marking; sphere bounds; ray traversal; deterministic generation under its stated scope; granular conservation; fixed-step repeatability; snapshot roundtrip; and invalid/corrupt snapshot rejection.

The [independent ray-query validation](VALIDATION_RAYCAST.md) additionally compares actual DDA targeting with exhaustive per-cell AABB intersections across fixed-seed randomized and explicit boundary cases. This is a geometric oracle, not a duplicate traversal implementation.

These tests do not replace visual checks. Incorrect triangle winding, normal direction, shader compilation, shadows, mesh cleanup, collider behavior, cursor lock, and Unity lifecycle behavior require Unity-side validation.

[Engine-independent validation](VALIDATION_CORE.md) has passed using the .NET 8 Roslyn compiler, including generation profile and shared physical-height checks. That result does not establish Unity compilation or rendering correctness; the linked report records the command, fixture, observations, and current check count.

A useful optimization admission test compares the optimized path against the reference on small fixed worlds. Compare material hashes after the same steps and commands, and compare surface occupancy rather than requiring identical vertex order. GPU floating-point shading need not be bit-identical, but authoritative edit semantics need a defined tolerance or exact integer result.

## 8. Budgeted simulation and determinism

A fixed seed does not imply deterministic gameplay. Reproducibility also depends on command order, simulation tick count, candidate order, conflict resolution, and budget policy.

For a deterministic fixture:

- Use a fixed tick rate and fixed candidate budget per tick.
- Insert commands at named tick boundaries.
- Avoid ordering derived from an unspecified hash-map enumeration.
- Resolve simultaneous movement conflicts explicitly.
- Prevent a grain from moving multiple times accidentally in one tick.
- Hash the complete material state at known checkpoints.

The current model moves each grain at most one downward cell per call and has no acceleration or momentum. At 30 Hz and 10 cm, the maximum vertical rate is 3 m/s when processed every tick, reduced by backlog. This is a cellular transport limit, not a measured terminal velocity.

Changing a budget may change when cells get a chance to move and therefore the final arrangement. A wall-clock budget will vary by hardware. If future multiplayer requires lockstep, schedule semantics become part of the game protocol; otherwise use server authority and correction rather than assuming identical local simulation.

Tick catch-up must be bounded too. If a long frame accumulates dozens of physics ticks, processing all of them immediately can cause a spiral of increasing delay. Define a maximum catch-up policy and choose whether excess delay slows the simulation or drops presentation time.

## 9. Jobs and Burst migration

Do not pass managed dictionaries or Unity objects directly into jobs. First establish data ownership:

1. Gather an immutable chunk snapshot and required neighbor halo, or use a native residency structure with explicit lifetime.
2. Associate work with a chunk revision and representation version.
3. Run independent meshing or classification in jobs using native containers.
4. Before applying output, reject it if the source revision changed or the chunk was unloaded.
5. Apply Unity Mesh and Collider operations on the appropriate Unity thread, measuring their cost separately from generation.
6. Dispose native buffers on success, cancellation, unload, and scene teardown.

A fast background mesh can still be followed by an expensive main-thread upload or collider cook. Track each phase. Concurrent edits need a predictable read/write policy; “jobs” alone do not make shared mutable voxels safe.

For granular updates, use explicit read/write phases, colored partitions, or deterministic atomic claims. A parallel in-place update can race at destination cells, move matter twice, or produce nondeterministic volume loss.

## 10. GPU brick prototype requirements

A sparse-brick renderer should be built as a comparative prototype before replacing the mesh path. Keep the same material grid and edit fixtures while evaluating:

- Brick occupancy hierarchy and empty-space skipping.
- Brick allocation, residency, eviction, and upload granularity.
- Material lookup bandwidth and cache behavior.
- Ray divergence and iteration limits under grazing camera angles.
- Primary visibility separately from shadows and indirect lighting.
- Depth and motion-vector integration with conventional characters and particles.
- Temporal accumulation reset around edits and disocclusions.
- Subpixel detail, thin foliage, and noise/shimmer during motion.
- GPU/CPU synchronization hazards and readback latency.
- Fallbacks for hardware without a chosen feature set.

Dynamic edits must not trigger a complete world texture upload. At the same time, thousands of tiny uploads can overwhelm submission overhead; aggregate changed bricks into bounded batches and measure the threshold.

Do not interpret a high static-scene FPS number as proof of destructive-world performance. Include a repeated-impact scene with residency updates and lighting changes.

## 11. Collision latency contract

Rendering and collision have different costs and deadlines. A production near-player terrain edit should have a measured maximum collision lag and an explicit recovery path.

Track separately:

- Edit accepted to authoritative material change.
- Material change to visible mesh.
- Material change to collision proxy.
- Collision proxy update to stable character contact.

Prioritize chunks intersecting the player's movement envelope. Conservative temporary collision or direct grid collision may be needed if cooking cannot meet the deadline. Do not assume that rebuilding every collider every frame is safe because the renderer only rebuilds a few meshes.

Dynamic islands need their own simplified colliders. A fine nonconvex terrain mesh is not automatically a suitable dynamic Rigidbody shape.

## 12. Overload behavior

Before a production boss encounter, define the behavior when workload exceeds capacity:

- Keep authoritative edits and input responsive within a bounded command budget.
- Prioritize nearby visible chunks and nearby collision, while avoiding starvation of older work.
- Bound visual debris independently from matter.
- Delay distant cosmetic work before compromising nearby collision.
- Reduce optional shading quality or detail under a documented quality tier.
- Expose backlog and saturation in the profiler/debug HUD.
- Never silently discard persistent edits to make the queue smaller.

A bounded queue that drops matter is not a correct optimization. If queued work must be coalesced, preserve the latest authoritative state and a revision that proves what the consumer has processed.

## 13. Exit criteria for a strong foundation

The foundation is ready for broader game production when:

1. A supported Unity editor imports the project with no compile errors and the demo is manually playable.
2. Core correctness checks pass, including destructive edits and save/load corruption tests.
3. Target hardware and target FPS are named and benchmark captures meet their budgets.
4. Sustained boss-like destruction does not cause unbounded dirty queues, allocation growth, or collision failures.
5. A representative forest cut exposes the intended materials and retains coherent visual quality during movement.
6. Any unsupported structural or reaction behavior is a deliberate scope choice rather than an accidental assumption.
7. Persistence survives interrupted writes and world-format changes under a documented migration policy.
8. The next renderer or simulation optimization can be evaluated against a reproducible baseline.

Until then, the repository is a foundation prototype with useful architecture and experiments, not a proven high-performance microvoxel engine.

## 14. Current generation/color costs and next measurements

The [pure-core benchmark](Benchmark/RESULTS.md) records its exact generation version, source hashes and environment. Generation4 introduced species scaffolds and coherent material tint data; Generation5 adds mature redwoods and physical bark relief. The small forest-arena benchmark does not contain a full-height redwood and cannot establish its cost. Container CPU results exclude Unity mesh upload, rendering, colliders and GPU work.

1. Profile the same species scene in an actual Unity development player, including full and clipped crowns, orbit motion, cuts and repeated color edits.
2. Measure material and tint arrays separately. Optional uint appearance adds16KiB per tinted chunk to the4KiB material payload. Fully tinted raw cell data costs5bytes/cell. Rendering halo color scratch adds18³×4=23,328bytes per mesher.
3. Compare triangles and mesh time with varied versus uniform tint at identical occupancy. Actual RGB plus corner AO constrain greedy merges. Coherent quantized palettes help, but do not remove the cost. Evaluate color-brick shader lookup or indexed palette fields before accepting per-cell color splits at much larger scale.
4. Track per-tree branch count, candidate count, foliage clusters, generated cells, color count, truncation and wall-clock generation time. Candidate caps bound work units, not milliseconds; generation is synchronous per tree.
5. Keep granular wake costs visible. A successful move can inspect many neighboring cells despite a fixed candidate budget. Preserve sleeping regions and conservation while evaluating native/Burst activation structures.
6. Save Format2 streams optional appearance channels with CRC; Format1 migration retains historical matter. Output-buffer allocations differ from writer auxiliary allocation. Load remains a validated capped-buffer reconstruction and can stall; larger worlds need coherent asynchronous snapshots and region persistence.
7. Record visual/cache queue age under repeated changes. A fast idle frame is not evidence for terrain destruction or highly fragmented foliage.

Offline CPU inspection and optional path-traced beauty studies help assess geometry and lighting choices. Their timings and appearance do not establish the current URP player's performance or visual parity.

## 15. Large-world and neural terrain budgets

The [world-scale investigation](WORLD_SCALE_RESEARCH.md) separates visible range,
matter residency, mesh residency, model generation, collision and active
simulation. The current WorldExplorer uses a bounded height-proxy plan and
separate temporary10 cm inspection mode. Its finest proxy spacing is2 m, not a
completed100 m microvoxel ring. GPU occlusion, streaming trees and persistent
continuous edits are not implemented merely by introducing a global sampler.

Actual official-model CPU inference took about92–94 s for a first64² raw query;
the padded canonical-owner bridge took185 s for its first tested region because
it generated a larger fixed context. Cached repeats are cheap, but do not
establish streaming throughput. Peak observed process memory was roughly2.8 GiB
with about1.14 GB of model weights and four CPU threads. GPU deployment must be
measured independently, including competition with Unity's meshes and textures.

Do not generate full native30 m rasters over each enormous far tile. Use
explicit rough far previews and bounded nearby model requests, and record when
rough and detailed fields differ. Source identity, request/backlog counts,
generation failures, cache occupancy and provisional/neural tile counts remain
observable. Rendering culling must not silently erase pending authoritative
simulation or edits.
