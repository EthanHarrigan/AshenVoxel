# Mature coast redwood reference

Research and visual inspection: 26 October 2026. This update treats the shared
redwood preset as **coast redwood (*Sequoia sempervirens*)**, rather than mixing
its proportions with the broader giant sequoia. The user requests mature height,
stylized beauty and the irregular ridged silhouette around the lower trunk, on
the existing **10 cm material grid**.

## Botanical evidence

| Evidence | Consequence for the generator |
| --- | --- |
| The [American Conifer Society species description](https://conifersociety.org/conifers/sequoia-sempervirens) gives mature height of roughly60–100 m and typical large-trunk breast-height diameters around3–4.6 m or more. | A75 m specimen is a plausible mature representative. It is not a height record, an average over every habitat, or a prescribed relationship between age and height. |
| The [National Park Service coast redwood article](https://www.nps.gov/articles/000/coast-redwood.htm) describes individuals exceeding350 ft in height and20 ft in ground-level diameter. | Trees over100 m are plausible optional specimens. Ground-level diameter must be distinguished from breast-height diameter and from the full buttress/root footprint. |
| [Flora of North America, Frank D. Watson](https://efloras.org/florataxon.aspx?flora_id=1&taxon_id=200005399), also exposed through the [Midwest Herbaria taxon page](https://www.midwestherbaria.org/portal/taxa/index.php?taxon=51568), describes thick, fibrous, ridged/furrowed bark; descending to slightly rising branches; a narrower crown with maturity; and millimeter-scale foliage. | Longitudinal geometry, a dominant leader, hanging/sweeping branch habit and aggregated leaf sprays matter.10 cm voxels cannot represent individual needles anatomically. |
| [NC State Extension](https://plants.ces.ncsu.edu/plants/sequoia-sempervirens/) explicitly describes a flared, buttressed base, bark with scaly ridges, and loss of lower branches with age. | The lower outline needs actual asymmetric wood geometry. Mature trees should expose a substantial lower bole rather than retain foliage to the ground. |
| The [NPS tree overview](https://www.nps.gov/redw/learn/nature/about-the-trees.htm) distinguishes basal burls and high foliage; [California State Parks](https://www.parks.ca.gov/pages/735/files/02secinathistory.pdf) notes that some trees have spiral bark. | Rounded local swellings and gentle wandering of vertical ridges are reasonable variation. A burl is not a regularly repeating spike and should not be applied to every tree. |

The [USDA Silvics chapter](https://www.srs.fs.usda.gov/pubs/misc/ag_654/volume_1/sequoia/sempervirens.htm)
is an additional forestry source; its complete page timed out during this pass.
No quantitative claim here depends on content that was unavailable to inspect.
The NPS FAQ contains inconsistent rounded feet/meters in one historical tree
height entry, so it is not used for a precise record-height claim.

## Visual inspection

Two accessible botanical references were downloaded and visually inspected:

- **Jim Robbins's bark photograph**, linked from NC State Extension, shows
  wandering vertical ribbons with ragged and split ends, uneven furrows, warm
  exposed edges and broad weathered surfaces. The lower bole widens into
  irregular root shoulders. [Original photograph](https://eit-planttoolbox-prod.s3.amazonaws.com/media/images/Sequoia_sempervirens_fe71mP6dwLKJ.jpeg).
  NC State attributes it under CC BY-NC-ND4.0. It remains outside the project
  archive as a viewing reference; it is not a texture or redistributed asset.
- **Robert Van Pelt's Arco Giant illustration**, linked from the American
  Conifer Society, shows the open, complicated high crown and reiterated woody
  structure of one exceptional old tree. [Original illustration](https://conifersociety.org/images/coniferimages/32095/05_190314_205243.jpg).
  This is a reference for morphology, not permission to copy artwork or a claim
  that a simple constructive specimen reconstructs this particular tree.

These references supplement the user's Avroid appearance direction. They do not
resolve the earlier access limitation for recent Reddit forest videos; no new
claim to have watched those videos is made.

## Stylization at10 cm

Use two separate shape scales: broad buttress/root shoulders that merge into the
bole, and shallower, much more numerous bark ridges running upward. The apparent
"spines" should read as fibrous vertical fluting, not thorns. Broad basal lobes
include wood; their size must not be described as ordinary bark thickness.

Choose unequal widths, strengths and angular spacing. Let adjacent ridges
wander slowly, split visually through smaller-scale relief and fade with height.
Keep them coherent across segment boundaries. Avoid a regular star-shaped gear,
horizontal bead rings or independent spherical bumps. At least one or two cells
of relief are needed for a silhouette difference at this grid scale. Subcell
fiber detail belongs in future shading rather than silently changing cell size.

Keep color variation restrained and spatially related to bark structure:
weathered russet/brown ribbons, darker natural pigment in fissures and occasional
warmer exposed edges. These are intrinsic colors; dynamic lighting must still
form the actual shadows. Strong random RGB speckle undermines the large forms.

A mature specimen should have a tall readable bole, irregular branching space
and a tapered high crown. The reusable generator can still provide young or
open-grown variants through its physical dimensions and branch controls.
Reiterated ancient crowns, fire hollows and biological growth history remain
separate extensions unless explicitly implemented and verified.

## Acceptance targets

- Report real generated height and bounds, rather than only the requested height.
- Inspect a full specimen and a close view of the lowest several meters.
- Confirm relief changes Wood occupancy and the silhouette, and that ridges
  remain attached to the trunk.
- Keep a continuous taper and supported roots, foliage and thin branches.
- Report actual candidate counts, truncation status and memory; mature scale
  must not be achieved by clipping the tree into a small demo world.
- Label offline renders accurately. Botanical scale and an attractive still do
  not establish Unity frame rate, shader correctness or a match to Avroid video.
