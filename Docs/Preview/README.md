# Offline world inspection previews

These PNGs are generated from **the actual project core** at seed `714103`, a
256 × 128 × 256-cell domain, and **0.1 m cells**. They are software-rendered CPU
inspection images, **not Unity screenshots**, performance measurements, final art,
or evidence that the Unity shader has compiled. Their lighting is an illustrative
ray-cast sun/ambient/fog model and does not reproduce the URP shader.

- `forest-overview.png`: unchanged current-generator forest and layered terrain.
- `material-section.png`: exact central material slice, with no lighting model.
- `terrain-edit.png`: a 2 m core spherical carve plus a 0.6 m sand placement,
  followed by 16 actual `Simulation.Step(5000)` calls. This brush combination
  illustrates core editing; it is not a capture of the boss attack animation.

Reproduce from the project root, with a .NET 8 SDK in the workspace `.dotnet`
directory and Python `numpy`/`Pillow` installed:

```sh
python3 Tools/Preview/render_preview.py --width 1100
```

The exporter invokes the same `Generation`, `WorldEdits`, and `Simulation` C#
sources that Unity uses. Temporary raw volumes and assemblies live under
`Tools/Preview/bin` and should not be committed. The renderer traces exact voxel
cell boundaries for primary and sun rays. The pictures inspect world composition,
voxel scale, material strata and edit results; Unity import, visual review and
profiling still need the Editor playtest protocol.
