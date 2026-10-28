# Bounded matter visual cache

`Assets/AshenVoxel/Exploration/NaturalMatterRenderer.cs` replaces per-frame complete
tree vertex reconstruction with nine ordinary URP mesh objects: source plus eight
rigid sections. The physics/controller/shader integration belongs to the parent
checkpoint; this note records the renderer's own contract and evidence.

Public contract: construct with `(Transform parentTransform, Material template)`,
call `SetPhysics`, tick with the tree instance position relative to the render
origin, and publish visibility only after `Ready`. `Current` additionally requires
the published structure revision to match current non-busy physics. Motion can be
held until Current; support/release work must remain able to progress. `Dispose`
releases objects, meshes and private materials.

An unchanged structure updates at most nine transforms and4096 instanced debris
matrices/tints; it never scans the tree or uploads its rigid vertices again. Each
changed structure uses at most8192 combined source/bin candidates per render tick.
Partial support commits and releases are not captured. A revision change cancels
staging; the latest stable revision coalesces continuous edits. Nine staging meshes
upload at most one group per tick, then swap together without extra renderer
objects. Uploaded mesh CPU copies and oversized staging-list capacities are freed.
Counts bound work, not elapsed upload time or native frame cost.

Small trees retain10cm exposed-face geometry. Large trees use an explicit power of
two render cell size, exposed by `RenderingCellSize`; the original-cell grid is
coalesced independently per source/section ownership. A load-bearing material wins
over foliage within a coarse bin. A sparse specimen that exceeds32768 aggregate
bins restarts at a coarser render cell size rather than dropping bins. Authoritative
matter, original intrinsic tints/materials and ownership counts remain untouched.
Coarse rendering can hide individual10cm cuts and change foliage silhouettes; it
is an intentional appearance approximation, not proof of full10cm visual detail.

Debris shares one cube mesh and one instanced material, in batches of at most1023.
At most4096 live parcel leaders are sampled evenly over the bounded loose index
list. Each remains a10cm visible cube; its exact represented matter weight stays in
physics. `DrawnDebris` and `DebrisSampled` expose the visual cap. Debris casts no
shadows and receives ordinary lighting/shadows. `_MatterTint` carries intrinsic
sRGB and packed material/AO alpha; `_MatterInstances=1` selects the parent's shared
shader path. Source/island meshes retain ordinary vertex colors and URP renderers.

Checks:

```powershell
python Tools/NaturalMatterRendererChecks/run_checks.py --api
python Tools/NaturalMatterRendererChecks/run_checks.py
```

The API mode compiles this renderer, current physics and matter dependencies against
the installed Unity6000.3.27f1 managed APIs. Numerical mode uses an explicitly local
Unity substitute and verifies exposed-face counts, canonical yaw coordinates,
unchanged-mesh origin/pose updates, visibility/disposal, sparse50000-cell renderLOD
retry,8192-candidate ticks, section publication,4096/1023 instance limits and
unchanged exact matter counts. Reports include source hashes and reject source
changes during execution. This does not import/render the shared shader, run
native gameplay or prove full-scene frame rate.

The parent checkpoint records integration and final current-source reports.
Preserved API/numerical reports for this renderer live alongside this note as
`matter-renderer-api.json` and `matter-renderer-checks.json`. Initial test failure
was an invalid zero body budget in the test fixture, corrected to the physics
minimum of one; its unique run log remains under `.terrain-cache/natural-matter-renderer`.
