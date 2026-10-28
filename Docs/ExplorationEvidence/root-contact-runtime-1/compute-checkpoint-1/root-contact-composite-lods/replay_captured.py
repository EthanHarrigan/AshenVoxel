from pathlib import Path
import datetime,hashlib,json,shutil,subprocess
HERE=Path(__file__).resolve().parent;ROOT=HERE.parents[1]
OUT=HERE/('captured-replay-'+datetime.datetime.now(datetime.timezone.utc).strftime('%Y%m%dT%H%M%S%fZ'));OUT.mkdir()
sha=lambda p:hashlib.sha256(p.read_bytes()).hexdigest()
baseline=json.loads((HERE/'specimen-11-20261027T131159753179Z/preimages.json').read_text(encoding='utf8'))
sources=[Path(p) for p in baseline if p.endswith('.cs')]+[HERE/'CapturedReplay.cs']
for p in sources[:-1]:assert sha(p)==baseline[str(p)],'Frozen source changed: '+str(p)
inputs=sources+[Path(__file__)];specimens={10:HERE/'specimen-10-20261027T130937232091Z',11:HERE/'specimen-11-20261027T131159753179Z'}
for id,d in specimens.items():
    manifest=json.loads((d/'manifest.json').read_text(encoding='utf8'))
    for name in ['canonical-order.bin','result.json']+[f'fixed-F{f}.bin' for f in [1,2,4,16,64]]:assert sha(d/name)==manifest['sha256'][name];inputs.append(d/name)
    fixture=ROOT/f'.terrain-cache/root-contact-runtime-prototype/v2-expected-{id}.bin';pre=json.loads((d/'preimages.json').read_text(encoding='utf8'));assert sha(fixture)==pre[str(fixture)];inputs.append(fixture)
hashes={str(p):sha(p) for p in inputs};(OUT/'preimages.json').write_text(json.dumps(hashes,indent=2),encoding='utf8')
dotnet=Path(shutil.which('dotnet'));v=lambda p:tuple(map(int,p.name.split('.')));sdk=sorted((dotnet.parent/'sdk').glob('8.*'),key=v)[-1];refs=sorted((dotnet.parent/'packs/Microsoft.NETCore.App.Ref').glob('8.*'),key=v)[-1]/'ref/net8.0'
assembly=OUT/'CapturedReplay.dll';q=lambda p:'"'+str(p)+'"';options=['-nologo','-warnaserror+','-langversion:9.0','-optimize+','-target:exe','-main:CapturedReplay','-out:'+q(assembly)]+['-r:'+q(p) for p in refs.glob('*.dll')]+[q(p) for p in sources];rsp=OUT/'compile.rsp';rsp.write_text('\n'.join(options),encoding='utf8')
result=subprocess.run([str(dotnet),str(sdk/'Roslyn/bincore/csc.dll'),'@'+str(rsp)],capture_output=True,text=True,encoding='utf8',timeout=30);(OUT/'compile.log').write_text(result.stdout+result.stderr,encoding='utf8');print(result.stdout+result.stderr);result.check_returncode()
assembly.with_suffix('.runtimeconfig.json').write_text(json.dumps({'runtimeOptions':{'tfm':'net8.0','framework':{'name':'Microsoft.NETCore.App','version':'8.0.0'}}}),encoding='utf8')
for id,source in specimens.items():
    dest=OUT/f'specimen-{id}';dest.mkdir();result=subprocess.run([str(dotnet),str(assembly),str(source),str(ROOT/f'.terrain-cache/root-contact-runtime-prototype/v2-expected-{id}.bin'),str(dest)],capture_output=True,text=True,encoding='utf8',timeout=30);(dest/'run.log').write_text(result.stdout+result.stderr,encoding='utf8');print(result.stdout+result.stderr);result.check_returncode()
assert hashes=={str(p):sha(p) for p in inputs},'Input changed during captured replay'
(OUT/'manifest.json').write_text(json.dumps({'status':'PASS','input_sha256':hashes,'sha256':{str(p.relative_to(OUT)):sha(p) for p in OUT.rglob('*') if p.is_file()},'scope':'Bounded captured-data replay only; no full-tree generation.'},indent=2),encoding='utf8')
print(str(OUT))
