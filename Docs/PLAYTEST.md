# First Unity playtest and smoke protocol

This is an **unexecuted Unity validation protocol**. Engine-independent results are recorded separately in [VALIDATION_CORE.md](VALIDATION_CORE.md). Completing those checks does not establish shader compilation, successful Unity import, playable movement, or player-build performance.

Run this protocol on the pinned editor before extending gameplay. Record failures as defects with the exact step, screenshot/log, world seed, and whether they occur in Editor, player, or both.

## Current default: Exploration acceptance

The default scene is now **Assets/Scenes/Exploration.unity**. The detailed arena
steps below remain applicable only when explicitly opening ForestArena. Use the
[exploration guide](EXPLORATION_GUIDE.md) for setup and settings. Actual-Unity API
compilation and numerical headless checks are recorded in the
[independent review](EXPLORATION_REVIEW.md); this native protocol remains unexecuted.

1. Open the complete project with6000.3.27f1. Let packages/import finish. Confirm
   first-open setup selects Exploration; a later reopen preserves your scene.
   Use **Ashen Voxel → Exploration → Repair project setup** if needed. Confirm
   no C#/shader errors and no magenta terrain, plants, rocks or water.
2. Press Play without Python or a local service. Record startup separately from
   steady play. The loading message must remain until trees/rocks, nearby
   terrain and clear spawn are ready; then one camera/listener and controls work.
3. Walk/sprint/jump on gentle terrain; brush through ferns/leaves, collide with
   tree trunks/woody bush stems and irregular rocks. Check camera smoothness,
   slope limits, stepping, head clearance and descending onto a rock. Cross
   population-cell and floating-origin boundaries while far cells still load.
4. Fly with F; move vertically with Q/E or Ctrl/Space, boost with Shift. Flight
   collides unless N explicitly enables noclip. Returning to walk in an occupied
   trunk/steep hillside must fail safely. Left/right click and the old edit keys
   must never break/place/reseed the exploration world.
5. F1 pauses movement and exposes speeds, view20–40 km, optional coarse horizon,
   tree/plant visibility, fog and sun. Save/restart to check numeric options.
   Escape releases the pointer; click recaptures. Stop/Play must not mutate shared
   source assets, duplicate listeners or retain a previous runtime pipeline.
6. Fly out of the decoded starter region and confirm F1 identifies learned
   coarse terrain. Inspect mountain silhouettes from high elevations at20,30
   and40 km, then optional80 km coarse coverage. Check missing tiles, cracks,
   sea-plane rebasing, snow/shore materials and near ground/feet agreement.
7. Visit different forest habitats and age stages using the surveyed example
   coordinates in the guide. Compare full-scale redwoods, young dense stands,
   mature open stands, rock colors and foliage shadow/AO at fixed lighting.
8. If enabling a matching optional model service, check absent/mismatched server
   stays playable, ahead patches arrive without moving occupied ground and
   disabling requests retains active detail. A resized baked world must refuse
   incompatible live coordinates. Record provider/runtime IDs with defects.
9. Profile CPU/GPU frame time, spikes, RAM/VRAM, terrain tiles, visible instances,
   triangles and startup decode/upload. Repeat in a standalone player built via
   **Ashen Voxel → Exploration → Build Windows x64** or **Build Linux x64**.
   Count/time budgets and headless passes do not establish a target FPS.

Batch entrypoints for this scene are
`AshenVoxel.Exploration.Editor.ExplorationProjectSetup.Repair`, `.BuildWindows`
and `.BuildLinux`; use the Unity command structure below with those methods.

## 1. Test record

Copy this block into your test notes:

```text
Date:
Project revision/archive:
Unity editor version:
OS:
CPU / GPU / driver / RAM:
Editor or standalone player:
Graphics API:
Display resolution / render scale:
Quality level / VSync / frame cap:
Seed / world dimensions / voxel size:
Core checks:
Import / mesh checks / build:
Smoke steps completed:
Observed defects:
Profiler/screenshot locations:
```

Use **Unity 6.3 LTS 6000.3.27f1** unless the project version file has been deliberately revised. Install the platform build module needed for the chosen build. A usable Unity license and initial package-download connectivity are required for command-line import/build; use the normal licensed editor setup on the test machine.

Keep the baseline at **0.1 m per voxel**. The default fixture is seed **714103**, **256 × 128 × 256 cells**, or **25.6 × 12.8 × 25.6 m**. Do not change voxel size to improve appearance during the baseline smoke run.

## 2. First import and scene setup

1. In Unity Hub, add the `AshenVoxel` directory as an existing project. Open it with the pinned editor. This is a native Unity project; do not create a second project around its `Assets` directory.
2. Allow Package Manager to resolve URP and Input System and allow script/shader imports to finish. Record the resolved package versions. Stop here if the Console has C# compile errors or unresolved package errors.
3. Run **Ashen Voxel → Create or repair demo scene** if first-import setup has not already created it. This calls `AshenVoxel.Editor.ProjectSetup.EnsureDemo`.
4. Open **Assets/Scenes/ForestArena.unity**. Confirm the scene contains the demo bootstrap and is enabled in the build scene list.
5. Confirm a URP pipeline asset is assigned in Graphics settings and is not overridden by an incompatible asset in the active Quality level. Generated pipeline settings belong under `Assets/AshenVoxel/Settings`.
6. Confirm Input System is enabled in Player settings. Accept any editor restart Unity requires, then reopen the scene.
7. Confirm `Assets/AshenVoxel/Rendering/Resources/VoxelSurface.mat` uses `AshenVoxel/WorldSurface`; its shader must compile. Magenta terrain is a failure, even if gameplay input works.
8. Run **Ashen Voxel → Validation → Run mesh regression checks**. Expect the Console message `Ashen Voxel: all mesh regression checks passed.` Record any thrown failure rather than treating successful import as a substitute.
9. Save the scene and generated project settings. Inspect the resulting changes before committing package locks and generated assets.

The setup command is intended to be repeatable. Run it a second time and confirm it does not create duplicate demo roots, cameras, pipeline assets, or scene entries.

## 3. Executable batch commands

These Bash commands are intended for a machine with the pinned Unity editor and its platform module installed. Set the two paths for that machine. The commands below have **not** been executed as Unity checks in the development container.

```bash
export UNITY_EDITOR="/absolute/path/to/Unity"
export ASHEN_PROJECT="/absolute/path/to/AshenVoxel"
mkdir -p "$ASHEN_PROJECT/Logs" "$ASHEN_PROJECT/Builds"
```

Import, resolve packages, and create/repair the scene:

```bash
"$UNITY_EDITOR" -batchmode -quit \
  -projectPath "$ASHEN_PROJECT" \
  -executeMethod AshenVoxel.Editor.ProjectSetup.EnsureDemo \
  -logFile "$ASHEN_PROJECT/Logs/import.log"
```

Run Unity-side mesh correctness checks after a successful import:

```bash
"$UNITY_EDITOR" -batchmode -quit \
  -projectPath "$ASHEN_PROJECT" \
  -executeMethod AshenVoxel.Rendering.Editor.MeshRegressionChecks.Run \
  -logFile "$ASHEN_PROJECT/Logs/mesh-checks.log"
```

Build a Linux desktop player with the Linux build module installed:

```bash
"$UNITY_EDITOR" -batchmode -quit \
  -projectPath "$ASHEN_PROJECT" \
  -buildTarget StandaloneLinux64 \
  -executeMethod AshenVoxel.Editor.ProjectSetup.BuildLinux \
  -logFile "$ASHEN_PROJECT/Logs/build-linux.log"
```

Or build a Windows desktop player with the Windows build module installed:

```bash
"$UNITY_EDITOR" -batchmode -quit \
  -projectPath "$ASHEN_PROJECT" \
  -buildTarget StandaloneWindows64 \
  -executeMethod AshenVoxel.Editor.ProjectSetup.BuildWindows \
  -logFile "$ASHEN_PROJECT/Logs/build-windows.log"
```

Run the created executable interactively from its output directory; use the build log for the exact output path. A successful process exit alone is insufficient: inspect each log for compile/shader/build errors and verify the expected success message and player artifact. Do not run a second Unity process against an already-open project. Do not add `-nographics` to a visual/shader validation and then interpret that run as GPU validation.

For static project/documentation integrity and the engine-independent core, from the project root:

```bash
cd "$ASHEN_PROJECT"
python3 Tools/check_project.py
python3 Tools/check_documentation.py
python3 Tools/run_core_checks.py
python3 Tools/run_raycast_checks.py
```

The documentation check recursively validates Markdown links to local files, including nested benchmark/preview pages. It does not fetch external URLs or validate claims about Unity behavior.

## 4. First-run smoke test

Run in Play mode first, then repeat the same actions in a standalone build. Clear the Console immediately before Play so old import messages do not obscure new runtime defects.

### A. Loading and default scene

1. Press Play and observe generation/surface-build status. Wait until the forest-ready state appears. Record startup duration separately from steady gameplay.
2. Confirm one player camera, one audio listener, one boss test figure, readable terrain, and visible tree silhouettes. There must be no duplicate scene or runtime roots after stopping and starting Play a second time.
3. Verify the HUD reports the intended 10 cm baseline. Inspect the demo bootstrap/world configuration for `VoxelSize = 0.1`.
4. Remain idle for 30 seconds. The dirty mesh queue should drain rather than grow indefinitely. Record active-candidate count, queued chunks, triangles, and frame timing after settling.

**Fail:** Runtime exceptions, magenta terrain, permanently blank surfaces, duplicate objects/listeners, or queues increasing while no changes occur.

### B. Walking, boundaries, and targeting

1. Use **WASD**, mouse look, **Shift** sprint, and **Space** jump. Walk over gentle voxel steps and into a tree trunk/rock face. Confirm walls stop the player and the camera does not pass through ordinary solid terrain.
2. Hold diagonal movement and jump near a slope. Confirm there is no unexpected launch, stair-climbing into a vertical wall, or repeated sinking.
3. Press **Esc** to release the cursor; click/relock according to the demo's current capture behavior. Confirm editing does not continue while the cursor is released.
4. Press **F** for inspection flight; use **Space/Ctrl** to move vertically. Return to a clear surface and toggle flight off. Verify movement is grounded again.
5. Approach the finite world edge on foot. Confirm the player cannot walk into an invalid region or become permanently trapped. Do not interpret the finite edge as streaming.
6. Press **R**. Confirm the player respawns with full body clearance, including after placing blocks near the normal spawn area.

**Fail:** Walking through a solid wall, trapping after a normal respawn, unbounded fall, or controls continuing to carve while cursor capture is released.

### C. Digging, layers, and chunk seams

1. From clear ground, hold **LMB** to dig a small crater. Use the mouse wheel to vary brush radius. The hit and excavation should follow current matter, even when visible surface rebuilding is pending.
2. Dig vertically through grass/litter/soil and into stone. Inspect the slice and record a screenshot. Not every column must contain mud; the generator applies moisture-dependent materials.
3. Dig through a chunk boundary. At 10 cm and 16 cells per chunk, boundaries lie at multiples of **1.6 m** from world origin. In Editor, inspect player/world coordinates or enable the available scene gizmos to choose a boundary deliberately.
4. Sweep across the boundary and inspect it from both sides. Repeat near a corner shared by four horizontal chunks. There must be no permanent missing faces, stale wall, or duplicated seam.
5. Dig below the player while walking away. Collision should follow authoritative cells rather than waiting for a MeshCollider. Verify the player lands on remaining matter and can escape the crater.
6. Continue down to the protected bottom layer. The tool must preserve the bedrock boundary rather than opening an unbounded fall.

**Fail:** Permanent holes at chunk borders, targeting an already-removed surface, terrain collision persisting indefinitely after removal, or tool deletion of the protected bottom layer.

### D. Limited resources and granular gravity

1. Choose **1 sand**, **2 dirt**, **3 stone**, and **4 wood** in turn. Use **RMB** to place each onto a visible target at a safe distance. Verify the material selection and brush size update in the HUD.
2. Dig beneath the arena's loose sand mound. Watch unsupported grains fall and settle. The active queue may grow during disturbance but should decay after the pile comes to rest.
3. Place a sand ledge over a cavity using a stone/wood support, then remove the support. Confirm matter moves downward and piles rather than disappearing or duplicating visibly.
4. Walk beside the moving pile. Verify movement remains responsive while surface work catches up.
5. Cut a tree base and inspect the result. **Current expected limitation:** wood/leaves do not yet form falling rigid islands. Do not file this documented missing structural solver as a newly discovered collision defect; do record whether granular matter behaves correctly around it.

**Fail:** Sand stays suspended after an ordinary support edit, visible repeated matter duplication, a permanently nondecaying queue without external edits, or uncontrolled frame stalls during a small pile test.

### E. Paused simulation

1. Press **P** to pause granular simulation.
2. Create a suspended sand patch or remove its support. Edits and mesh rebuilding should still work; grains should remain still while paused.
3. Wait several seconds. Confirm the active queue is retained rather than discarded.
4. Press **P** again. The disturbed matter should resume settling without requiring a second edit.
5. Repeat while holding no other controls so the result cannot be confused with repeated placement.

**Fail:** Pausing loses pending granular work, rendering stops rebuilding, or resume leaves an unstable pile asleep indefinitely.

### F. Boss terrain-attack harness

1. Stand on clear arena ground with room to retreat. Press **B**.
2. Observe the warning ring and wind-up. Move out of the marked region before impact.
3. Confirm the attack makes a crater in the telegraphed region, neighboring loose matter responds, and the actor returns to its idle pose.
4. Trigger three attacks after each previous attack completes. Record mesh queue age qualitatively, HUD timing, and any long frame spikes. The boss is a destruction harness, not a complete combat encounter.
5. Trigger a slam near an existing crater and while the player stands near its edge. Confirm the player remains controllable and collision follows changed ground.
6. During a wind-up, load a saved world as described below. The old attack must be canceled/reset rather than continuing to carve the replacement world at stale coordinates.

**Fail:** Attack coroutine exceptions, old attacks continuing after world replacement, unbounded queue growth through repeated attacks, or collision blocking an already-excavated passage.

### G. Save/load, including unsettled matter

The demo snapshot is stored as `ashen-demo.avxl` under Unity's `Application.persistentDataPath`. Locate the actual folder for the test machine; Editor and player paths may differ by product settings. F5 overwrites this local demo snapshot, so use a disposable test save.

1. Make a recognizable crater and place a small stone marker. Press **F5** and confirm the saved status.
2. Change the crater/marker visibly. Press **F9**. Confirm the saved terrain returns, the player has safe clearance, and the terrain rebuild queue drains.
3. **Mid-fall fixture:** pause with **P**, suspend a sand patch over a cavity, save while unsupported, then unpause briefly so some sand moves. Load the saved snapshot. On resume, allow the bounded allocated-chunk wake scan to reach the saved region; unstable saved grains must become active and settle **without a new edit**. The full default world scan spans millions of cell candidates and may take many ticks; record its completion/latency rather than requiring instant wakeup. A material snapshot may reawaken work in a different order; exact animation/replay equality is not required unless the format explicitly persists that schedule.
4. Repeat loading while simulation is paused. Confirm pause state remains intentional and that pressing **P** resumes the loaded unstable pile. A load must not silently discard its pending work.
5. Save twice to exercise replacement of an existing file. Verify the second save loads, rather than leaving only a temporary file.
6. With a disposable copy of the save, stop Play, truncate or alter that copy, and test loading it. The demo should report an error while retaining a usable current world. Restore the valid save afterward. Do not corrupt a save you intend to keep.
7. Load during a boss telegraph and during crater formation. Confirm no stale attack survives the world replacement and the boss relocates consistently with the loaded world.

**Fail:** Lost saved edits, frozen unsupported grains after resume, exceptions that leave no playable world, unsafe respawn, or an old attack carving the loaded world unexpectedly.

### H. Lighting, exposure, and appearance

Avroid is the primary appearance reference; the earlier Souls-like dark fantasy mood remains secondary. This smoke pass checks that the demo has a coherent readable baseline, not that it already matches a finished reference game.

1. Capture the same three views: arena overview, forest-ground cut, and close trunk/foliage silhouette against sky/fog.
2. Check that stone, organic ground, wood, and leaves remain distinguishable in shadow. The warning ring and boss silhouette must be readable without making all terrain flat and bright.
3. Inspect sun-facing and shadow-facing crater walls. Confirm directional lighting and shadow casting work and that triangles are not visible from only one incorrect side.
4. Walk slowly and rotate through a canopy silhouette. Look for shimmer, harsh grain, shadow acne, detached shadows, or fog abruptly hiding nearby terrain.
5. Compare Editor and standalone appearance at the same quality/resolution. Bloom and tone mapping should not vanish because a camera or pipeline setting was omitted.
6. Record suggested art changes separately from correctness failures. Preserve the 10 cm material grid while adjusting exposure, palette, roughness, fog, or shader grain.

**Fail:** Missing shader, absent shadows due to an integration defect, severe exposure clipping that prevents play, or a standalone build materially missing its intended rendering effects.

### I. Stats and frame-time capture

1. Record idle HUD values and those during a small dig, sustained sand motion, and a boss impact.
2. Confirm active candidates and queued chunks react to edits and eventually decay. Triangle count may stay higher after destruction because new interior surfaces are exposed.
3. Capture Unity Profiler CPU and GPU data in a development player. Separate generation, edit application, simulation, mesh construction/upload, and rendering.
4. Do not interpret the HUD's smoothed FPS or current sim/mesh timings as p99 data or total GPU cost. See [PERFORMANCE.md](PERFORMANCE.md) for the benchmark contract.
5. Stop and restart Play twice, then inspect object counts and native memory for persistent growth. Scene teardown must release owned materials, meshes, volume profiles, and runtime roots.

**Fail:** Growing idle work, recurring large allocations in ordinary steady play, native-object leaks across repeated starts, or any claim of target FPS without a hardware/build/scenario record.

## 5. Completion record

Mark each section **Pass**, **Fail**, or **Not run**. Attach logs for import/mesh/build and screenshots/profiler captures for runtime findings. A successful smoke run is a practical first demo milestone; it does not establish streaming, rigid collapse, fluid chemistry, production boss AI, or final graphics parity with Avroid.

## Tree/color follow-up smoke test

Open TreeWorkshop and press Play. Confirm four species build without missing scripts, pink surfaces or clipped crowns at their bundled sizes. Switch to a single profile, orbit through its crown and inspect bark/foliage; verify pale marked birch, spreading oak, redwood taper/root flare and conifer whorls. Change the seed then regenerate. Leave it unchanged and regenerate again: the same specimen should return.

Change branch droop, leaf form, density and palette through a profile. Changes should apply only after regeneration; changing Species must not silently erase artist values. Apply species defaults explicitly and verify the reset is deliberate. Set oak fruit probability, regrow and inspect attached brown acorn-like samples; zero probability or zero foliage should remove fruit. Observe any generation-limit status when deliberately creating a large specimen.

In ForestArena, inspect differently colored cells of the same material before/after digging across chunk seams. Save while loose colored matter falls, change the world, then load and allow its bounded wake scan to finish. Colors and material identity must remain paired. Move the sun/camera to distinguish intrinsic pigment from lighting. Verify isolated tint edits do not start granular simulation. Run the mesh color/occlusion tests in Unity.

These are unexecuted Unity checks. Actual-data CPU samples and standalone mapping/geometry checks are separate evidence.

## Mature redwood follow-up

Use the bundled Redwood profile in Single Tree layout. Confirm the complete mature specimen fits vertically, orbit and Home framing work, and no generation-limit warning appears at the default75 m setting. Middle-drag to pan down to the base, then zoom in: broad asymmetric root shoulders should merge into the trunk, with narrower wandering vertical bark ribs visible in silhouette and shadows at10 cm. Home must restore the original target after panning. Look for horizontal segment rings, repeating gear teeth, detached shells, bright color seams or inflated branch joints.

Set Bark Relief to zero, regenerate, and compare the same seed/camera; this must change physical bark occupancy rather than only pigment. Restore defaults afterward. Test120 m and younger-age profiles; inspect the full crown and root footprint, record any explicit limit, and avoid interpreting a successfully framed scene as proof of a frame-rate target.

Capture generation time, mesh queue drain time, material/tint/native mesh memory, triangle count, draw calls and steady orbit CPU/GPU timings separately. A mature redwood is intentionally much larger than a normal arena tree. Repeat root/branch excavation and save/load on a disposable world; wood remains static until the separate rigid-island system exists.

## WorldExplorer follow-up

Use **Ashen Voxel → World → Open world explorer** and Play. The default field
must say it is analytic, not diffusion. Inspect a multi-kilometer summit and fly
rapidly across positive/negative global coordinates: terrain should retain
coverage while LOD plans rebuild, camera-relative meshes should not jitter, and
resident/queued tile counts should stay bounded. Test32 km and100 km view ranges;
record GPU/CPU timing, depth artifacts, horizon silhouettes and shadow limits.

Open the bundled neural and Ceteviles samples through the same menu. Imported
dimensions/aspect must be preserved by default. Change horizontal width and
vertical scale separately, regenerate and inspect their independent effects.
Forest override must preserve source biome IDs and leave water as water.

Start the optional model service using its documented job setup. Enable remote
diffusion in the explorer and rebuild. The HUD must distinguish provisional
rough far terrain from actual model-ready regions. Disconnect/fail the service,
change identity, then reconnect: failures must remain visible, pending queues
must remain bounded and stale data must not replace a different world. No
synchronous HTTP or model inference should run inside per-vertex Sample calls.

Press P for the explicitly separate finite inspection region. A model-backed
inspection must wait for complete data. Dig at high altitude and check grid
truth; P discards these temporary edits and returns to the vista. This is not
a persistence test or a seamless near/far transition. Run the dedicated
WorldExplorer development build and profile the same route. These native checks
remain unexecuted until Unity is available.
