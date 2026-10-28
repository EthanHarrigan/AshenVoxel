# Terrain adapter admission correction — isolated revision

The original frozen adapter and MAIN are unchanged. The independent failure reproduction and audit remain in `root-contact-terrain-adapter-review/runs/20261027T133240179961Z` and its `REVIEW.md`.

This revision addresses the accepted `Detail(Surface(bare))` case, which returned one meter of snow as bare support and performed11 backing calls in the recorded reproduction.

## Minimal candidate change

`TerrainDetailField.cs` is a separate copy of MAIN's file with only a read-only `IWorldField BaseField => basis` accessor and its comment added. See `TerrainDetailField.diff`. Its constructor, sample/relief logic, bounds, climate, identity, and snapshot behavior are unchanged. No MAIN source or `.meta` was modified.

`RootTerrainSnapshot.Bare` now inspects the supported `SurfaceWorldField`/`TerrainDetailField` chain without reflection:

- Consecutive outer surface wrappers are stripped as before.
- Detail wrappers remain in the sampled/captured field; inspection follows their read-only basis solely to validate order.
- Any surface wrapper below a retained detail wrapper rejects with `nested-surface-wrapper`, even if its current profile has snow disabled.
- The whole inspected chain, including surface and detail wrappers, has an eight-wrapper depth limit and repeated-reference cycle guard.
- Source and returned snapshot chains both undergo this check.

The terminal provider still has the existing trusted immutable snapshot, never-reused revision identity, and bare-height semantics contract. This change verifies the known wrapper chain; it does not certify arbitrary opaque provider internals or accept new opaque assertions in place of that contract. There is no identity-string parsing, reflection, dropped relief, alternate quantization, or relaxed sample-count requirement. Accepted valid chains retain the same policy/content identity because their geometry and sampling semantics do not change.

## Validation

`runs/20261027T133746179096Z/report.json` records C#9/netstandard2.1 compilation, all10,954 unchanged original regression checks, and68 new layering/accessor/freshness checks. The run contains frozen candidate source copies.

The original suite preserves1,715 exact MAIN `SurfaceTop` comparisons, including precise quantization boundaries, negative/large coordinates,542 snow cases,478 detail-sensitive tops, stale/capture/sample errors, cancellation, and work limits.

New cases cover normal outer Surface wrappers; multiple retained Detail wrappers; mixed valid chains through depth8; reversed and alternating Surface-under-Detail chains; dry-profile inner surfaces; an invalid chain returned by a provider snapshot; unsupported terminal providers; whole-chain depth9; live revision changes; cancellation; and exact read-only accessor ownership. All valid chain queries retain exactly one backing height sample. Rejected nested surfaces perform no height sample. The sealed wrappers' read-only constructor graph cannot ordinarily form cycles; the reference guard is present, and no reflection-based cycle fixture was created.

Only small synthetic CPU fields were used. No world/atlas/pack load, full generation, native execution, Assets installation, or publication took place. Coordinated MAIN accessor review and production wiring remain pending.
