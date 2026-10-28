# Exploration sky, atmosphere and post processing

Implemented candidate on the main project, based on `76a8fc5d88299575f6468e5a49b16f0f952f405b`.
Native appearance and frame-time acceptance remain open. The ordinary URP mesh,
shadow, depth, normal, motion and instancing paths remain in use.

## Editing the appearance

Open **F1 → Sky, atmosphere and post processing**. Clear day, Warm dusk and
Overcast set coordinated sun, sky, cloud, fog, ambient and grading values. They
do not change terrain quality, reach, generation, material identity or matter.
Changes apply live. **Save these options** persists the complete environment;
**Reset** restores the scene's authored values. Presets are starting points,
not a continuous weather or day/night simulation.

The main controls cover sun elevation/azimuth/intensity, shadow fill, cloud
coverage/density/detail, haze, height fog, exposure, contrast, saturation, bloom
and tone mapping. **More atmosphere controls** exposes sun diameter, sky/disc
brightness, cloud scale/wind/speed, fog altitude/falloff/start/maximum opacity,
sunward scattering, warmth, tint, bloom threshold/spread and optional vignette.
**Edit sky and lighting colours** exposes the palettes. Full authoring controls
are also in `Assets/AshenVoxel/Settings/ExplorationSettings.asset` under Atmosphere.

Sun diameter defaults to 0.53 degrees and uses pixel-footprint smoothing. The
disc and halo are attenuated by clouds. Cloud detail Off, Layered and Detailed
use at most 0, 6 and 10 procedural value-noise evaluations per sky pixel.
Clouds are an artist-directed shell: no fly-through volumes, world cloud shadows,
moon or stars. Ordinary float-time precision limits still apply to long sessions.

Height fog integrates density along each camera-to-surface ray. Density is
extinction per metre at the reference altitude; the F1 control displays it per
kilometre. Falloff is inverse metres (displayed per kilometre); zero gives
uniform distance fog. Reference altitude is global metres, independent of the
floating origin. The initial clear distance is excluded from the integral.
Maximum opacity is a cap. Disabling Height fog uses the existing Unity fog mode;
disabling Atmospheric fog removes both. Fog/sky matching uses the same linear
horizon color and sky exposure, with a separate artistic sunward fog glow.

Post exposure changes both sky and surfaces. Sky brightness only changes the
sky and matching fog. ACES, Neutral and None are available. Grading can be disabled
while retaining the camera stage used by SMAA/DLAA. Blur, chromatic aberration,
film grain and depth of field are not added. Bloom retains ordinary URP work;
its GPU cost must be measured, even though the sky/fog add no render targets.

## Implementation and ownership

- `ExplorationAtmosphere.cs`: settings, finite/range validation, three presets
  and an independent version-1 options snapshot. Older options keep authored
  new controls; their old fog/sun preferences still load. Unknown environment
  versions are ignored. No voxel snapshot or terrain format changes.
- `ExplorationBootstrapEnvironment.cs`: F1 controls and reset. Bootstrap saves
  the environment alongside existing options and updates lighting live.
- `ExplorationEnvironmentRuntime.cs`: one owned state snapshot, raw working-space
  color uploads, shared horizon, directional light/ambient fill and fog globals.
  Rebase Y is updated after the camera/source publication; globals and RenderSettings
  are restored on teardown. No per-frame texture, material or LUT construction.
- `ExplorationVisualQuality.cs`: one owned volume/profile, cached overrides and
  component cleanup. Applying grading does not reset the upscaler's AA choice.
- `ExplorationSky.shader`: continuous shell coordinates, filtered noise octaves,
  HDR sun and art-directed haze/twilight. Existing shader name and GUID retained.
- `Rendering/AshenAtmosphere.hlsl`: shared analytical fog replacing each prior
  fog call in ground, ordinary props, both forest impostor paths and water.
  It falls back to URP MixFog outside the active environment.

The cloud task performed actual shader/include implementation, numerical checks
and primary-source research. The local task supplied source text because the
cloud had no GitHub connection, then integrated and reviewed the returned code.
Cloud thread: `01a18431-2b54-709a-a452-9f27e4159027`; reference local task:
`01a1842f-d72f-7bc3-952b-2330a38511a8`. No nested local worker or duplicate project
checkout was created. [Research, alternatives and cloud limits](AtmosphereEvidence/cloud-research.md).

Small scattering LUTs are the next physical-atmosphere experiment if native
comparisons justify them. HDRI trades dynamic weather control for authored detail;
Guerrilla-style volumes need additional data, sampling and reconstruction budgets.
Published timings from other engines are not predictions for this game.

## Checks and remaining gates

Lightweight checks (no Editor launch):

```text
python -X utf8 Tools/check_atmosphere.py
python -X utf8 Tools/check_voxel_unity_api.py --report Docs/AtmosphereEvidence/unity-api.json
python -X utf8 Tools/check_atmosphere_editor_api.py
python -X utf8 Tools/check_project.py
python -X utf8 Tools/run_mesh_checks.py
```

Numerical checks require NumPy. They compare fog to independent quadrature,
stress float32 arithmetic and origin shifts, verify periodic cloud coordinates,
working-space default tint and source integration contracts. They do not execute
HLSL. [Evidence and source identities](AtmosphereEvidence/README.md).

After approval for computer control, use **Ashen Voxel → Validation → Atmosphere
shaders and preset captures** outside Play. The bounded fixture compiles the six
affected shaders, checks native settings roundtrips/ownership, and renders three
960×540 sky-only images with stationary clouds. It loads no terrain data.
Output: `Logs/AtmosphereGraphics`. Batch entrypoint:
`AshenVoxel.Exploration.Editor.AtmosphereGraphicsCheck.RunBatch`.
Run the existing native mesh regression menu as well. Do not launch a second
Editor against this project while it is already open.

The bounded fixture is prepared, not executed. It cannot establish full-scene
quality or FPS. With approval, use one Exploration Play session to compare all
presets from the same forest, mountain and shoreline positions; inspect horizon,
sun behind clouds, zenith, below-horizon lighting, nearby fog and fly-up behavior.
Cross a vertical floating-origin boundary and inspect both near and distant trees.
Check SMAA, DLAA/DLSS, grading off, save/restart, reset and Stop cleanup.
Record resolution and frame-time p50/p95/p99, hitches, system commit and VRAM
before/after. Keep these measurements tied to the recorded source hashes.
