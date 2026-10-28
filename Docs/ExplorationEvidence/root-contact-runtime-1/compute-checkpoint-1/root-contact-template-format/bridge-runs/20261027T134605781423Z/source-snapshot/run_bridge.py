from pathlib import Path
import datetime,hashlib,json,shutil,subprocess
HERE=Path(__file__).resolve().parent;DEV=HERE.parents[1];CACHE=DEV/'.terrain-cache'
sha=lambda p:hashlib.sha256(p.read_bytes()).hexdigest()
managed=CACHE/'root-contact-managed-composite/ManagedComposite.cs'
assert sha(managed)=='32d7948cb2232263d809a5c3d266b6ae264255e525009bb72450c4d358c514b5'
array=CACHE/'root-contact-array-mesher'
assert sha(array/'ArrayRootMesher.cs')=='6424b3bd44e09caf291ee434bbd122ebf90286e8c5b6abaa2f1382ce9895ba27'
assert sha(array/'MeshData.cs')=='3e415dc9047f7c24dd01c5988dd9c8a74d963861ac741eabc09cad18443876ca'
export=json.loads((HERE/'export-manifest.json').read_text())
for name,digest in export['input_sha256'].items():assert sha(Path(name))==digest,name
for row in export['models']:assert sha(HERE/row['file'])==row['file_sha256']
out=HERE/'bridge-runs'/datetime.datetime.now(datetime.timezone.utc).strftime('%Y%m%dT%H%M%S%fZ');out.mkdir(parents=True)
sources=sorted((DEV/'Assets/AshenVoxel/Core').rglob('*.cs'))+[CACHE/'root-contact-runtime-prototype/RootContact.cs',CACHE/'root-contact-interaction-prototype/Interaction.cs',array/'MeshData.cs',array/'ArrayRootMesher.cs',managed,HERE/'TemplateBundle.cs',HERE/'ManagedTemplateAssembly.cs']
fixtures=[CACHE/f'root-contact-runtime-prototype/v2-expected-{i}.bin' for i in [10,11]]
fixtures+=list((CACHE/'root-contact-composite-lods/captured-replay-20261027T131848203063Z').rglob('lower-F*.bin'))
inputs=sources+[HERE/'ManagedBridgeChecks.cs',HERE/'run_bridge.py',HERE/'export-manifest.json']+list(HERE.glob('*.avrc'))+fixtures
before={str(p):sha(p) for p in inputs}
dotnet=Path(shutil.which('dotnet'));version=lambda p:tuple(map(int,p.name.split('.')));sdk=sorted((dotnet.parent/'sdk').glob('8.*'),key=version)[-1]
std=dotnet.parent/'packs/NETStandard.Library.Ref/2.1.0/ref/netstandard2.1';net=sorted((dotnet.parent/'packs/Microsoft.NETCore.App.Ref').glob('8.*'),key=version)[-1]/'ref/net8.0'
q=lambda p:'"'+str(p)+'"'
def compile(name,source,refs,kind):
 dll=out/(name+'.dll');rsp=out/(name+'.rsp');rsp.write_text('\n'.join(['-nologo','-warnaserror+','-langversion:9.0','-optimize+','-target:'+kind,'-out:'+q(dll)]+['-r:'+q(p) for p in refs]+[q(p) for p in source]))
 r=subprocess.run([str(dotnet),str(sdk/'Roslyn/bincore/csc.dll'),'@'+str(rsp)],capture_output=True,text=True);(out/(name+'.log')).write_text(r.stdout+r.stderr)
 if r.returncode:raise RuntimeError(r.stdout+r.stderr)
 return dll
library=compile('TemplateBridge',sources,list(std.glob('*.dll')),'library')
exe=compile('BridgeChecks',[HERE/'ManagedBridgeChecks.cs'],list(net.glob('*.dll'))+[library],'exe')
exe.with_suffix('.runtimeconfig.json').write_text(json.dumps({'runtimeOptions':{'tfm':'net8.0','framework':{'name':'Microsoft.NETCore.App','version':'8.0.0'}}}))
r=subprocess.run([str(dotnet),str(exe),str(HERE),str(CACHE),str(out/'checks.json')],capture_output=True,text=True,timeout=60);(out/'run.log').write_text(r.stdout+r.stderr)
unchanged=before=={str(p):sha(p) for p in inputs}
report={'utc':datetime.datetime.now(datetime.timezone.utc).isoformat(),'status':'PASS' if r.returncode==0 and unchanged else 'FAIL','source_input_sha256':before,'inputs_unchanged':unchanged,'returncode':r.returncode,'netstandard21_csharp9_compile':True,'library_sha256':sha(library)}
if (out/'checks.json').exists():report.update({k:v for k,v in json.loads((out/'checks.json').read_text()).items() if k!='status'})
(out/'report.json').write_text(json.dumps(report,indent=2));frozen=out/'source-snapshot';frozen.mkdir()
for p in sources[-7:]+[HERE/'ManagedBridgeChecks.cs',HERE/'run_bridge.py']:(frozen/p.name).write_bytes(p.read_bytes())
print(r.stdout+r.stderr);print(json.dumps({'report':str(out/'report.json'),'status':report['status']}));raise SystemExit(0 if report['status']=='PASS' else 1)
