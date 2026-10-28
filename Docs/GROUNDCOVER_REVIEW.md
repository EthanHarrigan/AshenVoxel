# Independent ground-cover review

Reviewed 26 October 2026 in the authoring Linux container. This is an independent
source and headless C# review of the procedural plant, rock, habitat-placement
and workshop additions. No Unity Editor, HLSL, GPU or native controller run is
claimed. The reviewer owns this document only and did not edit implementation
or shared test sources.

The user subsequently rejected the version 1 rock silhouettes as too circular.
The mechanical checks below did not establish sufficient appearance variety.
The original rock fingerprints and pictures are therefore historical; the
version 2 shape correction and its independent review are recorded in the
follow-up section at the end of this document.

## Initial findings and disposition

1. The first rock generator allowed a valid 1 m × 0.1 m × 1 m unburied Shale
   profile to emit no cells: its bedding blend put every cap below the first
   cell center. An isolated compiled reproducer confirmed zero cells while the
   other four materials emitted 30–44. The rock owner subsequently clamped a
   positive accepted cap to at least the first 10 cm cell center and added
   one-cell/low-plate regressions. This does not create finer physical matter.
2. The first `GroundCoverScatter.Materialize` incremented incomplete-specimen
   counts without necessarily setting the aggregate truncation flag. It also
   dropped rock clipping counts when the anchor itself fit the finite world.
   Root fixed aggregate budget/boundary reporting and exposed clipped rock
   cells/placements. Intentional occupied-cell overlap remains separately
   distinguishable from exhausted work or world clipping.
3. Shared `Leaves` matter was solid for actor-body queries, which would make
   future ground ferns and tall grass obstacles. Root added an explicit actor
   collision policy: default body queries ignore Leaves, Fungus and Petal;
   optional strict foliage collision remains available. Wood, soil, grass
   ground and rock still block. Rendering, tools and authoritative material
   identity retain their existing solid-matter semantics.

No source was changed by this reviewer. Findings were sent to the relevant
owners, who made the fixes and their own regression checks.

## Independently executed checks

The reviewer compiled actual engine-independent sources with C# 9/.NET 8 using
isolated temporary executables; official owner runners and their shared output
files were not used.

- 380 plant cases: all ten morphology families over 20 default seeds, plus
  minimum/maximum height/radius combinations with 32 stems and maximum
  variation. The initial complete profiles had no unexpected truncation,
  above-ground detached component, or conservative-envelope violation.
- 150 default rock seeds: five rock materials across 30 seeds each remained
  one six-connected mass with complete output.
- Six planner seeds (`0`, `1`, `-1`, `int.MinValue`, `int.MaxValue`, `714103`)
  over the half-open region [-30,30)². Each complete plan exactly matched the
  union of four independently requested half-open quadrants. Every anchor
  remained inside its owner region, and all same-layer pairs met the declared
  1.9/1.45/0.64 m priority-exclusion distance. These runs produced 1932–2271
  placements without truncation. This verifies those cases, not a spectral
  blue-noise property or arbitrary world streaming.
- All 24 material identities matched the actor-body policy in both default and
  strict-foliage modes. Actual `VoxelRaycast` still hit Leaves, Fungus and
  Petal cells while default actor policy ignored them. Static inspection
  confirmed `GridWalker.Blocked` forwards its Inspector flag. This is not
  native movement or stair-stepping verification.

Temporary reproduction sources are under `/tmp/ashen-ground-review` in the
original authoring container. The source freeze, final rerun and provenance
checkpoint are recorded below rather than treating an earlier mutable source
as final evidence.

## Static boundaries and appearance review

The placement planner uses global integer-grid keys, quantized cell centers,
fixed-distance hash-priority exclusion and source-biome habitat thinning.
Neighbor acceptance uses only hashes, so it does not secretly request extra
source terrain beyond the existing 32 m attribute halo. Half-open ownership and
signed negative-coordinate behavior passed the selected partition tests.
Finite-region materialization remains canonically ordered and bounded; the
source explicitly defers a stable cross-region cell-composition contract for
independently streamed overlapping plant volumes.

All material writes use `VoxelWorld.Set`; no GameObject, Mesh, Material or
Rigidbody is added per cell. New fungal/petal IDs append existing IDs and keep
byte storage and snapshot Format 2. The initial arena/tree generator provenance
is not relabelled by these independent version 1 generators. Pigment is opaque
intrinsic sRGB, independent of dynamic lighting. Plants use small per-material
palettes; rocks use one geology material with at most six shades.

The workshop uses existing chunk meshes, 15 finite specimen pedestals and a
close orbit camera. The scene/menu/script references and new assembly use were
inspected. No clear native API blocker was found. Native import, serialization,
rendered labels, camera framing, shadow/appearance quality and runtime cost
still require Unity 6000.3.27f1.

Initial offline actual-data drafts were visually inspected: the shrub has
separated foliage clumps and exposed woody structure, and the rocks have real
stepped voxel silhouettes. The initial fern looked too dense from a low front
view; the owner/gallery iteration reduced frond count, increased physical
pinna gaps and raised the camera. Reeds were similarly separated and received
brown seed-head pigment using the same Leaves material. Final pictures remain
offline CPU renders of actual generator data, not Unity screenshots.

Weathered rocks are a bounded rounded/faceted height-cap volume with static
joints, connected columns and coherent pigment. They are not a physical erosion,
mineral-weathering, fracture or general 3D cave/overhang solver. The fresh Reddit
requests remained inaccessible, so an exact Avroid rock-method or visual-match
claim would not be supported.

## Version 1 delivery checkpoint — historical

Both generator owners froze their final sources after the fern/reed and rock
iterations. Recompiled the isolated review executables against those sources:
all 380 plant cases and 150 seeded default rocks again passed the stated
connectivity/envelope checks. The five thin unburied rock cases were now
nonempty; the prior failing Shale case emitted 64 cells. The six planner cases
and 24-ID body/DDA policy checks also passed again against final sources.

Independently audited 116 recorded gallery hashes: current C# sources, exact
exported material/tint datasets, rendering tools, all 19 render outputs and the
three comparison sheets. Every entry matched. Final fern/reed/rock drafts and
the selected comparison sheet were visually inspected. These are controlled
actual-data fixtures, not a visual test of habitat scatter, neural terrain or
Unity rendering.

The WorldExplorer integration was inspected after root added it: terrain
materializes first, then a separate yielded phase plans the interior region
with a 1.7 m margin and builds under aggregate candidate/cell budgets. The same
immutable inspection sampler and 32 m source halo are retained. Closing the
inspection clears ground-cover statistics and discards its temporary world.
This remains a finite research mode; neither a per-frame millisecond guarantee
nor persistent distant vegetation streaming is claimed.

Root's current owner reports record 93 plant, 51 rock and 50 habitat-planner
checks plus the relevant engine-independent API compilation. This reviewer did
not rerun those official shared runners; the independent checks above are
separate evidence. Root owns final runtime integration verification and project
packaging. No further confirmed blocker was found in this review. Native Unity
and target-hardware verification remain the gates described above.

Final reviewed implementation fingerprints:

| File | SHA-256 |
| --- | --- |
| `Assets/AshenVoxel/Core/Details/PlantGenerator.cs` | `13230e8bf57cb8a8f305bb757363cb0f265cb0388de3a601b6965b220cd9b1d8` |
| `Assets/AshenVoxel/Core/Details/PlantModel.cs` | `4c48c80a11d367514399872ddca2515a0b22c90289356e7c49613ce40df732c2` |
| `Assets/AshenVoxel/Core/Rocks/RockGenerator.cs` | `1ce2880b16bca2b54c6abb582b902d11819daba81d91a74e39cef8b47e02c2f9` |
| `Assets/AshenVoxel/Core/Rocks/RockProfile.cs` | `8d38e605f125775fb0474f86bed32270bb272ad61c792372a9dd94d085a79885` |
| `Assets/AshenVoxel/WorldScaleData/GroundCoverScatter.cs` | `6b64cc54056e0d8c5fe57d3a6125695ee7b5b19c99d7d91c6861bc78d053dce9` |
| `Assets/AshenVoxel/Core/Materials.cs` | `0342f83df63737d241289e0c86931ebe501cd0c090cf2c773316cc170ef9afd3` |
| `Assets/AshenVoxel/Runtime/GridWalker.cs` | `4579a070edbea90f469ffe8e10d97990330a9d2442255f92887a1502b69e9f5d` |
| `Assets/AshenVoxel/WorldScaleRuntime/WorldExplorer.cs` | `f1c5e102a470609845f436bd59ee56d174771857b34957740ec4a5888fcb6fd6` |


## Version 2 rock correction

The user's later appearance feedback rejected the rounded version 1 rock forms.
That is a separate issue from the earlier mechanical safety checks. The rock
owner replaced the radial cap with six selectable polygon-piece/plane families
and retained weathering as secondary edge detail. A subsequent independent
review compared the published baseline, measured actual visible footprints,
inspected new shape/footprint drafts, and exercised 5,928 profile cases plus
budget/signed-bound probes. Two coarse-grid connectivity/visibility defects
were found and fixed during that pass. See [the full version 2 review](ROCK_SHAPE_REVIEW.md)
for methods, metrics, limitations and final source fingerprints.
