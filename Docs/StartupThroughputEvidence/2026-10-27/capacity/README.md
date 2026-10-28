# Capacity wakeup candidate

Based on installed `aerial-lod-1` (pitch-aware Voxels SHA256 `9aae54f5…`). Only
the three WorldExplorer partials change. This **capacity-candidate** directory
contains the installable canonical overlay, `source-manifest.json` and
`capacity-wakeup.patch`. Production Assets were not edited. The separate
**capacity-candidate-fixture** is testing only: its private far-detail allowance
is lowered to10MiB and must not be installed.

## Reproduced defects and resulting behavior

1. A retained in-flight far refinement can reject while old active coverage
   occupies the budget. After the replacement plan retires that coverage, a
   stopped observer previously left the request sleeping until another plan.
2. A completed replacement can reduce actual mesh bytes. Deferred work previously
   ignored the freed capacity. Contact replacements had the analogous issue.
3. Flight can reduce a still-visible tile's required sampling resolution, but
   the old policy retained its higher resolution indefinitely unless off-view.
   That surplus could block more useful detail elsewhere.
4. A larger effective contact allowance previously did not wake work without
   movement or source change, because the allowance was not checked before the
   unchanged-plan early return.

Far deferred records now retain the requested columns and measured required
detail bytes. After a **complete net decrease** in replacement/retirement
payload, eligible requests return to the existing work queue. This is not a
wakeup inside `Release`: same-size replacements cannot trigger retries during
their temporary release-before-add gap. The exact retained request is queued,
including same-resolution source replacements that a quality-only scan would
otherwise skip. Every result still passes normal admission before upload.

Contact deferred records similarly retain measured payload bytes. A completed
net decrease, or a larger clamped contact allowance, requeues only individually
admissible requests in the existing contact queue. If multiple woken requests
compete for the same available capacity, normal publication admits what fits
and defers the remainder. Static over-budget demand does not retry each frame.
Changing effective budget downward retains the existing resident-mesh behavior;
this patch does not introduce a new forced-eviction policy for that setting.

At a new plan, surplus cached far detail drops to the currently requested
resolution even for visible tiles. It does not drop below the pixel-selection
target. The10cm contact meshes and near full-detail selection are unchanged.

No memory cap was raised, no worker/cache/work queue was added, and no geometry,
material, source-resolution, renderer, source identity or save format changed.
The existing deferred collections gain measured-byte values; the existing
bounded `remove` list is reused. Far detail remains1536MiB plus the previously
proved coarse reservation; contact remains1GiB by default. Deferred work still
does not guarantee every visible tile eventually meets the pixel target.

## Counters and evidence boundary

- `VoxelRefinementDeferredTiles`: pending **memory-admission** records.
- `ContactDeferredTiles`: pending **contact memory-admission** records.
- `VoxelFaceCapacityLimitedTiles`: cached **geometric face-limit** decisions
  from `voxelCapacityQuads`; distinct from memory pressure. The far capacity
  fixture reports zero face-cap decisions while exercising memory rejection.
- `VoxelRefinementPendingTiles`, `PendingTiles`, `TerrainResidentMeshBytes` and
  existing contact byte/pending counters retain their separate meanings.

Final passing evidence is frozen under `.terrain-cache/progressive-lod/runs`:

| Run directory | Executed scope |
| --- | --- |
| `20261027-072709-420931-production-overlay-CapacityProgram` |19 bounded far-capacity checks; actual tile generation, test-only10MiB detail allowance |
| `20261027-072709-419412-production-overlay-ContactCapacityProgram` |10 contact admission/refresh checks; metadata-only synthetic payload counts, no large native buffers |
| `20261027-072547-612155-production-overlay-progressive` |30 progressive publication, capacity, fairness, source, rapid-replan and disposal checks at the shipping cap |
| `20261027-072709-431528-production-overlay-existing` |19 existing complete streamer lifecycle regressions at the shipping cap |
| `20261027-072547-631498-production-overlay-api` |5 runtime assemblies against installed Unity6000.3.27f1 APIs/C#9/netstandard2.1 |

The real-generation far fixture begins with about10.44MiB of mesh payload and
8.53MiB charged detail. It exercises in-flight rejection, flight/stop, old-plan
retirement, same-size replacement, payload reduction, a same-resolution refresh,
persistent over-budget work after wake, visible oversampling, turn/reversal,
return and shutdown. Its one same-resolution request supplies hypothetical
required bytes directly to isolate request scheduling; its neighboring payload
release uses actual generated meshes. `fixture-transform.json` records the sole
test-only source substitution,1536→10MiB.

Preserved negative evidence:

- `20261027-071917-220169-production-overlay-CapacityProgram`: the old behavior
  fails3 of18 checks (stopped wake, cheaper-replacement wake, visible oversampling).
- `20261027-072344-472124-production-ContactCapacityProgram`: installed old contact
  behavior fails4 of10 checks around payload reduction and budget-increase wake.
- `20261027-071744-555495-production-overlay-CapacityProgram`: initial16MiB fixture
  calibration did not cause rejection and timed out at that assertion; it is
  not a runtime regression. The corrected10MiB fixture reproduces the failures.
- Earlier candidate passes remain separate rather than being relabeled as the
  final revision.

All execution here is Windows .NET with numerical Unity substitutes, plus real
API compilation. No native import/rendering, terrain convergence, long-flight
memory, seam acceptance or FPS was established. A native vegetation queue of
zero does not establish terrain convergence.

## Reproduction and integration

Use `--production --overlay-dir .terrain-cache/startup-throughput/capacity-candidate`
with `Tools/ProgressiveLod/run_checks.py` for the standard progressive suite,
`--existing`, `--api`, or
`--program Tools/ProgressiveLod/ContactCapacityProgram.cs`.
For the bounded far test only, substitute overlay directory
`.terrain-cache/startup-throughput/capacity-candidate-fixture` and add
`--program Tools/ProgressiveLod/CapacityProgram.cs`.

Verify the baseline hashes against installed source, preserve .meta GUIDs and
integrate only the installable candidate through the parent's idle-safe path.
The pitch candidate is retained. Planner outside-radius slot retention remains
documented future work; this capacity change does not alter topology planning.
