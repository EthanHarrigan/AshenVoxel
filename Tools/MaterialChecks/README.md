# Material catalog regression checks

Run `python3 Tools/run_material_checks.py` from project root with .NET8 installed.
The runner compiles actual Core code with C#9 and no Unity or external packages.
It checks append-only IDs, opaque intrinsic palettes, preservation of old color
outputs, digging thresholds, snapshot validation and paired grain/color mass.

The catalog is revision3 with IDs0–23. The surface update appended IDs11–21;
ground cover appends only Fungus22 and Petal23. IDs0–21 and their existing base
colors are unchanged by ground cover. Snapshot Format2 remains unchanged; a current reader accepts old
material snapshots, while an older catalog reader correctly rejects unknown new
IDs. No save is silently relabelled with a different generator identity.

`fixtures/legacy-format2.bin` was produced by compiling the actual Core from the
previous published `AshenVoxel-InfiniteDiffusion-Worlds.zip`, SHA256
`c93e65b7955b93bc03de54aed6279f20a81743b087f0e860221476b4b4c33ff2`.
It contains all nonair historical materials with generated intrinsic colors.
Its SHA256 is `dcbfcf0a9c01964e8b1326448055c22de23935ddd04f22b24f78c07c5df3ad58`.
The builder is retained as `CreateLegacyFixture.cs`; compile it only against that
baseline Core, not together with the test program. The historical color hash
also comes from that baseline builder across1,221 material/seed/coordinate cases.

Hardness values are game tool thresholds rather than Mohs hardness or measured
fracture strengths. Density is an approximate bulk-density cue. Clay, silt,
snow and peat use the current simple granular approximation; cohesion,
compaction, snow energy balance, avalanches, melting and mechanical rock fracture
are not implemented by adding material names. Ice remains an opaque rigid
material in the reference mesh; visual water is a separate surface system.
