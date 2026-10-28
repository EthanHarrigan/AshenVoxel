# Startup floor and commit-pressure liveness — 2026-10-28

Observed during the user's failed loading report: the read-only Editor state at
01:37:20 UTC reported boot not ready, population ready,99 models,22469 instances,
1715 resident cells and zero population queue/error. Saved preferences were Maximum,
30km view,100m contact radius, grounded roots and the expanded source window. A
Windows memory-counter read found42,797,682,688 committed bytes against a
44,815,704,064-byte limit: approximately1.88GiB available commit. Free RAM was
approximately8.7GiB; that does not override the commit limit. These observations
strongly support the terrain safety pause as the loading blocker, rather than an
unfinished population or a proof that the learned source failed.

The existing terrain guard requires2GiB reserve plus64MiB fixed upload margin,
five times the pending payload and256MiB resume hysteresis. `AdvanceBuilds` formerly
returned before polling/draining any worker when even the base threshold failed.
Completed face/proxy data and large reusable upload arrays therefore remained
retained. A payload-specific headroom wait also occupied the one worker lane
indefinitely, preventing smaller replacements from reclaiming detail. The boot
coroutine held input until terrain readiness, without exposing that reason.

Installed in this source revision:

- On a base safety pause, cancel/requeue the exact current demand, drain a completed
  worker task, discard its replaceable result and release reusable packed scratch.
  Completed payload size is retained as small scheduling metadata so it is not
  regenerated repeatedly without enough headroom.
- Defer oversized voxel and height-proxy uploads by tile/representation/quads and
  measured payload. Keep requests queued and observable; return the worker lane to
  smaller work. Smaller same-key downshifts can run when their own safe headroom
  fits. Deferred work wakes only after actual available commit plus hysteresis
  admits it. Source changes invalidate old measurements; retired demand is pruned.
- Under base pressure, retire at most four outer contact meshes per call, only when
  complete current active far coverage exists underneath. Never retire urgent
  player contact, create a hole or lose the retained contact request.
- During coarse startup, generate a small arrival contact neighborhood before all
  far tiles finish. `IsTerrainReadyArea` already consults published contacts first.
  The100m preference expands after complete coarse coverage; initial contact demand
  is at most16 tiles in the regression. Whole-plan coverage remains pending and
  `coarseStartup` remains true until its existing complete-coverage gate passes.

The safety reserve, upload multiplier, quality limits, source identities and saved
preferences were not weakened. Diagnostics now expose `VoxelRequiredCommitBytes`
(base threshold including current hysteresis), `VoxelCommitDeferredTiles` and
`VoxelPressureScratchReleasedBytes`. Releasing references makes them collectible;
this does not assert immediate process-commit reduction or native recovery.

Parent integration owns the loading UI and Bootstrap ordering. Recommended order:
enable/configure Vista, yield until the local terrain area is ready, then initialize
the full population. Vista's worker/contact/update path needs its configured source
and camera, not Population or Bootstrap LateUpdate. This prevents thousands of
population instances/prototype meshes from consuming commit before the first floor.
If other applications already leave less than the unchanged safety reserve, the
loader must report the block honestly rather than enable unsafe movement.

Verification:

- 40 progressive lifecycle checks PASS, including local floor before far coverage,
  small startup neighborhood, completed-worker drain, scratch release, retained
  urgent floor/queues, unchanged reserve rejection, downshifts bypassing a held
  oversized upgrade, and recovery after simulated headroom returns.
- 43 source/refinement checks PASS.
- Actual installed Unity APIs compile WorldScaleData and WorldScaleRuntime with
  Windows Editor/Standalone symbols, including the real commit-probe declarations.
- UTF8 static project integrity PASS. No Unity control, native import/play, FPS or
  native memory-recovery assertion was performed by this worker.

Reports and exact source identities: `progressive-result.json`, `live-checks.json`,
`api-result.json` and `changed-source-sha256.json`. The progressive result points to
its unique frozen source snapshot. Preimages and the first unchanged-suite run are
preserved alongside the final logs. Parent checkpoint records subsequent integration
and any actual running-scene evidence.
