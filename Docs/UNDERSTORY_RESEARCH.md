# Procedural ground plants and weathered voxel rocks

Research checked 26 October 2026. The user requests small procedural plants,
ferns, bushes and mushrooms across the supported biome floors, plus attractive
weathered rocks. Matter remains on the required **10 cm grid**. A fern leaf and
a tree leaf should share a material; different appearance alone does not need
a new behavior ID. This notebook records design evidence and proposed methods,
not an assertion that every referenced simulation is implemented.

## Primary sources and practical consequences

| Source inspected | Useful result | Application and boundary |
| --- | --- | --- |
| [Prusinkiewicz and Lindenmayer, The Algorithmic Beauty of Plants, 1990](https://algorithmicbotany.org/papers/abop/abop.pdf), especially chapters 3–5 | Plant architecture separates a branching/attachment description from leaf and flower organ shapes. Chapter 5, printed pp.120–121, gives a fern example using L-system-generated polygonal leaf contours. Chapter 4 describes planar and cylindrical phyllotaxis. | Use a reusable curved axis with attached tapering leaflets, radial fronds and species parameters. A bounded constructive fern is an appropriate initial implementation; it need not execute a string-rewriting grammar to use the architectural idea. |
| [Fowler, Hanan and Prusinkiewicz, Modelling Spiral Phyllotaxis, 1989](https://algorithmicbotany.org/papers/modelling-spiral-phyllotaxis.html) | Geometric planar and cylindrical organ arrangement generates readable flower, cone and fruit patterns. | A seeded angular sequence gives rosettes and tufts organized negative space. Do not give every species the same rotational symmetry. |
| [Strauss et al., Phyllotaxis: is the golden angle optimal for light capture?, 2020](https://algorithmicbotany.org/papers/Strauss2019pit.html) | Their modeling finds other Fibonacci-like angles equally optimal under the examined light-capture conditions; the golden angle is not a uniquely optimal universal explanation. | Treat a golden-angle rosette as a useful shape parameter, not proof of ecological optimality. Paired fern pinnae and opposing herb leaves require different attachment rules. |
| [Bailey, A generalized framework for procedural generation of three-dimensional static and dynamic plant model geometries, December 2025 technical report](https://arxiv.org/html/2512.17966v1) | A measurable, shared parameter vocabulary supports organ prototypes, phytomers, compound leaves, shoot structure and species variation. A phytomer includes an internode, petioles/leaves and possible buds. The full text also describes growth, collision avoidance and attraction points. | Share axis/attachment/organ concepts between herb, fern and shrub presets. Keep static seed generation separate from dynamic growth, phenology, collision physics and attraction-point solving. The report is a preprint/technical report, not a claim of a peer-reviewed engine benchmark. |
| [Deussen et al., Realistic modeling and rendering of plant ecosystems, 1998](https://algorithmicbotany.org/papers/ecosys.sig98.html) | Distribution depends on plant/environment interactions; individual procedural shapes and their spatial arrangement are separate levels. Approximate instancing reduces geometric complexity by representing similar plants or groups with common objects. | Separate habitat selection and density patches from the shape generator. Reusing plant variants and distant groups is a useful future representation; authoritatively voxelizing every blade throughout a huge world is not required. |
| [Lane, Models of Plant Communities for Image Synthesis, 2002 thesis](https://algorithmicbotany.org/papers/laneb.th2002.html) | A two-level framework connects individual plant shape with community distribution, including clustering, succession and self-thinning in the demonstrated models. | Coherent density fields and local exclusion can produce patches and open ground. Static suitability rules do not implement succession, resource competition or population dynamics. |
| [Bridson, Fast Poisson Disk Sampling in Arbitrary Dimensions, 2007](https://www.cs.ubc.ca/~rbridson/docs/bridson-siggraph07-poissondisk.pdf) | A background grid and active list generate finite-domain minimum-distance samples in linear expected work for fixed candidate count. | Minimum spacing and bounded neighborhood lookup are useful. A globally keyed jittered-candidate method is a different algorithm: call it deterministic hard-core placement, not exact Bridson sampling or a proven blue-noise spectrum. |
| [Dorsey et al., Modeling and Rendering of Weathered Stone, 1999](https://graphics.cs.yale.edu/sites/default/files/p225-dorsey.pdf), [DOI](https://doi.org/10.1145/311535.311560) | Weathering changes shape and appearance through moisture and heterogeneous minerals. Their method uses a thin surface-aligned volume, transport/dissolution/recrystallization simulation and subsurface rendering. | Model chips, rounded exposed edges and coherent altered surface color as separate controls. Rounded/noisy shapes are a static appearance approximation, not the paper's weathering or optical solver. |
| [Quilez, 3D distance functions](https://iquilezles.org/articles/distfunctions/) | Analytic distances and constructive combinations provide controllable primitive silhouettes; the article distinguishes exact distances from approximations. | Compose anisotropic rounded forms, joint cuts and bounded displacement before voxel occupancy sampling. A noise-displaced or approximate ellipsoid field is not necessarily an exact signed distance function. We only need a stable inside/outside test for this use. |

The older constructive methods remain useful for deterministic editable matter.
The 2025 plant framework supplies a recent architectural comparison; it does not
make every recent reconstruction or learned image model appropriate for seeded
10 cm asset generation. No reference-paper code, photograph or model is copied
into project assets by this research.

## Avroid reference: this pass's access result

The user-selected reference remains [Avroid's newest posts](https://www.reddit.com/user/Avroid/submitted/?sort=new)
and his own comments. This pass requested that page, the previously retrieved
[v1.7 post](https://www.reddit.com/r/VOXEL/comments/1xddu2o/), and its
[author comment permalink](https://www.reddit.com/r/VOXEL/comments/1xddu2o/comment/19rjcztu71d37/?force-legacy-sct=1).
All three returned HTTP403. No login, alternate identity or access-control
bypass was attempted. The earlier retrieved text evidence remains in
[AVROID_REFERENCE.md](AVROID_REFERENCE.md); this pass cannot establish whether
additional newer posts now exist.

The author-linked [official site](https://voxelbit.net/) and its directly linked
`landing.mp4?v=1` returned HTTP200. The video is four seconds at2048×1152.
A frame at one second was extracted and visually inspected: it shows a small
red voxel apple with a stem and green leaf on gray. It does not show forest
plants, rocks or a biome environment. This is therefore **not a side-by-side
verification of Avroid's rock edges or environmental fidelity**.

The earlier author statements about polygon voxelization, greedy-meshed object
triangles, hardware ray tracing and distant grouping are reference evidence.
They do not establish how his plants or weathered rocks are generated. The
methods proposed below are our implementation choices, not attributed to him.
Downloaded research and retrieval logs are kept outside project assets under
`/workspace/research-understory`; paper/media files are not packaged as ours.

## A compact vocabulary with recognizable structure

| Family | Constructive signature at10 cm | Shared material plan |
| --- | --- | --- |
| Fern | A low crown of unequal curved fronds; paired tapered pinnae shorten toward each tip; gaps between fronds preserve the silhouette | Leaves; green stipes can use the same tissue, rather than falsely becoming thick timber |
| Grass / dry tuft | Several rooted, bent narrow axes with different lengths and lean; coherent green/straw palette | Leaves |
| Sedge / reed | Upright emergent stems and a few narrow lateral leaves; basal grouping, not a random green cloud | Leaves; only genuinely woody components use Wood |
| Broadleaf herb / flower | Short stem with opposite or alternating leaves; optional small terminal flower; leaf length and width are separate controls | Leaves and optional Petal if a separate flower behavior ID is introduced |
| Woody shrub / berry bush | A few connected tapering woody axes with separated leaf sprays, open interior and occasional supported berries | Wood, Leaves and existing Fruit |
| Mushroom cluster | Several unequal stems with cap profiles and cap-edge/underside variation; clusters share a local patch rather than perfect spacing | A distinct Fungus material; stem and cap colors remain pigment variants |
| Succulent / cactus | Rosette or branching fleshy columns, vertical color ribs and a dry habitat; variable arm count, not stretched shrubs | Reused plant tissue unless later cactus chemistry needs a distinct behavior |
| Alpine cushion / low scrub | Low compact mat, small lobes, sparse flower accents and much more exposed ground | Leaves, optional Petal; local Wood only in woody scrub |
| Ground pebble / boulder | A spectrum from a one-cell pebble to larger chipped, rounded or stratified forms | Existing basalt/granite/limestone/sandstone/shale; tint related to parent material |

Individual fern leaflets, grass blades, flower petals, mushroom gills and cactus
spines are often smaller than10 cm. These voxel forms are stylized aggregates,
not anatomically scaled reconstructions. One solid cell can be the smallest
ground accent; a bush can legitimately use hundreds or thousands. A16×16
cutout sprite is an optional future visual representation, not a reason to
quietly shrink authoritative voxels or add a new material for every pixel.
If cards are introduced, their visual bounds, picking/collision policy,
transparency sorting and distant replacement must be explicit.

Use a small quantized palette per organ and a few coherent cluster-level
variations. Keep base material and stored intrinsic pigment independent. A
dark fern should not become a new hard material, and mushroom caps should not
be Leaves merely because both are colored. Color-only edits must preserve the
existing simulation sleep and snapshot contracts.

## Biome floor proposals and overrides

These mappings use the current standard source IDs plus the transferred
custom Sand Sea13. Imported biome names can be edited by a map author, so
an eventual data-driven profile map should retain the exported semantics.
The temporary forest vegetation override must not replace source-ground
climate and habitat rules.

| Source biome | Initial floor vocabulary |
| --- | --- |
| 0 Marine | No terrestrial plants under fake sea water; underwater vegetation is a separate habitat |
| 1 Hot desert | Sparse succulent/cactus, dry tuft and weathered exposed rocks |
| 2 Cold desert | Low dry scrub/cushion and sparse tufts, avoiding implausible tropical cactus everywhere |
| 3 Savanna | Straw/green grass clumps, occasional shrub, exposed stony gaps |
| 4 Grassland | Grass, herbs and small flower patches |
| 5 Tropical seasonal forest | Broadleaf herbs, shrubs and seasonal dry grass |
| 6 Temperate deciduous forest | Ferns, herbs, shrubs/berries, mushrooms and litter gaps |
| 7 Tropical rainforest | Larger broadleaf/fern forms, wetter fungus patches and rocks |
| 8 Temperate rainforest | Ferns, fungus and damp rock accents |
| 9 Taiga | Low berry shrub, sparse grass/herbs and mushroom patches |
| 10 Tundra | Low cushions, short grass/scrub and exposed/lichen-colored rock |
| 11 Glacier | No terrestrial plants on snow or ice; rock exposure alone need not imply plant growth |
| 12 Wetland | Sedges and reeds on shallow emergent margins; wet-ground herbs/fungi as suitable |
| 13 Sand Sea | Very sparse xeric accents and rock; preserve long open areas |

Snow depth, sea depth, source land flag, slope, temperature, wetness and substrate
override the biome-wide selection. A hot desert rock face is not a sandy dune,
and a rainforest's shaded hollow is not its dry exposed crest. The current
surface wetness value is a local proxy, not a drainage or soil-water simulation.
No true canopy-light map exists yet; a noise value must not be labelled measured
shade. Keep an explicit future canopy/competition input rather than pretending
biome ID solves understory light availability.

## Stable placement and bounded work

Use fixed world-meter candidate cells with deterministic seed/key jitter and
a coherent patch-density field. A local acceptance rule should compare all
relevant neighboring candidate keys using a stable priority, with sufficient
halo. That makes results independent of which chunk was requested first.
For different plant footprints, spacing must account for the larger radius;
a tiny tuft and a large bush cannot share a single unjustified separation.
Ownership should be by global candidate key, with clipping/materialization
separate, so objects crossing a chunk edge are neither duplicated nor lost.

Do not use a shared sequential random stream whose state depends on iteration
order. Habitat rejection should not cause a different seed sequence for every
subsequent region. Surface context is evaluated once per candidate, reused for
its leaves/branches, and should not trigger HTTP or inference. Query cost,
candidate count, raster-cell count, object count, output bounds and clipping
are observable budgets, not promises of milliseconds.

The plant vocabulary does not imply a GameObject or Rigidbody per leaf or
plant cell. Nearby editable detail can be rasterized into the authoritative
local grid and rendered through chunk caches. Far representations can later
instance bounded seed variants, group clusters or use impostors. A declaration
of future instancing/LOD is not an implementation or performance result.

## Weathered rock shape rather than noisy cubes

Begin with an anisotropic mass whose dimensions, tilt and embed depth are
seeded. Combine a few rounded masses or planes to form an intentional silhouette.
Use one or two coherent deformation scales, not independent occupancy noise.
Cut a small number of joints/chips with consistent directions. For sedimentary
types, let cleavage and shallow ledges follow a shared tilted bed coordinate;
for massive granite, favor broader rounded lobes and occasional fractured
corners. Different presets should change shape as well as color.

Weathering needs a hierarchy: broad edge rounding, a few readable chips,
subdued surface pitting and optional altered/wet pigment near exposed surfaces.
At10 cm, millimeter mineral grains belong to restrained color, not isolated
black/white boulders. Couple pebble/boulder color to parent geology or an
explicit rock preset; neither random granite beside every sandstone cliff nor
a dense one-material sphere field conveys provenance.

Embed rocks sufficiently that their base meets actual terrain. Test connected
components after cuts/displacement: disconnected floating chips should be
removed or deliberately represented as separate grounded pebbles, rather than
accidentally appearing as unsupported matter. A static rock does not gain rigid
body motion or chemical decay because its initial shape looks weathered.
Moss/lichen-colored patches need not change rock hardness; if they become
physical separate matter later, use a declared surface layer/material rule.

## Verification and visual acceptance

Useful generator fixtures include fixed-seed repeatability and different seeds,
nonidentical silhouettes, exact output bounds and candidate-budget reporting,
six-connected support from plant crown into stems/leaflets, no cap floating
above its mushroom stem, finite limited palettes and correct material reuse.
Check every source biome including ocean, glacier and custom13; also override
source biome with incompatible snow, water, slope and substrate conditions.
Check negative coordinates, chunk boundaries, reversed request order and a
large floating-origin offset.

Inspect small isolated objects at the intended voxel scale, then a floor patch
with rocks, open ground and multiple plant sizes. Show seed variants rather
than one flattering specimen. A plain cube should make the10 cm physical scale
visible. Record actual material counts, dimensions and source/profile hashes.
An offline render must use the generated cells and their stored colors; label
its lighting and renderer, and do not present it as native Unity or verified
Avroid matching. Player collision, picking, shadows, movement shimmer, overlap
with trees, LOD transitions and target GPU cost still need Unity playtests.
