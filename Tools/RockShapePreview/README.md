# Rock shape comparison

This tool compares six explicit version2 morphology controls in the same
Granite material and with the same three seeds. A higher oblique camera makes
the actual angular and concave footprint visible. A separate diagram records
the exact union of above-ground occupied10 cm columns, excluding presentation
pads and buried connecting roots.

From project root:

```sh
python3 Tools/GroundCoverPreview/export_ground_cover.py --mode shapes
python3 Tools/GroundCoverPreview/render_ground_cover.py --input-dir Tools/GroundCoverPreview/bin/shapes --output-dir Docs/RockShapeGallery --spp 64 --threads 4
python3 Tools/RockShapePreview/package_shapes.py
```

For an early draft, add `--draft --spp 16` to the render command and use
`package_shapes.py --draft`. Drafts remain under the export's `bin/draft`.
The source and binary snapshot must be regenerated after generator changes.

The v1 comparison is historical data preserved before the geometry change;
it is not relabelled as current generation. Both versions use matched lighting
and camera rules in the before/after panel. The main gallery's five material
panels use `Auto`; the six morphology panels fix Granite to separate shape
changes from color. Native Unity and physical erosion/fracture are not tested
by these offline exact-data renders.

Historical source/data revalidation is optional: pass
`--historical-input-dir /path/to/frozen-v1-inputs` to package_shapes.py. The
authoring run used the verified old source snapshot and binary exports retained
outside active assets. Those temporary inputs are not included in the source
project archive; the historical images, hashes and validation are included.
Reproducing current version2 shapes does not require the old inputs.
