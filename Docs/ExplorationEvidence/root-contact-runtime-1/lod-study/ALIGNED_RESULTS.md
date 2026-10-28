# Supplemental aligned-seam analysis

The requested seams32,48 and64 do **not** provide a common mechanical split for
these packed trees. Each fixes LOD2, but LOD3 and LOD4 still reject varying-AO
crossings. The cause is the baked coordinate origin, not just the cell size.

Observed packed Y coordinates for both mature redwoods:

| Packed LOD | Coordinate step GCD, cells | Residue | Observed lattice |
|---|---:|---:|---|
|0|1|0|integer cells|
|1|2|0|0 + 2k|
|2|4|0|0 + 4k|
|3|16|12|-4 + 16k|
|4|64|60|-4 + 64k|

LOD2–4 all begin at Y=-4 cells. The full actual residue arrays modulo32,48,64 and
their Y extents are recorded per case in `aligned-seams.json`. Zero-based multiples
of16/64 miss the coarser baked origin. At seams32/48/64, the nonuniform attribute
is again AO only; RGB, material ID and normals do not vary across those quads.

| Tree | Seam, cells | LOD2 result / added bytes | LOD3 | LOD4 |
|---|---:|---:|---|---|
|10|32|Accepted /400|Rejected|Rejected|
|10|48|Accepted /240|Rejected|Rejected|
|10|64|Accepted /240|Rejected|Rejected|
|11|32|Accepted /320|Rejected|Rejected|
|11|48|Accepted /560|Rejected|Rejected|
|11|64|Accepted /80|Rejected|Rejected|

These 18 cases passed5,326,777 independent assertions, including expected
rejections. `aligned-provenance.json` binds their exact inputs. No frozen helper,
seam30 test source, or earlier report was modified.

## Confirmed common lattice seam60

The observed lattices suggested60 cells, and a separate test verified this rather
than treating alignment arithmetic as proof. At seam60 /6m, the **unchanged helper
mechanically accepts all ten packed LODs** for the two trees. Nine cases require
no appended geometry. ID11 LOD3 has one uniform crossing, adding two vertices and
six indices:56 vertex bytes +24 index bytes =80 GPU arithmetic bytes. Replacement
index-array costs are separately recorded in `common-seam60.json`.

This follow-on passed36,008,848 assertions and retained the original seam30 and
32/48/64 outputs byte-for-byte. It confirms a possible authored partition height,
not the correctness of a new root-contact mesh. A6m base is twice the intended3m
cut extent and needs its own geometry/performance acceptance.

## Contour mismatch remains

At seam60, source upper contours still differ from the original LOD0 upper
contour. Signed unit-edge segments missing / added relative to LOD0 are:

| Tree | LOD1 | LOD2 | LOD3 | LOD4 |
|---|---|---|---|---|
|10|96 /102|146 /148|166 /192|166 /256|
|11|92 /94|138 /152|166 /188|166 /252|

Those are geometric contour differences on the original packed meshes, not
measured holes or pixel differences for a proposed new base. LOD4 also has a
large original step at this seam: its below-seam contour has768 unit segments,
while the above-seam contour has256. The positive/negative horizontal seam-face
ownership tests preserve that original authored surface; matching only vertical
edge positions would miss the horizontal cap requirement.

Choosing seam60 therefore solves the helper's clipping/AO precondition for these
specific models. It does not weld different LODs together. A coarse root base
derived from each original baker lattice, including its phase and horizontal cap
ownership, or another separately verified transition is still needed. Keep the
nonuniform-crossing rejection; no attribute flattening was justified or applied.

There is no claim about runtime impostors, Unity submesh upload, native depth/
shadow/wind behavior, collision, frame times, or compatibility with the proposed
terrain-contact base. All work remains CPU-only and outside Assets/main.

Reproduce with `aligned_run.py` and `common_run.py` in this supplemental folder.
