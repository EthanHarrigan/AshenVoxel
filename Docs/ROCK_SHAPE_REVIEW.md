# Independent review of rock shape version 2

The user's criticism of version 1 was correct: its primary footprint remained
nearly elliptical across seeds. Grooves, pigment and small shoulder changes
had not created enough variety. Version 2 replaces that radial cap with unions
of polygonal pieces, major planes and independently selectable shape families.
Weathered bevels and joints now modify those primary forms.

This review was executed on 26 October 2026 using isolated C# compilations and
actual occupied-cell data. It is not Unity Editor, HLSL, GPU or player testing.
The reviewer did not edit implementation or shared test sources.

## Primary form and appearance

`RockShape` offers Auto, AngularBoulder, Slab, Wedge, Spire, SplitOutcrop and
Cluster. An explicit shape works with any of the five rock materials. Auto
uses deterministic, material-biased family selection; changing dimensions does
not unexpectedly select a different family. Requested and resolved shapes are
included in profile identity, and `RockGenerator.Version` is now 2.

The same-granite, same-dimension draft comparison and exact above-ground
footprints were visually inspected. Angular broken blocks, low slabs, inclined
wedges, narrow pointed spires, split masses and concave clusters are visibly
different. These changes exist in material occupancy rather than camera angle,
lighting or color alone. The latest Spire narrows its intrinsic footprint while
retaining height, improving its distinction from a broad wedge.

Drafts were inspected during iteration; final image provenance is audited
separately after the gallery export. Offline previews remain actual generator
data with presentation lighting, not native Unity screenshots or a verified
match to inaccessible Reddit footage.

## Geometry comparison with the published version 1

The published `AshenVoxel-GroundCover.zip` was extracted as reference sources
outside the active project and compiled independently. Its archive SHA-256 is
`2a475222aa8adbbf4a5747f497ce8225bb3827fd717838f6cfe6dffa337e5b2d`.
No active Unity source was replaced by that baseline.

For granite seed 714103 and a requested 2.4 × 1.5 × 2.1 m envelope, the exact
above-ground cell footprint was measured independently of lighting. The ellipse
comparison uses occupied-cell moments/covariance, then searches nearby center
and uniform scale for overlap. It is a descriptive covariance-ellipse fit,
not a mathematical global optimum over every possible ellipse. Convex fill
uses the convex hull of occupied cell-square corners; lower values indicate
more missing area/reentrant space inside that hull.

| Requested form | Covariance ellipse overlap (IoU) | Convex-hull fill | Central-symmetry difference |
| --- | ---: | ---: | ---: |
| Published version 1 radial cap | 0.9604 | 0.9497 | 0.0836 |
| AngularBoulder | 0.8279 | 0.8344 | 0.2832 |
| Slab | 0.9095 | 0.9034 | 0.2212 |
| Wedge | 0.8326 | 0.8998 | 0.4639 |
| Spire | 0.8784 | 0.8743 | 0.2410 |
| SplitOutcrop | 0.6486 | 0.6975 | 0.2846 |
| Cluster | 0.7531 | 0.7826 | 0.4885 |

A flat convex slab need not have a strongly concave footprint, and a spire's
height is not captured by a plan view. The actual 3D pictures remain the main
appearance assessment; these measurements check that silhouette differences
are present in the data.

Across Auto granite seeds 0–29 at those same dimensions, version 1 averaged
0.9488 ellipse overlap; version 2 averaged 0.8340. Version 2 resolved to ten
AngularBoulder, nine Wedge, six Cluster, four Slab and one SplitOutcrop cases.
That material-biased Auto distribution intentionally does not exercise all six
families; explicit shape selection does. All 30 generated geometry hashes were
different in each version, demonstrating why seed-hash uniqueness alone would
not have been a sufficient appearance test.

## Independent safety and scale probes

The isolated version 2 probe executed **5,928 profile cases**, plus strict
candidate/write-cap and extreme signed-anchor trials:

- 1,780 standard/scattered/showcase cases: all seven requested modes, five
  materials, four normal scattering sizes from 0.42 to 1.97 m and twelve seeds,
  plus one-cell/showcase, translated repeatability and 30 Auto-family cases.
- 2,548 extended cases: minimum and wider dimensions, short/tall caps, varied
  aspect ratios and burial values 0, 0.04, 0.05 and 0.2 m; also 12 m × 0.1 m
  thin shapes with 10 m height and 2 m burial.
- 1,600 narrow-axis cases: SplitOutcrop and Cluster with a 0.1 m axis, the other
  axis ranging from 0.42 to 12 m, both orientations and 100 seeds.

After the fixes below, every complete case had nonempty exposed matter, one
six-connected mass, correct material identity, at most six opaque intrinsic
colors and cell centers inside the physical envelope. Counts agreed with actual
matter; profile candidate/write limits and bounded column evaluations held.
Candidate budget 17 and write budget 13 produced exact flagged prefixes.
Extreme signed origins clipped without integer overflow. Translation tests
preserved local geometry and pigment.

These are selected deterministic cases, not a proof for every real-valued
profile or runtime. Count limits do not establish a millisecond or frame-time
budget, and these checks do not measure rendering cost.

## Defects found during this correction

The first version 2 draft disconnected a tiny diagonal Spire tip: seed 9,
0.42 × 0.2352 × 0.3276 m, with 0.2 m burial. All five materials reproduced it.
The owner added a small basal dilation so cardinal neighbors connect below
ground. A later owner case exposed an acute miter-tip variant; root dilation
now uses actual Euclidean distance to polygon segments/corners rather than an
expanded intersection of half-planes. Normal exposed silhouettes remain
unchanged by those buried connections.

Narrow SplitOutcrop/Cluster profiles could also have only buried matter: their
single sampled narrow-axis column fell entirely inside the split gap. The
owner added a visible one-cell basal ridge when the narrow dimension is at
most 0.3 m. This is an explicit coarse-grid fallback, since a 10 cm-wide model
cannot preserve a subvoxel gap and multiple resolved pieces simultaneously.
Both fixes passed the independent cases above and received owner regressions.

## Limits and final source checkpoint

The generator still fills bounded columns beneath piecewise top surfaces.
It does not model arbitrary overhangs/caves, physical erosion, mineral transport,
fracture or loose-body dynamics. Existing terrain is preserved by default;
buried connectors do not imply a structural-collapse solver. Source-biome
placement and shape-aware dimensions remain separate from this shape generator.

The owner froze version 2 after 69 formal checks, additional morphology/aspect
cases and C# 9/.NET Standard 2.1 compilation. The independent probe uses actual
Core sources and no Unity substitutes, because this component has no Unity
dependency. Native import, mesh appearance, collision and target-hardware
performance remain unexecuted gates.

| Final implementation | SHA-256 |
| --- | --- |
| `Core/Rocks/RockGenerator.cs` | `02657f6243d7cd173cb88883edd771e020910c2afce447d9d8c70a3f0ee802cb` |
| `Core/Rocks/RockProfile.cs` | `f92cca9d29263eada1c0284994b270f1b9b1d41e30e4b6bef8957bdf3e2c67c0` |
| `Core/Rocks/RockShapeGeometry.cs` | `56740b1cb403ea2ee5c15b54051743119b8774d3f8ddd6a029cfeaa8479e0b69` |

Isolated review sources, measured footprint datasets and results are under
`/tmp/ashen-rock-v2-review` in the original authoring container. The earlier
[ground-cover review](GROUNDCOVER_REVIEW.md) preserves the historical version 1
checkpoint; its original rock-appearance approval is superseded by this review.


## Final gallery audit

After the gallery owner froze the exports, independently checked **279 recorded
source/export/render/image hashes**, including the current 19 main-gallery
renders, six explicit-style renders, exact footprint diagrams, contact sheets
and before/after render reports. Every check passed. The preserved version 1
source snapshot and input binaries were validated against their original hashes,
not compared with or relabelled as version 2. The full historical backup manifest
also passed: **165 additional file hashes**. The published baseline ZIP's
checksum matched its recorded archive hash.

The final [before/after picture](RockShapeGallery/before-after.png) and
[six-shape comparison](RockShapeGallery/shapes-comparison.png) were visually
inspected. The old elliptical domes are visibly replaced by broken blocks,
planes, split masses, narrow spires and concave groups. These are matched
material/seed comparisons with disclosed framing rules; native Unity appearance
remains unverified. [Footprint diagrams](RockShapeGallery/footprints-comparison.png)
provide the corresponding direct occupied-column evidence.

All three frozen rock implementation fingerprints above matched the files used
by the final gallery. No unresolved defect remains from this independent review.
