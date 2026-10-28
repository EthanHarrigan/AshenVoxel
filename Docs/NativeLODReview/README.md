# Stick resistance and native forest LOD review

Follow-up: [Full-view GPU forest horizon](../ForestGpuReview/README.md) supersedes
this revision's independent30 km cap and CPU stand drawing. Evidence below remains
the original checkpoint and is not relabeled as testing the follow-up.

Checkpoint: 2026-10-26 America/Denver / 2026-10-27 UTC. This is a new local
revision, not a replacement or revalidation of the previously uploaded ZIP.

## Interaction

Exploration retains Wood/Leaves appearance, material IDs and 10 cm geometry.
AVPM0003 separates blocking spans, soft tissue and stick spans. Approximately
10–20 cm peripheral wood is a stick: an offline 30 cm wood-core test with a
20 cm attachment margin preserves thick branches/bark. A tracked continuous
upright leader preserves slender trunks. This is an occupancy heuristic, not
an exact measurement of branch diameter. Small woody understory is soft. The
existing fern specimens contain leaf tissue, rather than Wood; fern tissue and
woody understory both participate in resistance.

One allocation-free nearby span query per motor tick sums fractional overlapping
10 cm cells. Sticks count four times soft tissue. Horizontal walk/sprint speed
is multiplied by `max(0.65, 1 / (1 + 0.0008 * weightedCells))`: 100 leaf equivalents
reduce speed about 7.4%. Greater contact increases resistance, capped at 35%.
Jumping, gravity and flight are unchanged. Authoring settings expose coefficient
and speed floor. Soft spans never supply hard support, regardless of visible LOD.

All 412 original mesh payloads are unchanged. AVPM0001/2 remain readable with
their original policy until their caches are rebuilt. Core generator/catalog
versions and Format 2 matter saves are unchanged. The separate arena GridWalker
is not modified. [Bake provenance](../exploration-interactions-bake.json) records
original/current cache identities and counts. To reproduce, run
`bake_prototypes.py --mode all`, then `bake_interactions.py --source <AVPM0002-pack>`;
the just-baked prototype pack is a valid source. Conversion uses authoring-time
NumPy/SciPy, not runtime dependencies.

## Distant representation

Near trees retain URP voxel meshes. Far LODs use shared two-triangle billboards
with eight azimuths and one overhead view, captured in native Unity from the
actual full-resolution 16 prototypes. The 3072² PNG is about 2.8 MB on disk,
imported with compression and alpha-coverage mips; disk size is not GPU residency.
Forward, alpha-tested depth and approximate camera-facing normal passes agree
on silhouette. Far images cast no shadows. Redwoods ordinarily retain 0.4 m
geometry through 180 m; the existing triangle budget may still demote models.

A separate view-only stand layer follows source habitat, age, density and ground
height toward the configured horizon, default 30 km bounded by terrain view.
It uses one worker on an immutable source snapshot, at most 256 quadtree tiles /
16,384 samples, incremental publication, pooled buffers and same-call rebasing.
Old complete drawing survives replacements. These approximate canopy clumps are
not exact distant tree placements, matter, colliders or simulation. Their width
aggregates coverage, so coarse stands can appear broad/dotted. Grass/plants
remain near meshes; distant ground cover uses existing surface color, rather
than individual far blades or new high-resolution botanical textures.

This follows the separate cheap visual representation principle in
[Distant Horizons' project description](https://www.curseforge.com/minecraft/mc-mods/distant-horizons).
Multi-view imagery is informed by
[image-based complex-scene representations](https://www.cg.tuwien.ac.at/research/publications/2005/jeschke-05-ISTAR/jeschke-05-ISTAR-Paper.pdf).
No mod code was copied; those sources do not establish this project's performance.

The former 16-quad terrain ceiling is raised to 32 nearby / 24 farther away.
This improves mesh sampling only, not the 1280 m learned source. Distant terrain
material aliasing and error-bounded ridge preservation remain open work.

Settings: disable **Use Tree Impostors** before next Play to compare original
voxel LODs; **Enable Forest Horizon** / **Forest Horizon Km** control the extension.
Atlas regeneration is explicit: **Ashen Voxel → Exploration → Bake distant forest atlas**.

## Executed evidence and remaining gates

Native Unity 6000.3.27f1 ran in an isolated local Windows project copy using
D3D11 and an RTX 3080 Ti. It entered Play with bundled data, rendered the atlas
and fixed 1600×900 cameras, and passed the real Unity mesh regression entrypoint.
The user's already-open Editor was preserved. Impostor shader error flags were
false. These are native renders, rather than the older CPU previews or shims.

- [Original overview](baseline-overview.png): coarse brown tree blocks/cutoff.
- [Current overview](current-overview.png): actual-tree silhouettes and extension.
- [Valley overlook](current-ground.png): camera Y=160 m above the starting area.
- [Redwood overlook](current-redwood.png): X=90560 / Y=800 / Z=12480 m.
- [Native evidence inventory](native-lod-review.json): exact source/assets,
  captures, logs and executed report hashes. Adjacent JSON files retain counts.

Individual fixed-view stand-layer CPU submission observations were around
0.07–0.1 ms with roughly 6–8k resident clumps. These are not GPU measurements,
frame-time distributions or an FPS guarantee. .NET tests measured 10,000 warmed
actual soft-span body queries with zero managed allocations; exact elapsed
observations are in the streaming report and are not native Unity timings.

Checks: movement77, population/unchanged meshes136, streaming/impostors/horizon/
allocation29, numerical real-asset bootstrap31, trees73, authoring40, independent
mesher/oracles, project/docs/static shader checks, plus native mesh regressions.
Native Editor Search indexing emitted an internal `ArgumentOutOfRangeException`;
logs preserve it separately from gameplay. Windows test-path normalization,
UTF-8 invocation and Unity's appended editor revision line required compatibility fixes.

Remaining: interactive movement feel, temporal/view-angle popping, baked-light
mismatch when lighting changes, approximate plane depth/normals, coarse stand
quality/continuity, DX12/standalone builds, CPU/GPU distributions and overdraw/
VRAM profiling. Shared near meshes still cost about 340 MiB before other resources.
Fake imagery is not free to render. The historical global release freshness
audit intentionally rejects changed sources; its old API reports are preserved.
No new downloadable release was made. Separate local-generation work in another
chat is retained and is not activated by this update.

While recording this checkpoint, another chat began adding voxel-terrain sources
and editing Bootstrap/Settings/WorldExplorerIntegration. The evidence inventory
explicitly lists these later differences. Its native results describe the frozen
isolated source snapshot, **not the subsequently modified shared workspace**.
The stick/impostor/horizon source files still match that snapshot. Reimport and
integration checks must be repeated after concurrent terrain work settles.
