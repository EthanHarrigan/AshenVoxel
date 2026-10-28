# Gallery validation

These checks concern the **offline inspection tool**, not Unity rendering or
player performance.

The optional parallel Numba DDA was compared with the NumPy fallback using
**10,000 seeded random rays through a noncubic volume**. All hit masks agreed;
the 605 rays that hit also agreed on cell, normal and metric traversal distance
(with a floating-point distance tolerance of `1e-4` cell units). Separate fixtures
confirmed maximum reach and exact diagonal corner handling: a cell touched only
at a point is not entered, and all tied axes advance together.

Executed successfully with Python, NumPy and Numba in the development container.
Reproduce:

```sh
python3 Tools/TreePreview/check_acceleration.py
```

Without Numba, the script verifies the NumPy fallback and its boundary fixtures;
it explicitly reports that an independent compiled-backend comparison was not
performed. Numba is an optional acceleration dependency, not a game dependency.

The exported primary and variation samples were checked for complete metadata:
**13 actual generated worlds**, all with 10 cm cells and **no tree-generation
truncation**. The gallery contains four species, three seeds for each, and one
fruit-bearing oak variant. The full parameter values, material counts, bounds,
and status of each sample are in `generated-samples.json`. Source provenance
captures the immutable C# compilation snapshot; separate final-render tool
hashes identify rerenders without exporting geometry again.

Visual inspection covered all four species, the species comparison sheet,
birch bark, redwood furrows and branch foliage, and the oak acorn detail. The
images remain constructed voxel interpretations with obvious tunable leaf
clusters. They are not evidence of botanical accuracy, final environmental art,
Avroid appearance matching, Unity shader import, or frame rate.
