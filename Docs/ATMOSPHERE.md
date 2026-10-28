# Exploration sky, atmosphere and post processing

The current candidate replaces the rejected flat cloud shell with a finite cloud
volume that can be viewed from below, inside and above. It uses the existing URP
renderer and leaves terrain generation, source resolution and 10 cm matter intact.
Native shader, color, fog and mesh checks pass; final appearance and full-scene
performance remain separate gates.

## Appearance reference

The user selected MishMash's [Generating Massive Micro Voxel landscapes
(3,000m+ mountains)](https://www.youtube.com/watch?v=xGkWWfO87no), **14.5–20 seconds**.
The cut near 15.5 seconds shows the “Ultra” clouds from above: an opaque sea of
rolling tops, shaded troughs, warm lit edges, and mountains emerging through it.
[Actual reference frames and provenance](AtmosphereEvidence/reference/frame-provenance.json)
were inspected. This is a sky/light reference; the diagnostic terrain captures
do not claim to reproduce that game's terrain. Audio was not inspected or changed.

## Controls

Open **F1 → Sky, atmosphere and post processing**. Clear day, Warm dusk and
Overcast coordinate sun, sky, clouds, fog, ambient fill and grading. They do not
change terrain quality, reach, generation or matter. Changes apply live.
**Save these options** persists the environment; **Reset** restores authored values.

Cloud quality is Off, Layered or Volumetric. Advanced controls include the cloud
base and thickness in global metres, horizontal repeat length, vertical scale,
extinction, erosion, view distance, view/light sample counts, scattering and
distance haze. Wind direction and speed animate cloud coordinates. These are
artist controls, not a weather simulation. No moon, stars or cloud shadows on the
ground are added. Native motion quality and long-session float-time precision
remain unverified.

Height fog integrates extinction along the camera-to-surface ray. Density is
inverse metres at the reference altitude; F1 displays it per kilometre. Zero
falloff gives uniform distance fog. Reference altitude is global, independent of
floating origin. Fog start excludes the initial distance; maximum opacity caps
the result. Disabling Height fog restores the existing Unity fog mode; disabling
Atmospheric fog removes both. Fog and non-solar sky share the working-space
horizon color; the sunward fog glow is separately art directed.

Post exposure affects surfaces and sky. Sky exposure affects sky, clouds and
matching fog. ACES, Neutral and None are available. Turning grading off preserves
the SMAA/DLAA camera stage. Bloom uses ordinary URP processing. No depth of field,
grain, chromatic aberration or blur was added.

## Rendering and ownership

- `ExplorationAtmosphere.cs`: finite/range validation, presets, options version 2.
  Version 1 keeps its existing controls and receives new volume defaults. Unknown
  future versions are ignored. Voxel snapshot and terrain formats are unchanged.
- `ExplorationEnvironmentRuntime.cs`: shared horizon, directional/ambient light,
  fog globals and double-precision absolute camera phase. Updated after rebases.
  Owns the cloud resource and draw; restores globals and RenderSettings.
- `ExplorationCloudNoise.cs`: lazy immutable 64³ RGBA8 **linear** texture, repeat
  addressing, trilinear filtering and seven mip levels. Offline AVCN64-v1 payload
  is 1 MiB, about 1.143 MiB GPU storage with mips. CPU readability is discarded.
  No per-frame texture generation. Missing/unsupported data uses Layered clouds.
- `Resources/ExplorationCloudVolume.hlsl`: shared finite-layer ray integration,
  rolling tops, a faint fringe, fine boundary warping, early termination and
  premultiplied in-scattering. Opacity obeys Beer–Lambert. Primary steps are
  24–64 (default 64); sunlight uses 3–5 probes, with fine density at the nearest
  probe and coarse density farther away. Maximum 512 volume texture samples plus
  one depth sample per pixel; this is not a timing guarantee. Lighting is approximate.
- `ExplorationCloudVolume.cs` and `ExplorationCloudComposite.shader`: one owned
  camera-enclosing mesh/material on layer 29, drawing only for its perspective
  camera. It clips each ray to opaque depth and blends after water at queue 3100.
  The sky skips its volume march for that draw. No additional cloud render target,
  temporal history or renderer feature is introduced.
- `ExplorationRenderer.asset` now copies depth **after opaques**. A depth texture
  requested only after transparents would be stale for this draw. Runtime checks
  the configured URP renderer data before enabling the compositor.
- `ExplorationSky.shader`: existing shader identity/GUID, angular HDR sun,
  art-directed sky and Layered fallback (six value-noise evaluations).
- `Rendering/AshenAtmosphere.hlsl`: one shared fog call per existing ground,
  ordinary prop, near/far forest and water shading path.
- `ExplorationVisualQuality.cs`: one owned cached volume/profile; grading updates
  preserve the selected AA mode and dispose their resources.

The cloud layer is normally above sea level, so drawing after transparent water
correctly covers the sea from above. This is not general transparent sorting:
foreground transparent objects, or water intersecting clouds below sea level,
need a separate depth policy. Orthographic/unsupported pipelines retain the
background fallback. Cloud sample limits are operation bounds, not FPS guarantees.

Plain ShaderLab Color properties receive authored sRGB via SetColor. HDR Color
properties and globals receive explicit working-space vectors. Native GPU probes
caught and corrected a double color conversion in the first candidate. Capture
targets remain HDR through post processing and are encoded to sRGB only for PNG.
An LDR target clips bright sun values; raw HDR readback without encoding looks dark.

The cloud agent wrote actual shader code, the deterministic noise generator,
numerical checks and revisions in thread `01a18431-2b54-709a-a452-9f27e4159027`.
The local task supplied source because cloud GitHub access was unavailable,
integrated the returned code and ran native validation. Reference task:
`01a1842f-d72f-7bc3-952b-2330a38511a8`.
[Research and historical handoff](AtmosphereEvidence/cloud-research.md).

## Validation

The user authorized bounded shader/capture and mesh checks, **no Play**. The
Edit-mode fixture compiles seven production shaders, probes actual GPU color
uploads and height fog against independent quadrature, tests settings migration
and resource ownership, and captures 12 day/dusk/overcast views at 1600×900 from
below and above the layer. It alternates near/far opaque geometry to detect late
depth, and verifies that changing post exposure affects actual pixels. The plane
has the DepthNormals pass required by existing SSAO. StandardRequest refreshes
URP grading for each Edit-mode capture. Native mesh regression runs in the same batch.

Terrain context consists of two bounded meshes sampled from the shipped
source/climate/biome assets. It starts no streaming, services, population or
physics. The coarse diagnostic mesh is not evidence of full-scene terrain detail
or 10 cm matter rendering. Captures identify the camera and source separately.

Batch entrypoint:
`AshenVoxel.Exploration.Editor.AtmosphereGraphicsCheck.RunBatch`.
Menu: **Ashen Voxel → Validation → Atmosphere shaders and preset captures**.
Do not launch a second Editor against an already open project.

Lightweight checks:

```text
python -X utf8 Tools/check_atmosphere.py
python -X utf8 Tools/check_volume.py
python -X utf8 Tools/check_voxel_unity_api.py --report Logs/atmosphere-runtime-api.json
python -X utf8 Tools/check_atmosphere_editor_api.py --report Logs/atmosphere-editor-api.json
python -X utf8 Tools/check_project.py
```

[Evidence, preserved rejections and source identities](AtmosphereEvidence/README.md).
Full Exploration Play, moving-camera/cloud stability, shoreline/forest interaction,
DLAA/DLSS, save/restart UI, frame-time p50/p95/p99 and memory/VRAM deltas remain
unexecuted. No Play approval was inferred from permission for bounded captures.
