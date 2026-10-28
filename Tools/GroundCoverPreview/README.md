# Actual-data ground-cover previews

This tool exports the actual engine-independent plant and rock generators on
the required 0.1 m voxel grid. It does not run Unity. Geometry comes only from
occupied cells written through `VoxelWorld.Set`, including presentation pads.
The renderer emits exact exposed cell faces without replacing their silhouette.

From the project root with Python 3 and .NET 8 SDK installed:

```sh
python3 Tools/GroundCoverPreview/export_ground_cover.py
python3 -m pip install -r Tools/GroundCoverPreview/requirements.txt
python3 Tools/GroundCoverPreview/render_ground_cover.py --spp 64 --threads 4
python3 Tools/GroundCoverPreview/package_gallery.py
```

The exporter compiles an immutable source snapshot using the installed SDK,
without NuGet restore. Source and binary hashes remain in the gallery
provenance. Temporary snapshots, datasets and assemblies live under `bin` and
are excluded from project archives. A full reproduction does not depend on
these temporary files being delivered.

Each of the ten plant kinds and five rock materials is shown with seeds 714103,
271828 and 161803. The optional berry-shrub view uses the same shrub generator
with `fruitProbability: 1`; the baseline shrub view retains its default. The
micro view requests height 0.1 m and radius 0.05 m, producing exactly one cell
each of Fungus, Leaves and Petal. This is a minimum-scale reference, not resolved
botanical anatomy or an implemented textured-sprite system.

The two floor views arrange actual generated specimens at authored coordinates
on a small controlled layered voxel floor. They are composition mockups, not
the world biome-placement implementation or neural terrain. Buried rock roots
remain below the presentation-pad surface. No weathering/erosion simulation is
claimed: rock version2 constructs irregular polygon footprints, plane-capped
pieces, bevels and joints with buried connecting roots. It is not a full3D
erosion or fracture simulation. The separate six-shape study is reproduced by
Tools/RockShapePreview/README.md.

Mitsuba supplies offline CPU lighting with an explicit photographic display
transform. Its appearance and timings do not validate Unity/URP, real-time
performance, shadow/depth behavior or hardware ray tracing. Recorded image
hashes describe this execution; portable pixel identity across CPU/compiler
versions is not promised. The gallery verification checks current sources,
datasets, render tools and image hashes before composing its contact sheets.

For a quick review before a source freeze:

```sh
python3 Tools/GroundCoverPreview/render_ground_cover.py plant-fern rock-granite --draft --spp 16
```

Drafts stay under `bin/draft` and are not accepted by the final gallery proof.
