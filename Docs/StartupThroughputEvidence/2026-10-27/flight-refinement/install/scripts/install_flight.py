from pathlib import Path
import datetime, hashlib, json, os, shutil, subprocess

ROOT=Path(__file__).resolve().parents[2]
os.chdir(ROOT)
stage=ROOT/'.terrain-cache/quality-patch-staging'
candidate=ROOT/'.terrain-cache/startup-throughput/flight-refinement-1/flight-final'
backup=ROOT/'.terrain-cache/startup-throughput/flight-refinement-preimages-1'
assert not backup.exists(),'Unique install attempt already exists'
assert not (stage/'manifest.json').exists()
assert not (stage/'manifest.json.applied-flight-refinement-1').exists()
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
inventory={'utc':now.isoformat(),'memory':memory,'heartbeat':heartbeat,'preimages':preimages,'protected':protected,'frozen_six_sources':json.loads((candidate/'manifest.json').read_text())['files'],
           'editor_log_offset':log.stat().st_size,'authorization':'Coordinator01a17cb5 guarded exact flight-refinement-1 release; main compile/mesh regression only, no Play or new Editor.'}
(backup/'inventory.json').write_text(json.dumps(inventory,indent=2)+'\n')
manifest['files']=[row for row in manifest['files'] if row['before']!=row['sha256']]
assert len(manifest['files'])==4
patch={'id':'flight-refinement-1','utc':now.isoformat(),'files':manifest['files']}
(backup/'manifest.json').write_text(json.dumps(patch,indent=2)+'\n')
for row in manifest['files']:
    rel=row['path'];(stage/rel).parent.mkdir(parents=True,exist_ok=True);shutil.copyfile(candidate/rel,stage/rel)
    assert sha(stage/rel)==row['sha256'];assert sha(ROOT/rel)==row['before']
with (stage/'manifest.json').open('x',encoding='utf-8') as f:json.dump(patch,f,indent=2);f.write('\n')
print(json.dumps({'queued':patch['id'],'backup':str(backup),'commit_headroom_GiB':(memory['CommitLimit']-memory['CommittedBytes'])/1024**3},indent=2))
