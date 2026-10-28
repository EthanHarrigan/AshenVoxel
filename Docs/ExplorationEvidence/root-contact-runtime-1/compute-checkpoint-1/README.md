# Root compute integration checkpoint1 — isolated, not installed

The actual mature-redwood composite reducer now passes both specimens through
all five LODs with exact canonical geometry, material, intrinsic RGB, AO and
topology. Its pure managed successor publishes immutable compact arrays, holds
one private coarse world at a time, and checks full caller freshness/cancellation
around bounded phases and mesh work. No Unity object is created by these jobs.

| Component | Evidence | Boundary |
| --- | --- | --- |
| Actual canonical reducer/composite | [Checkpoint](root-contact-composite-lods/checkpoint.json) | Two actual specimens; earlier failed seams retained |
| Immutable array mesher | [Final report](root-contact-array-mesher/runs/20261027T132524372509Z/report.json) | 254 chunks/all10 lower meshes;2,619,718 mostly element checks |
| Managed composite | [Report](root-contact-managed-composite/runs/20261027T134408961318Z/report.json) |164,394 checks; one private coarse world, no Unity/shim |
| Compact companion reader | [Corrected report](root-contact-template-format/runs/20261027T134523587957Z/report.json) |140,751 checks +13 independent stream cases; trusted manifest pins |
| Companion-to-composite bridge | [Report](root-contact-template-format/bridge-runs/20261027T134605781423Z/report.json) |336,508 checks; all10 exact lower meshes from compact data |
| Bare terrain capture | [Corrected report](root-contact-terrain-adapter-v2/runs/20261027T133746179096Z/report.json) |10,954 existing +68 layering checks; requires candidate readonly accessor |
| Upload preparation | [Initial report](root-contact-upload-prototype/report.json) |API compile/synthetic checks only; initializer ownership defect found, not approved for native use |

All five lower mesh arrays retain1,589,976/1,644,512bytes per recorded placement.
These are exact populated array elements, not native/GPU/CLR process costs.
The companion files are480,819/508,934bytes. Bridge-created shared composite
templates declare178,150/190,246element bytes; conversion temporaries separately
124,972/133,452bytes. Peak declared composite elements were4,762,808/4,664,068bytes
in these two jobs. Counts and declared bytes do not establish wall time, native
residency, allocator behavior, or GC backlog. The older root24MiB reservation does
not implicitly include new templates, meshes, uploads or retained old resources.

Reviews preserved and corrected two defects: the first terrain adapter accepted
snow beneath a detail wrapper; the new candidate rejects that chain while retaining
all valid detail layers. The first companion reader allowed a throwing CanRead
accessor to escape; the corrected reader contains it. Earlier reports/sources and
failure reproductions are unchanged. The original uncompact mesher's oversized
buffers are also retained as superseded evidence. The upload helper's Mesh object
initializer can leak ownership if a setter throws; an isolated correction is in
progress and this first helper is NOT native-approved.

The accepted V4 native images remain a separate, earlier LOD0-only static result.
They do not test these newer composite, reader or upload revisions. Production
worker scheduling, resource admission, per-submesh instance batching, atomic
render/body/resistance publication, geometry version/provenance, wind/motion and
all-LOD travel/FPS remain open. Default far billboard atlases are unchanged.

Files with.bin/.avrc extensions here are bounded data fixtures, never executables.
Compiled libraries, runtime configurations and test binaries are omitted. Recorded
SHA identities of those historical binaries remain in reports. Scripts preserve
original development paths and are provenance, not standalone portable runners.
See manifest.json for this overlay's exact included bytes. The live game, packs,
worlds, scenes, settings and user preferences were not modified by this overlay.
