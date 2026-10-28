# Loading and distant coverage integration

Current local-date checkpoint: October27,2026, America/Denver. UTC reports after
midnight correctly retain October28 timestamps. No historical evidence is relabeled.

`observed-running-state.json` is the passive02:10UTC heartbeat before the final
coarse-first fix. Its population is current, but its loading subsection stopped at
02:00UTC in the earlier loader implementation. That old subsection records source
9/9, one contact,0/4096 far tiles, memory pause and1.27GiB headroom on entry. A
separate Windows counter observation at02:10UTC was43,723,919,360 committed of
44,815,704,064 bytes (~1.02GiB headroom), with7625MiB available physical RAM. Commit
headroom, available RAM and VRAM are different constraints. The fresh observation
supports continued upload pressure; it does not make frozen terrain counters fresh.

The user was able to enter/move after the earlier local-floor fix, yet the full
population could starve the4096-tile initial plan before its atomic visibility
gate. Source generation and mesh publication were incorrectly conflated in the
user-facing status. The final fix earns complete low-cost coverage before population,
then retains it through refinements. Post-entry snapshots continue and the normal
HUD exposes pauses/errors. The loading screen retains genuine stage counters,
ground-readiness samples, no-progress timing and per-run recovery controls.

Implementation and focused results:

- [Memory/local floor fix](../2026-10-28-startup-memory/README.md).
- [Whole-view coarse-first fix](../2026-10-28-coarse-first/README.md):45 progressive
  and43 source checks. The actual numerical fixture publishes127 first tiles over
  30km, then restores4096 requested tiles while old complete coverage stays visible.
- `loading-checks.json`:15 engine-free clock/progress checks; no fabricated progress
  from heartbeat, repeated status or decreasing counters; separate failure/stall.
- `unity-api.json`:four full source assemblies against installed Unity6.3 Windows
  APIs; prior unrelated Editor warnings retained. `source-sha256.json` freezes the
  final relevant code. Integrity248assets/13assemblies passed.

Native verification boundary: the previous user stopped computer control with
Escape. This continuation made no UI calls or Play restart. The running-state
evidence predates the final gate/plan fix; final native appearance/retry/convergence
and full-profile FPS remain unverified. A requested native capture did not execute
because Play had stopped; its request was archived so it cannot fire later.
No source/version, saved preferences, assets/settings GUIDs or historical release
identities were changed. UI full-model counters are additive diagnostics only.

Preimages preserve prior top-level documentation. Loading UI source/preimages are
also retained in `.terrain-cache/loading-screen-2026-10-27`; the final integration
preimages are in `.terrain-cache/distant-startup-2026-10-27`.
