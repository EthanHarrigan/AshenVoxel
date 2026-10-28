# Executed pure-core baseline

Generated UTC: `2026-10-26T08:40:53.5959005Z`. Generator version **5**.

AMD EPYC 9V74 80-Core Processor; x86_64; .NET 8.0.33; SDK 8.0.427; optimized Roslyn compilation.

**Container CPU observations only. Excludes Unity, meshes, graphics, colliders and GPU. These are not frame rates.**

| Workload | Samples | p50 ms | p95 ms | Maximum ms | Mean thread-allocated bytes |
| --- | ---: | ---: | ---: | ---: | ---: |
| generation | 5 | 1071.1964 | 1480.1645 | 1560.5378 | 78774148.8 |
| brush_calls | 64 | 0.4981 | 2.9306 | 3.7469 | 39448.6 |
| active_simulation_calls | 493 | 2.3752 | 3.8107 | 8.0263 | 693.4 |
| idle_simulation_calls | 32 | 0.0000 | 0.0000 | 0.0001 | 58.0 |
| ray_batches_1000 | 16 | 2.5467 | 4.6843 | 4.8794 | 58.5 |
| snapshot_save | 3 | 106.6378 | 152.0009 | 157.0413 | 95393829.3 |
| snapshot_load | 3 | 321.3879 | 357.9431 | 362.0048 | 55234010.7 |

Processed 846,136 candidates; 452,938 moves; peak frontier 19,158; final frontier 0.

All correctness gates passed: True. Full raw samples, fixture counts and exact source hashes are in [core-baseline.json](core-baseline.json).

Read [README.md](README.md) for methodology, sample-count limits and reproduction.
