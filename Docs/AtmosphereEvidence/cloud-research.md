# Cloud implementation and review

The original shell delivery below is historical (`12317c9`). The user rejected
its flat appearance and requested the thick, soft-edged cloud sea in MishMash's
video xGkWWfO87no at 14.5–20 seconds. The same cloud thread,
`01a18431-2b54-709a-a452-9f27e4159027`, then produced a deterministic 64³ RGBA noise
generator, finite volume integration, depth-composite shader and CPU checks,
followed by density/light revisions and review of the locally combined result.
Actual source text was exchanged because its private repository access was absent.

Current integration uses an opaque core with a faint, broad fringe and fine
three-dimensional boundary warp. Local native comparisons selected uniform 64
primary samples to reduce cloud-top bands. Cloud review prompted a ±12%-thickness
warp clamp and matching fine density at the nearest sunlight probe. The remaining
light probes are approximate coarse shadows. Maximum cost is 512 volume texture
samples plus one depth sample per pixel, seven noise mips (~1.143 MiB), one owned
camera-enclosing mesh/material and no cloud history or additional render target.
That bound is not a GPU timing result. Motion stability and full-scene performance
remain untested. The cloud agent did not run Unity; the local task ran the native
checks and captured the [current evidence](README.md).

## Historical first shader delivery

Source basis: supplied ExplorationSky.shader from reported local main
76a8fc5d88299575f6468e5a49b16f0f952f405b. The repository itself and AGENTS.md
were unavailable; this is a complete-file handoff, not a verified Git diff.
Only the shader and new include are integration changes. C# and .meta untouched.

## Recommendation and evidence

Use this editable analytic sky plus layered clouds as the initial baseline.
This is an art-directed approximation, not a physical scattering implementation
or Guerrilla volumetric reproduction. It provides coherent sun/cloud extinction,
periodic animated cloud coordinates, twilight/night response and explicit bounds.
Native appearance acceptance is still required; no Avroid reference claim is made.

| Option | Strength | Tradeoff and decision |
| --- | --- | --- |
| Artist-directed analytic sky | Palette and sunlight directly editable; no caches | Approximate scattering; chosen baseline |
| Hillaire-style small LUTs | Physical atmosphere/altitude and aerial perspective | Additional resources, invalidation and pipeline integration; staged upgrade |
| HDRI | Rich authored detail, little arithmetic | Texture storage and baked weather/sun coherence; rejected for dynamic baseline |
| Layered clouds | Bounded arithmetic, editable coverage and lighting | No true volume, fly-through or terrain shadows; chosen baseline |
| Guerrilla-style volumetrics | Rich cloud shapes, lighting and depth | March/sample budgets, authored data and temporal or spatial reconstruction need measurement; deferred |

Primary sources consulted:

- Hillaire 2020 publication abstract:
  https://diglib.eg.org/items/8a3e5350-18b3-46bd-9274-3add5af88c75
  Supports small LUTs, dynamic composition and aerial perspective. The linked
  35.99 MB PDF could not be fetched due to service size limit; its equations and
  performance tables are not claimed as reviewed.
- Horizon 2015:
  https://www.guerrilla-games.com/read/the-real-time-volumetric-cloudscapes-of-horizon-zero-dawn
  Discusses editable cloud modeling/lighting and a 2 ms target, not a cost forecast here.
- Nubis Evolved:
  https://www.guerrilla-games.com/read/nubis-evolved
  Describes sky/environment/VFX extensions and temporal reconstruction limitations.
- Nubis cubed:
  https://www.guerrilla-games.com/read/nubis-cubed
  Describes voxel data, distance-field acceleration and lighting sampling work.
- Unity 6.3 URP includes:
  https://docs.unity.com/en-us/engine/6000.3/manual/materials-and-shaders/shaders/writing-custom-shaders-urp/use-built-in-shader-methods/import

These publication pages were read; the full Nubis presentation PDFs were not.
The old Frostbite EA publication URL returned 404. Do not cite it as reviewed.
No publisher's measured cost is an AshenVoxel prediction.

## Integration contract

Shader name/path and all requested property names/defaults are preserved.
Retain existing ExplorationSky.shader.meta. Create one new include meta via the
project's normal import process; do not replace unrelated GUIDs.

_SkyTint retains tint semantics, not literal output zenith RGB. Material colors
are used in the active working space with no manual uniform gamma conversion.
In the intended linear project, output is linear HDR
before URP's ordinary post exposure/tonemapping. Sky _Exposure is a multiplier.

Tint correction: the default sRGB Color (.42,.53,.65) corresponds to linear
(.147318999244,.242866813904,.380056336450). The shader divides the incoming
working-space uniform by this linear reference in linear projects, and by the
original sRGB reference under UNITY_COLORSPACE_GAMMA. No uniform is decoded again.
Both default ratios are one, preserving baseline zenith (.13,.32,.63).

_CloudWind.xy is normalized (zero means stationary), .zw ignored. Speed is noise
units/second. Periodic 64-unit noise plus integer octave frequencies make wrapping
continuous. _Time.y still has ordinary float precision limitations after very long
sessions: CPU-owned bounded phase may replace it later without per-frame allocations.
No camera-world-position input is used for clouds; floating-origin shifts do not
move them. Cloud shell is dimensionless art geometry, not physical world altitude.

Quality 0: no cloud noise; 1: three octaves per field; 2: five octaves per field.
Two fields per cloud pixel (shape + sunward lighting probe): maximum 6/10 value
noise evaluations = 48/80 lattice hashes. No loop/ray march, textures, RTs, LUTs,
history, extra fullscreen passes or per-frame allocation. Noise octave contrast
is reduced using screen-space footprint for unresolved frequencies. CloudPoint
derivatives are calculated before divergence. Surface shadows/normals/depth and
instancing paths are not replaced. Shader model 3.5; instancing/stereo macros included.

Material CBUFFER: 8 float4 + 9 floats = 164 payload bytes, 176 bytes after packing.
Fog globals: exactly 3 float4 + 8 scalar globals = 80 payload bytes;
actual global packing/driver overhead is backend-dependent. No GPU time claim.
These are source-level evaluation bounds; inspect compiled GPU code and actual
timings before assuming proportional cost savings between quality settings.

AshenApplyFog replaces, rather than follows, each existing MixFog call. Do not
apply twice. Upstream Core.hlsl provides MixFog; include this helper afterward.
Disabled atmosphere calls MixFog unchanged. Fog off requires the caller to disable
RenderSettings.fog so the legacy path is identity. Height falloff is nonnegative;
zero is homogeneous fog. Start excludes the first N metres, and integration starts
at the corresponding height along the ray. Density is extinction at base height.

OriginY means absolute elevation = rebased positionWS.y + OriginY. Update it with
each origin shift, before rendering, and restore all owned globals on teardown.
Log-space mean density handles horizontal rays and downward paths without a fixed
height ceiling. Only final tau is clamped to [exp(-80),80] (negligible transmission
limits). MaxOpacity caps physical opacity with min, not a density multiplier.

Fog tint is base + sun tint * scatter * max(dot(view,sun),0)^8. Caller must coordinate
fog/sky horizon colors, ambient hemisphere and the actual directional Light.
At view.y=0 the non-solar sky is exactly _HorizonColor * _Exposure; sky-internal
night/sunset transformations and cloud extinction are zero there. Runtime owns
_HorizonColor tint for day/night/sunset. Sun disc/halo remain directional additions.
For the fog match, use the same linear horizon multiplied by sky _Exposure, or
keep that sky multiplier at 1 and use shared URP post exposure. Fog's own sun lobe
is separately artist-directed and not an exact match to the sky halo.

This include alone does not establish sky/forest/water color continuity. Transparent
water and distant terrain require their own existing fog call replacement; a sky
pixel must not be passed through this surface fog function.

## Validation

Run: python3 Tools/check_atmosphere.py

PASS: 150 analytical integrals vs independent 2048-step Simpson quadrature;
worst relative error 2.78e-12 (CPU double).

PASS: equal-height limit, zero falloff/density/distance, start exclusion;
35 extreme-height rays; 200 floating-origin invariance comparisons.

PASS: 100000 shell rays including both hemispheres, max radius error 1.09e-14;
300 periodic noise checks; shader identity/property/fallback checks.

PASS: 5000 CPU float32 stress rays; worst absolute transmission error 3.38e-06
relative to the double reference. NumPy is required for this test script.

PASS: float32 default tint normalization preserves baseline zenith in both linear
and gamma working spaces (relative tolerance 5e-7).

These are scalar CPU checks, not HLSL execution or native-render evidence.
No Unity, dxc, clang or glslangValidator was present in checked cloud PATH.
The integrating agent reports installed Unity API compilation passed for C#;
this was not independently run here and does not establish shader compilation.

Pending: matching Unity import/API/shader compile, Tools/check_project.py against
the full project, mesh regression and native fixed-camera comparisons. Check
day/dusk/overcast at forest/mountains/water; sun occultation, below-horizon disc,
zenith/poles/horizon, moving clouds, altitude/origin shift, DLSS/DLAA/SMAA. Measure
p50/p95/p99 frame time and CPU/system commit/VRAM deltas on actual hardware.
No full-scene FPS or appearance acceptance is proven. No PC tools were started.

## Native correction to the original color recommendation

The original recommendation to use Material.SetVector for every color was wrong
for plain ShaderLab Color properties. Native Unity 6000.3 GPU probes showed that
these properties still receive sRGB-to-linear conversion during upload, even
when SetVector supplies already-linear components. That caused a second decode.
Use authored sRGB with SetColor for plain Color properties; use explicitly
converted working-space vectors for HDR Color properties and shader globals.
The shader itself must not decode them again. Preserved GPU evidence is in
native/rejected-20261028-212310/color-upload.csv. Those captures fixed encoding
but the flat cloud appearance was rejected and is not a visual acceptance.

Reference: https://docs.unity.com/en-us/engine/6000.3/script-reference/unityengine/material/setcolor
