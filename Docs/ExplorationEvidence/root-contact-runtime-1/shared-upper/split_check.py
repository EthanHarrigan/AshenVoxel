from pathlib import Path
import datetime,hashlib,json,shutil,subprocess
HERE=Path(__file__).resolve().parent;ROOT=HERE.parents[1]
version=lambda p:tuple(int(n) for n in p.name.split('.'))
dotnet=Path(shutil.which('dotnet'));sdk=sorted((dotnet.parent/'sdk').glob('8.*'),key=version)[-1];refs=sorted((dotnet.parent/'packs/Microsoft.NETCore.App.Ref').glob('8.*'),key=version)[-1]/'ref/net8.0'
sources=sorted((ROOT/'Assets/AshenVoxel/Core').rglob('*.cs'))+sorted((ROOT/'Assets/AshenVoxel/WorldScaleData').glob('*.cs'))+[HERE/'SharedUpperSplit.cs',HERE/'SplitChecks.cs']
pack=ROOT.parent/'AshenVoxel/Assets/AshenVoxel/Exploration/Prototypes/ExplorationPrototypes.bytes';inputs=sources+[pack];hashes={str(p):hashlib.sha256(p.read_bytes()).hexdigest() for p in inputs}
assembly=HERE/'SplitChecks.dll';q=lambda p:'"'+str(p)+'"'
opts=['-nologo','-warnaserror+','-langversion:9.0','-optimize+','-target:exe','-out:'+q(assembly)]+['-r:'+q(p) for p in refs.glob('*.dll')]+[q(p) for p in sources]
rsp=HERE/'split.rsp';rsp.write_text('\n'.join(opts),encoding='utf-8');subprocess.run([str(dotnet),str(sdk/'Roslyn/bincore/csc.dll'),'@'+str(rsp)],check=True)
assembly.with_suffix('.runtimeconfig.json').write_text(json.dumps({'runtimeOptions':{'tfm':'net8.0','framework':{'name':'Microsoft.NETCore.App','version':'8.0.0'}}}))
subprocess.run([str(dotnet),str(assembly),str(pack),str(HERE/'shared-upper.json')],check=True)
assert hashes=={str(p):hashlib.sha256(p.read_bytes()).hexdigest() for p in inputs}
(HERE/'shared-upper-provenance.json').write_text(json.dumps({'utc':datetime.datetime.now(datetime.timezone.utc).isoformat(),'source_and_input_sha256':hashes},indent=2),encoding='utf-8')
