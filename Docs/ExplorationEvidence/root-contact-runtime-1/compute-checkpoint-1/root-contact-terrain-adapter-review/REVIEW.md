# Independent root terrain adapter audit

## Finding: P2 — nested surface wrappers can be admitted as bare terrain

`RootTerrainSnapshot.Bare` removes only consecutive outer `SurfaceWorldField` instances (`RootTerrainSnapshot.cs:41–46`). `TryCapture` then accepts any remaining snapshot provider (`:59–64`). MAIN's `TerrainDetailField` is such a provider, but its basis is private and may itself be a `SurfaceWorldField`. Its `Sample` adds relief to that basis's sampled height (`TerrainDetailField.cs:45–48`), and its snapshot method preserves a basis without `IWorldFieldSnapshotProvider` (`:62`). `SurfaceWorldField` has no snapshot-provider interface and samples the snow-bearing `SurfaceHeightMeters` (`SurfaceWorldField.cs:15–16`).

Consequently, both `TerrainDetailField(SurfaceWorldField(bare,snow))` and `SurfaceWorldField(TerrainDetailField(SurfaceWorldField(bare,snow)))` are accepted with a `no-snow` adapter identity but include snow. The captured field may also retain the inner live wrapper rather than a fully detached backing snapshot.

The separate CPU reproduction uses the frozen adapter and actual MAIN source types, a constant50m field, and the same explicit cold/wet profile used by the adapter's existing checks. At cell(0,0):

| Layering | Accepted | Returned top | Correct bare+detail top | Backing height calls |
| --- | --- | ---: | ---: | ---: |
| Surface(Detail(bare)) | yes |49.8m|49.8m|1|
| Detail(Surface(bare)) | yes |50.8m|49.8m|11|
| Surface(Detail(Surface(bare))) | yes |50.8m|49.8m|11|

The adapter reports `SampleCalls=1` in all three cases. The internal surface sampler therefore also violates the intended no-material/no-snow/no-32m-halo interpretation for the accepted reversed chains.

This is **not evidence that current MAIN's standard wiring is wrong**. MAIN `ExplorationBootstrap.cs:86–88` obtains the refinement authority, optionally adds relief, then passes it to the renderer; the renderer adds the outer surface wrapper. `ExplorationLiveRefinement.Configure` creates the snapshot-capable refinement authority even when live decoding is disabled. That known path is covered by the successful standard control. The existing adapter report acknowledges standard-layering-only support, but the public admission method does not enforce that precondition.

Before generic integration, either restrict admission to an explicitly verified bare-authority boundary supplied by the owner, or provide supported wrapper inspection that rejects reversed/alternating surface-under-detail chains. Preserve the detail contribution; stripping `TerrainDetailField` would introduce a different geometry mismatch. Do not infer wrapper safety by parsing identity strings or claim that every accepted arbitrary `IWorldField` is no-snow. If the owner guarantees the standard path, retain this as a documented integration constraint with a regression rejection/control case.

Reproduction: `runs/20261027T133240179961Z/result.json`; `Repro.cs` and `run.py` in this directory. No existing source was modified.

## Other checks and accepted behavior

- Reviewed adapter SHA256 `d132d7b36405e94624db437f161a85d47a3e5f409f423ebee9b1c1a4d9336462`. All source/input identities from its existing report matched MAIN and the frozen candidate before the separate reproduction compiled those exact sources.
- MAIN `SurfaceSample` rejects nonfinite constructor heights. `VoxelTerrainPage.TopCell` additionally checks finiteness and the +/-1e12m limit before its checked long conversion, then corrects floating boundary rounding; `SurfaceTop` is reused directly. The adapter catches failures and returns false/output0 with a sticky reason. No alternate epsilon/rounding implementation was introduced.
- Sixteen additional checks passed: NaN, positive/negative infinity, positive/negative `double.MaxValue`, heights beyond +/-1e12m, exact +/-1e12m, +/-0.05m, zero, and extreme long-coordinate rectangles. Valid heights match MAIN's `SurfaceTop` exactly; invalid heights fail closed.
- Rectangle endpoint bounds make subtraction safe. The side-limit short-circuit occurs before area multiplication for wide rectangles, avoiding an overflow path. Query attempts cannot overflow within the declared maximum; out-of-rectangle attempts consume a slot and fail before sampling.
- For the standard chain, sampling exactly `WorldCell.Center` preserves10cm horizontal distinctions and the detail field. It matches quantized `SurfaceMaterialSampler.GroundHeightMeters`, intentionally excluding snow, material choice, and climate evaluation. Visible snow top is a separate quantity and may lie above this root support surface.
- Live and captured identities are compared before and after sampling, and after capture. Failures remain sticky. The public adapter `SourceIdentity` is immutable; downstream cache code must use `adapter.IsCurrent()` rather than comparing that property with its original value to establish live freshness.

## Practical integration limits

- This is a single-owner object. Its counters/failure state are mutable, and `Cancel` is not a cross-thread cancellation token. Unknown `SourceIdentity`, `CaptureSnapshot`, and `Sample` callbacks still need a thread-safe/immutable provider contract. A captured object does not itself prove that contract. A coordinator must recheck terrain, template/context, owner/edit, and publication identities after CPU work.
- Query count measures attempts and top-level snapshot calls, not all provider work. MAIN's standard refinement state can inspect its bounded patch list; detail adds procedural work and identity-string allocation. Snapshot capture and identity callbacks are outside the per-query attempt budget. The caller still needs an outer candidate/scheduling budget; there is no frame-time or memory guarantee here.
- The existing10954-check report and1715 quantization comparisons were inspected, not rerun wholesale. Its11 full-column backing calls use an explicit climate override, as already stated in its limitations; other providers may add work.
- No world, atlas, pack, full generator, native/GPU, or Unity session was loaded. No main/Assets/frozen source changed. Only this review directory and its small CPU reproduction artifacts were written.
