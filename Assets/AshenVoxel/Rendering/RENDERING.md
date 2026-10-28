# Surface cache

Current material-optics overlay: GreedyChunkMesher emits both sides of distinct
material interfaces; equal-material interiors remain culled. Vertex alpha is now
`(materialId << 2) | AO2`, decoded before interpolation. Intrinsic stored RGB stays
opaque and never controls physics. The shared MaterialOptics resource has separate
opacity/transmission values; see `Docs/QualityPatches/patch-5/README.md` for controls,
AVPM4 migration and native evidence. The historical AO-only descriptions below
refer to the earlier render cache. All four current URP passes apply coverage.

`VoxelWorld` owns the data. `ChunkWorldRenderer` is an optional cache, intended to
be replaceable with a brick renderer without changing generation or simulation.
Keep its transform at identity. Mesh vertices use world coordinates; floating
origin support requires a future explicit conversion layer.

Every 16³ chunk uses greedy quad merging that preserves material identity,
effective persistent RGB, and corner AO. Vertex alpha contains
classic local voxel ambient occlusion derived from two side neighbors and a
diagonal neighbor outside each face. It attenuates ambient lighting only, and is
not global illumination. Faces with nonuniform corner AO retain their cell quads
to preserve interior creases; uniform AO faces merge only with identical material,
RGB, and AO. Triangles choose the AO-consistent diagonal. This adds polygons near
cavities and foliage; the budget remains chunk based, not a guaranteed frame time.

Every mesh reads reusable 18³ material and effective-color halo snapshots.
One-cell halo invalidation must
include diagonal owner chunks on boundary edges/corners. Fully internal faces,
including interfaces between different opaque materials, are omitted. A boundary
face belongs to the chunk containing its solid cell. Edits must invalidate both
sides of chunk boundaries; the core's Set operation does this. The renderer
consumes `DirtyChunks`; do not have a second consumer clearing that collection.

Call `Initialize`, then progressively call `RebuildBudget` during startup and play.
`RebuildAll` is an explicitly blocking debug/loading helper; the demo does not use it.
The chunk budget limits the number of mesh builds, not milliseconds. A pathological
checkerboard chunk is much more expensive than smooth terrain; do not interpret
this as a guaranteed frame time. UInt32 indices allow such worst cases to render.
Startup enqueues resident chunk keys and existing cache keys, so a large sparse
finite domain does not allocate a queue proportional to its empty volume.
The queues deduplicate edits, including collider requests. There are no individual
voxel GameObjects or rigidbodies. Meshes are reused, and released on destruction.

Optional Unity mesh colliders are disabled on edit, then rebuilt on their own
budget. There is a temporary collision gap; use authoritative grid collision for
the player. Large rigid bodies should not rely on these proxies until a collision
synchronization policy is implemented. Collider cooking remains a main-thread
cost. Visual queues can also show delayed terrain; gameplay queries use the grid.

The URP shader provides directional/additional light, shadow receive/cast, SH
ambient, fog, rough highlight, and deterministic material grain independent of
quad merging. It is an opaque forward shader, not a full physically based URP Lit
replacement. There is no SSAO normals pass, GI bake/meta pass, motion-vector pass,
transparent liquid, smooth dual contouring, LOD, streaming, Burst, GPU meshing,
virtualized geometry, or ray tracing in this baseline. Shadow acne/normal bias and
the grain cell size must be tuned at the actual camera scale. Set `_VoxelSize` to
world.VoxelSize. The included `Resources/VoxelSurface.mat` explicitly references the shader with a
fixed GUID, keeping that dependency in player builds. Runtime loads and clones
this material; retain the Resources asset and both .meta files in source control.
A custom SurfaceMaterial override must likewise retain its own shader dependency.
Unity editor/shader compilation still needs validation.

## Persistent color and geometry cost

`VoxelWorld.GetColor` supplies opaque `0xFFRRGGBB` artist sRGB. A cell can have an
explicit stored tint or use its material's default color; visually identical
values merge regardless of whether the tint is explicit. Every emitted vertex
gets that cell's RGB, while alpha is reserved for AO. Tint-only edits invalidate
render caches without waking material physics. Colored cells moving through the
simulation retain their appearance through the core's move operations.

The forward shader explicitly converts vertex RGB to linear in a Linear color
space project. Mesh vertex attributes do not receive texture-import sRGB
conversion. Optional cell grain defaults to 0.025 strength (at most 1.25% before
fading); optional fine grain defaults to zero. Stored multiscale species/material
color patterns therefore remain the main source of variation. Grain is cosmetic,
not saved state. `_VoxelSize` is set from the world; the demo target is 0.1 m.

Different effective colors cannot share a greedy quad in this implementation.
Dense cell-by-cell variation can substantially increase quads, memory, upload
cost, and build time. Use coherent/quantized tone regions rather than independent
random RGB per cell when authoring species and geological palettes. This tradeoff
is explicit: exact stored color is preserved. A future GPU color-brick lookup or
palette-index texture sampled by world cell could decouple geometry merging from
color detail, but needs additional streaming, filtering, and synchronization work.
The color halo adds a reusable 23,328-byte uint buffer and the color mask another
1,024 bytes per mesher instance; they are not allocated per rebuild.

The offline preview has its own lighting model. Checking packed channels and mesh
RGB does not establish screenshot parity with Unity's linear lighting, fog, shadows,
tone mapping, or exposure.

## Validation boundary

The actual mesher C# and its editor regression methods were compiled with Roslyn
C# 9 against .NET 8 using minimal Unity vector/mesh data shims. The randomized
oracle checked 27,208 emitted unit faces including RGB and 108,456 corner AO values across 12
worlds, in addition to explicit material, seam, winding, empty-world and scale
regressions, explicit RGB channel tests, tint-only updates, differently colored
same-material merge rejection, and colored chunk seam ownership. This validates geometry algorithms, not Unity Mesh behavior, GPU
output, shader compilation, runtime allocations, or editor integration. Run the
same checks through the Ashen Voxel validation menu in Unity before relying on a
player build.

ProjectSetup API usage was reviewed against Unity's official Graphics repository
`6000.3/staging` sources for UniversalRenderPipelineAsset, UniversalRendererData,
and PostProcessData: Create(rendererData) is the public editor factory; the
postProcessData field is public; renderer OnEnable reloads its resource fields;
and light/shadow settings with internal setters use checked SerializedObject
fields. Existing missing renderer references are repaired. The shader includes
URP 17 soft-shadow quality variants. Source review does not establish successful
Package Manager restore or Unity/shader compilation; these still require an editor.
