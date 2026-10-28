# Five seeds of each procedural tree

Twenty individual800×1000 renders: five birches, five oaks, five mature coast
redwoods and five pines, using the same species presets and only changing seed.
Each species sheet labels seed and height. Panels are individually framed;
a redwood is much larger than a birch in real-world scale.

These images trace actual generated10 cm voxel geometry and stored colors with
Mitsuba CPU lighting. They are not Unity screenshots or AI-generated images.
All20 exports completed without truncation. `samples.json`, source provenance
and individual render reports record parameters, hashes and rendering settings.

Reproduce: `python3 Tools/TreePathtrace/render_seed_study.py` from project root.
Requires the dependencies listed in the TreePathtrace tool. Existing frozen
TreeGallery inputs and images are preserved.
