# Terrain quality presets beta candidate

Current identity: **terrain-quality-presets-2**. V1 and its exact source-matched
evidence remain under `frozen-v1/` and `checks/`; no earlier result is relabeled.
V2 adds the single real physics attachment hook and the requested restart-only
grounded-base option. The latest six-assembly API compile includes actual finite
matter/helper sources. A small options-only extension checks root opt-in,
version migration, frozen applied state and prevention of live owner toggling;
the world-ring/coverage/admission scan was not repeated.

Four existing source files implement restart-applied Normal, Detailed and Maximum
terrain requests through the existing settings/F1 path. This lane made no MAIN,
Unity, scene, asset, saved-option or patch-queue writes. The integration coordinator
owns installation and a short shared Exploration beta Play/visual check.

| Preset | Plan limit | Pixel target | Split factor | Far-detail quota | Contact quota |
| --- | ---: | ---: | ---: | ---: | ---: |
| Normal |512|1.5|2.2|1536MiB|1024MiB|
| Detailed |1536|0.75|4.4|2304MiB|1024MiB|
| Maximum |4096|0.5|20|4096MiB|1024MiB|

Maximum requests256 columns for eligible visible leaves and prioritizes projected
error, continuing8→32→64→128→256 through the existing queue. It does not start
4096 fine builds at once or make all visible terrain10cm. Leaf size, plan count,
per-tile face limits and available mesh budget still bound the result. Source
atlases,10cm matter, generation identity, geometry formats, renderer, contact
urgency and flight/hysteresis behavior are unchanged. No new runtime queue/cache,
worker or package is added. Higher settings explicitly trade startup/refinement
time and memory for requested detail.

F1 shows the selected and actually applied requests, restart pending, copied
validation/custom profiles, plan/refinement/deferred counts and the current
limiting reason. Selection changes only the session clone; explicit Save records
an independent versioned preset field. Old options retain their authored profile;
unknown terrain option versions/values are ignored. Explicit Normal rollback
restores the shipping request and applies after Save plus restarting Play. Normal
copied test overrides remain effective and visibly labeled until explicitly reset.

Coverage-reserve accounting scales with requested, active and desired plan counts,
including the old larger active plan during replacement. One tile's proven
reserve is668,160bytes. F1 shows separate far-detail quota, coarse per-plan reserve,
current+next-plan ceiling, contact quota, actual mesh payload and upload scratch.
The current+next-plan plus contact payload ceilings are3212.5MiB Normal,
5285.5MiB Detailed and10340MiB Maximum. These are limits rather than eager
allocations. They exclude worker data, upload scratch, retained native/driver
copies, vegetation and the rest of the process. Maximum is not guaranteed safe
on this machine. Existing pre-upload rejection, deferred demand, wake after
actual tracked mesh release, allocation retry and face-cap reporting remain.

The consolidated focused run passes1995 assertions, mostly repeated canonical
sample-coverage checks, plus preset/persistence/restart/memory/priority/traversal
cases. The exact final four-file candidate passes real Unity6000.3.27f1 API
compilation across all five complete recursive runtime assemblies, including
current root/wildlife and the coordinator's movement-feel-1 payload.

An actual-source static elevated-spawn request scan at30km/FOV93.43 sampled648
disk points per preset; every sample belongs to exactly one planned leaf. For11
forward samples per ring, requested mean horizontal spacing was:

| Distance | Normal | Detailed | Maximum |
| --- | ---: | ---: | ---: |
|2.048km|4.073m|2.327m|0.8m|
|4.096km|9.891m|5.236m|1.6m|
|8.192km|16.291m|8.145m|3.2m|

No forward sample in that comparison became coarser. These are requested targets
before geometry building or memory admission; they establish neither achieved
valley appearance nor native Start/FPS/total memory. The source resolution is
unchanged. The sampled plans contain511/1534/4096 tiles; increasing initial
coarse coverage to3×/8× as many tiles can increase startup work substantially.
One .NET planning observation is recorded only as fixture cost, not a benchmark
or prediction of native startup.

The F1 responsive-traversal checkbox is disabled by default and requires the
coordinator-owned movement-feel-1 Motor/Controller pair. Periodic settings apply
preserves explicit opt-in and its tuning, and preserves explicit opt-out. There
are no movement-persistence fields. BeePatch status/drawn actors are displayed;
no ecology is changed. Grounded bases have a separate explicit-save versioned
option with applied/selected/restart notice. The root default stays false and old
options preserve the authored Inspector value. Root selection is captured before
initialization; it never toggles an initialized owner. Native root memory/gameplay
acceptance remains open, and its extra memory is called out beside the option.
The beta known-issues label identifies refinement popping and budget limits.

`manifest.json`, `candidate.diff`, readable `baseline/`, final `candidate/`, exact
dependency payloads and source-frozen check results form the handoff. Initial
compile failures (duplicate existing property and missing numerical shim support)
are preserved with their source identities. The focused shim substitutes bee
rendering and wind constants; the API pass uses the real bee/wind/root sources.

Install only the four manifest files after comparing preimages. Movement files
are compile dependencies owned/installed by the coordinator, not part of this
patch. Preserve settings-asset SHAa29a4afc…, original scene/options, world/pack,
installed integer adjacency and root/wildlife changes. V2 includes one call to
`AshenVoxel.Exploration.ExplorationBetaMatter.Attach(this)` immediately after
`bees.Initialize(this)`. Install atomically with the separately owned helper
SHA8e13c1b0…/meta and Exploration assembly reference to AshenVoxel.FiniteMatter.
The helper implementation is a compile dependency under `dependencies/`, not
part of this four-source manifest. After adoption the coordinator should
update MEMORY/TODO/session evidence and record the short shared beta result.
