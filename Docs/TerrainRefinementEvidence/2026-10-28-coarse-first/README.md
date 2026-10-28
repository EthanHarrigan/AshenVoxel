# First earn whole-view coarse coverage

The user's running Maximum30km configuration requested4096 terrain tiles before
its first atomic far-coverage publication. A local contact floor alone did not
publish that view. Starting the full population after the first floor could then
consume remaining commit headroom while every far mesh stayed invisible behind
the complete-plan gate. The parent checkpoint owns running-state evidence and the
Bootstrap/loading-UI integration; this change addresses initial plan size.

`WorldExplorer.RequestPlan` now limits the initial external voxel plan to at most
128 tiles and split factor2.2. It preserves the complete requested view distance,
root footprint, source/generator identity, stored MaximumTiles and requested split
tuning. Every initial tile still builds the existing8-column coverage mesh. The
ordinary complete-plan gate publishes them together. Its existing activation then
ends coarse startup and forces planning with the original requested quality. The
complete first view remains visible until a later complete replacement is ready.

Public diagnostics: `StartupCoveragePending`, `CurrentPlanTileLimit` and constant
`StartupCoverageMaximumTiles`. The latter is128; a smaller requested cap remains
smaller. This is a temporary runtime planning bound, not a saved quality reset or
shortened draw distance. Non-external and non-voxel planning remain unchanged.

The parent should wait for BOTH local floor readiness and `HasActiveCoverage`
before initializing the full population. A plan cap alone cannot overcome an
external system-commit shortage. All prior2GiB reserve, upload margin, payload
multiplier and hysteresis remain unchanged; blocked quality remains pending.

Validation:45 progressive lifecycle checks,43 live source checks, actual Windows
Unity API compilation of WorldScaleData/WorldScaleRuntime and static project
integrity PASS. The new actual planner/worker fixture starts from a4096-tile,
split18,30km request; checks128 perimeter points near30km; builds/publishes every
initial8-column mesh; verifies exact requested controls return; and requests the
larger actual plan while confirming complete old coverage stays active. Exact
first/subsequent tile counts are in `progressive-result.json`.

These are .NET numerical Unity substitutes and actual API compilation, not native
import, a full Exploration visual acceptance or an FPS measurement. A coarse
whole-view mesh is an initial representation and can visibly alias mountains until
the requested plan earns replacement. No native run/control was performed here.

Evidence: source-hashed `progressive-result.json`, `live-checks.json`,
`api-result.json`, `changed-source-sha256.json` and logs. The progressive result
points to its unique frozen source snapshot; preimages remain in `preimages`.
