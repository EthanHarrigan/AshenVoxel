# World-generation performance: measured results

2026-10-27, America/Denver. Follow-up to [the research report](WORLDGEN_SPEED_RESEARCH.md).
**Retained:** allocation-free material comparison and bounded two-worker column
sampling. **Tested but not adopted:** GPU quad rendering and four-worker default.
The retained changes preserve the exact 10 cm terrain and ordered face output.

## Result and scope

The native Unity comparison reduced tile generation work by **56.7% (2.31x)**
across eight representative locations. The recommended-spawn tile went from
**732 ms to 308 ms**. These are warmed tile-builder timings, not world loading
times or FPS. The improvement does not accelerate diffusion inference, model
loading, vegetation generation, frame-budget waits or whole-plan publication.

Hardware: Ryzen 7 5800X, 8 cores/16 logical processors; RTX 3080 Ti;
Unity 6000.3.27f1, URP 17.3.0, Windows/DX12. Tests used the current imported
`14fb32f2…` world, recommended spawn X=-19065/Z=-10200, 1280 m macro and 5 m
decoded samples. Existing 10 cm matter, full-detail spacing, pigments, material
layers, AO, face order, LOD selection and collision remain unchanged.

Eight cases cover two spawn tiles, decoded-edge and macro-only 10 cm tiles,
plus levels 2/5/8/9 with128/128/128/32 columns. Native run4 warmed every case/mode,
then made five observations per case/mode, reversing order on alternate repeats:
160 timed builds. Aggregates below sum the eight per-case medians; this weights
locations equally and is not an observed whole-world workload distribution.

| Native build strategy | Sum of tile medians | Versus original | Decision |
| --- | ---: | ---: | --- |
| Original serial builder | 4863 ms | 1.00x | Baseline |
| Remove enum boxing, serial sampling | 2590 ms | 1.88x | Keep |
| Remove boxing, two-worker sampling | 2105 ms | 2.31x | Keep |
| Remove boxing, four-worker sampling | 1757 ms | 2.77x | Do not enable by default |

| Location | Original | Retained strategy | Speedup |
| --- | ---: | ---: | ---: |
| Spawn, 10 cm | 732 ms | 308 ms | 2.37x |
| Spawn east, 10 cm | 650 ms | 313 ms | 2.08x |
| Decoded edge, 10 cm | 1265 ms | 582 ms | 2.17x |
| Macro-only, 10 cm | 933 ms | 420 ms | 2.22x |
| Near LOD | 328 ms | 119 ms | 2.76x |
| Middle LOD | 458 ms | 180 ms | 2.55x |
| Distant LOD | 471 ms | 173 ms | 2.72x |
| Rear LOD, serial small tile | 27 ms | 11 ms | 2.49x |

The native performance comparison used instrumented private copies of the
algorithms. The exact retained production method was then checked against the
frozen original in120 .NET builds with identical hashes. Installed Unity API
compilation and actual streamer lifecycle checks also pass. A fresh full-world
movement/FPS comparison of the retained production revision remains outstanding.

## What profiling found

The original builder spends much more time sorting/merging side faces than the
research-only estimate assumed. Sum of native stage medians: sampling1464 ms,
side merging3005 ms, total4863 ms. Main-thread vertex expansion added416 ms;
mesh upload added67 ms. Stage medians are calculated independently, so they
need not sum exactly to the median total.

`MaterialId` is a byte-backed enum. Calling `a.Material.CompareTo(b.Material)`
inside two face sorts boxes enum values on every material comparison. Comparing
their byte values preserves the ordering and eliminates that temporary garbage.
Native merge time fell to806 ms with serial sampling. This small fix is a better
first investment than replacing the renderer.

Windows .NET8 allocation accounting across40 builds per strategy measured:

| Strategy | Total managed allocations | Per-build average |
| --- | ---: | ---: |
| Original | 6,394,300,272 bytes / 6098 MiB | 152.5 MiB |
| Unboxed serial | 614,288,808 bytes / 586 MiB | 14.6 MiB |
| Unboxed two-worker | 614,351,976 bytes / 586 MiB | 14.6 MiB |

That is **90.4% less allocated memory**, not a90.4% reduction in resident RAM.
The native Mono allocation/process-memory API counters returned zero and are
unusable on this fixture; their raw zero fields do not mean zero allocations or
memory consumption. Resident geometry buffers have not been reduced by the
adopted CPU changes. The standalone process's recorded memory includes its own
test/data/runtime overhead and does not measure main-game memory.

## Multithreading without another streaming system

Only the initial column sampling runs concurrently. Rows write disjoint slices
of the same existing arrays; subsequent face generation and sorting keep their
deterministic order. There is still one in-flight tile, one retained cancellation
token and the existing upload/queue/admission policies. No extra tile buffers,
cache hierarchy, ECS/Burst package or work scheduler was added.

The runtime opts into at most two sampling workers on its immutable captured
source. Public callers remain serial by default. Tiles below128 columns and
machines exposing at most two processors also stay serial. Cancellation checks
remain in the sampling loop and use the same token in `Parallel.For`.

Two workers reduced sampling from1444 ms to847 ms after removing boxing, giving
a further **1.23x** overall builder improvement over unboxed serial. Four workers
gave another20% throughput beyond two on this test, but consume more CPU capacity
while gameplay, population and uploads also need time. We did not establish a
better frame-time distribution with four workers, so the runtime uses two.
Thread-pool scheduling has small allocations and CPU/stack overhead; it is not
free. Mutable custom fields must not opt into concurrent sampling.

The adopted runtime delta is confined to
[VoxelLodSurface.cs](../Assets/AshenVoxel/WorldScaleData/VoxelLodSurface.cs) and one
call in [WorldExplorerVoxels.cs](../Assets/AshenVoxel/WorldScaleRuntime/WorldExplorerVoxels.cs).
Generator version remains1 because generated geometry and appearance are equal;
save formats and source identities are unchanged.

## GPU quad experiment

The prototype stores float position/extent, tint, four corner AO values,
roughness, wetness, material identity, axis and sign in32 bytes per rectangle.
The vertex shader reconstructs the same six triangle vertices, diagonal and
winding. This is GPU vertex reconstruction, not GPU terrain generation.

Existing geometry costs120 bytes per face: four24-byte vertices plus six4-byte
indices. The prototype costs32 bytes: **73.3% less payload / 3.75x smaller**.
The spawn tile falls from8.51 MiB to2.27 MiB of resident geometry payload.
Generation scratch arrays, face lists, driver allocation overhead and other world
resources are outside those numbers. Keeping both renderers resident would lose
part of the memory benefit; the proposed path would have to replace those buffers.

Across the two-worker native rows, aggregate packing/upload work was59 ms versus
501 ms for expansion/mesh upload, about **8.5x faster for that stage**. Including
the measured generation work gives a hypothetical2606→2165 ms, only **1.20x**
additional synchronous pipeline speedup. That is arithmetic from isolated stages,
not a completed integrated loading measurement. Yield/frame caps could change
the result in either direction.

The isolated DX12 shader passed32 comparisons: eight terrains × forward,
shadow-caster, depth-only and depth-normal passes. The corrected lighting run had
zero pixels differing by more than1e-5 and maximum RGB error1.79e-7. Depth was
read from actual depth buffers; nonempty output and visible material pixels were
required. This verifies these draw calls, not URP integration or rendered shadow
reception in the full game.

Draw speed was **not consistently better**. In the corrected lit test, median
32-draw completed batches ranged from about9% faster to28% slower than meshes;
the spawn batch was effectively unchanged. The measurement includes command
submission, GPU work and a completion readback, not a GPU timestamp or game FPS.
Procedural triangles can invoke six vertices per quad where indexed meshes reuse
four; decoding, structured-buffer reads and lost index reuse can offset bandwidth
savings. The result depends on scene coverage, shading and hardware.

Adoption would require buffer ownership/eviction, frustum and shadow submission,
contact masks, rebasing, source/edit invalidation, and compatibility with the
concurrent patch5 material encoding. Those are real maintenance costs. The tested
shader is frozen before patch5; it does not validate that later material revision.
**Keep the ordinary renderer.** The prototype stays under
[Tools/TerrainPerformance](../Tools/TerrainPerformance/NativeBenchmark.cs), outside
gameplay assemblies, so it adds no runtime buffers or shader variants to the game.

## Verification and limitations

- 30 data checks, including exact serial/parallel face equality, mutable-field
  serial default, small-tile fallback, pre-cancellation and in-flight cancellation.
- 19 actual streamer checks against Unity substitutes: upload cap, contact10 cm,
  byte admission, turn/travel/rebase, invalidation, retry queues and shutdown.
- 31 actual-asset bootstrap checks against Unity substitutes; five runtime
  assemblies compile against installed Unity APIs.
- 120 exact retained-method builds on actual world data compare every height and
  ordered face field against the frozen original. No appearance reduction.
- 160 native comparative builds and corrected32 native render-pass comparisons.

Native and .NET timings are not interchangeable. Some distant generated values
differ across Mono/.NET floating-point math; equality is checked within each
runtime, not asserted across runtimes. The profiling fixture used double literals
for snow depth2.4 and winter fraction0.2; final retained verification uses the
actual Unity float values promoted to double. That correction preserved all
eight .NET geometry hashes; the different source strings are retained as evidence.
The120 retained-method checks ran alongside other CPU checks, so use them for
correctness, not standalone performance claims.

After material patch5 landed, a second120-build retained-method comparison,
30 data checks and five installed-API assemblies passed on the combined source.
Those reports are separately preserved in `adopted-after-material-patch5.json`
and `after-material-patch5`; the native timings above still describe their
original pre-patch5 source. Project integrity175 assets/11 assemblies and
documentation91 Markdown files/394 links also pass.

The project has concurrent quality/material work. This report records its own
source hashes and does not relabel another chat's native runs or release ZIP.
No main Play restart or OS/driver change was made by this work. The user resumed
Play separately after the isolated tests. Full-world startup duration, p95 frame
time during travel, peak resident memory and repeated edit/rebase render parity
remain adoption gates for a future GPU renderer, not completed outcomes here.

## Evidence and reproduction

[Evidence directory](TerrainPerformanceEvidence/2026-10-27/summary.json) contains
raw per-build data, source manifest, preserved failures, snapshots of retained
sources, validation reports and SHA256 records. Key files:

- [Native CPU/stage comparison](TerrainPerformanceEvidence/2026-10-27/native-run-4/results.json).
- [Corrected lit GPU comparison](TerrainPerformanceEvidence/2026-10-27/native-run-5/results.json).
- [.NET allocation comparison](TerrainPerformanceEvidence/2026-10-27/parallel-unboxed/cpu-20261026-235901.json).
- [Exact retained implementation verification](TerrainPerformanceEvidence/2026-10-27/adopted.json).
- [Checks and hashes](TerrainPerformanceEvidence/2026-10-27/evidence-sha256.json).

Native runs1/2 failed prototype shader compilation; the repaired run3 was stopped
when another bounded GPU fixture overlapped. Their logs/partial evidence remain
historical failures, not successful timings. Run4 completed; its dark forward
image made it unsuitable for appearance conclusions. Run5 explicitly initialized
URP lighting globals, required lit pixels and passed. Use run4 for CPU stages and
run5 for lit draw comparisons. Neither is a full Exploration playtest.

`Tools/TerrainPerformance/prepare.py --experiment --unbox --run` instruments the
frozen pre-change source. It must retain that baseline; a fresh snapshot of already
optimized code is not an original baseline. `verify_adopted.py` compiles the live
retained method against it. `prepare_native.py` creates a separate minimal Unity
project once; `TerrainNativeBenchmark.Run` runs the full comparison and
`RunVisual` the shorter lit test. Never overlap native GPU timing with another
benchmark or a full-world Editor load. Test-only generated sources/binaries and
the copied world data remain in ignored `.terrain-cache`, outside Assets.
