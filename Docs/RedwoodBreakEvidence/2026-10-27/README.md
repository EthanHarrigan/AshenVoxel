# Responsive redwood breaking — October 27, 2026 (Denver)

Installed in the main Exploration runtime. Rapid taps previously bypassed the
renderer-current gate and could repeatedly cancel unfinished patch builds. Taps
and holds now share one cut/support/publication boundary. Busy taps are acknowledged
but not queued; holding continues at current aim every 150 ms or when ready,
whichever is later. Release, UI/focus loss and restoration cancel the gesture.

Accepted cuts immediately pulse a localized amber 10 cm cell region on the old
surface until the exact replacement publishes, then fade for 120 ms. This is
render-only state: no permanent tint, material, depth, shadow or collision change.
Amber cursor brackets distinguish preparation/update waits; green confirms a cut.
Hold and aim supports dragging by repeated current-ray spheres; swept interpolation
between samples is not implemented, so very fast drags can leave gaps.

The exact patch worker now skips absent interior owner groups while retaining
neighbor halos for AO and faces. On the 1,159,639-cell mature redwood, 11 alternating
same-process Windows/.NET pairs give median 24.6102 to 14.5274 ms (40.97% less worker
CPU). Geometry/mask hashes match. Group passes fall 117 to 13; halo lookups fall
682,344 to 75,816. The two full ownership scans still visit 2,319,278 records.
This is not Unity frame time, click latency or a whole-game FPS gain.

Validation: 360 input-policy assertions with delayed publication; 89 renderer
assertions and paired exact geometry/mask checks; five installed Unity API
assemblies; CPU and native mesh regression; project integrity. Native Unity
6000.3.27f1 / RTX 3080 Ti preview passes: 183 pixels acknowledge the cut during
a forced one-second wait, 114 change with the pulse, and initial/final reference
comparisons both have zero differing pixels. Source geometry and conservation
remain intact. PNGs were visually inspected. This isolated native test does not
establish full-redwood Play feel, native fade/yaw/rebase, detachment or FPS.

Evidence: [input](input-result.json), [worker comparison](renderer/summary.json),
[native result](native-result.json), [sources](source-manifest.json),
[validation commands](validation-commands.json). Baselines and preliminary runs
remain preserved. The historical release ZIP, scene and package versions are
unchanged. The simultaneous Noita awake/debris work remains separately owned.

Research and future alternatives: [Noita/meshing tradeoffs](../../REDWOOD_BREAK_RESEARCH_NOTES_2026-10-27.md).
Next measured opportunities are dirty ownership summaries, fast exact local faces
followed by greedy refinement, and eventually a per-chunk edit journal. No new
renderer, GPU authority, swept-stroke carving or structural bending was installed.
