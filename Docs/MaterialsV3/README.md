# Surface appearance v3 (2026-10-28): Gaia-style terrain rules

Surface profile version 3. All changes are in `SurfaceMaterialSampler` (CPU, per terrain vertex / voxel).

## Landform masks (per column)
| Mask | How it is measured | Used for |
|---|---|---|
| Ridge / Crease | Convexity of the 2 m ring (or of the vertex footprint for distant terrain) | Pale crests and edges; dark, damp creases (cavity occlusion); bare rock on crests |
| Sun facing | Normal against a 40° sun on the snow model's azimuth | Sunlit faces bleach, warm and dry out; shaded faces cool, darken and grow moss/lichen; soil holds longer in shade |
| Talus | Ground 7–16 m uphill much steeper than here | Scree aprons at cliff feet, freshly broken (lighter) debris |
| Flow | Concave across the contour (gully), on slopes | Wetter soil, dark runoff streaks on rock, gravel erosion chutes on steep/cold/arid slopes, lush green gullies |

## Rules
- Rock: strata/joint pigment, then the formation tone on weathered faces, crest highlights, cavity darkening, sun bleaching versus shade cooling, runoff streaks, lichen (more in shade), moss on damp shaded rock in mild wet climates, orange crustose lichen on cold sunny crests, desert varnish.
- Vegetation: dries to straw on sunny crests and steep sun faces, deep green in hollows and gullies, olive/straw alpine turf with cold, patchy meadows. Alpine bare ground is patchy (noise, crests, sun) instead of one altitude line.
- Snow: cool blue in shade, warm in sun.
- Geology: most 60 m formations share their ~500 m group's lithology mix (fewer garish alternating bands); basalt/granite contacts and ~3 km crystalline subtypes blend smoothly (no hard region edges).

## Distance (footprint-aware sampling)
Terrain vertices pass their spacing (`EvaluateColumn(x, z, footprint)`):
- Detail finer than the footprint (grain, joints, mottling, lichen, streak noise, bed-scale lithology) fades to its average.
- Slope, aspect and convexity are measured at the footprint's scale, and threshold noise is low-passed, so a far vertex takes its patch's majority class.
- Snow becomes a coverage fraction.
- Vertices 8 m and wider supersample the patch (5 taps; 3 beyond 32 m).

Measured along a real boreal slope, vertex-to-vertex colour jumps (speckle) fell from 32 to 9 at 16 m spacing, and from 18 to 7 at 60 m. Far vertices cost about 7× more CPU on terrain worker threads.

Previews come from `Tools/MountainPreview/Smooth.cs`: an engine-free perspective render with sun, sky and fog, not Unity.
