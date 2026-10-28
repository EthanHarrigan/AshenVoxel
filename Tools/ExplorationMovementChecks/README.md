# Exploration movement checks

Run `python3 Tools/run_exploration_movement_checks.py` from the project root
with a .NET 8 SDK installed. No NuGet restore or Unity installation is required.
The runner compiles the actual engine-free motor and actual Unity input/view
controller with explicit Unity substitutes, then compiles engine-free data
against C# 9/.NET Standard 2.1. It records source hashes in
`Docs/exploration-movement-validation.json`.

The checks cover 30/60/144 Hz metric walk/sprint/diagonal motion, continuous
inclines, jump apex and airtime, gravity, one-cell wood barriers, wall sliding,
stable corners, passable Leaves/Fungus/Petal, small/higher steps, hard-prop
supports and ceilings. Other cases cover collision-enabled fast flight,
explicit noclip and safe flight exit, streaming holds, bounded spawn search,
global double coordinates, finite budgets, input focus and camera-height
smoothing. The warmed stationary motor check measures managed allocations.

`IExplorationObstacles` is an obstacle-only global-meter interface. Its readiness
query must include the player's footprint. `BlocksBody` receives conservative
upright body bounds and must ignore soft tissue; support queries return the
highest hard top in the requested vertical interval and circular footprint.
The adapter samples terrain separately from the continuous source field.
Renderer rebasing occurs only after the controller produces its global eye.

Cell sweeps stay below half of a 10 cm cell. Reaching the motor work limit
returns an observable time remainder; the controller carries accepted input
time. An intentional streaming/input pause clears old input so loading cannot
cause a catch-up launch. A single input sample longer than 250 ms is capped and
reported separately, avoiding old input after a very long application stall.

These are source-level execution checks, not native Unity controller, physics,
shader, pointer-focus, input-device or player-performance verification. The
simple upright prop body is conservative rather than a full rigid capsule
solver. Terrain editing, swimming, moving rigid props and animation are outside
this basic exploration controller.
