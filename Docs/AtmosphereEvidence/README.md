# Atmosphere implementation evidence

Current native run: **2026-10-28 22:30:42 UTC**, Unity 6000.3.27f1, Direct3D 11,
linear color, RTX 3080 Ti. This revision follows `12317c9`. The user authorized
bounded shader/capture and mesh checks, **no Play**. These are actual Unity renders,
not image generation. [Native source hashes](native/20261028-223042/source.sha256)
identify the exact shader/runtime/settings/fixture used.

## Current captures and checks

The reviewed candidate has opaque cloud bodies, irregular translucent fringes,
warped billows and pale dusk lighting. The agent considers these stills ready for
user review against MishMash's **14.5–20 second** above-cloud reference; this does
not record user acceptance or full-game visual/performance acceptance.

- [Dusk above the cloud sea](native/20261028-223042/WarmDusk-above-clouds.png)
- [Dusk close to the layer](native/20261028-223042/WarmDusk-close-clouds.png)
- [Daylight from below](native/20261028-223042/ClearDay.png)
- [Daylight above the layer](native/20261028-223042/ClearDay-above-clouds.png)
- [All 12 captures and native outputs](native/20261028-223042/)
- [Reference frames and provenance](reference/frame-provenance.json)

Each preset has a sky-only view, a bounded source-terrain view, and two above-cloud
views. The terrain backdrop is a coarse diagnostic mesh sampled from shipped
source/climate/biome data, **not the full Exploration terrain renderer**. It starts
no streaming, population, services or physics. [Camera/source details](native/20261028-223042/terrain-context.txt).
Cloud motion is frozen for comparisons; HDR post processing precedes PNG sRGB encoding.

| Check | Evidence and result |
| --- | --- |
| Native shaders, settings migration, texture reuse/disposal, global cleanup and captures | [Completion](native/20261028-223042/completed.txt), [Editor log](native/20261028-223042/unity-editor.log); pass |
| Actual GPU color upload and display encoding | [Float-target probe](native/20261028-223042/color-upload.csv); property-specific color conversion and sRGB output verified |
| Production fog HLSL versus independent quadrature | [192 rays across four origin offsets](native/20261028-223042/fog-gpu.csv); maximum absolute transmission error 5.9027e-7 |
| Alternating near/far opaque occlusion | [Depth probe](native/20261028-223042/cloud-depth.txt); foreground on/off error 0, far cloud luminance gain 0.441369057 |
| Actual URP grading | [Post exposure probe](native/20261028-223042/post-exposure.txt); exposure 0/+1 luminance 0.0065136645/0.0192064662 |
| Native mesh regression/oracle | [Completion](native/20261028-223042/mesh-completed.txt); 32,198 faces, 128,416 AO checks |
| Current CPU math/data and API compilation | [Volume](native/20261028-223042/volume-cpu.txt), [fog/integration](native/20261028-223042/atmosphere-cpu.txt), [six runtime assemblies](native/20261028-223042/runtime-api.json), [Editor fixture](native/20261028-223042/editor-api.json); pass |
| Project asset/reference integrity | [Current check](native/20261028-223042/project-check.txt) |

The CPU/API scripts' “not run” statements describe those scripts, not the separate
native run above. Operation bounds (64 primary steps, up to five light probes,
512 volume texture fetches plus depth) are not measured frame times. No Play,
moving-camera/cloud stability, forest/shoreline interaction, DLAA/DLSS, full-scene
memory/VRAM or FPS acceptance was performed. Existing unrelated packed-vertex and
DLSS initialization warnings are retained in the log; no DLSS acceptance follows.

## Preserved rejected evidence

- `native/rejected-20261028-210214`: initially dark output from incorrect plain-Color
  upload and PNG encoding. Retained as failure evidence.
- `native/rejected-20261028-212310`: corrected encoding, but flat/cartoon shell
  appearance rejected. These early images also predate the grading fixture fix.
- `native/rejected-volume-20261028-215459`: smooth initial volume and a failed
  foreground-depth probe. Its synthetic plane lacked the DepthNormals pass needed
  by existing SSAO; production terrain already had that pass. Fixing the fixture
  made the alternating occlusion test pass. This was not a production depth defect.
- Repeated SingleCameraRequest captures did not refresh URP grading. StandardRequest
  now updates the Volume framework, and an actual pixel exposure test guards it.
  Earlier images cannot establish their preset grading.

## Historical initial implementation (12317c9)

Base was `76a8fc5d88299575f6468e5a49b16f0f952f405b`.
[Original source set](source-sha256.json), [CPU checks](cpu-checks.txt),
[runtime API](unity-api.json), [Editor API](editor-api.json),
[project integrity](project-check.txt) and [CPU mesh oracle](mesh-checks.txt)
belong to that initial shell implementation. Native testing had not yet run then.
They do not test the later volume or native corrections.

The initial project check found two already-tracked terrain reshape files without
metadata. Only missing `.meta` files were added; their source content did not change.
Existing GUIDs and historical ZIP release identities remain intact.

[Cloud implementation/research](cloud-research.md), [procedure and remaining gates](../ATMOSPHERE.md).
