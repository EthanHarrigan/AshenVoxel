# Exploration awake/dormant debris — October 27, 2026 (Denver)

The debris scheduler is installed in `NaturalTreePhysics` and wired into
`ExplorationNaturalTreeMatter`. Exact 10 cm material/tint ownership and the complete
loose-parcel render list remain intact. Motion visits only awake parcel leaders.
The 100,001-cell fixture retains all 4,001 weighted parcels while settled ticks
perform **zero body candidates**, down from 4,001 in the preceding study.

## Wake rules and bounded work

- Pickup enable, hand motion and radius changes request a scan only when the
  collection sphere overlaps conservative dormant bounds. Falling into pickup
  range also schedules a wake. Changing a query does not reset its scan cursor.
- Explicit kicks wake matching dormant leaders immediately and apply an impulse.
  The existing 8,192-leader per-command examination limit remains.
- Terrain publication/retirement uses `ExplorationLiveRefinement.RegionChanged`.
  The owner converts global metres to session-local metres, clears its bounded
  ground cache when the affected region overlaps sampled columns, then queues
  affected dormant leaders. Future terrain-edit publishers can call the same
  `NotifyTerrainChanged` entrypoint. Terrain excavation itself is not added here.
- Terrain regions coalesce; a further pass preserves changes arriving behind the
  cursor. Cargo swap-removal also retains a follow-up pass when necessary.
- Wake scans examine at most 256 candidates per Step and share the overall body
  candidate budget. Section, wake and motion work alternate at budget one. Pending
  wake work remains visible through public counters and the session HUD.
- Waking resets the parcel clock: dormant time is never accumulated as gravity or
  suction travel. Waking removes the parcel's old coarse pile occupancy, preventing
  phantom stack growth after repeated kicks or pickup.
- The renderer continues to use every loose leader. Dormancy neither erases a
  parcel nor transfers its ownership. Terrain subscriptions are released on
  retirement, disable and destruction. Unknown terrain leaves an awake retry.

Dormant bounds deliberately need not shrink until the dormant population empties;
they may admit extra scans but cannot omit a relevant sleeper. Added scheduling
containers scale with parcel leaders, not the million-cell source specimen.
The coarse pile remains an approximate visual contact model. Candidate limits are
not wall-clock guarantees; cache clearing is bounded by the existing 131,072-column
cache, and normal render work continues for visible dormant parcels.

## Accepted evidence

- [Full physics suite](run-04/cheap-physics.json): **144,362 assertions**. Includes
  500 dormant parcels across 600 idle ticks with zero candidates/samples/pose
  revisions; pickup of a tail parcel at budget one; continuous hand movement,
  radius/enable changes, swapped list entries, conservation, 20 kick cycles,
  raised/removed/missing terrain, coalesced events and eventual cache readiness.
  The 100,001 logical cells/4,001 parcels are eventually collected with exact
  material/tint credit and follower ownership. Mature redwood support and previous
  cut/section/liveness tests also pass.
- [Renderer checks](renderer-result.json): actual renderer submits dormant
  instances with an empty motion list and stops submitting them after collection;
  existing geometry/lifecycle checks pass. The shared workspace suite also includes
  unrelated concurrent renderer feedback/patch checks; see its source manifest.
- [Native Unity fixture](native-final/result.json): Unity 6000.3.27f1,
  Direct3D12, RTX 3080 Ti. Actual shader passes, mesh regression, visible dormant
  debris, zero scheduled idle candidates, kick motion, terrain wake, exact cargo,
  global/local cache invalidation and subscription cleanup pass. Five frame PNGs
  are retained. Saved Exploration scene roots and dirty state remain unchanged.
- [Core](core-checks.log): 91 checks. [Trees](tree-checks.log): 73 checks.
  [Meshes](mesh-checks.log): independent face/color/AO oracle.
  [Authoring](authoring-checks.log): 40 checks.
  [Unity API compilation](unity-api-current.json): six real assembly boundaries.
  [Project integrity](project-checks.log): assets, references and 10 cm contract.

Standalone idle measurements for 3,001 parcels were about 0.0002 ms per Step
(near timer resolution, zero measured allocations). This measures this scheduler
only. Native evidence is an isolated Edit-mode fixture with explicit draw commands,
plus a CPU test of the ordinary renderer submission path. It does not establish
full Exploration gameplay feel, whole-scene FPS, terrain memory acceptance or
automatic camera/depth/shadow acceptance in Play. Those broader gates remain open.

## Provenance and intermediate runs

`verification.json` records source identities and final evidence hashes. Historical
gameplay ZIPs, scene, packages, generator and save format were not changed by this
work. Concurrent hand-input/renderer work is preserved and final native validation
uses its then-current owner adapter; earlier evidence is not relabelled.

`run-01` is the focused scheduler suite; `run-02` is the first full run. Its legacy
airborne timing fixture had only woken the first batch and must not be used for
all-awake performance claims. `run-03` waits for every parcel to wake. `run-04`
additionally asserts zero visits for the exact 4,001-parcel fixture. All these runs
passed their stated assertions; the fixture correction is retained explicitly.

`unity-api.log` and `unity-api-final.log` preserve failed attempts: the old checker
omitted the FiniteMatter assembly and runtime subdirectories. The checker now
includes both and accepts a separate report destination. Successful later reports
do not replace those failures. `native-run01.json` precedes a concurrent hand-input
adapter edit; `native-final` is the later complete source-matched run.

Reproduce from the workspace root:

```text
python Tools/run_cheap_matter_checks.py --output-dir <new-evidence-directory>
python Tools/NaturalMatterRendererChecks/run_checks.py
python Tools/check_voxel_unity_api.py --report <new-report.json>
python Tools/run_core_checks.py
python Tools/run_tree_checks.py
python Tools/run_mesh_checks.py
python Tools/run_authoring_checks.py
python Tools/check_project.py
```

The native fixture accepts the literal `run` in
`.terrain-cache/natural-matter-native.request` only after current scripts have
compiled and the Editor is stopped. It does not enter Play or change the scene.
