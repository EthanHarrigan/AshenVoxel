from pathlib import Path
import datetime,hashlib,json,os,shutil,subprocess,time
HERE=Path(__file__).resolve().parent;ROOT=HERE.parents[1];OUT=HERE/'runs'/datetime.datetime.now(datetime.timezone.utc).strftime('%Y%m%dT%H%M%S%fZ');OUT.mkdir(parents=True)
sha=lambda p:hashlib.sha256(p.read_bytes()).hexdigest()
array=ROOT/'.terrain-cache/root-contact-array-mesher';known={'MeshData.cs':'3e415dc9047f7c24dd01c5988dd9c8a74d963861ac741eabc09cad18443876ca','ArrayRootMesher.cs':'6424b3bd44e09caf291ee434bbd122ebf90286e8c5b6abaa2f1382ce9895ba27'}
for name,digest in known.items():assert sha(array/name)==digest,'Finalized array API changed'
production=sorted((ROOT/'Assets/AshenVoxel/Core').rglob('*.cs'))+[array/n for n in known]+[HERE/'ManagedComposite.cs']
inputs=production+[HERE/'Checks.cs',Path(__file__)]
base=ROOT/'.terrain-cache/root-contact-composite-lods';dirs=[base/'specimen-10-20261027T130937232091Z',base/'specimen-11-20261027T131159753179Z'];fixtures=[]
for id,d in zip([10,11],dirs):
    manifest=json.loads((d/'manifest.json').read_text(encoding='utf8'))
    for name in ['canonical-order.bin','result.json']+[f'fixed-F{f}.bin' for f in [1,2,4,16,64]]:assert sha(d/name)==manifest['sha256'][name];inputs.append(d/name)
    fixture=ROOT/f'.terrain-cache/root-contact-runtime-prototype/v2-expected-{id}.bin';pre=json.loads((d/'preimages.json').read_text(encoding='utf8'));assert sha(fixture)==pre[str(fixture)];fixtures.append(fixture);inputs.append(fixture)
hashes={str(p):sha(p) for p in inputs};(OUT/'preimages.json').write_text(json.dumps(hashes,indent=2),encoding='utf8');snapshots=OUT/'sources';snapshots.mkdir()
for p in production+[HERE/'Checks.cs',Path(__file__)]:dest=snapshots/p.name;assert not dest.exists();shutil.copyfile(p,dest);assert sha(dest)==hashes[str(p)]
dotnet=Path(shutil.which('dotnet'));v=lambda p:tuple(map(int,p.name.split('.')));sdk=sorted((dotnet.parent/'sdk').glob('8.*'),key=v)[-1];std=dotnet.parent/'packs/NETStandard.Library.Ref/2.1.0/ref/netstandard2.1';net=sorted((dotnet.parent/'packs/Microsoft.NETCore.App.Ref').glob('8.*'),key=v)[-1]/'ref/net8.0';q=lambda p:'"'+str(p)+'"'
def compile(name,sources,refs,library=False,extra=[]):
    target=OUT/(name+'.dll');options=['-nologo','-warnaserror+','-langversion:9.0','-optimize+','-target:'+('library' if library else 'exe'),'-out:'+q(target)]+['-r:'+q(p) for p in refs.glob('*.dll')]+['-r:'+q(p) for p in extra]+[q(p) for p in sources];rsp=OUT/(name+'.rsp');rsp.write_text('\n'.join(options),encoding='utf8');result=subprocess.run([str(dotnet),str(sdk/'Roslyn/bincore/csc.dll'),'@'+str(rsp)],capture_output=True,text=True,encoding='utf8',timeout=30);(OUT/(name+'-compile.log')).write_text(result.stdout+result.stderr,encoding='utf8');print(result.stdout+result.stderr);result.check_returncode();return target
try:
    library=compile('ManagedComposite',production,std,True);checks=compile('Checks',[HERE/'Checks.cs'],net,extra=[library]);checks.with_suffix('.runtimeconfig.json').write_text(json.dumps({'runtimeOptions':{'tfm':'net8.0','framework':{'name':'Microsoft.NETCore.App','version':'8.0.0'}}}),encoding='utf8')
    env=os.environ.copy();env['DOTNET_GCHeapHardLimit']='8000000';start=time.monotonic();result=subprocess.run([str(dotnet),str(checks),str(OUT),str(dirs[0]),str(fixtures[0]),str(dirs[1]),str(fixtures[1])],capture_output=True,text=True,encoding='utf8',timeout=45,env=env);(OUT/'run.log').write_text(result.stdout+result.stderr,encoding='utf8');print(result.stdout+result.stderr);result.check_returncode()
    assert hashes=={str(p):sha(p) for p in inputs},'Input changed during tests'
    (OUT/'manifest.json').write_text(json.dumps({'status':'PASS','utc':datetime.datetime.now(datetime.timezone.utc).isoformat(),'elapsed_process_seconds':time.monotonic()-start,'managed_heap_limit_bytes':128*2**20,'timeout_seconds':45,'inputs_unchanged':True,'source_and_input_sha256':hashes,'sha256':{str(p.relative_to(OUT)):sha(p) for p in OUT.rglob('*') if p.is_file()},'scope':'Managed captured-data checks only. No generation, pack decode, Unity/shim/native dependency or production worker/cache integration.'},indent=2),encoding='utf8')
except Exception as error:
    (OUT/'runner-failure.json').write_text(json.dumps({'status':'FAIL','error':str(error),'source_and_input_sha256':hashes,'scope':'Failure and source snapshots retained; no automatic script retry.'},indent=2),encoding='utf8');print('FAILED evidence '+str(OUT));raise
print('PASS evidence '+str(OUT))
