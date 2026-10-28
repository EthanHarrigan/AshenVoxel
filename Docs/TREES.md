# Tree Workshop and shared voxel generator

Material optics update: leaf transparency and transmitted light are now separate
controls in the shared MaterialOptics asset. The common genome/skeleton/raster
path and opaque intrinsic sRGB colors are unchanged. AVPM4 retains material IDs
through LODs; the new normal/material atlas shades distant leaves consistently.
See [controls and evidence](QualityPatches/patch-5/README.md).

All tree matter remains on the required **10 cm grid**. Birch, oak, redwood and pine use the same scaffold, rasterization, color and foliage pipeline, with different validated presets. The outputs are actual Wood, Leaves and optional Fruit cells, rather than separate Unity objects for each voxel.

## Inspect and tune in Unity

Open `Assets/Scenes/TreeWorkshop.unity` and press Play. Alternatively use **Ashen Voxel → Trees → Open tree workshop**. The normal forest arena remains a separate scene.

The workshop can display a single profile or compare four species. Drag the right mouse button to orbit, middle mouse to pan, scroll to zoom, press Home to restore framing and G to regrow. Pan down to the base when inspecting a full-height redwood. Select the Tree Workshop object to assign profiles or change layout. Select an asset in `Assets/AshenVoxel/Settings/Trees` to edit the generator.

Changing Species keeps your edits. **Apply species defaults** deliberately replaces the profile settings; **New seed** changes the individual. **Regenerate active workshop** rebuilds the play-mode preview. Preview objects are temporary, while profile edits are saved assets.

| Controls | What they change |
| --- | --- |
| Height, trunk radius, crown radius, age | Physical dimensions and specimen proportions |
| Branch count, levels, angle, fork angle | Scaffold complexity and branching habit |
| Leader strength, upward growth, droop, irregularity | Upright dominance, curvature and individual character |
| Whorl size, crown start, cone taper, leader height | Layered conifers, open lower trunks and crown envelopes |
| Root flare | Buttress/root silhouette |
| Bark relief, ridge count | Physical longitudinal trunk folds and the number of irregular bark ribs; relief is in meters |
| Leaf form, density, cluster radius | Broadleaf sprays, needle fans or scale sprays; canopy openness |
| Bark pattern and bark/leaf/fruit colors | Birch markings, fissures, plates, intrinsic palette and variations |
| Fruit probability | Optional supported fruit samples; zero disables fruit |
| Seed and variation | Repeatable individual variation within a species |

Artist height controls allow2–120 m, with age applied separately. The sparse workshop caps its domain and each tree's branch/candidate/foliage work. A limit reached is shown in the status/Console. Raising height is not a promise that the current mesh renderer can display arbitrarily large forests at target FPS.

The redwood preset depicts a mature **coast redwood**, with a nominal75 m leader rather than the previous26 m young specimen. Actual height varies with seed and age. Broad basal flutes and smaller irregular longitudinal bark ridges change the occupied-cell surface; persistent bark pigment follows those folds. See [redwood reference evidence](REDWOOD_REFERENCE.md) for botanical dimensions and the distinction between bark ridges and woody buttresses. The optional fruited oak places brown acorn-like samples. Acorns at10 cm are deliberately exaggerated to remain visible. A custom orchard/fantasy profile can change fruit settings and palette, but red apples are not presented as a normal oak characteristic.

## Use from code

```csharp
using AshenVoxel.Core;
using AshenVoxel.Core.Trees;

var world = new VoxelWorld(240, 320, 240, .1f, 714103);
var genome = TreePresets.Get(TreeSpecies.Birch)
    .With(height: 11, branchDroop: .65f, foliageDensity: .72f);
TreeResult tree = TreeGenerator.Generate(world, genome, 12, .5f, 12, 714103);
// tree.Branches, BoundsMin/Max, WoodCells/LeafCells/FruitCells,
// VoxelCandidates, Truncated and TruncationReason are inspectable.
```

The origin is in meters. Provide suitable terrain/support when using a tree in a world; the example focuses on the generation call. Branch records carry parent index, level, kind, endpoints, radii and path distance. Results report the generated bounds and counts, plus explicit truncation flags. These records are an extension point for pruning, wind and structural bodies; those runtime systems are not automatically implemented by having a skeleton.

## Geometry and appearance

The generator builds a deterministic hierarchy of leaders, curved scaffolds, subordinate shoots and foliage-bearing twigs. Species controls define their arrangement. Tapered segments become Wood with six-connected spines for thin branches. Root geometry is local and never scans the entire world. Foliage uses connected local candidates around bearing locations rather than one filled enclosing crown. Fruit attaches to supported foliage.

Mature redwood boles use closely sampled taper and localized root-collar swelling. Adjoining horizontal trunk sections avoid repeatedly voxelizing large spherical endcaps around each short segment. The irregular bark field uses physical relief, unequal ribbon widths/strengths and slow longitudinal wandering; it stays continuous across the leader's segments. Larger basal flutes and supported buttress roots supply the broader silhouette. Bark Relief changes actual occupied matter and scales with `Scaled`; Ridge Count changes ribbon organization. Zero relief remains useful for comparing the same underlying specimen. At10 cm these are stylized folds and needle aggregates, not subcell botanical fibers.

Branch and foliage work have explicit count limits, not hard millisecond guarantees. The current generator runs synchronously within each specimen. The workshop spreads different specimens and mesh rebuilding over frames; this does not make an individual generation call asynchronous.

Colors are intrinsic **opaque sRGB 0xFFRRGGBB** values stored separately from material IDs. `VoxelColor.Base(material)` is the shared fallback. `world.GetTint` returns a raw optional override; `GetColor` returns the effective base/override color. Explicit tint edits preserve the material's hardness, granular behavior and other properties. Tint-only edits invalidate halo-dependent meshes without waking gravity. Falling grains carry their tint.

Tree color varies by species, specimen, branch direction/pattern and foliage cluster, with restrained voxel detail. Birch trunk markings and brown young twigs share the Wood material. Leaf clumps vary coherently rather than receiving unrelated random RGB colors. Terrain rock, soil and grass also receive seeded coherent color variation during generation.

The shader converts authored sRGB to linear lighting in a Linear project. Its extra fine grain defaults off so generated pigment is not obscured by duplicate noise. Lighting, occlusion and shadows are not painted into the saved colors.

## Cost and persistence

Tint channels are optional per chunk. A material-only16³ chunk stores4 KiB of cell IDs; a tinted chunk adds16 KiB for4096 packed colors, before metadata and meshes. Full tinted raw storage is5 bytes/cell. More distinct adjacent colors reduce greedy merging because a quad must preserve material, color and corner occlusion. That fidelity cost is explicit; future color-brick sampling can decouple color detail from triangle splits without changing material truth.

Snapshot Format 2 streams optional appearance channels with CRC and validation. Format 1 material snapshots load with default colors and preserve their generator provenance. Resaving upgrades the container without pretending that old matter was regenerated with the new tree system. Saves still do not persist an exact active simulation continuation or the generated branch hierarchy.

## Samples and validation

[Tree gallery](TreeGallery/README.md) contains full specimens, material details and seed comparisons generated from the actual C# core. They are offline CPU inspection renders, not Unity screenshots, performance measurements or a demonstrated match to Avroid's video frames.

The [research notebook](TREES_RESEARCH.md) explains the modeling alternatives and reference limits. Run core, tree, mesh and authoring checks before committing changes, then use the real Unity playtest. Geometry and mapping tests cannot establish shader import, editor UI behavior or GPU appearance.
