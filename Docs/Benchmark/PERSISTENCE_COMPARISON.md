> Historical Generation3 / Format1 optimization comparison. Current Generation4 adds intrinsic color channels and different tree geometry; use RESULTS.md and its source-hashed JSON for the current baseline. These older and newer workloads are not directly equivalent.

# Persistence optimization observation

Two executions of the same optimized .NET benchmark in this container compared
the old buffered/bitwise-CRC snapshot implementation with the new streaming,
table-driven-CRC implementation. Both used generator v3, the same 32-edit-pair
workload, and the same settled material contents. Both passed exact round-trip
content and metadata checks. The file schema remains Format1.

| In-memory workload | Earlier implementation | Current implementation |
| --- | ---: | ---: |
| Save p50, 3 samples | 212.84 ms | 30.86 ms |
| Load p50, 3 samples | 353.17 ms | 186.71 ms |
| Save mean thread allocation | 47.13 MB | 23.08 MB |

The earlier execution was at `2026-10-26T04:24:31.6027427Z`; the final execution
was at `2026-10-26T05:05:06.0346128Z`. The current full raw results and source
hashes are in `core-baseline.json`; the earlier numbers are retained here as a
summary observation, not a second complete raw-data archive. The final core also
includes corrected diagonal AO-halo dirty invalidation and read-only cell views;
the material contents and snapshot format are unchanged. These are small
sample sets with uncontrolled host load and are not a reliable speedup guarantee.

The benchmark's save operation creates a `MemoryStream` and calls `ToArray`, so
its allocations include destination growth and a complete copied result. The
separate core validation observed **22,056 bytes** of writer allocation when
writing the unedited default world to `Stream.Null`; that excludes all
destination buffering. These measurements answer different questions. The
current load still stores a capped payload before validation and import.

Core validation additionally compares the unchanged default world's full
6,079,884-byte output against the previous implementation byte-for-byte, and
pins a smaller golden snapshot hash. See `../VALIDATION_CORE.md` for those
compatibility checks. No disk, Unity-main-thread, or player-frame timing is
established by this comparison.
