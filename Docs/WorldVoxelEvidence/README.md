# World voxel terrain evidence

- [Final native DX12 validation](native-final/validation.json): full Exploration,
  three completed512-tile views, source hashes and native mesh regression.
- [Ground capture](native-final/ground.png), [overview](native-final/overview.png),
  [turn/rebase](native-final/turn-rebase.png).
- [26 data checks](data.json), [16 runtime checks](runtime.json),
  [integration/backup manifest](integration.json).
- native-attempt1.log and native-attempt2.log preserve memory failures;
  main-editor-memory-crash.log preserves the affected main session.
- native-attempt3 preserves the earlier successful D3D11 terrain-only run.
- smoke-liveness-failure.json records the old synthetic-frame timeout.

The final per-view mesh_regression field is false because those reports were
written before the later regression menu ran. completed.txt and validation.json
record the successfully completed regression; the Editor log contains its PASS.
Mesh memory reported by Profiler is an attribution estimate, not measured VRAM.
Use vertex_index_bytes for exact layout payload. None of these reports establishes
stable FPS, interactive feel, long-flight memory safety or a new release ZIP.
