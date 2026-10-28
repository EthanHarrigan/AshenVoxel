# Horizon source passes and terrain edge bridges

October 27, 2026 (Denver). Implemented on top of the separately installed 320 m
learned base. Cheap complete mesh coverage starts while nearby decoding runs;
nearby safe-spawn requirements remain. Physical mesh passes prioritize far tiles
across all headings, with deferred/cap-limited work visible in diagnostics.

New source refinement is separate from mesh subdivision. The local overview
provider uses the pinned bake's coarse/base models for 160 m, then 80 m heights.
The controller completes the current horizon's 160 m pass before starting 80 m,
farthest first. One request covers a 2560 m core with 320 m blend margins. Near
5 m work has priority; publication and retirement protect a 2048 m player area
and active matter sessions. Immutable snapshots preserve fine interiors, climate
lapse adjustment, biome identity and source/cache invalidation. Max 8192 resident
regions / 64 MiB heights; remote grids max 65x65, CRC and height SHA verified.

The separate loopback provider on port 8788 is running for the next Play. It uses
the exact job in `.terrain-cache/coarse320-study/bake320-v1/job.json`, one worker,
BelowNormal process priority, bounded model strips, 4 GiB load / 2 GiB query commit
guards and 20-second idle model release. Existing port 8787 service is unchanged.
Inference is not preemptible within a model call and is not a zero-GPU-cost feature.
F1 can pause far source refinement. Cached/baked terrain stays visible on failure.

Exact edge bridges join both visible triangle profiles, including contact-mask
cuts and differing source/resolution edges. Work and 32 MiB seam residency are
bounded; old skirts remain a fallback while queued bridges are pending.

Validation: 84 mesh-scheduling, 20 source-demand, 33 overview-field and 29 seam
numerical checks pass, as do Python provider tests, C# wire/HTTP tests and five
owned Unity API assemblies. Project integrity: 259 assets / 13 assemblies.
Native stopped-Editor seam fixtures filled 679 and 4139 formerly uncovered pixels,
with zero uncovered tested seam pixels and no shader errors. Actual images remain
in `Logs/TerrainSeamAppearance/20261028-053201-422`.

One actual 160 m inference attempt executed a model query but did NOT complete:
available commit fell below the retained guard, then below the 4 GiB reload
requirement. No complete 160 m source publication or throughput claim is made.
The service is idle and the logical region remains requestable. Diagnostics and
failed attempt remain in `.terrain-cache/horizon320-20261028/overview160-smoke`.
Full-world native transitions, performance and model-completion acceptance remain
open. Queued seams can leave transient gaps under memory pressure; isolated seam
closure is not acceptance of every world gap. Supported broad source tiers are
160/80 m learned intermediate heights; nearby 5 m decoding remains separate.

All agents were stopped at the user's request. No further research, inference
attempt or full-scene test was started after that instruction. Exact source and
individual report identities are preserved alongside this note.
