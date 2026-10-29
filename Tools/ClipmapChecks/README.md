# Clipmap verification

Run `python -X utf8 Tools/ClipmapChecks/run_checks.py` from the repository.
The runner compiles actual engine-free clipmap state and the shared Editor checks
against .NET 8. It never launches, refreshes, or controls Unity. Reports and source
hashes go to `.terrain-cache/clipmap-checks/<timestamp>/`.

The CPU checks use deterministic completion order and explicit GPU-publication
acknowledgement. A completed sampling job is not evidence that its data has been
uploaded. Tests retain the previously published surface until acknowledgement,
then exercise movement, negative coordinates, teleport, invalidation and stale
source/window generations. Incomplete/faulted regions must not create false
coverage or overwrite newer state.

Geometry checks use barycentric coordinates of the actual triangle vertices as an
independent oracle. They check the fixed triangle diagonal, morph transition
interiors as well as edges, and parent/fine ring coverage. A direct interpolation
of morph factors at the query point is not generally the rendered surface.

The plan's synthetic terrain has separate near (<=400m, p99 <=.15m) and far
(p99 error/distance <=.006) measurements. Coverage must reach the full 40km test
domain; an eight-level .2m/512 grid only reaches about 6.5km. Failed numerical
targets remain failures; they are not relaxed or reported as native results.

Native shader import, forward/depth/shadow parity, camera hole sweeps, tree
alignment, motion flashing, GPU memory and frame timing remain separate gates.
The CPU runner cannot establish any of them. Native tools require an explicitly
approved existing Play session; no tool enters or exits Play automatically.

`python -X utf8 Tools/ClipmapChecks/benchmark_sampling.py` measures 100,000
production `SurfaceMaterialSampler.EvaluateColumn` calls split across three
representative windows. Add `--all-levels` to distribute the same total over all
15 High startup windows, including the fixed coarsest anchor. The bundled macro,
fine, climate, biomes, far cache and reshape archives are loaded with their real
readers, then composed with the saved detail seed and surface profile. Live
refinement is an empty immutable snapshot. Reports include source and asset
hashes, throughput, current-thread allocation, loaded process memory and a
single-thread startup extrapolation. Each regime has a 20-second guard and the
process has a 150-second timeout. No result is a Unity frame-time measurement.

Measured evidence from 2026-10-29:

- Baseline three-window run: `.terrain-cache/clipmap-sampling/20261029-034746-578864/`.
- After removing the per-call snow-quantile array:
  `.terrain-cache/clipmap-sampling/20261029-035216-741377/`. The checksum exactly
  matches the baseline; allocation falls from 233.5/58.0 bytes per middle/far
  sample to the 40-byte Stopwatch overhead per complete level measurement.
- Refined all-level run:
  `.terrain-cache/clipmap-sampling/20261029-035313-006747/`. All sampling loops
  allocate zero bytes after excluding Stopwatch construction. The 100,000 samples
  imply 38.37 single-thread seconds for 3,932,160 startup texels at the saved
  spawn. File loading took 2.47 seconds. This estimate excludes worker scaling,
  sample result arrays, appearance packing, snapshot/copy/upload and Unity costs.
  L6/L7 cost 20.8/23.1 microseconds per sample because appearance uses five full
  evaluations. L0 costs 3.21 microseconds; outer windows become cheaper as more
  samples leave the source domain. The material sampler source SHA-256 is
  `4cb3bcffaa2a6ec4f35480324adeb76f13df2572294fbab0aa1564df24be7c93`.

Raw field-height timing is diagnostic only: it omits snow and cannot replace
`SurfaceColumn.SurfaceHeightMeters` without changing the surface. Sampling
optimizations must preserve appearance, height and the shared source identity.
