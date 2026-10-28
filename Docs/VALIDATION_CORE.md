# Engine-independent core validation

Material optics checkpoint (2026-10-27 UTC, Windows/.NET8.0.407): **91 Core
checks** and **55 material checks** pass with catalogue4/25 IDs. Four Core checks
cover independent optical defaults, exact material/AO render encoding, soft Web
body policy and Web roundtrip in unchanged Format2. Historical IDs/pigments,
Format1 migration, corruption handling and gravity remain covered. Authoritative
generator sources are unchanged. Native rendering is verified separately in
[patch5](QualityPatches/patch-5/README.md); these CPU checks are not GPU/FPS evidence.

Executed in the development Linux container using .NET SDK 8.0.427 on 2026-10-26. **87 checks passed**, with compiler warnings treated as errors. The same core also compiled using C# 9 against the installed .NET Standard 2.1 reference assemblies, covering the Unity API compatibility baseline for these engine-independent sources. An external-assembly compiler probe also verifies that callers cannot write the `ReadOnlySpan` material view or access the internal backing array. This validates the C# core on .NET 8; it does not establish Unity Editor compilation, scene correctness, GPU behavior, or player-build frame rates.

## Reproduce

Rock generation2 subsequently reran the unchanged87 Core checks. Its separate
69-check shape suite and51-check habitat suite validate the new silhouettes;
42 workshop and65 explorer checks use actual runtime sources with Unity
substitutes. See [rock shapes](ROCK_SHAPES_UPDATE.md).

The subsequent ground-cover update reran all87 Core checks with catalog version3
and24 IDs. The material suite now passes53 checks, adding Fungus/Petal round trips
while preserving original material IDs/colors. Shape/habitat suites are separate:
93 plant,51 rock and50 ground-cover checks, all with C#9/.NET Standard2.1
compilation. Actor-body policy is separate from solid matter/tool targeting;
soft tissue remains DDA-targetable. See [ground-cover report](GROUNDCOVER_UPDATE.md).

The surface-material update reran all87 checks after expanding the append-only
catalog to22 IDs. Generation5 and the historical arena fixture below are
unchanged. The separate49-check material suite verifies1221 old generated
colors, an actual old Format2 snapshot, new material/tint round trips and
hardness/granular behavior. See [surface verification](SURFACE_MATERIALS_UPDATE.md)
and `Docs/material-validation.json`. Older evidence remains historical where
its recorded source hashes predate this catalog extension.

From the project root, with Python 3 and .NET 8 SDK installed:

```sh
python3 Tools/run_core_checks.py
```

If needed:

```sh
python3 Tools/run_core_checks.py --dotnet /path/to/dotnet
```

The script invokes the installed SDK's Roslyn compiler against the installed .NET reference assemblies. It performs no NuGet restore and needs no network. This route avoids this container's restricted named-mutex support, which prevented the normal `dotnet run` restore workflow. A normal development machine can also run `dotnet run --project Tools/CoreChecks/CoreChecks.csproj`.

## Covered behavior

- Sparse allocation, address mapping, empty-chunk release, idempotent writes, negative floor division, bounds handling, and positive/negative corner halo invalidation, four-chunk edge halos, single-owner interior invalidation, finite-domain clipping, and exactly one edit-origin event.
- One-cell-per-call particle movement, mass conservation, eventual sleeping, strict candidate budgets, a one-candidate budget reaching rest, and removal of support waking a sleeping grain under that same budget.
- Brush hardness, immutable tool bedrock, and placement preserving existing solids.
- Axis-aligned DDA, normalized metric distance, finite-domain entry, maximum reach, zero/NaN directions, maximum-domain boundaries, and rays facing away from an exact solid boundary.
- Ten thousand warmed DDA calls allocate zero managed bytes on this runtime after moving traversal scratch arrays to stack storage.
- Historical Format 1 golden bytes load without losing material data and migrate to Format 2; nonseekable destinations work, caller streams remain open, failed writes propagate, partial output is rejected, and nonwritable destinations are rejected.
- Snapshot metadata/cell round trips, stable byte serialization, checksum failure, truncation, unexpected trailing bytes, future-format rejection, and valid-checksum future-generator rejection.
- Seeded generation repeatability, trees and strata, continuous bedrock, equal physical terrain sampled at two resolutions, 10 cm / 5 cm continuous-height coherence, matched physical layer depths, and invalid generator spacing.
- Bounded allocated-chunk rescans: exact/zero candidate budgets, resuming mid-fall material snapshots without losing mass, chunks removed during scanning, new chunks edited during scanning, and world-clear cancellation.
- Persistent color packing, explicit black, default sentinel behavior, per-chunk optional storage, same-material tint preservation, material-change reset, tint-only notifications and halo invalidation, reclamation, grain-color conservation, Fruit ID append, and generator-provenance preservation.
- Full recorded finite demo domain containing wood, leaves, sand, and wet strata. Every wood component connects to terrain through six-neighbor wood paths; elevated lateral branches are present.

## Default fixture

The final recorded fixture uses **Generation 5 / snapshot Format 2** and the integrated species/color system. Seed **714103**, dimensions **256 × 128 × 256**, **0.1 m** cell width: **25.6 × 12.8 × 25.6 m**.

| Metric | Observed |
| --- | ---: |
| Generation time (unoptimized checks, concurrent container work) | 7,152.1 ms |
| Allocated chunks | 1,339 |
| Allocated material cell bytes | 5,484,544 |
| Tinted chunks | 1,339 |
| Allocated optional tint bytes | 21,938,176 |
| Occupied cells | 2,942,437 |
| Air, including unallocated chunks | 5,446,171 |
| Stone | 2,292,211 |
| Mud | 100,328 |
| Dirt | 196,581 |
| Litter | 121,581 |
| Grass | 60,012 |
| Wood | 58,588 |
| Leaves | 112,528 |
| Sand | 608 |
| Ember / Fruit | 0 / 0 |

All **38 of 38 wood components** connect to terrain by six-neighbor paths; the elevated branch underside fixture counts35,634 wood cells. Individual species and optional acorn validation are reported separately in [VALIDATION_TREES.md](VALIDATION_TREES.md).

The test timing is a single unoptimized container observation and includes startup generation only. The [optimized benchmark](Benchmark/RESULTS.md) is a separate run/compiler configuration; neither is a Unity or target-machine timing. Material/tint payload bytes exclude dictionaries, managed overhead, active queues, meshes and GPU storage.

## Snapshot allocation, appearance and migration

The current writer emits **Format 2** with optional per-chunk appearance channels. Explicit packed colors are opaque authored sRGB (`0xFFRRGGBB`); raw0 selects material base color, while explicit black remains `0xFF000000`. Color-only edits preserve material identity and invalidate all relevant mesh/AO chunks without waking material physics. Falling grains preserve their stored tint. The optional color allocation is released after its last explicit tint is cleared.

The final colored fixture writer allocated **20,408 bytes** to `Stream.Null`, excluding destination storage. It uses sorted chunk keys, one reusable4096-byte buffer, and table-driven CRC32. Optional packed color channels reuse that buffer. The checks cover nonseekable color output, stable tinted round trips, bad appearance flags, nonopaque colors, colors attached to air, mixed colored/default chunks, and reclamation.

The original **Format 1 golden fixture is retained and loads successfully** with default material colors. Its pinned SHA256 still verifies the historical fixture bytes. Format 1 and Format 2 snapshots from generator versions1 through5 are accepted as self-contained worlds. Loading does not rerun the generator; re-saving emits Format 2 while preserving the historical generation provenance. This is a format migration, so new Format 2 files are intentionally not byte-identical to Format 1 files.

Both formats validate CRC before importing. Saving to `MemoryStream` still allocates destination storage; loading buffers the capped payload. Material storage is bounded by8192 chunks; Format 2's optional colors raise its worst-case capped payload to approximately160 MiB. Runtime disk duration remains a separate measurement.

## Simulation review

The active frontier uses a FIFO queue plus a membership set. A popped cell is removed from membership before processing, so edits can wake it again. Movement enqueues local neighbors through the authoritative world mutation event. The per-call destination set prevents a newly arrived grain from moving a second time when it lands on an already queued position. Work is bounded by the lesser of the starting queue count and the caller's candidate budget; newly woken cells remain queued for later calls as necessary. Gravity strictly decreases grain height, and no movement leaves the finite domain, providing a finite settling argument when no further external edits occur.

Results may differ when callers choose different budget/tick sequences: diagonal tie choices include the simulation tick. Replay requires the same edits, seed, budget policy and tick sequence. The checks demonstrate conservation and wakeups under small budgets; they do not prove cross-runtime bitwise replay or realistic soil mechanics. Rigid wood/stone islands, fluid pressure and chemical reactions remain outside this prototype simulation.
