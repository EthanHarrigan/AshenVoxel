# Real-asset exploration bootstrap smoke

Run `python3 Tools/run_exploration_smoke_checks.py` from the project root with a
.NET 8 SDK. This compiles the actual bootstrap, settings, player, vista partials,
visual water, remote/live adapter and population streamer, then drives their
startup and frame methods with numerical Unity substitutes.

The harness reads the actual Exploration scene/settings GUID references and all
bundled macro/fine height/climate, biome, metadata and 99-model prototype files.
It does not replace the world with a test plane. Live refinement stays disabled;
startup must never create an HTTP client. The coroutine is driven until its
terrain coverage and published solid-object footprint unlock the player.

It checks walking and collision-enabled flight in that learned scene, no-edit
inputs, F1 settings, clamping, saved options in an isolated temporary directory,
camera/instance rebasing, private pipeline/material ownership and shutdown.
Negative cases require clear failures for missing world data or render pipeline.
Those two expected failure handlers intentionally log exceptions in the test.

Mesh uploads and draw calls are substitutes. They validate counts and bounds
and discard fake GPU array storage; they do not compile shaders or run Unity.
The clock is simulated and planner tasks are allowed cooperative wall time.
Startup frame counts and elapsed container time are **not native Unity startup
or gameplay FPS measurements**. Native import, serialized scene behavior, GPU
rendering, pointer/input and target-machine performance remain separate gates.

The report is `Docs/exploration-smoke-validation.json`, with actual source and
asset hashes. Temporary assemblies/configs live under `bin`, outside delivery.
