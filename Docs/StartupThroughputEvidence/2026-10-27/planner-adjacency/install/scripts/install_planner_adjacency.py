from pathlib import Path
import datetime, hashlib, json, os, shutil, subprocess

ROOT=Path(__file__).resolve().parents[2]
os.chdir(ROOT)
stage=ROOT/'.terrain-cache/quality-patch-staging'
candidate=ROOT/'.terrain-cache/startup-throughput/planner-adjacency-1'
backup=ROOT/'.terrain-cache/startup-throughput/planner-adjacency-preimages-1'
assert not backup.exists(),'Unique install attempt already exists'
assert not (stage/'manifest.json').exists()
assert not (stage/'manifest.json.applied-planner-adjacency-1').exists()
assert not (ROOT/'.terrain-cache/quality-review-job.json').exists()
assert not (ROOT/'.terrain-cache/quality-review-job.json.running').exists()
def sha(p):return hashlib.sha256(p.read_bytes()).hexdigest()
heartbeat=json.loads((ROOT/'.terrain-cache/quality-editor-state.json').read_text())
now=datetime.datetime.now(datetime.timezone.utc)
assert (now-datetime.datetime.fromisoformat(heartbeat['utc'].replace('Z','+00:00'))).total_seconds()<15
assert not any(heartbeat[k] for k in ['playing','changing','compiling'])
memory=json.loads(subprocess.check_output(['powershell','-NoProfile','-Command','Get-CimInstance Win32_PerfFormattedData_PerfOS_Memory | Select-Object AvailableMBytes,CommitLimit,CommittedBytes | ConvertTo-Json'],text=True))
assert memory['CommitLimit']-memory['CommittedBytes']>2*1024**3,'Existing-Editor compile safety reserve below2GiB'
manifest=json.loads((candidate/'manifest.json').read_text())
for row in manifest['files']:
    assert sha(ROOT/row['path'])==row['before'],row['path']
    assert sha(candidate/row['path'])==row['sha256'],row['path']
protected={
 'Assets/AshenVoxel/Settings/ExplorationSettings.asset':'38a9b5b822c4e61418597114703af58cf51847e69f80fd9940494c9e55738664',
 'Assets/Scenes/Exploration.unity':'ba6d8368f6cc8b8916b00fe1d9bec018ac9020e0b22fcd24ab1ef018fd750626',
 str(Path(os.environ['USERPROFILE'])/'AppData/LocalLow/Ashen Voxel/Ashen Voxel Exploration/exploration-options.json'):'93dc85a8d103b3de75587116507cbbd8de4b21029eeb2643cd0ba43c7451c231',
}
installedBase=json.loads((ROOT/'Docs/StartupThroughputEvidence/2026-10-27/flight-hysteresis/install/installed.json').read_text())
protected.update(installedBase['protected_unchanged'])
for row in installedBase['files']:protected[row['path']]=row['sha256']
protected_data={
 'Assets/AshenVoxel/Exploration/Prototypes/ExplorationPrototypes.bytes':'41556b474ce7a26342e4dd7bcff2e4fe658ab0333e8360058262ee21c0f011aa',
 'Assets/AshenVoxel/Exploration/Samples/exploration-world.json':'85ff39810a717cc35db248d9c48b38af52839346e392c32ba707f67e6df94ea3',
}
for p,d in protected_data.items():assert sha(ROOT/p)==d,p
assert json.loads((ROOT/'Assets/AshenVoxel/Exploration/Samples/exploration-world.json').read_text())['world_id']=='14fb32f26ff725abaf9f1e04668a1f46c5b4783a66f91d23253676f595f613e4'
assert not (ROOT/'.terrain-cache/quality-patch-planner-adjacency-1-backup').exists()
for p,d in protected.items():assert sha(ROOT/p)==d,p
backup.mkdir(parents=True)
preimages={}
def preserve(src,dst):
    dst.parent.mkdir(parents=True,exist_ok=True);shutil.copyfile(src,dst)
    assert sha(src)==sha(dst);preimages[dst.relative_to(backup).as_posix()]=sha(dst)
for row in manifest['files']:
    rel=row['path'];preserve(ROOT/rel,backup/'main'/rel)
    if (stage/rel).exists():preserve(stage/rel,backup/'staging'/rel)
for i,(p,d) in enumerate(protected.items()):preserve(ROOT/p,backup/'protected'/f'{i}-{Path(p).name}')
log=Path(os.environ['LOCALAPPDATA'])/'Unity/Editor/Editor.log'
inventory={'utc':now.isoformat(),'memory':memory,'heartbeat':heartbeat,'preimages':preimages,'protected':protected,'frozen_six_sources':manifest['files'],'protected_data':protected_data,
           'editor_log_offset':log.stat().st_size,'authorization':'Coordinator01a17cb5 guarded exact planner-adjacency-1 release required before invoking this prepared installer; main compile/mesh regression only, no Play or new Editor.'}
inventory['asset_source_hashes']={p.relative_to(ROOT).as_posix():sha(p) for p in (ROOT/'Assets').rglob('*') if p.is_file() and p.suffix in ['.cs','.hlsl','.shader','.compute','.meta']}
(backup/'inventory.json').write_text(json.dumps(inventory,indent=2)+'\n')
manifest['files']=[row for row in manifest['files'] if row['before']!=row['sha256']]
assert manifest['files'][0]['path']=='Assets/AshenVoxel/WorldScaleData/LodPlanner.cs'
assert len(manifest['files'])==1
patch={'id':'planner-adjacency-1','utc':now.isoformat(),'files':manifest['files']}
(backup/'manifest.json').write_text(json.dumps(patch,indent=2)+'\n')
for row in manifest['files']:
    rel=row['path'];(stage/rel).parent.mkdir(parents=True,exist_ok=True);shutil.copyfile(candidate/rel,stage/rel)
    assert sha(stage/rel)==row['sha256'];assert sha(ROOT/rel)==row['before']
with (stage/'manifest.json').open('x',encoding='utf-8') as f:json.dump(patch,f,indent=2);f.write('\n')
print(json.dumps({'queued':patch['id'],'backup':str(backup),'commit_headroom_GiB':(memory['CommitLimit']-memory['CommittedBytes'])/1024**3},indent=2))
