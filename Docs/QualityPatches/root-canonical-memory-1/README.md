# Canonical root upload memory candidate1

READY, NOT INSTALLED. The canonical upload now reads a validated packed scalar
view rather than constructing a complete duplicate float position/normal/color
buffer before Unity's conversion arrays. Original source/split indices and sparse
crossing vertices remain owned until upload ends; lower meshes are unchanged.

One actual model11 LOD0 comparison:3,562,010vertices, exact positions/normals/RGB/
material/AO/submesh indices/bounds. Current-thread allocated bytes121,116,096 ->
21,376,528, a99,739,568byte reduction. Eliminated full vertex payload99,736,280bytes.
These are .NET observations and declared elements; the previous native abort is
not attributed conclusively and native peak reduction is unverified. Actual pinned
Unity API compilation passes. No broad unchanged tests, newagents or research.

Three existing source changes only, same generator/geometry/cache identity,
no asset/GUID/budget/default change. Root startup option stays false. Serialized
coordinator native slot and one agreed targeted changed-workload retest with
roots explicitly enabled in the copied profile remain pending. Guard thresholds
and original scene/settings/preferences must remain unchanged.
