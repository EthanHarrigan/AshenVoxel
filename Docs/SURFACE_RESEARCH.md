# Research for terrain surface materials

Research checked 26 October 2026. The requested world remains on a **10 cm
material grid**, with separate distant height proxies. This notebook supports a
deterministic static surface classifier. It does not claim to implement a
calibrated snow, soil, sediment-transport or geological-evolution simulator.
Executed behavior belongs in the implementation/validation report.

## Primary evidence and its practical use

| Source | Finding relevant to this project | Use and boundary |
| --- | --- | --- |
| [Quéno et al., Snow redistribution in an intermediate-complexity snow hydrology modelling framework, 2024](https://tc.copernicus.org/articles/18/3533/2024/) | Wind and gravity redistribution improve heterogeneous snow, including exposed ridges and accumulation below slopes. Its simplified SnowSlide uses slope-dependent holding capacity and transfers excess downslope. Rough steep rock can retain snow that simplified redistribution misses. | Use slope retention, exposure, shelter and varying depth together. Do not present a single slope cutoff as a universal physical law or claim an avalanche solver. |
| [Strasser et al., openAMUNDSEN v1.0, 2024](https://gmd.copernicus.org/articles/17/6775/2024/) | This distributed model includes snow/ice layers, radiation effects of slope/orientation/shadow and terrain-based snow redistribution. Its example uses openness to distinguish convex and concave terrain. | Separate coldness/supply, terrain retention and melt/exposure. The game approximation has no hourly energy balance or calibrated meteorological forcing. |
| [Umirbekov et al., GEMS v1.0, 2024](https://gmd.copernicus.org/articles/17/911/2024/) | An empirical snow accumulation/melt model uses climate and topographic drivers without requiring a full complex snowpack solver. | A cheaper research-informed model is a reasonable starting point when detailed forcing is absent. Its coefficients and validation do not automatically transfer to fantasy terrain. |
| [Shirley et al., Disentangling microtopography and shrub effects on snow depth, 2025](https://agupubs.onlinelibrary.wiley.com/doi/full/10.1029/2024JG008604) | In the examined Alaskan watershed, terrain strongly correlates with snow variations below approximately 60 m; larger-scale behavior is more complex. Depressions and slope breaks matter. | Sample terrain at multiple physical scales. This is evidence from one environment, not a rule that every mountain shares a 60 m characteristic scale. |
| [Jasiewicz and Stepinski, Geomorphons, 2013](https://doi.org/10.1016/j.geomorph.2012.11.005), and [current GRASS documentation](https://grass.osgeo.org/grass-stable/manuals/r.geomorphon.html) | Landform classification uses relative surrounding elevation and visibility patterns at selected search scales. | Distinguish ridges, shoulders, slopes, footslopes and hollows. A few finite differences/TPI samples are a simpler heuristic, not an implementation of the full geomorphon classifier. |
| [USDA NRCS, Soil Facts](https://www.nrcs.usda.gov/resources/education-and-teaching-materials/soil-facts) | Soil develops through interacting parent material, climate, organisms, relief and time; sediments differ in grain size and depositional setting. | Parent geology and terrain/wetness should influence soil and sediment cover. A single biome ID cannot determine the whole underground column. |
| [NPS, Coastal sediments: parent material](https://www.nps.gov/articles/coastal-sediments-parent-material.htm), and [NOAA, How does sand form?](https://oceanservice.noaa.gov/facts/sand.html) | Beach composition depends on provenance. Dark volcanic material, including basalt, supplies many black sands; beaches also have other geological/biogenic sources. | Couple black sand to a volcanic source region and shoreline/depositional setting, rather than placing black patches randomly in every desert. |
| [PBRT, fourth edition: Transmittance, 2023](https://pbr-book.org/4ed/Volume_Scattering/Transmittance) | Extinction along a path gives transmittance; a homogeneous medium yields exponential Beer attenuation. | Depth-dependent water color is a useful visual approximation. A shader using an estimated path length is not a complete scattering or fluid model. |
| [Unity 6 URP: world-position reconstruction from depth](https://docs.unity3d.com/6000.0/Documentation/Manual/urp/writing-shaders-urp-reconstruct-world-position.html) | URP provides depth sampling/reconstruction, with the depth texture enabled. | A water shader may estimate submerged thickness from scene depth. Validate reversed-Z, sky pixels and platforms in the actual Unity renderer. |

Sources were accessed as primary paper pages or official documentation. No
paper text, reference photographs or third-party model implementation is copied
into project assets. The 2024–2025 papers guide the design; older terrain and
optics foundations remain useful rather than being relabelled as new research.

## Terrain context, not unrelated material noise

Evaluate a reusable column context in world meters: bare terrain height, local
slope/aspect, multi-scale relative elevation, upwind exposure, source biome,
climate availability, wetness proxy, water level and parent geology. Then choose
a surface cover and a physical depth stack. Sample rock beneath that stack from
the same spatial geology field.

`TPI = centerHeight - neighborhoodMean` is positive on a crest and negative in a
hollow. Use more than one neighborhood radius: a small ledge and an entire
mountain shoulder are different features. A local slope/concavity measure is
only a **wetness proxy**. A true topographic wetness index also needs contributing
catchment area; do not label a local-noise expression as a solved TWI or river
network.

Terrain attributes must come from the authoritative height field at fixed
physical scales, not the current render triangle normal. Otherwise identical
world points change snow or geology as the LOD switches. A source-resolution
limit also matters: bilinearly interpolating a 30 m learned field and taking a
2 m derivative does not recover measured microtopography. The imported macro
atlas is coarser still. Mark extra small-scale structure as procedural detail,
and avoid node-aligned curvature artifacts from undersampled source data.

For unknown climate, use explicit preset assumptions and report their origin.
The original model has five climate planes, but the current generic heightfield
interface does not guarantee that those measurements are available everywhere.
**Source biome**, not the temporary vegetation Forest override, should determine
surface climate/material defaults. Unclassified land can have a declared forest
fallback. Ocean must not become forest when the vegetation override is enabled.

## Patchy snow with actual thickness

A useful static design is a bounded product of independent factors:

`verticalDepth = supply × coldness × slopeRetention × shelter × meltRetention × coherentDrift`

Use a smooth slope retention curve; broad gentle terrain can keep deeper snow,
while exposed steep faces tend to lose it. Candidate art defaults such as a
transition between roughly 20° and 55° are **tunable design choices**, not the
calibration of the cited models. Small rough ledges may keep pockets on a larger
steep face. Wind-scoured convex crests can expose rock even in very cold terrain;
lee hollows can have deeper drifts. Warm/aspect-exposed patches can lose cover.
Latitude/hemisphere must be a setting rather than assuming one compass direction
is always the sunny face.

The result should be **vertical meters** if it is added to a heightfield and
rasterized as vertical voxel columns. Some scientific holding-capacity formulas
refer to thickness normal to a slope; that quantity is not directly the same
height offset. Keep the definition explicit and avoid dividing by a near-zero
cliff normal.

Use coherent patch scales and depth thresholds instead of white confetti.
Quantize matter only when materializing the 10 cm grid: a centimeter-thin visual
veneer is not automatically a 10 cm solid block. For stored snow matter, the
surface must actually rise above the bare rock, with Snow cells occupying the
added thickness and exposed rock remaining where depth vanishes. Derive far
cover/color from the same classification. Add maximum cover depth to conservative
height bounds so culling cannot cut off drifts. Compute snow eligibility from
the bare field, avoiding a recursive feedback where snow changes its own slope
classification on each sample.

Ice needs an explicit retained/permanent-cold setting or condition. Do not paint
every high cliff blue and call it a glacier. The static cover has no snowfall
history, energy conservation, snow-density evolution, avalanching or glacier
flow unless those systems are separately implemented and verified.

## Coherent geology and sediment cover

Choose broad parent-rock provinces or bodies first. Granite/basalt bodies should
be coherent in three dimensions. Sedimentary sequences can use a gently warped,
inclined stratigraphic coordinate rather than uniform horizontal world-height
bands. Keep layer thickness in meters and distinguish compositional rock
changes from small pigment weathering. Randomly choosing a new rock type for
each voxel produces confetti, not geology.

Relief and cover depth govern exposure. Steep convex ground tends toward bare
bedrock; gentler weathered terrain can carry soil. Talus/gravel belongs near
steep source rock and footslopes, with colors related to that source. Gentle
wet hollows can favor mud, clay and silt; organic wet cover can use peat. A thin
wet margin can darken existing sand without introducing a new mineral identity.
Coastal sand/gravel depends on slope and source material. Volcanic shore deposits
can become black sand; volcanic uplands need not be blanketed by it.

Gameplay hardness/density values are comparative tuning data. They are not a
Mohs scale or a calibrated rock-mass strength model; jointing, weathering and
water can make nominally similar rocks behave very differently. Preserve old
material IDs and append new ones so existing material snapshots remain readable.

## Visual water and its future boundary

Keep water surfaces and water-level queries separate from solid terrain matter.
A simple sea/lake proxy can provide a surface shader, depth/tint variation,
Fresnel-style reflection and mild wave normals. A camera below a valid water
surface can receive a restrained color/fog change. Check terrain/water coverage
so caves and inland below-sea-level dry regions are not automatically declared
flooded merely from camera height.

This version has no liquid volume, conservation, flow, pressure, buoyancy or
erosion. The interface should make those absences explicit while leaving room
for a later water simulation. Wave/texture phase should use stable world-space
coordinates or rebased phase offsets, so moving the floating origin does not
make water patterns jump.

## Implementation and verification gates

Compute expensive terrain context once per column or cache region; do not issue
dozens of height/climate queries for every buried voxel. Use bounded caches with
source/profile identity. Intrinsic color variation belongs in world coordinates
and remains independent from lighting. Fine tint variation can reduce greedy
merging, so use restrained palettes and measure the fidelity cost.

Useful fixtures include:

- Cold gentle basin versus equally cold exposed cliff and ridge; multiple
  depths and bare-rock pockets at the same elevation.
- Same point across LODs, query orders, chunk borders and origin rebases.
- Source desert/volcanic/wetland biomes while the vegetation override is Forest.
- Soil/snow/sediment thickness followed downward into coherent parent rock.
- Volcanic coast versus ordinary coast versus dry volcanic upland.
- Independent temperature, precipitation, latitude/aspect and sea-level changes.
- Actual near voxel edits, tint retention and snapshot round trips for appended
  material IDs.
- Above/below-water transitions, valid shoreline boundaries, missing depth
  texture fallback and scene teardown restoring owned visual state.

Standalone rules and renderer substitutes can verify contracts. Actual URP
shader compilation, transparent ordering, depth handling, underwater appearance,
ground-level readability and GPU cost still require Unity.
