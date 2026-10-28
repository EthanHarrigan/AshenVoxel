# Independent CPU root-cache review

Reviewed 2026-10-27. Read-only review of the frozen controller, adapter, root and interaction APIs, test source, and recorded CPU evidence. No production/Assets edits, native execution, test rerun, or new agents. This note is the only new review artifact; no reproduction was needed.

## Result

No actionable correctness finding within the documented scope: one cooperative owner, the included `FrozenJob`, side-effect-free freshness callbacks, immutable provider revision identities, and caller-managed lease release. This is acceptance of the isolated CPU cache design at these identities, not production integration or a guarantee for arbitrary `IJob` implementations.

Reviewed identities:

| Source | SHA256 |
| --- | --- |
| `root-contact-cache-prototype/Controller.cs` | `1c95beb9f475f41bcb32ba0bbd408ee2ed90f17027db9ee72fdff3efee06599f` |
| `root-contact-cache-prototype/FrozenJob.cs` | `644081f435f5ef8337ecf62bf51f03610fa3853fb6ac3447f6655312cd7ee72e` |
| `root-contact-runtime-prototype/RootContact.cs` | `0154d84bb0f3ff2f7638ffe55c389f813237a4d438545a3f7a4d53902e314b8a` |
| `root-contact-interaction-prototype/Interaction.cs` | `8133a5181e77df83a94f66b6b4ae668c51e408ffce78df406a59bd01aa066f8e` |

All 36 source/input hashes in the cache report matched the current files. Existing evidence reports C#9/netstandard2.1 compilation and 313,824 assertions; most actual-data assertions are per-cell comparisons, not independent scenarios. I inspected that evidence rather than rerunning it.

## Correctness observations

- Identity includes template, terrain revision, root policy/seam, complete upper context, interaction policy, and exact pose. `Identity.For` uses the same policy identities supplied by the frozen builders (`Controller.cs:22`).
- `FrozenJob` carries the root result directly into interaction classification, then copies the matching cell index, pigment, and role into private arrays. Row30 retains neighbor context with `None`; owned rows below30 must have `Hard` or `Stick`. It publishes only after every cell is packed (`FrozenJob.cs:56–69`). The adapter nulls its array references after transfer. The immutable artifact exposes values only through a lease.
- Queue admission, worker startup, active continuation, final publication, and lease access check freshness. The final check occurs after capacity handling and before inserting the entry (`Controller.cs:199`). A stale pinned entry remains accounted, rejects reads, and is removed after its last lease releases. Same-identity replacement waits while the retired entry remains pinned; it cannot overwrite that entry and subsequently subtract the wrong replacement's bytes.
- Queued cancellation removes the queue and pending records; active/waiting cancellation cancels and disposes the producer and clears ticket callbacks (`Controller.cs:139–157`). `FrozenJob.Clear` releases its builder/result/packing references. Published artifacts survive producer disposal through their separate entry ownership.
- Worker admission includes the full reservation in the resident ledger. Publication checks the artifact size, ready entry/byte limits, and resident capacity. Transfer replaces the worker reservation; retired pinned artifacts continue to count. Pinned-capacity failures retain the completed worker and queued requests rather than silently discard them.
- Cooperative work is preserved at phase boundaries. A zero-candidate underlying phase transition can yield and resume on the next pump; a zero-progress running producer remains observable. Startup/publication work is charged independently from cell/query candidates. These units bound supplied work counts, not wall time.

The existing tests exercise FIFO/deduplication, queue-full retry, zero budget, LRU, pins, waiting publication, resident admission, cancellation, failure paths, producer identity/work contracts, stale leases/publication, partial actual packing, and exact actual cell/RGB/role payloads. The two actual artifacts contain 51,454 and 52,979 cells, including 1,344 and 1,441 context cells; combined ready accounting is 1,778,589 bytes.

## Boundaries that remain important

Freshness during final packing and after publication relies on the controller's supplied `Current` predicate. The adapter releases its `ICurrentIdentities` reference after interaction completion (`FrozenJob.cs:56`); the frozen builders' independent checks do not continue after their own completion. The documented wiring correctly checks both terrain and context identities in the controller. Do not describe a standalone completed `FrozenJob`, or a controller given `()=>true`, as independently protected against later revisions.

The reentry rejection is scoped to `Pump`; this review relies on the existing side-effect-free callback/cooperative-producer contract outside it. Arbitrary producer callbacks, caller-retained references, provider storage, CLR garbage-collection backlog, and native/GPU objects are outside the accounting guarantee already stated in the report.

No asynchronous worker, Unity scheduling, downstream mesh/collision publication transaction, edit/LOD integration, native residency, or frame-time behavior was reviewed or established here. Those remain separate adoption gates.

Evidence: `C:/Users/ethan/Downloads/AshenVoxel-WorldVoxels/.terrain-cache/root-contact-cache-prototype/report.json` and `checks.json`.
