# Exploration placed-branch beta, candidate 1

Hook for Bootstrap owner: `AshenVoxel.Exploration.ExplorationBetaMatter.Attach(this);`
once in ExplorationBootstrap startup. Attach is idempotent and waits for Ready.
Reserved new install files: Assets/AshenVoxel/Exploration/ExplorationBetaMatter.cs
and its .meta. Add AshenVoxel.FiniteMatter to Exploration's asmdef references.
No replacement of installed finite runtime, Bootstrap, terrain or population sources.

This candidate is API-compiled only. Coordinator owns the single combined idle
integration and actual native test. No Assets, Editor, patch queue or UI writes
were performed here. Readable preimages and exact source hashes accompany manifest.
Merge assembly reference with the Bootstrap owner's current candidate; never replace
a concurrently modified asmdef with this snapshot. Preserve original GUIDs.

In the actual Exploration scene, J first places one experimental 72-cell wood
branch three metres ahead. Placement waits for terrain/population readiness and
rejects water, more than 30cm height range, or hard props in the fall footprint.
J then cuts eight support cells; cut chips move aside into bounded debris/cargo.
The remaining 64 cells transfer atomically into the existing aggregate actor,
fall/rotate, and break on conservative contact. K toggles nearby suction. The
on-screen panel shows all four ownership counts; Escape permits GUI interaction.
Aggregate suction uses the player's actual eye in the private frame (4m radius).
Debris pulls toward the player's eye cell, clamped to the admitted grid; outside
that grid, credit occurs at the boundary as explicitly labeled. The fixed side
collector is used only for evacuating the eight cut chips before capture.
Walking through this experimental prop is explicitly labeled: Exploration's motor
does not use this physics collider. Mature authored trees/rocks are unchanged.

Private VoxelWorld only; all 72 payload writes use Set. One aggregate, 16 debris,
8 cargo keys. Two chunk builds/update, 64 brush candidates/16 writes per physics
tick, 16 fragment candidates and 16 suction candidates. Whole aggregate capture is
one admitted 1024-candidate/64-write transaction. No duplicated ground collider:
the admitted footprint's highest quantized surface plus clearance defines a
conservative contact plane; rotated-cube contact stops and requests breakup.
This can visibly hover on uneven ground. It does not implement ground impulses.

Static/debris meshes use a double world anchor minus current render origin. Actor
uses the existing adapter with render origin translated into the private frame.
Origin sync runs after Bootstrap late update and before owned physics steps.
Outside a 10km render window all motion/rendering suspends with the ledger retained;
returning rebases/rearms it. Disabling suspends, teardown disposes owned meshes,
objects and event subscription. No reset loop, external inventory, save/load or
outside-world mutation. Cargo ends with the explicitly labeled Play session.

Native gate for coordinator: use J on a dry loaded clearing, J cut, observe aggregate
fall/contact, K until owned72/cargo72 and debris/aggregate/world0, then translate
render origin, disable/re-enable, and Stop. Check compilation, no errors, conserved
counts, bounded objects and correct terrain alignment. Do not claim native success
from api-check.json. Existing mature-tree destruction remains future work.

The build_candidate.py file preserves initial adaptation provenance only; do not
rerun it over the reviewed final source. check_and_freeze.py is the focused actual
Unity API compile and freezes current candidate hashes. api-check-before-clearing-
guard.json and api-check-before-player-suction.json belong to earlier sources;
api-check.json identifies final source. Only the coordinator performs native tests.
