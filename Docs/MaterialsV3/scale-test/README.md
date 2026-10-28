# Mountain scale test (low angle) — 2026-10-28

Offline mock-ups made with `Tools/MountainPreview/Scenic.cs`. They use the game's own heights and v3 surface tints. These are **not in-game renders**, and nothing in the game has changed yet.

## Findings
- **Why trees can't climb today:** the 80 m/320 m mountains rise as 6–8 km walled plateaus. Their fronts are steeper than 45° right from the valley floor, so the slope rule removes every tree. Climate is a second cause: the temperature at those heights is far below treeline.
- **Elevation remap (×0.5 above sea level)** plus **talus aprons** (thermal weathering, 38° repose, cliffs mostly kept) create foothills. Forest can grow up these, with sheer faces left above them.
- **Hydraulic gullies** break up the faceted macro triangles. They also give couloirs where snow can sit and fingers of forest that run uphill.
- **Cast shadows and sky occlusion** add the most depth for the least cost (step 2).
- The **biome tiles show as square forest edges**. Blurring the edges (about 60 m) and adding ragged noise hides them.

## Candidate production work
1. Erosion + talus pre-bake: a one-off offline pass, then stored in the 80/40 m caches.
2. A slope- and height-driven montane forest rule in ForestBiomes, independent of the biome tile.
3. Baked horizon/sky occlusion for far terrain.
4. A decision on the elevation remap. It changes the world's shape, so it is a design choice.

Env knobs: HSCALE, THERMAL, REPOSE, CLIFFKEEP, TREELINE, MONTANE, SUNAZ, SUNEL, CAM.
