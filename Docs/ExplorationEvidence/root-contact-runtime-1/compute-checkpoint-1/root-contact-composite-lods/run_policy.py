from pathlib import Path
import datetime,hashlib,json,shutil,subprocess
HERE=Path(__file__).resolve().parent;ROOT=HERE.parents[1]
OUT=HERE/('policy-'+datetime.datetime.now(datetime.timezone.utc).strftime('%Y%m%dT%H%M%S%fZ'));OUT.mkdir()
sha=lambda p:hashlib.sha256(p.read_bytes()).hexdigest()
baseline=json.loads((HERE/'specimen-11-20261027T131159753179Z/preimages.json').read_text(encoding='utf8'))
sources=[Path(p) for p in baseline if p.endswith('.cs')]+[HERE/'PolicyChecks.cs']
for p in sources[:-1]:assert sha(p)==baseline[str(p)],'Frozen source changed: '+str(p)
inputs=sources+[Path(__file__)];hashes={str(p):sha(p) for p in inputs};(OUT/'preimages.json').write_text(json.dumps(hashes,indent=2),encoding='utf8')
dotnet=Path(shutil.which('dotnet'));v=lambda p:tuple(map(int,p.name.split('.')));sdk=sorted((dotnet.parent/'sdk').glob('8.*'),key=v)[-1];refs=sorted((dotnet.parent/'packs/Microsoft.NETCore.App.Ref').glob('8.*'),key=v)[-1]/'ref/net8.0'
assembly=OUT/'PolicyChecks.dll';q=lambda p:'"'+str(p)+'"';options=['-nologo','-warnaserror+','-langversion:9.0','-optimize+','-target:exe','-main:PolicyChecks','-out:'+q(assembly)]+['-r:'+q(p) for p in refs.glob('*.dll')]+[q(p) for p in sources];rsp=OUT/'compile.rsp';rsp.write_text('\n'.join(options),encoding='utf8')
result=subprocess.run([str(dotnet),str(sdk/'Roslyn/bincore/csc.dll'),'@'+str(rsp)],capture_output=True,text=True,encoding='utf8',timeout=30);(OUT/'compile.log').write_text(result.stdout+result.stderr,encoding='utf8');print(result.stdout+result.stderr);result.check_returncode()
assembly.with_suffix('.runtimeconfig.json').write_text(json.dumps({'runtimeOptions':{'tfm':'net8.0','framework':{'name':'Microsoft.NETCore.App','version':'8.0.0'}}}),encoding='utf8')
result=subprocess.run([str(dotnet),str(assembly),str(OUT/'result.json')],capture_output=True,text=True,encoding='utf8',timeout=30);(OUT/'run.log').write_text(result.stdout+result.stderr,encoding='utf8');print(result.stdout+result.stderr);result.check_returncode()
assert hashes=={str(p):sha(p) for p in inputs},'Input changed during policy check'
(OUT/'manifest.json').write_text(json.dumps({'status':'PASS','input_sha256':hashes,'sha256':{p.name:sha(p) for p in OUT.iterdir() if p.is_file()},'scope':'Small synthetic CPU checks only; no full-tree generation.'},indent=2),encoding='utf8')
print(str(OUT))
