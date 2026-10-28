# Actual-data CPU path-tracing study

These images trace the generated 10 cm voxel cells and stored sRGB colors. They
are **not Unity screenshots**, do not run the URP shader, and do not establish
Unity performance. No concept-image generator, replacement tree mesh, synthetic
foliage, photographic background, or hand-painted tree is involved.

The four final samples are `Docs/TreeGallery/pathtraced-{birch,oak,redwood,pine}.png`;
`pathtraced-comparison.png` places them side by side. Each image explicitly says
CPU PATH TRACE / NOT UNITY. The existing direct-light CPU inspection gallery is
kept separately. These are two lighting models applied to the same final dataset.

The mature coast-redwood study additionally includes
`pathtraced-redwood-base.png` (1000×1000) and `redwood-study.png` (a composed
full-height/base comparison). `--view base` frames the actual basal material
geometry using trunk radius, root flare and bark relief from the exported genome.
It does not add sculpted render-only bark. All these outputs retain explicit
offline CPU / not Unity labels.

## Reproduce the frozen images

Requires Python 3.10+, Mitsuba 3.9.1, NumPy, Pillow, and a usable LLVM library for
Dr.Jit's CPU backend. The captured environment used LLVM 19 and `llvm_ad_rgb`.
The optional tool is independent of opening, building, or playing the Unity project.

```bash
python3 -m pip install -r Tools/TreePathtrace/requirements.txt
python3 Tools/TreePathtrace/verify_inputs.py --require-current-source
python3 Tools/TreePathtrace/render_pathtrace.py --input-dir Tools/TreePathtrace/inputs --name birch-0 --width 800 --height 900 --spp 64 --threads 6 --output Docs/TreeGallery/pathtraced-birch.png
python3 Tools/TreePathtrace/render_pathtrace.py --input-dir Tools/TreePathtrace/inputs --name oak-fruit-0 --width 800 --height 900 --spp 64 --threads 8 --output Docs/TreeGallery/pathtraced-oak.png
python3 Tools/TreePathtrace/render_pathtrace.py --input-dir Tools/TreePathtrace/inputs --name redwood-0 --width 800 --height 900 --spp 64 --threads 6 --output Docs/TreeGallery/pathtraced-redwood.png
python3 Tools/TreePathtrace/render_pathtrace.py --input-dir Tools/TreePathtrace/inputs --name redwood-0 --view base --width 1000 --height 1000 --spp 64 --threads 6 --output Docs/TreeGallery/pathtraced-redwood-base.png
python3 Tools/TreePathtrace/render_pathtrace.py --input-dir Tools/TreePathtrace/inputs --name pine-0 --width 800 --height 900 --spp 64 --threads 6 --output Docs/TreeGallery/pathtraced-pine.png
python3 Tools/TreePathtrace/make_comparison.py
python3 Tools/TreePathtrace/make_redwood_study.py
```

A future core change can intentionally make `--require-current-source` fail while
the historical snapshots remain valid. Omit that flag to verify the frozen input
and final images independently of subsequent generator development.

## New generated input

Generate fresh sparse exports with the existing TreePreview tool first:

```bash
python3 Tools/TreePreview/render_trees.py
python3 Tools/TreePathtrace/render_pathtrace.py --name oak-fruit-0
```

The default input directory is `Tools/TreePreview/bin`; `--input-dir` explicitly
selects historical frozen data. `freeze_inputs.py` replaces this tool's four
snapshot files from a completed fresh export. Run it only after all exports have
finished. It stores the source manifest and file hashes, and it does not modify
the generator or the main preview outputs.

## What is rendered

The sparse input carries integer voxel coordinates, material IDs, effective
opaque packed `0xFFRRGGBB`, dimensions, and 0.1 m cell size. Each exposed voxel face
becomes an exact planar quad with two triangles. Internal opaque interfaces are
omitted. This offline exporter deliberately retains cell quads instead of calling
Unity's greedy mesher; both describe the same occupied-cell surface. Stored RGB
is converted to linear before Mitsuba's diffuse BSDF reads the mesh attribute.
Local shader AO and cosmetic grain are not applied a second time.

The camera, procedural blue-sky environment and warm rectangular area emitter are
presentation fixtures added by the tool. Tree and ground geometry come entirely
from the sparse dataset. The broad area emitter is a soft key, not a physically
sized solar disk. Source distance and dimensions scale together with specimen
height to retain comparable angular size and avoid excessive near-light falloff
across a mature redwood. The renderer uses a diffuse path integrator with maximum path
depth five, Russian roulette beginning at depth three, 64 samples per pixel, and
sampling seed 42. A recorded 1.3 exposure and exponential display transform are
followed by linear-to-sRGB encoding. This is not Unity's tone mapping or lighting.
Materials currently share a matte diffuse BSDF: leaf transmission, material
roughness variation, volumetric atmosphere, wind, animation and game simulation
are not demonstrated.

Each report under `reports` records the exact input data hash, full exported
metadata, source hashes, camera, light, sample count, renderer/dependency versions,
image hash, triangle counts, and elapsed scene/export/render/save time. These
elapsed times are offline task observations, not an FPS or gameplay benchmark.
The frozen inputs have a separate SHA-256 manifest so later source changes do not
silently change the evidence behind the images.
