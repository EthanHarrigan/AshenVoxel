# Terrain clipmap implementation checkpoint

Current stage: Phase 0.2 native gate passed; Phase 1.1 authorized by host.
Date: 2026-10-28 (Denver).

Phase 0.1 corrections were already committed in `ce9c1ab`. Existing Unity
6000.3.27f1 was observed in Edit mode after the user stopped Play, refreshed,
and compiled without errors. No agent started or stopped Play.

Phase 0.2 preserves unrelated in-flight work and queued refinements across local
source publication and its subsequent ApplyPlan. Affected work includes the
normal/material sampling halo. Global revision fences remain for resets; contact
refresh obligations survive admission deferral and overlap replans, then clear
on successful publication or complete eviction. Queue dedupe is linear.

Evidence: `Docs/ClipmapEvidence/phase0-20261028/phase02-native-report.json` has
17 passing native Edit-mode checks, source hashes, and no errors. Official Unity
API compile, project integrity, and the native mesh regression menu also passed. The first native fixture report
claimed PASS while Unity logged Edit-mode Destroy(null) diagnostics: it is **not
an accepted gate**. Its report/log are preserved as phase02-first-*; production
null guards and fixture error capture fixed the clean rerun. The old standalone
ContactFreshness harness could not compile its stale Unity shim/source inventory
(UNorm16/property-block/VoxelStepTextureOwner); no test outcome is inferred from it.

Remaining concern: the held-iterator and synthetic contact sizes validate queue
lifecycle and admission, not real worker timing or visible settling. Optional
standing-still Play acceptance remains unexecuted; legacy stays the default.

## Review amendments to apply before their respective phase

- Keep Legacy as the default until user appearance acceptance; the Phase 3 code
  default contradicts Phase 6 and the project renderer adoption invariant.
- Phase 0.2 must include contact freshness and normal/material sample halos.
  `SourceRevision` currently tracks the global revision, so merely deleting the
  increment leaves contact surfaces stale. Keep a local dirty obligation through
  contact capacity deferral and replanning. Cancel/requeue only affected work.
- `ForceReplan` reaches `ApplyPlan`, which clears pending/refining. Preserve
  unaffected queued work when applying refreshed bounds/topology, or source
  publications will still cause queue churn one step later.
- Baked far identity must include sorted accepted patch identities/content,
  basis identity, grid and quantization plus payload. Validate finite coordinates,
  dimensions, lengths, decompression limits, CRC and each height SHA256. Do not
  edit the pinned overview-provider/bake scripts.
- Sparse far interpolation must weight heights by coverage. Uncovered zero
  height must not bias the edge toward sea level.
- Clipmap publication must reject stale generations and cannot join unrelated
  completed rectangles into a falsely valid region. Moving/teleporting requires
  a genuinely valid ancestor fallback and retained published coarse coverage;
  one-hop fallback to an unfilled parent is insufficient.
- Do not rely on NaN vertices as a topology proof. Test actual ring geometry and
  all depth/shadow passes, including invalid-data fallback, not just fully filled
  mathematical boundaries.
- T=512, s0=.2, N=8 covers about 6.5 km, not the 40 km test domain. At 400 m the
  needed level has about 1.6 m spacing, so the <=.8 m assumption is false. Measure
  height error rather than assuming the requested bound.
- High resources are roughly 49 MiB before overhead, not just a few MiB; drawing
  full grids can submit about 6.2 million triangles per pass. Prefer real rings and
  measure total owned memory and terrain-only timings.
- The height-only optimization's `min(L,3)` color lookup is reversed for fine
  levels if L0-L2 are intended to use L3 color. Define and test the mapping.

Native gates remain gates: API-only compilation and CPU fixtures do not establish
Unity shader import, native rendering, geometry holes, tree alignment or FPS.
