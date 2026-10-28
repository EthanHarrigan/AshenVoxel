# 320 m baked coarse-map experiment

Status: **complete and validated**. [Download the standalone map](Coarse320Map.zip)
(9.78 MB ZIP). [Whole-world comparison](whole-world.png) and
[regional comparison](valley-comparison.png) show the actual baked heights.

The 439,453 samples provide about 16 times the previous sample count. Height,
biomes and land flags occupy 2,637,719 bytes; paired climate occupies 8,789,491
bytes (11.43 MB combined). The ZIP also includes a 1,337,084-byte float32 TIFF,
identities, checksums, instructions and validation reports.

All 120 blocks completed. Time inside generation calls totaled **1,660.12 s
(27 min 40 s)**; median block time was 12.11 s. Thirty memory-wait exits and model
reloads extended the run. `run-timing.json` preserves the process accounting;
this shared-desktop run is not a clean hardware throughput comparison with the
historical 78.26 s CPU coarse-only bake.

Validation passes: 11 coordinate/filter/archive checks, **4,008 checks using the
actual project C# readers**, complete block/file hashes, exact TIFF/NPZ/atlas
height agreement, height/climate pairing, archive CRCs and ZIP extraction. Project
integrity passes for 252 assets and 13 assemblies. All ten preserved source,
scene, settings, job and upstream-file hashes remain unchanged.

The final atlas reduces RMSE against 169 filtered samples of the existing 5 m
patch from **159.21 m to 18.49 m** (88.4% reduction in that region). This supports
better shape retention in the tested patch; it is not a whole-world visual
percentage. Across the full map, 1.80% of samples change land/water classification
relative to interpolated old macro heights. The learned overview remains an
approximation and has a separate source identity.

The user requested a higher-fidelity baked coarse download, restricted to source
data. The experiment preserves the 299,520 x 149,760 m world and produces a
937 x 469 lattice at 320 m. The current bundled source remains 235 x 118 at
1,280 m, with a separate bounded 5 m decoded patch. No gameplay source, renderer,
LOD, package, scene or saved preference is changed by this experiment.

## What is generated

`learned-lowfreq-overview-v1` runs the pinned InfiniteDiffusion coarse and base
models, then reads the learned low-frequency elevation channel of the latent
stage. That channel is normalized signed-square-root elevation: division by its
blend weight, multiplication by 38.6 and subtraction of 31.4 reproduce the
upstream denormalization. Signed squaring yields height in meters. A Gaussian
with sigma 160 m and finite support of 480 m filters the result before global
320 m sampling. This actually executes the learned base stage; it does not merely
interpolate the old coarse map or add arbitrary procedural noise.

The established horizontal scale maps each native 30 m sample to 5 world meters.
The latent compression is 8, giving a 40 world-meter learned intermediate. Its
sample center is `40*i + 17.5`, matching upstream's half-pixel reconstruction.
Finite halos make filtering independent of the output block partition. The
expensive full-resolution residual decoder is omitted. This is an approximate
learned overview, not exact downsampled full-pipeline terrain and not a newly
trained native 320 m model.

Climate retains the original 1,280 m source fidelity, interpolated onto the new
grid. Temperature is adjusted using the existing physical lapse channel and
positive-elevation delta. Biome categories retain the original conditioning-cell
mapping. The land mask follows the new elevation's zero level.

## Provenance and recovery

- Upstream commit: `e8dcb4b1a834ab2f6b1a6f5256ed7c9f2f3e8230`.
- Model revision: `9ef8030cb805b433b98ec25c5dddefbac07a9e26`.
- Derived GPU/batch job: `0647786260a982ef6793a9e77fac850f0d9e23b8ae8ec3319fe6978b6538c84a`.
- Bake: `653a72959be6e6ec9abb8e9643c685b2c690c183768a109f2110ede02b7dfb99`.
- Existing playable source: `14fb32f26ff725abaf9f1e04668a1f46c5b4783a66f91d23253676f595f613e4`.

The CUDA/fp32/batch-8 job has its own identity. It is not relabeled as the current
CPU service's native world. Pinned model/config/statistics hashes and pristine
upstream source are checked before model loading. Meta initialization loads the
exact existing parameters without allocating random CPU model copies. After
each block, unused CUDA allocator reservations are released; model parameters
and completed outputs remain intact.

The 120 blocks are cached separately, each with a SHA256 and bake identity. A
resume rejects changed source, settings or corrupted blocks. Windows commit
guards require 4 GiB before loading and 2 GiB before the next block; a memory wait
preserves completed blocks. No dependencies or running service settings changed.
The full run encountered memory waits after blocks 55 and 60. Subsequent attempts
use `resume_coarse_overview.py`, which keeps those same guards, waits ten seconds
between attempts and preserves each process status/log before resuming. The
`attempt-*.json` files distinguish generation time from reloads and memory waits.

Reproduce/resume from the project root with the existing GPU environment:

```powershell
.\.terrain-cache\python-gpu\Scripts\python.exe -X utf8 Tools/ExplorationBake/bake_coarse_overview.py --job .terrain-cache/world-job-gpu.json --output .terrain-cache/coarse320-study/bake320-v1 --batch 8
.\.terrain-cache\python-gpu\Scripts\python.exe -X utf8 Tools/ExplorationBake/check_coarse_overview.py --output Docs/Coarse320Evidence/2026-10-27/contracts.json
.\.terrain-cache\python-gpu\Scripts\python.exe -X utf8 Tools/ExplorationBake/check_overview_consumer.py --input .terrain-cache/coarse320-study/bake320-v1 --output Docs/Coarse320Evidence/2026-10-27/consumer.json
.\.terrain-cache\python-gpu\Scripts\python.exe -X utf8 Tools/ExplorationBake/evaluate_coarse_overview.py --input .terrain-cache/coarse320-study/bake320-v1 --output Docs/Coarse320Evidence/2026-10-27
python -X utf8 Tools/check_project.py
```

## Probe observations and boundaries

The original full-backend probe loaded in 16.08 s and queried two small latent
regions in 9.88 and 4.19 s. This was not a whole-map measurement. The slim batch-8
probe produced a 64 x 64 overview in 15.73 s but stopped before its next block
at the commit guard. Batch 2 took 47.83 s and likewise stopped. Releasing unused
CUDA reservations allowed the batch-8 two-block probe to finish; it ran during
heavy Unity GPU contention, taking 118.56 and 89.15 s. Failures/partial probes
remain separate from the final bake. Their local reports are retained under
`.terrain-cache/coarse320-study/`.

The whole-map run's first block took 21.53 s; its second took 182.83 s. A Windows
GPU-engine observation showed Unity consuming approximately 92% of the GPU.
The user authorized temporarily minimizing Unity. When Computer Use inspected
the existing Unity window, it was already minimized, so no window input or
settings change was issued. The bake was then processing blocks in approximately
12 s. This is shared-desktop bake timing, not an isolated GPU benchmark or FPS
evidence.

`valley-probe.png` is an actual-data preview from the earlier batch-8 probe.
Final `whole-world.png` and `valley-comparison.png` are generated independently
from the finished atlas. Both sides use the same area, height colors and lighting.
These are offline hillshades, not Unity screenshots.

`completed-valley-preview.png` is assembled from four completed blocks of the full
bake while other regions were still running. Its 4,096 height samples match the
separate batch-8 probe exactly (`model-repeatability.json`). This verifies the
tested region across query partitions and processes, not universal GPU bitwise
determinism. Against 169 samples of the existing decoded 5 m patch, filtered to
the same 320 m scale, the preliminary RMSE falls from 159.21 m to 18.49 m. The
final evaluation confirms this comparison from the published atlas. This is one
patch, not a whole-world visual score or an FPS measurement.

The initial TIFF validator incorrectly required an absent CRS. GDAL emitted an
unnamed `LOCAL_CS` with the Point raster key; it is neither geographic nor
projected. The validator now accepts only absent/local coordinates and verifies
the exact signed pixel centers and all heights. No baked bytes changed. The
initial failure is preserved in `evaluation-attempt-1.json`; the successful
recheck and executed validator hash are in `evaluation.json`.

Source spacing, mesh spacing and authoritative 10 cm matter remain distinct.
No LOD processing is needed to create this baked source. Any future in-game
adoption still needs source identity/biome/fine-patch integration and native
visual, collision and performance acceptance. Existing downloadable project ZIPs
and historical native test evidence are not changed or relabeled.
