# Azgaar and elevation control-atlas conversion

This offline tool converts Azgaar **Full JSON** or **Pack Cells JSON** into the
bounded AVWF0001 height/biome/land atlas consumed by `WorldScaleData.AtlasIO`.
It supplies macro terrain controls. It does not run a diffusion model, create a
voxel volume or establish Unity runtime performance.

From project root, with Python3 and NumPy:

```bash
python3 Tools/WorldScaleImport/convert_world.py your-full-export.json world.avwf --width-km 100 --cols 513
python3 Tools/WorldScaleImport/convert_world.py your-full-export.json world.avwf --width-km 80 --depth-km 50 --peak-meters 5000 --landmass-scale 0.8
python3 Tools/WorldScaleImport/check_import.py
```

Default conversion preserves Azgaar's exact native meter-height equation using
the exported height exponent. Pack Cells exports omit settings: exponent2 is
used and this assumption is recorded in source identity and the output report;
`--height-exponent` explicitly overrides it. Input `heightUnit` is not applied
to raw0–100 heights because native `heightToMeters` already produces meters.

`--width-km` determines physical width. Omitting `--depth-km` preserves the source
canvas aspect ratio. Explicit depth permits anisotropic scaling. Atlas origin
is centered on zero. Source canvas y grows toward positive world z; this is not
a spherical/geographic projection. Sample locations include both atlas edges.

`--peak-meters` selects a deliberate game mapping: land height is
`peak*((rawHeight-20)/80)^exponent`, plus `--sea-level-meters`. Native raw20
therefore changes from4 m at exponent2 to shoreline0 m in this mode. Water
retains Azgaar's signed depth curve. Default maximum raw100 native height at
exponent2 is6,724 m. Original file hash and mapping controls are fingerprinted.

`--landmass-scale` scales the source continent around its center within the same
physical atlas extent. Below1 introduces ocean outside the source map; above1
zooms/crops the map. Scale changes regenerate atlas values and identity.

Default polygon rasterization retains categorical cell ownership; shared edges
belong to the lowest source cell ID. Azgaar deliberately omits deep ocean from
its packed graph. Default `--uncovered grid-water` restores these absent samples
from original-grid water polygons; remaining boundary gaps may use Azgaar's
nearest-original-grid-center classification only when that cell is water.
Coverage reports separate these counts. Missing original-grid land still fails;
`--uncovered error` requests strict packed coverage. Pack Cells exports without
an original grid cannot restore missing deep water this way.
`--sampling nearest` explicitly chooses nearest packed cell centers; it can use
optional SciPy for large inputs and records that alternative in source identity.
No numeric interpolation of biome IDs occurs. Source biome IDs and land flags
are separate channels. Forest override remains a runtime policy and must not
erase imported biome identities or convert ocean into forest.

## Input limits and formats

JSON input is capped at64 MiB,200,000 cells and4,194,304 output samples.
Explicit vertices are capped at600,000, and polygon rasterization stops above
100 million bounding-box candidates rather than processing pathological overlap
without a limit.
Dimensions must be at least2×2. Coordinates/heights must be finite; raw heights
must be0–100 and biome IDs0–255. Duplicate/missing IDs, ambiguous polygon data,
nonfinite JSON constants, invalid source references and invalid extents are
rejected. Atlas heights are limited to±1,000,000 m to match the C# contract.

Native `.map` is intentionally rejected with conversion guidance: load it in
Azgaar and export Full JSON. Its grid heights and packed biome arrays have
different indexing and cannot safely be paired. Minimal JSON omits terrain
geometry; GeoJSON discards raw-height/unit information and requires a separate
explicit import workflow. See [research](../../Docs/AZGAAR_IMPORT_RESEARCH.md).

Optional single-band floating-point elevation TIFF import requires installed
`rasterio` or `tifffile`, explicit meter units, and explicit width/depth:

```bash
python3 Tools/WorldScaleImport/convert_world.py elevation.tif world.avwf --elevation-units meters --width-km 100 --depth-km 100
```

This path accepts floating-point meter elevations, rejects normalized integer
imagery and NoData, and does not infer GeoTIFF projection/extents or pixel scale.
The caller explicitly supplies its physical extent and may set origin with
`--origin-x-meters` and `--origin-z-meters`. Declaring meters does not convert a
normalized image into calibrated elevation. Optional TIFF dependencies are not
installed by this tool. The executed checks exercise its explicit-unit gate;
full TIFF decoding needs the corresponding optional dependency and fixture.

The current Unity control-field design centers imported maps at world zero;
it uses atlas extents by default and retains source origin as canonical metadata.
A nonzero TIFF source origin therefore is not a claim of GIS/world placement.
Geographic placement requires a separate explicit world-coordinate mapping.

## AVWF0001 interchange

Little-endian layout:

| Field | Representation |
| --- | --- |
| Magic/version |8 ASCII bytes `AVWF0001` |
| Columns, rows |2 signed32-bit integers |
| Origin x,z; extent x,z |4 float64 meter values |
| Source identity length |unsigned16-bit UTF-8 byte count,1–4,096 |
| Source identity |UTF-8 text, commonly compact canonical JSON |
| Samples |row-major z then x,6 bytes each: float32 height, uint8 source biome, uint8 land0/1 |
| Integrity |uint32 CRC32 of every preceding byte |

The writer validates all channels before replacing output atomically. A sidecar
`.avwf.json` records source biome definitions and conversion summary.

Public bridge API:

```python
write_avwf(path, heights, biomes, land,
           origin_x, origin_z, extent_x, extent_z, source_identity)
# Arrays: two-dimensional [rows, columns]. Returns output SHA256.
# source_identity accepts a nonempty string or JSON-serializable mapping.

read_avwf(path)
# Returns dict: columns, rows, origin_x/z, extent_x/z, source_identity,
# heights(float32), biomes(uint8), land(bool).
```

## Executed validation

`check_import.py` executes49 checks on the declared synthetic schema fixture:
Full/PackCells wrappers, exact native heights, independent physical scales,
landmass/ocean changes, categorical biome retention, shared-edge ownership,
byte determinism, source identity, original-grid water restoration, missing-land
rejection, authored summit preservation/application, malformed input, budget limits, AVWF channel
roundtrip, UTF-8 identity, CRC/truncation and valid-CRC invalid-land rejection.

It writes `fixtures/azgaar-synthetic.avwf`: a5×5 atlas,20×10 km extent,5,000 m
game peak, source biomes0/6/9/11. This is an interoperability test fixture,
not an actual user-generated Azgaar map or a diffusion output. Native C# reading
is a separate integration check. No Core tree or terrain generator files change.


## Authored summit annotations from the supplied mod

The `terrainDiffusion` extension is preserved in the sidecar. Default conversion
keeps its annotations without changing elevation and reports a warning when
summit anchors are present. Add `--apply-summits` to apply the versioned
`unity-summit-envelope-v1` adaptation: the handed-off V13 shoulder/core weights
and altitude gate, maximum rather than summed overlapping boosts, land only.
Anchor radii convert reference native pixels into meters using
`radiusNativeAt750 * (widthKm/750) * 30`, then the landmass scale. Thus1800 km
Ceteviles radii are129.6 km and111.6 km, with10,000 m and6,500 m boost parameters.
Physical map aspect and source u/v positions are retained.

This adaptation does not implement the mod's V13 noise, valley-detail sampling,
anchor resnapping or Minecraft build-height ceiling. It is explicitly not
bit-exact mod parity. `--peak-meters` controls base terrain; additional authored
uplift can exceed that base maximum. Biome profile annotations/source IDs are
preserved, but this converter does not execute Minecraft biome mappings or dune
and ecology systems. Runtime forest override remains separate.

## Real Ceteviles import executed

The user's1.152.2 Full JSON was imported at513×265 samples,1800×926.015625 km,
with8,000 m base peak. Source SHA256:
`103f9b9d364b884dd532cd4e41effa83a327fd5a60ce0ba69cd612cd9a470d61`.
Packed polygons cover105,955 samples. Original-grid water restores29,990 missing
samples:29,908 by polygon and82 by nearest original-grid water classification.
No missing original-grid land is accepted. The result retains source biome IDs
0–13, including698 Sand Sea samples,42,405 land and93,540 water samples.

Base imported height range is−990 to8,000 m. Explicit summit adaptation yields
−990 to17,905.6875 m; that upper result is not the Minecraft ceiling-limited
height. The source extension and profile difference are recorded with the atlas.
Artifacts are under `/workspace/world-generation-artifacts`, outside project
assets until the integration owner deliberately bundles a selected atlas.
