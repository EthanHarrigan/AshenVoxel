# Small weathered rock matter

`RockGenerator` is a deterministic engine-independent static rock/outcrop
generator on the required **0.1 m** grid. It writes ordinary material and
intrinsic color through `VoxelWorld.Set`; there is no object per cell and no
extra rock physics implementation.

```csharp
using AshenVoxel.Core;
using AshenVoxel.Core.Rocks;

RockProfile profile = RockPresets.For(MaterialId.Granite, seed: 714103)
    .With(widthMeters: 2.2, heightMeters: 1.3, depthMeters: 1.8,
          buriedDepthMeters: .2, weathering: .8, jointStrength: .4,
          shape: RockShape.SplitOutcrop);
RockResult result = RockGenerator.Generate(world, firstAirCell, profile);
```

`firstAirCell` marks the horizontal center and the first exposed layer; its
lower face is the nominal ground plane. The rock extends below that plane by
`BuriedDepthMeters`. Existing nonair matter and its tint remain unchanged unless
`overwriteExisting: true` is supplied deliberately. Placement on nonplanar
terrain should select a suitable anchor and slope before calling this generator.

Version 2 replaces the original round cap with six primary silhouette families:
`AngularBoulder`, `Slab`, `Wedge`, `Spire`, `SplitOutcrop` and `Cluster`.
`Auto` chooses a stable material-biased family from the seed. Explicit shape
controls remain independent of parent material. `Shape` records the request;
`ResolvedShape` reports the actual family and stays unchanged when only
dimensions change. Dimensions are maximum bounds; a spire intentionally fills
less horizontal space than a boulder with identical bounds.

Seeded irregular convex polygon pieces supply straight broken outlines,
inclined planes and asymmetric shoulders. Several pieces produce concave
clusters and open split clefts. Rounded top/corner bevels, readable finite-width
grooves and sandstone/shale bedding weather those primary forms without
turning every silhouette into the same mound. Every exposed column extends
to its buried base; Euclidean-expanded basal footprints and physical-width
strips connect even diagonally sampled tips. Burial normally hides these
connections. If burial is less than half a cell, or the narrow footprint axis
is at most three cells, a one-cell surface ridge supplies a connected visible
fallback instead of an empty or disconnected mockup.

This restricted solid-column composition cannot make arbitrary undercuts,
overhanging arches or cavities. The five material presets bias morphology and
keep coherent rock pigment; they are art-directed habits, not geological
reconstructions or physical weathering simulations.

Rock identity includes generator version 2, requested/resolved shape, material
catalog version, seed, dimensions, weathering/joint controls, budgets and
overwrite policy. The five
supported IDs reuse existing basalt, granite, limestone, sandstone and shale.
Each specimen uses at most six restrained opaque material colors; coarse
mineral variation and sedimentary bands remain separate from dynamic lighting.

`CandidateCount` limits visited cell candidates; `WrittenCells` limits actual
mutations. `ColumnEvaluations` reports the additional bounded cap evaluations.
`Truncated` explicitly marks incomplete output when a budget is exhausted.
Truncated output is a deterministic prefix, not an asynchronous resumable job.
Clipped and blocked counts are separate. Actual written/unchanged own-matter
bounds are inclusive and valid only when `HasMatter` is true. Budgets count
operations rather than guarantee milliseconds. Generate before subscribing a
granular simulation when bulk creation should not wake an entire generated area.

Run `python3 Tools/run_rock_checks.py` from the project root for actual C#
invariant tests and C# 9/.NET Standard 2.1 compilation. Native Unity import,
shader rendering, streaming and rigid support/fracture remain separate gates.
