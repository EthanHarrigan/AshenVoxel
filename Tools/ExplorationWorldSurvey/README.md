# Shipped-world habitat survey

Run `python3 Tools/run_exploration_world_survey.py` from the project root with .NET SDK 8 and Python 3 available. The tool reads the actual shipped height, physical-climate and source-biome archives using `exploration-world.json`, builds the same immutable `ExplorationAtlasField` as the bootstrap, and evaluates the default `SurfaceProfile` and forest seed 714103 on a 640 m cell-center lattice over the whole map.

`Docs/exploration-world-survey.json` contains point counts for every habitat/stage and source biome, plus three nearby example coordinates for each forest habitat/stage. Examples favor proximity to the recommended spawn, require dry ground with slope at most 30 degrees, and verify clearance for the default 0.28 m-radius, 1.8 m-tall body against the actual full-resolution hard-material prototype spans. Each example also verifies matching tree species/stage instances in surrounding population cells. Y is a proposed player-feet height, including snow and the default contact skin.

The report records exact source and input hashes. It changes no runtime settings or generation. Counts are sampled points, not exact biome areas; examples describe static baked-data walkability, not a native Unity movement or graphics test. Locations outside the bundled fine patch still use the explicitly reported coarse learned terrain.
