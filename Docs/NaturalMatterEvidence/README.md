# Natural matter implementation and evidence

Current frozen source/report index: [2026-10-28 checkpoint](checkpoint-20261028-003705/status.json).
The132087-assertion physics result includes exact weighted cargo,100001 logical
debris cells represented by4001 moving parcels, and actual flat/sloped birch
toppling before release. These are .NET CPU observations, not full-game FPS.

Native final: [Unity6.3/DX12 isolated result](native/20261028-003433-272/result.json),
[tipping section](native/20261028-003433-272/02-falling-section.png),
[weighted debris](native/20261028-003433-272/03-settled-weighted-debris.png),
[collected stump](native/20261028-003433-272/04-explicit-collection.png).
Actual cached meshes and instanced material/tint leaders were drawn explicitly;
all10 shader passes and the native mesh regression passed. Automatic input,
ordinary gameplay rendering, maximum settings and scene FPS remain unaccepted.
Earlier keyword/projection/cancellation runs remain under Logs/NaturalMatterNative;
the earlier numerical PASS with inverted images is not accepted as visual evidence.
The harness now follows installed URP's non-GPU projection input to its command;
passing an already converted matrix caused the failed depth/orientation results.

Current ray selection ignores leaf-only spans while retaining woody soft sticks.
The following replay description includes those separately verified span types.

## Full prototype matter replay support

`NaturalTreeTemplate` replays the exact unchanged `ExportPrototypes.cs` recipes for
all published tree IDs 0–15 and rock IDs 200–259. The prototype pack identity,
complete cell counts and canonical coordinates/material/raw-tint SHA-256 identities
are pinned. A same-count recipe change is refused intact. These hashes are a
replay safety check, not a new generator version or rebaked render pack.

`TryRequest(id, out Task<Prepared>)` admits one background build and retains one
immutable result. A different request while a build is running returns false;
retry later. New completed requests evict the previous retained result. At most
2,000,000 cells are admitted; the current maximum is model 11, 1,159,639 cells.
The current packed arrays consume about 19.7 MB for that model (12-byte Int3,
1-byte material, 4-byte tint per cell), separate from source world, sorting,
branch metadata, editable copy, physics and CLR overhead. This is a bounded
retained payload, not a measured process-memory guarantee.

`Prepared.CreateWorldAsync()` constructs a private mutable world away from the
Unity thread. `Anchor` is the original recipe anchor (Y=4 cells for all recipes),
including rocks. Worlds must not be mutated concurrently with physics creation.
Do not call synchronous `Prepare`/`Replay` from a gameplay click; those remain CPU
and compatibility entry points. No new meshes or generator settings are baked.

`TryReserveNaturalMatterRay` selects the nearest exact 10 cm cell along the
world-space ray, after inverse instance yaw. Its DDA uses the existing immutable
hard, stick and leaf interaction spans; it does not use LOD triangles as matter.
The published registry is scanned completely or the request is refused. A
100,000 candidate allowance covers the existing 60,000 instance registry cap;
callers should throttle attempts. Existing exclusive lease publication validates
source identity and epoch after asynchronous preparation. Adapted grounded-root
specimens are refused because their modified base does not match these recipes.

Important: the existing source pack divides wood into hard trunk and soft sticks.
The full matter world includes both. Incoming physics must preserve player safety
when converting soft source wood into solid physics contact. Close distance alone
is not a reason to refuse; actual overlap is what matters.

Validation: `python Tools/run_natural_matter_checks.py`. Its source-matched JSON
contains all 76 complete counts and identities, exact equality between authority
hard materials and pack hard+stick spans, equality of soft matter and leaf spans,
private-copy independence, asynchronous cache/copy checks, and exact DDA hard,
foliage and miss probes. Registry substitutes compile the actual selection source;
this is CPU/API evidence, not native Unity, GPU, visual or FPS acceptance. No
terrain matter editing is provided by this path.

## Approximate debris CPU checks

`python Tools/run_cheap_matter_checks.py` compiles the actual engine-free physics
with complete tree replay. It covers real young-birch cut/fall/ground/release and
pickup on flat/sloped terrain, no representative below its floor, 1-candidate
support/release progress, retained repeated cuts, all60 rock initial support and
weighted parcels with variable tail weights. Cargo counts include every logical
cell, and same-tint synthetic matter retains exact tint-key counts. Source-only
stump contact is not misreported as actual terrain impact.

`python Tools/run_cheap_matter_checks.py --baseline` compiles the archived original
physics against the same3395-cell birch workload. Both use600 warmup steps followed
by240 measured1/60-second steps, with collection disabled and identical cut layer,
yaw, terrain and budgets. The baseline's accumulated physics debt is reported as
an outcome; it has not advanced as much simulated time as the cheap model. These
are execution-cost comparisons for the same supplied workload, not equivalent
physical solver accuracy. Full shared-engine render/frame/GPU costs are excluded.
Timings and allocation probes are .NET CPU observations, not a Unity/FPS result.
The allocation measurement excludes the preallocated timing buffer and report.

The original budget1 release-liveness failure is retained separately; the parent
fixed it. Its hash was sampled after failure while physics was being edited, so
that artifact explicitly does not claim a matching compiled source identity.
Passing reports include hashes checked before and after execution. `--perf`
provides only rock/support/performance coverage and is not a substitute for the
full default suite; its source report is overwritten by the final default run.

Performance records now distinguish settled parcels, active airborne parcels and
the first120 collection frames. The active-air fixture first wakes all parcels
through one ordinary suction step toward a distant hand, then disables suction
and moves the fixture floor to-100000m. This uses public physics APIs, with no
private-state mutation. Weight4 has3001 representatives for12001 logical cells;
the independent weight1 trial has12001 active representatives. At8192 visits per
frame, the latter has a maximum two-frame round-robin visit interval; a reported
zero global debt does not imply every representative is visited every frame.

The same3395-cell comparison is also recorded at weight1 in the new solver, so
both original and new measured workloads have3376 representatives. The separate
default weight4 record has867 representatives. Synthetic queue testing explicitly
requests three cuts while support is busy and verifies that two queued requests
are retained and each applied exactly once. The real birch cut layer contains one
wood cell and needs no manufactured bodies or private-state edits.

## Read-only hollow-state experiment

`python Tools/run_hollow_probe.py` measures actual complete generator outputs,
without altering gameplay or baked visuals. `hollow-probe.json` records full
counts, per-material counts, source identities, six-neighbor surface definitions,
vertical run counts and prospective direct-array savings.

| Prototype | Total cells | Air-exposed | Fully occupied neighbors | All six neighbors same material |
| --- | ---: | ---: | ---: | ---: |
| Young birch0 | 3,395 | 2,786 | 609 | 25 |
| Mature redwood10 | 1,111,103 | 442,738 | 668,365 | 612,951 |
| Mature redwood11 | 1,159,639 | 454,550 | 705,089 | 647,522 |
| Large rock209 | 1,991 | 960 | 1,031 | 1,031 |

Model11's705,089 fully air-buried cells comprise350,914 Wood and354,175 Leaves.
Its canopy is not entirely a sparse shell:354,175 of747,735 leaf cells are buried.
Omitting buried physical state could therefore matter for both wood and canopy.
This is prospective: current gameplay retains logical and physics state for all
cells, while its renderer can hide internal faces. Removing interior logical
matter would change support and cargo; this experiment does not authorize that.

With current96-byte Cube plus26 bytes/cell of direct parallel arrays, omitting
model11's air-buried physics entries would avoid86,020,858 bytes of direct array
payload before dictionaries/CLR/mesh overhead. Preserving all material interfaces
instead leaves647,522 removable same-material interior cells and a78,997,684-byte
prospective saving. These are arithmetic estimates, not measured process memory.

Logical model11 matter has169,129 material-only vertical runs or504,791 exact
material+raw-tint runs. A candidate packed Int16 X/Z/Ystart/Yend plus material and
tint record would use13 bytes/run:6,562,283 raw bytes for the exact runs. This is
an unimplemented representation estimate, not a new save/cache format. A dense
per-cell Int3/material/tint list uses19,713,863 bytes before overhead. Existing
sparse chunk storage has different costs and is not measured by this comparison.

The globally optimal quarter-fill debris estimate groups exact material+raw-tint
keys and retains remainder weights. Model11 needs289,928 representatives for
1,159,639 exact logical cells (Wood102,983; Leaves186,945). Actual bounded releases
can need more representatives because frame/spatial partition tails cannot always
be merged. This estimate alone does not prove acceptable runtime frame cost.

The latest cheap-physics suite also exercises12 disconnected components with only
8 island slots and a simulated three-frame mesh publication delay. Movement uses
`!StructureBusy && publishedRevision==StructureRevision`. It observes the waiting
state, allows old islands to move while waiting, and drains every section without
a slot/mesh gate deadlock. No engine renderer is involved in this CPU liveness test.

## Adaptive parcel completion check

The final adaptive-release suite adds one truly connected100,000-cell upper
component (100×100×10 cells), a one-cell stem and one-cell root. Its default
weight is25 for100,002 total cells. A16-candidate body allowance releases only
eight members per frame; the check observes a partially assembled group retained
across frames and no prematurely published incomplete parcel. It finishes with
4,001 representatives containing100,001 exact loose logical cells, then collects
all100,001 and checks every original cell's resolved Read/ReadLocal ownership.
The original root remains Source. Exact colored cargo is100,000, separate from
the cut stem. Logical conservation is checked throughout12,649 support/release
frames; the complete CPU fixture takes about127ms in this recorded run, excluding
construction/generation. This is synthetic CPU liveness/count evidence, not a
frame-time or Unity claim.

Latest captured full-suite evidence is `cheap-physics-adaptive-final.log`, with
131,720 passing assertions and matching source hashes in `cheap-physics.json`.
The new grouping changes a visible representative into exact weighted ownership;
it does not remove logical interior material. The older explicit quarter-fill
hollow probe remains a requested comparison scenario, not the adaptive default
for million-cell trees.
