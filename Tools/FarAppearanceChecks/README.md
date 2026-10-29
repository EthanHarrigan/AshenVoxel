# Far appearance cache checks

Run `python -X utf8 Tools/FarAppearanceChecks/run_checks.py` from the repository.
This compiles actual engine-free production code with .NET8. It never interacts
with Unity or loads the full world. Reports preserve source hashes and failures
under `.terrain-cache/far-appearance-checks/<timestamp>/`.

Checks cover 384/768 negative-coordinate toroidal addressing, independent window
overlap counts, single-job admission, completion/publication acknowledgement,
stale scroll/edit/publication rejection, failure requeue, disjoint/overlapping
edit obligations and a fixed two-mirror memory bound. Edits during initial fill
must preserve unrelated completed/running work, reject overlapping stale jobs,
and retain active reads until publication. A flood of separate edits must fall
back to bounded coverage without dropping obligations. GPU upload/sampling,
appearance quality and native performance remain separate unexecuted gates.

The final focused run passed 5,904,428 assertions across 16 publications:
`.terrain-cache/far-appearance-checks/20261029-052541-179338/result.json`.
Its source-matched report is retained at
`Docs/ClipmapEvidence/far-appearance-cpu-background.json`. Small target-selection
fixtures also check whole-world anchor exclusion, far-frustum corner reach and
positive/negative source-domain edge clipping. Runtime post-loader admission,
Ultra disabling and failure isolation were reviewed as code, not run in Unity.
