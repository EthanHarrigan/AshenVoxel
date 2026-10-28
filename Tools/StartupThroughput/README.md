# Small startup and per-LOD probes

Results and evidence boundaries: [startup report](../../Docs/STARTUP_LOD_PROGRESS.md).
These are development probes, not runtime dependencies.

- `prepare.py --variant baseline --task population-real --run` uses the frozen
  imported world and original source in `.terrain-cache/startup-throughput/frozen`.
  `--variant compact` selects the final vegetation experiment. Other retained
  variants are intermediate comparisons, not adopted configurations.
- Add `--triangle-budget 32000000` or `--triangle-budget 40000000` for isolated
  CPU budget comparison directories. The later audit records exact placement
  hashes and tree/plant/rock LOD distributions. These are frozen Version1 runs;
  do not relabel them as the concurrent grounding revision or native rendering.
- `--task lod` measures CPU tile generation with numerical renderer substitutes.
  `prepare_native.py` creates a new small native project using only Core/Data;
  `NativeLodProbe.Run` runs one tile at a time with warm-up and three repeats.
  Do not create additional native Editors while system commit is constrained.
- `validate.py --merged` validates the staged, wind-preserving vegetation source
  and exact fallback coverage. The permanent coverage case lives in
  `Tools/ExplorationStreamingChecks/CoverageCheck.cs`.
- `stage.py` reconstructs the original four-file merge from frozen sources and
  known patch6 wind guards. Its before-hash assertions intentionally reject an
  already installed or concurrently modified project. It never edits Assets.
- `Tools/ProgressiveLod/run_checks.py --production` tests the installed terrain
  scheduler; `--overlay-dir` can test canonical staged sources without importing.

The final real-world population observation is `population-real-20261027-005139`.
Earlier experiment files retain their identities. Frames are scheduling
iterations, CPU builder rates exclude upload, and neither substitutes for native
Play FPS. The merged source and native import records are archived in
`Docs/StartupThroughputEvidence/2026-10-27`.

`VegetationBudgetNativeTrial.cs` is a prepared Editor-only native fixture, not
runtime code. It passed installed-API compilation but has **not been executed**.
The current prepared snapshot is `.terrain-cache/startup-throughput/budget-native-installed-v1`,
using installed population v1 and current wind/aerial/contact-freshness sources.
Do not copy the rejected grounding-v2 sibling into it. The snapshot contains a
fixture-only guard revision, its original preimage, source inventory, API result
and `memory-plan.json`; no native process has been launched. Coordinator
acceptance and fresh memory preflight are required before `run_guarded.ps1`.
Never run this exit-on-completion fixture inside the user's main Editor. Preserve
the full source/settings/pack/atlas hashes before both runs. Run separate processes using
`AshenVoxel.Exploration.Editor.VegetationBudgetNativeTrial.Run32` and `Run40`.
Each opens an empty scene, owns one population, performs cold/warm publication,
renders eleven fixed views, saves `Logs/VegetationBudget/<budget>-<timestamp>`,
then exits. It does not start Play or WorldExplorer. Require adequate system
commit headroom and no competing native fixture; do not weaken its60s/mesh/
matrix/process guards merely to obtain a successful run. Camera.Render timing
and readback wait are CPU observations, not GPU timers or whole-game FPS.
A different placement hash, timeout or incomplete run invalidates the pair.

CPU-only travel and flight-altitude follow-up:

- `python Tools/ProgressiveLod/run_checks.py --production --program Tools/ProgressiveLod/RouteProgram.cs`
  exercises the real asynchronous Update/planner/geometry worker over three
  10km out-and-back routes, stops, turns and ground arrivals. This deliberately
  small64-tile/4km/16m-contact fixture uses accelerated virtual timestamps; its
  elapsed times are not gameplay speed or full-world residency measurements.
- Add `--overlay-dir .terrain-cache/startup-throughput/capacity-candidate` to
  test the frozen capacity fix without editing/importing main Assets.
- `python Tools/ProgressiveLod/stage_altitude.py` creates a separate experiment
  layered on that frozen capacity candidate. It reuses captured conservative
  planner bounds for contact urgency, with stale/unknown bounds preserving the
  original priority. It does not alter the frozen capacity overlay or Assets.
- Test that overlay with `--program Tools/ProgressiveLod/AltitudeProgram.cs`,
  `RouteProgram.cs`, the ordinary progressive suite, and `--api`. A measured
  first-refinement scheduling improvement does not prove native Start/FPS gain.
  Capacity-lod-1 and contact-freshness-1 are now installed and native mesh-checked.
  Altitude remains isolated and must be rebased onto the current freshness source
  before any adoption; its frozen capacity-only evidence keeps its original identity.
