# Actual procedural tree gallery

The images in this folder are **CPU inspection renders of actual generated
10 cm voxel data**. Each tree comes from this project's species presets and
branch/foliage generator, and the renderer uses its stored voxel colors. These
are not concept images, external models, Unity screenshots, path-tracing
performance demonstrations, or a claim of visually matching Avroid.

The separately labelled `pathtraced-*.png` images, if present, come from the
separate Mitsuba CPU beauty-render experiment and have their own reproducible
tool and provenance. They are also not Unity captures.

The mature redwood update adds `redwood-study.png`, combining the full tree and basal bark, plus `pathtraced-redwood-base.png` for a close view of actual10 cm buttresses and ridges. The preset is nominal75 m; the seed714103 gallery top is73.5 m above its planting point.

The inspection gallery shows:

- `redwood-study.png`: full mature coast-redwood and basal-bark study, composed
  from the exact generated dataset's CPU path traces.
- `pathtraced-redwood-base.png`: 1000×1000 close view of actual sculpted roots
  and vertical ridges. The geometry is 10 cm matter, rather than a bark texture.

- `species-comparison.png`: individually framed birch, oak, redwood, pine, and an
  oak reproductive-detail variant. Heights are labelled; equal panel sizes do
  not imply equal real-world tree heights.
- `birch.png`, `oak.png`, `redwood.png`, `pine.png`, `oak-fruit.png`: full-tree
  samples of the actual generator.
- Corresponding `*-detail.png`: bark at left and foliage/branch detail at right.
- `seed-variations.png`: three deterministic seeds per species with unchanged
  species parameters.
- `generated-samples.json`: bounds, dimensions, seed, material counts, and
  truncation status captured from actual generated worlds.
- `provenance.json`: core source SHA-256 hashes and compiler version for the
  export used by these images.

The gallery worlds contain real matter for the ground plane and tree. Primary
and direct-sun visibility trace exact voxel boundaries; a local ambient-occlusion
approximation, sky ambient, restrained fog, and a filmic display curve make
species form and color readable. This illustrative lighting differs from the
Unity shader and does not prove renderer compatibility or frame rate. No cell
color is painted on afterward.

Reproduce from the project root, with Python `numpy`/`Pillow` and an installed
.NET 8 SDK:

```sh
python3 Tools/TreePreview/render_trees.py --width 900
# Optional explicit SDK selection:
python3 Tools/TreePreview/render_trees.py --dotnet /path/to/dotnet
# Or set DOTNET_ROOT to the SDK installation directory.
```

Optional Python `numba` accelerates the same cell traversal; NumPy remains the
fallback. See `VALIDATION.md` for the executed backend and boundary checks.

The runner exports via the actual C# core without NuGet restore. Temporary
assemblies and sparse voxel records are under `Tools/TreePreview/bin` and are not
project assets. To rerender unchanged exported data, pass `--skip-export`.
`--no-details` and `--no-variations` shorten an inspection run.
For a provisional single-tree export before committing a full gallery, use
`--export-only --export-species Redwood --export-variations 1`. A partial export
is explicitly identified in `provenance.json`; do not freeze mixed old/new files
as a complete gallery. Run the normal unfiltered export before final capture.

Trees are voxel interpretations with controllable species traits, not botanical
simulation. Fruit/reproductive features may be exaggerated to remain visible at
10 cm. Review real-world silhouette, branch taper, canopy gaps, bark color,
foliage clustering, and seed variation independently of this lighting before
accepting a preset. Unity visual playtests remain necessary.
