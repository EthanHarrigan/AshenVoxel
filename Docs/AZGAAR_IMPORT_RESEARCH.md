# Azgaar world import research

Inspected26 October2026 against upstream commit
`67d555b83f0b2f40ae50b03c9bbfd018175acc6d` (source reports version1.156.0).
This is direct source inspection, plus an actual upstream1.154.0 `.map`
fixture. It does not establish compatibility with every historical map version.

## Recommended first interchange

Prefer Azgaar **Full JSON** or **Pack Cells JSON**, exported from the Data
export menu. They contain explicit packed-cell geometry, heights and biomes.
Accept the following two wrappers deliberately:

```text
Full:      {info, settings, mapCoordinates, pack:{cells, vertices, biomes, ...}, grid, ...}
PackCells: {info, cells:{cells, vertices, biomes, ...}}
```

`info` contains `version`, `width`, `height`, `seed`, `mapName`, `mapId` and
`exportedAt`. Width and height are Azgaar canvas coordinates, not meters.
Each packed cell has `i`, `p:[x,y]`, raw `h` in0–100, `biome`, neighboring
cell IDs `c`, and vertex IDs `v`. Each vertex has `i`, `p:[x,y]`, neighboring
vertex IDs and incident cell IDs. The current biome table is an array of
objects including `i`, `name`, `color`, habitability and icon properties.
Do not assume historical parallel-array biome tables have this shape.

Full JSON includes `settings.heightExponent` and `settings.heightUnit`.
Pack Cells JSON omits those settings, so a caller must choose the physical
height mapping explicitly rather than silently assuming the map author's units.
Minimal JSON omits cell geometry and is insufficient for terrain reconstruction.
Grid Cells JSON contains the original heightmap graph, but lacks the packed
biome assignment; it is useful for terrain control only, not a substitute for
the full biome-bearing packed graph.

## Height and horizontal scale

The inspected source defines land as raw `h >= 20`. Its exact height conversion
in meters is:

```text
h >= 20: (h - 18) ^ heightExponent
0 < h < 20: ((h - 20) / h) * 50
h == 0: -990
```

At default exponent2, h20 is4 m and h100 is6,724 m. Shoreline raw20 is
therefore not a mathematical zero of that conversion. A game can choose an
explicit shoreline-relative mapping, such as
`maxElevationMeters * ((h-20)/80)^exponent`, for land, with a separate capped
ocean-depth mapping. That is a deliberate game remap, not Azgaar's original
display-height equation. Preserve raw h and the selected conversion parameters
in imported metadata so the decision remains changeable.

Recommended controls have separate meanings:

- **World width in meters:** maps the imported canvas width to physical space;
  preserve canvas aspect ratio unless an explicit anisotropic scale is desired.
- **Landmass scale and center:** samples the source map through an additional
  centered coordinate transform. This can enlarge/shrink the continent within
  a fixed playable domain, rather than confusing physical meters with land area.
- **Sea level, maximum land elevation and elevation curve:** independent
  vertical controls. Thousands-of-meters peaks do not require generating a solid
  voxel column all the way to the world bottom.
- **Stone depth:** local editable/physical rock depth below the surface;
  deeper unmodified matter can remain implicit. This is a representation choice,
  separate from the height source.

A simple canvas-to-world convention is centered x and source-y mapped to world z:
`x=(sourceX-width/2)*metersPerCanvasUnit`,
`z=(sourceY-height/2)*metersPerCanvasUnit`. Keep this documented: source y grows
downward on the map. Use global double/integer coordinates before local Unity
float conversion. Changing horizontal scale must invalidate generation/cache
identity, not merely stretch an existing near-world mesh.

## Biomes and continuous terrain

Preserve `sourceBiomeId`, original biome name/table, raw height, source-cell ID,
and land/water classification. The provisional forest override should alter
the **resolved gameplay biome** only; it must not erase source IDs or turn
ocean into forest. A later biome resolver can map Azgaar's desert, taiga,
tundra, wetland and forest IDs to native profiles. The standard source names
include Marine, Hot desert, Cold desert, Savanna, Grassland, Tropical seasonal
forest, Temperate deciduous forest, Tropical rainforest, Temperate rainforest,
Taiga, Tundra, Glacier and Wetland; use exported names/IDs because users can edit
the table.

Azgaar's Voronoi cell heights are coarse macrostructure, not final10 cm terrain.
Rasterize a continuous control field and coast/land mask, retaining nearest-cell
or polygon categorical ownership for biome labels. Do not interpolate biome IDs
as numbers. Bilinear/triangulated height interpolation needs a single global
sampling convention, deterministic borders and a protected shoreline mask.
Packed polygons intentionally omit deep-ocean territory: the upstream pack
generator drops noncoastal ocean grid points. Full JSON retains the original
grid for water depth/coverage. A robust importer should restore unrepresented
water through that original grid, while retaining packed biome ownership on
represented terrain. Absence of a packed polygon must not extend the nearest
land biome over an ocean.

Macro elevation and coast controls can condition a later diffusion/erosion
detail stage; that stage must not invent conflicting borders or alter saved
edits on regeneration.

## Why `.map` is a different importer

The inspected native save is a positional record format joined with CRLF. The
header contains version, license text, date, seed, canvas width/height and map ID.
It is not JSON despite containing JSON records. The loader also supports
base64/URI-encoded and gzip-compressed envelopes. SVG content can contain
newlines; blindly calling `splitlines()` changes record indices.

Relevant current records use zero-based indices: settings1, biomes3,
grid-general6, grid heights7, packed biomes16. Critically, the file does not store
the same explicit packed polygon geometry as Full JSON. The official loader
restores grid data, then invokes `Pack.generate()` and `Features.markupPack()`
before restoring packed-cell attribute arrays.

The actual upstream1.154.0 fixture has10,005 grid points/heights but7,227 packed
biome entries. **Zipping grid heights with packed biomes by index is wrong.**
An implementation must either reproduce the correct versioned pack algorithm,
or have Azgaar load/update the file and export Full JSON. For a reliable first
release, detect `.map` and explain that conversion path rather than silently
pretending it is a supported JSON export.

Fixture inspected outside the project archive:

```text
tests/fixtures/1.154.0.map
4,868,085 bytes;53 CRLF records; canvas8430×4000; height exponent2
grid heights0–100; packed biome IDs0–12
SHA256 5ea91eda983bae356fc7ebf21609a9fa3617ac1664f52e7345553eabc40f84fc
```

## GeoJSON warning

Cells GeoJSON exports Polygon coordinates as geographic longitude/latitude,
rounded to four decimals; those are not original canvas meters. Its properties
include `id`, `height`, `biome`, feature `type` and neighboring cell IDs, but
`height` is `parseInt(getFriendlyHeight(...),10)`. This discards the unit suffix
and rounds the converted height in the user's selected meters/feet/fathoms.
It also does not retain raw h. Do not infer meters or0–100 from that number.
Support GeoJSON only with explicit projection/extent and elevation-unit metadata
or user configuration; Full JSON is substantially less ambiguous.

## Import validation and implementation gates

Before replacing a current world, validate bounded input size/cell count,
finite positive canvas dimensions, unique cell and vertex IDs, valid coordinate
ranges, raw-height bounds, complete biome references and polygon references.
Reject unsupported wrappers/version shapes with actionable messages. Treat JSON
as data; never execute SVG/script or embedded content from a map save.

Keep a normalized, versioned internal control-map schema with source hash,
source version, coordinate mapping, height mapping, biome table and override
policy. Tile caches need this fingerprint plus generator/model versions. Tests
should include Full/PackCells wrappers, units, map aspect/scale, coast behavior,
forest override retaining source identities, missing IDs, malformed inputs,
and neighboring-tile border equality. Include a generated synthetic schema
fixture separately from any claim to import a specific user map.

## Primary sources

- [Full/Pack/Grid JSON exporter](https://github.com/Azgaar/Fantasy-Map-Generator/blob/67d555b83f0b2f40ae50b03c9bbfd018175acc6d/src/services/io/export-json.ts)
- [Native `.map` writer](https://github.com/Azgaar/Fantasy-Map-Generator/blob/67d555b83f0b2f40ae50b03c9bbfd018175acc6d/src/services/io/save.ts)
- [Native loader and pack reconstruction](https://github.com/Azgaar/Fantasy-Map-Generator/blob/67d555b83f0b2f40ae50b03c9bbfd018175acc6d/src/services/io/load.ts)
- [GeoJSON export](https://github.com/Azgaar/Fantasy-Map-Generator/blob/67d555b83f0b2f40ae50b03c9bbfd018175acc6d/src/services/io/export.ts)
- [Height conversion and units](https://github.com/Azgaar/Fantasy-Map-Generator/blob/67d555b83f0b2f40ae50b03c9bbfd018175acc6d/src/model/units.ts)
- [Biome definitions](https://github.com/Azgaar/Fantasy-Map-Generator/blob/67d555b83f0b2f40ae50b03c9bbfd018175acc6d/src/model/physical/biomes-generator.ts)
- [Actual `.map` fixture](https://github.com/Azgaar/Fantasy-Map-Generator/blob/67d555b83f0b2f40ae50b03c9bbfd018175acc6d/tests/fixtures/1.154.0.map)
- [Packed graph deliberately omitting deep ocean](https://github.com/Azgaar/Fantasy-Map-Generator/blob/67d555b83f0b2f40ae50b03c9bbfd018175acc6d/src/model/graph/pack-generator.ts)

## Supplied Ceteviles source: actual import follow-up

The later user handoff supplies a1.152.2 Full JSON with6,971 packed cells and
10,008 original grid cells. At513×265 atlas resolution,29,990 samples are absent
from packed polygons. All are original-grid water.29,908 are inside explicit
grid water polygons;82 boundary/near-boundary samples use the source's own
nearest-original-grid-center lookup, still restricted to water. These gaps are
an intentional packed-graph omission, not evidence of missing continent land.

The implemented converter preserves source biomes0–13 and the custom Sand Sea
ID. It also preserves the source `terrainDiffusion` format2 annotation block.
Optional versioned Unity summit-envelope application translates the two authored
anchor radii to physical meters and applies their documented boost envelopes.
It does not claim the handed-off mod's full V13 noise, valley, resnapping or
build-height-ceiling behavior. See [converter and executed real-map results](../Tools/WorldScaleImport/README.md).
