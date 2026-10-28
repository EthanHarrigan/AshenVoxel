# Independent shared-upper split: all packed redwood LODs

The frozen `SharedUpperSplit` is **not mechanically compatible with every packed
LOD at its 30-cell / 3 m seam**. It accepts LOD0 and LOD1 for mature redwood IDs10
and11. It correctly rejects LOD2, LOD3 and LOD4 for both trees because crossing
quads have varying packed AO. Their RGB, material IDs and normals are uniform;
the failing attribute is AO in every observed nonuniform crossing.

All ten packed LODs were examined against immutable main AVPM4 SHA256
`41556b474ce7a26342e4dd7bcff2e4fe658ab0333e8360058262ee21c0f011aa`.
The independent verifier completed 34,108,523 assertions. The original helper,
original tests/script/report, Core/reader sources and pack bytes remain unchanged.
Only this supplemental folder was written. No main or Assets writes, native
execution, Unity UI, or new agents were involved.

## Exact seam30 results

| Tree ID | LOD | Crossing quads | Nonuniform AO crossings | Result | Extra vertex bytes | Extra index bytes |
|---|---:|---:|---:|---|---:|---:|
|10|0|60|0|Accepted|3,360|1,440|
|10|1|29|0|Accepted|1,624|696|
|10|2|52|33|Rejected|N/A|N/A|
|10|3|16|13|Rejected|N/A|N/A|
|10|4|12|6|Rejected|N/A|N/A|
|11|0|59|0|Accepted|3,304|1,416|
|11|1|31|0|Accepted|1,736|744|
|11|2|46|24|Rejected|N/A|N/A|
|11|3|15|10|Rejected|N/A|N/A|
|11|4|11|4|Rejected|N/A|N/A|

Byte arithmetic uses 28 GPU bytes per appended vertex and 32-bit indices. The
accepted four results add 14,320 bytes in total **if** represented with one shared
vertex buffer and partitioned index submeshes. This is not measured VRAM or a
native upload. Splitting also allocates complete replacement upper/base index
arrays: 20,743,824 / 4,464,062 bytes for ID10 LOD0/1, and 21,374,066 / 4,576,858 bytes
for ID11 LOD0/1, including packed extra-attribute arrays. Two independent Unity
Meshes duplicating the original vertices would have a very different cost.

## What is verified

Untouched quads retain exact original index order. Crossing quads must retain
uniform normal and packed color; each clipped output rectangle has exactly the
expected four corners, outward nondegenerate winding, exact area, and a shared
diagonal so overlapping triangles cannot conceal a hole. Upper and base pieces
share the same two seam vertex indices. Every output triangle and appended vertex
has an owner. Horizontal positive-Y faces at the seam belong only to the base;
negative-Y faces belong only to the upper part. Input arrays are hashed before and
after. Independent adversarial cases verify RGB, packed material/AO, and normal
variation is rejected without mutating source data.

## Geometric limitation even where clipping passes

LOD1's cut contour differs substantially from its own LOD0 contour. Contours are
sets of signed, axis-aligned unit edge segments at the original authored seam;
they measure source geometry, not pixels or proposed new root-contact geometry.

- ID10 upper: both have188 unit segments, but118 fine segments are missing and118
  coarse segments added. Base:120 missing /124 added.
- ID11 upper: both have188 unit segments, but123 fine segments are missing and123
  added. Base:142 missing /140 added.

The per-LOD split can therefore preserve its original geometry perfectly while
still failing to meet a different LOD's base. These counts are not a measured gap
length or proof of overlap when two different surfaces are paired. Deriving a
coarse root-contact base on the same original baker lattice, or another explicitly
verified transition, remains necessary. LOD2–4 additionally require an aligned
seam or explicit shading reconstruction; silently flattening/interpolating packed
AO is not validated by this helper.

No runtime LOD pairing, impostor transition, collision, wind, normals/shadow pass,
native Mesh/submesh behavior, or new-root compatibility is established here.

Run: `python -X utf8 .terrain-cache/root-contact-alllod-checks/run.py`.
`all-lods.json` and `provenance.json` are the final seam30 evidence. The first
supplemental report/provenance is retained separately; the final report separates
attribute causes and uses null, not zero, for unproduced rejected output bytes.
