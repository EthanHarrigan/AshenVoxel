from pathlib import Path
import datetime,hashlib,json,shutil
ROOT=Path(__file__).resolve().parents[2]; D=ROOT/'.terrain-cache/startup-throughput'
E=ROOT/'Docs/StartupThroughputEvidence/2026-10-27'; F=E/'flight-refinement'; B=D/'flight-install-doc-preimages'
def sha(p):return hashlib.sha256(p.read_bytes()).hexdigest()
def save(p,data):p.parent.mkdir(parents=True,exist_ok=True);p.write_text(json.dumps(data,indent=2)+'\n',encoding='utf-8')
def copy(s,d):d.parent.mkdir(parents=True,exist_ok=True);shutil.copyfile(s,d);assert sha(s)==sha(d)
installed=json.loads((F/'install/installed.json').read_text());assert installed['installed']
for row in installed['files']:assert sha(ROOT/row['path'])==row['sha256']
extra={'Assets/AshenVoxel/Exploration/Prototypes/ExplorationPrototypes.bytes':'41556b474ce7a26342e4dd7bcff2e4fe658ab0333e8360058262ee21c0f011aa',
       'Assets/AshenVoxel/Exploration/Samples/exploration-world.json':'85ff39810a717cc35db248d9c48b38af52839346e392c32ba707f67e6df94ea3'}
for rel,digest in extra.items():assert sha(ROOT/rel)==digest,rel
save(F/'install/protected-world-pack.json',extra)
oldSnapshot=json.loads((D/'snapshot.json').read_text())
worldPath='Assets/AshenVoxel/Exploration/Samples/exploration-world.json'
assert oldSnapshot['sha256'][worldPath]==extra[worldPath]
save(F/'install/world-hash-reconciliation.json',{'initial_check':'FAIL before checkpoint/doc writes: world_id was incorrectly used as JSON file SHA256',
    'world_id':'14fb32f26ff725abaf9f1e04668a1f46c5b4783a66f91d23253676f595f613e4','actual_file_sha256':extra[worldPath],
    'unchanged_vs_prior_frozen_snapshot':True,'prior_snapshot_sha256':sha(D/'snapshot.json'),'runtime_or_world_edits_to_resolve':False})
copy(D/'finalize_flight.world-id-assertion-failure.py',F/'install/scripts/finalize_flight.world-id-assertion-failure.py')
for name in ['install_flight.py','verify_flight.py','finalize_flight.py']:copy(D/name,F/'install/scripts'/name)
copy(F/'current-status.json',B/'flight-current-status-before.json')
save(F/'current-status.json',{'utc':datetime.datetime.now(datetime.timezone.utc).isoformat(),'status':'flight-refinement-1 installed; native compile/reload and mesh regression PASS; mountainous flight/FPS still unverified',
    'installed':True,'current':'install/installed.json','cpu_comparison':'final-checkpoint/summary.json','rejected_prior':'v4 same-XZ descent failure retained in final-checkpoint','native_Play_by_this_lane':False})

H=E/'prototype-demand';assert not H.exists();H.mkdir()
baseline=F/'baseline-source';baseHashes={p.relative_to(baseline).as_posix():sha(p) for p in baseline.rglob('*') if p.is_file()}
runs=['20261027-122348-574900-production-PrototypeHotsetProgram','20261027-122453-544486-production-PrototypeHotsetProgram']
for name in runs:
    run=ROOT/'.terrain-cache/progressive-lod/runs'/name;report=json.loads((run/'result.json').read_text());out=H/'checks'/name
    for n in ['result.json','console.txt','compile.rsp']:copy(run/n,out/n)
    overrides=[]
    for rel,digest in report['source_sha256'].items():
        if baseHashes.get(rel)==digest:continue
        assert sha(run/'source'/rel)==digest;copy(run/'source'/rel,out/'source'/rel);overrides.append(rel)
    save(out/'archive-source-layout.json',{'base_snapshot':'../../../flight-refinement/baseline-source','overrides':'source','overridden_files':overrides,'all_hashes':'result.json/source_sha256'})
report=json.loads((ROOT/'.terrain-cache/progressive-lod/runs'/runs[-1]/'result.json').read_text())
save(H/'summary.json',{'utc':datetime.datetime.now(datetime.timezone.utc).isoformat(),'status':'PASS46; read-only CPU demand measurement, no lazy-loading implementation',
    'checks':report['checks'],'data_sha256':report['data_sha256'],'scope':report['scope'],'all_runtime_mesh_bytes':report['all_runtime_mesh_bytes'],'results':report['results'],
    'source_matched_run':'checks/'+runs[-1]+'/result.json','preserved_fixture_failure':{'run':'checks/'+runs[0]+'/result.json','reason':'Metadata-only material missed enableInstancing; fixed test fixture only.'},
    'limits':['17 safety-cell set is deterministic for recorded spawn/profile; first/ready published snapshots depend on CPU budget scheduling.',
              'The audit decoded all99 records once to measure exact metadata and retain collision/resistance. It is not a lazy-loading timing comparison.',
              'Metadata Mesh placeholders preserve bounds/triangle costs only. No native upload, frustum appearance, VRAM or Start-speed claim.',
              'First64 nearest planned cells are an explicit sample, not a guarantee of the full desired view or horizon.',
              'Main authored settings, not saved user preferences; separate small profile matches coordinator smoke fields.'],
    'decision':'Measure prioritizing required model/LOD GPU preparation before building a lazy framework; preserve exact collision/resistance, missing-prop fallback and coverage. No prototype runtime change adopted.'})
copy(ROOT/'Tools/StartupThroughput/PrototypeHotsetProgram.cs',H/'PrototypeHotsetProgram.cs')

note='''Flight-refinement-1 LIVE (2026-10-27 ~12:24 UTC): coordinator reviewed and
released the exact four-file candidate. Existing idle-Editor install preflight
passed at3.934GiB commit headroom. Main native compile/reload and mesh regression
PASS:32,198 faces/RGB,128,416 AO,12 worlds. Main targets and two independent
rollback copies match; Bootstrap/Settings, original scene/settings/options,
world and pack are unchanged. No Play/new Editor from this lane; native slot
returned to coordinator. Real mountainous flight/visual/frame/memory acceptance
remains open. Earlier NOT INSTALLED entries below are dated history. Evidence:
Docs/StartupThroughputEvidence/2026-10-27/flight-refinement/install/installed.json.

Separate prototype-demand CPU audit PASS46: actual16m spawn safety needs17 cells,
1372 placements,25 prototype IDs,17.39MiB across their runtime mesh LODs versus
416.49MiB all99. First64 planned cells need29 IDs including redwoods and338.30MiB
all LODs; authored desired LODs plus fallback198.99MiB. This quantifies potential
preparation priority, not an implemented lazy loader or measured Start gain.
Original interaction data retained; native meshes replaced with metadata only.
Evidence: Docs/StartupThroughputEvidence/2026-10-27/prototype-demand/summary.json.

'''
todo='''Flight install and demand-audit checkpoint (2026-10-27 ~12:25 UTC):

- [x] Exact four-file flight installation; main native compile/reload and bounded
      mesh oracle pass; original scene/settings/options/world/pack unchanged.
- [ ] Real mountainous flight: quality during transit, stop/descent/reversal,
      vegetation persistence, frame cost and native process/GPU memory.
- [x] Quantify actual16m safety and first64-cell prototype/LOD demand using
      production population/selection/readiness and exact live pack metadata.
- [ ] Decide a small demand-priority prototype experiment after review; no lazy
      runtime loader, index format, collision bypass or altered Ready adopted.
- [ ] Initial contact scheduling: active coverage is empty until all initial
      coarse tiles publish; relaxing only the contact guard cannot safely overlap
      work. Review a smaller complete first coverage plan before implementation.

The earlier uninstalled flight/native-review items below are historical;
native compile/mesh passed, real input-route/FPS/visual acceptance did not run.
See Docs/STARTUP_LOD_PROGRESS.md and flight-refinement/current-status.json.

'''
reportText='''
## Flight-refinement-1 installed; native compile and mesh gate passed

At approximately12:24UTC the coordinator independently reviewed the frozen
127-added/21-removed-line four-file diff and released the existing-Editor
installation slot. Fresh idle preflight had3.934GiB available commit. The idle
hook installed only Controller/WorldExplorer/Integration/Voxels; Bootstrap and
Settings stayed unchanged. Exact targets, independent readable preimages and
the hook's rollback copies were verified. Main native compile/reload and the
mesh oracle pass32,198 unit faces/RGB and128,416 AO values across12 worlds.
Existing unrelated obsolete DLSS API/null-options warnings remain documented.

Original scene/settings/saved options and the world/pack hashes are unchanged.
No Play/new Editor was started; the native slot was released. The actual mountain
flight, trees, stop/descent/reversal latency, FPS and process/GPU memory remain
unverified for this policy. Its public travel-deferred flag is transient read-only
diagnostic state; ResetObserverMotion explicitly resets spawn/teleport history
and wakes planning, without changing generated geometry or saved state.
[Installed identity, rollback and native logs](StartupThroughputEvidence/2026-10-27/flight-refinement/install/installed.json).

## Prototype demand at the actual spawn — measurement only

A separate tools-only audit reads the current99-record pack once, retaining
original collision/resistance and exact triangle/payload metadata. It invokes
production population planning, LOD selection, matrix publication and IsReady
with tiny metadata Mesh placeholders. At spawn X=-19065/Z=-10200, seed714103,
both small and authored profiles require the same17 cells/1372 placements/25
prototype IDs for the16m safety test. Removing each required cell independently
causes actual IsReady to fail. All46 checks pass; the first fixture's missing
instancing-material flag is preserved as a test setup failure.

| Needed set | Model IDs | All their runtime mesh LODs | Desired LODs plus cheapest fallback |
| --- | ---: | ---: | ---: |
| Whole shared library |99|416.49MiB|—|
| Spawn16m required cells, authored profile |25|17.39MiB|12.73MiB|
| First64 nearest cells, authored profile |29|338.30MiB|198.99MiB|
| First64 nearest cells, small profile |29|338.30MiB|88.38MiB|

The spawn safety set contains mature birches2/3, plants and granite rocks;
mature redwoods10/11 and conifers14/15 enter the first64-cell sample and dominate
its cost. Selecting only whole models is therefore much less effective once
that nearby forest is needed. Required LOD preparation is a stronger target to
measure. Desired-LOD figures precede the actual triangle-budget demotion; the
audit also records first-published and spawn-ready snapshots, whose membership
depends on CPU scheduling. The deterministic safety-cell set is the firmer
quantity. First64 cells do not represent the full horizon or every desired cell.

No lazy reader, runtime index, native upload change or Ready bypass was added.
All records were decoded to perform this audit. These figures describe arithmetic
mesh demand, not resident process/GPU memory or measured startup savings. Any
follow-up must preserve CRC/size validation, collision plus leaf/stick interaction,
fallback representation and published readiness before enabling input.
[Exact IDs/LODs, source/data hashes and preserved failure](StartupThroughputEvidence/2026-10-27/prototype-demand/summary.json).

Source inspection also closes an assumption in the proposed contact overlap:
the initial active terrain set is empty until every coarse tile exists, and
coarseStartup clears during that same activation. An active spawn ancestor
cannot currently exist earlier. Simply relaxing RefreshContactPlan would not
provide the requested safe overlap. A smaller complete first coverage plan is
the next bounded hypothesis; an extra unpublished-result cache was not added.
'''
log='''
### 2026-10-27 — Flight patch installed; prototype-demand audit

Coordinator released exact four-file flight-refinement-1 after independent
review. Fresh existing-Editor idle/preimage/protected checks and3.934GiB headroom
passed. Native compile/reload and mesh regression PASS32,198faces/RGB,128,416AO,
12worlds. Exact installed hashes and independent/hook rollback copies verified.
Bootstrap/Settings plus original scene/settings/options/world/pack unchanged.
No Play/new Editor. Slot released; mountainous quality/flight/FPS/memory untested.

Separate tools-only actual spawn audit PASS46:17 safety cells/1372 placements/
25IDs/17.39MiB all mesh LODs; first64 cells29IDs/338.30MiB all LODs,198.99MiB
desired-plus-fallback at authored quality (88.38MiB small profile). All99 library
416.49MiB. Native uploads replaced by metadata meshes; original interactions
preserved. Initial missing enableInstancing fixture failure retained. No runtime
lazy loader, startup gate change or speed claim. Active terrain ancestor cannot
precede initial all-coarse activation; smaller complete coverage plan awaits
review. See flight-refinement/install and prototype-demand evidence directories.
'''
updates={'MEMORY.md':('insert',note),'TODO.md':('insert',todo),'Docs/AI_HANDOFF.md':('insert',note.replace('Docs/StartupThroughputEvidence','StartupThroughputEvidence')),
         'Docs/STARTUP_LOD_PROGRESS.md':('append',reportText),'Docs/SESSION_LOG.md':('append',log)}
for rel,(mode,addition) in updates.items():
    p=ROOT/rel;old=p.read_bytes();text=old.decode('utf-8');assert addition.strip() not in text
    backup=B/rel;assert not backup.exists();backup.parent.mkdir(parents=True,exist_ok=True);backup.write_bytes(old);assert backup.read_bytes()==old
    n=text.find('\n')+1;new=text[:n]+'\n'+addition+text[n:] if mode=='insert' else text.rstrip()+'\n'+addition
    assert p.read_bytes()==old,'Concurrent edit '+rel;p.write_bytes(new.encode('utf-8'));print(rel)
index=E/'archive-sha256.json';old=index.read_bytes();copy(index,B/'archive-sha256.json')
hashes={p.relative_to(E).as_posix():sha(p) for p in sorted(E.rglob('*')) if p.is_file() and p!=index}
assert index.read_bytes()==old;save(index,hashes)
print('Updated flight installed checkpoint and prototype-demand evidence',len(hashes))
