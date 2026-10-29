# Terrain clipmap implementation checkpoint

Current stage: wrapper integration, CPU scheduling, GPU ring rendering and explicit
Exploration next-Play preview are implemented; combined background verification
is complete. User Play exposed live-climate roundoff and missing material texture
properties; both repairs are installed and the user confirms the preview now works
correctly. Quantitative native render/performance gates remain pending. A narrow
loading-only draw suppression passes background lifecycle/API checks; a later
combined user run confirms hidden draws are zero but is not a controlled speedup
comparison. The optional1.5x far-color window is now implemented and background
checked; native appearance/cost acceptance remains pending. Phase 1.2 background checks passed; implementation continues with
native gates batched later under the user's explicit authorization. Phase 1.1
native bake and separate verification gates passed. Phase 1.2 native comparison
remains unexecuted (the user stopped Computer Use before refresh/menu input).
Date: 2026-10-28 (Denver).

No Unity/Computer Use, screenshots, refresh or menu interaction is authorized
during this background implementation. Native import/render/performance gates
remain pending; production source/renderer defaults remain Legacy. The user explicitly authorized
an Exploration scene preview override for the next Play; old preferences cannot
suppress it. A saved explicit renderer choice and the Graphics rollback button
override the scene preview. This is a candidate, not accepted renderer adoption.
The user waived native gate ordering for implementation, not evidence requirements.

Ownership: `terrain_implementation` owns field wrapper, settings/bootstrap/options,
CPU scheduler/mirrors and WorldExplorer integration.
`terrain_gpu` owns new GPU resources, scatter compute, clipmap shader and draw
submission plus bounded vegetation/forest render-height resnapping. `terrain_plan_review` owns independent CPU checks and native probe
tools. Shared contracts use WorldScaleData `TerrainClipmapState`, `ClipmapRect`,
`ClipmapSample`, `ClipmapJob`, `ClipmapPublication`, `ClipmapLevelInfo` and
`TerrainClipmapMath`. Published windows are complete and immutable until upload
acknowledgment; N+1 CPU/GPU banks keep one staging candidate. A stationary world
anchor and complete adjacent parent chain provide fallback. Child rings fit the
parent's unmorphed interior; GPU draws four nonoverlapping ring rectangles.
Unsupported configurations exceeding the 16-level anchor bound reject explicitly.

Phase 0.1 corrections were already committed in `ce9c1ab`. Existing Unity
6000.3.27f1 was observed in Edit mode after the user stopped Play, refreshed,
and compiled without errors. No agent started or stopped Play.

Phase 0.2 preserves unrelated in-flight work and queued refinements across local
source publication and its subsequent ApplyPlan. Affected work includes the
normal/material sampling halo. Global revision fences remain for resets; contact
refresh obligations survive admission deferral and overlap replans, then clear
on successful publication or complete eviction. Queue dedupe is linear.

Evidence: `Docs/ClipmapEvidence/phase0-20261028/phase02-native-report.json` has
17 passing native Edit-mode checks, source hashes, and no errors. Official Unity
API compile, project integrity, and the native mesh regression menu also passed. The first native fixture report
claimed PASS while Unity logged Edit-mode Destroy(null) diagnostics: it is **not
an accepted gate**. Its report/log are preserved as phase02-first-*; production
null guards and fixture error capture fixed the clean rerun. The old standalone
ContactFreshness harness could not compile its stale Unity shim/source inventory
(UNorm16/property-block/VoxelStepTextureOwner); no test outcome is inferred from it.

Remaining concern: the held-iterator and synthetic contact sizes validate queue
lifecycle and admission, not real worker timing or visible settling. Optional
standing-still Play acceptance remains unexecuted; legacy stays the default.

## Review amendments to apply before their respective phase

- Keep Legacy as the default until user appearance acceptance; the Phase 3 code
  default contradicts Phase 6 and the project renderer adoption invariant.
- Phase 0.2 must include contact freshness and normal/material sample halos.
  `SourceRevision` currently tracks the global revision, so merely deleting the
  increment leaves contact surfaces stale. Keep a local dirty obligation through
  contact capacity deferral and replanning. Cancel/requeue only affected work.
- `ForceReplan` reaches `ApplyPlan`, which clears pending/refining. Preserve
  unaffected queued work when applying refreshed bounds/topology, or source
  publications will still cause queue churn one step later.
- Baked far identity must include sorted accepted patch identities/content,
  basis identity, grid and quantization plus payload. Validate finite coordinates,
  dimensions, lengths, decompression limits, CRC and each height SHA256. Do not
  edit the pinned overview-provider/bake scripts.
- Sparse far interpolation must weight heights by coverage. Uncovered zero
  height must not bias the edge toward sea level.
- Clipmap publication must reject stale generations and cannot join unrelated
  completed rectangles into a falsely valid region. Moving/teleporting requires
  a genuinely valid ancestor fallback and retained published coarse coverage;
  one-hop fallback to an unfilled parent is insufficient.
- Do not rely on NaN vertices as a topology proof. Test actual ring geometry and
  all depth/shadow passes, including invalid-data fallback, not just fully filled
  mathematical boundaries.
- T=512, s0=.2, N=8 covers about 6.5 km, not the 40 km test domain. At 400 m the
  needed level has about 1.6 m spacing, so the <=.8 m assumption is false. Measure
  height error rather than assuming the requested bound.
- High resources are roughly 49 MiB before overhead, not just a few MiB; drawing
  full grids can submit about 6.2 million triangles per pass. Prefer real rings and
  measure total owned memory and terrain-only timings.
- The height-only optimization's `min(L,3)` color lookup is reversed for fine
  levels if L0-L2 are intended to use L3 color. Define and test the mapping.

Native gates remain gates: API-only compilation and CPU fixtures do not establish
Unity shader import, native rendering, geometry holes, tree alignment or FPS.

## Phase 1.1 format decision (host approved after preflight)

Use new derived cache `AVFAR002`, rejecting unshipped `AVFAR001` with a rebake
message. No savegame migration is involved. The proposed int16 half-meter height
would silently clip supported elevations above 16,383.5 m; use int32 half-meter
units and reject out-of-range values instead. Current grid payload is 140,231,525
bytes (28,046,305 samples), before compression.

The little-endian gzip body contains magic, columns/rows, finite double origin,
spacing and height unit; a bounded UTF8 base identity; a 32-byte source fingerprint;
exact payload length; int32 heights then byte coverage; SHA256 and CRC32 both
covering header and payload. The computed runtime identity includes all header,
source and payload bytes. Source fingerprint includes full base identity, exact
input asset hashes and sorted selected patch coordinates, spacing and AVOV content
hashes. Equal-spacing conflicting patches are an error, not directory-order choice.

Limits before allocation: 2..16,384 columns/rows, <=32 million samples,
<=160 MiB decompressed bytes, <=16 KiB identity, supported finite origins/extents,
positive spacing/height unit. Exact read lengths, EOF, CRC and SHA reject truncation,
extra decoded bytes and corruption. Read requires a seekable FileStream or
MemoryStream; footer CRC and decoded length are checked independently because
Unity Mono's gzip decoder accepted a one-byte-truncated footer in the first native
fixture. That failed report is preserved, and no asset was published by that run.
AVOV input is capped at 256 KiB, JSON 64 KiB,
canonical aligned 40/80/160 m windows, checked native/query identity, finite supported
heights, CRC and height_sha256. Decode/cell selection is bounded and deterministic.

Bake defaults remain separate from runtime settings. The Editor reads imported
source assets, requires the supported imported dimensions and vertical scale 1,
and publishes only a verified temp result into the Samples path. Sampling uses
coverage-weighted heights at sparse edges. Verification separates grid-node
quantization (<=.25 m) from arbitrary-point resampling error (plan gate <=.6 m),
so a failed approximation is reported rather than hidden by sampling only nodes.

Preflight observation: about 11.1 GiB free physical memory, 3.6 GiB available commit,
and 208.8 GiB free disk before bake; cap estimated temporary array ownership below
700 MiB and limit parallel rows to four. The validated scan found 21,240 files:
7,080 each at 40/80/160 m, all valid; finest selection is 7,080 cells at 40 m, with
no equal-spacing conflicts. Observed selected heights are -6,012.965..10,721.936 m,
so this dataset fits int16. Int32 adds 53.5 MiB decoded/resident payload and avoids
restricting later valid 48,000 m sources; current data does not require it.
Selected patch arrays total 177.2 MiB. Bake and readback arrays total 267.5 MiB,
plus source atlases and bounded buffers. This is an estimate of owned arrays, not
a measured process peak. Detailed preflight is
`Docs/ClipmapEvidence/phase1-preflight-20261028.json`.

## Phase 1.1 native evidence

Existing Unity 6000.3.27f1 ran both menu commands in Edit mode. Bake completed in
47.007 seconds and separate Verify in 32.177 seconds. Both accepted 21,240 files
without rejects, selected 7,080 cells at 40 m, and passed 17 format/sparse checks.
Across 1,000 arbitrary land points, maximum height error was 0.231450 m; 1,000 grid
points had maximum quantization error 0.249939 m. Estimated temporary array
ownership was 623,247,702 bytes (594.4 MiB), not measured process peak. Immediately
before the accepted bake, free physical memory was 12,858,012 KiB and available
commit was 3,416,908 KiB.

The published `ExplorationFar40.bytes` is 52,223,271 compressed bytes with identity
`baked-far-v2/e3578d39a5ee0f1a15e024e505495acb45120d5220f5bec79a0b9a0c27204c1f`.
Both reports record matching reader/baker source hashes, input asset hashes,
selected-content identity and the complete AVOV manifest hash. Sources are checked
again at completion and before atomic publication. Reports are in
`Docs/ClipmapEvidence/phase1-20261028/bake-report.json` and `verify-report.json`.

Initial compile ambiguity, gzip-footer acceptance, and a fixture that did not
classify Mono's IOException for deliberately corrupted gzip are preserved in the
same evidence directory. These are failed earlier revisions, not passing gates.
The final fixture rejects that corruption and truncated footer explicitly.

The Phase 1.1 evidence above covers cache format/comparison only. Later runtime
work does not retroactively extend that native gate.


## Current background implementation and remaining gates

`BakedOverviewField` pairs the full original atlas identity with the far cache,
keeps immutable live fine snapshots, and uses coverage-weighted sparse blending.
Its shared suite passes 27 checks. The native 10,000-point comparison remains
unexecuted. The wrapper loads asynchronously before source adoption; corrupt,
missing or mismatched caches retain the legacy source and renderer.

`TerrainClipmapState` publishes complete windows only after GPU submission,
retains disjoint local invalidation obligations and rejects stale generations.
Independent checks report 1.708 million assertions across negative coordinates,
scrolls/teleports, out-of-order/faulted jobs, upload acknowledgment, ring coverage,
world-corner ancestor containment and barycentric rendered-height parity.
`TerrainClipmapResources` uses N+1 texture slices and procedural nonoverlapping
rings. High payload fits 64 MiB; Ultra is explicitly larger. Fifteen offline
D3D11/SM5 shader variants compile against the installed URP includes; this is
not Unity shader import, D3D12 rendering, shadows/depth or frame-time evidence.

Runtime workers use immutable captured surface samplers, at most eight workers
and 300,000 samples including stale tasks. One GPU transaction uploads within a
sample and submission-time budget. Initial spawn waits for fine local coverage;
later movement can use genuinely published coarse anchor coverage while detail
catches up. The scene and imported settings asset `PreviewClipmapOnNextPlay` flags select
High/.2m explicitly, including an already loaded scene;
production settings stay Legacy. Graphics offers a saved Legacy/source rollback.
Runtime support/allocation/sampling failure restores Legacy terrain with status.
Far vegetation resnaps only its render matrices in bounded work; source positions
and gameplay collision remain separate.

A bounded actual-source benchmark (far + reshape + relief + material sampler)
measured 3.26 us fine, 22.88 us middle and 18.24 us far samples under .NET. The
first representative estimate is 50.6 single-thread CPU seconds for High startup,
not observed Unity wall time or parallel scaling. A four-quantile snow array was
allocating per material evaluation; it is now one private static array with the
same values/order. The unchanged sampled checksum was -21368870.438541174; the post-fix benchmark
measured zero per-sample allocation. A refined 100,000-sample pass distributed
across all 15 actual startup windows estimates 38.372 single-thread CPU seconds
for 3,932,160 samples. Field load took 2.47 seconds and retained about 238 MiB
managed data in that process. These are .NET observations, not Unity startup
time, renderer process memory or frame timing. No material
quality or appearance taps were removed.

Background evidence is saved under `Docs/ClipmapEvidence/preview-background/`.
All six runtime assemblies and the runtime UNITY_EDITOR branch compile against
the installed Unity API; the shared state/surface suite passed 1,708,439 assertions
and the wrapper passed 27 synthetic checks. Project integrity passed 296 assets
and 13 assemblies. Final Editor-assembly compile found an ambiguous Random name
in the new fixture. The alias repair passed the full Editor-assembly compile;
the failed report/log remain preserved. CPU production code is unchanged from
the passing assertion run (only the fixture's Editor name resolution changed).
The draw rejection uses actual far-frustum corners with a margin, not far-plane
distance as a radial threshold. Full-frame/native performance remains unknown. Native wrapper gate,
mesh regression, render/depth/shadow/normal/edit/teleport tests, vegetation parity,
startup memory and frame-time acceptance remain pending. The user retains Unity
control and plans to do the final visual check by pressing Play.

Runtime first-fine-coverage timing and payload counters are logged automatically
when the user presses Play. The HUD labels this as preview with visual acceptance
pending; Escape → Graphics has a saved Legacy/source rollback for the next Play.
The shared Bootstrap file retains the user's prior GUI edits and is intentionally
not included in the implementer's scoped commit.

Final source-matched API reports: `preview-api-background.json` (six runtime
assemblies) and `preview-editor-api-background.json` (runtime Editor branch and
full Exploration.Editor assembly), both PASS. GPU/resnap commit `95bc28d` and
validation commit `e359512` own their respective files. The final preview source
manifest is `preview-background/preview-manifest.json`.

## User Play fallback and repair

The user observed a standing-still fallback after fine coverage. A manual retry
with nested exception diagnostics identified live climate convex-blend roundoff
violating the strict SurfaceClimate bounds. The production-path red fixture
reproduced the same exception; the bounded roundoff repair passes31 targeted
checks, retains exact interior values/input rejection/temperature policy, and
passes the27 wrapper checks plus real Unity API compilation. Full evidence is
in `Docs/ClipmapEvidence/fallback-20261029/`. The subsequent user Play reached
current detail ready but was visually rejected: rapid noisy brown/gray/white/black
changes, near-flat ground and floating trees. The host observed the current view
with separately granted read-only permission; no controls were driven.

The attachment path copied the entire Legacy material after the clipmap had
bound its height/tint/appearance arrays. This is an unsafe ownership/order hazard
consistent with missing height data; native property-sheet loss has not yet been
observed directly. Attachment now copies only the four shared style floats,
restores/checks the exact owned texture references, and validates before publishing
the replacement or clearing Legacy. No palette, sampler or shader-quality change.
The original attachment fails the modeled material-boundary regression; the fixed
path passes13 checks, including failure leaving the replacement unattached. This
model does not prove native CopyPropertiesFromMaterial behavior or rendered pixels.
Six-assembly API compilation passes; a final runtime-assembly check covers the
atomic attach order. Evidence is `Docs/ClipmapEvidence/material-binding-20261029/`.
The binding-only repair was rejected by the next native loading attempt:
Material.GetTexture reported that _CmHeights was not a texture property. The
shader declared its arrays in HLSL but omitted their ShaderLab Properties entries.
The preserved log is material-binding-20261029/shader-schema-native-failure.log.
All three arrays now have explicit hidden NoScaleOffset 2DArray properties using
the installed URP17 Lit.shader syntax. The stronger regression reads the actual
Properties block, rejects the old shader and old broad-copy attachment separately,
and passes16 checks with the repair. Its shim no longer accepts undeclared texture
reads. The following user run passed binding validation, reached fine coverage
in43.330s (4,579,871 accepted samples), and the user confirms the preview now works
correctly. This is bounded user appearance approval, not quantitative FPS, depth,
shadow or all-scenario acceptance. The earlier modeled PASS did not establish
the native material schema and is preserved.


## Loading-screen rendering and timing

The completed investigation found frame-dependent worker refill and atomic upload
gaps, plus hidden terrain submission under the full-screen alpha1 loader. Eight
8192-sample jobs occupy only65,536 worker samples; the single spare then uploads a
full512² candidate across at least four65,536-sample passes. The2ms upload clock
also includes prior harvest work. Main progress reads the live counters directly,
so the quarter-second diagnostic poll does not explain the reported two-second
pauses. The observed43.330s run accepted16.47% more samples than nominal startup
from source updates; this does not include rejected work. No native CPU/GPU split
was measured and no new full-world sampling sweep was run.

Bootstrap now explicitly suppresses clipmap draws only while its opaque loading
screen owns the view. Sampling, uploads, publication, visible-chain refresh and
height/readiness queries continue. Draw counters reset to zero. Immediately before
Ready, the gate opens for a covered submitted frame; the ordinary-frame wait is
bounded to two frames and does not depend on Game view focus or claim GPU completion.
Disable, teardown and retry release the gate. A failed covered draw keeps loading
until the restored Legacy renderer has both local and distant coverage. Gameplay source refresh never hides
the published terrain. Sample precision, appearance, ring topology and work budgets
are unchanged. Aggregate worker-running/queue/finished-to-harvest durations, upload
frames/submission time, stale samples and frame intervals now accompany automatic
first-fine and loader-handoff logs. Parallel worker duration sums are not CPU time.

Evidence: `Docs/ClipmapEvidence/loading-investigation-20261029.json` records the
original code/log analysis; `loading-20261029/lifecycle.json` passes20 checks of
the actual runtime and extracted production handoff with a modeled graphics
boundary and tiny flat-field windows. Six real Unity API assemblies pass in
`loading-20261029/unity-api.json`. No Unity interaction was performed. Native
startup improvement and warm-handoff appearance remain unmeasured; no seconds
saved are claimed. The shared Bootstrap hookup remains unstaged with existing
user GUI edits, as before.


## Optional far appearance cache

User-authorized modest far-detail upgrade: one independent world-anchored square
at1.5x the chosen geometry ring's linear sampling density (Fast384², High768²).
It stores tint and appearance only; height, collision, topology, fine levels and
initial readiness are unchanged. Target selection intersects actual ring
rectangles with the world domain and the camera far-frustum enclosing sphere,
excluding empty anchor rings. It is independent of camera heading, polls twice
a second, and waits2s before switching selected level. The full chosen ring fits
the cache; shader edges fade into the existing footprint-filtered appearance.

Bootstrap enables the lane only after Ready, including after the covered loader
handoff. The primary scheduler/upload wins admission. At most one2048-sample
worker remains counted until finished, including cancelled work; uploads are
limited to8192 texels with a .5ms pre-dispatch budget check. Neither is a measured
millisecond guarantee. Two CPU mirrors and two GPU texture banks each add9MiB
at High (2.25MiB at Fast), plus one bounded16KiB result and small metadata. GPU
upload scratch is reused. High default GPU payload becomes61MiB;16geometry
levels reach64MiB. Ultra keeps the base appearance: its1536² enhancement would
add36MiB per side, outside the approved scope. The original32MiB High color-only
target is exceeded by this explicitly approved9MiB texture addition.

Same-lattice scrolls reuse overlap. Local edits expand by32+3*min(spacing,512)m
for current nested appearance taps, retain unaffected completed/running work,
and requeue only affected regions. Publication generations reject stale GPU
uploads; global profile changes resample all. Live snapshot identity changes
alone do not restart the full window. Once the complete replacement is uploaded,
the shader fades the old enhancement to base color, swaps banks at zero weight,
and fades the new cache in. Optional failures release only this enhancement;
base clipmap geometry/colors survive. Escape/F1 Graphics reports target, remaining
work and active/upload state; payload counters include these allocations.

Focused evidence: `far-appearance-cpu-background.json` passes5,904,428 engine-free
assertions, including local-edit forward progress, publication-region coverage,
negative non-power-of-two addressing, stale rejection, ring/domain/corner targets
and two-mirror bounds. `far-appearance-20261029/runtime-lifecycle.json` passes23
actual-runtime checks with a modeled graphics boundary, including post-loader
eligibility and optional failure isolation. `binding-integration.json` passes39
resource/ownership checks with historical red cases; GPU shader/fade checks are
in the same evidence folder. Six real Unity API assemblies and project integrity
(305assets,13assemblies) pass. There was no Unity interaction or native far-color
quality/performance measurement.

GPU changes are scoped in08824bb. Shared TerrainClipmap.cs and Bootstrap hooks
remain installed but unstaged because they also contain independent user/external
startup, disk-cache, horizon and GUI work. That work was preserved and included
in the compile context, not claimed as authored or natively accepted here. The
later native loader observation is preserved in loading-20261029/later-native-*
with imported-source uncertainty; it does not establish an A/B improvement.
