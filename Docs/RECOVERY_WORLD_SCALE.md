# World-scale research recovery notes

**Historical checkpoint log.** Current continuation starts with
[MEMORY.md](../MEMORY.md), [TODO.md](../TODO.md) and [AI_HANDOFF.md](AI_HANDOFF.md).
Pending transfers, old scene defaults, agent ownership and work-in-progress
statements below describe earlier moments; they are not the current task state.

Last updated: 2026-10-26 UTC. This is a handoff record, not a running watchdog.

## Latest user scope — takes precedence

The user clarified the scope to **diffusion terrain; Azgaar import, landmass scale and biomes; a 100 m LOD goal; tall mountains, far views and culling**. Ignore unrelated Minecraft features. **Do not port settlements, roads, buildings, or full hydrology.** Incoming modified-mod transfer is still pending and does not block the scoped prototype. Settlement history below is provenance context only, not implementation authorization.

The user subsequently required **full base Infinite Diffusion terrain functionality**, rather than an offline-only approximation. The scoped new-mod terrain features remain in scope; roads and settlements remain excluded. A source bridge or offline artifact alone does not establish full live terrain functionality.

**Transfer update:** the main agent has now fetched and SHA-256 verified the full source handoff. See the received-transfer checkpoint below; earlier pending-transfer text is historical.

## Current objective

Build and document an additive path from this Unity 10 cm voxel foundation to kilometer-high streamed worlds with distance LOD, including Azgaar world import, physical world scale, biome interpretation, and full base Infinite Diffusion terrain functionality. An incoming source-session delegation has located prior modified-mod/history materials and is packaging them for transfer; inspection is pending. Direct conversation UUID fetching remains unsupported. Do not infer untransferred contents or imply they were inspected.

## Ownership and status

- `/root`: main research and integration agent; owns evidence collection, architectural recommendations, and user communication.
- `/root/wake_up`: explicitly requested backup/recovery agent; owns this file and independent documentation/check work only. It must not mutate Core, renderer, or importer.
- Additional agents observed running: `/root/diffusion_research`, `/root/azgaar_research`, `/root/lod_research`, `/root/world_data`, and `/root/world_explorer`. Names indicate their topic, but consult their handoffs before assuming exact scope or completion.
- `/root/diffusion_bridge` subsequently owns a persistent upstream `WorldPipeline` HTTP bridge exposing native float height plus five climate channels, with immutable configuration, SNR/conditioning, cache, and queue behavior. `/root/world_explorer` is adding optional asynchronous remote-field prefetch. These are in-progress responsibilities, not completed validation claims.
- Initial recovery scaffold is written. Main-agent findings are pending. No implementation or performance claim is implied by this document.

## Resume here

From the shared workspace, inspect the latest work before editing:

```bash
cd /workspace/AshenVoxel
cat AGENTS.md
cat README.md Docs/DECISIONS.md
git status --short
rg --files Docs | rg 'WORLD|SCALE|STREAM|DIFFUS|AZGAAR|RECOVERY'
```

Read newly created research documents and this file. Preserve uncommitted work. Obtain the missing prior mod source/chat from the user's supplied artifact if it arrives; absence is a limitation, not evidence about the implementation.

At the scaffold checkpoint, `/workspace/AshenVoxel` was not a Git repository (`git status` reported no `.git` ancestor). Treat the status command as conditional on a later checkout being available; do not assume Git can recover current files.

Relevant existing baseline documents: `Docs/ARCHITECTURE.md`, `Docs/PERFORMANCE.md`, `Docs/FOUNDATION_PLAN.md`, `Docs/PLAYTEST.md`, and `Docs/VALIDATION_CORE.md`. For eventual code changes, use the checks specified in `AGENTS.md`; documentation-only recovery edits do not require running the entire core suite.

## Constraints to preserve

- The physical cell size is 0.1 m; 16³-cell default chunks span 1.6 m. Keep meter-space and grid-space conversions explicit.
- Core material data is authoritative and has no Unity dependency. Apply matter mutations through `VoxelWorld.Set`; meshes and collision are replaceable caches.
- No per-voxel Unity objects. Keep generation seed/version/physical profile provenance and existing saves intact.
- Current baseline is a finite world with synchronous generation and snapshot I/O. Streaming, distance LOD, and kilometer-high world support are not established implementations.
- Streaming requires explicit residency, generation dependencies/halos, revision cancellation, bounded queues, and persistent edits. Hiding distant meshes alone does not satisfy it.
- Unity Editor/player validation is unavailable unless a later agent records an actual run. Standalone C# checks and offline previews do not establish Unity rendering or runtime performance.
- Architecture changes must respect `AGENTS.md`, `README.md`, and `Docs/DECISIONS.md`. Preserve `.meta` GUIDs, pinned package versions, and assembly boundaries.

## Recovery capabilities and limits

The backup can read shared files, update this record, run authorized independent checks, and send collaboration messages while its turn is active. It cannot restart the parent agent, repair the platform, guarantee observation of every failure, or remain an always-on watchdog after its turn/session ends. Agent status alone is not proof that work is stalled or that a platform error occurred. There is no infinite background process.

If the main agent becomes unavailable, inspect durable files first, record the last verified checkpoint and missing inputs, and continue only within the documentation/check scope above. A message to the parent may be sent, but reactivating a root agent is not a guaranteed callable capability. Report any such limitation plainly.

## Verified checkpoints

- Backup read `AGENTS.md`, `README.md`, and `Docs/DECISIONS.md` before writing this scaffold.
- No Core, renderer, importer, save format, or generated terrain was changed by the backup.
- Backup independently ran `python3 Tools/run_world_scale_checks.py` from `/workspace/AshenVoxel` after new world-data files appeared: all 50 world-scale checks passed, followed by the C# 9 / .NET Standard 2.1 API compile. Compiler emitted informational CS9258 for the `field` identifier in `ImplicitMatter.cs`; no failure. This verifies the then-current engine-independent data/planner code, not Unity runtime/rendering. Other agents may subsequently change those files.

## Latest main-agent checkpoint

Received from `/root` on 2026-10-26 UTC:

- Exact prior chat is still unavailable. The user supplied conversation UUID `01a0a26f-a378-7c71-ba46-0602d1004db4`, but current tools do not support fetching a conversation by UUID. The user was asked for a paste, export, accessible shared URL, or mod files. A UUID alone is not access.
- Infinite Diffusion sources identified: paper `https://arxiv.org/abs/2512.08309`, upstream `xandergos/terrain-diffusion` and `xandergos/terrain-diffusion-mc`. Pinned reference-code clones are outside this project; consult the research handoff for their exact location and revisions.
- `Docs/AZGAAR_IMPORT_RESEARCH.md` is reported complete. `Docs/INFINITE_DIFFUSION_RESEARCH.md` and `Docs/WORLD_SCALE_RESEARCH.md` are in progress. Read the files before relying on their conclusions.
- `/root/world_data` owns additive `WorldScaleData` and `WorldScaleChecks`; `/root/world_explorer` owns additive Unity World Explorer runtime, scene, and editor work; `/root` owns the Python Azgaar/TIFF → AVWF import bridge plus documentation/integration.
- An `AVWF0001` contract is recorded in the world-data agent's messages. Recover the actual contract or code before implementing consumers; this note does not duplicate or redefine it.
- Core v5, the finite arena, and existing gallery must remain unchanged. All new world-scale work is additive.

## Next recovery actions

### Incoming modified-mod/history transfer

A later `/root` checkpoint reports an incoming delegation from the source conversation: another local task located the session and is packaging files/history for transfer. **Transfer has not yet been inspected or reproduced here.** Earlier missing-access statements describe the prior checkpoint; direct UUID fetching remains unsupported by this agent's tools.

- Repository reported: `https://github.com/EthanHarrigan/Imported-Azgaar-Diffusion-for-Minecraft`.
- Published state reportedly reaches blueprint `.37`, commit `d808aee2b7102917417d3928cb021e0a842f3ec6`.
- Substantial `.38`–`.41` settlement/audit fixes reportedly remain uncommitted and must be included in the incoming artifact, not inferred from the public commit.
- Upstream baseline reported: `23d3f50e5108882bb88a03c3ab048aa63633a02f`.
- City targets are not a guarantee of built cities. Largest target Givry was rejected for mountainous terrain; the user accepted rejection and does not require relocation.
- Independent world/import/runtime work continues. Do not claim modified-mod reproduction until the actual transferred source/history is inspected and checked.

1. Read the three research documents when present and inspect the world-data/explorer agent handoffs.
2. Locate the AVWF format definition and confirm the bridge and Unity consumer agree on units, dimensions, coordinates, biome values, height encoding, validation, and versioning.
3. Run the new checks specified by those owners, then asset/reference checks where applicable. Record actual command outcomes separately from pending Unity Editor/player validation.
4. Update this file with exact resumable commands, artifact locations, and source revisions after the main agent supplies them.

No implementation success, source audit completion, importer compatibility, or Unity runtime success is established by this checkpoint alone.

### Live diffusion checkpoint

`/root/diffusion_research` is preparing an isolated CPU Torch environment and an actual official-weights inference smoke attempt. Reported environment is 16 GB RAM / 4 CPUs; weights download is approximately 1.14 GB. Installation, download, and attempted inference are not proof of successful generation. Preserve actual outcome, peak memory/runtime observations if available, and exact upstream/model revisions when handed off. The data/planner suite currently has 50 passing engine-independent checks, as recorded above.

### World-data completed handoff

`/root/world_data` reports its engine-independent implementation complete and frozen, with existing Core unchanged. Authoritative validation record is `Docs/world-scale-validation.json`: 56 passing checks, source hashes, and C# 9 / .NET Standard 2.1 compilation. This later owner result supersedes the backup's earlier 50-check observation for the final world-data revision; inspect hashes before comparing results.

Owned additions are the six C# files plus assembly definition/metas under `Assets/AshenVoxel/WorldScaleData`, `Tools/WorldScaleChecks/Program.cs`, and `Tools/run_world_scale_checks.py`. Interfaces include `IWorldField` / `IWorldFieldBounds`, strict `AVWF0001` read/write, double/long global coordinates and floating origin, `AtlasWorldField`, analytic mountain fallback (**not diffusion**), bounded-edit `ImplicitMatter`, and balanced 2:1 LOD with capacity/backlog. A narrow 6,200 m atlas peak is retained through 32² min/max blocks and tested. This does not establish Unity runtime or model inference success.

Resume verification:

```bash
cd /workspace/AshenVoxel
python3 Tools/run_world_scale_checks.py --atlas Tools/WorldScaleImport/fixtures/azgaar-synthetic.avwf
```

### Full source transfer received

`/root` reports the authorized private Google Drive handoff fetched and extracted outside this project:

- Artifact size: **91,195,186 bytes**.
- Verified SHA-256: `abb50845f0785f48f67c3e928f340654d6c180a7a0e4773240e39154e9d8aeda`.
- Extracted location: `/workspace/reference-code/user-handoff/bundle` (7,353 files reported).
- Private Drive file identifier begins `1x0Oyx`; use the existing authorized tool result for the full identifier, not this abbreviated note.
- Inventory and relevant-source review remain assigned to the research agent after its neural overlap diagnosis. Download/checksum verification does not establish implementation reproduction or audit completion.
- Optional routing caches are unnecessary for the scoped work and were not downloaded.
- Latest main-agent validation counts: data 56 checks, import 38 checks, runtime static/shim checks 15. Consult their actual validation artifacts and commands before interpreting scope.
- `/root` discovered and flagged a square-aspect bug in the runtime. Treat it as unresolved until the runtime handoff records the fix and validation.

### Pretrained smoke document observed

Backup read `Docs/VALIDATION_INFINITE_DIFFUSION.md` after it appeared. It records actual successful pretrained CPU inference through the genuine upstream `WorldPipeline`, plus a retained failure of strict raw cropped-query equality. **Do not collapse these into either “no inference” or “fully validated raw streaming.”**

The document pins source `e8dcb4b1a834ab2f6b1a6f5256ed7c9f2f3e8230`, model revision `9ef8030cb805b433b98ec25c5dddefbac07a9e26`, and verified genuine weights. Outputs/logs/environment are outside the project at `/workspace/infinite-diffusion-smoke` and `/workspace/infinite-diffusion-venv`.

Reported 64² native 30 m samples and five climate channels are finite, with exact same-query cached repeats and matching hashes from two cold processes. First-query CPU observation is roughly 92–94 s, with about 2.85 GiB peak process RSS; these are not Unity performance measurements. A separate 32² cropped request changed 447/1,024 elevations versus the larger-query crop, up to **0.1761474609375 m**. The tested discrepancy was confined to a four-native-pixel boundary. The proposed integration policy is fixed global sample ownership plus padded requests/cropped publication and immutable identity/cache checksums. That wrapper needs separate real-model validation; the raw failure remains documented.

See `Docs/WORLD_GENERATION_PLAN.md` for the broader implementation plan when resuming. Re-read both documents because active owners may add later findings.

### Modified-mod review completed handoff

The diffusion research agent subsequently completed its scoped `.41` source/history review. It reports **all 7,352 present inventory files matched hashes**, with two deliberately omitted routing-cache fixtures still explicitly recorded as missing. The earlier extracted-file count includes material beyond those present inventory entries; use the inventory report rather than equating the counts. `Docs/MOD_PARITY.md` now records received-source evidence and relevant terrain/cache fixes. Roads/settlements remain outside scope. The research agent completed normally; canonical padded-owner validation remains the bridge agent's responsibility.

### Canonical bridge and runtime evidence observed

Backup subsequently read `Docs/WorldScaleEvidence/canonical-neural-validation.json` (**PASS**, 2026-10-26 10:30 UTC) and `Docs/world-explorer-validation.json` (**PASS**, 31 checks, 10:37 UTC). These newer artifacts supersede the corresponding pending-test statements above within their exact test scopes.

- Actual pinned pretrained backend: all float channels finite; cached response, canonical overlapping height/climate requests, and owner disk recreation all bit-exact. AVWF baked. First canonical region took about **185 s CPU**, with peak RSS **2,955,692 KiB**. Only one canonical owner, overlaps, and verified disk replay were covered. Adjacent-owner geometry, GPU/device changes, eviction/recomputation, Unity, and far LOD remain separate tests.
- The runtime artifact covers actual C# through Unity API substitutes plus Python producer/C# consumer fake transport. A live probe also exercised the actual HTTP provider and actual C# adapter with a JSON substitute: 33² region at native row 608 / column 1120, elevation 1,434.4678955078125 m, world identity `a5575739d5d761843a5c32af9d49f35b9fd1dfc7a54f93f0d62d34fba32dbf5a`. **This is not native Unity/Editor/URP validation.**
- Inspect recorded source hashes before rerunning or comparing results; active agents may make subsequent changes. Do not generalize the 31 checks into visual quality or target-machine FPS.

### Real Ceteviles import handoff

`/root/azgaar_research` completed normally and reports **49 importer checks pass** against the supplied Ceteviles JSON. Imported atlas: **513 × 265 samples**, **1,800 × 926.016 km** physical extent, source biome IDs **0–13**, including **698 Sand Sea samples**. It restores **29,990 omitted ocean samples** using original-grid water data; missing land remains an error. Source annotations/warnings are preserved in atlas sidecars. No existing Core files were changed.

- Base atlas: `/workspace/world-generation-artifacts/Ceteviles.avwf`, reported heights −990 to 8,000 m.
- Optional authored summit atlas: `/workspace/world-generation-artifacts/Ceteviles-summits.avwf`, reported heights −990 to 17,905.6875 m. This adaptation explicitly differs from Minecraft V13; do not imply exact parity.
- Diagnostic: `/workspace/world-generation-artifacts/Ceteviles-import-diagnostic.png`.

These are import artifacts and validation results, not proof of a complete model-conditioned full-continent render. World-data integration was observed active again after its earlier completion; consult latest hashes and handoff before treating the data revision as frozen.

## Concrete current check entrypoints

Run from `/workspace/AshenVoxel`; these entrypoints were located in the shared tree and their READMEs read by the backup. Use the recorded inference environment for upstream-dependent work.

```bash
python3 Tools/WorldScaleImport/check_import.py
python3 Tools/run_world_scale_checks.py --atlas Tools/WorldScaleImport/fixtures/azgaar-synthetic.avwf
python3 Tools/run_world_explorer_checks.py
python3 Tools/DiffusionBridge/check_bridge.py
/workspace/infinite-diffusion-venv/bin/python Tools/DiffusionBridge/check_azgaar_prepare.py --upstream-source /workspace/reference-code/terrain-diffusion
python3 Tools/check_project.py
```

`check_bridge.py` uses a deliberately fake backend to test contracts; it cannot replace real-model validation. For actual provider planning/serving/baking and optional real-weight checks, follow `Tools/DiffusionBridge/README.md`. Job preparation is not inference. Do not silently change the pinned source, package versions, model, statistics, device/precision, conditioning or generation identity. Existing jobs enforce those identities. Infer the current job path from the owner handoff rather than inventing one.

## Latest completed implementation checkpoint

The following agent completions were observed after the earlier partial reports. `/root` was still running normally at this checkpoint; no parent error was observed.

- **Diffusion bridge frozen:** 49 explicitly fake-backend contract checks; 19 actual upstream Azgaar conversion checks without weights; 7/7 real pretrained canonical sampling/replay checks; actual Ceteviles JSON → conditioning → pretrained model → AVWF **5/5**. That conditioned sample spans **5,145.75–5,803.96 m**. Current-source replay proof: `/workspace/diffusion-bridge-final/validation.json`; historical cold timing: `/workspace/diffusion-bridge-final/validation-cold.json`; conditioned sample/proof: `/workspace/diffusion-bridge-conditioned/ceteviles-neural.avwf`, `validation.json`, and `ceteviles-neural.report.json`. Adjacent-owner geometry, GPU behavior, model-cache eviction/recomputation, native Unity, hard coast parity and exceptional-summit parity remain separate/unclaimed.
- **World Explorer completed:** **42 checks**, including the live pretrained provider; project integrity **74 assets / 9 assemblies**. Review fixes include height/LOD bounds aligned to **−12,000..48,000 m**, bounds-aware skirts covering the reproduced **5.2 km** steep-edge gap, Cancel aborting atlas changes, incompatible live scaling/recentered crops rejected, and a Ceteviles neural-sample menu. This supersedes earlier 15/31-check reports and resolves the documented runtime review issues. Native Unity remains unverified.
- **Independent world-data review completed:** confirmed the four runtime review findings above were addressed; no runtime files changed by that review. Matching the centered control atlas to the provider's conditioning remains an explicit setup requirement.

Re-read final validation hashes and `/root`'s integration handoff before further edits. This recovery agent has only changed this document and run the earlier independent data checks; it has not modified Core, importer or renderer.
