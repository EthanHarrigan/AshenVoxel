# Atmosphere implementation evidence

Base commit: `76a8fc5d88299575f6468e5a49b16f0f952f405b`.
Source set: [source-sha256.json](source-sha256.json).

- [CPU numerical/integration output](cpu-checks.txt): independent fog quadrature,
  5,000 float32 stress cases, 100,000 shell projections, periodicity, color-space
  normalization and all five production fog integrations. Not GPU execution.
- [Runtime Unity API compilation](unity-api.json): six runtime assemblies using
  installed Unity 6000.3.27f1 references. No Editor launch, HLSL or Play test.
- [Editor fixture API compilation](editor-api.json): bounded capture/test harness
  compiles against installed Editor references; it has not been executed.
- [Project integrity](project-check.txt) and [mesh oracle](mesh-checks.txt): static
  references/GUIDs and CPU mesher regression; not native rendering.
- [Cloud research/handoff](cloud-research.md): source pages, alternatives,
  evaluation/storage bounds and cloud verification limits.

The first project check failed on two already-tracked terrain reshape files
without metadata. New `.meta` files were added for `ExplorationReshape.bytes`
and `TerrainReshape.cs`; their contents and source identities did not change.
The subsequent full project check passes. Existing GUIDs remain untouched.

Native shader compilation, captures, mesh menu, full-scene appearance, horizon
continuity, edit interaction and frame-time/memory acceptance remain open.
Follow [the native procedure](../ATMOSPHERE.md). Historical runs and downloadable
ZIPs are unchanged and do not test this revision.
