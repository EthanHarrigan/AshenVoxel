## Global distant sweeps and large-tree cuts — October 27, 2026 (Denver)

Outer two active LOD levels now refine globally one rung at a time (8→16→32→64,
up to the pixel target/256 cap), retaining quality when the camera turns. Visible
fallbacks refine during a pending larger plan: one refinement per two coverage
completions; urgent nearby ground remains first. Deferred lower rungs retain their
barrier; downshifts, source refresh and retirement remain supported. F1 and saved
diagnostics show sweep progress. This is mesh refinement, not new full-view 5m data.

Tree input no longer replays released activation clicks or queues repeated hand
cuts behind support work. Certified connected props use local neighbor support
searches; live support work gets a 1.25ms checked slice per Update. Existing falling
bodies remain dynamic. A bounded exact hard-surface stump collider (3.3×3.3×1m)
restores contact near the cut; there is no artificial native rotation or torque.
Only fully disconnected sections fall; narrow connected supports still hold, and
a balanced severed section can remain upright. Whole large detachment, first
activation/mesh preparation and full-scene FPS still require acceptance.

70 terrain checks and the full CPU matter suite pass; the mature 1,159,639-cell
bark fixture removed38 cells using1422 support candidates (~1.15ms standalone CPU;
one-time constructor/connectivity preparation ~734ms on background task). Four
actual Unity API assemblies and250-asset/13-assembly integrity checks pass.
The Editor consumed the mesh-regression job at04:12:40UTC. New free-fall and stump
native probes were prepared but NOT run: user physical Escape stopped Computer
Use during Play entry. Passive04:13:44UTC state says playing=true; no subsequent
UI action was issued. No request file was armed. Prior native tests are not
acceptance for this revision. Saved settings, scene, source data and ZIPs unchanged.

The previous memory-blocked distant-ground report remains unresolved as a native
acceptance gate. Sweeps obey existing memory reserves and cannot promise zero lag.
Evidence: [checkpoint](combined-source-sha256.json).

Files: progressive-result.json/progressive-checks.log retain frozen scheduler
source provenance and failures. combined-unity-api.json records final full API
compilation. combined-source-sha256.json pins the combined changes. The Editor
snapshot is passive evidence only, not a completed playtest.
