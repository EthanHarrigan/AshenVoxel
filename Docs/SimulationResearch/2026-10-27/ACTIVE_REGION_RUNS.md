# Active-region experiment run identities

- **run01:** compilation succeeded; harness failed `tint-only does not wake`.
  The fixture wrote Sand back into(14,14,14) after that grain could fall. This was
  a material mutation, not necessarily a tint-only change. The exact failed source
  identities and failure log remain in
  [manifest](active-regions-run-01/manifest.json) and [log](active-regions-run-01/run.log).
  Corrected by asserting Stone at(14,13,14) and changing only that cell's tint.
- **run02:**399,165 assertions passed. Preliminary CPU records. The first spatial
  index used HashSet IDs plus a position dictionary; its comparator only counted
  matches, while the index returned a list. Do not use that query comparison as
  the final like-for-like result. [Manifest](active-regions-run-02/manifest.json),
  [raw data](active-regions-run-02/results.json).
- **run03:** accepted study revision;399,228 assertions,41 measured records.
  Contiguous bucket entries with move/remove index, matching ID-list outputs,
  simple bounds comparator and final snapshot SHA-256 equivalence. Five paired
  gravity trials per workload;600 real current idle-debris calls per condition;
  seven batches of1,000 snapshot queries per case. No compiled source changed
  during execution. Core91 additionally passed. [Manifest](active-regions-run-03/manifest.json),
  [raw data](active-regions-run-03/results.json), [summary](active-regions-run-03/summary.json).

All observations are standalone Windows/.NET CPU results, not Unity/native/FPS
acceptance. Each directory was created new; earlier evidence was preserved.
