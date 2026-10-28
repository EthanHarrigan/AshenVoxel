# Local crash diagnosis — 2026-10-26 (America/Denver)

Windows System event 2004 at 22:08:13 explicitly reported a low virtual-memory
condition. Its largest consumers were Unity PID28932 (20,950,024,192 bytes),
Unity PID13160 (2,712,387,584 bytes), and Unity PID34188 (1,186,476,032 bytes).
PID28932 is the main AshenVoxel Editor and remained running during inspection.

The crash artifact belongs to the separate AshenVoxel-WorldVoxels Editor run,
not the main Editor.log. Application event1000 at22:08:20 identifies PID0x3368
(13160), exception0xe0000001 in KERNELBASE.dll. The retained native log shows
Mono assertion/codegen frames during UIElements UXML type registration, plus
multiple crash-symbol loader failures saying insufficient memory resources.
System memory exhaustion is confirmed; it is the strongest explanation for the
concurrent initialization crash, though the precise failed allocation is not
identified. This does not establish a gameplay memory leak or GPU VRAM exhaustion.

Original artifacts (left in place):

- `C:/Users/ethan/AppData/Local/Temp/Unity/Editor/Crashes/Crash_2026-10-27_040815478/native-world-run-1.log`
- Same directory: `crash.dmp` (1,520,818 bytes at inspection).
- `C:/Users/ethan/AppData/Local/Unity/Editor/Editor.log` (main project, still active).
- Windows System event2004 and Application events1000/1001 around22:08.

After the crash the machine reported31.92GiB usable physical RAM, a47.92GiB
commit limit and only1.78GiB free commit. Pagefile allocation was16GiB.
The main Editor had approximately19.49GiB private commit and8.84GiB resident
memory; another WorldVoxels batch run was active with about3.77GiB commit.
These are live post-crash observations, not peak per-system measurements.
A subsequent PowerShell launch failed loading CoreCLR with HRESULT0x800705AF,
consistent with continuing allocation pressure.

Recommended recovery: save and close unused Editor sessions and avoid concurrent
native test Editors while memory is this tight. Then profile the main Editor's
startup and repeated Play/Stop memory to determine whether retention grows or
settles. No process was stopped and no pagefile/project settings were changed.
No new Unity instance or reproduction run was launched by this diagnosis.
