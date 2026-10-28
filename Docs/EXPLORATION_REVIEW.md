# Exploration prototype: independent verification

Current grounding candidate: [frozen CPU/API evidence](ExplorationEvidence/population-grounding-1/candidate-manifest.json).
It is NOT installed or native-tested. Population v2 uses actual basal footprint
constraints and quantized real-column exposure; AVPM1–3 keep legacy placement.
159 population, 35 streaming plus exact fallback coverage, 31 smoke and five
runtime/changed Editor API compilation checks pass. The fixed 16-placement main
world sample accepts 12 with zero measured gaps and rejects four steep rocks.
These measurements do not establish all-world nonlinear support or frame times.

Current movement overlay: [movement-slope-1](ExplorationEvidence/movement-slope-1/installed.json)
records the installed two-file slope-policy fix,127 CPU/controller checks and
actual main Editor compile/reload. Earlier native limitations below retain their
original revision. This patch has no interactive movement/FPS acceptance yet.

This review covers the requested first walk/fly exploration scene, solid wood and
rocks with passable soft foliage, an Azgaar-conditioned world of approximately
300 × 150 km, long terrain views, distinct forest habitats and stand ages, and
editable settings. It separates source review, headless behavior tests, compilation
against real Unity APIs, and native Editor/GPU execution.

## Official Unity API verification

The [official Unity 6000.3.27f1 release](https://unity.com/releases/editor/whats-new/6000.3.27f1)
was inspected. Its Linux Editor archive was downloaded directly from Unity at
release hash `672d562a42a7`, with length 4,573,113,148 bytes. Reference files were
extracted outside the project under `/workspace/exploration-native-check`.

The actual Editor archive supplies URP, Render Pipelines Core and Shader Graph
package sources at version **17.3.0**. It also contains compiled Core/URP and
dependency DLLs in its bundled 3D template's `ScriptAssemblies` cache. These
real assemblies and the Editor's actual engine/editor module DLLs were used for
compilation; no replacement Unity API stubs were used. Input System 1.17.0's
unmodified runtime source was compiled against these engine APIs. Its optional
UI/InputForUI defines were excluded; the project's keyboard/mouse APIs are covered.

Runtime assemblies were compiled separately through their asmdef dependencies,
with C# 9 and the Editor archive's actual .NET Standard 2.1 reference
assembly. Editor assemblies were also compiled separately against Unity's actual
custom `unity-4.8-api` framework reference profile. The final complete pass covered **53 runtime source files in seven assemblies**
and **seven Editor source files in four assemblies**, including the new bootstrap,
settings, live refinement, population renderer and first-import/build setup. The
source set remained unchanged throughout both passes, and every current checked
source hash was audited again before the reports were copied. The reports contain per-source hashes and reference-DLL
hashes; those hashes, rather than this prose's counts, identify the checked revision:

- [Runtime official-API compilation](ExplorationEvidence/real-unity-api-validation.json)
- [Editor official-API compilation](ExplorationEvidence/real-unity-editor-api-validation.json)

This caught an actual project import blocker that the prior API shims missed:
`Camera.AddComponent<AudioListener>()` in the legacy arena bootstrap. Unity only
provides `AddComponent` on the camera's GameObject. The owner corrected it, and
both the legacy and exploration assembly sets then passed the real API compiler.

The [Unity licensing manual](https://docs.unity3d.com/6000.3/Documentation/Manual/ManagingYourUnityLicense.html)
requires an activated license, with Personal activation through Unity Hub. No
Unity login or activated license was supplied here. No credentials were requested,
read or invented. **These checks did not launch native Unity, import assets,
compile HLSL, render a frame, establish FPS or build a tested Windows executable.**
Reference DLLs and the large Editor archive remain outside project assets and
are not redistributed as project content.

## Learned terrain units and registration

The selected map is 299,520 × 149,760 metres. One native 30 m model sample is
placed every 5 m in the exploration world, following the transferred Minecraft
scale-5 horizontal mapping. Physical elevation is retained independently. This is
not a fivefold expansion of native pixel spacing; mountain slopes become steeper
when horizontal scale is compressed while height remains unchanged.

A significant coarse/fine registration error was caught during review. The pinned
upstream model's actual `_compute_climate` method was exercised with a controlled
linear coarse-feature ramp, without loading weights. Its effective interpolation
coordinate is:

`coarseCoordinate = (nativePixel + 0.5) / 256 - 0.5`

Sampling macro features at `nativePixel / 256` omitted an almost-half-cell offset,
which corresponds to **637.5 m** at the prototype's 1,280 m coarse spacing. The
provider owner corrected feature resampling, kept categorical biome-cell ownership
separate, and regenerated the whole learned macro bake using the pinned actual
weights. The signed-coordinate regression is preserved in
[coarse-registration-validation.json](ExplorationEvidence/coarse-registration-validation.json)
and `Tools/ExplorationBake/check_coarse_registration.py`. The ramp is a unit-test
input, not shipped terrain or evidence of neural generation.

The corrected shipped macro height SHA-256 is
`a15c39d2777274e7973309501e6cd1f6ca8a6a2d13a8115291054370f6c6cbad`.
The decoded starting-region height SHA-256 is
`0f8585ebf87a269478e3e4f4fed8f600dbf3c6c6538ff57181674f9b64af7c14`.
The dataset manifest remains the authority for climate/source hashes and units.
These hashes include the versioned publication headers; migrating the publication
identity does not mean that the model ran again or that elevation values changed.

Review also found a full-world service failure that the valid forest starting
region did not expose. Raw upstream coarse climate predictions include negative
precipitation/CV values, while the transport contract correctly requires physically
nonnegative channels. Passing them straight to strict encoding would terminate
otherwise valid dry/ocean terrain requests. The corrected macro audit recorded
6,263 precipitation and 18 CV corrections. The optional service now applies the
explicit `physical-nonnegative-v1` publication policy before canonical encoding:
only negative temperature standard deviation, precipitation and precipitation CV
become zero. Heights, temperatures, lapse rates and existing nonnegative values
remain unchanged. Nonfinite and excessive values still fail validation. The policy
has its own backend/cache namespace and participates in the coordinate-view ID;
valid historical owners migrate with array equality checks and no new inference.
Raw cache data and model identity remain separate from this derived publication.
The default baked data was already valid; its headers now advertise the matching
policy identity. An actual uncached desert-region canonical-owner inference and
its halo contained **36,419 negative precipitation predictions**; the corrected
service published zero for those values without changing elevation. This CPU
request took **212.83 seconds**, illustrating why live decoding is asynchronous
and optional rather than a prerequisite for default world startup. The
[actual negative-climate validation](ExplorationEvidence/negative-climate-live-validation.json)
records raw/published hashes and its archived executed producer source. That run
preceded a lock-only synchronization change to status/provenance snapshots; the
report distinguishes the executed revision from final source rather than
relabelling the run. Six further checks against the restarted final service
matched cached dataset values. These are model/HTTP checks, separate from Unity
execution.

## Default source and visible detail

The default download contains actual learned coarse terrain over the whole map
at 1,280 m spacing, and an actual decoded 1,024 × 1,024 starting-region grid at
5 m spacing. The fine region spans 5,115 m per axis. A fixed 512 m world-space
blend joins it to the macro field. Opening this bundled world needs no Python
server or model download. It does **not** imply decoded 5 m terrain everywhere
on the approximately 300 × 150 km map.

The default field is immutable. Rendering, collision, material classification and
population use the same positioned, resized, blended source. Climate archives
must match the exact height archive hash and grid; both layers must share the
model world identity. Biome categories use a separately aligned cell atlas. Review
also identified that its payload needed to participate in source identity; the
owner added a SHA-256 over its cloned category bytes and a regression.

Optional live refinement requires a distinct checked policy: a patch's full
influence, query halo and solid-prop margin must remain outside the protected
player region before publication or retirement. Async planning and multi-frame
mesh construction must each capture an immutable field snapshot. A camera-centred
moving blend that changes the ground beneath a walking player is not acceptable.
The implemented optional client refuses resized or vertically scaled worlds unless
its transform matches the live service, captures immutable snapshots for both
planning and each complete mesh build, and retains decoded grids independently of
HTTP-cache eviction. Review caught and corrected an unreachable terminal-failure
branch, a missing copy halo and climate interpolation at inconsistent elevations.
The live validation fixtures cover these policies; they do not establish a native
Unity or GPU play test.

The exploration terrain renderer currently uses smooth height-proxy meshes, with
a default near mesh spacing of 0.5 m. Trees, plants and rocks retain actual 0.1 m
prototype geometry and hard-material collision; the separate local matter engine
also retains its 0.1 m contract. The exploration scene does not populate or draw a
full 0.1 m volumetric terrain grid throughout the distant world. Finer proxy mesh
spacing alone does not create additional neural terrain detail.

An independent 100,000-point sample of the actual decoded bare height field
compared its bilinear authority to the renderer's anti-diagonal grid triangles:

| Proxy spacing | 99th-percentile height difference | Maximum sampled difference |
| --- | --- | --- |
| 0.5 m | 0.008 m | 0.036 m |
| 1 m | 0.031 m | 0.158 m |
| 2 m | 0.469 m | 2.895 m |

This check excludes snow/material relief and native rendering. It supports the
submetre spawn-readiness gate and does not prove a universal geometric error bound.
Review caught Inspector combinations that could never satisfy that gate, such as
a 128 m minimum tile with 64 near quads. The owner constrained and validated the
settings pair so the finest available proxy spacing can reach at most 1 m.

## Launch, collision and population review

The end-to-end [numerical smoke test](exploration-smoke-validation.json) exercises
the actual bootstrap, terrain renderer, population component and controller with
the final bundled world and 99-model pack. Its 31 checks reached Ready after 588
simulated frames, walked 8 m without readiness flicker, and covered input, live-off,
no-edit, settings, rebase, cleanup and expected missing-data/pipeline failure paths.
Its maximum measured grounded eye increment was 2.39 mm. This uses numerical Unity
substitutes, so the frame count is not elapsed native startup time or an FPS result.

The startup coroutine reads and validates bundled data, binds the external world,
loads the shared prototype pack, and waits for nearby terrain and hard-object
collision before enabling movement. External navigation disables the old research
movement, regeneration and inspection/edit shortcuts. A missing required dataset
fails visibly rather than substituting analytic hills.

The controller owns double-precision global feet and camera positions; rebasing is
a rendering operation. The bootstrap drives population after the controller updates
the camera/origin. Published and partially built vegetation matrices and bounds
shift during the same rebase call. No independent vegetation Update races that
origin update.

Movement uses a bounded substep solver and continuous terrain heights, with step,
slope, gravity and jump rules. Prop collision reads retained full-resolution hard
material spans independently of rendered LOD. Soft leaves, fern tissue and fungi
are passable; wood and rocks remain solid. Rotated prop collision uses conservative
local bounds, so it is not an exact circular capsule versus every transformed
voxel. Unknown collision cells must hold movement rather than be treated as empty.
Review also caught spawn search accepting unwalkable steep ground. The controller
owner added slope rejection plus steep-only and nearby-flat-plateau regressions;
the movement suite now contains 62 passing checks.

Population cells own globally keyed candidates, avoiding shared random state and
cell-order seams. Forest habitats choose a dominant species; separate coherent
fields choose young, denser stands and mature, more open stands. Prototype stages
are separately generated instead of scaling the physical voxel size. Mature
redwood prototypes retain approximately 74–75 m height without generator truncation.
The version-2 pack stores 99 models and 412 LODs through lossless quad encoding;
decoder checks compare expanded vertices, normals, colour/AO and triangle indices
to their original mesher bytes. An independent inventory additionally decoded all
99 models and matched all 893,767 authored hard-material cells to retained
collision spans. Every one of the 77,945 spans was queried through the public local
collision API; all 21 models containing only soft tissue had no actor collision.

Spatial instance pages, explicit frustum tests and bounded instance batches address
the actual [Unity instance-culling contract](https://docs.unity3d.com/6000.3/Documentation/ScriptReference/Graphics.DrawMeshInstanced.html).
Terrain samples, uploads, population-cell generation and matrix rebuilding have
work/count budgets. These are design controls, not evidence of a target GPU frame
rate. Prototype upload spans hundreds of startup frames; the loading state is part
of the intended launch flow. The numerical streaming suite executes the actual component against the real
99-model pack: 413 startup LOD ticks, 48 minimum-budget near warmup ticks, and zero
streaming holds during a 900-frame walk across a cell boundary. It also verifies
that a newly arriving unpublished cell 97 m away does not freeze the player, plus
active/pending rebase shifts, bounded batch finalization, off-screen shadow-only
submission and the corrected inverse yaw of an asymmetric rock collider. This
uses numerical Unity substitutes, not a native renderer. Native profiling must
still inspect the worst single prototype decode/upload, cell-generation work and
batch publication/rebase costs.

## Surface lighting review

The custom rock/vegetation and ground shaders now include ForwardLit, ShadowCaster,
DepthOnly and DepthNormals passes. Review found that the original ground shader
both lacked a normals pass and ignored the SSAO texture. The shader owner added
the missing pass, the screen-space occlusion variant and actual AO consumption.
Material constant buffers remain consistent between each shader's passes. The
instanced surface shader sets up instance transforms in all four passes.

The new Editor setup uses the actual URP 17.3 serialized SSAO setting names and
initializes global resources through the package's own factory. These names were
also confirmed against the real cached URP assembly metadata. Default SSAO uses
mesh normals, interleaved gradients and half resolution. This remains static/API
evidence until native shader compilation and an actual frame verify it.

## Remaining native acceptance checks

An activated Editor must still open the complete archive, finish Package Manager
and asset import, compile shader variants, and enter the exploration scene. The
first native smoke test should verify these concrete behaviours:

1. The default bundled world starts without Python; the chosen clearing loads,
   the pointer captures only when ready, and F1/Escape release it appropriately.
2. Walking/jumping over ground and stones is smooth; fern and leaf tissue passes;
   trunks, branches and rocks block movement, including after a floating-origin
   crossing and while distant population continues loading.
3. Flight crosses coarse/fine boundaries, forest habitats and large height changes;
   descent gates unknown solid objects, and returning to walking cannot embed the
   character in wood or an unwalkable slope.
4. Default 30 km terrain coverage and the 20–40 km controls remain coherent from
   high viewpoints. Optional longer coarse coverage is visibly lower fidelity.
   Profile CPU, GPU, RAM, VRAM and streaming spikes on the user's target hardware.
5. Saved options reload; imported world dimensions, expected model identity and
   source checksums are respected; no optional refinement changes occupied ground.

Native Editor/GPU verification is the material remaining evidence gap. Passing the
headless and official-API checks must not be presented as a native visual/play test
or as matching Avroid's reference appearance.

## Root-contact study1 — isolated CPU geometry, not installed

The failed rigid seating candidate is still blocked. A base-only alternative
preserves the mature trunk/crown source above3m and leaves each tree upright at
its original natural placement. Below that boundary it uses a narrower flare,
the original central shaft and terrain-following tapered columns. All geometry
remains10cm cubic material data. No main Assets or AVPM pack were replaced.

| Natural redwood site | Original max gap/burial | Candidate max gap/burial | Lower connectivity / seam |
| --- | --- | --- | --- |
| Seed714103, 5.37° | 0.549 /1.951m | 0 /0.251m | Connected /no unsupported upper cells |
| Seed271828, 7.19° | 0.118 /2.282m | 0 /0.282m | Connected /no unsupported upper cells |

These are exact positive-area rotated-cell/global-terrain-cell contact samples
for two placements, not general terrain guarantees. The original low8m exposed
wood and pigments match the current live pack. The final CPU imagery uses actual
top terrain pigments; subsurface pigment is representative at half-metre depth.
Foliage is omitted. Lighting is the existing illustrative CPU renderer, not URP.
See [six before/after views](ExplorationEvidence/root-contact-study-1/comparison.png)
and [checkpoint](ExplorationEvidence/root-contact-study-1/checkpoint.json).

The narrowed generation changes total wood by−8.16%/−7.87%; this is not a gravity
operation or conserved matter transfer. Every original wood voxel/tint above3m
is unchanged. Initial pigment/geometry collars and bad camera positions are
preserved as failed evidence. No1.26m relaxation was used to pass the contact gate.

Actual Core.Set plus the actual mesher with a non-Unity Mesh shim reproduces the
base cells and RGB. Warm CPU material build is about2.4ms, meshing9.4–9.8ms,
arithmetic output1.30–1.35MB/base. Source/template and terrain query costs, native
allocation/upload, total cache residency and Play/FPS are not included. Building
these synchronously during a frame is not accepted. Adoption requires bounded
worker/cancellation/cache behavior, geometry versioning, save/provenance handling,
render/collision/matter/edit agreement and matching LOD/depth/shadow/wind behavior.
