# Executed world-scale evidence

All evidence is separate from native Unity validation. No Unity Editor/player or
GPU frame-time result is implied.

| Check | Executed result |
| --- | --- |
| World data / LOD / Python→C# atlas | 56 checks passed; C#9/.NET Standard2.1 compile passed |
| Azgaar importer | 49 checks passed; actual Ceteviles import verified |
| Explorer / remote client | 42 checks passed, including actual provider HTTP consumption |
| Provider contracts | 49 checks passed with an explicitly fake backend |
| Upstream conditioning preparation | 19 checks passed using the actual upstream converter |
| Canonical pretrained output | 7/7 checks passed; exact tested overlap and disk replay |
| Actual Ceteviles-conditioned output | 5/5 checks passed; 5,145.75–5,803.96 m |
| Project references | 74 assets, 9 assemblies valid under static checks |

- [World data checks](../world-scale-validation.json): engine-independent global
  coordinates, atlas/CRC, min/max summit bounds, implicit matter and LOD tests.
- [Explorer checks](../world-explorer-validation.json): actual runtime C# under
  Unity substitutes, transport fixtures, edge-gap regression and live HTTP probe.
- [Current canonical model validation](canonical-neural-validation.json): actual
  pretrained output, exact height/climate overlaps and disk replay. The final
  code check reused an existing verified owner cache; its warm timings are not
  cold neural inference.
- [Historical cold owner run](canonical-neural-cold-historical.json): recorded
  185 s cold CPU work before later bridge preparation/validation refinements.
  Its exact older tool hashes are retained rather than relabelled current.
- [Ceteviles model report](Ceteviles-neural.report.json): actual user-map
  conditioning and pretrained output; adjacent climate data is
  `Ceteviles-neural.climate.npz`.
- [Ceteviles-conditioned checks](Ceteviles-conditioned-validation.json): source,
  model, input/output checksums and actual end-to-end verification.
- [Imported Ceteviles atlas](Ceteviles-import.json): source hash, physical
  dimensions, biome preservation and water-coverage repair.
- [Raw-query diagnostic](../VALIDATION_INFINITE_DIFFUSION.md): successful real
  neural output plus the retained strict raw crop-equality failure.

Bundled small neural and macro atlases are under
`Assets/AshenVoxel/WorldScaleRuntime/Samples`. Original source/map archives,
model weights, dependency environments and large generation caches stay outside
the project. The private mod transfer was inspected, not redistributed wholesale.

Reproduce standalone checks from the project root:

```sh
python3 Tools/WorldScaleImport/check_import.py
python3 Tools/run_world_scale_checks.py --atlas Tools/WorldScaleImport/fixtures/azgaar-synthetic.avwf
python3 Tools/run_world_explorer_checks.py
python3 Tools/DiffusionBridge/check_bridge.py
python3 Tools/check_project.py
```

The optional `--live-url http://127.0.0.1:8765` explorer check needs the prepared
real service and known cached region. `check_azgaar_prepare.py` and
`check_real_bridge.py` need the documented inference environment; their exact
arguments are in the provider README. Fake contract checks and actual model
execution are reported separately.
