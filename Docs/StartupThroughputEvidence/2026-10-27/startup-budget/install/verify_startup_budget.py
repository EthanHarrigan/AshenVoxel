from pathlib import Path
import datetime, hashlib, json, os, re, shutil, sys
ROOT=Path(__file__).resolve().parents[2]
os.chdir(ROOT)
B=ROOT/'.terrain-cache/startup-throughput/startup-budget-preimages-1'
E=ROOT/'Docs/StartupThroughputEvidence/2026-10-27/startup-budget/install'
E.mkdir(parents=True,exist_ok=True)
manifest=json.loads((B/'manifest.json').read_text());inventory=json.loads((B/'inventory.json').read_text())
def sha(p):return hashlib.sha256(p.read_bytes()).hexdigest()
for row in manifest['files']:
    assert sha(ROOT/row['path'])==row['sha256'],row['path']
    assert sha(B/'main'/row['path'])==row['before']
    assert sha(ROOT/'.terrain-cache/quality-patch-startup-budget-1-backup'/row['path'])==row['before']
for p,d in inventory['protected'].items():assert sha(ROOT/p)==d,p
heartbeat=json.loads((ROOT/'.terrain-cache/quality-editor-state.json').read_text())
now=datetime.datetime.now(datetime.timezone.utc)
assert (now-datetime.datetime.fromisoformat(heartbeat['utc'].replace('Z','+00:00'))).total_seconds()<15
assert not any(heartbeat[k] for k in ['playing','changing','compiling'])
log=Path(os.environ['LOCALAPPDATA'])/'Unity/Editor/Editor.log'
with log.open('rb') as f:f.seek(inventory['editor_log_offset']);delta=f.read().decode('utf-8',errors='replace')
assert 'QUALITY_PATCH_APPLIED startup-budget-1' in delta
assert 'Mono: successfully reloaded assembly' in delta
assert 'ExitCode: 0' in delta
assert not re.search(r'error CS\d|Shader error|Compilation failed|Exception:|Exception\]',delta)
if sys.argv[1]=='queue':
    (E/'compile-reload.log').write_text(delta,encoding='utf-8')
    (B/'regression-log-offset.json').write_text(json.dumps({'offset':log.stat().st_size,'utc':now.isoformat()}))
    job=ROOT/'.terrain-cache/quality-review-job.json'
    assert not job.exists() and not job.with_name(job.name+'.running').exists()
    with job.open('x',encoding='utf-8') as f:json.dump({'action':'mesh-regression','label':'startup-budget-1'},f)
    print('Verified exact main/rollback/protected hashes and native compile/reload. Queued bounded mesh regression.')
else:
    matches=[p for p in (ROOT/'.terrain-cache').glob('quality-review-job.json.completed-*') if json.loads(p.read_text()).get('label')=='startup-budget-1']
    assert len(matches)==1,'Regression completion missing'
    offset=json.loads((B/'regression-log-offset.json').read_text())['offset']
    with log.open('rb') as f:f.seek(offset);regression=f.read().decode('utf-8',errors='replace')
    assert 'mesh regression' in regression.lower() and ('PASS' in regression or 'passed' in regression),regression
    assert not re.search(r'error CS\d|Shader error|Compilation failed|Exception:|Exception\]',regression)
    (E/'mesh-regression.log').write_text(regression,encoding='utf-8')
    for name in ['manifest.json','inventory.json','regression-log-offset.json']:shutil.copyfile(B/name,E/name)
    for row in manifest['files']:
        for name,src in [('preimages',B/'main'/row['path']),('installed-source',ROOT/row['path'])]:
            dst=E/name/row['path'];dst.parent.mkdir(parents=True,exist_ok=True);shutil.copyfile(src,dst);assert sha(src)==sha(dst)
    result={'utc':now.isoformat(),'id':'startup-budget-1','installed':True,'files':manifest['files'],
            'native_compile_reload':'PASS','mesh_regression':'PASS','protected_unchanged':inventory['protected'],
            'new_native_process':False,'Play_started':False,'small_profile_native_smoke_verified':False,
            'warnings':['Existing DlssNativeCheck.cs obsolete API warning','DLSSIUpscaler null-options pipeline warning'],
            'heartbeat':heartbeat,'preimage_inventory':str(B/'inventory.json')}
    (E/'installed.json').write_text(json.dumps(result,indent=2)+'\n')
    print(json.dumps({'installed':True,'compile':'PASS','mesh_regression':'PASS','readyIdle':True,'evidence':str(E/'installed.json')},indent=2))
