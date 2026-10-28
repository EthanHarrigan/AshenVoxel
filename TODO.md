# TODO — prioritized (condensed 2026-10-28)

The full checklist history, including every completed item and its evidence links, is in
[Docs/History/TODO_through_2026-10-28.md](Docs/History/TODO_through_2026-10-28.md).
Edit this list in place. Do not stack new dated sections on top.

## P0 — Set up for success (housekeeping, small, unblocks everything)
- [x] Consolidate folders (WorldVoxels archived and removed; handoff emptied) (see START_HERE.md, "Cleanup").
- [x] `git init` and push to the private GitHub repo (2026-10-28). The existing .gitignore already excludes Library/Temp/Logs/.terrain-cache.
  Make a first commit. Consider Git LFS for `*.bytes` and `*.png` larger than 1 MB.
  After that, retire the `.terrain-cache/quality-patch-*-backup` folders.
- [ ] R2: make validation runnable for anyone.
  - Add UTF-8 to `Tools/check_project.py`.
  - Fix the smoke runner's source inventory.
  - Make the doc checker skip archives.
  - Add one `Tools/run_all_checks.py` entry point.

## P1 — One real playtest (the single biggest gap)
- [x] Atmosphere: bounded native shader/color/fog/depth/grading probes and mesh regression. Reference-led volume revision
  adds opaque billows with soft irregular fringes; 12 current captures saved for user review. Earlier rejections preserved.
  [Procedure/evidence](Docs/ATMOSPHERE.md). No Play was run.
- [ ] Atmosphere: user visual acceptance; inspect full forest/shoreline, moving clouds/rebases,
  saved controls and DLAA in a separately approved Play session. Measure frame/commit/VRAM deltas. No full-scene acceptance.
- [ ] First: stop/restart Play to compile the 2026-10-28 changes; follow the checklist in Docs/REPORT_2026-10-28.md.
- [ ] One consolidated native Play session in the Exploration scene with other games closed.
  - Normal profile first, then the saved Maximum.
  - Record: loading stages, whether distant ground appears, frame time and hitches,
    and peak commit memory (F1 → Save diagnostics).
  - Test punch, cut and fall on young birch, mature redwood and a rock. Then test pickup, kick and O restore.
- [ ] Run the prepared native free-fall and stump probes.
- [ ] Write the results into MEMORY.md. These results decide P2.

## P2 — Performance and memory (expected outcome of P1)
- [ ] Find where the mesh/terrain commit memory goes.
  - One open item: 2,609 terrain demands remained unmet despite source being ready.
  - Separate exposed-cube meshes, upload/transient buffers and GPU payload.
- [ ] Complete one real 160 m horizon region once headroom allows. Otherwise decide to adopt the
  standalone 320 m bake as the far source instead (needs compatible biome/fine identity).
- [ ] Cold-target preparation latency for mature trees. Compact the buried interior cells (60.8% of model 11).

## P3 — Gameplay foundations
- [ ] **Voxel floor near the player** (see below): bounded 10 cm ground plus a cheap voxel look in the distance.
- [ ] Persistent edit and inventory ledger (saved across Play). Move-on/keep sessions done 2026-10-28.
- [ ] Collision for kept (retired) broken remains; keep rooted redwood remains; breakable lab plants (300+ need replay recipes).
- [ ] Arbitrary terrain excavation with sparse authoritative edits, plus collision and LOD invalidation.
- [ ] R3: falling cubes collide with other props. R4: bee teardown.

## P4 — Optional adoptions (only on request)
- [x] Foliage lab integrated into the pack and biomes (2026-10-28). Still needs native LOD/memory/FPS check (P1).
- [ ] Hybrid worldgen experiment: a procedural intermediate field vs learned detail
  (Docs/HYBRID_WORLDGEN_FEASIBILITY_2026-10-27.md).
- [ ] Microphysics: edit transactions, blasts, small reaction set, conserved water
  (Docs/MICROPHYSICS_TRADE_STUDY_2026-10-27.md).
- [ ] Code cleanup R5–R10. Preserve GUIDs throughout.

## Voxel floor plan (added 2026-10-28; see Docs/VOXEL_FLOOR_REVIEW_2026-10-28.md)
- [ ] Step 1: revive the existing bounded 10 cm `ExplorationVoxelTerrain` path as an explicit F1 mode
  ("Ground: Smooth / Merged voxels / Virtual steps"). This fixes R7 instead of adding a new demo.
- [ ] Step 2: a shader-only "virtual step" mode that uses world-space 10 cm cell coloring on the
  existing connected mesh.
- [ ] Step 3: distance-faded cell coloring on far LODs, with the cell size growing per LOD ring.
- [ ] Step 4: A/B the modes on the same seed, camera path and shadows.
  Record frame-time p50/p99, hitches over 33 ms and peak commit memory.
