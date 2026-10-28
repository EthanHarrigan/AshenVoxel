# Engine-independent voxel core

All cells are material identifiers; world positions are meters with positive axes and origin at the finite world's minimum corner. World.Set is the authoritative mutation path. Chunk.Cells exposes an allocation-free `ReadOnlySpan<MaterialId>` for rendering and serialization. The backing array is internal to the core; public callers cannot mutate cells through that view and must use `VoxelWorld.Set`, preserving revision tracking, simulation wakeups and renderer invalidation. Chunks are 16³, allocated on the first non-air write, released when empty. Out-of-bounds reads return air; writes fail. Terrain tools and simulation preserve y=0; low-level Set intentionally permits importing/generating the floor.

Generate before constructing Simulation. Simulation subscribes to subsequent edits and wakes granular neighbors automatically, including support changes across chunk boundaries. Call Step once per fixed simulation tick. Budget is a maximum number of candidate evaluations; work not processed stays queued. A cell moves at most once per call. Processing is deterministic for the same seed, edits, budgets and sequence of ticks. It is a simple cellular sand model, without fluid pressure, temperature, rigid connectivity or real-world soil cohesion. Wood and stone currently stay suspended when disconnected: structural support is a later system, not a hidden per-cell rigid body approximation.

After loading a material snapshot, construct a new `Simulation`, call `BeginWakeAll()` once, then call `WakeBudget(candidateBudget)` alongside `Step` on successive fixed ticks. The wake scan captures only allocated chunk keys and inspects at most the requested number of current cell values per call. `PendingWakeCandidates` reports remaining cell candidates; `LastWakeCandidates` reports the latest work. Chunk removal is safe, and edits/new chunks wake through the ordinary mutation subscription. The snapshot does not preserve the active queue, tick count, or an in-progress rescan, so this resumes material gravity without promising an identical mid-tick replay.

Renderer dirty chunks include removed chunks and every neighbor whose one-cell sampling halo contains an edited voxel. Interior, face, edge and corner edits invalidate 1, 2, 4 and 8 chunks respectively, clipped to the finite domain; this covers visibility and vertex ambient occlusion at chunk seams. Snapshot then clear DirtyChunks only after scheduling all needed rebuilds. World is **single-writer** and has no locking; asynchronous jobs require immutable snapshots and revision-checked commits. No Unity types or dependencies exist in this folder.

Generation v3 uses coherent world-meter noise and a jittered forest grid indexed in physical space. `GenerationProfile` defines meter-valued terrain amplitudes, strata depths, arena dimensions, and tree shapes. Trees use tapered curved trunks, staggered primary limbs, secondary forks, and separate irregular foliage crowns with negative spaces. Branches rasterize tapered capsules plus six-connected digital spines so thin twigs remain attached at 10 cm. This is a deterministic scaffold generator rather than a growth/light competition solver. Equal physical world dimensions and seed share the continuous `HeightMeters` field across voxel resolutions; cell rasterization and layer boundaries naturally quantize to each grid. `SurfaceHeight` returns the top terrain cell before trees and the sand mound. Generation has a public version recorded in saves. Edits do not rerun generation. Snapshots carry format version and CRC32; loading validates dimensions, length, material IDs, unique chunk positions and edge occupancy before returning a new world. Snapshots cap at 8192 chunks (32 MiB material data, up to approximately160 MiB payload with a complete optional appearance channel on every chunk). Format2 adds per-chunk appearance flags and an optional little-endian uint color channel. Format1 remains readable with default material colors. Self-contained snapshots from generator versions1 through4 load without regenerating terrain. Saving streams the known-length payload through a table-driven CRC32 writer with one reusable chunk-sized buffer and sorted chunk keys; it supports nonseekable destinations and leaves caller streams open. Loading still buffers the capped payload for validation; this is a demo snapshot format, not the future streamed region/delta persistence system. Save only on the single writer thread or from a coherent snapshot.

Core checks: `python3 Tools/run_core_checks.py` from the Unity project root with Python 3 and .NET 8 SDK (no restore/network required). On unrestricted developer machines, `dotnet run --project Tools/CoreChecks/CoreChecks.csproj` also works. Actual results and full default fixture counts are in `Docs/VALIDATION_CORE.md`. These exercise mass conservation, per-tick movement, budgets, chunk seams, deterministic generation, DDA entry, brush rules and corrupt saves.

Material hardness and density are prototype relative coefficients, not calibrated engineering measurements. Density does not alter the current cellular falling rule. Before adding structural mass, choose explicit physical units and conversion rules; flammability is metadata until a thermal/reaction system consumes it.

## Persistent appearance

Material identity and color are separate. `VoxelColor.Pack(r,g,b)` stores opaque authored sRGB bytes as `0xFFRRGGBB`. Raw tint0 means use `VoxelColor.Base(material)`; explicit black is `0xFF000000`, and air always has effective color0. `world.GetTint` reads the stored optional value; `world.GetColor` resolves the effective color. `Chunk.Tints` is an allocation-free read-only span, empty when no color array is allocated. The optional color array costs16 KiB per tinted chunk and is released when the last tint returns to default.

`Set(x,y,z,material,tint)` changes material and appearance together. The material-only overload preserves tint when the material is unchanged, and resets tint when changing material. Tint-only changes increment revision, dirty the full meshing/AO halo, and emit `AppearanceChanged`; they do not emit material `Changed` or wake granular physics. Falling grains carry their raw tint. Fruit is material ID10, appended without changing existing material IDs. Removing voxels and clearing worlds remove their appearance data.


## Small plants, rocks and body queries

Core/Details provides ten static plant morphology presets on the10 cm grid;
Core/Rocks provides five bounded weathered lithology habits. Their generation
versions are independent of the unchanged arena/tree Generation5. All writes
use Set; candidate/cell limits and clipping are observable. Profiles are static
geometry controls, not growth or erosion simulation. Global habitat placement
belongs to WorldScaleData, not Core.

Catalog version3 appends Fungus22 and Petal23 after the unchanged IDs0–21.
Small plant tissue reuses Leaves, woody shrub axes reuse Wood and berries reuse
Fruit. Opaque stored tint and limited palettes do not create new material IDs.
BlocksActorBody separates actor passability from matter solidity: Leaves,
Fungus and Petal are normally passable, but remain targetable/renderable. The
GridWalker flag can restore solid-foliage body collision. Tree leaves share
that policy; wood/terrain still block.
