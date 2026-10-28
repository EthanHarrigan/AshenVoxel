# Staged progressive terrain LOD scheduler

2026-10-27. This isolated candidate changes three `WorldExplorer` partials under
`.terrain-cache/progressive-lod/candidate`. Production Assets were not edited.
The parent owns integration and shared project documentation. No Editor, Play,
driver, inference service, source terrain data, shader or renderer was changed.

## Behavior

- Every newly keyed tile first builds at eight columns. The complete new coarse
  coverage plan publishes when its keys exist, independently of quality work.
  The old coverage remains visible until that point.
- The existing queue and single worker then refine visible/near tiles through
  8 → 32 → 64 → 128 → 256 columns. Visible tiles exceeding the projected pixel
  target can progress beyond the previous 128-column ceiling. This changes
  render sampling density, not the 1280 m/5 m learned source or 10 cm matter.
- Near and visible tiles win priority. At comparable distance the rougher tile
  wins. A view turn reduces excessive rear-view detail before improving the new
  view. Static settled tiles are not rebuilt.
- Pending contacts within 32 m remain urgent. After that, two completed outer
  contact builds allow one far build on the same worker. During outer contact
  work, refinement slots skip base tiles already masked by full contact meshes.
  The 100 m contact target, 10 cm output and its existing admission budget remain.
- Useful builds survive same-key replans. Source invalidation/cancellation,
  rebasing, worker-failure retry and shutdown retain the existing ownership model.
  Source changes refresh affected face-capacity decisions.

## Memory admission and quality tradeoff

The old far terrain had no total mesh-byte admission and measured roughly
2–3 GB of far vertex/index payload. This candidate caps the added detail allowance
at 1536 MiB. Some visible tiles therefore **will remain below the 1.5 pixel
target** when capacity is exhausted; 256 columns is also still a hard tile
ceiling. High quality across the entire view is not guaranteed. A greater final
sampling density also costs more generation work; this is not a speedup to the
time required to finish every maximum-detail tile.

Each resident tile reserves 668,160 bytes for guaranteed coarse coverage. An
eight-column tile has at most 64 top faces, 112 internal exposed sides and 32
boundary sides. Each side has at most 38 intervals (31 regular cuts and six
physical-layer cuts); a boundary can add one apron. Thus the pre-merge limit is
`64 + 112*38 + 32*39 = 5568` rectangles, at 120 bytes per rectangle. Merging can
only reduce that count. The builder remains unchanged.

Admission charges `max(0, actualMeshBytes - 668160)` to the detail allowance and
credits only the replaced tile. With the planner's existing 768-tile maximum:

| Payload | Hard accounting bound |
| --- | ---: |
| Steady far plan | 2,123,759,616 bytes / 1.978 GiB |
| Active + replacement far plans | 2,636,906,496 bytes / 2.456 GiB |
| Single uncommitted replacement mesh | At most 120 MiB, existing face limit |
| Contact meshes | Existing 1 GiB default, independently accounted |

These are vertex/index payload limits, **not process RAM or VRAM guarantees**.
They exclude driver/Unity overhead, sources, compact height arrays, vegetation,
worker data and the existing reusable expansion scratch buffers. One upload can
temporarily coexist with its old mesh before destruction takes effect.

An over-budget result is rejected after worker generation but **before** packed
vertex expansion and mesh upload. The old mesh stays visible. The request remains
in `VoxelRefinementDeferredTiles` and `VoxelRefinementPendingTiles`, and contributes
once to `PendingTiles`. It is reconsidered after a plan/source change rather than
regenerated every idle frame. Other tiles can still make progress.
`TerrainResidentMeshBytes` includes all stored far meshes, including inactive
replacement coverage. The steady/transition limit properties expose the limits.
No new public quality setting, queue, worker, persistent mesh cache or package
was introduced.

## Executed evidence

All results preserve frozen source copies and SHA256 maps under
`.terrain-cache/progressive-lod/runs`; earlier runs remain intact.

- `20261027-065147-candidate-progressive/result.json`: 27 focused checks pass,
  including actual 8/32/64/128/256 publication, early coarse travel coverage,
  termination, exact admission boundary, deferred-request backoff, starvation,
  turns, source refresh, useful in-flight work, contact/far fairness and shutdown.
- `20261027-065238-candidate-api/result.json`: all five assemblies compile against
  installed Unity 6000.3.27f1 APIs, C# 9 and .NET Standard 2.1.
- `20261027-065238-candidate-existing/result.json`: final existing 19-check
  streamer lifecycle regression passes, including contact publication, turns,
  travel, source failure/recovery and shutdown. The earlier pre-fairness run at
  `064305` is preserved separately.
- `20261027-064619-baseline-progressive/result.json`: the unchanged baseline on
  the same controlled four-tile fixture. It stops the forward tile at 128.

In one final focused run, four newly keyed travel tiles published coverage in
8 cooperative ticks / 123 ms, versus 25 ticks / 202 ms in the baseline observation.
The candidate publishes coarse coverage first and continues refining afterward;
those are different publication quality points. These tiny Windows .NET/shim
observations are not native startup, FPS, frame-time or full-world measurements.
The contact fairness fixture used real mesh generation with less than 128 MiB
contact payload. Capacity boundary checks use hypothetical byte counts rather
than allocating multi-gigabyte test meshes.

No native import, rendering, seam/pop acceptance, sustained travel/FPS or native
memory-pressure validation was performed for this revision. The ordinary URP
renderer, material encoding and `VoxelLodSurface.Build` algorithm are unchanged.
The policy cannot promise seamless or maximum-detail scenery immediately at Play.

Rapid-replan follow-up: eight deliberately incomplete plans preserve only the
active plan plus the latest desired plan. `ApplyPlan` removes every stored
surface outside that union before adding replacement work. A retained in-flight
request must still belong to the latest desired plan; others are cancelled.
Final publication replaces `active` and removes the remaining obsolete surfaces.
The new test verifies superseded partial mesh destruction, exact resident-byte
accounting at each replan and complete retirement after the latest plan publishes.
It passed without further runtime changes in the expanded 30-check suite:
`20261027-065923-580614-production-overlay-progressive/result.json`. That run also
verified an isolated canonical overlay spanning WorldScaleRuntime and Exploration;
it is a runner check, not evidence of the parent's merged vegetation revision.
`20261027-065951-659789-production-api/result.json` independently verifies the
runner's direct-production selection against all five official API assemblies.
That production run predates installation of this progressive candidate.

## Reproduce and integrate

`python Tools/ProgressiveLod/stage.py` regenerates candidates from the frozen
three-file baseline. `run_checks.py` runs the focused suite; `--existing` runs
the existing lifecycle suite; `--api` compiles against real installed APIs;
`--baseline` runs the controlled baseline comparison. These commands write only
their own timestamped staging evidence.

`--overlay-dir <root>` gives exact canonical paths beneath that root precedence,
for example `<root>/Assets/AshenVoxel/Exploration/VegetationStreaming.cs`. It
supports all five runtime assembly folders and additional files within them.
`--production` reads current production Assets instead of substituting the three
staged terrain files; it is mutually exclusive with `--baseline`. The overlay
can be combined with either source mode. Each report records selected source
paths, hashes, source mode and overlaid files, preventing a merged or production
run from being confused with the isolated candidate.

For the parent's staged integration, use:

```powershell
python Tools/ProgressiveLod/run_checks.py --production --overlay-dir .terrain-cache/startup-throughput/merged
python Tools/ProgressiveLod/run_checks.py --production --overlay-dir .terrain-cache/startup-throughput/merged --existing
python Tools/ProgressiveLod/run_checks.py --production --overlay-dir .terrain-cache/startup-throughput/merged --api
```

After idle-safe installation, omit `--overlay-dir` and retain `--production` to
validate the actual installed sources. The focused production suite requires
the progressive runtime to have been installed first.

Use the final manifest and unified diff in `.terrain-cache/progressive-lod` for
integration. Verify baseline hashes against production first; if they differ,
merge the narrow changes rather than overwriting concurrent edits. Preserve
the three existing `.meta` files. Re-run combined checks after parent changes,
then integrate only through the parent's idle-safe workflow. All gameplay release
and earlier evidence identities remain unchanged.
