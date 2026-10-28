# Pure-core workload baseline

`core-baseline.json` records an executed **optimized .NET 8** workload against the
actual C# material world, generator, brushes, gravity, DDA and snapshot code.
`RESULTS.md` is its generated readable summary.
`PERSISTENCE_COMPARISON.md` retains the earlier/current save observation. These are container observations,
**not Unity FPS, GPU measurements, target-machine budgets or shipping claims**.
There is no meshing, graphics upload, shader execution, collision proxy, boss
animation, post-processing or Unity scheduling in this harness.

Reproduce from the project root:

```sh
python3 Tools/Benchmarks/run_benchmark.py
# Optional explicit SDK:
python3 Tools/Benchmarks/run_benchmark.py --dotnet /path/to/dotnet
```

The runner needs Python 3 and the installed .NET 8 SDK/reference pack. It compiles
with Roslyn using `-optimize+`, no package restore and no network. It records the
CPU model, architecture, runtime/SDK, logical CPU count and SHA-256 of each core
source. A source edit during the run causes failure instead of silently assigning
mixed-source provenance to the result. CPU quota, host load, thermal behavior and
available memory are not controlled. GC and JIT/tiered-compilation effects remain
part of these process observations.

## Workloads and correctness gates

1. A smaller world warms generation and gravity code. Five independent complete
   256 × 128 × 256-cell, 10 cm worlds are generated at seed `714103`. No scene
   object or renderer is created. This is a 25.6 × 12.8 × 25.6 m finite domain.
2. Thirty-two deterministic positions around the arena receive a 0.85 m carve
   and a suspended 0.45 m sand placement. Each pair is followed by four gravity
   calls with at most 1,800 queued candidates per call. Brush statistics combine
   carve and placement calls; use raw samples or split the harness for separate
   operation distributions.
3. The active gravity frontier is drained with the same candidate budget.
   Material counts before and after pure simulation must match exactly, and
   total occupied cells must equal the initial cells minus successful removals
   plus successful placements. The maximum processed candidate count is checked.
   Active and idle timings are kept in separate distributions. The harness has
   a 10,000-tick safety bound and records `final_active`; a nonzero value signals
   incomplete draining rather than evidence of full settling.
4. Sixteen batches of 1,000 oblique downward DDA queries sample the finite world.
   Timings are **per batch**, not per ray. Hit counts are recorded; rays that
   exit the domain can legitimately miss.
5. Three full in-memory snapshot save/load cycles compare deterministic content
   hashes, material counts, dimensions, voxel size and seed. Save includes
   creating and copying a `MemoryStream`; load validates the current checksum
   and rebuilds the sparse world. This does not measure disk latency, atomic
   rename, crash recovery or the runtime save coordinator.

Raw millisecond samples and process-thread allocated-byte averages are retained.
The tiny stopwatch/sample bookkeeping cost is included in allocation metrics;
these are not total resident memory measurements. Percentiles use linearly
interpolated order statistics. Five generation and three snapshot samples are
useful for a rough baseline, **not stable tail-latency estimates**. The larger
active-simulation set is still only one deterministic scene/workload.

## Reading the result

Use these numbers to identify where to profile next. The main-thread cost of a
candidate-budgeted call can still be substantial, and two calls in one rendered
frame roughly add their CPU work before graphics are considered. A count budget
does not promise a millisecond budget. Snapshot save/load and generation are
synchronous and need explicit loading-state or background-work architecture as
the world grows.

The test runner in `Tools/run_core_checks.py` serves correctness and is compiled
without the benchmark's explicit optimization flag. Its single fixture timing
can differ considerably; neither number should be presented as a Unity build
measurement. Follow `Docs/PERFORMANCE.md` and `Docs/PLAYTEST.md` for real Editor
and player-build validation.
