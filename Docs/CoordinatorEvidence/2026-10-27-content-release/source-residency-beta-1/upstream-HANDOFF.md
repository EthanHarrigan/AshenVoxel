# Source resident selection 1 — frozen follow-up

FROZEN /55focused checks + affected API PASS / NOT INSTALLED / NOT NATIVE TESTED.
Only ONE payload file: ExplorationLiveRefinement.cs. Preserve its .meta. MAIN
preimage0c2f90... and unchanged Field26a7... match freeze; manifest pins full hashes.
No MAIN, Field, Bootstrap/settings, cache/worker, Editor/UI, source-byte, camera,
collision/matter, package/service or agent modifications. DEV docs checkpoint only.

## Actual evidence and bounded inference

Original native snapshot: MAIN Logs/MountainReviewFrames/valley-source-view-cache-first/
terrain-20261027-212408-234.json,21:24:08UTC. Full SHA in manifest. Reduced immutable
metadata in native-input.json. Snapshot directly observes16resident,16selected,
14selected ready,2completed held, sourcePoint5m,8,282,525,696B available commit.

At exact observer(-40954.96880764632,1984.2649999761584,-13453.1573951418),
horizontal forward from native(-.253278017,-.871728182), horizontal half-angle
56.505306degrees from vertical93.432838/FOV and1668x1172aspect:

- Reconstructed missing selection keys: -63/-25/640 and -65/-26/640.
- Observed resident keys -65/-22/640 and -64/-21/640 are OUTSIDE reconstructed
  grounded selection. Both full influence+96m guard contain observer (distance0).
- Current -64/-22/640 is the third protected footprint and IS selected.

Original JSON has no selected-key list, held-key list, policy or held reasons.
The two missing keys are reconstructed, not directly observed held-key telemetry.
The controlled replay preserves native coordinates, keys, extents and payloads,
using fixture height/climate with default grounded publication safety, not actual
neural sample bytes. Default/uncleared-flight protection makes those two unselected
residents non-evictable. If actual session had a different policy, collect the new
telemetry below; do not relabel inference as native held-key proof.

## Cause and fix

Old selector reserved16 DESIRED cells, ignoring TWO unselected mandatory residents.
Minimum actual footprint was18, so correct admission refused instead of evicting
protected ground or higher-priority desired detail. No source decoder was stuck.

New selection reserves actual non-evictable resident payload/count FIRST, then buys
ranked current/destination/view demand. A selected core already covered by a
reserved resident is charged once, including current or enclosing grids. Existing
safe LRU admission still rechecks publication and selected retention. No safety,
content digest, composition, source spacing or cache-key logic changes.

At the16/16 current-camera replay, feasible selection is14 and all14 are already
ready; current/destination remain first, no eviction needed. Further candidates
remain explicitly capacity-deferred. This repairs impossible scheduling; it does
NOT add source detail, raise caps, certify the whole valley, or erase deferred work.
The old two completed low-priority requests lose selection and drain/discard through
the existing ownership path. No request cancellation bypass, orphan work, extra
slot/thread/queue, protected eviction or selected-detail churn.

If capacity blocks current/destination too, PriorityCapacityDeferredCells reports
that separately; do not equate0missing-selected with satisfying all urgent demand.
Existing grounded late-source and HoldForGameplayMatter publication holds remain.
Original Field file (State, Safe, TryPublish/TryRetire/hash) is completely untouched.

## Bounded read-only diagnostic seam (main thread)

CaptureSelectedSourceDemand(): <=32 copies of Key,Core,Priority,Ready.
CaptureSourceRequests(): <=2 copies of Key,Core,Priority,Selected,Current,Completed,
Reason. Reason distinguishes request/copy, stale draining, complete awaiting check,
actual HoldReason (capacity vs field safety/matter refusal).
CaptureMandatorySourceKeys(): <=32 key copies from LAST DEMAND PLAN.
MandatoryResidentPatches / MandatoryResidentPayloadBytes show reserved footprint;
PriorityCapacityDeferredCells distinguishes urgent demand blocked by caps.
Existing SourceCoverageStatus adds mandatory count and priority-deferred count.
These are source diagnostics, not rendered-mesh or whole-view coverage metrics.

Coordinator can add these public arrays/fields to its own native diagnostic DTO.
Before this patch, exact old held-key/reason could only be observed through a
read-only main-thread reflection of private work list's public Key/Complete/Desired/
HoldReason fields. That inspection belongs to coordinator; this lane did not take
Unity control or claim to have performed it.

## Evidence-based32/32 photo allocation proposal (NOT PAYLOAD)

Default195x195 copied640m grid at5m with128blend+32query halo has exact912,600B
height+5climate payload.16extra patches add14,601,600B=13.925MiB, ~0.176% of the
observed8.28GB commit headroom, well inside that snapshot's2GiB reserve margin.
Existing constructor allows32patches/128MiB maxima; neither needs changing.

Recommendation: a controlled32patch/32MiB valley photo trial is justified by actual
payload and headroom. Bootstrap-32-source-photo.PROPOSAL.diff contains TWO runtime
assignments AFTER AddComponent and BEFORE Configure. It is a separate merge
proposal, not an edit/payload/default change. Calling these setters after Configure
would not change the field's immutable caps. Coordinator owns session choice,
guarded install/import and rollback; preserve saved scene/options/camera.

Native-coordinate replay with32/32 can select30cores plus the two mandatory
unselected residents =32footprints. Feasible core area becomes12.288square km vs
current14ready5.7344square km, not a full-world claim. All32 resident cores together
occupy13.1072square km plus blend halos. Source payload floor29,203,200B stays below
32MiB. Decode/copy time, transport cache, old immutable snapshots, managed overhead,
mesh/collision/VRAM changes and concurrent PC activity are additional/unmeasured;
12GB GPU capacity is not free VRAM. No near-instant/FPS/quality promise.

## Focused validation / remaining native gates

checks/20261027T213214053992Z/report.json and focused.log:55checks PASS, reusing44
scope plus11native-residency/mandatory-byte/priority/hold/allocation cases. No broad
renderer/network/concurrency suites. C#9 warnings-as-errors separate engine-free
Core/WorldScaleData compile; affected Live API compile uses official Unity6000.3.27f1
and current WorldScaleRuntime binary. .NET CPU replay only, no native calls/service.

Native: coordinator installs one guarded file, captures actual selected/held/
mandatory keys/reasons and verifies stationary requests settle; valid safety holds
must remain visible. If trial32 adopted, record actual source residency/payload,
selected/deferred extents, model completion/copy and commit during warmup, then
check source-triggered terrain/population refresh and render convergence. Camera
turn/movement/matter hold must retain protected/selected useful detail. Original
native14/16 is not full valley detail; fixed14/14 is only the achievable subset.
