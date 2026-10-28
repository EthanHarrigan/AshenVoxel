# Near-front source 1 — bounded continuous priority

FROZEN /18narrow actual-coordinate checks + affected API PASS / NOT INSTALLED.
ONE existing Live.cs payload only. Preimage542046...; Field26a7... untouched.
Full hashes/diff/raw preimage in manifest. Preserve .meta. No Bootstrap/Main/Field,
Unity/Editor/UI, world bytes, camera, collision/matter, packages/services or agents
changed. Bootstrap proposal is separate, not payload or tested native integration.

## Exact cause / evidence boundary

Native valley-source32-late snapshot (UTC22:19:10, full path+SHA in
manifest) shows32resident/30selected/30ready/0held. Source-map JSON already proves
projected1km center/right1280m macro while2km center5m; not actual visibility tests.
The missing center/right cores -65/-23/640 and -64/-23/640 were FILTERED OUT before
rank/capacity selection, because influence128m + query/prop guard96m approaches
observer within402.8426m, inside the512m protection disk. Correct grounded safety
prevents late publication. Four scattered pre-spawn publications (current,
destination,left neighbor,behind neighbor) were insufficient continuity evidence.
This is a prewarm/demand defect, not32quota exhaustion or proof of a broken decoder.

New central near-front footprint finds9 required cores at exact saved camera:
-64/-22,-65/-24,-65/-22,-65/-23,-64/-23,-66/-22,-66/-23,-64/-24,-67/-23 (all/640).
Native has6/9; THIRD missing protected edge cell is -66/-22/640. Required and missing
are now explicit metadata, not inferred from total selected count. No whole-valley,
whole-view, decoded-height authenticity, render-convergence or FPS claim from this
controlled coordinate/extents replay; fixture sample values are controlled.

## Implemented behavior

Current priority0 and actual/predicted destination1 remain first. Priority2 now
contains every640m core intersecting a continuous bounded CENTRAL triangle from
observer to1280m forward. Its half-width is min(1280m,0.5*tan(horizontal-half-FOV)
*1280m), covering the reference map's near center and half-frustum side samples
at this camera. This is an XZ heuristic, not visibility/occlusion or full-frustum
coverage. Strict positive-area triangle/AABB SAT uses observer-relative doubles.
At most the existing290 candidate scans; no world expansion.

Other near cells priority3, mid corridor4, peripheral5 follow required continuity.
Removed old optional-mid slot reservation so far cells cannot consume capacity
while required near cores remain unselected. Mandatory footprint accounting and
safe LRU admission are unchanged. Default16/16MiB and explicit32/32MiB are unchanged;
no arbitrary cap increase, worker, queue or publication policy added.

After grounding, already-ready protected view cores remain selected. Missing
protected noncurrent cores are still skipped and counted; no forced terrain change
beneath player. Current late results keep existing safe hold behavior. Normal
forward movement requests future near/destination cores while still beyond512m
guard, not only at startup: exact native replay600m earlier reaches the future
center-hole core at979.016m guard distance, priority1 (destination beats near2),
can publish under ordinary safety then remains protected on approach. This is
spatial prefetch, not a model-latency/speed guarantee; fast travel or a new view
can still expose safely held/missing source. Explicit waypoint remains supported.

## Read-only API / coordinator integration

NearFrontRequiredCells, NearFrontReadyCells, NearFrontGroundProtectedMissingCells,
NearFrontSourceReady (required>0 and all required ready).
CaptureNearFrontSourceDemand() copies key/core/priority/ready/NearFront flags for
the REQUIRED set, not just selected cells. Bounded by existing290 candidate limit;
9actual. Selected metadata now also has NearFront flag. SourceCoverageStatus adds
near ready/required and ground-protected missing count. These are source extents,
not geometry/material/view-quality metrics. Required set updates at existing0.5s
demand cadence; ready counts refresh during online Tick.

Pure coverage planning now runs while service is connecting/unavailable, using
existing demand cadence; starting FetchCopy is explicitly guarded by Connected.
Thus bundled-ready near source can be recognized without waiting for a model
reply. No new network call/task/slot; stopping still drains before planning. This
supports gating even when source at player was already5m at launch.

## REQUIRED companion Bootstrap proposal (coordinator owns)

Bootstrap-near-front-gate.PROPOSAL.diff, NOT PAYLOAD:
1. Run existing pre-spawn source gate when live refinement is enabled, even when
   current POINT already5m (near source may not be).
2. Existing point/selected4 condition additionally requires NearFrontSourceReady.
3. Keep exact existing deadline and BeforeSpawn witness. Timeout status says source
   here and near ready/required, explicitly incomplete; only all-required ready
   earns verified destination+near-front status. No unbounded loading wait.

Without this companion gate, Live-only ranking cannot turn the old4selected gate
into complete near source. Even new first4 (current,destination,left,center hole)
leave some near required cells pending. A deadline can still end early, in which
case default grounded guard correctly continues to block missing local source.
Long stationary post-spawn waiting does not repair guarded holes; pre-spawn or
verified old/new clear flight is required. No safe weakening suggested. Camera
turn to other unprepared protected cores can still require safe source promotion;
this bounded FRONT requirement is not360degree/all-world source coverage.

## Narrow validation

checks/20261027T225107454760Z/report.json + focused.log:18narrow checks only. Existing
helper/harness reused;44/55 suites are NOT rerun. C#9 warnings-as-errors separate
engine-free Core/WorldScaleData, changed Live compiled against official Unity
6000.3.27f1/current WorldScaleRuntime API. Native coordinates/published extents with
controlled patch values; no network/model/native/render calls. Cases: exact filter
cause, all3guarded missing cells, required core union on near projected rays,
current/destination, prewarm4insufficiency, all9safe publication within caps,
grounded retention/default refusal, forward movement before guard, matter/hash.
Earlier failure retained: test incorrectly required rank2 for a future core whose
legitimate higher rank was destination1; acceptance corrected to rank<=2. Final
also adds complete3missing audit and offline read-only planning support. Do not
relabel earlier17case/source run as this18case candidate.

Native gates: coordinator merges Live+its gate/status, imports then restart exact
valley before player initialization; capture all required keys and source spacing,
truthful timeout/missing guard counts, request<=2/resident<=configured cap and bytes.
Record9/9near independently from selected30/30 or32/32. Verify ordinary movement
prefetch/retention plus grounded/flight/matter/return/cancel. Actual mesh commit
pressure and terrain/source transition cliffs remain independent open issues.
