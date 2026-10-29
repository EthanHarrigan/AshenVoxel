Run `python Tools/ClipmapLoadingChecks/run_checks.py` with .NET 8 installed.

Compiles the actual runtime sampler/scheduler and loader handoff against a small
modeled graphics boundary. Uses 64-texel flat-field levels to check hidden loading,
publication/readiness, covered handoff, continued gameplay draws during refresh,
bounded handoff without render updates, first-draw failure restoring Legacy while
requiring both local and distant coverage, cleanup and retry. No world assets loaded.

This does not measure Unity frames, verify GPU completion or prove faster startup.
