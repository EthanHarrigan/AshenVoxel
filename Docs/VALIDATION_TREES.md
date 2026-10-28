# Executed procedural-tree validation

**73 checks passed** against the actual engine-independent Generation v5 tree code on 2026-10-26, using .NET SDK **8.0.427** and the required **0.1 m cells**. Compilation treats warnings as errors. The [machine-readable result](tree-validation.json) records exact source hashes, seed, counts, timings, and truncation fixtures.

This is software validation of a bounded constructive tree generator, not a botanical reconstruction study, Unity playtest, or GPU performance result. [Actual-data gallery images](TreeGallery/README.md) help assess appearance separately.

## Reproduce

From the Unity project root with Python 3 and an installed .NET 8 SDK/reference pack:

```bash
python3 Tools/run_tree_checks.py
```

Or explicitly select the SDK:

```bash
python3 Tools/run_tree_checks.py --dotnet /path/to/dotnet
```

The runner directly invokes the installed Roslyn compiler with no restore or network, compiles the actual recursive Core source tree and [TreeChecks program](../Tools/TreeChecks/Program.cs), then executes the fixtures. It records SHA-256 provenance and rejects source changes during a run. Result publication uses a temporary file and rename after successful validation.

## Checked behavior

- All four mature presets complete inside default branch, foliage, and candidate budgets in a deliberately adequate finite world.
- Every actual wood component is connected by six-neighbor paths to ground matter. The check scans generated material cells independently of the skeleton metadata.
- Every foliage component reaches actual wood through six-neighbor foliage/fruit paths. Each fruit voxel directly touches a leaf.
- Every tree cell has valid persistent opaque packed appearance; the default foliage palette stays within a restrained number of shades.
- Birch has both white mature bark and dark lenticels/young twigs.
- The same genome and seed reproduce an exact material/appearance hash. Another seed changes the generated data, and younger age changes physical height.
- Skeleton parent indices precede children, attachments lie inside the parent radius, segment radii taper monotonically, and child radii do not thicken at the joint.
- Paired recursive fork cross-section areas fit within their parent tip's area. This is a geometric heuristic, not a load-bearing stress calculation.
- Optional oak fruit uses the explicit brown acorn palette and remains attached to supported foliage; the visible 10 cm fruit cells are deliberately exaggerated compared with real acorns.
- Colored wood, leaves, and fruit survive a complete snapshot save/load round trip with an identical data hash.
- Voxel, branch, foliage-cluster, and world-boundary truncation are explicit. Zero foliage creates no unsupported fruit.
- Immutable presets remain unchanged when `With` creates an artist variation; style controls can be combined independently of species identity.
- Nonfinite dimensions, excessive recursion depth, invalid age, ambiguous transparent palette colors, and zero candidate budgets are rejected.

## Default specimen observations

Seed **714103**, age **1**, preset dimensions in meters, with enough room for the mature canopy:

| Species | Actual wood cells | Leaf cells | Segments | Supported foliage clusters | Voxel candidates | Truncated |
| --- | ---: | ---: | ---: | ---: | ---: | --- |
| Birch | 2,806 | 19,803 | 1,150 | 684 | 524,962 | No |
| Oak | 4,640 | 42,141 | 593 | 404 | 770,728 | No |
| Redwood | 378,643 | 732,413 | 2,016 | 2,935 | 13,624,984 | No |
| Pine | 5,258 | 45,194 | 1,397 | 1,593 | 1,324,395 | No |

The default work limits are4,096 branch segments,16 million voxel candidates and4,096 foliage clusters. The mature redwood fixture is73.8 m tall including buried root cells; it allocates1,761 chunks (7,213,056 material bytes and23,511,040 optional tint bytes), including the test ground. Its one unoptimized generation observation was10.61 s; this is synchronous generation, not per-frame gameplay.

Additional redwood checks verify height, tapering cross-sections, physical relief versus a zero-relief control, supported buttress wedges without suspended root columns, physical relief scaling and validation of ridge controls.

Each specimen has **one wood component, grounded**, and all leaves are supported through the checked connectivity relation. Each has 24 effective packed leaf shades in this seed: limited brightness variation combined with three subtle clump temperature choices. That does not imply independent random colors per cell.

The JSON includes single-run container timings. They include tree construction and voxel writes, but omit Unity meshes, shader work, collision proxies, input, and rendering. They are not FPS, streaming latency promises, or comparisons against published engines.

## Truncation must preserve matter connectivity

Wood rasterization preflights the complete local branch transaction before writing its volume or six-connected spine. If the remaining candidate budget is insufficient, the branch is skipped, truncation is flagged, and further geometry work stops. This avoids leaving isolated shell samples when a budget expires between volume and spine writes.

The dedicated cutoff fixture uses a half-scale birch with curved thin branches. Four different candidate caps deliberately interrupt different stages:

| Candidate cap | Candidates used | Wood cells retained | Leaf cells retained | Actual wood components / grounded |
| --- | ---: | ---: | ---: | --- |
| 100 | 80 | 8 | 0 | 1 / 1 |
| 500 | 496 | 78 | 0 | 1 / 1 |
| 2,000 | 1,999 | 358 | 0 | 1 / 1 |
| 10,000 | 10,000 | 1,186 | 55 | 1 / 1 |

All four explicitly report `VoxelBudget`; none leaves an orphan wood or foliage component. A partial result can contain a skeleton segment list beyond the rasterized subset. Callers must inspect `Truncated` and `TruncationReason` before treating that graph as a fully materialized tree.

## Interpretation limits

The generator shares a leader/scaffold/fork/tropism grammar across presets and uses local voxel stamping with connected foliage filtering. It does not implement space colonization, competitive light-driven growth, biological development, a structural stress solver, or falling rigid branches. Ten-centimeter thin twigs are represented with six-connected digital spines even when their nominal radius is below one cell.

Material and appearance persist; the returned skeleton is transient metadata and is not an ownership channel saved with each voxel. Specimen count assertions use initially clear tree space. When trees overlap, reported counts describe newly written material cells rather than a unique per-tree ownership partition.

The species fixtures use one representative seed for full connectivity/palette assertions and additional seed/age tests for birch. They do not prove appearance or connectivity for every possible custom genome. Deliberately clipped/custom-budget output must be handled through explicit truncation. Unity scene import, native serialization, shader appearance, authoring UI, and player performance still require their own checks.
