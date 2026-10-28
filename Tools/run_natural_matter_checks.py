from pathlib import Path
import os,shutil,subprocess,json,hashlib,datetime
root=Path(__file__).resolve().parents[1]
dotnet=Path(shutil.which('dotnet') or root.parent/'.dotnet/dotnet')
if not dotnet.is_file():dotnet=Path(os.environ['DOTNET_ROOT'])/'dotnet.exe'
version=lambda p:tuple(int(x) if x.isdigit() else 0 for x in p.name.split('.'))
sdk=sorted((dotnet.parent/'sdk').glob('8.*'),key=version)[-1]
refs=sorted((dotnet.parent/'packs/Microsoft.NETCore.App.Ref').glob('8.*'),key=version)[-1]/'ref/net8.0'
out=root/'Tools/NaturalMatterChecks/bin';out.mkdir(parents=True,exist_ok=True)
files=sorted((root/'Assets/AshenVoxel/Core').rglob('*.cs'))+[root/'Assets/AshenVoxel/Exploration/NaturalTreeTemplate.cs',root/'Assets/AshenVoxel/Exploration/NaturalTreePhysics.cs',root/'Assets/_Validation/FiniteMatterSlice/Runtime/LocalizedMatter.cs',root/'Assets/AshenVoxel/WorldScaleData/ExplorationPrototypePack.cs',root/'Tools/NaturalMatterChecks/Program.cs',root/'Tools/NaturalMatterChecks/RegistryShim.cs',root/'Assets/AshenVoxel/Exploration/VegetationTreeMatter.cs']
hashes={str(p.relative_to(root)):hashlib.sha256(p.read_bytes()).hexdigest() for p in files}
q=lambda p:'"'+str(p)+'"'
dll=out/'Checks.dll';rsp=out/'compile.rsp';rsp.write_text('\n'.join(['-nologo','-warnaserror+','-langversion:9.0','-optimize+','-target:exe','-out:'+q(dll)]+['-r:'+q(p) for p in refs.glob('*.dll')]+[q(p) for p in files]))
dll.with_suffix('.runtimeconfig.json').write_text(json.dumps({'runtimeOptions':{'tfm':'net8.0','framework':{'name':'Microsoft.NETCore.App','version':'8.0.0'}}}))
subprocess.run([str(dotnet),str(sdk/'Roslyn/bincore/csc.dll'),'@'+str(rsp)],check=True)
evidence=root/'Docs/NaturalMatterEvidence';evidence.mkdir(exist_ok=True);report=evidence/'template-replay.json'
subprocess.run([str(dotnet),str(dll),str(root/'Assets/AshenVoxel/Exploration/Prototypes/ExplorationPrototypes.bytes'),str(report)],check=True)
assert hashes=={str(p.relative_to(root)):hashlib.sha256(p.read_bytes()).hexdigest() for p in files}
d=json.loads(report.read_text());d.update(source_sha256=hashes,generated_utc=datetime.datetime.now(datetime.timezone.utc).isoformat());report.write_text(json.dumps(d,indent=2)+'\n')
