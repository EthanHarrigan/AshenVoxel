# Source view coverage 1

FROZEN / 44 focused checks and API PASS / NOT INSTALLED / NOT NATIVE VERIFIED.
Exactly two existing C# payload files, no .meta changes; preserve both GUIDs.
MAIN preimages match at freeze. Manifest hashes and raw preimages/diffs are here.
No MAIN/control files, cache helper, terrain worker/lifecycle, saved camera/options,
scene, source bytes, collision/matter, service/dependency launch, UI or agents edited.

## Behavior

Existing Tick calls still work. Optional trailing arguments are WorldPoint?
destination=null and double viewHorizontalHalfAngleDegrees=55. Without an explicit
waypoint, the destination is the current horizontal view direction times the
existing1600m look-ahead. Fixed640m grid keys match the current runtime. Configure
now requires TileMeters==640 rather than creating incompatible demand grids.

A finite17x17 neighborhood plus at most one destination insertion (<=290 scan
attempts) supplies unique sorted cells. Planning radius is bounded to5120m plus
cell intersection allowance; core/influence extents can extend by a cell/halo.
Default corridor reach is3520m plus a cell allowance. Far destinations use a
bounded intermediate route cell4480m along the route, rather than scheduling the
whole route or map. This XZ corridor is an approximate view-priority heuristic,
not camera occlusion, terrain visibility or full viewport coverage.

Priority: current cell (except fast-travel deferral), destination, immediate view
sides within1920m, mid-distance view corridor, then optional near peripheral cells.
A224m score preference keeps previously selected cells stable. Two-thirds of the
available non-current/destination slots may go to immediate view cells, preserving
some selection capacity for mid-distance terrain. At default16patch/16MiB limits,
selection is at most16 source cells:6.5536 square km of non-overlapping640m cores,
plus blend halos. This is not5m source throughout the full visible world.

Current/destination requests and all other demand share the existing TWO slots.
No queue/thread/service added. Stale tasks retain slot ownership until completion;
completed results remain owned while selected but publication is unsafe. Existing
30s per-key backoff,64-key bounded retry metadata, immutable copy and cancellation/
terminal draining remain. Source-ready stable cores, including an enclosing live
core or fully bundled fine coverage, never start a new request. Completed work that
has become source-ready is released without unnecessary publication.

Selection reserves estimated EXACT copied-grid bytes within the existing field
resident caps, without increasing either cap. Published extent/LastUse metadata
permits simulation of the existing safe LRU victim order after ordinary view-demand
Touch calls. If publication would remove selected ready coverage, admission holds
the completed result instead of churning. Unselected safe LRU content can retire.
Up to two completed request payloads remain outside the resident field cap, as in
the existing two-slot implementation; no claim that16MiB is whole-process memory.
Completed publication attempts are limited to twice per second per owned slot.

Default grounded protection is unchanged. Non-current near protected cells are
not requested by the wider selector while grounded. Pre-spawn or exact airborne
policy witness plus current conservative512m clearance permits wider demand.
That is only a demand hint: actual publication still checks old AND new bounds.
HoldForGameplayMatter still blocks publication and eviction everywhere. A late
current result after ground arrival may remain held until safe; no terrain change
under a standing player is introduced by this patch.

## Read-only extent / diagnostics API

Field CapturePublishedCoverage() returns a metadata COPY (<=32 constructor cap,
16 default), with Key, SourceIdentity, CoreBounds, InfluenceBounds, SpacingMeters,
PayloadBytes and LastUse. Arrays of actual source samples are never exposed.
MaximumResidentPatches/MaximumResidentPayloadBytes expose actual configured caps.
HasPublishedCoreCoverage(core) requires an enclosing full-resolution core, never
partial blend influence. CanEvictPublishedPatch(key,observer,policy) uses the exact
existing Safe check without mutation. Source hashes, State composition, Safe,
Commit, TryPublish and TryRetire are textually unchanged from current MAIN.

Live diagnostics: DemandCandidateCells, DesiredSourceCells,
ReadyDesiredSourceCells, MissingDesiredSourceCells, HeldCompletedRequests,
BundledCandidateCells, CapacityDeferredCells, DesiredCoreAreaSquareMeters,
ReadyCoreAreaSquareMeters and SourceCoverageStatus. Existing Status includes the
selected5m ready/missing/held/deferred summary and explicitly warns that terrain
outside selected cells may remain macro. Read published metadata to identify the
ACTUAL6/16 resident extents; source-here5m is not a far scene coverage measurement.
Ready/core-area stats describe complete source cores, not rendered mesh fidelity,
FPS, material detail, matter coverage or percent of the camera view.

## Integration / minimal Bootstrap proposal

The two payload files alone work with current Bootstrap calls/default55degree
horizontal half-angle. Bootstrap-FOV.PROPOSAL.diff is OPTIONAL, NOT PAYLOAD: read
actual camera horizontal FOV from vertical FOV/aspect and pass it in Tick. If an
actual travel waypoint already exists, pass that as destination instead of null;
do not invent UI or a waypoint service. Both existing public Tick parameters and
saved camera are preserved.

No startup gate edit is included. Current Bootstrap stops its pre-spawn wait as
soon as the player POINT reaches5m, so it does not promise a decoded neighborhood
before entering gameplay, especially if source here was already5m at launch. For
an optional bounded visible prewarm, the coordinator can reuse its existing startup
deadline and BeforeSpawn witness, tick until demand has initialized, then require
current5m plus ReadyDesiredSourceCells>=Min(4,DesiredSourceCells), or deadline.
This may wait on cold model decode and needs an explicit product/time-budget
choice; never claim this candidate changes that gate or certifies full-view ready.

## Validation and remaining gates

checks/20261027T210941085608Z/report.json + focused.log:44 focused checks PASS.
Reused/adapted existing Tools/ExplorationLiveChecks FieldChecks, removed broad
renderer/network/concurrency suites, added pure actual planner/admission checks.
C#9/warnings-as-errors Core/WorldScaleData compile separately against netstandard2.1
without Unity; changed Live compiles against official Unity6000.3.27f1 refs and the
current native WorldScaleRuntime API binary (hash in report). CPU-only .NET tests;
no native calls, model request, import/render/performance claim. Earlier run kept
its own identity; final rerun corrects only a test output label about composition
returning to the original hash after identical content is restored.

Native acceptance: guarded install/recompile; saved valley inspect exact published
bounds and selected readiness; existing service source requests progress beyond
current/forward strip, settle rather than oscillate; stationary and camera turn,
destination change, fast travel/deceleration, grounded late-result hold, verified
high flight and descent, matter hold, unavailable service/disable/cancel/re-enable;
resident<=16/16MiB, requests<=2 and held payloads accounted. Confirm visible source
promotion triggers existing terrain/population refresh, and old still-needed
coverage remains. Actual near5m does not prove far5m or render convergence. No
whole-map decode or near-instant/full fidelity claim.
