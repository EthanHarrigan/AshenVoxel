# Ground-cover workshop checks

Run from the project root with Python3 and .NET8 SDK:

```sh
python3 Tools/run_groundcover_workshop_checks.py
```

The runner compiles the actual new workshop, orbit camera, Editor menu/Inspector
and engine-independent core against explicit Unity API substitutes. It executes
the actual generation iterator for all ten plant and five rock specimens,
checks material/appearance repeatability and seed variation, validates displayed
cell counts against the complete world, tests focus wrapping and owned-object
cleanup, and checks the serialized scene's script GUID. The six-rock shape study
checks actual resolved profiles and requires six different occupancy hashes
after removing translation and color. It also exercises layout changes,
material/shape overrides and the Inspector comparison button.

The output report is `Docs/groundcover-workshop-validation.json`. Tracked runtime,
Editor, core, test, scene and meta files are fingerprinted before and after the
run. `bin` contains temporary compiled output and is excluded from delivery.

The substitutes do not run Unity, serialize a scene, render the resulting chunk
meshes or compile HLSL. Native Unity import, Inspector behavior, camera motion,
lighting, label placement and player-build performance remain separate checks.
