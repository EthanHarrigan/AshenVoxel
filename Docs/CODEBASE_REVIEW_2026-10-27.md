# Codebase review — 2026-10-27

Broad review requested by the user: incomplete work, duplication, and simplification opportunities. Inventory: **131 C# files / 20,586 lines under Assets**, plus shaders, assembly boundaries, validation runners and current handoff documents. This is a focused review of important paths, not a claim that every line has been verified.

**No gameplay, asset, package, scene, preference or generation changes. No Unity control or native playtest.** Review artifacts and the three required checkpoint documents are the only intentional changes; the existing core runner also produced its normal ignored build output. This directory is not a Git repository, so later cleanup needs readable preimages and GUID preservation.

Evidence: [results](CodebaseReview/2026-10-27/results.json), [source identities](CodebaseReview/2026-10-27/source-sha256.json), [reproduction driver](CodebaseReview/2026-10-27/run_review.py). All inspected source hashes remained unchanged during the checks. Historical release/test reports were not overwritten.

## Fix before broad refactoring

### R1 — High: loose-tree physics cannot keep up, and debt has no catch-up capacity

[NaturalTreePhysics.cs](../Assets/AshenVoxel/Exploration/NaturalTreePhysics.cs), lines 299–315 and 364–376, visits 27 neighboring hash buckets per individual cube and shares a 131,072-candidate budget across substeps. Even empty buckets cost candidates. At 3,380 cubes, two full substeps need at least 182,520 bucket visits before linked-list candidates. The cap therefore cannot sustain two 10 ms substeps per 20 ms update at this population. There is no sleeping-body path. This is a structural throughput limit, not merely a slow machine.

The [synthetic CPU reproduction](CodebaseReview/2026-10-27/PhysicsDebtProbe.cs) removed 3,380 wood cells into individual bodies and supplied fifty 20 ms ticks: **0.11 s advanced, 0.89 s debt, conservation intact**. A separate single-cube check started with 0.08 s debt; one hundred ordinary ticks left the same debt because Step permits at most two 10 ms substeps. These are Windows/.NET scheduler observations, not native frame rates or a natural-tree gameplay replay. [Output](CodebaseReview/2026-10-27/physics-probe.log).

Simplification direction: reduce repeated empty-bucket/pair work, maintain active/sleeping sets with explicit wake conditions, and specify bounded catch-up behavior. Preserve every pending candidate and material; merely raising caps or throwing away debt would not resolve the contract. Native pile, cut, kick, collection and restitution/contact acceptance must follow.

### R2 — High: the normal validation path no longer covers the installed source set

[run_exploration_smoke_checks.py](../Tools/run_exploration_smoke_checks.py), lines 13–18, uses shallow source globs and a manually enumerated runtime list. It misses RootContact, Wildlife, the finite-matter assembly, root upload helpers and WorldExplorerMemory. Compiling its exact current source selection failed with missing namespace/type errors. Only compilation was invoked; the old report-writing stage was not run. [Compiler output](CodebaseReview/2026-10-27/smoke-compile-only.log).

There are 25 top-level `run_*checks.py` launchers, with repeated SDK discovery, Roslyn arguments, source selection and report writing. Some already recurse while others still use shallow globs. Consolidate that plumbing around explicit assembly/source groups, retain each suite's independent assertions, and support a fresh output directory for each evidence run. Avoid a blind recursive Assets glob, which would mix Editor/native-only code with shims.

Two additional packaging blockers were reproduced:

- [check_project.py](../Tools/check_project.py), line 66, reads Markdown without an encoding. Plain Python 3.11 on this Windows host crashes with a cp1252 `UnicodeDecodeError`. The same check passes with `python -X utf8`: **244 assets / 13 assemblies**. Specify UTF-8 consistently. [Failure](CodebaseReview/2026-10-27/project-default-encoding.log), [pass](CodebaseReview/2026-10-27/project_utf8.log).
- [check_documentation.py](../Tools/check_documentation.py), lines 42–61, treats archived readable preimages as live documents. All **500** reported broken links are inside preimage paths, where the original relative bases no longer apply. Define archive-aware validation or validate those links against recorded original locations; retain the backups. [Output](CodebaseReview/2026-10-27/documentation.log).

[package_project.py](../Tools/package_project.py) requires these checks, so this affects packaging too. The release-freshness check separately rejects changed Materials.cs against old evidence; that rejection is expected and should remain until a new revision has its own verification. Do not relabel the old reports. [Output](CodebaseReview/2026-10-27/release_freshness.log).

### R3 — Medium: natural-tree collision with unrelated props is not integrated

[NaturalTreePhysics.cs](../Assets/AshenVoxel/Exploration/NaturalTreePhysics.cs), lines 54 and 265, exposes `OtherStaticOverlap`, but no assignment exists under Assets or Tools. [ExplorationNaturalTreeMatter.cs](../Assets/AshenVoxel/Exploration/ExplorationNaturalTreeMatter.cs), line 67, connects terrain only. Individual cubes therefore cannot use this hook to stop against neighboring static trees/rocks; falling islands also lack that external-prop query.

Use the existing population collision authority with the transferred tree excluded, as the shrub adapter already does. Contact should test the cube/body extent, not only its center. This is an implementation gap, distinct from an unexecuted native acceptance test.

### R4 — Medium: bee invalidation stops drawing without completing teardown

[ExplorationBeePatch.cs](../Assets/AshenVoxel/Exploration/Wildlife/ExplorationBeePatch.cs), lines 71–83: a source change or new obstruction sets `stopped=true` and reports the patch unloaded, but does not call `Release`. Subsequent Tick calls immediately return. The material, generated meshes, companion components and camera claim remain until destruction, and reported draw counts retain the last successful draw.

[WildlifeFixedPatchOwner.cs](../Assets/AshenVoxel/Exploration/Wildlife/WildlifeFixedPatchOwner.cs), lines 45–65, only releases the camera claim in its cleanup path. Give invalidation one coherent stop/release/reset operation, performed safely after the active render call unwinds. Also avoid allocating bird meshes when the installed adapter explicitly requests zero birds. This retention is small; it does not explain the multi-GiB terrain pressure.

## Simplifications with clear boundaries

| Item | Evidence and proposed change | Boundary to preserve |
| --- | --- | --- |
| R5: Gameplay under `_Validation` | [Exploration asmdef](../Assets/AshenVoxel/Exploration/AshenVoxel.Exploration.asmdef) depends on `AshenVoxel.FiniteMatter`, defined under [validation runtime](../Assets/_Validation/FiniteMatterSlice/Runtime/AshenVoxel.FiniteMatter.asmdef). Production shrubs use LocalizedMatter; trees use CargoKey from that assembly. Move shared production types into a clearly named runtime/data location. Keep actual fixtures separate. | Do not delete `_Validation` wholesale; it would break gameplay. Preserve GUIDs and assembly identity during the first move. |
| R6: Retired branch demonstration remains coupled to startup and capture | [ExplorationBetaMatter.cs](../Assets/AshenVoxel/Exploration/ExplorationBetaMatter.cs) duplicates portions of [FiniteMatterSliceOwner.cs](../Assets/_Validation/FiniteMatterSlice/Runtime/FiniteMatterSliceOwner.cs). Bootstrap still has an always-null betaMatter field and dead source-hold term; Editor capture code retains a branch fallback after its recording entry was retired. Move the demo to an explicit fixture and remove unreachable production branches after reference checks. | The existing natural-tree/shrub lease and source holds remain necessary. A dormant demo is not evidence of duplicate active simulation. |
| R7: Unreachable legacy overlay hookup | [ExplorationBootstrap.cs](../Assets/AshenVoxel/Exploration/ExplorationBootstrap.cs) calls ApplyVistaSettings at line 117, which assigns `Vista.UseVoxelLodTerrain = ActiveSettings.EnableVoxelTerrain` at line 277. Line 162 then requires those same flags to be true and false. The normal startup path cannot attach ExplorationVoxelTerrain. Remove the dead hookup or expose a deliberate comparison mode. | The overlay class still has direct runtime/native fixtures. Preserve the ordinary height renderer and shared VoxelTerrainPage conversion/contact APIs. |
| R8: Repeated meshing logic | [GreedyChunkMesher.cs](../Assets/AshenVoxel/Rendering/GreedyChunkMesher.cs) and [ArrayRootMesher.cs](../Assets/AshenVoxel/WorldScaleData/RootContact/ArrayRootMesher.cs) repeat face/AO/merge logic; the latter explicitly identifies itself as an adaptation. Branch/shrub/fixture debris paths repeat cube emission too. Extract common geometry rules or share equivalence oracles first. | Root meshing has cancellation, bounded capacity and a maximum emitted Y while retaining neighbor context. Do not merge away these differences or introduce Unity references into engine-free code. Native mesh regressions are required after changes. |
| R9: Tree rendering updates unchanged geometry every frame | [ExplorationNaturalTreeMatter.cs](../Assets/AshenVoxel/Exploration/ExplorationNaturalTreeMatter.cs), lines 201–236, reserves six faces per source cube and uploads all positions/normals on every LateUpdate, including intact and fully collected sessions. Hidden faces collapse but retain vertices and indices. Add pose/topology dirtiness and skip unchanged uploads before considering another rendering architecture. | At 3,395 cells this is 81,480 vertices and 122,220 indices; those counts are not a measured frame-time cost. Preserve 10 cm cells and shared mesh ownership. |
| R10: Bootstrap mixes too many independent responsibilities | [ExplorationBootstrap.cs](../Assets/AshenVoxel/Exploration/ExplorationBootstrap.cs) handles loading, source readiness, scene wiring, lighting, F1 UI, settings migration and disk persistence in 533 densely packed lines. Extract the options store and settings panel first; leave startup ordering in one place. Rename shared ShrubLease/IShrubMatterOwner to neutral prop-matter terms once callers are migrated. | Independent option-version fields currently preserve backward compatibility. Do not collapse versions without explicit migration tests. |
| R11: Current-state documents behave like duplicated history logs | MEMORY has 13,967 lines but only 784 nonblank; TODO has 5,939/697; AI_HANDOFF 4,695/824; SESSION_LOG 14,965/1,155. Repeated historical pending claims make current status hard to find and amplify the backup-link problem. Keep one concise current-state summary and one current acceptance queue, linking to dated history/evidence. Normalize blank-line inflation in a recoverable documentation-only pass. | Preserve all old failure evidence and release identities; archival does not make an old run current. |

The duplication scan used whitespace-normalized 12-line windows, followed by source inspection. It found repeated algorithm sections, not whole duplicate production files. Similar paths with different responsibilities should not be merged merely to lower a file count.

## Unfinished work: implemented versus accepted

- **Implemented beta with limits:** young birch variants 0/1 only, 4,096-cell ceiling, one shared exclusive prop lease, session-local cargo and rollback. Mature/general tree destruction, persistent edits/inventory and complete prop contact are absent. [Template](../Assets/AshenVoxel/Exploration/NaturalTreeTemplate.cs), [lease](../Assets/AshenVoxel/Exploration/VegetationShrubMatter.cs).
- **Implemented, still awaiting acceptance:** disk-cache warm/revisit benefit, dense-forest overdraw/appearance, production root lifecycle/visuals, bee animation/lifecycle, movement feel and successful native kick. Core/shim success does not complete these gates.
- **Known terrain debt:** full-profile travel/convergence and frame/memory budgets, cube/proxy and source seams, snow aliasing and final material quality. The commit guard can safely pause refinement below the nominal mesh quota. No new native measurement was made by this review.
- **Future work:** continuously editable/persistent terrain, complete fluid/thermal simulation, combat and broader ecology. Do not confuse these with missing implementations of otherwise finished small features.

## Recommended order and verification

1. Repair UTF-8/archive-aware checks and the shared smoke-runner source inventory. Preserve old evidence and emit new results separately.
2. Fix tree-physics throughput/contact and bee teardown, using focused CPU checks followed by the existing native route.
3. Move shared finite matter out of validation, retire the branch fixture coupling, and remove the unreachable overlay hookup. Verify metadata/assembly/scene references and smoke startup.
4. Reduce unchanged mesh uploads and extract options/UI responsibilities. Consolidate mesher logic only with output-equivalence and native mesh evidence.
5. Compact the current-state documentation while keeping its historical evidence accessible.

Executed: the existing core suite passed **91 checks**, including C#9/.NET Standard 2.1 compilation and the mutation-boundary probe ([record](CodebaseReview/2026-10-27/core-check-summary.json)); project integrity passed under UTF-8; the two physics debt reproductions passed. Smoke compilation, plain-encoding integrity, archive-link validation and historical release freshness failed as described. No changes were made to make those failures disappear during the review.
