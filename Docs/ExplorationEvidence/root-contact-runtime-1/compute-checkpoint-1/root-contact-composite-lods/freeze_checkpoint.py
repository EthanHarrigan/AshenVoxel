from pathlib import Path
import datetime,hashlib,json,shutil,subprocess
HERE=Path(__file__).resolve().parent;ROOT=HERE.parents[1]
def sha(p):
    h=hashlib.sha256()
    with p.open('rb') as f:
        for b in iter(lambda:f.read(1024*1024),b''):h.update(b)
    return h.hexdigest()
dirs=['specimen-10-20261027T130937232091Z','specimen-11-20261027T131159753179Z','policy-20261027T131630278854Z','captured-replay-20261027T131848203063Z']
verified=0
for name in dirs:
    d=HERE/name;m=json.loads((d/'manifest.json').read_text(encoding='utf8'))
    for p,expected in m['sha256'].items():assert sha(d/p)==expected,'Frozen evidence changed '+str(d/p);verified+=1
    inputs=m.get('input_sha256') or json.loads((d/'preimages.json').read_text(encoding='utf8'))
    for p,expected in inputs.items():assert sha(Path(p))==expected,'Input changed '+p;verified+=1
old=ROOT/'.terrain-cache/root-contact-alllod-checks';old_manifest=json.loads((old/'dependency-manifest.json').read_text(encoding='utf8'))
for name,expected in old_manifest['sha256'].items():assert sha(old/name)==expected,'Old LOD evidence changed '+name;verified+=1
for name,expected in old_manifest['unchanged_frozen_inputs'].items():assert sha(Path(name))==expected,'Old frozen input changed '+name;verified+=1
audit=json.loads((HERE/'completion-audit.json').read_text(encoding='utf-8-sig'));assert audit['ownedCompositeCaptureProcessCount']==0
dotnet=Path(shutil.which('dotnet'));v=lambda p:tuple(map(int,p.name.split('.')));sdk=sorted((dotnet.parent/'sdk').glob('8.*'),key=v)[-1];refs=dotnet.parent/'packs/NETStandard.Library.Ref/2.1.0/ref/netstandard2.1'
sources=sorted((ROOT/'Assets/AshenVoxel/Core').rglob('*.cs'))+[ROOT/'Tools/ExplorationPrototypes/UnityShim.cs',ROOT/'.terrain-cache/root-contact-mesh-prototype/RootBaseChunkMesher.cs',HERE/'CompositeLowerLod.cs'];q=lambda p:'"'+str(p)+'"'
options=['-nologo','-warnaserror+','-langversion:9.0','-target:library','-out:'+q(HERE/'CompositeLowerLod.netstandard21.dll')]+['-r:'+q(p) for p in refs.glob('*.dll')]+[q(p) for p in sources]
rsp=HERE/'netstandard21.rsp';assert not rsp.exists();rsp.write_text('\n'.join(options),encoding='utf8');result=subprocess.run([str(dotnet),str(sdk/'Roslyn/bincore/csc.dll'),'@'+str(rsp)],capture_output=True,text=True,encoding='utf8',timeout=30);(HERE/'netstandard21.log').write_text(result.stdout+result.stderr,encoding='utf8');print(result.stdout+result.stderr);result.check_returncode()
names=['CompositeLowerLod.cs','CompositeChecks.cs','run.py','run_guard2.py','PolicyChecks.cs','run_policy.py','CapturedReplay.cs','replay_captured.py','README.md','completion-audit.json','freeze_checkpoint.py','netstandard21.rsp','netstandard21.log','CompositeLowerLod.netstandard21.dll']
checkpoint={'utc':datetime.datetime.now(datetime.timezone.utc).isoformat(),'status':'PASS isolated actual two-root all-LOD CPU equivalence; not production/native integration','actual_assertions':37033317+38142151,'policy_assertions':67,'all10_replayed_mesh_hashes_exact':True,'csharp9_netstandard21_compile':True,'no_owned_capture_processes_remain':True,'prior_frozen_evidence_and_inputs_verified':verified,'evidence_manifests':{name:sha(HERE/name/'manifest.json') for name in dirs},'sha256':{name:sha(HERE/name) for name in names},'scope':'Only this new isolated directory written; root builder, classifier/caps, main pack, Assets, shaders and old frozen sources/evidence untouched.'}
out=HERE/'checkpoint.json';assert not out.exists();out.write_text(json.dumps(checkpoint,indent=2),encoding='utf8');print('PASS checkpoint SHA256 '+sha(out));print('Verified '+str(verified)+' frozen/input hash comparisons')
