# Height-proxy worker cache — frozen candidate, not installed

One existing runtime partial connects the quality owner's frozen disk helper to
the current source-reach-beta-1 terrain worker. Three upstream helper payload files
are copied unchanged, with original checks/hashes retained. No MAIN/Assets, Unity,
import, native, queue, saved preference, source generation or matter writes.

## Payload and seam

WorldExplorerVoxels.cs is based on current d63c7eeb, preserving reach controls and
the current source/representation upload path. The existing Task.Run captures its
immutable sampler/tile/columns/cache and computes skirt once. A valid disk hit
returns the existing height-grid result; any miss builds normally and then stores
with exactly the same key/inputs. Cancellation is rethrown after helper calls so
the helper's cancellation-as-false result never triggers unwanted regeneration.
Key/load/write errors remain optional-cache failures, not terrain failures.

Configure exactly once before terrain initialization:

`Vista.ConfigureHeightProxyDiskCache(enabled, absoluteLocalDirectory, budgetBytes)`

No call means OFF. Passing false is also OFF and performs no helper construction
or disk IO. Budget defaults to1GiB; helper maximum is4GiB. Default dedicated path:
`WorldExplorer.DefaultHeightProxyDiskCacheDirectory` resolves LocalApplicationData/
AshenVoxel/HeightProxyCacheV1. Relative/UNC/root/Assets/reparse paths are rejected by
the owner/helper guards. Invalid configuration disables only this optional cache.
Construction itself performs no file IO; first read/index/lock/write is on the
existing worker. No extra terrain job, task queue or persistent surface cache.

One instance survives ClearTerrain/regeneration during the owner's session.
DisposeVoxelWorker cancels current work, clears the owner's reference and attaches
an ExecuteSynchronously terminal continuation when needed. Cache disposal follows
worker completion, without blocking Unity or closing a file owner during its IO.
No new worker thread is started. Closing leaves persisted records for a later owner.

Read-only bounded diagnostics:

- HeightProxyDiskCacheEnabled / Status (last status at most128characters)
- HeightProxyDiskCacheHits / Misses / Writes / WriteFailures
- HeightProxyDiskCacheOwnedBytes / Records

Writes count actual `stored` results; already-stored does not count as a new write.
Owned bytes mean indexed cache files, not arbitrary directory content, RAM or VRAM.
Counters are per owner, Interlocked/Volatile and retained across terrain clears.
The existing helper bounds its index to4096records and scanned files to8192.

## Settings/Bootstrap are proposals only

The coordinator owns both live files. Separate `control-proposal/`,
`ExplorationSettings.cs.proposed.diff`, `ExplorationBootstrap.cs.proposed.diff` and
controls-proposal.json contain a small concrete default-OFF next-Play checkbox,
frozen applied/selected state, independent explicit-save option version, early
Configure call and F1 counters. They are NOT in the runtime install manifest or
API/focused evidence. Coordinator must merge against exact current files, compile
the combined result, preserve source-promotion/shrub hooks and saved preferences.
No automatic save, directory cleanup button, cache purge or live owner toggle.

## Evidence

25narrow runtime assertions PASS:
checks/focus-20261027-210511-283834/result.json. Drives the actual existing terrain
worker/scheduler with numerical Unity: lazy initialization, cold miss/store, warm
hit skipping all source samples, retained session instance, new-owner persistent
hit, changed captured source identity miss, exact height/upload payload equality,
no false near readiness, OFF/no files, invalid path, IO-failure regeneration,
cancellation/terminal disposal/exclusive-lock reopening and stale view rejection.
The disposal test deterministically holds sampling; its elapsed bound is a fixture
blocking check, not a native frame-time measurement.

One affected API pass, two full assemblies:
checks/api-20261027-210511-280327/result.json. Current WorldScaleData plus
WorldScaleRuntime against installed real Unity6000.3.27f1/Core/Rendering APIs;
all source/reference hashes recorded. No native import/render/HLSL/Play test.

Upstream quality helper remains exact c933498 restore seam,7853e036 disk helper,
42aacc2d new meta. Its original43focused + engine-free C#9/netstandard2.1 checks
remain separate at the sibling height-proxy-disk-cache-1 handoff; not rerun or
relabeled. HeightProxySurface.Build is unchanged, and its existing meta is protected.
New content-based source/patch identities from source-reach-beta-1 are preserved.

## Costs and remaining adoption

Disk hits still query conservative skirt bounds, validate/hash data, allocate one
bounded result and upload the mesh. Cold builds additionally validate/hash/store
with durable file flush; this can make first generation slower. Many progressive
LOD stages can churn the bounded disk index/budget. Source/profile/composition
changes correctly miss, so new decoded regions still require source acquisition.
Cache lifetime may hold its directory lock/index until a canceled worker finishes.
Counts bound work/data, not milliseconds; slow storage or a large index can delay
that one worker. Failure status remains visible and generation falls back.

This avoids repeated proxy generation only when matching entries survive. It does
not evict active meshes/heights, free GPU memory, accelerate decoding, improve
source detail or prove native Start/FPS/total-memory gains. Same mesh quotas,
commit guard, contact cubes, cancellation and stale upload checks remain.

Coordinator next: guarded adoption + merged explicit controls + project integrity,
then one native cold/warm/same-source revisit and source-change/cancellation run.
Measure actual hit rate, worker/coverage/refinement time, disk bytes and failure
status under unchanged memory guards before enabling by default. Preserve file
identities and record installed/native status in canonical MEMORY/TODO/SESSION_LOG.
