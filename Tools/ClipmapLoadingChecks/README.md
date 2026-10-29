Run `python Tools/ClipmapLoadingChecks/run_checks.py` with .NET 8 installed.

Compiles the actual runtime sampler/scheduler and loader handoff against a small
modeled graphics boundary. Uses 64-texel flat-field levels to check hidden loading,
publication/readiness, covered handoff, continued gameplay draws during refresh,
bounded handoff without render updates, first-draw failure restoring Legacy while
requiring both local and distant coverage, cleanup and retry. No world assets loaded.

This does not measure Unity frames, verify GPU completion or prove faster startup.

The runtime partial is included. A small Fast256 fixture also checks no optional
cache allocation before Ready and isolation of an injected optional sampler
failure. Disk/horizon loading are disabled/missing in the fixture; their Unity
boundaries are shims, not native acceptance. Use `--report <path>` to preserve
earlier source-matched reports when checking a later integration.
