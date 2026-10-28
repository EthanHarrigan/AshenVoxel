# Responsive redwood breaking: research notes

October 27, 2026 (Denver). Research and source inspection only. This note does not
claim an installed fix, a new Unity result, or a measured performance improvement.
The parent task owns implementation and final acceptance evidence.

## Immediate finding

The reported difference between tapping and holding has a concrete code path.
In `ExplorationNaturalTreeMatter.Update`, a new press reaches `CutTrunk` without
the renderer-current gate applied to held repeats. `CutTrunk` checks support and
queued cuts but not visual publication. `NaturalMatterRenderer.Tick` cancels a
build when support starts or its structure revision changes; `AdvanceBuild`
also rejects a completed obsolete result. Repeated taps can therefore invalidate
the visual work before it publishes. Holding already repeats at a nominal
0.15-second interval, gated by support and renderer readiness. This is source
evidence of a starvation opportunity, not a native timing measurement.

The first improvement should make both input paths use one finite transaction:
accept cut, finish support and matching mesh publication, then admit another cut.
Give the accepted area immediate feedback while that transaction completes.
Showing animation without fixing publication would leave the underlying problem.

## What transfers from the Noita memo

Noita's developer describes local dirty rectangles inside 64x64 chunks and
conflict-avoiding update passes. He also describes per-pixel rigid-body membership
and shape recalculation after destruction. The transferable principles are local
work, explicit ownership and selective invalidation; its 2D threading schedule is
not a proven 3D implementation for AshenVoxel. [Developer interview](https://80.lv/articles/noita-a-game-based-on-falling-sand-simulation)

The [existing trade study](MICROPHYSICS_TRADE_STUDY_2026-10-27.md) recommends finite
edit transactions and measuring backlog drainage, which directly fit this issue.
The [active-region experiment](ACTIVE_REGION_SCHEDULING_2026-10-27.md) found a
17% improvement in a separate dense granular CPU fixture, while current sleeping
debris visits were only about 0.032 ms/call. Neither finding identifies the cause
of redwood mesh delay. A broad active-region or physics rewrite is not the first
response to this publication problem.

## Visual and input design

The following are proposed AshenVoxel choices, not claims about another game's
implementation.

| Choice | Benefit | Guardrail |
| --- | --- | --- |
| Local warm tint with a restrained moving pulse at the accepted cut | Communicates that this exact area is breaking even before new triangles appear | Store transient renderer state; do not rewrite intrinsic tint or wake material simulation |
| Separate busy indication on the crosshair | A press during preparation/support/publication is acknowledged honestly | Do not show a successful removal or queue hidden future damage |
| Tap once or hold M1 for repeated cuts | Same interaction semantics and cadence; holding naturally follows current aim | One admission path; resample the current ray when ready; release, focus loss, menu or restore stops repeats |
| Optional brief chips/dust on successful removal | Sells the strike and makes progress visible | A bounded shared particle/instance effect, with no authoritative mass or inventory duplication |
| Later continuous swept brush | Reduces holes when dragging rapidly across a surface | Bounded spacing/work, ray and reach checks at samples; never connect unrelated or occluded surfaces |

The safest first visual is an albedo/emission pulse on the existing surface.
Shrinking or displacing the trunk vertices would deform still-attached material
and complicate collision, normals and motion vectors. Clipping the old surface
before replacement walls exist can expose an empty interior. Keep disappearance
atomic with the replacement patch; the pulse can persist until that publication
then decay. A cosmetic minimum duration must not delay actual geometry completion.

Unity's `ButtonControl` distinguishes the edge `wasPressedThisFrame` from current
`isPressed` state; the project already uses both. Use the edge for an immediate
attempt and the held state for bounded repeats, not a catch-up loop after a slow
frame. The project's pinned Input System is 1.17.0. [Version-matched API](https://docs.unity3d.com/Packages/com.unity.inputsystem@1.17/api/UnityEngine.InputSystem.Controls.ButtonControl.html)

Unity supports per-draw colors and values through `MaterialPropertyBlock`, and
recommends reusing blocks. Its documentation also warns that this path is
incompatible with the SRP Batcher. AshenVoxel already has borrowed-mesh draw
blocks; extending that active-prop path avoids introducing a block for every
forest instance, but GPU cost still needs native measurement. [Unity 6.3 API](https://docs.unity3d.com/6000.3/Documentation/ScriptReference/MaterialPropertyBlock.html)

## Further speed opportunities, in order

1. **Guarantee publication before admitting another hand transaction.** This is
   a liveness fix, not a claim that the mesher itself runs faster. Keep separate
   timings for first feedback, support completion, worker completion and upload.
2. **Cache ownership summaries and remesh only dirty chunks plus the required
   neighbor halo.** Current borrowed geometry already preserves the untouched
   tree. Further work should avoid rescanning all cell ownership for a tiny cut,
   while preserving complete component changes during a large detachment.
3. **Fast exact local surface first, optimized local surface later.** Mikola
   Lysenko explicitly treats mesh size and edit latency as different goals and
   proposes a quick mesh followed by a more optimized replacement. Here that can
   mean culled 10 cm faces only around the changed patch, followed by greedy
   merging. It does not require coarsening the whole redwood or changing matter
   resolution. AO, materials, tint, depth and shadows still need equivalence
   checks. [Original meshing article](https://0fps.net/2012/06/30/meshing-in-a-minecraft-game/)
4. **Reduce measured upload/allocation cost.** Unity's writable MeshData APIs
   allow population from worker threads and recommend batching allocations for
   multiple meshes. This is a later optimization if profiling shows managed
   list copies or uploads dominate. It cannot repair revision cancellation.
   Preserve the current pinned packages and mesh format. [Unity 6.3 MeshData API](https://docs.unity3d.com/6000.3/Documentation/ScriptReference/Mesh.AllocateWritableMeshData.html)
5. **Persistent transaction journal and per-chunk publication for sustained
   high-rate edits.** This would let input advance independently of a whole-tree
   publication, but needs stable body identities, changed-cell overlays and
   revision-aware collision/ownership. Publishing an obsolete whole-tree result
   blindly is unsafe: it can resurrect removed surfaces or draw a reused body
   slot with the wrong geometry. This is larger architecture work.

`Mesh.MarkDynamic` is a hint for frequently rewritten buffers, not a universal
meshing accelerator. The current renderer builds replacement meshes and uploads
them once, so adding it indiscriminately is not a justified first change.
[Unity API explanation](https://docs.unity.com/en-us/engine/6000.5/script-reference/unityengine/mesh/markdynamic)

Slow Rush's developer documents how static colliders generated from falling
matter can destabilize moving bodies, and the complications of grounding and
simplified collider holes. That supports keeping visual feedback separate from
collision truth and testing actual falling sections after changes. The article
describes a development experiment, not a turnkey solution. [Developer analysis](https://www.slowrush.dev/news/playing-nice-with-moving-bodies/)

## Acceptance sequence

Use the same mature redwood, camera and edit sequence for tap, rapid tap, hold,
stationary hold and held drag. Include delayed worker completion, support across
chunk boundaries, a complete detached section, restore, focus loss and a cold
target. Force worker latency longer than the repeat interval in deterministic
tests: accepted transactions must publish without requiring the user to stop
input; pending work must remain bounded; releasing must not replay stale cuts.

Record accepted/refused attempts, transaction revision, first-feedback age,
oldest unpublished age, cancellations, support/worker/upload duration, vertices,
memory and full-frame p95/p99. A next-frame feedback target and a provisional
sub-100 ms small-cut geometry target are useful proposed goals, not established
requirements or achieved results. Large severing may need longer with explicit
progress. Preserve material/tint conservation, depth/shadow/normal correctness,
body transforms and original prototype appearance in native Unity verification.

The Slow Rush optimization article linked from the prior memo was inaccessible
through the web tool during this pass; no new speed claim is derived from it.
The accessible developer follow-up is cited above. No external code was imported.
