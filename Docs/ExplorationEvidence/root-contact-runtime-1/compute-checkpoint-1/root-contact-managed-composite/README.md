# Pure managed composite LOD compute adapter

**PASS: all ten recorded lower meshes are byte-identical**, for both actual root
fixtures and factors1/2/4/16/64. The adapter removes Unity/shim dependencies from
the earlier reduction candidate and publishes immutable `MeshData` arrays only.
It is a compute producer candidate, not an installed background worker, scheduler,
cache transaction, upload path, collider or native rendering result.

Final source: `ManagedComposite.cs`, SHA256
`32d7948cb2232263d809a5c3d266b6ae264255e525009bb72450c4d358c514b5`.
Final evidence: `runs/20261027T134408961318Z`, 164,394 assertions, about0.84seconds
for the test process with128MiB managed heap limit and45second timeout. Most
assertions are repeated fixture/candidate checks, not separate scenarios.
The earlier compile-only harness typo, source snapshots and diagnostics remain
under `runs/20261027T134352164276Z`; no compute run occurred in that attempt.

The exact unchanged array API sources are:

* `root-contact-array-mesher/MeshData.cs`:
  `3e415dc9047f7c24dd01c5988dd9c8a74d963861ac741eabc09cad18443876ca`.
* `root-contact-array-mesher/ArrayRootMesher.cs`:
  `6424b3bd44e09caf291ee434bbd122ebf90286e8c5b6abaa2f1382ce9895ba27`.

## Loader and ownership contract

All types are in `RootContactManagedComposite`:

* `RootCell(Int3 local10cmPosition, uint effectiveRGB)` and
  `FixedSample(Int3 coarseCell, MaterialId, uint effectiveRGB, int originalRank,
  int fineChunkIndex)` are readonly values.
* `GridTransform(anchor, fineYShift, sourceSizeX, sourceSizeZ, seed)` explicitly
  captures the original phase. Actual fixtures use anchor(235,4,235), Yshift1024,
  source X/Z size470. AnchorY must4 and shift must be a supported multiple1024.
* `CanonicalSource(orderedChunkArray, pinnedRowArray, transform, identity, limits,
  currentPredicate, token)` privately captures original rank order and canonical
  row29. Rank lookup uses sorted immutable records, preserving each original rank.
* `LodTemplate(source, factor, cut, fixedSampleArray, identity, limits,
  currentPredicate, token)` privately captures bounded fixed representatives.
* `CompositeJob(fiveTemplates, rootArray, identity, limits, currentPredicate, token)`
  snapshots the root and template references. The five immutable templates must
  share the same `CanonicalSource` and have factors1/2/4/16/64 with cuts30/32/36/60/124.
* `Step(recordBudget, faceBudget, chunkBudget)` continues fixed/root records and
  neighbor-face admission. One chunk unit covers a complete halo acquisition and
  one synchronous array mesher call. It is not a millisecond budget.

`CompositeJob.Published` is null until success. `CompositeOutput.Count/At(lod)`
returns immutable `LodOutput`; `LodOutput.Count/At(chunk)` returns readonly
`MeshPart(Coordinate, Data)`. `Data` is finalized `MeshData` with read-only spans.
Outputs do not expose root arrays, coarse worlds, winner arrays or mutable lists.
No output retains a template, job, halo or coarse-world reference. Earlier outputs
remain unchanged by subsequent builds. The job itself can retain its input copies
until its owner releases it; publication does not imply that job memory disappeared.

The only mutable `VoxelWorld` is private and replaceable. One LOD completes before
the next coarse world is allocated. Per-chunk integer winner tokens refer to the
bounded immutable inputs, replacing per-voxel winner dictionaries. Root duplicate
tracking uses bounded per-chunk bit arrays. Every cache mutation uses Core.Set.
Root data remains10cm, ownedY<=29, with canonical row29 complete and exact. Fixed
coarse samples preserve the canonical band, material priority, pigment, original
rank and fine-cell order; no per-instance upper fine tree is created.

Positions in `MeshData` are coarse world-grid-relative meters. Local decimeter
coordinates are `round(position/(0.1*factor))*factor - (235,1028,235)` for these
fixtures. `LodOutput.Transform.LocalMeterOffset` is(-23.5,-102.8,-23.5). Normals,
intrinsic RGB and packed alpha `(materialID<<2)|AO` are unchanged. The mesh owner
cutoff is exclusive at `Yshift/factor+(cut+4)/factor`.

## Freshness, cancellation and publication

Constructors validate the complete caller predicate/cancellation before and after
copying inputs. Step checks it around record work, before/after halo acquisition,
after the halo's private copy, before/through/after array meshing and its compaction,
around LOD-result compaction, and before/after final publication. Predicate errors
fail closed. Cancelled, stale, failed or rejected jobs clear publication, completed
LOD references and private world/chunk references. No partial artifact is returned.

This is a single-owner API. External code must not poll mutable job state from
another thread while Step is running. Final publication checks cannot prevent a
source changing afterward: the future owner must still atomically revalidate the
full identity before changing scene render/collision state. Identity strings and
the current predicate must cover all pack/template/context/terrain/pose/edit/policy
inputs. This adapter validates bounds/structure; the trusted companion reader must
verify hashes and fixed rank/index provenance. It does not invent a production
cache identity or claim arbitrary caller-supplied samples match the original bake.

## Byte admission and measurements

`Limits` independently caps shared template payload, cloned root payload, declared
scratch, retained output payload and their aggregate. Defaults are4MiB templates,
65,536*16+8,192bytes root,12MiB scratch,8MiB outputs and24MiB aggregate. These are
**additional adapter admission figures**; the existing root builder's24MiB
reservation does not absorb them. Count caps remain65,536cells,128chunks and
65,536raw faces. Canonical ranks cap8,192 and fixed samples cap65,536 per LOD.

Declared scratch includes occupied coordinate arrays, bounded chunk/reference
arrays, winner arrays, Core material/tint arrays, duplicate bit arrays, both halo
copies, array-mesher working plus compacted buffers and mask arrays. A chunk reserves
at most its admitted raw-face count times136bytes for mesh working arrays, with
another copy allowed during compaction. Retained prior LOD outputs remain charged
while later LODs run. Output accounting includes mesh arrays and part/reference
arrays; result scalar/object metadata is separate.

| Specimen | Retained mesh arrays | Declared output elements | Peak declared scratch | Peak declared aggregate |
|---|---:|---:|---:|---:|
|10|1,589,976B|1,591,816B|2,758,412B|4,760,572B|
|11|1,644,512B|1,646,232B|2,643,296B|4,661,832B|

Shared templates account176,000/188,096bytes and root input801,816/824,664bytes
in these harness identities. Scratch figures are **declared bounded array-element
accounting**, not measured live CLR/private memory. Core dictionaries/dirty sets,
object/array headers, allocator effects, constructor caller buffers, metadata and
unreachable GC backlog are separate. Structure counts are capped, but no hard CLR
heap reservation or frame-time guarantee is claimed. Constructor conversion/copy
cost must also be charged by the sidecar reader/owner while its input arrays coexist.

## Verification and remaining gates

The production candidate compiles standalone as C#9/netstandard2.1. The external
net8 harness confirms it references no Unity assembly and defines no Unity/shim
namespace. It uses compact fixed/rank/root fixtures only: no full generation, pack
decode, Unity/native call, source/Assets/main modification or old evidence edit.

All10 exact mesh-file hashes match the previous actual-baker proof. Reverse root
order and large budgets produce identical results while prior outputs stay intact.
Tests cover tiny/zero budgets, private input copies, changed/incomplete pins,
incorrect template sets, invalid/duplicate roots, ingestion/count/byte limits,
predicate exceptions, explicit cancellation and staged stale/cancel injection
through acquisition, meshing, compaction, LOD handoff and final publication.

Further integration must combine the real bounded root builder/role result with
these arrays under one complete identity, account for every overlapping stage,
and validate the future owner's lifetime/publication behavior. Native uploads,
collision/edit coherence, LOD/wind/depth/shadows, travel and FPS remain unverified.
