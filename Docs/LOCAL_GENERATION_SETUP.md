# Local generation setup — 2026-10-26 America/Denver

## Background GPU preparation

Preparation subsequently completed successfully: CUDA12.8, torch2.9.1+cu128,
torchvision0.24.1+cu128, RTX3080 Ti. The small CUDA tensor operation and original
pipeline import passed. `world-job-gpu.json` is prepared separately. No model
inference, terrain bake, service launch or active-world publication followed.
User redirected ongoing work to voxel terrain conversion.

User subsequently authorized GPU environment preparation in the background while
continuing game work, without resuming terrain generation. Task-owned hidden,
below-normal-priority setup process runs `.terrain-cache/prepare-gpu.py` and creates
a separate `.terrain-cache/python-gpu` environment. CPU environment, game assets,
and existing world job remain untouched. CUDA package target is the matched
official pair torch2.9.1+cu128 / torchvision0.24.1+cu128. Model files are reused.

Read `.terrain-cache/gpu-setup-status.json` for actual running/ready/failed state;
starting the process does not prove completion. Logs are `gpu-setup.log` and
`gpu-setup-errors.log` in that directory. Successful setup checks a small CUDA
tensor operation, imports the real pipeline, records `gpu-probe.json`, and
prepares a separate `world-job-gpu.json` without executing model inference.
It does not launch a service, bake terrain, or publish assets. A ready environment
is not a measured GPU terrain-generation time or memory budget. Preserve the
user's stopped-bake instruction until explicitly asked to generate again.

User requested setup, then explicitly stopped the bake and asked to import
completed data. A separate chat is also editing this workspace.

Installed a project-local Python3.11 CPU environment in `.terrain-cache/python`.
Pinned upstream checkout: `e8dcb4b1a834ab2f6b1a6f5256ed7c9f2f3e8230`.
All original pretrained model files match `Tools/DiffusionBridge/model-manifest.json`;
statistics match the pinned SHA256. Installed dependencies pass `pip check`,
and the full upstream inference pipeline imports. Added numba, required by the
upstream portable RNG. Windows map loading requires `PYTHONUTF8=1` or `-X utf8`.
No shared source code was edited for setup.

Real CPU inference completed the whole-map coarse stage. The detailed stage was
stopped by user request before any complete decoded owner was written. Partial
outputs and generation logs are retained under `.terrain-cache/exploration-bake`
and `.terrain-cache/generation.log`. An interrupted large-model download was
rejected; range downloads recovered the complete file and verified its pinned hash.

Completed data is imported into
`Assets/AshenVoxel/Exploration/LocalGenerationPreview`: paired macro height/climate,
source biomes and metadata explicitly marked PARTIAL. This separate dataset is
not connected to the Exploration settings. It contains no decoded starter region.
Local view identity:
`e938375c3b9bad94e2e355441aa3dc5f1760929b9ee293221388eafd90d5a7bd`.
Native job identity:
`7392e32629296ee4d8e45a9f507cf27ec19f641f615d4b85eecadd13b674a132`.

The original playable sample files, including metas, remain byte-identical to
`.terrain-cache/original-bundled-samples`. Original world identity and frozen ZIP
are unchanged. Do not mix local macro data with the old detailed files or bypass
the world identity check. The current startup requires a detailed pair, so this
partial import cannot activate remote diffusion by itself.

Executed verification: `Tools/check_project.py` passed (148 unique assets,
11 assemblies at this instant). Imported macro pair passed CRC, height pairing,
world-identity and finite-climate checks. All original sample bytes were compared
to backup. No native Unity import/render/play verification was executed here.

No service is running. `.terrain-cache/Start-Service.cmd` is a service-only
restart helper for the unpublished local job; it does not make that job compatible
with the active playable world. `.terrain-cache/prepare-local.py` is the cached
generation/staging helper; **do not rerun without a user request to resume**.
Finishing the matching detailed bake, validating it, and deliberately publishing
the complete dataset remain necessary before remote diffusion can be enabled.
