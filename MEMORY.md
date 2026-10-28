# MEMORY — current state (condensed 2026-10-28)

The full dated history before condensing is in
[Docs/History/MEMORY_through_2026-10-28.md](Docs/History/MEMORY_through_2026-10-28.md).
Nothing was deleted. **Rule:** edit this file in place to keep it current and
under ~150 lines. Put dated narrative in Docs/SESSION_LOG.md.

## Identity
- Unity 6000.3.27f1 (6.3 LTS), URP 17.3.0, Input System 1.17.0. Default scene: `Assets/Scenes/Exploration.unity`.
  Other scenes: ForestArena (original dig/granular demo), TreeWorkshop, GroundCoverWorkshop, WorldExplorer (research).
- Git: private repo github.com/EthanHarrigan/AshenVoxel (`main`), first pushed 2026-10-28. Commit instead of making backup folders. The last published ZIP is 2026-10-26,
  sha256 `bfaa982d…a8207` (Docs/releases.json). Everything after that is local only.
- Machine: Windows, RTX 3080 Ti. **System commit headroom is the recurring limiter.**
  Observed available memory was 1.0–2.7 GiB against a 2.31 GiB resume threshold, often with another game running.
  On 2026-10-28, free disk was also too low for a 17 GB tool workspace.
- Workspace: this folder is the only integrated copy. `../AshenVoxel-WorldVoxels`
  is superseded (its candidates were installed here). `../AshenVoxelFoliageLab` is an
  isolated authoring lab. `../terrain-diffusion-cloud-handoff-2026-10-26` is the
  Minecraft mod reference. See START_HERE.md.

## Resolutions (never conflate)
| Layer | Resolution | Status |
|---|---|---|
| Whole-world learned source | 1280 m | Shipped |
| Standalone baked coarse map | 320 m | Built ([Coarse320Evidence](Docs/Coarse320Evidence/2026-10-27/README.md)), **not adopted** |
| Horizon learned refinement (service :8788) | 160 → 80 m | Code installed; the one real attempt hit the memory guard, **no region completed** |
| Nearby decoded source (service :8787) | 5 m | Bounded 16/32-patch moving window, not full view; 5.115 km starter region bundled |
| Ground render mesh | continuous connected mesh, 0.4 m contact grids | Default |
| Props and local matter (authority) | **0.1 m cubes** | Authoritative; collision is source-backed |

## Gameplay as installed
- Loader: 5 stages, readiness grid, diagnostics export, same or lighter retry. It waits for usable local ground AND complete distant coverage before spawning the full population.
- LMB: 0.25 m sphere, 2.5 m reach, 7.5 cm aim assist. It works on all 99 baked tree/rock/plant recipes, including adapted roots.
  Aimed props start preparing within 12 m. The original mesh stays visible, and cuts rebuild only local patches.
- Detached sections get one native Rigidbody each. After settling they become weighted debris parcels (awake/dormant scheduling).
  Exact material conservation is checked.
- I = pickup (on by default), G = kick, O = undo the *current* prop only. Punching another specimen, or moving
  18 m away, **keeps** the broken one (registry "harvested" set; remains settle 15 s then freeze; ≤48 kept) and
  starts the next at once. Ownership swaps in-frame (published batches patched); replays are cached (24 / 3M cells)
  and prewarmed for nearby species. Stopping Play still discards everything. (2026-10-28, not yet native-tested)
- Flora: pack has 99 core + 180 foliage-lab models (IDs 300–479; canonical core identity still = old pin).
  `BiomeFlora` = 26 communities (understory 1.4 m / midstory 3 m / canopy 7 m grids). ForestBiomes v2 adds young
  thickets with an infill grid. Instance budget 90k. F1 → Flora tour teleports to a community.
- Ground: voxel step surface (merged steps + shader 10 cm colour, parent morph) is default; Geology v2 strata.
  See Docs/REPORT_2026-10-28.md.
- Terrain predicts travel, requests nearby 5 m source, refreshes LODs and retires distant detail.
  The outer LOD rings refine globally in passes (8→16→32→64…).
- F1 contains: terrain quality Normal/Detailed/Maximum, reach and allowances, wind strength, shadow fill, DLSS/DLAA,
  ground representation, and pause for far-source refinement.
- Also installed: GPU-culled distant forest and impostors, wind, material optics, shrubs (U), bees (unverified),
  and grounded roots (off by default).

## Evidence boundary
- **Passed:** CPU/.NET suites (core 91, tree 73, authoring 40, about 144k physics assertions, renderer, terrain,
  source and HTTP checks), compile against the actual Unity API for 4–5 assemblies, project integrity,
  and isolated native Unity fixtures (mesh regression, seam bridges, gravity drop, appearance with zero pixel diff).
- **Never accepted:** full Exploration Play feel, FPS on any profile, distant ground under memory pressure,
  live mature-redwood cut and fall, native kick, cold-target latency, bee and root behavior, and disk-cache warm-hit speed.
- Native free-fall and stump probes were prepared but **never run**.

## Known code debt (Docs/CODEBASE_REVIEW_2026-10-27.md)
- R2: validation plumbing.
  - The smoke runner's source globs miss assemblies.
  - `check_project.py` crashes without `-X utf8`.
  - The doc checker flags archived preimages.
- R3: falling tree cubes do not collide with other props.
- R4: bee invalidation does not release resources.
- R5: gameplay depends on `Assets/_Validation/FiniteMatterSlice` (move it while preserving GUIDs).
- R6/R7: dead branch-demo and voxel-overlay startup paths.
- R8–R10: duplicated meshing, unchanged mesh uploads, and an oversized bootstrap.
- R1 (the old pair-solver throughput problem) was largely addressed by the sections plus sleeping-debris rewrite.
  This is not natively accepted.

## Process notes (learned the hard way)
- Previous work used several parallel ChatGPT/Codex agents, a "coordinator" and frozen candidates in sibling copies.
  That produced duplicate folders, 47 hand-made backup folders and 100 KB+ status files. Prefer one copy plus git commits.
- The user has repeatedly interrupted Computer Use with a physical Escape during Play.
  Do not drive Unity Play without asking first.
- All agents were stopped at the user's request at the end of Oct 27. No jobs are queued.
  The last Editor activity was around 00:28 UTC Oct 28.

## Invariants
See AGENTS.md. In short:
- Core has no Unity references.
- All writes go through `VoxelWorld.Set`.
- No per-voxel GameObjects.
- Meshes are caches.
- Seed + version + meter profile identify generation.
- Preserve .meta GUIDs.
- 10 cm is the matter truth.
