# Actual composite lower-LOD CPU candidate

**Both frozen root fixtures pass all five LODs** at cuts [30,32,36,60,124].
The candidate's lower meshes agree with the original baker/reducer, and its
unchanged upper oriented face coverage, normals, material, RGB and corner AO
agree exactly. This is a CPU equivalence result, not installed gameplay or native
LOD/wind/FPS acceptance. Every file written by this lane is in this directory.

Implementation: `CompositeLowerLod.cs`, SHA256
`6efb2d4e2c6fcf644b7f42af570656c93fc4ba5f7fe9093939b59c543496e319`.
It is deliberately compiled with the existing numerical Unity shim. Its mutable
`VoxelWorld` and shim `Mesh` results are review artifacts, not production worker
publication objects. A separate lane is preparing plain CPU mesh arrays from the
completed compact fixtures.

## What was implemented

`CanonicalOrder` retains the original fine-chunk visitation rank. Each shared
`Template` retains one winning fixed candidate per coarse cell, including its
material, effective RGB, original chunk rank and fine-cell index. Fixed candidates
exclude mutable Wood at local Y<=28; canonical non-Wood and higher cells remain.
The job combines these candidates with the changed root using the original
first-non-Air/first-Wood policy. It never averages colors or selects a majority.

Root input remains **10 cm cells, owned Y<=29**, with row29 complete and exactly
canonical. Rendering context above29 is a replaceable cache, not added authoritative
matter. Each derived LOD uses one canonical upper neighbor layer and emits owners
strictly below its cut. No per-instance fine tree through12.4m is generated.

Original anchor (235,4,235), source dimensions470x978x470, reduction factors
1/2/4/16/64 and original chunk/cell phase are preserved. Fixture10 reaches local
Y=-5, below the original generator domain. The oracle and candidate use a sparse
Y translation of1024 fine cells, a multiple of every LOD's16-cell chunk extent;
the mesh export subtracts anchor (235,1028,235). This admits the extended root
without clipping it or shifting the visual reduction lattice.

The full reference is constructed in original canonical chunk rank order,
including all original materials/tints except replaced lower Wood. Empty chunks
are skipped without reordering surviving canonical chunks. Newly occupied chunks
follow afterward in z/y/x order. Cells inside each chunk retain the original
index order. Reversed root input produces identical candidate mesh bytes.
This explicitly avoids relying on Core.Set removal/reinsertion dictionary slots
or the sorted chunk order of an ordinary persisted snapshot.

`Job.Step(recordBudget, faceBudget, meshChunkBudget, revision)` resumes fixed
candidates, root candidates, six-neighbor face admission and mesh chunk calls.
Remaining work and counters are exposed; zero budgets retain work. Existing
65,536-cell,128-chunk and65,536-raw-face limits are retained. Shared ingestion is
bounded while enumerating (65,536 fixed samples,8,192 original chunk ranks).
An additional128-chunk root check uses the canonical phase; it can conservatively
reject a root admitted on the original builder's differently translated phase.
It does not enlarge the original builder's limits or classifier context.

One mesh budget unit is **one atomic16-cubed chunk build**:26,112 mask candidates
plus halo reads, AO, greedy scanning, allocations and output. It is not a voxel or
a millisecond budget. Cancelled/rejected jobs publish nothing and retain bounded,
job-owned managed state until the owner releases the job. Native Unity Mesh
destruction and immutable worker output require the separate array/integration
work; this prototype must not simply be compiled into a background Unity worker.

## Executed actual-data proof

Original main AVPM SHA256:
`41556b474ce7a26342e4dd7bcff2e4fe658ab0333e8360058262ee21c0f011aa`.
All included Core, original exporter, original mesher and numerical shim source
hashes match the recorded historical bake. One full original tree at a time was
generated transiently, using the unchanged generator and its existing budget.
No full pack/world/atlas export was run.

For every LOD, the check streamed original mesh chunks and compared **every packed
vertex attribute and triangle index** against the actual AVPM. The composite
reference then used the actual private `ExportPrototypes.Reduce` and actual
`GreedyChunkMesher`. Candidate cells including halo matched that reference's
coarse representatives exactly. Candidate lower meshes matched reference masked
topology, winding and vertex attributes. Expanded oriented face comparisons prove
the lower plus preserved upper covers the reference surface exactly, with no CPU
ownership gaps, duplicates or shading-attribute mismatches. This does not prove
native rasterization, temporal wind or transition appearance.

| Specimen | Assertions | Owned root cells | Process duration | Sampled private peak | Minimum sampled commit headroom |
|---|---:|---:|---:|---:|---:|
| 10 | 37,033,317 | 50,110 | 4.78s | 286.18MiB | 4.02GiB |
| 11 | 38,142,151 | 51,538 | 4.52s | 288.42MiB | 4.05GiB |

Assertions are mostly per-cell/per-vertex checks, not independent scenarios.
The first run used a1.5GiB stop floor; the revised2GiB instruction arrived after
completion, and run11 used2GiB. Both used launch>=3GiB,320MiB managed limit,
512MiB sampled private limit and120s timeout. Neither hit a guard. Samples at0.25s
can miss/overshoot peaks. These are Windows.NET CPU observations, not frame times
or native memory guarantees. Owned capture processes exited0 and none remained
at the recorded inventory check. The coordinator CPU window was released.

Exact evidence directories:

* `specimen-10-20261027T130937232091Z`: result SHA256
  `1025c0248fb03a6242e0ae0a990bc085547512b1bb1d96313fe78f769b1e5277`.
* `specimen-11-20261027T131159753179Z`: result SHA256
  `f1693a1766feb5e89c9c523f0acfcbbd335e90c98e0d4a9306c99dc215549fd6`.

Each contains readable source snapshots, pre/post input hashes, resource samples,
exact lower meshes, canonical-order/fixed-representative artifacts and a manifest.
The earlier compile-only directory is not labeled a capture or proof run.

## Artifact sizes and lower geometry

| ID | LOD/factor | Shared fixed artifact bytes | Coarse cache cells/chunks | Lower vertices/indices | Lower GPU byte arithmetic |
|---|---|---:|---|---|---:|
| 10 | 0/1 | 45,852 | 51,454/50 | 33,636/50,454 | 1,143,624 |
| 10 | 1/2 | 18,244 | 7,636/15 | 9,652/14,478 | 328,168 |
| 10 | 2/4 | 5,069 | 1,295/4 | 3,004/4,506 | 102,136 |
| 10 | 3/16 | 547 | 69/4 | 392/588 | 13,328 |
| 10 | 4/64 | 105 | 6/2 | 80/120 | 2,720 |
| 11 | 0/1 | 49,099 | 52,979/46 | 35,124/52,686 | 1,194,216 |
| 11 | 1/2 | 19,587 | 7,920/15 | 9,804/14,706 | 333,336 |
| 11 | 2/4 | 5,477 | 1,358/4 | 2,996/4,494 | 101,864 |
| 11 | 3/16 | 564 | 66/4 | 376/564 | 12,784 |
| 11 | 4/64 | 105 | 6/1 | 68/102 | 2,312 |

Shared original order has1,419/1,491 ranks, serialized14,194/14,914bytes. Including
all fixed artifacts gives84,011/89,746bytes per prototype. This excludes managed
dictionary/array overhead, existing pinned-row data and loader bookkeeping; it is
not a runtime RAM estimate. GPU arithmetic assumes28bytes/vertex+4bytes/index:
all five lower meshes total1,589,976/1,644,512bytes. These are derived meshes for
the two placements, not every possible root, and exclude shared original buffers,
native overhead and upload retention. Loading only the currently needed LOD is
a separate runtime residency decision.

Both actual captures reported **zero non-Wood fine candidates in these specific
lower/context bands**, including F1 through localY30. This does not justify
dropping non-Wood support for another prototype or future geometry revision.

`policy-20261027T131630278854Z` adds67 assertions, including eight synthetic actual
reducer cases for Leaves/Fruit order, Wood priority, removed-Wood fallback, fixed
upper Wood competing with changed lower Wood, same-chunk index order and new-chunk
order. It also verifies bounded shared ingestion, cell/chunk/raw-face admission,
zero/small budgets, pinned rows, invalid/duplicate root input, and cancellation or
stale identity after partial meshing. No tree generation occurred in this check.

## Compact replay handoff

`captured-replay-20261027T131848203063Z/specimen-{10,11}` contains `cells-F*.bin`,
`lower-F*.bin` and `replay.json`. Reloading only captured summaries/order and the
frozen root fixtures reproduced all10 original lower-mesh hashes exactly.
No whole-tree generation or pack decode occurred. The array-mesher lane received
these complete cell grids including the canonical neighbor layer.

All binary values are little endian; these are test fixtures, not a production
save format or committed migration:

* `canonical-order.bin`: int32 count, then (int16 chunkX,Y,Z; int32 rank).
* `fixed-F*.bin`: int32 magic0x314c4352,factor,cut,seed,count; each17-byte record
  has int16 coarseX,Y,Z, byte material, uint32 effective RGB, int32 original rank,
  uint16 fine-chunk index.
* `cells-F*.bin`: seven int32 values magic0x31434352,factor,cut,sizeX,sizeY,sizeZ,
  occupiedCount, then11-byte records (int16 stored-gridX,Y,Z, byte material,
  uint32 effective RGB). `replay.json` supplies voxel size and exclusive owner
  cutoff `1024/factor + (cut+4)/factor`.
* `lower-F*.bin`: int32 magic0x314d4352,chunkCount; per chunk int32 chunkX,Y,Z,
  vertexCount,indexCount, then11-byte vertices (int16 local-decimeterX,Y,Z,
  byte axial-normal code, uint32 packed render color), then int32 indices.
  Local decimeter vertex = coarse grid vertex*factor - (235,1028,235).

## Remaining integration limits

This is specialized to these mature Redwood source dimensions/phase and frozen
root fixtures. No production template loader, binary corruption/migration policy,
cache residency implementation, immutable publication or live edit bridge was added.
The job's test revision strings are not a production identity contract: integration
must use full template/pack/generator/terrain identities and recheck them atomically
before render/collision publication. Source identity changed mid-Step is not polled
by this CPU-only API; publication-time invalidation remains the caller's obligation.

The previous bounded root builder, interaction role classifier, cache caps and
frozen shared-upper splitter are unchanged. The accepted integration direction is
one shared canonical vertex buffer with upper/base index submeshes: unchanged
instances draw both; adapted instances draw shared upper plus derived lower.
This candidate does not install that runtime selection. Tree impostor routes,
all-pass wind/depth/shadows, collision/soft-tissue coherence, native LOD transitions,
travel, edits, generation provenance and FPS remain separate acceptance gates.
