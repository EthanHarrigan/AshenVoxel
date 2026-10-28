# Exploration prototype bake

Local interaction overlay: after producing AVPM0002 with this baker, run
`python Tools/ExplorationPrototypes/bake_interactions.py --source Assets/AshenVoxel/Exploration/Prototypes/ExplorationPrototypes.bytes`.
It preserves every visual mesh and writes AVPM0003 hard/leaf/stick spans.
NumPy/SciPy are authoring dependencies only. See
[current interaction/LOD evidence](../../Docs/NativeLODReview/README.md).

Run from the project root with .NET SDK 8 and Python 3 available:

```sh
python3 Tools/ExplorationPrototypes/bake_prototypes.py --mode all
```

The tool snapshots current Core generators and the actual `GreedyChunkMesher`, compiles them with a numerical Unity API substitute, generates every model at 10 cm, builds conservative visual LODs, exports exact hard-material collision spans and packages the asset. Unity is not required to rebuild this data cache. The final pack is checked into `Assets/AshenVoxel/Exploration/Prototypes/ExplorationPrototypes.bytes` with a stable Unity GUID.

The complete bake contains 99 models:

| IDs | Content |
| --- | --- |
| 0–15 | Birch, oak, redwood, pine; young/mature; two seeds each. ID = species × 4 + stage × 2 + variant. |
| 100–119 | Ten ground-cover families, two seeds each. |
| 120–122 | Single-cell mushroom, herb and flower accents. |
| 200–259 | Basalt, granite, limestone, sandstone and shale; six shapes; two separately rasterized sizes. |

No instance scale changes the required cell size. Trees use 0.1/0.2/0.4/1.6/6.4 m visual levels, other models 0.1/0.2/0.4/0.8 m levels. Full-resolution collision always survives visual reduction. Both mature redwoods are generated without truncation, at approximately 74–76 m; the tool does not crop them to a small demo domain.

`quad_pack.py` losslessly transcodes each actual greedy quad to compact AVPM0002 records. It retains face normal, original corner/index order, exact decimeter positions, RGB palette entries and two-bit per-corner AO. Before packaging, it reconstructs the old expanded representation and asserts exact byte equality for every LOD and unchanged collision bytes. Each record is independently GZip-compressed, length-bounded and CRC-checked. The runtime reader also accepts the earlier AVPM0001 format.

The pack is about 8.6 MB on disk; the decoded shared GPU mesh estimate is about 358 MB before driver overhead. Detailed redwoods dominate that cost. `Docs/exploration-prototype-bake.json` records every model's bounds, cell/vertex/triangle counts, actual seed/profile, truncation flag, source provenance and round-trip hashes. `bin/` contains disposable compiler snapshots and intermediate records and should not be shipped in the project download.

`--mode trees`, `plants` or `rocks` rebuilds only that category for iteration. `--mode pack` repackages existing intermediate records. For a release with coherent current-source provenance, use `--mode all` and then run:

```sh
python3 Tools/run_exploration_population_checks.py
python3 Tools/run_exploration_streaming_checks.py
```

These tools verify generated data and numerical runtime behavior. They do not run Unity's native renderer or provide an FPS result.
