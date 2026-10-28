# Ashen Voxel — start here

One page for you or any AI picking this up. Written 2026-10-28 after a full
review of every AshenVoxel-related folder in `Downloads`. If this page
disagrees with an older doc, this page and `MEMORY.md` win.

## What this project is

A Unity 6.3 LTS (6000.3.27f1, URP 17.3.0) exploration game. The world is about
300 × 150 km, generated from the Azgaar map "Ceteviles" plus a learned terrain
diffusion model. Nearby props and matter use 10 cm cubes that you can break.
Look target: Avroid's newest Reddit posts. Mood: Dark Souls.

## The five folders: what each one is

| Folder | Verdict | What to do with it |
|---|---|---|
| `Downloads\AshenVoxel` | **The real project.** It is the only integrated copy, and all accepted work has been merged into it. | Work here. Nowhere else. |
| `Downloads\AshenVoxelFoliageLab` | Active side lab: 108 plant/coral/cactus recipes, generated offline. Nothing is installed in the game yet. | Keep it separate. It gets adopted into the main project only on request (see TODO P4). |
| `Downloads\AshenVoxel-WorldVoxels` | **Superseded sibling copy.** Its terrain candidates (source near-front, residency, view coverage, disk cache, distant rocks, grounded roots) were hash-checked and installed into the main project. Its files are older (last touched Oct 27, 16:53) and smaller. | Zip or delete once you have checked it (see Cleanup). |
| `Downloads\AshenVoxel-AI-Handoff-2026-10-27` | **Empty.** It holds just an empty `AshenVoxel` folder. The doc overlay was already merged into the main project (`AI_HANDOFF_README.txt`, `HANDOFF-MANIFEST.sha256`). | Delete. |
| `Downloads\terrain-diffusion-cloud-handoff-2026-10-26` | A separate project: the **Minecraft** Azgaar + terrain-diffusion mod, blueprint .41. It was packaged as a reference for the Unity port. GitHub only has .37. | Keep as reference. If you still care about the mod, push .41 to GitHub (open item). |

## Where the game stands (end of Oct 27 sessions)

**Working in code, with CPU/.NET checks and Unity API compile passing:**
- Walk/fly exploration with a staged loading screen, F1 settings and diagnostics.
- Learned terrain at layered resolutions:
  - 1280 m for the whole world
  - 5 m decoded near the player, from local service :8787
  - 160/80 m horizon refinement, from service :8788 (it has never completed a region)
  - a continuous connected ground mesh near and far
- Breaking things: LMB removes a 0.25 m sphere within 2.5 m on any of the 99 baked
  tree/rock/plant recipes. Disconnected sections fall as Unity rigidbodies. Debris
  turns into cheap sleeping parcels. I = pickup, G = kick, O = restore.
- Forest streaming, GPU-drawn distant trees, impostors, wind, material optics, DLSS/DLAA.

**Not proven in the real game yet. This is the big gap:**
- A full-scene Play session with acceptable **FPS**, on any settings.
- Distant ground staying visible under memory pressure. **System memory
  (commit headroom) is the recurring wall**, often with another game open at the same time.
- Large-tree cutting and falling feel, kick, and the cold-target delay.

**Not built yet:** saved edits or inventory, digging into terrain, more than one
breakable object active at a time, and full-view 5 m terrain.

## How to play it right now

1. Close other games and heavy apps, because memory is the limiter.
2. Open `Downloads\AshenVoxel` in Unity Hub with 6000.3.27f1, then open
   `Assets/Scenes/Exploration.unity` and press Play.
3. If loading stalls, click **Restart with lighter settings**, or set F1 to **Normal** terrain quality.
4. Controls:
   - WASD to move, Shift to go faster, Space to jump, F to toggle walk/fly
   - LMB on wood or rock to break it
   - I / G / O for pickup / kick / restore
   - F1 for settings, Esc to release the mouse
5. For local 5 m refinement (optional), run `.terrain-cache\Start-Service.cmd`.
   Default Play does not need it.

## How to brief any AI (ChatGPT, Codex, Claude)

> Continue AshenVoxel in `Downloads\AshenVoxel`. Read START_HERE.md, MEMORY.md,
> TODO.md, then Docs/FILE_MAP.md. Work only in this folder; do not create sibling
> copies. Preserve .meta GUIDs, 10 cm matter and source identities. Distinguish
> code-checked from natively-played. Keep MEMORY.md under ~150 lines: replace the
> current-state section instead of stacking new dated blocks on top; move old
> detail to Docs/History/. My request: …

Deep references, only when needed:
- `Docs/AI_HANDOFF.md` (89 KB)
- `Docs/DECISIONS.md`
- `Docs/SESSION_LOG.md`
- the full archives in `Docs/History/`

## Cleanup status (2026-10-28)

- Done: `AshenVoxel-WorldVoxels` was archived to
  `.terrain-cache/archive/AshenVoxel-WorldVoxels-2026-10-27.zip` (5,179 files, verified)
  and its contents were deleted. The AI-Handoff folder was emptied. The two empty
  folder shells can be deleted in File Explorer.
- Done: this folder is a git repo (`main`), with remote `origin` = github.com/EthanHarrigan/AshenVoxel (private).
- Open: the first push was rejected because GitHub already holds commits. Run
  `Fetch-GitHub.cmd`, merge, then push again (`Setup-Private-GitHub.cmd` pushes).
- After the push succeeds, the 47 `.terrain-cache/quality-patch-*-backup` folders
  and `pip-install.log` can go. Keep `.terrain-cache/python`, `python-gpu` and `model`.
