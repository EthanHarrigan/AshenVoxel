# Climate units correction and baked archive

The original upstream `WorldPipeline.get` returns an elevation-adjusted surface
temperature in Celsius, but its second climate plane (temperature seasonality)
remains **Celsius × 100**. The earlier bridge preserved those raw values while
advertising the historical name `temperature_std_c`. Height-only consumers did
not use the values. The new terrain-surface classifier does, so accepting those
values as Celsius would reject real regions or produce incorrect snow.

The actual retained outputs establish this directly:

| Executed model region | Raw seasonality | Correct physical seasonality |
| --- | --- | --- |
| Generic neural sample | 653.639–663.908 | 6.536–6.639 °C |
| Ceteviles-conditioned sample | 1076.167–1079.516 | 10.762–10.795 °C |

This is an interface-unit correction, not a new climate simulation or a change
to the pretrained weights. Channel 0 is already altitude-adjusted by the model;
applying the lapse rate to its original height again would be another error.

## Explicit wire versions

The byte layout remains ATDR0001. Climate interpretation is versioned by the
immutable job's `climate_encoding`, which is copied into region metadata:

- Missing encoding or `upstream-native-v1`: retained legacy raw values. The
  second plane must be divided by 100 once at the physical consumer boundary.
- `physical-si-v2`: new jobs normalize the second plane once in `RealBackend`,
  before canonical owner caching. The consumer uses it directly.

`climate_value_units` is also emitted. New physical arrays use
`[degC, degC, mm/year, percent, degC/m]`; explicit native arrays use
`[degC, centi_degC, mm/year, percent, degC/m]`. Unknown encodings and contradictory
unit lists are rejected. Consumers must inspect the encoding, not guess by
numeric range. Job identity includes the new field so old raw owner caches
cannot be mixed with normalized owner caches.

Legacy job identity is not rewritten. Old verified owner files remain readable
as native values. Historical model reports and arrays are retained as evidence
of what actually ran; they are not silently relabelled normalized output.
The corrected bridge also validates physical ranges before publication.

## Paired baked climate

`export_climate_archive.py` converts an executed AVWF/NPZ/report triple into
AVCL0001, a physical-only archive with five float32 planes. It verifies exact
height and climate input hashes against the original bake report, world ID,
dimensions, origin and native spacing, then explicitly converts the recorded
encoding. The archive has CRC32 and a SHA256 link to the exact AVWF height file.

The C# `ClimateAtlasField` decorates the matching `AtlasWorldField` and implements
`IClimateWorldField`. A wrong atlas, shifted grid or changed height payload is
rejected, even if a filename and dimensions happen to match. Horizontal resize
uses the same transform for height and climate. Independent vertical resize
applies beta only to the difference from the original above-sea elevation;
original surface temperature is not adjusted twice. Extreme rescaling outside
the declared physical climate range reports climate unavailable, allowing the
surface system's explicit fallback rather than inventing a measured value.

The archive stores no mutable array references and uses a separate format, so
the existing AVWF height schema does not change. Maximum arrays are bounded;
loading a full-size climate archive is a preparation operation, not frame work.

```sh
python3 Tools/DiffusionBridge/export_climate_archive.py \
  terrain.avwf terrain.climate.npz terrain.report.json terrain.climate.bytes
python3 Tools/run_climate_checks.py --height terrain.avwf --climate terrain.climate.bytes
```

Validation results are recorded in `climate-validation.json`; bridge unit tests
explicitly use a fake backend for transport checks. Real pretrained execution,
legacy cached model consumption and native Unity behavior are separate evidence.

## Executed verification

The corrected adapter passed61 fake-backend contract checks, including legacy
metadata absence, explicit plane units, cache identity isolation and rejection
of centi-C values falsely tagged as physical seasonality. The actual archive
reader/decorator passed27 C# checks with the retained Ceteviles pair, plus
C#9/.NET Standard2.1 compilation.

A fresh real pretrained physical-si-v2 canonical query passed7/7 inference,
overlap and disk-replay checks. It took196.33 s on four CPU threads in this
container. Comparing the new region against the prior same-region model bake:
height samples and climate planes0,2,3,4 were bit-identical; new plane1 was
bit-identical to the prior native plane multiplied by0.01. Seasonality is
6.536–6.639 °C. Thus this observed correction changes the intended unit scale,
not terrain height or the other climate channels. The raw execution artifacts
are under `/workspace/climate-bridge-si-v2` in the authoring environment.

The selected30 m checkpoint, source revision, statistics, package versions and
world identity remain recorded in that execution report. GPU/native Unity,
adjacent-owner behavior and a complete long-running world remain separate
validation; these small checks do not establish them.
