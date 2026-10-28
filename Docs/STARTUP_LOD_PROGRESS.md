# Startup throughput, forest coverage and progressive terrain

2026-10-27. `startup-lod-1` is installed in the main project and compiled by the
main Unity Editor. It preserves patch6 wind/material work. `aerial-lod-1` is
also installed and native-checked, correcting tree-angle transitions and pitched
terrain visibility as described below. This is a local source update;
the earlier downloadable release and earlier test identities are unchanged.

## What changed and why

The expensive terrain builder already has the measured unboxed sorting and
two-worker sampling optimization described in
[WORLDGEN_PERFORMANCE_RESULTS.md](WORLDGEN_PERFORMANCE_RESULTS.md). The next
large opportunity was scheduling and publication, rather than adding threads.

1. Prototype loading previously advanced only one of 412 mesh-LOD yields each
   frame. It now advances up to eight, stopping between meshes when its existing
   time budget is used. This removes an artificial startup floor. An individual
   mesh remains atomic, so this is **not a hard 2 ms limit**.
2. Vegetation previously generated two cells, stopped generation, rebuilt the
   entire instance set, published it, then repeated. The captured instance
   snapshot now advances first while new cells can use the remaining budget.
   Existing publication/rebase/readiness checks remain intact.
3. Near detailed vegetation could consume all 32 million triangles, leaving
   distant trees with no representation even though they were generated. The
   allocator now reserves each captured entry's actual cheapest LOD before
   spending on detail. This includes collidable rocks. Coverage is guaranteed
   when the sum of minimum representations fits the budget. The original near
   hard-prop exception is retained: an impossible minimum-cost configuration can
   still exceed the nominal budget or omit distant entries.
4. Matrix arrays grow only to the capacity needed by their existing pooled
   batch. Previously every batch allocated 1,023 matrices, including tiny batches.
5. Newly keyed terrain first builds eight-column coverage. Once the new plan
   has complete coverage it publishes, then the existing worker/queue refines
   visible terrain through 32, 64, 128 and up to 256 columns. A turn releases
   unnecessary rear detail and prioritizes the new view. Pending contact tiles
   within 32 m remain urgent; afterward two outer contact builds allow one far
   build. The 100 m contact target remains 10 cm.

No additional runtime worker, persistent cache, renderer, package, public quality
knob, voxel object or terrain source was added. Source resolution remains
1280 m learned macro / 5 m decoded patches; mesh sampling and 10 cm matter are
separate. Refining a mesh cannot invent missing learned terrain features.

## Same-world vegetation comparison

Actual imported world `14fb32`, seed 714103, spawn X=-19065/Z=-10200, unchanged
99-model pack and placements. These are Windows .NET runs of actual generation
and scheduling code with numerical rendering substitutes. They are **not native
FPS or elapsed Play-load measurements**. One matched run per final variant:

| Measurement | Before | Retained candidate |
|---|---:|---:|
| Resident cells / instances | 1,715 / 22,469 | 1,715 / 22,469 |
| Generated trees | 11,734 | 11,734 |
| Submitted trees within eligible range | 405 | 11,296 |
| Scheduling iterations until settled | 26,579 | 1,033 |
| Prototype-ready iteration | 413 | 70 |
| Near-ready iteration | 521 | 111 |
| Complete instance rebuilds | 887 | 34 |
| Instance visits during rebuilds | 14,464,906 | 405,002 |
| Matrix array payload | 109.77 MiB | 14.98 MiB |
| Total triangles submitted | 32,000,000 | 31,995,100 |
| CPU execution wall time | 4.00 s | 1.75 s |

This is 25.7 times fewer scheduling iterations, 97.2% fewer instance visits,
86.3% less matrix payload and about 2.28 times faster execution in this small
CPU harness. At a hypothetical fixed 60 ticks/s the scheduling counts correspond
to 443 versus 17.2 seconds; these are arithmetic illustrations, not measured
native startup times. Generating at most two cells per tick still limits this
fixture to at most 120 cells/s at 60 Hz before other budgets apply.

More useful work per tick raises the observed harness p95 tick from 0.57 to
3.60 ms; the cold prototype peak remains roughly 240–246 ms. Faster publication
does not prove smoother frames. Native frame-time and atomic-upload profiling
remain required. The matrix pool can retain larger arrays after other scenes;
14.98 MiB is this fixture's observation, not a global memory cap.

An independent dense-forest regression compares the exact 25,439 eligible tree
placement tuples against submitted matrices: no missing or duplicate tree,
within the same triangle limit. A separate near-rock case protects against
invisible collidable props when detail allocation is exhausted.

## Native terrain sections per second

Unity 6000.3.27f1 Mono, CPU-only, one actual-world tile at a time, warm-up plus
three timed repeats; table uses medians. It measures sampling and face building,
**excluding vertex expansion, GPU upload, frame pacing and other game work**.
Sections are dyadic tiles, not Minecraft chunks: LOD0 is 25.6 m wide and each
level doubles the width. The 16-cell matter chunk is separately 1.6 m wide.

| LOD | Section width | Mesh spacing | CPU ms/section | CPU sections/s | Mesh payload |
|---:|---:|---:|---:|---:|---:|
| 0 | 25.6 m | 0.1 m | 282.0 | 3.5 | 8.51 MiB |
| 1 | 51.2 m | 0.4 m | 94.7 | 10.6 | 5.09 MiB |
| 2 | 102.4 m | 0.8 m | 122.8 | 8.1 | 7.94 MiB |
| 3 | 204.8 m | 1.6 m | 131.0 | 7.6 | 8.04 MiB |
| 4 | 409.6 m | 3.2 m | 156.7 | 6.4 | 11.91 MiB |
| 5 | 819.2 m | 6.4 m | 152.3 | 6.6 | 10.29 MiB |
| 6 | 1638.4 m | 12.8 m | 138.1 | 7.2 | 10.01 MiB |
| 7 | 3276.8 m | 25.6 m | 181.2 | 5.5 | 13.64 MiB |
| 8 | 6553.6 m | 51.2 m | 158.0 | 6.3 | 11.64 MiB |
| 9 | 13107.2 m | 102.4 m | 151.5 | 6.6 | 13.12 MiB |
| 10 | 26214.4 m | 204.8 m | 148.1 | 6.8 | 11.74 MiB |
| 11 | 52428.8 m | 409.6 m | 88.6 | 11.3 | 6.57 MiB |

Large root sections can cover terrain outside the visible 30 km radius. The raw
report also includes LOD12/13, mostly outside-map/ocean roots; their faster rates
would be misleading as typical visible mountain throughput. The chosen tiles
are samples, not averages over every section in a level.

Eight-column coarse tiles usually took approximately 0.3–1 ms in this run.
That does not mean a thousand sections appear per second: the runtime retains
one upload per frame, asynchronous yield boundaries and all normal frame work.
At 60 Hz one upload/frame alone caps publication at 60 sections/s, before the
worker and additional yield constraints.

Increasing a tile from 128 to 256 columns halves its horizontal step but costs
roughly 3–4 times the CPU and mesh payload in these samples. For example, LOD3
changes from 1.6 m / 131 ms / 8.04 MiB to 0.8 m / 519 ms / 28.19 MiB. LOD7 changes
from 25.6 m / 181 ms / 13.64 MiB to 12.8 m / 681 ms / 48.21 MiB. This is why the
new policy improves selected visible tiles progressively rather than raising
every tile's resolution immediately.

## Progressive quality and its limits

A small four-tile travel test published replacement coarse coverage in eight
cooperative ticks / about 121 ms, versus 25 ticks / 202 ms for the earlier
baseline. These are different publication quality points: the new policy
continues refining afterward. It is not a claim that maximum quality finishes
faster. Static settled terrain stops rebuilding; failed/capacity-limited work
stays observable, with backoff rather than an endless idle retry loop.

Far mesh accounting is bounded to 1.978 GiB steady and 2.456 GiB during a plan
replacement, plus one at-most-120 MiB uncommitted mesh. These limits include a
proven worst-case coarse reserve per tile and a **total**, not additive-to-old,
1.5 GiB detail allowance. Existing contact meshes have their separate 1 GiB
default budget. Unity/driver overhead, sources, worker data, vegetation and
upload scratch are additional. This is not a process commit or VRAM guarantee.
Eight interrupted replans confirmed only active plus latest desired coverage
is retained, and superseded partial meshes are destroyed.

Capacity can prevent tiles meeting the pixel target. The 256-column ceiling and
bounded quadtree also remain. This update therefore does not yet establish the
requested seamless high-setting Distant Horizons appearance. The initial
512-tile plan already reports 147 pending splits. Neighboring mountain detail
must be evaluated in the actual aerial view after refinement, including pitched
camera classification, silhouette quality, seams, popping and budget deferrals.

## Aerial tree issue reported during this work

The supplied screenshot predates `startup-lod-1`. Both tree shaders previously
switched from side-view atlas images to a true overhead image at direction.y >
0.72, roughly46 degrees elevation, while changing quad height. This caused the
abrupt canopy-stamp boundary in an oblique aerial view. `aerial-lod-1` now keeps
side imagery through oblique views, foreshortens its projected height, and
transitions toward overhead imagery over76–88 degrees. Both forest paths share
the same sampling, coverage, decoded material and normal logic. Geometry/draw
counts and atlas assets are unchanged; only the narrow transition samples two
views. Ordinary angles retain single-view sampling.

This is an approximation: the existing atlas lacks true oblique crown depth.
The near-overhead transition uses stable atlas-anchored stippling, visible in
large isolated test captures. It avoids a second whole-canopy alpha-threshold
pop, but does not establish seamless geometric parallax or temporal quality.
Actual native captures at46/60 degrees retain recognizable side silhouettes.

The terrain classifier also ignored pitch/roll when estimating visible heading.
At a60-degree downward pitch, an actually visible corner tile could be treated
as rear terrain and held at32 columns. The replacement bounds the horizontal
projection of the actual camera ray rectangle, preserving the existing near
and20-degree guards. It adds no cache or allocations. Across356,400 ray/tile
cases in1,188 camera configurations, the candidate misses zero visible cases
versus35,124 legacy misses.2,592 level-camera comparisons remain identical and
36 rear-view cases retain their lower-detail behavior. This fixes eligibility;
it does not remove the tile-count,256-column or memory limits.

Native Unity6000.3.27f1 / DX12 / RTX3080Ti: four actual prototypes at16 elevations
(64 views) passed both instanced and indirect GPU rendering. Their silhouettes
differed by at most one pixel; depth/normal coverage differed by zero pixels.
All six forest passes compiled. Parent inspected the native46/60/78/84/90-degree
views. Main Editor then imported/recompiled the four aerial files, followed by
its native mesh regression:32,198 independent face/color checks and128,416 AO
values across12 worlds. [Exact aerial hashes and evidence](StartupThroughputEvidence/2026-10-27/aerial/manifest.json)
are separate from the earlier startup revision. This was a bounded correctness
fixture, not a full-world FPS benchmark or final landscape art acceptance.

## Capacity-release correction — validated, not installed

The subsequent stopped-pose audit reproduced three defects in the installed
scheduler: a request could remain deferred after old coverage retired; a cheaper
replacement could free capacity without waking another tile; and travel could
retain visible detail above its newly requested resolution. A source replacement
or an increased contact allowance exposed the analogous contact retry gap.

The candidate records required bytes in the existing deferred entries and wakes
eligible requests only after a **net** payload decrease, or an effective contact
budget increase. It uses the existing queues and still checks admission for
every result. Same-size replacements do not wake requests during their temporary
release-before-add step; competing requests that still cannot fit defer again
without a busy retry loop. Surplus visible detail falls only to its current
pixel target. Memory admission and the geometric per-tile face limit now have
distinct counters. No cap, worker, cache or renderer change is involved.

[Source-matched capacity evidence](StartupThroughputEvidence/2026-10-27/capacity/reconciled.json)
passes19 real-generation far checks,10 contact accounting checks,30 progressive
checks,19 existing lifecycle checks and five real Unity API assemblies. The far
fixture lowers its private allowance to10MiB solely to exercise rejection with
small allocations; the installable candidate retains1536MiB. Contact admission
tests use synthetic byte metadata rather than allocating huge native buffers.
The baseline failures and test-only transformation are preserved separately.

As of2026-10-27 09:22UTC this candidate is **not installed**. The coordinator's
Unity maintenance/installation hold remains in force. No new native rendering,
full-route convergence or FPS claim is attached to these CPU checks.

**Update~10:01UTC: capacity-lod-1 is installed.** The replacement coordinator
explicitly released the hold for this frozen patch. Three exact source hashes
and readable backups were verified before and after the idle-only application.
Main Unity compile/reload and native mesh regression passed:32,198 faces/RGB
and128,416 AO values across12 worlds. No new Editor process, Play, budget change,
world generation or atlas/prototype regeneration occurred. [Installed identity
and logs](StartupThroughputEvidence/2026-10-27/capacity/install/installed.json)
remain separate from the earlier CPU reports. Actual flight/FPS acceptance is open.

## Controlled32M versus40M vegetation budget preflight

Three alternating CPU-only repetitions per budget use the frozen Version1
population and identical actual-world settings; a separate audit pair hashes
all22,469 placements and confirms exact identity. Frustum/GPU rendering is
substituted, so these are published batch totals, not actual on-screen triangles.

| Measurement | 32M budget | 40M budget |
|---|---:|---:|
| Trees represented | 11,296 | 11,296 |
| Trees using 3D mesh LOD0–2 | 388 | 606 |
| Highest-detail trees | 136 | 136 |
| Published batch triangles | 31,995,100 | 39,993,630 |
| Shared prototype mesh payload | 416.49MiB | 416.49MiB |
| Matrix payload across observations | 14.10–14.63MiB | 14.05–14.61MiB |
| Median CPU execution | 1.594s | 1.565s |
| Median scheduling iterations | 1,007 | 1,025 |

The higher allowance promotes218 additional trees to3D meshes (+56.2% at this
pose), with25.0% more batch triangles. It does not add trees, change placements,
increase shared prototype storage, or show a convincing generation-speed gain.
Matrix differences reflect reuse histories, not a reliable budget-dependent
memory benefit. Whether the promoted trees improve the visible image enough to
justify rendering cost requires the native comparison. The default remains32M;
this preflight does not establish32M as optimal or rule out40M.

[Raw comparison and source hashes](StartupThroughputEvidence/2026-10-27/budget-cpu/comparison.json)
remain distinct from the upcoming Version2 grounding revision. The staged
native vegetation-only fixture compiles against installed Unity APIs and has
separate32M/40M entrypoints, identical fixed camera/settings, cold and warm
publication measurements, LOD/visible/shadow counts and process-memory guards.
It renders no terrain or distant stand horizon. Camera.Render CPU elapsed and
readback wait are explicitly not GPU timer values or full-game FPS. Execution
is pending coordinator release and a single validated source freeze from the
grounding owner. No native budget measurement or budget default change occurred.

## Repeated travel and high-altitude priority experiment

The installed aerial revision and the separate capacity candidate passed an
independent regression through actual `Update`, asynchronous planning, worker
cancellation and publication. Each performs three10km normal outbound flights,
stops,180-degree turns,10km boosted returns and ground arrivals. The middle
route flies1000m above a flat immutable field. All stationary stops converged;
contact remained10cm, previous complete coverage stayed available, at most one
geometry worker and one upload per tick were observed, and shutdown disposed
the owned caches. Neither test installed code or launched Unity.

This intentionally bounded fixture uses64 planned tiles,4km view distance,
16m contact radius and256MiB contact admission, not the user's full-world
settings. Both observed at most100 resident far tiles,21 contact tiles,
310.30MiB far mesh payload,22.25MiB contact payload and8MiB upload scratch.
Payload accounting excludes Unity/driver overhead. Virtual route timestamps
advance100ms per tick while workers run on real CPU time; the resulting roughly
9000 scheduling ticks and elapsed durations are not native travel latency/FPS.

The altitude audit found a separate priority problem: contact requests use the
global supported height interval, so nine tiles below the camera still counted
as urgent within32m at both100m and1000m above flat ground. A separate minimal
candidate reuses the conservative height enclosure already produced by the
planner. Unknown, missing or stale bounds retain the original urgent behavior.
It leaves all contact requests, their10cm resolution, the2:1 outer-contact/far
fairness and every memory allowance intact. Descending changes priority
immediately without waiting for a new contact plan.

| Focused four-tile experiment | Installed policy | Altitude candidate |
|---|---:|---:|
| Contact tiles before first far refinement, ground level | 9 | 9 |
| Contact tiles before first far refinement,100m above ground | 9 | 2 |
| Contact tiles before first far refinement,1000m above ground | 9 | 2 |
| Contact requests retained initially | 98 | 98 |

The candidate advances distant quality by seven contact builds in this case;
it does not remove those builds or establish a whole-generation speedup.
The single CPU observations were roughly450ms versus110–119ms to first far
improvement at altitude, but are not a native timing estimate. Its tradeoff is
later completion of some ground detail well below the camera. Descent priority
and conservative fallback protect arrival; actual mountainous flight and native
frame/render cost still require validation. Ten thousand warmed urgency scans
made no field queries or per-call allocations, taking20.8ms total in this
fixture. The only new persistent scheduling state is one revision integer.

The isolated candidate passes18 focused altitude checks,30 progressive checks,
the repeated-route regression and five official Unity API assembly compiles.
All three route variants retain separate source/fixture identities in the
[travel/altitude evidence](StartupThroughputEvidence/2026-10-27/travel-altitude/summary.json).
The altitude route adds an active-key completeness assertion and retains the
captured plan during its priority audit; do not treat the earlier fixture hashes
as identical or compare route tick totals as a speed benchmark.
It is **not installed** and does not replace
the installed capacity revision. Its native validation awaits a coordinated slot;
the earlier blanket maintenance hold has been released specifically for capacity.

## Verification and reproduction

[Evidence](StartupThroughputEvidence/2026-10-27/manifest.json) freezes before/after
sources, actual measurements and exact hashes. Combined retained source passed
30 focused progressive checks, 19 existing lifecycle checks, five installed
Unity API assembly compiles, 31 bootstrap and 31 streaming checks plus the exact
coverage/hard-prop regressions. Project integrity passes 178 assets. The main
Editor compiled and reloaded the four changed runtime files without compiler
errors. No new terrain geometry/source version or Core matter change occurred.

Tools: `Tools/StartupThroughput/prepare.py` freezes/replays the bounded CPU
variants; `NativeLodProbe.cs` and `prepare_native.py` reproduce the small native
builder probe. `Tools/ProgressiveLod/run_checks.py --production` tests installed
scheduling; `--existing` runs lifecycle checks and `--api` uses real Unity APIs.
`Tools/run_exploration_streaming_checks.py` includes the coverage regression.
The GPU quad experiment remains unadopted for the reasons in the earlier report.

Still open: same-route native Start timing, per-LOD completed/publication counts
over real seconds, frame-time percentiles, commit/VRAM peaks, prolonged travel,
and visual acceptance of the user's aerial view. Do not label the earlier
native builder run or shader runs as validating these later runtime changes.


## Contact source freshness — installed 2026-10-27 ~10:21 UTC

A source edit could leave an old contact surface visible after capacity denied its
replacement. Moving within overlapping tiles or changing the radius then cleared
deferred bookkeeping and mistook the existing surface for current data. The fix
adds one source-revision integer to the existing surface cache, stamps it only
when its originating work is admitted, and queues stale surfaces on replans.
The fallback remains visible until capacity permits a replacement. No queue,
worker, cap, geometry or 10 cm resolution change was added.

The exact two-file idle installation passed main Unity compile/reload and native
mesh regression (32,198 face/RGB and 128,416 AO comparisons, 12 worlds).
[Installation identity and rollback evidence](StartupThroughputEvidence/2026-10-27/contact-freshness/install/installed.json)
are separate from the earlier capacity installation. Focused checks cover denied
replacement, overlapping movement, radius change, away/back, repeated source edits,
capacity release and real worker cancellation. The latest installed-source run
has 34 assertions, including 10 repeated publication-cap ticks; it directly checks
published source stamps against work/current revision. This is not 34 distinct
scenarios. Actual mountainous source-edit Play and frame-time validation remain open.

## Prototype startup experiments — CPU only, not installed

Three fresh processes per retained variant read the actual 99-record/412-LOD
12,016,333-byte AVPM4 pack. Full geometry and interaction digests match for all
records. Fresh-process measurements exclude pack-file I/O/reader construction;
no operating-system disk cache was flushed. Warm rows summarize repeated decodes.

| Measured stage | Installed reader | Isolated candidate | Difference |
| --- | ---: | ---: | ---: |
| First-process decode median | 510.34 ms | 437.28 ms | 14.3% less time |
| Warm decode median | 368.17 ms | 276.52 ms | 24.9% less time |
| Cumulative decode allocation | 260.83 MiB | 260.83 MiB | unchanged |
| Worst observed warm record | 130.76 ms | 96.54 ms | still an atomic hitch |

The candidate writes quad corners directly and uses the same IEEE CRC32 with
slicing-by-eight tables: approximately 7 KiB additional fixed tables, no package,
worker, format or cache change. Checks include 144 population assertions, 6,635
independent decoder assertions and five actual Unity API assemblies. Native
Mono/Unity timings remain unknown. The realistic measured first-stage saving is
about 73 ms, not a major world-generation multiplier.

A separate CPU buffer experiment packs axis normals into four signed bytes,
reducing each vertex from 28 to 20 bytes with identical decoded positions,
normals, colors and AO. For the current uploaded LOD set, arithmetic mesh payload
falls from 416.49 to 318.49 MiB (98 MiB, 23.5%), and warm CPU conversion median
from 93.59 to 65.47 ms. This does not measure actual RAM/VRAM residency or GPU
upload time. No production vertex-layout implementation exists yet; Unity Mesh,
shader, wind, depth, shadow and bounds comparisons are required before adoption.
The first branch-heavy packing attempt was slower and was rejected.

Bounds-only and compressed-copy-elimination variants gave no convincing CPU
speedup. Billboard LOD3/4 decode combined was under 1 ms, so lazy skipping adds
state for little gain. A fully resumable or coarse-first decoder would require
more changes because per-record compression/CRC, collision and grounding consume
shared data; it has not earned that complexity yet.

[Raw measurements, rejected variants and source snapshots](StartupThroughputEvidence/2026-10-27/prototype-decode/summary.json)
keep CPU results distinct from native Start/FPS claims. The 32M/40M vegetation
comparison remains a separate native gate using installed population v1; the
failed grounding-v2 candidate is excluded.

## Native vegetation budget preflight — no run

The installed-main snapshot passed exact source and fixture API checks. The
coordinator accepted one bounded Run32 with a 3 GiB own-process-family cap,
1.5 GiB reserve and 0.5 GiB overshoot allowance, conditional on at least 5 GiB
available system commit and an empty-Editor baseline at most 1 GiB. These are
constrained feasibility assumptions, not demonstrated native peaks. The launcher
checks process identities, source hashes, exit status and native report status.

At 10:35 UTC the initial gate found only 4.32 GiB commit headroom and exited
before launching Unity. No Run32/40 timing or visual comparison was obtained;
no cap was raised, user process changed or automatic retry performed. Run40
requires a separate decision after a successful, fully reclaimed Run32.
[Preserved preflight and exact fixture identity](StartupThroughputEvidence/2026-10-27/budget-native-preflight/result.json).

## Material anti-banding control — no significant performance gain

This isolated comparison uses the coordinator's frozen anti-banding control,
which replaces the exact 54 m material cycle with slow projected noise and a
private profile-version bump. The color function is unchanged. This is not the
proposed final alpine geology, and it has not been installed. Main sampler,
profile, world data and settings remained unchanged.

Sixteen actual14fb32 cases cover eight initial 8-column tiles and eight refined
tiles. Each source had one warmup and three measured builds per case, one tile
at a time with the existing two sampling workers. The table gives .NET CPU
builder measurements only; it excludes upload, scheduling and rendering.

| Tile sample | Level | Columns | Horizontal cell (m) | Faces, baseline → control | Median build ms, baseline → control |
| --- | ---: | ---: | ---: | ---: | ---: |
| spawn-10cm | 0 | 256 | 0.10 | 74322 → 74322 | 79.90 → 78.86 |
| spawn-east-10cm | 0 | 256 | 0.10 | 72587 → 72587 | 77.89 → 75.80 |
| decoded-edge-10cm | 0 | 256 | 0.10 | 175555 → 175536 | 124.26 → 133.36 |
| macro-10cm | 0 | 256 | 0.10 | 88254 → 88254 | 90.83 → 92.99 |
| near-lod | 2 | 128 | 0.80 | 65383 → 65383 | 35.74 → 34.70 |
| middle-lod | 5 | 128 | 6.40 | 104512 → 103882 | 52.66 → 49.78 |
| distant-lod | 8 | 128 | 51.20 | 111318 → 110480 | 52.44 → 49.62 |
| rear-lod | 9 | 32 | 409.60 | 6943 → 6871 | 3.55 → 3.38 |

The eight refined tiles total 698,874→697,315 faces (−0.223%), with arithmetic
mesh payload 79.980→79.801 MiB. Their summed per-tile median CPU times are
517.28→518.50 ms and cumulative allocation is effectively identical
(122,874,800→122,874,928 bytes). This is no convincing speed improvement.
Initial coverage totals 4,102→4,077 faces (−0.61%), also effectively unchanged
in timing and allocation. These sums are a small chosen sample, not a world-wide
weighting, whole Start measurement or native peak-residency estimate.

All height-array digests match. Exposed rectangle area sums are not bit-exact:
changed merges produce differences up to 0.265 m² on a large far-tile boundary,
with maximum per-axis relative difference 4.36e-9. This is consistent with
single-precision rectangle dimensions but is not a rendering/coverage oracle.
The initial absolute-area assertion failed; the raw discrepancy is retained.
No visual or geological-quality acceptance follows from this CPU comparison.

Exact tint, material, wetness, elevation and AO conditions constrain greedy
merging. Removing the repeating material bands alone barely affects these
samples. Spatial color changes still need face-count checks; a more complicated
geology or tint system cannot be justified as a generation optimization here.
[Raw measurements, source snapshots and limitations](StartupThroughputEvidence/2026-10-27/material-merge-control/summary.json).

Targeted coverage follow-up: the changed `rear-lod-coverage` tile has the largest
absolute area discrepancy. Its487 baseline/485 control rectangles were decomposed
into oriented planes and horizontal strips with10cm vertical integer intervals,
then compared with a separate direct column-height/halo/seam-floor oracle.
Both cover the same64 top cells and144 side/boundary strips with multiplicity1;
intentional missing/duplicated-face checks are detected. Canonical coverage hashes
match. Maximum coordinate snap error is0.00041504m (0.415mm), supporting float
rounding as the cause on this representative tile. The original strict area-sum
failure and all absolute/relative differences remain recorded. This does not
establish native rendering or final geological quality.
[Targeted coverage evidence](StartupThroughputEvidence/2026-10-27/material-merge-control/boundary-coverage/result.json).

## Startup-only mesh quotas — isolated test-enabling candidate

The smaller candidate changes three files and remains outside Assets. New
settings retain1536MiB far detail and1024MiB contact defaults. Bootstrap applies
them once, immediately after creating WorldExplorer and before configuring its
source or initial plan. Far quota clamps64–1536MiB; contact128–2048MiB. Changing
the new settings requires a Play restart. The periodic settings pass leaves them
alone, including the existing direct contact-budget debug/increase API. There is
no F1 control, live far-budget eviction/debt policy, new queue, worker or cache.

Contact mesh admission uses its existing replacement-aware byte rule earlier:
after geometry construction but before vertex scratch allocation, and again
before native Mesh creation in case the legacy debug allowance changed between
yields. Final publication still checks admission and preserves an old fallback.
This avoids expanding/uploading a known-over-budget contact tile. It does not
avoid the preceding geometry build or bound all native transient memory.

The exact candidate passes15 focused startup/admission assertions,30 progressive,
10 contact-capacity,35 contact-freshness (including11 repeated publication-cap
checks),19 existing lifecycle checks, and five C#9/.NETStandard2.1 assemblies
against installed Unity6000.3.27f1 APIs. The progressive fixture now reads the
public effective quota instead of reflecting a removed private constant; its
other assertions are unchanged. These are CPU/shim/API checks, not native Play.

For a deliberately small test with192 terrain slots and64/128MiB quotas:

| Admitted mesh payload | MiB |
| --- | ---: |
| Far detail plus one reserved coarse plan |186.34|
| Far detail plus two reserved coarse plans during replacement |308.69|
| Far plus contact, steady |314.34|
| Far plus contact, two-plan transition |436.69|

These figures exclude an incoming contact replacement before old retirement,
geometry results/upload scratch, Unity CPU/GPU copies, delayed destruction and
other scene costs. All99 vegetation prototypes still load, with approximately
416.49MiB arithmetic mesh payload. Reduced quotas can leave distant quality or
contact coverage deferred; admitted contact tiles retain10cm resolution. This
small profile enables a resource-conscious experiment, not a quality upgrade or
evidence of faster full Start. No asset, scene or saved user option was changed.

The earlier five-file live-decrease candidate, its32 focused checks and separate
regressions remain readable in the same evidence archive. Its initial builder
syntax failure, baseline-only API result and old private-constant reflection
failure are retained and clearly separated. Neither candidate is installed.
Next gate is exact-source coordinator review, followed by a separately guarded
native small-profile test. Main Editor/Play and the older downloadable ZIP are
unchanged by this checkpoint.
[Candidate, readable preimages, full source hashes and test reports](StartupThroughputEvidence/2026-10-27/startup-budget/summary.json).

## Startup-budget-1 installed — native compile and mesh checks

The coordinator reviewed and released exactly the three-file startup-only patch
at approximately11:25UTC. The idle hook verified all preimages and produced its
own rollback copies in addition to the readable independent backup. Main Unity
compiled/reloaded successfully; native mesh regression passed32,198 exact unit
faces/RGB and128,416 AO values across12 worlds. Existing obsolete-DLSS-test-API
and null-options pipeline warnings remain in the log; no C#/shader errors were
observed in this patch's log interval. Preflight commit headroom was3.71GiB;
that dated observation is not permission for an unguarded later Play run.

Original Settings.asset, Exploration.unity and saved exploration-options.json
hashes remain unchanged. No Play or new Editor process was started by this lane;
the coordinator owns the next temporary-profile smoke test. The earlier candidate
report above and summary.json preserve their pre-install checkpoint identities.
The five-file live-decrease alternative remains uninstalled.

One additional CPU feasibility check used actual14fb32 source data, the16m
contact-radius plan and128MiB contact quota. The17 requested10cm tiles total
159.72MiB; actual priority/admission accepted13 (120.92MiB) and retained four
deferred. The2m spawn terrain-ready footprint was covered after the first tile.
One cold sequential geometry pass took1.695s (about10.0 tile builds/s), with two
sampling workers and no native upload. This is not overall Start throughput:
contact Mesh objects held metadata only, while actual geometry was built one
tile at a time. Bootstrap also waits for shared prototypes/near population and
spawn clearance. A smoke test must not wait for contactPending to become zero
under an intentionally restrictive budget.
[Installed hashes, rollback identity and native logs](StartupThroughputEvidence/2026-10-27/startup-budget/install/installed.json).
[Exact actual-spawn geometry and admission report](StartupThroughputEvidence/2026-10-27/startup-budget/checks/small-profile-actual-spawn/result.json).

## Flight scheduling — final isolated candidate, 2026-10-27

The installed scheduler repeatedly canceled a useful distant build when the
observer crossed a25.6m contact-cell boundary. Preserving still-desired work fixes
that defect, but the small retention-only alternative still spends almost all
fast-travel work on ground contact tiles that are soon discarded. The final
candidate combines retention with altitude-aware priority and brief travel
deferral, using existing conservative planner enclosures and existing queues.

It enters after sustained horizontal speed of at least128m/s for0.25s, only
above the enclosure by32.1m and without rapid descent. Moving below96m/s exits
after0.25s; stopping below1m/s, descending, reversal, stale/unknown bounds or an
explicit spawn/teleport reset exits immediately. Reversal has a0.5s recovery
hold. Velocity uses global double coordinates, so floating-origin rebases do
not look like travel. The shipped40m/s x4 boost enters reliably at15/30/60/144/
240Hz; the rejected exact160m/s threshold did not, due to time precision.

During deferral, newly scheduled distant quality is capped at32 columns and
nonurgent contact work waits in its existing list. Source-refresh/coarse work
and physically urgent contact work remain runnable. Existing detailed surfaces
are retained. Stop/arrival wakes the refinement scan without requiring movement.
No worker, queue, persistent mesh cache, package, geometry/save version or larger
memory allowance was added. Contact matter remains10cm.

The final comparison runs a fixed nine-tile level5 plan on conservative flat
enclosures through high travel, stop,180-degree turn, reversal, vertical descent,
ground travel and disposal. It uses real geometry, queue, publication, retirement
and rebase code under .NET with Unity substitutes. Two repeats reverse order;
identical test-only worker timing hooks are absent from deployable sources.

| Fast-leg metric | Installed baseline | Final candidate |
| --- | ---: | ---: |
| New distant publications, each route |0|83|
| Of those:8 /32 columns |0 /0|39 /44|
| Contact geometry jobs, each route |191|14|
| Summed worker stopwatch duration, mean |3978.74ms|341.00ms|
| Published expensive tiles retired by destination |5|3|
| Peak far mesh payload |43,028,160B|43,028,160B|
| Peak contact mesh payload |23,334,000B|23,334,000B|
| Peak tracked upload scratch |7,340,032B|7,340,032B|
| Concurrent geometry jobs / uploads per tick |1 /1|1 /1|

The91.43% reduction applies only to summed worker wall time during this synthetic
flight workload. It is not CPU utilization, a Start-speed improvement or an FPS
prediction. Total harness wall averaged19.671s versus18.267s and includes test
waits. Fixed0.05s virtual ticks and requested2ms wall yields are separate clocks;
Windows wait granularity makes either unsuitable as native input latency.

After stopping outbound, the first new fine tile arrives at virtual tick25
instead of90; after reversal,8 instead of90; after descent,36 instead of80.
There is a tradeoff: outbound terrain readiness is tick4 versus2 and reversal
readiness3 versus2, because contact work was deferred. Those are harness ticks,
not milliseconds. Ground travel never enters deferral. Both variants converge
with equal final payloads; measured maximum far surfaces are11 versus17.

The visual downside is deliberate: newly encountered terrain can stay coarse
during very fast high flight. Conservative mountain enclosures can also keep
ordinary ground priority longer, reducing the gain. Real mountainous flight must
show whether the extra coverage and faster stopped refinement outweigh temporary
quality loss. A renderer or bigger budget was not adopted to mask the problem.

Source-matched final checks pass:39 policy,10 controller,21 actual-worker/contact
preemption,30 progressive,36 freshness (24 fixed plus12 tick-cap assertions),19
lifecycle and five installed-Unity-API assemblies. The21-check fixture includes
eight successive contact boundaries, same-XZ vertical descent, cancellation before
contact upload, source invalidation and removed-plan work.10000 stationary policy
calls observed0 added bytes and0 terrain queries in the final serialized check.

The previous v4 candidate failed same-XZ vertical descent: its unchanged-contact-
center return skipped newly urgent contact work. Final source explicitly checks
urgency there and cancels/requeues distant work before contact publication.
That failure, an earlier threshold failure, a harness directory collision, an
incomplete-bound fixture and a concurrent allocation assertion failure remain
preserved. Do not use the earlier v4 checkpoint as installation approval.

Final candidate changes only Controller, WorldExplorer, its Integration partial
and Voxels partial. All six frozen main runtime preimages are unchanged. It is
**not installed**; native compile, mountainous visual flight, frame/memory and
arrival recovery gates remain. No Editor process or Play run was started by this
lane. [Current identity and evidence](StartupThroughputEvidence/2026-10-27/flight-refinement/current-status.json),
[final source, comparison and preserved failures](StartupThroughputEvidence/2026-10-27/flight-refinement/final-checkpoint/summary.json).

## Separate installed-profile native startup observation

The coordinator completed a60.028s bounded Play smoke using an owned temporary
small profile in the existing main Editor. First observed Bootstrap Ready was
8.391s after ExitingEditMode. Private bytes rose from8,995,590,144 to a peak of
10,415,910,912: growth1,420,320,768B (1.323GiB). Minimum global commit headroom was
3,133,915,136B (2.919GiB). Original scene/settings/saved-options hashes were
restored. The first attempt used an invalid Mono private-memory counter returning
zero and was stopped; that failure remains separate from the corrected native-
counter run. Rendered forest pixels were observed, but this is not a measured
full-quality input route or FPS acceptance.

| First sampled boundary from Play-transition clock | Seconds |
| --- | ---: |
| Play/reading world observed |3.237|
| Loading prototypes status |4.218|
| All shared prototype mesh payload counted |6.364|
| Starting population queue empty |7.574|
| First contact tile and Bootstrap Ready |8.391|

The first3.237s includes Editor transition and observation gaps. Reading existing
world/pack data, mesh decoding/conversion/upload and terrain work overlap; these
boundaries are not exclusive stage costs. Boot does not regenerate the atlas or
procedural prototype library. Shared prototype mesh arithmetic reaches416.49MiB,
with major jumps around mature redwood models10/11. This is not measured VRAM.
At Ready, one8.51MiB contact tile exists and the far queue still contains208 items;
Ready means the local spawn gates passed, not that the distant view finished.
Eventually13 contact tiles fit and four remain deferred under128MiB. No honest
native per-LOD chunks/second figure can be derived from these sampled backlog
counters. [Raw native smoke](StartupThroughputEvidence/2026-10-27/coordinator-smoke/bounded-result.json),
[sampled attribution with limits](StartupThroughputEvidence/2026-10-27/flight-refinement/final-checkpoint/coordinator-startup-breakdown.json).

Next separate small tests should measure the actual prototype/LOD set needed for
spawn safety and first visible cells before adding lazy-loading machinery, and
test preparing the spawn contact tile once its active coverage ancestor exists.
The current global coarse-startup gate waits for every coarse tile before any
contact starts. Any change must preserve complete coarse coverage, published
collision readiness, one worker and the true Ready condition. The measured
73ms first-process decoder improvement alone does not explain the8.391s result.

## Flight-refinement-1 installed; native compile and mesh gate passed

At approximately12:24UTC the coordinator independently reviewed the frozen
127-added/21-removed-line four-file diff and released the existing-Editor
installation slot. Fresh idle preflight had3.934GiB available commit. The idle
hook installed only Controller/WorldExplorer/Integration/Voxels; Bootstrap and
Settings stayed unchanged. Exact targets, independent readable preimages and
the hook's rollback copies were verified. Main native compile/reload and the
mesh oracle pass32,198 unit faces/RGB and128,416 AO values across12 worlds.
Existing unrelated obsolete DLSS API/null-options warnings remain documented.

Original scene/settings/saved options and the world/pack hashes are unchanged.
No Play/new Editor was started; the native slot was released. The actual mountain
flight, trees, stop/descent/reversal latency, FPS and process/GPU memory remain
unverified for this policy. Its public travel-deferred flag is transient read-only
diagnostic state; ResetObserverMotion explicitly resets spawn/teleport history
and wakes planning, without changing generated geometry or saved state.
[Installed identity, rollback and native logs](StartupThroughputEvidence/2026-10-27/flight-refinement/install/installed.json).

## Prototype demand at the actual spawn — measurement only

A separate tools-only audit reads the current99-record pack once, retaining
original collision/resistance and exact triangle/payload metadata. It invokes
production population planning, LOD selection, matrix publication and IsReady
with tiny metadata Mesh placeholders. At spawn X=-19065/Z=-10200, seed714103,
both small and authored profiles require the same17 cells/1372 placements/25
prototype IDs for the16m safety test. Removing each required cell independently
causes actual IsReady to fail. All46 checks pass; the first fixture's missing
instancing-material flag is preserved as a test setup failure.

| Needed set | Model IDs | All their runtime mesh LODs | Desired LODs plus cheapest fallback |
| --- | ---: | ---: | ---: |
| Whole shared library |99|416.49MiB|—|
| Spawn16m required cells, authored profile |25|17.39MiB|12.73MiB|
| First64 nearest cells, authored profile |29|338.30MiB|198.99MiB|
| First64 nearest cells, small profile |29|338.30MiB|88.38MiB|

The spawn safety set contains mature birches2/3, plants and granite rocks;
mature redwoods10/11 and conifers14/15 enter the first64-cell sample and dominate
its cost. Selecting only whole models is therefore much less effective once
that nearby forest is needed. Required LOD preparation is a stronger target to
measure. Desired-LOD figures precede the actual triangle-budget demotion; the
audit also records first-published and spawn-ready snapshots, whose membership
depends on CPU scheduling. The deterministic safety-cell set is the firmer
quantity. First64 cells do not represent the full horizon or every desired cell.

No lazy reader, runtime index, native upload change or Ready bypass was added.
All records were decoded to perform this audit. These figures describe arithmetic
mesh demand, not resident process/GPU memory or measured startup savings. Any
follow-up must preserve CRC/size validation, collision plus leaf/stick interaction,
fallback representation and published readiness before enabling input.
[Exact IDs/LODs, source/data hashes and preserved failure](StartupThroughputEvidence/2026-10-27/prototype-demand/summary.json).

Source inspection also closes an assumption in the proposed contact overlap:
the initial active terrain set is empty until every coarse tile exists, and
coarseStartup clears during that same activation. An active spawn ancestor
cannot currently exist earlier. Simply relaxing RefreshContactPlan would not
provide the requested safe overlap. A smaller complete first coverage plan is
the next bounded hypothesis; an extra unpublished-result cache was not added.

## Smaller initial-plan sanity check — rejected shortcut, correctness finding

A read-only production-planner probe at the actual spawn/20km compared64,192
and512 limits. It creates63,192 and510 desired tiles. The64 cap leaves the near
tile6553.6m wide, with819.2m spacing for its initial8-column mesh;192 and512
reach25.6m near tiles with3.2m initial spacing. No geometry was built and no
startup savings were measured. Automatically using64 first would sacrifice
initial terrain shape substantially, so this shortcut was not adopted.

The same check found an existing planner correctness issue. Its floating meter
coordinate adjacency predicate misses legitimate boundaries; split closure can
therefore leave neighbors more than one LOD apart. An independent integer
TileKey oracle finds68 directed/34 unique unbalanced pairs in the192-cap plan.
For52 directed pairs, the finer tile's parent is integer-adjacent but not detected
by the floating predicate. The64/512 plans happen to have no unbalanced pair in
this sample, although their floating adjacency tests also miss boundaries.
All360 sampled XZ disk positions per plan remain covered exactly once. This is
not evidence of a rendered crack, and is not caused by the flight patch: planner
source hash31f2f4ba… is unchanged. Separate correctness review is required before
changing planner code. The original assertions and subsequent diagnostics stay
FAIL rather than being relabeled as a passing planner test.
[Exact plans, counterexamples, source identities and limits](StartupThroughputEvidence/2026-10-27/startup-plan-shape/summary.json).

## Timer correction installed; planner adjacency remains isolated

The one-line slow-speed timestamp reset was installed separately at approximately
12:57UTC after explicit coordinator release. Idle preflight had4.030GiB commit
headroom. Main native compile/reload and bounded mesh oracle pass32,198faces/RGB,
128,416AO and12worlds. Exact Voxels464c5661… and independent/hook preimages were
verified; other five flight sources and original scene/settings/options are
unchanged. No Play/new Editor or new flight benchmark. Earlier timer-candidate
statements are historical. [Installed evidence](StartupThroughputEvidence/2026-10-27/flight-hysteresis/install/installed.json).

The separate one-file LodPlanner candidate validates levels0..24 before shifting,
uses checked Int64 grid endpoints, and falls back to exact widened decimal
arithmetic when a full signed64 key's endpoint exceeds Int64. A shared edge must
have positive overlap; corner contact is not adjacency. Normal project-coordinate
calls measured zero allocation; extreme fallback allocates an overflow exception.
No persistent cache, worker, queue or budget change was added. API5,54 existing
world-scale checks and4403 focused assertions pass, including independent
BigInteger predicate cases and complete pairwise non-overlap/balance plus exact
root-area coverage for15 synthetic plans. Counts include repeated oracle cases.

| Actual source,20km cap | Before/after tiles | Before/after near initial spacing | Unbalanced unique pairs before/after |
| --- | ---: | ---: | ---: |
|64|63 /63|819.2m /819.2m|0 /0|
|192|192 /192|3.2m /12.8m|34 /0|
|512|510 /510|3.2m /3.2m|0 /0|

The192-cap regression is explicit. Correct neighbor closure consumes different
slots under the existing priority rule; this does not prove all possible balanced
192-tile allocations must sacrifice that near resolution. No cap increase or
priority workaround was added. Synthetic near-quality regressions also occur at
one very large coordinate with64/192caps. This candidate is **not installed** and
does not meet the requested across-cap near-quality adoption gate.

The one actual shipping-profile measurement reads512tiles/30km/contact100m/
1.5px from serialized settings, LoadSavedOptions from the scene, and saved view/
FOV30km/93.4328deg. At recorded spawn yaw20/pitch8 and a1440x900 shim viewport,
both versions produce identical511tile records and all576 ring samples from101m
to8192m.412 samples have refinement eligibility in either plan; all are unchanged.
The nearest eligible far tiles extending past contact remain25.6m/128columns,
or0.2m target spacing. Those are requested targets before mesh memory admission,
not native achieved detail, point visibility or occlusion proof.

This is a correctness change with cost: the matching synthetic15-plan run sums
median planning times26.36 ->38.07ms.100k normal predicate calls took11.56 ->
12.81ms in that pair. These small .NET observations are not Start/FPS estimates.
An initial test used rounded meter endpoints to select the observer tile and
found two memberships at(-1e8,+1e8)/192. The final test uses canonical grid
selection and retains that meter-endpoint diagnostic; runtime meter queries
were not changed. Earlier failures retain their original source identities.
No world/pack files were edited, but render-plan topology changes; provenance
and adoption decisions remain for review. [Candidate, all source-matched evidence and limitations](StartupThroughputEvidence/2026-10-27/planner-adjacency/summary.json).

### 2026-10-27 — Planner capacity scan, no runtime adoption

At actual spawn, both256 and384 caps retain observer L0 (25.6m tile, initial
3.2m grid) at20km and saved30km views, with the same total tile counts as baseline.
Exhaustive supported integer caps64..first L0 find baseline150/163 and corrected
234/247, respectively. These are current greedy-plan outcomes, not theoretical
minimum counts or cap recommendations. At192/30km the corrected plan is L3 with
25.6m initial grid versus baseline L0/3.2m; at20km it is L2/12.8m. The earlier
512-profile exact plan/neighborhood comparison remains the broader quality check.
Both source-frozen runs pass, with zero geometry builds. No settings, priority,
runtime source or native process changes. Candidate remains isolated for review.
[Evidence](StartupThroughputEvidence/2026-10-27/planner-adjacency/capacity-checkpoint.json).

### 2026-10-27 — Planner-adjacency-1 installed after explicit tradeoff review

Coordinator released only LodPlanner31f2 ->1651 after reviewing the192-cap
coarsening and unchanged recorded512/30km plan/neighborhood. Fresh idle/no-job
preflight had4.432GiB commit headroom. Both readable rollback copies and exact
source hashes passed. Main Unity compile/reload and the bounded mesh oracle
passed32,198faces/RGB,128,416AO across12worlds. Installed-source .NET world-scale
checks passed54; these are not native world-scale Play or FPS tests. Integrity
passed184assets/11assemblies. All6 protected runtime files, original scene,
settings and saved options, world JSON and prototype pack remain unchanged.
No Play/new Editor; native slot returned to coordinator.

The render-plan deployment revision is `planner-adjacency-1/1651adfb`; generator
identity and serialized formats are unchanged. Topology can change because
neighbor split closure now uses exact dyadic keys. At192tiles the actual-spawn
initial near grid changes3.2 ->12.8m at20km and3.2 ->25.6m at30km. Caps/priorities
were not raised or altered to conceal that cost. The recorded512 profile is
unchanged, but this is not a guarantee for all positions. It is a correctness
fix, with the earlier measured planner CPU cost increase retained, not a speedup.

Exact key adjacency does not fix floating meter-coordinate render/membership
rounding. The earlier synthetic(-1e8,+1e8) overlap diagnostic remains; full
signed64-key predicate tests do not establish arbitrary-coordinate rendering.
Build still limits camera coordinates to+/-1e12m. Extreme endpoint fallback
allocates an overflow exception; ordinary-coordinate predicate calls measured
zero allocation. Earlier candidate/install-hold records retain their dates.
[Current status and installed evidence](StartupThroughputEvidence/2026-10-27/planner-adjacency/current-status.json).
