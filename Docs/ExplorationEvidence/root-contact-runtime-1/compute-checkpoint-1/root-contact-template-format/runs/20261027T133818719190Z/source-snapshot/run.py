from pathlib import Path
import datetime,hashlib,json,shutil,subprocess
HERE=Path(__file__).resolve().parent;DEV=HERE.parents[1];CACHE=DEV/'.terrain-cache'
sha=lambda p:hashlib.sha256(p.read_bytes()).hexdigest()
out=HERE/'runs'/datetime.datetime.now(datetime.timezone.utc).strftime('%Y%m%dT%H%M%S%fZ');out.mkdir(parents=True)
export=json.loads((HERE/'export-manifest.json').read_text())
for name,digest in export['input_sha256'].items():assert sha(Path(name))==digest,name
for model in export['models']:assert sha(HERE/model['file'])==model['file_sha256']
sources=sorted((DEV/'Assets/AshenVoxel/Core').rglob('*.cs'))+[CACHE/'root-contact-runtime-prototype/RootContact.cs',CACHE/'root-contact-interaction-prototype/Interaction.cs',HERE/'TemplateBundle.cs']
inputs=sources+[HERE/'Checks.cs',HERE/'run.py',HERE/'export.py',HERE/'export-manifest.json']+list(HERE.glob('*.avrc'))
before={str(p):sha(p) for p in inputs}
dotnet=Path(shutil.which('dotnet'));version=lambda p:tuple(map(int,p.name.split('.')))
sdk=sorted((dotnet.parent/'sdk').glob('8.*'),key=version)[-1]
std=dotnet.parent/'packs/NETStandard.Library.Ref/2.1.0/ref/netstandard2.1'
net=sorted((dotnet.parent/'packs/Microsoft.NETCore.App.Ref').glob('8.*'),key=version)[-1]/'ref/net8.0'
q=lambda p:'"'+str(p)+'"'
def compile(name,source,refs,kind):
 dll=out/(name+'.dll');rsp=out/(name+'.rsp')
 rsp.write_text('\n'.join(['-nologo','-warnaserror+','-langversion:9.0','-optimize+','-target:'+kind,'-out:'+q(dll)]+['-r:'+q(p) for p in refs]+[q(p) for p in source]))
 result=subprocess.run([str(dotnet),str(sdk/'Roslyn/bincore/csc.dll'),'@'+str(rsp)],capture_output=True,text=True)
 (out/(name+'.log')).write_text(result.stdout+result.stderr)
 if result.returncode:raise RuntimeError(result.stdout+result.stderr)
 return dll
library=compile('TemplateBundle',sources,list(std.glob('*.dll')),'library')
exe=compile('Checks',[HERE/'Checks.cs'],list(net.glob('*.dll'))+[library],'exe')
exe.with_suffix('.runtimeconfig.json').write_text(json.dumps({'runtimeOptions':{'tfm':'net8.0','framework':{'name':'Microsoft.NETCore.App','version':'8.0.0'}}}))
r=subprocess.run([str(dotnet),str(exe),str(HERE),str(CACHE),str(out/'checks.json')],capture_output=True,text=True,timeout=60)
(out/'run.log').write_text(r.stdout+r.stderr)
unchanged=before=={str(p):sha(p) for p in inputs}
report={'utc':datetime.datetime.now(datetime.timezone.utc).isoformat(),'status':'PASS' if r.returncode==0 and unchanged else 'FAIL','source_input_sha256':before,'inputs_unchanged':unchanged,'returncode':r.returncode,'netstandard21_csharp9_compile':True,'library_sha256':sha(library)}
if (out/'checks.json').exists():report.update(json.loads((out/'checks.json').read_text()))
(out/'report.json').write_text(json.dumps(report,indent=2))
frozen=out/'source-snapshot';frozen.mkdir()
for p in [HERE/'TemplateBundle.cs',HERE/'Checks.cs',HERE/'run.py',HERE/'export.py']:(frozen/p.name).write_bytes(p.read_bytes())
print(r.stdout+r.stderr);print(json.dumps({'report':str(out/'report.json'),'status':report['status']}));raise SystemExit(0 if report['status']=='PASS' else 1)
