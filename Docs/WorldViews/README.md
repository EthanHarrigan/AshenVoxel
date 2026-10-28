# Actual world generation and LOD visuals

The five rendered views use exact meshes produced by the C# WorldExplorer
builder, with the real fields, planner, vertex colors and triangle topology.
They are offline CPU lighting renders, not Unity screenshots or GPU benchmarks.
No trees, decorations, replacement mountains or concept art were added.

- `terrain-lod-comparison.png`: the same mountain view with normal runtime
  colors and diagnostic LOD colors. It uses the analytic fallback, not diffusion.
- `lod-layout.png`: actual selected leaves over64 km and a2 km close view.
- `lod-altitude-comparison.png`: actual plans6 m and6000 m above the same summit.
- `analytic-high-view.png`: the higher observation view.
- `ceteviles-observer-view.png`: the imported authored macro field before
  neural refinement. Its large plateaus/low-detail appearance are real limits
  of that rough field, not final environment art.
- `neural-study-view.png`: the actual Ceteviles-conditioned33×33 learned
  region,960 m across at30 m native sampling. This finite top-surface study
  omits boundary skirts and is not a generated32 km neural landscape.
- `imported-map-and-biomes.png` and `learned-heightfield-data.png`: source-map
  and actual learned-field diagnostics from the executed integration.

Each height-proxy tile has32 quads per side. LOD0 samples every2 m; coarser
levels double that spacing. Authoritative editable matter remains10 cm; these
images do not demonstrate a complete100 m microvoxel ring. The planner cap is
384 tiles; captions expose the actual selected count and pending refinements.
Camera positions were chosen through the same sampler for clear observation,
rather than using the default coarse spawn search. They are reachable by the
implemented free-flight camera.

Offline illumination uses Mitsuba CPU path tracing, geometric face normals and
illustrative aerial perspective. Unity's exported smooth normals remain in the
data but are not used for this lighting study, so facets can be more visible.
It does not execute the URP shader or establish Unity appearance.
Source/binary/image hashes and camera settings are recorded alongside images.

Reproduce with the installed .NET8 SDK, NumPy/Pillow/Mitsuba dependencies:

```sh
python3 Tools/WorldViews/export_views.py
python3 Tools/WorldViews/lod_diagrams.py
python3 Tools/WorldViews/render_views.py analytic-observer
python3 Tools/WorldViews/render_views.py analytic-observer --lod
python3 Tools/WorldViews/render_views.py analytic-high
python3 Tools/WorldViews/render_views.py ceteviles-observer
python3 Tools/WorldViews/render_views.py neural-study
python3 Tools/WorldViews/package_views.py
```
