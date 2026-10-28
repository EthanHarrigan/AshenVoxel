# Voxel floor return — review of the OpenAI recommendations (2026-10-28)

This is a review only. No code was changed. It was checked against the current source:
- `WorldScaleData/VoxelLodSurface.cs`
- `WorldScaleData/VoxelTerrainPage.cs`
- `WorldScaleRuntime/GroundSurface.shader`
- `Exploration/ExplorationVoxelTerrain.cs`
- `Docs/WORLD_VOXEL_LODS.md`
- `Docs/VOXEL_TERRAIN.md`

## Verdict

All four points are sound. "Merged steps + voxel-color shader" is the right first approach,
and it targets the **actual root cause** of why voxel floors were removed. Below are that
cause, the parts the recommendations leave unspecified, and a concrete plan that fits this codebase.

## Why the old voxel floor was expensive (from source)

The whole-world voxel LOD path (`VoxelLodSurface`) already merges faces. It merges a top only when:
- the height matches,
- the material matches,
- the tint matches **exactly**,
- the wetness matches,
- and all 8 neighbours are at or below the cell (`Compatible` / `Open`, lines 77–80).

Side faces also require an exact `Tint` match (`MergeSides`). Every 10 cm cell carries its own
pigment, so in practice almost nothing merges. The recorded native result was:
- **38.54 M triangles** and **~2.31 GB** of vertex/index payload across 512 tiles,
- on a 32 GB PC where commit headroom is the recurring wall.

The cause is the colour-in-geometry coupling, not the step shape.

## Point-by-point

**1. Genuine 10 cm floor in a bounded area: shader cell colouring, no per-voxel physics. Agree.**
- No per-voxel objects is already a project invariant.
- `VoxelTerrainCollision` already treats the ground as 10 cm stepped tops. Restoring stepped
  visuals nearby makes **what you see match what you stand on**. That is a correctness win, not just looks.
- Suggested bound: real stepped geometry within about 48–64 m, rounded to whole 25.6 m tiles.
  The old 22.4 m overlay was too small, and the old whole world was too large.

**2. Cheap distant terrain with a voxel look, no subpixel cubes. Agree.**
- `GroundSurface.shader` already has the building block: world-space `_VoxelSize` cell hashing
  with an `fwidth` fade.
- Extend it in two ways:
  - Make the cell size grow per LOD ring (0.1 → 0.4 → 1.6 m…), so far terrain keeps a
    "slightly blocky" grain at a size it can actually show.
  - Quantise the shading normal toward the dominant axis, with strength fading by ring.
    This gives stepped light and shadow banding without stepped silhouettes.
- Cross-fade the cell size and strength over a band at each ring boundary to avoid popping.

**3. One toggle in the existing Exploration scene; measure hitches and peak memory. Strongly agree.**
- This also removes dead code. Review item R7 found the old `ExplorationVoxelTerrain` hookup is unreachable.
- Make it a single F1 option: `Ground: Smooth | Merged steps | Virtual steps`. Restart Play to switch, as with the other ground options.
- Reuse the existing native flight replay (`.terrain-cache/native-flight-replay`) for an identical camera path.
- Record per mode:
  - frame time p50/p95/p99,
  - count of frames over 33 ms,
  - largest single mesh upload in ms,
  - resident triangles,
  - mesh bytes,
  - texture bytes,
  - peak commit.
- Do 3 runs per mode with other games closed.

**4. Merged steps + voxel-colour shader. Agree. This is the fix.**
- Remove `Tint` and AO from the merge keys. Merge by height/plane only, and by material only if
  roughness or optics must stay per-vertex. Let the shader recover per-cell colour.
- Expected effect: flat ground collapses to a few rectangles per tile, and slopes become
  terrace strips along the contours.
- Noisy real terrain fragments the strips, so treat any reduction factor as unknown until it is
  measured offline. `VoxelLodSurface` already reports `UnmergedFaceCount`, which makes this a cheap first measurement.

## What the recommendations leave unspecified (decide before coding)

1. **Where the shader gets colours.**
   - Recommended: one small per-tile texture set, 256×256 for a 25.6 m tile:
     - top colour (RGBA8),
     - top height in cells (R16),
     - material/strata code (R8).
   - That is about 0.4 MB per tile, so about 10 MB for a 64 m radius.
   - Wall texels: use `depth = topCell - floor(y/0.1)` to select the strata layer
     (grass/litter → soil → sediment → bedrock), as `SurfaceMaterialSampler` layers them.
     Then add a deterministic per-cell hash variation.
   - **Caveat:** that is a display approximation of per-cell pigment, not a byte-exact copy of
     the authoritative matter. That is acceptable, because meshes and colours are caches,
     but it must be labelled. Do not port the C# sampler into HLSL; that would create a second classifier that can drift.
2. **Cell lookup robustness.**
   - Use the rebased `_SurfaceOriginPhase` position, not raw world space, which suffers fp32 error at km scale.
   - Offset inward by the normal before `floor()`, which the shader already does with 0.0001.
     Use about half a cell for walls so each wall reads the solid cell rather than the air cell.
   - Use point sampling with explicit derivative control, or mip-fade. Otherwise the grid shimmers.
3. **AO.** Merged quads lose the per-vertex corner AO, which is a large part of the blocky look.
   Compute it in the shader from the height texture (the 3 neighbours per corner, same formula
   as `VoxelTerrainPage.Face`). It costs a few texture taps per pixel. That is cheap for the near ring only.
4. **Shadows.** Real stepped geometry keeps correct self-shadowing nearby. In virtual-step
   rings the steps cast no shadows, which is acceptable once 10 cm is below a shadow texel anyway.
5. **Seams.** Use the existing aprons and the `_VoxelTerrainMask` clip between stepped tiles and
   the connected mesh. Check the 5 cm quantisation step at the ring boundary.
6. **Future digging.** Colour textures are cheap to patch per edit (a sub-rect upload) compared with a remesh.
   This design suits later excavation well.
7. **Upload hitches.** One mesh upload is indivisible, so fewer triangles directly shrinks the
   worst hitch. Also stagger the texture uploads, at most one per frame.

## Recommended order (each step is measurable before the next)

1. **Offline:** change the merge keys, then run the existing voxel LOD checks on actual tiles.
   Report the face reduction against `UnmergedFaceCount`. No Unity is needed.
2. **Shader:** colour texture + strata + hash, plus shader AO, on merged tiles within a bounded radius.
3. **F1 toggle and A/B** on the same flight replay: Smooth vs Merged steps.
4. **Virtual steps** on the far rings, with a cell size that grows per ring and fades.
   A/B again, then pick defaults.
5. **Acceptance:** close-up visual parity with the old unmerged look (screenshot diff at fixed
   views), no seams while walking or flying, and memory below the commit threshold with a Maximum profile.
