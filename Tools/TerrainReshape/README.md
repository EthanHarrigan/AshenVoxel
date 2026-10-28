# Terrain reshape pre-bake (erosion + talus)

Applies the "Teton" look from `Docs/MaterialsV3/scale-test` to the whole map. The result is one baked file,
`Assets/AshenVoxel/Exploration/Samples/ExplorationReshape.bytes` (gzip, about 17 MiB).

## What the bake does (40 m grid, whole 300 x 150 km world)
1. **Base:** samples the game's own composed height: the bundled atlas plus the 80 m overview windows from `.terrain-cache/regional-overview-cache`.
2. **Remap:** above a soft knee (150 m), the mountain mass is lowered by half. Lowlands, coasts and the sea are unchanged.
3. **Talus:** thermal weathering on an 80 m grid (38 degree repose, 90 iterations). Cliffs shed aprons at their feet, and the faces keep 80% of their height.
4. **Gullies:** particle hydraulic erosion (0.8 droplets per cell). This makes gullies, couloirs and fans. It is deterministic: tiles run in checkerboard phases.
5. **Output:** an additive delta in 0.25 m units, plus 80 m flow and talus masks for future material rules.

## Runtime
- `TerrainReshapeField` wraps every height source: atlas, overview and decoded 5 m patches. It sits below the surface relief.
  - Collision, rendering, population and all LODs therefore agree.
  - Land never crosses the shoreline.
  - Climate follows the new height through the lapse rate, so lowered flanks warm up.
- `ForestBiomes` v3 adds a montane conifer belt. Trees thin out through the treeline (-7 to -4 C), on slopes of 38-46 degrees and where it is dry. This lets forest climb the flanks.
- Toggle: `ExplorationSettings.EnableTerrainReshape`, effective on the next Play.
- Cost: about 70 MB of memory, plus about 1 s to decompress on a background thread at load.

## Rebuild
1. On the PC, pack the cache: `python Tools/TerrainReshape/pack_overview_cache.py .terrain-cache/regional-overview-cache patches.bin`
2. Compile `ReshapeBake.cs` together with `Assets/AshenVoxel/Core` and `Assets/AshenVoxel/WorldScaleData` (.NET 8).
3. Run it: `ReshapeBake <Samples dir> patches.bin ExplorationReshape.bytes blur=0 thermalFactor=2 thermalIterations=90`

The bake takes under a minute on 2 cores and about 1.5 GB of RAM. Rebake after the 40 m overview pass to pick up finer windows.
