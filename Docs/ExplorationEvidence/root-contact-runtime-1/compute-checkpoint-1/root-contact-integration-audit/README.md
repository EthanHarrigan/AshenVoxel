# Root-contact production integration audit

> Correction 2026-10-27T13:26:03.091191+00:00: the full material sampler reaches **32m**, not 24m. Roots need only its initial bare-height sample. The isolated `../root-contact-terrain-adapter` now verifies one exact global10cm-center query plus `SurfaceTop`, preserving terrain relief and excluding snow. See `report.json.terrain_adapter_correction`; earlier audit files are retained with `-before-terrain-correction` names. Original main hashes below remain a dated audit, not a new current-state verification.

Read-only audit of current MAIN, recorded 2026-10-27. No gameplay, Assets, pack, atlas, Unity or native changes were made. `report.json` contains 32 current-main SHA256 identities, exact source locations, evidence hashes, interfaces and the file-level delta plan; `audit.py` reproduces this inspection without running the proofs. This is an adoption plan, not installed integration.

Current main uses Population version 1 and AVPM0004 (99 models, pack SHA256 `41556b474ce7a26342e4dd7bcff2e4fe658ab0333e8360058262ee21c0f011aa`). Its streaming source hash is `5048632bc8a14276501758f22b4f8347c1a195d936dee359ae84aa571f629bb7`. Preserve existing flight, capacity, population scheduling and source-height authority.

## Smallest coherent adoption

Start with validated models 10 and 11, their original population poses/keys, and a small explicitly capped active set. Before the existing first upload, partition each canonical mesh into **upper submesh 0 and authored-base submesh 1 over one shared vertex buffer**. Fallback instances draw both; admitted grounded instances draw upper plus their complete derived lower mesh. Do not copy the isolated native fixture's global submesh-disable toggle: production needs per-draw submesh selection. Uniform cut crossings add two vertices and six indices each, only 4,800/4,720 GPU arithmetic bytes at LOD0 for models 10/11; keeping another upper/full vertex buffer is unnecessary.

`VegetationStreaming.Model`, `PageKey`, `Batch`, `Load`, `BuildMatrices` and `Draw` need the split metadata, representation part and submesh index. A small owner pumped by the existing `Tick` should prepare roots, roles and lower meshes. Keep the existing 32M triangle cap and coverage reservation, accounting for upper plus actual lower or upper plus authored base. Rebase every new lower matrix and bound through the existing `SetObserver` path.

Publish the immutable instance-binding map **at the existing matrix-batch swap**, together with render, body, resistance and support selection. Retain prior resources until the old batches retire. For active roots, replace original lower hard/stick spans below 3m; preserve canonical upper spans from 3m and canonical leaves. Never combine replacement roots with the full original lower collision. The two validated templates have no canonical non-Wood in the selected lower/context bands; enforce that certificate, or add a leaf-only query before extending to other specimens. Broadphase minimum Y must include the extended root.

Default tree LOD3/4 remains `ForestBillboard`, and `ForestHorizon` remains independent. Five-LOD mesh equivalence does not fix the authored distant atlas silhouette. No atlas rebake is required or authorized. Lower vertices must retain the original prototype-local origin and instance matrix for wind phase/height, shadows and motion passes.

## Evidence and resource boundaries

| Actual specimen | Composite checks, all five LODs PASS | Shared fixed representatives + original-order bytes | Derived lower GPU arithmetic, LOD0â€“2 | All five LODs |
|---|---:|---:|---:|---:|
| 10 | 37,033,317 | 84,011 | 1,573,928 | 1,589,976 |
| 11 | 38,142,151 | 89,746 | 1,629,416 | 1,644,512 |

These are inspected independent CPU proofs, not rerun here. Canonical order matters: ordinary snapshots sort chunks and cannot reconstruct the original first-representative coarse reducer. The captured fixed representatives/order provide a small companion-template route without changing AVPM4 or regenerating full trees per runtime instance. Composite outputs currently expose mutable equivalence artifacts; production still needs immutable mesh-data ownership, cancellation and disposal.

Existing root and interaction templates for both models contain 893,810 bytes of exact scalar payload. A conservative structural estimate is 2,302,274 managed bytes, excluding the added composite structures. A two-template 4MiB reservation is proposed, conditional on actual managed accounting; serialized sizes do not prove CLR residency.

The frozen CPU controller is a cooperative single-owner worker with defaults of four pending jobs including active, two ready entries, four leases, 2MiB cached results and a declared 24MiB worker reservation (26MiB ledger total). It publishes immutable cells/roles, not ready meshes. Root admission caps are 65,536 occupied cells, 128 chunks, 65,536 raw faces, and Â±20 cells for every positive-area terrain overlap. Owned output ends at row29; row30 is render guard only, interaction context extends through32. Connectivity, cancellation and freshness remain admission requirements.

**The 24MiB reservation does not cover added composite meshing or native uploads. No production native residency/upload cap or frame-time guarantee has been implemented.** Coarse meshing can perform 26,112 mask candidates atomically per chunk, so count budgets do not prove the current 2ms stream target. Specimen11's authoring/reference child took 4.516 seconds and reached 288.42MiB sampled private memory; that includes canonical capture and comparison and is not a runtime per-instance envelope. Production must separately cap/reaccount coarse worlds, mesh scratch, completed LOD arrays, GPU/native meshes and pending uploads before adoption.

## Publication, revisions and remaining gates

The final currentness predicate must cover terrain validity, pack/template/context/reducer and policy identities, original pose, resident-cell owner epoch and future edit revision. Terrain-only freshness is insufficient after interaction completion and during cache residency. Capture an immutable source/profile snapshot, sample global 10cm cell centers and use bare-ground `VoxelTerrainPage.SurfaceTop`; do not substitute a rendered LOD or snow top.

Global source revisions can change for unrelated distant patches. Initially, cancel/retry uncommitted work globally while retaining committed render and collision together until safe replacement. Never remove nearby collision merely because a far revision changes. Any later regional epoch scheme must cover the bare provider's complete dependency footprint. Full material-column sampling reaches 32m; the new direct bare-height adapter avoids that external halo. Activation/reversion must not change collision beneath the body. Queue-full, missing data, cliffs, invalid geometry, source changes or memory refusal retain a complete canonical fallback and an observable reason/retry state.

Remaining adoption gates are per-submesh batching and triangle accounting; atomic render/collision publication and resource retirement; immutable lower mesh ownership; measured CPU/native/upload limits; moving-wind/shadow/motion/depth continuity; approach/travel/rebase/density/source transitions; billboard transitions and the mesh-fallback path. Main Exploration currently has no prop-edit tool: CPU cache completion is not destructible gameplay integration. Future edits require authoritative `VoxelWorld.Set`, explicit provenance/edit epochs and transactional cache replacement.
