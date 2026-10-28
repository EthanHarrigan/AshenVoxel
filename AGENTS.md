# Agent entry point (updated 2026-10-28)

Read **START_HERE.md → MEMORY.md → TODO.md**, then Docs/FILE_MAP.md. Docs/AI_HANDOFF.md
and Docs/History/ are deep references. Work only in this folder: sibling copies
(AshenVoxel-WorldVoxels) are superseded and must not be revived. Keep MEMORY.md and
TODO.md short and edit them in place; do not stack dated blocks. Put narrative in
Docs/SESSION_LOG.md. Ask before driving Unity Play/Computer Use.

---
Original entry text follows (still valid apart from the reading order above):

# Agent entry point

Read **MEMORY.md → TODO.md → Docs/AI_HANDOFF.md** when joining or resuming.
MEMORY.md is the short current-state record; TODO.md distinguishes unexecuted
acceptance gates from future work; the handoff contains provenance, pitfalls,
validation commands and recovery paths. Use Docs/FILE_MAP.md to find code.
README.md is the user launch guide. Read Docs/DECISIONS.md before architecture changes.

Current checkpoint: main Assets/Scenes/Exploration.unity contains an integrated
local beta with native compile/Ready and bounded mountain-render evidence. Read
MEMORY.md for the current control block, source identities and remaining gates;
do not infer full-profile FPS, feature interaction or target visual acceptance.
Historical downloadable ZIPs are unchanged. Source, render and authoritative10cm
matter resolutions remain distinct; ordinary trees are not yet destructible.

Older recovery/update files record historical checkpoints; pending statements
inside them are not current status. Follow the current-state reading order above
and verify source hashes rather than treating an old plan as implementation.

At a meaningful checkpoint, update MEMORY.md and TODO.md with completed work,
remaining gates and evidence. Record changes in Docs/SESSION_LOG.md. Preserve
failure evidence and release identities; never relabel an earlier run or ZIP as
testing a later revision. A docs-only overlay does not change the gameplay release.

# Project invariants

Read README.md and Docs/DECISIONS.md before changing architecture. The current user requirement is **10 cm cubic voxels**. Avroid's newest posts and his comments on his own posts are the primary appearance reference; preserve evidence boundaries in Docs/AVROID_REFERENCE.md. Prior Unity conversations are unavailable.

- `Assets/AshenVoxel/Core` is authoritative matter and must have no Unity references. All mutations use VoxelWorld.Set so dirty boundaries and sleeping simulation wake correctly.
- Render meshes, collision meshes and acceleration structures are replaceable caches. Never treat an old visual mesh as current gameplay truth.
- No GameObject, Rigidbody, Material or independent Mesh per voxel. Ordinary actor/region/chunk objects are allowed.
- World coordinates in tools are meters; grid coordinates are cells. Keep conversion explicit. Default chunks are16³ cells (1.6m each axis).
- Simulation work has bounded candidates and observable remaining queues. Do not discard pending work when a budget expires. Count budgets do not prove millisecond budgets.
- Keep material mass under gravity, prevent repeated movement within a tick, and wake neighboring cells after edits. Rigid islands and thermal/fluid simulation are distinct systems.
- Generation uses seed + version + physical-meter profile. Changes to generated geometry need a version change and validation; existing snapshots retain material/appearance state and original generator provenance.
- Save-format changes need corruption/truncation/size-limit validation and an explicit migration decision. Never claim exact resumed replay when only materials are persisted.
- A new renderer must earn adoption on the same scene/edit sequence with correct depth, shadows, normals and edit invalidation. Keep the ordinary URP renderer until comparisons justify replacement.
- Preserve .meta GUIDs, scene references, pinned package versions and assembly boundaries. Do not commit Library, builds or test binaries.

# Verification

After core changes run `python3 Tools/run_core_checks.py`. After asset/reference changes run `python3 Tools/check_project.py`. Rendering changes require the Unity mesh regression menu and visual playtest when Unity is available. See Docs/PLAYTEST.md for executable batch entrypoints. Core benchmarks are in Tools/Benchmarks; record source hashes and label observations as container/.NET measurements.

Unity was unavailable during initial authoring. Preserve that limitation until an actual Editor/player verification is performed; standalone C# tests and offline previews are not Unity import/render tests. Update Docs/VALIDATION_CORE.md after material core changes and regenerate actual-data previews when generation changes. Keep assumptions and rejected alternatives discoverable for the user.

Tree work: read Docs/TREES.md and Docs/TREES_RESEARCH.md. Keep one shared genome/skeleton/raster path; preserve branch taper and support. Intrinsic colors are opaque sRGB 0xFFRRGGBB, separate from material identity and dynamic lighting. Color edits must not wake granular physics. Snapshot Format 2 accepts Format 1 migration. Run Tools/run_tree_checks.py, Tools/run_mesh_checks.py and Tools/run_authoring_checks.py after relevant changes. Regenerate actual-data gallery/provenance after geometry changes; artist preset assets must match their intended compiled preset versions.
