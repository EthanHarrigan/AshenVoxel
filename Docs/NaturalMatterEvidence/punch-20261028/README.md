# Close punching and bounded ground work

October 27, 2026 (Denver); raw evidence timestamps are October 28 UTC.

LMB uses a shared 2.5m reach measured from the eye, with eight parallel rays on a
7.5cm ring around the center ray. Exact center hits take priority over assistance,
even if an assisted candidate is closer. Assistance only runs on a punch; it does
not add frame-by-frame targeting. Registry admission and active matter use the
same footprint. Selection scans the registry once and still refuses incomplete
budget-limited scans. Each accepted punch removes or queues one authoritative cell.
Foliage remains transparent to wood/stone targeting. Adapted root variants remain
unsupported and now report that reason explicitly.

Performance changes:

- Reuse the immutable pack identity already computed during population loading;
  remove the first-punch TextAsset clone and SHA256 pass over 12,016,333 bytes.
- Ground sampling processes at most64 pending columns per Update, checking a0.75ms
  elapsed budget between columns. Pending work is retained. One indivisible column
  query may exceed the time target; this is not a hard0.75ms guarantee.
- Stop the90 hidden startup map probes after Ready. Keep the last map with its
  `groundSampleUtc`; live terrain readiness, queues and memory still update at1Hz.

Verification:132217 cheap-matter assertions, including42 wood/stone cases covering
1m,2m,2.5m, inside, assist, misses and yaw;61 registry selection checks; all76 exact
prototype replays;15 loading-clock checks; integrity248assets/13assemblies; four
owned assemblies compiled against installed Unity APIs. Exact-center priority,
invalid input, budget refusal and conservation are checked. No native Play/FPS
acceptance is claimed. Final source hashes are frozen in the reports here.

The first new registry test failed at exactly2.5m because accumulated double DDA
steps exceeded the endpoint by rounding. A0.01mm boundary tolerance fixes that;
returned distances remain clamped so tolerance cannot accumulate across candidates.
The failure is recorded in `initial-failure.txt`; prior report copies remain in
`prior/`, and have not been relabelled as testing these changes.

At02:36 UTC, passive Windows evidence showed41,926,410,240 committed bytes against
a44,815,704,064 limit (~2.69GiB remaining). Burg-Tag held~3.53GiB private memory;
Unity's main Editor held~5.37GiB plus two import workers. Physical available memory
was8808MiB. The Editor heartbeat was stopped, so these figures cannot attribute
the earlier Play lag or measure GPU contention. No game was closed, no saved quality
was changed, and no Play session was started. A same-scene frame-time comparison
with the other game closed remains necessary.

One active matter session, temporary inventory, adapted roots and arbitrary terrain
excavation retain the limitations documented in MEMORY/TODO.
