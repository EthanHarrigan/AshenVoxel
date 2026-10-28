# Procedural voxel tree research and implementation direction

Research updated 26 October 2026. The required matter scale remains 10 cm. The aim is a shared botanical scaffold with species presets, persistent material colors and a separately replaceable renderer. A convincing tree needs a distinctive branching habit, readable empty space, taper, foliage organization and scale before lighting can make it look convincing.

## Reference evidence

The newest entries retrieved from [Avroid's submissions](https://www.reddit.com/user/Avroid/submitted/?sort=new) remain v1.7 survival, v1.6 tropics and v1.5 procedural generation. The [latest author discussion](https://www.reddit.com/r/VOXEL/comments/1xddu2o/comment/19rjcztu71d37/?force-legacy-sct=1) describes 10 cm voxels, polygon-model voxelization, ray-marched ground, greedy triangle objects in hardware ray tracing and coherent felled-tree bodies. This does not establish that his trees are produced with a procedural branching algorithm.

An older [oak/cherry author discussion](https://www.reddit.com/r/VOXEL/comments/1vuqkrr/v12_oakcherry_forest/) explicitly describes handmade or voxelized assets alongside procedural terrain. It also describes the older WebGPU implementation. Keep that history separate from the newer NVIDIA-based rewrite; neither the older engine nor its performance settings should be assumed current.

This pass retried the latest Reddit page, JSON, video and poster through ordinary public requests. Reddit's media endpoints returned 403 or a login redirect, while the web tool exposed post/comment text. The author's linked [official site](https://voxelbit.net/) was accessible. Its exact linked `home.png?v=1` and `landing.mp4?v=1` assets downloaded successfully: the image was visually inspected and shows a small voxel apple on a gray backdrop; the video is a 4-second 2048×1152 asset. Neither supplies a forest comparison. We therefore do not claim to have watched the recent forest videos or matched their frames. See [reference evidence](AVROID_REFERENCE.md).

## Useful primary methods

| Method/source | Useful idea | Decision for this project |
| --- | --- | --- |
| [Weber and Penn, Creation and Rendering of Realistic Trees,1995](https://doi.org/10.1145/218380.218427) | Parameterized structural variation and geometry detail management | Use an accessible parameter/preset workflow, without claiming an exact reimplementation |
| [Runions etal., Space Colonization,2007](https://algorithmicbotany.org/papers/colonization.egwnp2007.html) | Attraction points guide branching through a desired crown volume | Good future option for obstacle-aware crowns; keep the branch-skeleton output independent of the growth strategy |
| [Palubicki etal., Self-organizing Tree Models,2009](https://algorithmicbotany.org/papers/selforg.sig2009.html) | Bud/branch competition for light and space with interactive shaping | Rich ecological/growth direction; a substantially different computational commitment from generating static seed variants |
| [Hoetzlein, A Procedural Model for Diverse Tree Species,2022](https://pcgworkshop.com/archive/hoetzlein2022aprocedural.pdf) | Curved branch features and hierarchical parameters separate species habit from individual variation; plant parts have semantic attachment locations | Closest practical scaffold for the current pass: shared curved branches, taper, controlled directional tendencies and attached foliage/fruit |
| [Bailey, Generalized Static/Dynamic Plant Framework,2025 preprint](https://arxiv.org/abs/2512.17966) | Common measurable botanical parameters across species and explicit plant structure | Supports keeping parameters and branch records reusable; this project does not implement Helios growth/collision physics |

The age of a proven modeling method is not a defect. Recent reconstruction or learned-radiance methods often solve fitting images/scans, which is different from generating editable material matter from a seed. We need control over topology, branch support, color identity, resource budgets and save behavior.

## Shared generation stages

1. Validate a species preset plus individual controls and physical dimensions. Distinguish species-level means from seed variation and age.
2. Build a parent-indexed branch skeleton: trunk/leader, primary scaffolds, subordinate forks and tips. Branch direction combines species habit, upward growth tendency, curvature, droop and bounded irregularity. Keep deterministic branch/candidate budgets.
3. Rasterize tapered curved branches into Wood. Preserve six-connected digital spines when a radius becomes smaller than a 10 cm cell. Rounded branch joints, root flares and taper matter more than simply adding branches everywhere.
4. Attach broadleaf sprays or needle fans to growing branch regions. Use subclusters and gaps. Do not fill a single solid ellipsoid around the whole tree.
5. Attach optional fruit near supported foliage/branch locations. Oak acorns and custom orchard variants should be distinguished; apples do not become normal oak fruit merely because a generic fruit material exists.
6. Assign coherent intrinsic colors from species palettes, branch/cluster variation, bark patterning and restrained voxel detail. Preserve material IDs separately from color.
7. Extract current render geometry and inspect full silhouettes, close-ups, seed variants and camera motion. Do not judge the generator only from a distant forest image.

## Species signatures at 10 cm

- **Birch:** slim pale trunk, horizontal dark bark markings, finer branching, light irregular crown and some droop. White bark must be actual color rather than a directional lighting trick.
- **Oak:** spreading curved scaffolds, lower forks, thicker limb hierarchy, rounded but separated foliage sprays and a broad uneven crown. Optional acorns are stylized at this grid scale.
- **Redwood:** a mature coast-redwood preset with a nominal75 m dominant leader, substantial trunk, high layered crown, irregular basal flutes and longitudinal bark relief. Broad woody buttresses and narrower bark ribs operate at different scales. These are stylized botanical proportions rather than reconstruction of a particular living tree; see [redwood sources](REDWOOD_REFERENCE.md).
- **Pine:** recognizable whorls, conical envelope, exposed woody arms and needle fans with gaps. Avoid both a solid green cone and a stack of identical flat disks.

Needles and many individual leaves are smaller than 10 cm. Their voxel forms are occupancy aggregates rather than anatomically exact leaf meshes. Higher visual fidelity can later add a separate foliage detail representation while leaving gameplay matter at 10 cm; do not silently shrink the authoritative grid.

## What makes the system adaptive

The same scaffold/raster/material pipeline should accept a new preset without a new renderer or physics system. Geometry controls need meaningful ranges and explicit budgets. A branch hierarchy can later drive pruning, damage, local body extraction and wind; storing it does not by itself implement those systems. Static age/size controls are not a simulation of years of growth. Forest competition, self-shadowing growth, obstacles and seasonal phenology remain distinct extensions.

Persistent colors cost memory and can reduce greedy merging. Start with coherent quantized palettes and measure rebuild/upload costs. A future renderer may sample a color brick while merging material surfaces more aggressively; color storage should not be tied to triangle layout. Lighting remains dynamic and separate from saved pigment.
