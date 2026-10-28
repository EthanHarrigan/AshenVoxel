# Surface materials recovery and independent review

**Historical checkpoint log.** Use [MEMORY.md](../MEMORY.md),
[TODO.md](../TODO.md) and [AI_HANDOFF.md](AI_HANDOFF.md) for current status.
Old ownership, material counts and pending statements below remain historical.

Last updated: 2026-10-26 UTC. This is a durable checkpoint, not an always-on watchdog.

## Current task and ownership

The active task adds research-informed ground materials, physical static snow, shared global surface sampling for far terrain and near 10 cm matter, genuine five-channel climate input, and visual water/underwater tint. Existing Core material IDs 0–10 must stay unchanged; appended IDs are 11 Granite, 12 Limestone, 13 Sandstone, 14 Shale, 15 Gravel, 16 Clay, 17 BlackSand, 18 Snow, 19 Ice, 20 Silt, 21 Peat.

- `/root` owns integration and user communication.
- `/root/surface_data` owns `Surface*.cs`, `ImplicitMatter.cs`, and associated data checks.
- `/root/surface_runtime` owns `WorldExplorer.cs`, `RemoteTerrainField.cs`, and runtime checks.
- `/root/visual_water` owns visual-water code, shader, and checks.
- `/root/material_catalog` completed the additive catalog and reported 49 material checks.
- `/root/surface_research` owns research documentation.
- `/root/surface_review` owns this file and independent review only. Do not modify other owners' active source files.

## Resume here

Read `AGENTS.md`, `README.md`, and `Docs/DECISIONS.md` before architectural changes. This review read them on 2026-10-26. `/workspace/AshenVoxel` currently has no `.git` ancestor; do not rely on Git recovery. Preserve existing shared work.

Review these contracts after their owners freeze: physical snow extent and conservative bounds; global appearance coordinates; source biome versus vegetation override; near-sea wet materials and water flags; framework compatibility; appended IDs and existing-save migration. Run the owner check entrypoints and retain exact failures/reproducers. Engine-independent/shim checks do not establish Unity import, URP rendering, or player performance.

Useful entrypoints (verify current scripts before running):

```bash
cd /workspace/AshenVoxel
python3 Tools/run_material_checks.py
python3 Tools/run_surface_checks.py
python3 Tools/run_water_checks.py
python3 Tools/run_climate_checks.py --height Assets/AshenVoxel/WorldScaleRuntime/Samples/CetevilesNeural.bytes --climate Assets/AshenVoxel/WorldScaleRuntime/Samples/CetevilesNeural.climate.bytes
python3 Tools/DiffusionBridge/check_bridge.py
python3 Tools/run_world_scale_checks.py
python3 Tools/run_world_explorer_checks.py
python3 Tools/check_project.py
```

## Recovery limits

The review agent can inspect files, document checkpoints, run checks, and message the parent while active. It cannot restart a parent agent or guarantee detection/recovery from platform failure. Root will supply freeze/API checkpoints. This file does not claim background monitoring after the turn/session ends.

## Verified observations

- Initial `SurfaceProfile`, `SurfaceMaterialSampler`, and `SurfaceWorldField` files exist. Source owners are still active, so this is presence evidence only.
- No active source or tests were changed by this review agent.
- Independent review and final checks are pending source freeze.
- Independently ran `python3 Tools/run_material_checks.py`: all 49 checks passed. This includes exact historic IDs/colors, old Format 2 fixture load, new-material snapshot roundtrip, invalid-ID rejection, digging thresholds, and conservation of material plus tint through granular settling. The script regenerated `Docs/material-validation.json`; this is engine-independent verification, not Unity rendering.
- Confirmed the upstream `WorldPipeline._compute_climate` computes `temp_realistic = temp_baseline_up[0] + beta_up[0] * maximum(elev, 0)` before publishing channel 0. The new surface-climate contract correctly says temperature is already adjusted at terrain elevation. Do not apply beta a second time.
- Sent the runtime owner an integration warning: surface classification needs the complete 32 m halo for immutable neural snapshots, replacing the prior 2 m normal-sampling halo. The runtime source is active; resolution is pending final review.

## Confirmed review findings

### Live-remote LOD bounds fail after snow decoration (fixed)

`RemoteTerrainField.QueryBounds` returns conservative heights −12,000 to 48,000 m. `SurfaceWorldField.QueryBounds` adds the profile maximum snow depth, yielding 48,002.4 m by default. The reviewed `WorldExplorer.RequestPlan` still configured `LodSettings.maximumHeightMeters` as 48,000 m; `LodPlanner.Create` rejects the decorated upper bound. This fails live-remote planning regardless of the sampled height or whether actual snow occurs.

Independently compiled the actual current Core/WorldScaleData sources with a temporary field returning the same conservative bounds. `LodPlanner.Build(new SurfaceWorldField(field), ..., new LodSettings(minimumHeightMeters: -12000, maximumHeightMeters: 48000))` printed:

```text
decorated_max=48002.4
REPRODUCED: World field exceeds conservative LOD height bounds; expand settings before rendering.
```

The temporary reproducer is `/tmp/ashen-surface-review/Program.cs` and its executable is `/tmp/ashen-surface-review/repro.dll`; run with `/workspace/.dotnet/dotnet /tmp/ashen-surface-review/repro.dll`. Runtime owner and root were notified. No active source was mutated. Resolution requires LOD settings to contain the decorated field, followed by a remote-path regression check.

### Actual model temperature seasonality uses °C × 100 (fixed)

The reviewed `RemoteTerrainField.ClimateRegion` passed channel 1 directly to `SurfaceClimate.SeasonalityC`. Upstream `world_pipeline.py` documents internal temperature-standard-deviation units as °C × 100 in `set_custom_conditioning_import`; `tiff_export.py` explicitly applies a factor of 100 to Celsius input. `_compute_climate` publishes `coarse_up[3]` unchanged as its second climate channel. The existing bridge wire labels this `temperature_std_c`, but preserves the raw upstream values.

Independent inspection of genuine retained outputs found:

| Artifact | Raw channel 1 range | Correct Celsius range |
| --- | ---: | ---: |
| `/workspace/diffusion-bridge-final/neural-sample.climate.npz` | 653.63898–663.90771 | 6.53639–6.63908 |
| `/workspace/diffusion-bridge-conditioned/ceteviles-neural.climate.npz` | 1076.16736–1079.51563 | 10.76167–10.79516 |

`SurfaceClimate` rejects seasonality above 150 °C, so unnormalized real samples throw; removing validation would instead apply winter cooling 100 times too strongly. Normalize raw channel 1 by 100 at the runtime adapter boundary, preserving immutable provider/cache identities, and use a realistic raw fixture. Root, runtime owner, and surface-data owner were notified. Other four climate channels must retain their established units; channel 0 is already elevation adjusted.

Root subsequently assigned a climate-bridge agent to make the legacy raw protocol explicit, add a versioned physical-units protocol, and retain baked climate. Consult that owner's final API before choosing the adapter path; do not normalize a physical-units channel a second time.

### Inspection underwater status samples evictable global matter (fixed)

The reviewed `WorldExplorer.Update` passes global `BareField(field)` to `VisualWater.SetFrame`, although inspection matter uses immutable `inspectionField`. If a neural region is evicted, global sampling returns the rough fallback while the already materialized patch remains unchanged. For example, actual bed −10 m, rough bed +10 m, camera −5 m, and sea 0 m changes underwater status from true to false solely because the region was evicted.

The camera-bed query must use the immutable inspection snapshot while inside its footprint. Simply replacing the entire water source with that bounded snapshot fails because the broad water-grid hints sample outside its region. Sent root, runtime, and water owners the recommendation to separate global mesh-hint sampling from local camera-bed sampling.

## Additional verification checkpoint

Independently ran `python3 Tools/run_water_checks.py` after the water owner first froze: all 26 C# shim checks plus retained shader/material/static URP contract checks passed. This includes bounded geometry/sampling, global wave-phase rebase continuity, coalesced depth-hint invalidation, scoped GUI state, and teardown. A later inspection-bed-source API change may require a new rerun. Native Unity/HLSL import remains unverified.

Runtime owner reports the LOD maximum now includes maximum snow depth and every snapshot/prefetch path has a 32 m halo. Independent final regression verification is pending its freeze.

### Exposed proxy rock differs from physical top rock (fixed)

The reviewed surface sampler chooses the column's parent rock at `groundHeight - 2 m`. Exposed steep surfaces use that as their proxy material, but physical matter with no cover calls `RockAt(actualY)`. This can cross a sedimentary contact and make the distant surface a different rock from its near matter.

Independently compiled reproducer `/tmp/ashen-surface-review/Geo.cs`: a planar field `height = 108 + 2*x`, seed 714103, snow disabled, sampled at `(x,z) = (0,0)` yields slope 63.43495°, proxy Sandstone, and physical top at y = 107.95 m Limestone. No ground cover obscures the mismatch. Executable: `/workspace/.dotnet/dotnet /tmp/ashen-surface-review/geo.dll`. Root and surface-data owner were notified; preserve depth-dependent geology while selecting exposed surface rock at its actual surface depth.

Owner subsequently selected bedrock and tint at the top occupied 10 cm cell center. Independently recompiled the temporary geology reproducer against that source and scanned 40,000 exposed columns (`z = 0..19900` by 100; integer ground heights 100..299). It printed `No mismatch`. Final owner regression suite remains pending.

### Inspection-water fix independently verified at component boundary

Water owner added optional `cameraBedField` to `SetFrame`; global hints still use the wide source, while the camera-bed point query can use a retained inspection snapshot. Independently reran `python3 Tools/run_water_checks.py`: all 28 C# shim checks plus static shader/material checks passed, including immutable inspection occupancy after the wide/global source changes. Runtime must still pass this optional source while the camera is within the patch footprint; final runtime integration review is pending.

## Final data verification

After the surface-data owner froze, independently ran:

- `python3 Tools/run_surface_checks.py`: **44 checks passed**, followed by C# 9 / .NET Standard 2.1 compilation. Includes rock-contact material/tint agreement, depth-varying excavated rock, wind and solar azimuth, organic litter above soil, packed ice, physical snow extent, source biome, and bounded zero-allocation column sampling.
- `python3 Tools/run_world_scale_checks.py --atlas Tools/WorldScaleImport/fixtures/azgaar-synthetic.avwf`: **56 checks passed**, followed by C# 9 / .NET Standard 2.1 compilation. Includes Python/C# AVWF interchange and existing LOD/save/edit contracts.

Both runners' source-hash guards passed. Their updated JSON reports retain source hashes. These are engine-independent checks, not Unity import/render verification.

Current runtime source was also inspected after its camera-bed update: it supplies `inspectionField` only inside the finite patch's horizontal footprint and invalidates water hints when remote revision changes. Final runtime and climate suites remain pending their owners' freeze.

## Final climate verification

After climate-bridge code froze, independently ran the external-pair climate command above: **27 checks passed**, plus C# 9 / .NET Standard 2.1 compilation and the source-hash guard. It read the actual executed Ceteviles neural height bake and its paired physical-climate archive; the surface sampler consumed the resulting Celsius climate without a second original-elevation adjustment. This reuses executed neural evidence and does not claim new inference.

Independently ran `python3 Tools/DiffusionBridge/check_bridge.py`: **61 contract/unit checks passed**, explicitly using a fake backend. Covers legacy raw centi-C support, physical-v2 conversion exactly once, distinct immutable cache identities, explicit units, conditioning conversion and existing bounded transport/cache behavior. Actual new physical-v2 pretrained inference remains separately owned by `/root/climate_bridge`, running at `/workspace/climate-bridge-si-v2`; do not infer its outcome from these checks.

Static review found no additional climate-archive defect. Height pairing uses an exact canonical AVWF SHA-256 and matching dimensions/origin/extents, corruption/truncation/size guards reject invalid input, horizontal transforms stay aligned, and vertical rescaling applies beta only to the elevation difference. Root will bundle the sidecars; update the climate reproduction path if the final retained destination changes.

Independent `python3 Tools/check_project.py` also passed at the then-current 85-asset checkpoint. Final project asset count may grow when root bundles climate sidecars.

## Final runtime verification and review disposition

After runtime code froze, independently ran:

```bash
python3 Tools/run_world_explorer_checks.py \
  --baked-height Assets/AshenVoxel/WorldScaleRuntime/Samples/CetevilesNeural.bytes \
  --baked-climate /workspace/CetevilesNeural.climate.bytes
```

**57 checks passed** and the source-hash guard passed. Actual baked probe: ground **5770.9462890625 m**, seasonality **10.761672973632812 °C**, source biome **11**, climate supplied by the paired field. Covers the actual explorer LOD plan with 48,000 m plus snow bounds, required 32 m halo, legacy-missing/native-v1/physical-v2 climate conversion exactly once, immutable height/climate snapshots, bounded cache/transport, and real snow in local matter.

All four confirmed review findings now have source fixes and regression evidence: snow-expanded LOD bounds, climate seasonality units, immutable inspection camera-bed sampling, and exposed-rock contact agreement. Final runtime source passes the immutable inspection bed only inside the patch footprint. No further confirmed defect remains from this review.

Root is preparing a legacy actual-model HTTP service for the expanded live runtime probe. That later result, new physical-v2 real inference, final bundled sidecars, and final asset-integrity count remain root/owner handoff items. Do not treat 57 shim checks as native Unity or live model execution. All Unity Editor/URP shader/play-mode verification remains pending.

## Final frozen-source review — latest checkpoint

Root requested a focused final static review after freezing the arid parent-consistent gravel/Clay deposit change, restrained granite pigment, Inspector wind/sun fields, and private owned terrain material copies. No source was changed and the WorldExplorer runner was not run during this final review, because root owns the sequential live runs.

The checked evidence now records material **49**, surface **44**, world-scale **55** (without external synthetic interop), water **28**, paired climate **27**, and runtime **66**. The prior independent world-scale **56** included the extra synthetic-AVWF-specific interop assertion; the differing count does not indicate a dropped baseline test. `Docs/SurfaceEvidence/runtime-legacy-live.json` also records **66**. Root may replace the main runtime report with its final physical-v2 result.

Independently checked **289 source-hash entries across 40 unique source files** in the current validation reports and gallery provenance: every entry matches current files. Real-climate report filenames resolve relative to `Tools/DiffusionBridge`. Also checked all five gallery image hashes, the comparison image, their exact materialized binary inputs, and renderer tool hashes: all match. The final gallery source hash includes the revised arid deposits and granite pigment. These are controlled actual-sampler fixtures, not neural scenes or Unity screenshots.

Three final runtime/editor assets are outside the selected test reports' source maps; static-review fingerprints are:

| File | SHA-256 |
| --- | --- |
| `Assets/AshenVoxel/WorldScaleRuntime/Editor/WorldExplorerSetup.cs` | `de2742adc4c0f290be562d6b72bccedc177e9dd5f2b91f3a16686a73acb36b73` |
| `Assets/AshenVoxel/WorldScaleRuntime/GroundSurface.shader` | `a3abd7d04f33a073f56547ed11eec108191712da44951ebb742e154cdf7d6481` |
| `Assets/AshenVoxel/WorldScaleRuntime/Resources/GroundSurface.mat` | `6f0f168c926c316a0d65cea43830620246028c175de89c447af7b1a8a9aaa5aa` |

Final asset/reference check independently passed: **87 unique assets, 9 assemblies**, scene/material references, 10 cm contract and local documentation links. Both paired climate `.bytes` assets and `.meta` files are now bundled in `Assets/AshenVoxel/WorldScaleRuntime/Samples`.

No new native Unity API blocker was found in static review. Mesh sizes remain within 16-bit limits for these proxy/water grids; material templates remain shared immutable inputs with private mutable copies; missing voxel UV data uses generic roughness; shader material CBUFFER layouts agree across GroundSurface passes; Resources materials retain shader dependencies. Actual Unity 6000.3.27f1 import, HLSL/URP 17.3 compilation, transparency/depth/shadow appearance and player performance remain unexecuted gates.

Two documentation overclaims were sent to root for final wording correction: VisualWater has coarse shallow/deep color and opaque-depth occlusion, but no actual zero-alpha shoreline fade; optional nonzero GroundSurface grain can change at a 4096 m phase wrap because the hash input is not wrapped. The default owned material disables that grain, so this does not affect default generated pigment or water wave continuity. Near voxel roughness remains a generic fallback rather than material-specific ice roughness, as documented. These do not require expanding the frozen implementation scope.

Final package target is `/workspace/AshenVoxel-SurfaceMaterials.zip`. It did not exist at this review checkpoint; root owns its creation and final package check. The parent was observed running normally; this review does not imply an always-on recovery process.


## Root final delivery checkpoint

Both final live consumer runs completed: **66 checks each** against the legacy
native-unit cached model and the new physical-SI cached model. The main runtime
report and `SurfaceEvidence/runtime-physical-live.json` record the new path;
`SurfaceEvidence/runtime-legacy-live.json` preserves the old-path result. The
actual new model's seven inference/replay checks are retained in
`SurfaceEvidence/physical-si-v2-model-validation.json`.

Final root reruns passed material49, surface44, paired climate27, water28,
world-scale55 with the actual Ceteviles neural atlas, and bridge61 fake-backend
checks. Engine-independent data compiled against C#9/.NET Standard2.1. The
existing Core87, tree73, authoring40 and mesher face/AO checks passed after the
catalog extension. The five gallery source/data/image proofs were revalidated.
The final documentation corrects both optional-grain and shoreline-fade claims.

The task-owned model HTTP services were stopped after successful checks. To
resume live work, start a bridge with an immutable job as documented in
Tools/DiffusionBridge/README.md; an idle HTTP service is not required to open
bundled baked samples. Final source package target remains
`/workspace/AshenVoxel-SurfaceMaterials.zip`; the separate pictures package is
`/workspace/AshenVoxel-Surface-Images.zip`. Archive checksums are sibling
`.zip.sha256` files; package contents carry their own manifest. No native Unity
verification or always-on restart service is claimed.
