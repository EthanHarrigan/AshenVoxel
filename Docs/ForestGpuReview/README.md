# Full-view forest horizon and GPU drawing

2026-10-26 America/Denver / 2026-10-27 UTC. This local revision follows the
earlier [forest LOD prototype](../NativeLODReview/README.md); old captures and
release identities remain historical.

Forests now follow the complete visible terrain range automatically, including
the extra coarse horizon, instead of the independent30 km forest cutoff.
Default30 km terrain gets30 km forestry; F1's **Coarse horizon** can extend both
to100 km. Existing world boundaries, habitat/snow rules, fog, terrain occlusion
and subpixel visibility still apply. This does not mean every generated tree
on the full299×150 km map is rendered individually.

The same256tile /16384sample cap covers the larger range by lowering spatial
detail. Near trees retain their existing meshes; far stands are approximate
image proxies. No distant colliders, material voxels or simulation are added.
Complete old drawing remains until replacements publish.

## GPU boundary

On supported devices, the stand layer now uses:

1. Persistent double-buffered GPU instance records, uploaded only on publication.
2. A compute shader for per-stand frustum/range culling and append compaction.
3. GPU-written indirect instance counts and one procedural distant-forest draw.
4. GPU billboard orientation, prototype-view selection and matching alpha-tested
   forward/depth/normal passes. Origin changes update one offset, not every matrix.

There is no GPU readback in ordinary gameplay. Validation explicitly reads counts
and pixels to prove the compute/indirect path ran. Habitat/source placement
continues on the immutable background CPU worker, preserving existing biome and
terrain behavior. This is Unity graphics compute, requiring neither CUDA nor
Python; stopped terrain-generation work remains stopped. Unsupported devices
retain the CPU-instanced fallback.

Settings: **Forest Follows Terrain View** defaults on. Disabling it restores
the manual **Forest Horizon Km** override, now supporting2–100 km. **Use GPU
Forest Horizon** defaults on and is applied on next Play. The HUD identifies
GPU indirect versus CPU instanced drawing and displays resident stand counts;
it does not pretend to know GPU visibility counts without readback.

Uses Unity's official [indirect primitive drawing](https://docs.unity3d.com/6000.3/Documentation/ScriptReference/Graphics.RenderPrimitivesIndirect.html)
and [GPU append-counter copying](https://docs.unity.com/en-us/engine/6000.3/script-reference/unityengine/graphicsbuffer/copycount).
The pinned UnityIndirect.cginc non-indexed command layout has instanceCount at
byte4. The first native run rejected an attempted Marshal.OffsetOf call because
the C# IndirectDrawArgs members are properties, not marshaled fields. The corrected
layout was checked against the installed include and
[pinned Unity C# reference](https://github.com/Unity-Technologies/UnityCsReference/blob/6000.3/Runtime/Export/Graphics/GraphicsBuffer.bindings.cs).
The failed log is preserved separately; it is not presented as a pass.

## Executed verification

- Native Unity6000.3.27f1 /RTX3080 Ti: ten checks each on D3D11 and D3D12.
  These execute compute culling, append count reset, actual indirect tree pixels,
  camera turns, origin rebasing, empty publication, shader import and settings.
  Synthetic instances at50 km and90 km survive the100 km range test and are
  excluded at30 km. [D3D11](native-d3d11.json), [D3D12](native-d3d12.json).
- Actual learned-world Play capture at100 km, D3D11:9,620 resident stand proxies,
  3,201 GPU-selected stands, one indirect draw, farthest resident stand96.83 km,
  884,768 bytes of GPU instance/index/argument buffers at the capture.
  [Capture](scene-100km-d3d11.png), [observations](scene-100km-d3d11.json).
- Native Unity mesh regressions passed. Headless streaming31 checks include real
  100 km planning with stands beyond80 km and the unchanged sample cap; bootstrap31
  and movement77 pass. GPU code is intentionally verified natively, not through
  numerical Unity substitutes. Project/documentation integrity also passed.
- [Exact native/source evidence](validation.json) records source, asset, report,
  image and local raw-log hashes. Isolated copies preserve the user's open Editor
  and other source work. Native Editor Search indexing exceptions remain in logs.

The observed CPU enqueue sample is not GPU duration or an FPS benchmark. Far
imagery still has baked-light, plane-depth, view-angle and coarse-clump limits;
very distant individual trees become subpixel. GPU overdraw, sustained flight,
standalone builds and frame-time distributions still need profiling. No new
downloadable gameplay release or source/model migration was made.
