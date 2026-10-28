# Proposed per-LOD canonical buffer: conditional CPU result

The frozen `SharedUpperSplit` mechanically accepts cuts **[30,32,36,60,124]**
for both actual mature Redwood prototypes (IDs 10 and 11), across all five LODs.
The baker's local visibility/AO dependency supports these cuts if the immutable
canonical coarse representatives are preserved. This is **not** a completed
composite-root mesher, a runtime compatibility result, or native rendering evidence.

Only supplemental files in this directory were written. Original helper, tests,
reports, main AVPM, project sources and previous supplemental results remain frozen.
No root-builder cap, authoritative payload, classifier context, shader or scene changed.

## Identity and exact mechanical checks

The immutable main pack is
`C:/Users/ethan/Downloads/AshenVoxel/Assets/AshenVoxel/Exploration/Prototypes/ExplorationPrototypes.bytes`,
SHA256 `41556b474ce7a26342e4dd7bcff2e4fe658ab0333e8360058262ee21c0f011aa`.
`proposed-cut-provenance.json` records every source/input SHA and before/after checks.
The independent verifier executed **36,016,006 assertions**, mostly repeated
per-index/per-face checks, not independent scenarios. It verifies original attributes,
uncut index order, winding, clipped surface coverage, shared seam vertex ownership,
horizontal seam ownership, and rejection of unsupported nonuniform crossings.

| ID | LOD | Cut (10 cm units) | Crossing quads | Added vertex bytes | Added index bytes |
|---|---|---:|---:|---:|---:|
| 10 | 0 | 30 | 60 | 3,360 | 1,440 |
| 10 | 1 | 32 | 6 | 336 | 144 |
| 10 | 2 | 36 | 15 | 840 | 360 |
| 10 | 3 | 60 | 0 | 0 | 0 |
| 10 | 4 | 124 | 0 | 0 | 0 |
| 11 | 0 | 30 | 59 | 3,304 | 1,416 |
| 11 | 1 | 32 | 10 | 560 | 240 |
| 11 | 2 | 36 | 9 | 504 | 216 |
| 11 | 3 | 60 | 1 | 56 | 24 |
| 11 | 4 | 124 | 0 | 0 | 0 |

Total incremental arithmetic is **12,800 bytes**: 8,960 vertex bytes plus 3,840
index bytes, assuming one shared vertex buffer, 28-byte GPU vertices, and 32-bit
indices. This excludes native allocation overhead, complete replacement index
arrays, retained source buffers and per-instance derived-root geometry. It is not
measured VRAM. Full CPU split-array sizes are in `proposed-cuts.json`.

## What the actual baker does

The report for the actual pack matches the inspected `ExportPrototypes.cs`,
`GreedyChunkMesher.cs`, `UnityShim.cs`, `VoxelWorld.cs`, `VertexAppearance.cs`,
`VoxelColor.cs` and `Materials.cs` SHA256 values. The dependency runner calls the
actual private `ExportPrototypes.Reduce` directly; its `Main`/tree bake never runs.

* Tree reduction factors are 1, 2, 4, 16 and 64, applied directly to the original
  fine field, not successively to the preceding LOD.
* Reduction bins use original nonnegative source coordinates divided by the
  factor. Tree source anchor Y is 4 fine cells. Mature Redwood profile dimensions
  derive to 470 x 978 x 470 fine cells and anchor (235,4,235). Preserve the original
  coordinate phase on **all three axes**, not a new world centered on the root.
  Local coarse grid planes are `factor * integer - anchor`.
* There is **no averaging or majority vote**. Enumeration visits `source.Chunks`
  in its dictionary order, then each chunk's indices 0 through 4095. The first
  non-air cell wins; the first Wood overrides a non-Wood representative. Later
  Wood does not replace an existing Wood representative or its pigment.
* The reducer copies `GetColor`, meaning effective intrinsic RGB: an explicit
  tint if present, otherwise the material's base color. It does not retain a raw
  zero-tint sentinel. Material identity and effective RGB must be preserved separately.
* The mesher reads an 18-cubed halo for each 16-cubed coarse chunk. A face depends
  on its owner, its immediate material neighbor, and three side/diagonal occupancy
  samples per corner in the adjacent face plane. AO occupancy is any non-air
  material. Different non-air materials expose their interface faces too.
* Every dependency differs from the face owner's coordinate by at most one coarse
  cell per axis. RGB is the owner's effective color; normals are axial. Nonuniform
  AO faces stay one cell wide/high. Uniform AO faces merge only with equal packed
  face key and RGB within a chunk. AO also chooses each quad's triangle diagonal.

Observed Y grids agree with the source: LOD2 is 0 modulo 4; LOD3 is 12 modulo 16;
LOD4 is 60 modulo 64. The grids have local origin -4, not zero.

## Conditional dependency argument

Assume changed fine cells are restricted to local **Y <= 28**, canonical row 29 is
pinned, and every fine material/effective color above that remains unchanged.
In source coordinates the last changed fine Y is 32. For reduction factor `F`,
the highest potentially changed coarse cell is `K = floor(32/F)`. A full unchanged
coarse buffer is cell `K+1`; shared upper ownership starts at cell `K+2`.
The local cut plane is therefore `C = F*(K+2)-4`.

| LOD | F | Last affected coarse index K | Unchanged buffer local fine Y | Shared cut plane |
|---|---:|---:|---|---:|
| 0 | 1 | 32 | 29 | 30 (3.0 m) |
| 1 | 2 | 16 | 30..31 | 32 (3.2 m) |
| 2 | 4 | 8 | 32..35 | 36 (3.6 m) |
| 3 | 16 | 2 | 44..59 | 60 (6.0 m) |
| 4 | 64 | 0 | 60..123 | 124 (12.4 m) |

For a face owned by `Y >= K+2`, the lowest required sample is `K+1`. If all
coarse representatives there and above remain canonical, its visibility, normal,
RGB, material and corner AO remain equal. At the cut, an upward horizontal face
belongs to the lower owner; a downward face belongs to the upper owner. This is
the frozen splitter's ownership rule.

This permits an authoritative root payload still ending at row 29. The additional
visual band can be immutable shared canonical data or exact pre-reduced samples;
it does not require a per-instance fine world extending to 12.4 m. However, a
partially affected coarse cell can also contain unchanged fine cells above 29.
Its new representative must combine the edited cells with those canonical cells
under the original policy/order. Copying its old representative blindly is not
correct if a winning cell was removed. That lower-cell aggregation is unimplemented.

The argument is about the oriented unit-face surface and its attributes, **not
identical greedy quad/index partitioning**. Lower changes can alter how a uniform
face merges across the cut. Exact upper appearance still requires preserving the
original surface interpolation, coverage and ownership when joining render pieces.

`DependencyChecks.cs`/`dependency-results.json` contain **33,839 assertions**:
40 deterministic synthetic lower-change cases across five factors, using actual
baker/mesher code, plus seven counterexamples. Complete buffer and upper coarse
representatives, and all upper face atoms with their four packed attributes,
remain exactly equal in the 40 cases. These are sparse synthetic fields, not
proof that the new root implementation already provides the required canonical data.

## Counterexamples and proof gaps

1. **One coarse layer too low changes upper AO.** For every factor, a lower side
   occluder changes two upper face atoms when the buffer is removed. Unsafe cuts
   are 29,30,32,44,60; proposed cuts exclude that dependency. In particular,
   common seam 60 mechanically clips every original LOD, but it does **not**
   isolate LOD4 AO from lower changes. The old seam-60 result remains true only
   for partition mechanics.
2. **An identical reconstructed fine field can reduce differently.** Two cells
   at source (1,128,1) and (17,128,1) fall in the same LOD4 upper coarse cell but
   different fine chunks. Reversing chunk insertion order changes the actual
   reducer's winning Wood tint from red to blue. Replacing the materials with
   Leaves and Fruit changes the winning material too. Fine material/tint maps
   are identical. This is a reconstruction-order counterexample, not a claim
   that a lower-only edit necessarily reorders unchanged upper cells.

Therefore either retain frozen original coarse representatives for every unchanged
cell, or reproduce the original fine chunk/cell visitation and material-priority
semantics. Sorting reconstructed chunks is deterministic but need not reproduce
the historical bake. No cross-runtime dictionary-order guarantee is established.
Exact canonical data must include non-Wood occupants and the full diagonal halo;
root collision columns or an all-Wood silhouette alone cannot supply those facts.

Original coarse authored cross-sections also still differ from original LOD0:

| ID | LOD/cut | Upper unit contour segments missing/added versus LOD0 at same cut |
|---|---|---|
| 10 | 1/32, 2/36, 3/60, 4/124 | 119/119, 154/164, 166/192, 140/256 |
| 11 | 1/32, 2/36, 3/60, 4/124 | 126/126, 165/173, 166/188, 146/256 |

These measurements describe authored meshes, not new-root gaps. A proposed
composite lower LOD must match its **own** baker lattice and canonical upper
neighborhood. A fine LOD0 root contour cannot simply attach to a coarse upper.

## Triangle clipping versus the aligned canonical band

Clipping each original triangle preserves its interpolation only if new seam
vertices retain the interpolated values. Current Color32 alpha packs material
in the upper bits and integer AO 0..3 in the low two bits. The shader decodes
these **at the vertex**; material is then non-interpolated. Actual rejected
packed quads have uniform RGB/material/normals but varying AO. The material ID
does not change in the following counterexample.

For an AO 0-to-3 edge on the actual LOD4 Y interval -4..60, cutting at 30 gives
`t=17/32`, requiring raw AO `51/32 = 1.59375`. Packing the nearest integer gives
2. Current `MaterialOptics.hlsl` maps raw AO `q` to
`(64 + q*(191/3))/255`; the resulting shader AO error is
`2483/24480`, about **0.10143**. This is not an HLSL/native measurement.
`clipping_check.py` verifies the rational arithmetic, same material ID 6, triangle
winding, area conservation and original-triangle interpolation in 25 assertions.
Source hashes and exact fractions are in `clipping-counterexample.json`.

| Route | What it can preserve | Added cost and unresolved requirements |
|---|---|---|
| Clip each original triangle and retain separate float AO | Original piecewise interpolation, up to shader/GPU precision | New attribute/shader path. Example crossing quad needs 3 shared intersection vertices and 12 extra indices: 132 bytes at the existing stride before an AO stream. Tightly packed float AO adds 4 bytes per stored vertex, potentially mesh-wide; real allocation/alignment unmeasured. Preserve the original diagonal; retriangulating a whole quad can change AO. |
| Keep original triangles and fragment-clip by local height | Original vertices/interpolation | Consistent clipping in forward, shadow, depth, normals, motion and other relevant passes, with compatible rest/deformed coordinates. Extra fragment/discard work and conservative draw bounds remain. No measured GPU cost. |
| Per-LOD aligned canonical buffer | Existing packed vertex format and shading path | Requires exact canonical reduction representatives/order, correct complete halo and a matching derived lower coarse mesh. No extra fractional AO at accepted crossings. |

Neither triangle clipping nor fragment clipping fixes a mismatched new-root
coarse contour or an incorrect AO neighborhood. Those routes address partition
interpolation; the composite lower geometry still needs the baker-equivalence work.

## Remaining acceptance work

The next implementation gate is a bounded composite LOD derivation using original
lattice phase, canonical representative data and the edited lower payload, then
comparison to the original baker on actual root fixtures. Verify seam coverage,
all materials/RGB/AO, horizontal ownership, source invalidation and unchanged
upper face atoms on those exact fixtures. Runtime residency, queue/cost caps,
LOD transitions, wind, depth/shadows, native appearance and FPS remain unverified.
The frozen root builder's limits and role-classifier context are unchanged.

Commands already run, from DEV: `python -X utf8 .terrain-cache/root-contact-alllod-checks/proposed_cut_run.py`,
`python -X utf8 .terrain-cache/root-contact-alllod-checks/dependency_run.py`, and
`python -X utf8 .terrain-cache/root-contact-alllod-checks/clipping_check.py`.
These generate supplemental CPU outputs only; no live adoption is authorized by them.
