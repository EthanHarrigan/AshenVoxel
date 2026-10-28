from pathlib import Path
import hashlib,json,subprocess,shutil,datetime
HERE=Path(__file__).resolve().parent;DEV=HERE.parents[1];FIX=DEV/'.terrain-cache/root-contact-composite-lods/captured-replay-20261027T131848203063Z'
sha=lambda p:hashlib.sha256(p.read_bytes()).hexdigest()
fixture_manifest=json.loads((FIX/'manifest.json').read_text())
for name,digest in fixture_manifest['sha256'].items():
 path=Path(name)
 if not path.is_absolute():path=FIX/path
 assert sha(path)==digest,'Captured replay identity changed: '+str(path)
out=HERE/'runs'/datetime.datetime.now(datetime.timezone.utc).strftime('%Y%m%dT%H%M%S%fZ');out.mkdir(parents=True)
core=sorted((DEV/'Assets/AshenVoxel/Core').rglob('*.cs'));candidate=[HERE/'MeshData.cs',HERE/'ArrayRootMesher.cs']
baseline=DEV/'.terrain-cache/root-contact-mesh-prototype/RootBaseChunkMesher.cs';assert sha(baseline)=='b5b90cd21534fd69a066e0dfdedd60a4a328b8be67d4ca6c31d84c5ae64ff222'
inputs=core+candidate+[HERE/'Checks.cs',HERE/'derive.py',HERE/'run.py',baseline,DEV/'Tools/ExplorationPrototypes/UnityShim.cs']+sorted(FIX.rglob('*.bin'))+sorted(FIX.rglob('*.json'))
hashes={str(p):sha(p) for p in inputs}
dotnet=Path(shutil.which('dotnet'));version=lambda p:tuple(map(int,p.name.split('.')));sdk=sorted((dotnet.parent/'sdk').glob('8.*'),key=version)[-1]
std=dotnet.parent/'packs/NETStandard.Library.Ref/2.1.0/ref/netstandard2.1';net=sorted((dotnet.parent/'packs/Microsoft.NETCore.App.Ref').glob('8.*'),key=version)[-1]/'ref/net8.0'
def compile(name,sources,refs,kind):
 dll=out/(name+'.dll');rsp=out/(name+'.rsp');q=lambda p:'"'+str(p)+'"';opts=['-nologo','-warnaserror+','-langversion:9.0','-optimize+','-target:'+kind,'-out:'+q(dll)]+['-r:'+q(p) for p in refs]+[q(p) for p in sources];rsp.write_text('\n'.join(opts))
 result=subprocess.run([str(dotnet),str(sdk/'Roslyn/bincore/csc.dll'),'@'+str(rsp)],capture_output=True,text=True);(out/(name+'.log')).write_text(result.stdout+result.stderr)
 if result.returncode:raise RuntimeError(result.stdout+result.stderr)
 return dll
library=compile('ArrayMesher',core+candidate,list(std.glob('*.dll')),'library')
test=compile('Checks',[HERE/'Checks.cs',baseline,DEV/'Tools/ExplorationPrototypes/UnityShim.cs'],list(net.glob('*.dll'))+[library],'exe')
test.with_suffix('.runtimeconfig.json').write_text(json.dumps({'runtimeOptions':{'tfm':'net8.0','framework':{'name':'Microsoft.NETCore.App','version':'8.0.0'}}}))
r=subprocess.run([str(dotnet),str(test),str(FIX),str(out/'checks.json')],capture_output=True,text=True);(out/'run.log').write_text(r.stdout+r.stderr)
unchanged=hashes=={str(p):sha(p) for p in inputs}
report={'status':'PASS' if r.returncode==0 and unchanged else 'FAIL','scope':'Pure managed candidate C#9/netstandard2.1; test-only Mesh shim oracle. No Unity module dependency, native run, tree generation or production installation.','source_input_sha256':hashes,'inputs_unchanged':unchanged,'library_sha256':sha(library),'returncode':r.returncode}
if (out/'checks.json').exists():
 checks=json.loads((out/'checks.json').read_text());report.update({k:v for k,v in checks.items() if k not in ('status','scope')});report['checks_scope']=checks['scope']
(out/'report.json').write_text(json.dumps(report,indent=2));print(r.stdout+r.stderr);print(json.dumps({'status':report['status'],'report':str(out/'report.json')},indent=2));raise SystemExit(0 if report['status']=='PASS' else 1)
