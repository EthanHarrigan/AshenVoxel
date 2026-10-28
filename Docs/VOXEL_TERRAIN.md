# Streamed10cm terrain conversion

**Superseded for default Exploration:** [whole-world voxel LODs](WORLD_VOXEL_LODS.md)
now cover the complete view. This document and its49-page native evidence retain
the earlier near-overlay checkpoint; do not relabel those runs as global LOD tests.

Exploration now converts nearby terrain into actual material-bearing10cm cubic
cells. `EnableVoxelTerrain` in the Exploration settings takes effect on the next
Play; it is enabled by default. Disable it to compare original smooth terrain
and continuous collision. No source atlas or baked world identity was changed.

## Materials and authority

Conversion samples the original learned field and shared SurfaceMaterialSampler,
not a potentially stale display mesh. Existing catalog grass/litter, soil,
sediment, bedrock, ice and snow retain their material IDs and opaque sRGB pigment.
Geometry provenance is `voxel-surface-v1`; Core Generation5 and save Format2
remain unchanged. No save format migration or resumed-simulation claim is made.

Engine-free VoxelTerrainPage covers32×32 cells (3.2m square), samples a one-column
halo and materializes the upper metre plus exposed sidewalls through
VoxelWorld.Set. Its16³ chunks hold actual matter. Buried terrain remains implicit
under the shared sampler: absent deep resident cells must not be treated as air
or caves. Pages are currently immutable after publication and are not attached
to gravity or editing. One replaceable mesh per page draws cubic exposed faces
using the existing URP voxel material, flat normals, pigment and corner AO.

Collision queries the same global cell centres and occupied top faces regardless
of mesh residency. Page publication therefore does not move occupied ground.
Existing prop collision and soft-vegetation resistance remain delegated. Prop
placement still uses the smooth field; surface seating can differ by roughly5cm
before footprint/slope effects. The controller retains its camera step smoothing.

## Bounds and transitions

The default ring is7×7 pages (22.4m square), with at most49 residents, one worker
and one main-thread mesh upload per Tick. Workers capture immutable refinement
snapshots and create no Unity objects. F1 shows page/cell/triangle and queue counts.
A flat page stores10,240 occupied cells regardless of mountain altitude.

Pages are capped at256 vertical cells (25.6m) and65,536 faces. Exceeding either
keeps the height proxy and reports why. Deep mountain volumes are never allocated.
These bounds do not guarantee millisecond budgets: a page upload can exceed a
frame's desired work time and still needs native profiling.

Only completed uploaded pages mask the original terrain. Forward, shadow, depth
and depth-normal passes share one bounded8×8 readiness grid. A one-metre perimeter
apron closes small smooth/cubic differences. Rebase transforms and mask coordinates
subtract the same double origin in LateUpdate. Invalidation, travel and teardown
cancel obsolete jobs and restore proxy coverage; remaining queues are retained.

Distant terrain retains its existing mesh LODs. This change does not create
sub5m learned relief, geological microdetail, caves, overhangs, digging, full-world
persistent edits, granular terrain or falling islands. Those require additional
volume-residency, persistence and simulation work. Meshes are still render caches.

## Executed evidence

- `python -X utf8 Tools/run_voxel_terrain_checks.py`:25 engine-free checks,
  including actual bundled terrain, layered cliffs, cell/pigment ownership,
  quantization, negative coordinates, cancellation, caps, collision agreement and
  actual motor traversal over cubic steps at30/60/144Hz.
- `python -X utf8 Tools/run_voxel_runtime_checks.py`:11 real runtime C# checks
  with Unity substitutes: masks, uploads, queue preservation, invalidation,
  rebasing, fast travel and cleanup.
- `python -X utf8 Tools/check_voxel_unity_api.py`:five affected runtime assemblies
  compiled separately against installed Unity6000.3.27f1 and .NETStandard2.1 APIs.
- Core87, mesh regression/oracle, startup31, movement77 and existing explorer86
  checks passed. These are headless/.NET observations, not native player timings.

[VoxelTerrainEvidence](VoxelTerrainEvidence) holds reports and an exact-face
offline preview. The learned spawn page and controlled cliff fixture are labelled
separately. Regenerate using `Tools/VoxelTerrainChecks/render_preview.py` after
data checks. Provenance records exported-data, renderer and image hashes.

Native controlled-fixture verification **passed** in a separate Windows
Unity6000.3.27f1 / D3D11 project on RTX3080 Ti:49 uploaded pages,501,760 retained
cells,240,576 triangles, eight material passes checked, neither terrain shader
reported an error, and the native Unity mesh regression menu passed. Close and
transition captures were inspected. See `native-validation.json`, `native-close.png`
and `native-transition.png` in the evidence folder. This is a controlled fixture,
not the full Exploration scene; its proxy uses deliberately constant pigment to
make the replacement boundary visible. It does not prove full-world palette
matching or every lighting variant.

The first native harness timed out during initial package import before any
pages published. Its exact harness, failure and log remain in `native-attempt1`.
Attempt2 starts its timer when updates can run; its separate report/log and
source hashes describe the successful execution. Voxel conversion/residency and
proxy-mask files matched that snapshot. Concurrent forest work subsequently changed
Bootstrap/Settings and other forest sources; the report explicitly lists those
differences. Its native result must not be relabelled as a full current-scene test.
Reproduce using the native-project preparation
and recording scripts under `Tools/VoxelTerrainChecks`; do not overwrite runs.

Interactive Exploration movement, temporal transitions, full-scene shadows/AO,
standalone builds and CPU/GPU frame-time distributions remain acceptance gates.
Earlier frozen forest native evidence does not test this later terrain revision.

Native follow-up: inspect close ground/slopes/snow, cross page boundaries walking
and flying, rebase beyond256m, inspect F1 counts, compare fixed views with the
setting on/off, run the Unity mesh regression menu and check console errors.
