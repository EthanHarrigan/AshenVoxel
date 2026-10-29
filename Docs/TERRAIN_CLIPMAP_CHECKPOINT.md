# Terrain clipmap implementation checkpoint

Current stage: wrapper integration, CPU scheduling, GPU ring rendering and explicit
Exploration next-Play preview are implemented; combined background verification
is complete. The user's first Plays reached fine coverage but then failed on live-climate
blend roundoff. The narrow repair passes background regressions and is ready for
a manual retry; native acceptance remains pending. Phase 1.2 background checks passed; implementation continues with
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
in `Docs/ClipmapEvidence/fallback-20261029/`. The fixed native retry is pending;
this is not a performance, visual or sustained-operation acceptance. No Unity
control was used by agents.
