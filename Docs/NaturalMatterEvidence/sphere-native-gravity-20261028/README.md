# Spherical breaking and native section gravity

October 27, 2026 (Denver); evidence uses October 28 UTC timestamps.

LMB now removes a 0.25 m radius sphere around the aimed cell, with the existing
2.5 m reach. The bounded brush includes soft matter, queues whole requests while
support is busy, and mutates through VoxelWorld.Set. All 99 baked recipes are
supported: 16 trees, 60 rocks and 23 plants, including single-cell plants. Adapted
redwoods copy their committed root artifact, preserve the canonical upper section,
and pin the exact root binding through publication/restoration. No rebake, package,
scene, saved preferences or generator identity changed.

The live game now gives each disconnected section one native Unity Rigidbody and
one small convex hull derived from actual matter extrema (at most 8 bodies).
Gravity and contacts own the pose; the old numerical tip torque and missing-height
pause are bypassed. A worker prepares one bounded local terrain collision mesh
before ownership changes. This collision approximation samples at roughly 2–3 m;
it is not per-voxel exact terrain/stump/prop contact. Coherent bodies use gravity,
friction, damping and sleeping. After grounded sleep or the 3 s contact timeout,
the existing exact weighted non-colliding debris/pickup path takes over. Render
cache creation can still delay the initial body handoff on million-cell trees.

Validation: all 99 recipes and root/soft targeting checks; 132230 cheap-matter
assertions including the spherical footprint, microplants, queued brush and native
pose handoff; renderer checks; four full owned Unity API assemblies; project
integrity 250 assets/13 assemblies. Native mesh regression passes 32198 faces and
128416 AO comparisons. The isolated native PhysX probe passed: 16-cell section
dropped from Y=2.20 to 0.275, zero airborne spin, contact at 0.64 s and release at
1.70 s, preserving all 36 logical cells. The separate scene was cleaned up and
the user scene remained unchanged. This is not a maximum-tree/full-scene FPS or
automatic all-prop LMB acceptance. Unity was returned to stopped Play.

Remaining limits: one active prop session; O/range/Stop restores the original and
rolls back its inventory. Arbitrary terrain excavation and persistent edits are
not implemented. Ground collision uses the source snapshot, so unfinished visual
terrain refinement can still disagree with it.

The preceding distant-terrain change is retained separately: completed, balanced
coverage groups/new frontier tiles may publish without the entire replacement
plan. Its 62 progressive checks pass. A native session at 03:31 UTC still paused
with 1.72 GiB commit headroom against a 2.31 GiB resume threshold: 127 visible,
131 cached, 4096 planned tiles. Missing-ground acceptance remains OPEN; partial
publication does not remove memory pressure. Evidence is in
../../TerrainRefinementEvidence/2026-10-28-subtree-publication/native-state.json.
