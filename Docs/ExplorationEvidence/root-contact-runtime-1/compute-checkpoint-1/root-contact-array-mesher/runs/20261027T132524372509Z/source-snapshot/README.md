# Isolated managed-array root mesher

This candidate mechanically adapts the frozen `RootBaseChunkMesher` face, material, pigment, AO, greedy merge, and triangle-diagonal algorithm. It creates no Unity `Mesh` and calls no Unity/native API. Its only assembly reference is netstandard; PE inspection found no Unity namespace definitions or P/Invoke methods.

## Contract

`ArrayRootMesher.Build(ChunkInput, MeshCapacity, Func<bool> isCurrent, CancellationToken)` builds one complete chunk synchronously. It returns `MeshAttempt` with one of `Complete`, `Cancelled`, `Stale`, `CapacityRejected`, or `Failed`. Every non-success result has null output. A zero-capacity empty chunk can complete successfully.

`ChunkInput` takes an 18³ material/effective-pigment halo, integer chunk coordinate, positive finite voxel size, exclusive emission cutoff in grid-cell Y, and caller-supplied identity. Halo indexing is `(x+1)+18*((y+1)+18*(z+1))` for coordinates -1 through16. Constructor validation and copies make the input independent of subsequent caller array mutation. An air halo cell must have color0; an occupied cell must have an opaque effective `0xFFRRGGBB` color. The caller owns snapshot acquisition and must validate its source revision around acquisition. The supplied identity/freshness predicate must cover the correct source, LOD, and emission policy; this helper is not a cache-key implementation.

`MeshData` exposes immutable `ReadOnlySpan` views of populated positions, normals, colors, and indices, plus explicit min/max bounds and the input identity. Positions are in the same world-grid-relative meters as the frozen mesher. There is no transform, terrain sampling, generator, material mutation, or collider construction here. Alpha preserves packed material identity and two-bit AO; intrinsic RGB remains distinct.

Each call uses a private worker. At success, underfilled arrays are copied to exact populated lengths and exclusively transferred to the result. The result retains no oversized scratch buffers or worker/input references. Later builds cannot overwrite it. There is no pool or global retained buffer.

## Capacity and cost

Capacity is explicit quads, at most24,576 (six directed faces for each of4096 chunk cells). The default4096-quad scratch reservation contains557,056 array-element bytes. Successful output contains136 bytes per emitted quad: four positions/normals/colors plus six int indices.

Compaction may coexist with the full scratch arrays. `MeshCapacity.MaximumLiveElementBytes` conservatively allows twice the scratch element payload (1,114,112 bytes at default capacity). Add the immutable halo's29,160 element bytes, worker mask/scratch arrays'2060 element bytes, all object/array/string overhead, any caller snapshot arrays, retained prior outputs, and GC backlog separately. This is declared element accounting, not a CLR heap sandbox or a measured native/GPU reservation. The existing root-builder24MiB reservation does not cover these additional meshes.

Work remains **chunk-atomic**. A full call evaluates102 mask planes with256 candidates each (26,112 mask candidates), plus AO probes, merging, output writes, final bounds, and possible compaction. It is not a continuation or a frame-time budget. Cancellation/freshness checks occur before worker allocation, before each plane, and before/after compaction/bounds. A plane or compaction can finish before the next check; no partial mesh becomes visible. Downstream upload/publication still needs its own identity check and atomic render/collision transaction.

## Evidence

Final report: `runs/20261027T132524372509Z/report.json`; engine-dependency inspection: the adjacent `metadata.json`.

- C#9/netstandard2.1 compilation passes without Unity references.
- 254 chunk comparisons and2,619,718 assertions pass. The large count is predominantly per-vertex/index checks, not independent scenarios.
- All ten actual composite lower meshes (specimens10/11, factors1/2/4/16/64) match frozen binary positions, normals, RGB/material/AO, and exact indices. Each also matches the existing frozen mesher through a test-only managed Mesh shim.
- Synthetic mixed-material/color/AO geometry crosses chunk and emission boundaries. Tests verify outward nondegenerate triangles, bounds, exact capacity success, insufficient/zero capacity rejection, empty output, stale publication, cancellation before/during/after work, predicate exceptions, source-copy ownership, output ownership, and invalid input rejection.
- Actual data came from `root-contact-composite-lods/captured-replay-20261027T131848203063Z`; its manifest is verified before tests. No full trees were generated or packs decoded.

| Specimen | F1 populated/retained bytes | All five LODs populated/retained bytes | Largest chunk quads |
| --- | ---: | ---: | ---: |
|10|1,143,624|1,589,976|559|
|11|1,194,216|1,644,512|591|

The original un-compacted draft report remains at `runs/20261027T132207078527Z`. Its source snapshots were reconstructed and verified byte-for-byte against that report's original SHA256 values. Its large retained capacities are historical evidence, superseded only by the compact candidate's new tests.

Run `python .terrain-cache/root-contact-array-mesher/run.py` from DEV to create a fresh report. `derive.py` regenerates only the isolated algorithm adaptation and verifies the frozen original's hash first. No original helper, composite module, main file, Assets file, or native session was changed. Production wiring, aggregate cache admission/reservations, scheduling, Unity upload, collision publication, and native performance remain unimplemented/unverified.
