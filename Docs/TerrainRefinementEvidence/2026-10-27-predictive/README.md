# Bounded predictive source and mesh reclamation — 2026-10-27

Installed source changes, with numerical lifecycle evidence; native play/render,
GPU cold-inference speed, maximum quality memory and visual convergence remain
unverified for this revision. Existing settings, datasets, generator identity,
historical ZIPs and earlier release evidence are unchanged.

The runtime requests actual learned 5 m coordinate-view source through the existing
matching local service and immutable canonical-owner transport. This is distinct
from the model's native 30 m training resolution, render-column spacing and the
authoritative 10 cm matter grid. No new generated geometry version is introduced:
the changes choose which existing source samples and replaceable meshes to retain.

Implemented:

- Nearby rear/side source cores intersecting a 640 m disk now share priority with
  near-front source, before distant visible detail. Existing current/destination
  priorities, protected ground/query/prop guards and 16/32-patch byte caps remain.
- At each existing 0.5 s demand interval, actual XZ travel predicts a destination
  six seconds ahead, with a minimum one-cell lead and bounded maximum lead.
  Explicit destinations win. Speeds below 4 m/s, above 2000 m/s or samples outside
  0.1–2 s retain view-based demand. This helps sideways/backwards flight without
  assuming that camera yaw equals velocity; teleports are not velocity estimates.
- At most one decoded resident beyond 5120 m plus a 640 m hysteresis cell is
  retired per demand interval. Influence/guard padding is included. Selected
  residents and active gameplay-matter holds are retained. Atomic retirement
  emits the same exact regional renderer/population invalidation as publication;
  existing captured worker snapshots stay immutable until they finish.
- The one-worker mesh refinement scan can now lower quality on an unchanged plan
  key. Fast travel reduces retained fine tiles to the existing 32-column ceiling;
  other camera changes require a twofold quality gap to avoid boundary churn.
  Reclamation takes priority over upgrades, and old complete coverage stays visible
  until replacement. Deferred upgrade admission does not block downshifting.
  Slowing resumes the existing 8→32→64→128→256 refinement.

Source publication already calls `WorldExplorer.RefreshExternalRegion`; inspection
and the existing renderer lifecycle check confirm that changed regions mark cached
meshes dirty, cancel stale build ownership and retain active coverage for replacement.
This revision reuses that mechanism for retirement, rather than adding another cache.

Validation commands (from project root):

```powershell
python Tools/run_exploration_live_checks.py --report Docs/TerrainRefinementEvidence/2026-10-27-predictive/live-checks.json
python Tools/ProgressiveLod/run_checks.py --production
$env:PYTHONUTF8='1'; python Tools/check_project.py
```

`live-checks.json` records exact source/asset hashes and scope. The progressive
runner preserves a unique frozen source snapshot and result for every run; the
path is in `progressive-checks.log`. `progressive-result.json` copies the final
successful report. These are Windows .NET8 checks with numerical Unity substitutes,
not Editor import, URP rendering, millisecond bounds or full-scene FPS evidence.
The new checks exercise velocity rejection, near rear/side priority, gameplay hold,
atomic immutable retirement, bounded runtime unload/event emission, actual same-plan
mesh byte reduction, retained coverage and restored detail after slowing.

Validation plumbing was stale: missing WorldExplorerMemory inventory/Time substitute,
old revision-in-source-identity assertion, hardcoded earlier dataset height and a
removed constant reflected as a field. Their initial failures remain in separate
logs. Fixes test the current content identity/current bundled data and the public
detail budget, without weakening guard/conservation/lifecycle requirements.

Limits: learned source is still a bounded moving resident window, not full-view
5 m coverage. Ground-protected missing source waits for pre-spawn or verified clear
flight; changes never silently move ground beneath a standing player. Two in-flight
model requests remain owned until completion, including stale work. Macro source
provides distant coverage while learned patches fill; this patch does not add a
new coarse neural stage or prove any new cold inference throughput. Coarse/detailed
mesh work still shares one worker and existing admission budgets. Existing cube/proxy
seams, snow/material appearance and full-profile memory pressure remain open.

Research basis (primary sources checked this session):

- [Distant Horizons project](https://gitlab.com/distant-horizons-team/distant-horizons)
  describes lowering distant terrain quality; its
  [world generation API](https://distant-horizons-f74d0f.gitlab.io/com/seibel/distanthorizons/api/interfaces/config/both/IDhApiWorldGenerationConfig.html)
  separates distant generation outside vanilla render distance. This motivates
  decoupling source residency, mesh quality and complete distant coverage. The
  travel predictor here is our implementation, not a claim of DH algorithm parity.
- [InfiniteDiffusion project](https://xandergos.github.io/terrain-diffusion/) and
  [upstream implementation](https://github.com/xandergos/terrain-diffusion)
  describe lazy unbounded sampling and a cascade from coarse global features to
  decoded height maps. Their performance claims do not measure this Unity scene;
  project canonical ownership and published source identity remain authoritative.
