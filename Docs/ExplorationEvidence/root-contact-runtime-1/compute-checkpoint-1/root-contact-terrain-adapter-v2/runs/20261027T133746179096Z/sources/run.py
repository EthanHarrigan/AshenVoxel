from pathlib import Path
import datetime,hashlib,json,shutil,subprocess
HERE=Path(__file__).resolve().parent;OLD=HERE.parent/'root-contact-terrain-adapter'
sha=lambda p:hashlib.sha256(p.read_bytes()).hexdigest()
known=json.loads((OLD/'report.json').read_text())['source_sha256']
for path,digest in known.items():assert sha(Path(path))==digest,path
original_detail=next(Path(s) for s in known if Path(s).name=='TerrainDetailField.cs')
assert sha(original_detail)=='8393d97d95a2d4deffda3db93d8b077cfa7223bb98f1e2c55b97f8242d17ac1b'
sources=[Path(s) for s in known if s.endswith('.cs') and Path(s).name not in ('Checks.cs','RootTerrainSnapshot.cs','TerrainDetailField.cs')]+[HERE/'RootTerrainSnapshot.cs',HERE/'TerrainDetailField.cs']
inputs=sources+[OLD/'Checks.cs',HERE/'LayeringChecks.cs',HERE/'run.py',HERE/'TerrainDetailField.diff',OLD/'RootTerrainSnapshot.cs',original_detail]
identities={str(p):sha(p) for p in inputs}
out=HERE/'runs'/datetime.datetime.now(datetime.timezone.utc).strftime('%Y%m%dT%H%M%S%fZ');out.mkdir(parents=True)
dotnet=Path(shutil.which('dotnet'));version=lambda p:tuple(map(int,p.name.split('.')));sdk=sorted((dotnet.parent/'sdk').glob('8.*'),key=version)[-1]
net=sorted((dotnet.parent/'packs/Microsoft.NETCore.App.Ref').glob('8.*'),key=version)[-1]/'ref/net8.0';std=dotnet.parent/'packs/NETStandard.Library.Ref/2.1.0/ref/netstandard2.1';q=lambda p:'"'+str(p)+'"'
def compile(name,files,references,kind):
 dll=out/(name+'.dll');rsp=out/(name+'.rsp');rsp.write_text('\n'.join(['-nologo','-warnaserror+','-langversion:9.0','-target:'+kind,'-out:'+q(dll)]+['-r:'+q(p) for p in references]+[q(p) for p in files]));r=subprocess.run([str(dotnet),str(sdk/'Roslyn/bincore/csc.dll'),'@'+str(rsp)],capture_output=True,text=True);(out/(name+'.log')).write_text(r.stdout+r.stderr);assert r.returncode==0,r.stdout+r.stderr;return dll
library=compile('TerrainV2',sources,list(std.glob('*.dll')),'library')
for name,test in [('Regression',OLD/'Checks.cs'),('Layering',HERE/'LayeringChecks.cs')]:
 dll=compile(name,[test],list(net.glob('*.dll'))+[library],'exe');dll.with_suffix('.runtimeconfig.json').write_text(json.dumps({'runtimeOptions':{'tfm':'net8.0','framework':{'name':'Microsoft.NETCore.App','version':'8.0.0'}}}));target=out/name;target.mkdir();arg=target if name=='Regression' else target/'checks.json';r=subprocess.run([str(dotnet),str(dll),str(arg)],capture_output=True,text=True);(out/(name+'-run.log')).write_text(r.stdout+r.stderr);assert r.returncode==0,r.stdout+r.stderr;print(r.stdout)
assert identities=={str(p):sha(Path(p)) for p in identities},'Inputs changed'
snapshot=out/'sources';snapshot.mkdir()
for p in [HERE/'RootTerrainSnapshot.cs',HERE/'TerrainDetailField.cs',HERE/'LayeringChecks.cs',HERE/'TerrainDetailField.diff',HERE/'run.py']:shutil.copyfile(p,snapshot/p.name)
report={'status':'PASS','scope':'Separate candidate only. Original frozen adapter and MAIN unchanged. C#9/netstandard2.1; small analytical CPU checks; no native/world/pack/generation.','source_sha256':identities,'original_regression':json.loads((out/'Regression/checks.json').read_text()),'layering':json.loads((out/'Layering/checks.json').read_text()),'library_sha256':sha(library)}
(out/'report.json').write_text(json.dumps(report,indent=2));print(out/'report.json')
