# Ashen Voxel

A Unity 6.3 LTS (6000.3.27f1) exploration game set in a ~300 × 150 km learned-terrain world
built from the Azgaar map "Ceteviles", with breakable **10 cm voxel** props and matter.

- **Start here:** [START_HERE.md](START_HERE.md). It covers status, folder map, how to play and how to brief an AI.
- Current state: [MEMORY.md](MEMORY.md). Priorities: [TODO.md](TODO.md).
- Controls and settings in depth: [Docs/EXPLORATION_GUIDE.md](Docs/EXPLORATION_GUIDE.md). Note that START_HERE lists newer controls.
- Code map: [Docs/FILE_MAP.md](Docs/FILE_MAP.md). Architecture decisions: [Docs/DECISIONS.md](Docs/DECISIONS.md).
- Previous long README, with every feature-patch note and evidence link:
  [Docs/History/README_through_2026-10-28.md](Docs/History/README_through_2026-10-28.md).

## Open and play
1. Open this folder in Unity Hub with Unity 6000.3.27f1. Allow packages and shaders to import.
2. Open `Assets/Scenes/Exploration.unity` (or use **Ashen Voxel → Exploration → Open playable world**) and press Play.
3. Close other games first. Memory headroom is the main limit on this PC.

Other scenes:
- `ForestArena`: the original dig and sand-gravity demo.
- `TreeWorkshop` and `GroundCoverWorkshop`: generators.
- `WorldExplorer`: the research scene.

## Verify
```text
python -X utf8 Tools/run_core_checks.py      # after Core changes (.NET 8 SDK)
python -X utf8 Tools/check_project.py        # after asset/reference changes
python -X utf8 Tools/run_tree_checks.py      # trees; also run_mesh_checks / run_authoring_checks
```
Always pass `-X utf8` on Windows. Unity mesh regression is under **Ashen Voxel → Validation**.
