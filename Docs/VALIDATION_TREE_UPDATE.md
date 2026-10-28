# Tree fidelity update: executed verification

The update keeps the 10 cm material grid and Unity 6.3 LTS baseline. Generation5 refines mature redwoods and physical bark relief in the shared species system and coherent terrain colors; snapshot Format 2 preserves optional intrinsic pigment while accepting Format 1 material snapshots.

| Check | Executed result | Scope |
| --- | --- | --- |
| Core checks | 87 passed | Storage, halo invalidation, colors, physics, persistence/migration, generation |
| Core API compilation | C# 9/.NET Standard 2.1 and external-write probe passed | Engine-independent compatibility and mutation boundary |
| Tree checks | 73 passed | Species, taper, connected matter, fruit, seeds, age, budgets, snapshots |
| Authoring checks | 40 passed | Actual profile/control mapping with Unity API substitutes |
| Mesh oracle | 27,208 faces with RGB and 108,456 AO values matched | Actual mesher with geometry substitutes; 12 randomized worlds |
| Picking oracle | 11,637 comparisons passed | Exhaustive cell-box intersections against actual DDA |
| Offline gallery traversal | 10,000 rays and boundary fixtures passed | NumPy/optional Numba inspection backends |
| Final gallery | 13 of 13 sample exports untruncated | Seeds, bounds, parameters and actual data hashes recorded |
| CPU path trace study | Four datasets/images passed input, surface, color and provenance checks | Mitsuba CPU renderer, 64 samples/pixel; not Unity |
| Project integrity | 54 unique assets, 6 assemblies; scene/profile/material references valid | Static files/GUIDs, not native asset import |

Core, tree, mesh and authoring checks executed after the mature-redwood changes. Authoring checks include physical relief controls, conservative preview bounds and camera pan/reset behavior. The final benchmark and image provenance record their source SHA-256 values. The legacy world inspection images were also regenerated from Generation5 with actual stored colors.

## Current cost observation

The optimized container benchmark reports generation p50 about1.07 s for the default colored mixed forest. Material arrays occupy 5,484,544 bytes and optional tint arrays 21,938,176 bytes before metadata, meshes and graphics. Fixed 1800-candidate gravity calls have p95 about3.81 ms in this workload. Snapshot timing includes the larger material/appearance payload. This finite arena contains no full-size redwood. The separate mature-redwood fixture has more than1.1 million wood/leaf cells and synchronous generation; see [redwood update](REDWOOD_UPDATE.md). Read [raw results](Benchmark/RESULTS.md) for source hashes, environment, sample counts and limits.

These are CPU observations while other development work could run. They are not Unity frame rates, target-machine guarantees or a performance comparison against Avroid. The additional geometry and pigment are an explicit fidelity cost. Optimize through the same correctness/gallery fixtures after measuring Unity mesh upload, draw cost and GPU time.

## Still unverified or intentionally absent

Unity Editor/package restore, native asset deserialization, shader compilation, workshop inspectors, play mode and desktop builds were not run because Unity is not installed here. Test API substitutes do not establish those results.

The CPU path-traced samples demonstrate a lighting study on actual geometry; no path tracer or GI system was added to the URP game renderer. Tree growth is a bounded constructive generator, not competitive biological growth or space colonization. Wind animation, rigid tree/branch collapse, persistent branch ownership and a streaming forest remain later systems.

Recent Reddit post/comment text was inspected. Reddit media requests still failed; no recent forest video-frame match is claimed. The [research notebook](TREES_RESEARCH.md) distinguishes direct evidence, older author comments, procedural modeling papers and project decisions.
