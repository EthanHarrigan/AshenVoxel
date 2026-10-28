movement-feel-1 — isolated traversal candidate, NOT INSTALLED

Two changed runtime files are under RuntimePatch. Original readable preimages
are under Baseline, with exact hashes and current main comparison in manifest.json.
Keep existing .meta GUIDs. No main-project, scene, world, pack, shader, native
process or patch-queue changes were made in this lane.

Behavior
--------
Traversal remains disabled by default (Settings.Traversal == null). The existing
127-check baseline is preserved. Opt in using ExplorationController's Inspector
EnableTraversalExperiment checkbox or SetTraversalExperiment(true). The public
API accepts a tunable ExplorationTraversalSettings. SetTraversalExperiment(false)
restores the legacy movement path. Existing physical settings are copied intact.
Applying replacement movement settings uses that settings object's Traversal mode.

WASD: bounded acceleration; Shift: sprint; releasing Shift eases toward walk speed.
Air retains horizontal momentum with limited steering; no-input air has no drag.
Space is a press event: .10s jump buffer and .09s coyote opportunity, consumed once.
Ctrl: crouch; above 5 m/s on ground it slides with 3 m/s2 braking and no speed boost.
Below the threshold it becomes ordinary slower crouch movement. Slide jumping
retains horizontal momentum. Full standing clearance is required before standing
or entering flight. F/N and 40/160 m/s flight remain as before. No FOV effects,
head bob, wall running, dash, stamina or input rebinding was introduced.

Horizontal velocity lives inside the consumed-time substep loop. Its current
magnitude participates in collision budgeting even when the new target is slower.
Rejected axes lose stored momentum while accepted wall tangents remain. It uses
the existing axis solver, not inferred collision normals. Exact 10 cm contact and
continuous slope classification remain separate. Under a loading hold, momentum,
queued jump and coyote opportunity are cleared and no old input time is retained.
Focus loss, pointer release, input disable, respawn and fly transitions clear
traversal intent. Changing the tuning object also clears intent. An interrupted
physical airborne fall can continue; loading holds cancel positive jump velocity.

Reproduce CPU checks
--------------------
From ReplaySource with the installed .NET 8 SDK and Python:
  $env:DOTNET_TieredCompilation='0'
  python Tools/run_exploration_movement_checks.py
Expected: 242 checks and C#9/.NETStandard2.1 data compile. No NuGet restore needed.
The actual controller executes with explicit Unity substitutes. These do not
verify Unity input devices, rendering, native loading, GPU cost or subjective fun.

Real Unity API compile (without launching Unity):
  python Tools/check_voxel_unity_api.py --controller-only --reference-project C:/Users/ethan/Downloads/AshenVoxel
This uses locally installed Unity6000.3.27f1 APIs and the reference project's
InputSystem/URP assemblies. Four complete dependency assemblies and the actual
controller compile; the rest of Exploration is explicitly excluded. The full
copied Exploration compilation failed on unrelated root/wildlife namespaces;
that failure is retained. The candidate does not claim full native import.

Integration and recovery
------------------------
1. Coordinator/movement owner reviews runtime.diff and manifest.json. Check each
   live target hash against its preimage hash immediately before any install.
   A mismatch requires a narrow merge/retest; do not overwrite concurrent work.
2. Preserve a new readable live backup and existing .meta files. Integrate only
   the two runtime sources, merging the existing ResetObserverMotion calls.
   ReplaySource is a CPU fixture, NOT an Assets overlay for the main project.
3. Run the whole project's required checks and native import using the owner's
   controlled resource window. Keep the experiment disabled for the first check.
4. Follow PLAY_ROUTE.txt with identical world/route/settings for A/B testing.
5. Recover by restoring matched readable preimages ONLY if live files still match
   the candidate hashes; otherwise merge out the narrow diff and preserve edits.
   Never restore the complete replay source tree over the project.

Limits and choices
------------------
This is a tunable baseline experiment inspired by responsive traversal, not exact
Titanfall tuning. Air direction changes may dissipate speed; there is no automatic
bunny-hop boost. Slide has no burst and ends below its threshold; hill physics
still use the existing steep-slide policy. Crouch is a conservative upright AABB,
not a capsule animation system. Disabling traversal while crouched under a ceiling
holds position until tall clearance exists; it never expands into a solid.
Jump timing/collision transitions are bounded by solver substep sampling. Timer
windows need native route evaluation. Existing slope descent camera behavior and
the separate uninstalled cliff-slide experiment were not silently adopted.

Evidence
--------
Baseline shim initially lacked ResetObserverMotion used by the current controller;
the local shim was repaired before the 127 baseline checks ran. It changes no
runtime API. Candidate-first stream-boundary assertion incorrectly held a jump
press event every tick for a fixed duration; it was replaced by observation at
the first actual hold. An overspeed regression initially replaced the tuning
object, correctly clearing velocity; the corrected case preserves that object
while changing physical target speeds. Both diagnostic failures are retained.
Default tiered-JIT benchmark results varied during tier promotion; final measured
CPU fixture explicitly disables tiered compilation. No Unity FPS inference follows.
