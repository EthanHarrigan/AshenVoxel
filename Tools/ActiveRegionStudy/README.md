# Active-region scheduling study

Standalone research harness. **No Assets assembly references these candidates.**
The normal game and Unity controls are unchanged by this study.

Run from the project root with installed .NET 8:

```powershell
python Tools/ActiveRegionStudy/run_study.py --output Docs/SimulationResearch/2026-10-27/active-regions-new-run
```

The output directory must be new. The runner freezes the compiled source under
ignored `bin/`, records hashes, compiles optimized C# with tiered compilation off,
preserves logs on failure, and records source changes during execution. It does
not download packages or replace production source. Build binaries stay in bin.

`PagedCellSet.cs` replaces only the two membership HashSets in a generated copy
of current `Simulation.cs`. Movement, wake behavior, FIFO order and tick semantics
remain unchanged. This is **chunk-based membership, not a completed region job
scheduler**. Bitmap pages are retained until Release; production streaming needs
bounded retirement. An empty moved set can still retain storage.

`DormantRegionIndex.cs` is a spatial query over settled parcel positions. It
supports insertion, move and removal with contiguous bucket entries. It is not
integrated with live physics, nor is its query resumable or safe for arbitrary
user-supplied radii. Only small finite radii are tested. A separate bounds-then-
scan comparison establishes whether indexing is worth its cost.

Checks cover signed chunk boundaries, duplicate membership, epoch clear/release,
ordered material-event equivalence, variable/zero budgets, wake scan, material and
tint equality, dirty boundaries, eventual drainage, and spatial query equivalence
after move/removal. All ten runs of each gravity fixture must have identical final
snapshot SHA-256, candidate counts, moves and call counts. The real current debris
solver supplies a synthetic 100,001-cell released fixture, weighted to4,001 parcels.

Measured gravity workloads include initial wake scans and each Step until quiet;
world construction and final serialization are excluded. Allocation counts cover
the measured workload, including authoritative chunk/tint allocations; they are
not membership-only or peak resident memory. Five fresh runs per variant alternate
order. Both code paths are warmed. Per-query batches run1,000 iterations, seven
times, on an immutable settled snapshot; all alternatives produce matching ID lists.
Actual debris Step timing is separate from query-only timings. Tiny bounds-only
nanosecond measurements should be treated as an operation-count observation, not
a useful frame-time promise. Host load and thermal state are uncontrolled.

Current evidence and interpretation:
[ACTIVE_REGION_SCHEDULING_2026-10-27.md](../../Docs/ACTIVE_REGION_SCHEDULING_2026-10-27.md).
