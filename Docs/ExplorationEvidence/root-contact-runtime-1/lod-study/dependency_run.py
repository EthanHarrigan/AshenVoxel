from pathlib import Path
import datetime,hashlib,json,shutil,subprocess
HERE=Path(__file__).resolve().parent;ROOT=HERE.parents[1]
names=['Tools/ExplorationPrototypes/ExportPrototypes.cs','Assets/AshenVoxel/Rendering/GreedyChunkMesher.cs','Tools/ExplorationPrototypes/UnityShim.cs']
bake=ROOT/'Docs/exploration-prototype-bake.json';report=json.loads(bake.read_text(encoding='utf8'))
known={k.replace('\\','/'):v for k,v in report['bakes'][0]['source_sha256'].items()}
for name in names:assert hashlib.sha256((ROOT/name).read_bytes()).hexdigest()==known[name],name+' differs from actual pack baker'
assert report['asset_sha256']=='41556b474ce7a26342e4dd7bcff2e4fe658ab0333e8360058262ee21c0f011aa'
sources=sorted((ROOT/'Assets/AshenVoxel/Core').rglob('*.cs'))+[ROOT/'Assets/AshenVoxel/WorldScaleData/ExplorationPrototypePack.cs']+[ROOT/n for n in names]+[HERE/'DependencyChecks.cs']
inputs=sources+[bake,HERE/'dependency_run.py'];hashes={str(p):hashlib.sha256(p.read_bytes()).hexdigest() for p in inputs}
dotnet=Path(shutil.which('dotnet'));version=lambda p:tuple(map(int,p.name.split('.')));sdk=sorted((dotnet.parent/'sdk').glob('8.*'),key=version)[-1];refs=sorted((dotnet.parent/'packs/Microsoft.NETCore.App.Ref').glob('8.*'),key=version)[-1]/'ref/net8.0'
assembly=HERE/'DependencyChecks.dll';q=lambda p:'"'+str(p)+'"';options=['-nologo','-warnaserror+','-langversion:9.0','-optimize+','-target:exe','-main:DependencyChecks','-out:'+q(assembly)]+['-r:'+q(p) for p in refs.glob('*.dll')]+[q(p) for p in sources]
rsp=HERE/'dependency-compile.rsp';rsp.write_text('\n'.join(options),encoding='utf8');result=subprocess.run([str(dotnet),str(sdk/'Roslyn/bincore/csc.dll'),'@'+str(rsp)],capture_output=True,text=True,encoding='utf8');(HERE/'dependency-compile.log').write_text(result.stdout+result.stderr,encoding='utf8');print(result.stdout+result.stderr);result.check_returncode()
assembly.with_suffix('.runtimeconfig.json').write_text(json.dumps({'runtimeOptions':{'tfm':'net8.0','framework':{'name':'Microsoft.NETCore.App','version':'8.0.0'}}}),encoding='utf8')
result=subprocess.run([str(dotnet),str(assembly),str(HERE/'dependency-results.json')],capture_output=True,text=True,encoding='utf8');(HERE/'dependency-run.log').write_text(result.stdout+result.stderr,encoding='utf8');print(result.stdout+result.stderr);result.check_returncode()
assert hashes=={str(p):hashlib.sha256(p.read_bytes()).hexdigest() for p in inputs}
(HERE/'dependency-provenance.json').write_text(json.dumps({'status':'PASS','utc':datetime.datetime.now(datetime.timezone.utc).isoformat(),'source_and_input_sha256':hashes,'actual_bake_source_match':names,'pack_identity':report['asset_sha256'],'scope':'Read-only actual baker and mesher source; only supplemental output folder written. ExportPrototypes.Main never invoked.'},indent=2),encoding='utf8')
