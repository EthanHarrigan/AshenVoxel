# Pitch-aware terrain priority candidate

Based on the installed `startup-lod-1` runtime on 2026-10-27. Only
`Assets/AshenVoxel/WorldScaleRuntime/WorldExplorerVoxels.cs`, method
`InVoxelView`, changes. Production Assets remain untouched. Exact installed
baseline and candidate SHA256s are in `source-manifest.json`; the narrow diff is
`pitch-visibility.patch`. Existing .meta GUIDs must be preserved on integration.

The old heading cone uses horizontal FOV plus20 degrees regardless of pitch.
Until the camera is within roughly11.5 degrees of vertical, that can label
frustum-visible tiles as rear terrain and hold them at32 columns. For example,
at pitch60 degrees down, FOV75,16:9 and eye(0,1000,0), screen NDC(.9,-.9)
intersects ground(1013.54,-80.96), inside level5 tile(1,-1), but the old cone
rejects that tile.

The replacement projects the camera's forward/right/up ray rectangle into
horizontal heading coordinates. For normalized heading `b`, bound the minimum
forward component by `|forwardXZ| - |right·b| tanH - |up·b| tanV` and maximum
lateral magnitude by `|right·perpendicular| tanH + |up·perpendicular| tanV`.
When the minimum is positive, their ratio bounds every ray's heading. When it
is nonpositive, retain every heading conservatively. The existing20 degree
guard, near guard and tile footprint circumsphere stay in place. This handles
roll as well as pitch, requires only bounded scalar math, and matches the old
classification at level pitch/roll. Both build priority and quality selection
already use this helper, so both benefit.

No new cache, frustum-plane array, worker, queue, runtime setting, source sampler
or mesh algorithm was added. Memory admission and32m contact urgency/fairness
are unchanged. Correctly classifying more visible tiles can increase demand
within the existing detail allowance; it does not promise every tile meets the
pixel target. This is not a finding about which specific screenshot pixels
were misclassified.

Verification uses frozen source snapshots under
`.terrain-cache/progressive-lod/runs`:

- `20261027-070916-744739-production-overlay-PitchProgram/result.json`:8 checks
  pass,356,400 ray/tile cases across1,188 pitch/FOV/aspect/yaw/roll configurations,
  **zero candidate misses versus35,124 legacy misses**. Rays include viewport
  edges/corners, three depths and four tile levels.2,592 level-camera heading
  comparisons match the baseline;36 rear-view cases preserve32-column quality
  and lower priority.10,000 warmed classifications allocate zero managed bytes.
- `20261027-070940-737045-production-PitchProgram/result.json`: expected negative
  control **FAIL** against the installed old classifier, preserving the35,124
  misses and known pitched-view case. Do not relabel this as candidate testing.
- `20261027-070940-714006-production-overlay-progressive/result.json`: all30
  progressive lifecycle/capacity/fairness/rapid-replan checks pass.
- `20261027-070940-714006-production-overlay-api/result.json`: all five runtime
  assemblies compile against installed Unity6000.3.27f1 APIs/C#9/netstandard2.1.
- The first check harness compilation at `20261027-070854-095850` failed because
  shim Camera aspect/pixelHeight are read-only. The harness now sets the shim's
  Screen dimensions. That failed source snapshot remains preserved.

No native rendering, Editor/Play control, screenshot match or FPS claim. The
ray/AABB oracle exercises the actual runtime helper under numerical Unity API
substitutes; it is independent of the heading-bound formula.

Reproduce:

```powershell
python Tools/ProgressiveLod/run_checks.py --production --overlay-dir .terrain-cache/startup-throughput/pitch-candidate --program Tools/ProgressiveLod/PitchProgram.cs
python Tools/ProgressiveLod/run_checks.py --production --overlay-dir .terrain-cache/startup-throughput/pitch-candidate
python Tools/ProgressiveLod/run_checks.py --production --overlay-dir .terrain-cache/startup-throughput/pitch-candidate --api
```

Deferred planner issue: roots are distance-filtered, but splitting retains all
four children even when some no longer intersect the requested distance disk.
Those leaves consume the bounded tile budget. The511-tile native spawn plan
recorded147 still-needed splits. Changing this requires balanced-coverage tests
and is deliberately outside this patch. The pitch fix also does not split large
leaves or raise the256-column ceiling.
