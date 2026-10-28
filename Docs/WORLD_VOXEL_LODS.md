# Whole-world voxel terrain

Current scheduling overlay: [STARTUP_LOD_PROGRESS.md](STARTUP_LOD_PROGRESS.md).
Coarse-first coverage and progressive up-to256-column refinement are installed;
far payload admission replaces the formerly unbounded detail policy. Prior native
render/FPS evidence below retains its original source identity. Aerial classifier
and tree-atlas corrections are installed as aerial-lod-1 with separate native
64-view evidence. Capacity-release retry audit remains active.

Performance update (2026-10-27 local): unboxed material sorting and bounded
two-worker row sampling preserve exact terrain faces while reducing native
candidate build work56.7% across eight measured tiles. One tile remains in
flight; small tiles stay serial and mesh storage/renderer are unchanged.
[Measured results and limits](WORLDGEN_PERFORMANCE_RESULTS.md). GPU quads remain
an isolated experiment; no full-world FPS claim follows from these timings.

Current-source qualification (2026-10-26 local): quality patch1 now uses a separate
100m-target contact stream with1GiB default mesh admission. It shares the geometry
worker with far terrain but publishes completed contact tiles independently.
The32m near-detail selector below still exists for regular LODs; it no longer
describes the complete contact policy. Whole-terrain byte admission and far-plan
progressive activation remain distinct work. The native counts below retain their
original tested revision. See [processing research/source audit](WORLDGEN_SPEED_RESEARCH.md).

This replaces the former22.4m overlay in Exploration. Enable Voxel Terrain now
selects exposed voxel faces at every terrain LOD, anywhere in the authored world.
The learned source and original world ID are unchanged. Disable it before the
next Play to compare the retained conventional URP height-surface path.

## Representation and quality

The world is sampled as implicit10cm material cells through the existing surface
sampler: grass/litter, soil, sediment, geological rock, snow and ice retain their
existing material IDs and pigment. Buried mountains are implicit rather than
stored as enormous dense arrays. There are no per-voxel Unity objects. This does
not add digging, caves, persistent edits or dynamic terrain simulation.

25.6m base tiles contain256 columns per axis at full detail. Dyadic tiles cover
the complete view, with8–256 samples per axis. Nearby ground tiles intersecting
the default32m detail radius use10cm columns. Farther LODs aggregate horizontal
columns, while elevations remain on the10cm grid. Tile-level caps can retain a
larger leaf near the observer; full detail is therefore a target, not a promise
that every visible point is resolved to10cm.

`Voxel Target Pixels` defaults to1.5 (smaller means more detail). It uses actual
camera pixel height/FOV; the regular far sampling cap is128 columns per tile.
This is a projected spacing target, **not** a certified bound on geometric error
or preserved ridges. F1 reports tiles that cannot meet it. Source resolution is
still1280m macro/5m decoded starter; voxelization cannot invent learned detail.

Tops and side walls have axis-aligned normals. Compatible rectangles merge only
when height, material, pigment, wetness and relevant AO agree. Side merging is
checked against exact unit-face coverage. AO retains full10cm crevice contrast
nearby and averages it down on larger aggregates. Deep boundary aprons conceal
different sampling levels; they are render caches, not exposed gameplay truth.
Discrete transitions and occasional material/height steps remain visible.

## Culling, streaming and storage

The ordinary URP MeshRenderer path remains in use, including forward lighting,
depth, depth normals/SSAO and shadows. Unity performs camera frustum culling for
each tile and independent shadow culling. We do not switch an entire offscreen
GameObject off and accidentally remove a shadow caster. See Unity's
[frustum/occlusion documentation](https://docs.unity.com/en-us/engine/6000.3/manual/cameras/occlusion-culling/occlusion-culling).
No new runtime occlusion/horizon-Z culling is claimed.

A conservative camera-heading cone, with20° padding and a256m surrounding
reserve, prioritizes visible detail. Distant rear tiles retain32-column coverage.
Turning in place requests refinement. Old complete coverage stays until the
replacement exists, including during fast travel, rebasing and source changes.

One captured-source worker produces engine-free geometry. Conversion into upload
buffers is incremental; one complete tile uploads per frame by default. Tile
residency is bounded by active+desired plans (default512 each). Face count is
capped per build; a capacity failure retries at a lower render sampling level.
Other worker failures retain queued retries and expose their cause/counter.
Counts and work-time checks are not hard millisecond guarantees: one native
mesh upload is indivisible.

Vertices are24 bytes: FP32 position, exact signed8-bit axis normal, byte RGB/AO,
and normalized byte surface parameters. This replaces44-byte vertices without
reducing geometric detail or RGB/AO precision; roughness is quantized to8 bits.
One reusable vertex/index scratch pair avoids five new large lists per tile.
The GPU copy releases redundant CPU mesh data after upload. See Unity's
[vertex-layout API](https://docs.unity3d.com/cn/6000.0/ScriptReference/Rendering.VertexAttributeDescriptor.html).

The profile deliberately favors fidelity. Native ground terrain alone retained
38.54M triangles across512 tiles and approximately2.31GB of vertex/index payload;
171 tile bounds intersected the camera frustum. Those are resident counts, not
GPU timings or actual submitted triangle counts. Large traversals can temporarily
retain two plans. A strict total byte-budget/admission policy and FPS profiling
remain acceptance work; do not run two high-detail game instances on this32GB PC.

## Verification and failures

Evidence is under [WorldVoxelEvidence](WorldVoxelEvidence). Executable checks:

```text
python -X utf8 Tools/run_voxel_lod_checks.py
python -X utf8 Tools/run_voxel_lod_runtime_checks.py
python -X utf8 Tools/run_exploration_smoke_checks.py
python -X utf8 Tools/run_world_explorer_checks.py
python -X utf8 Tools/check_voxel_unity_api.py
python -X utf8 Tools/check_project.py
```

26 conversion/quality/merging checks and16 asynchronous runtime checks pass.
The actual bundled-data bootstrap passes31 checks. Existing Core87,
WorldExplorer83 (offline), and live-refinement33 suites also ran successfully.
Five real Unity runtime assembly boundaries compile against6000.3.27f1.

Initial concurrent native attempts exhausted system memory. The user's main
DX12 Editor also failed in the old uncompressed tile upload path. Logs and source
hashes are preserved; this was a real failure, not a successful performance run.
Subsequent compact-buffer D3D11 actual-source terrain rendering completed three
views. The final D3D12 full Exploration run is recorded separately, with its own
source hashes and screenshots; see its completion record for exact scope.
Editor Search indexing emitted a separate startup exception in the isolated
copy. It did not prevent subsequent play/captures; it is retained in the log.

The unthrottled smoke harness initially exhausted its old4000 synthetic-frame
limit while an asynchronous worker still had work. It now uses12000 synthetic
frames **and** a90-second wall-clock timeout; these are test liveness bounds,
not native loading/FPS measurements.

Remaining gates: sustained GPU/CPU frame distributions and memory across long
flights, interactive movement/turns, LOD popping/aliasing, broad cliff/snow/coastal
visual review, standalone player and byte-budget policy. No60 FPS acceptance.

## Working copy and recovery

Development was moved to the sibling `AshenVoxel-WorldVoxels` project at the
user's request. Its `workspace-baseline.json` records main-project hashes at
isolation. Integration compares those hashes before copying owned files, so
concurrent forest changes are not overwritten. This workspace is not a Git
repository; integration is a verified local file patch, not a Git push.
GPU diffusion preparation remains available; the model bake/service stay stopped.
