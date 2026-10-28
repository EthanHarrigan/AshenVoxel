from pathlib import Path
import hashlib,json,shutil,subprocess,datetime
HERE=Path(__file__).resolve().parent;ROOT=HERE.parents[1];visual=ROOT/'.terrain-cache/grounding-root-visual'
version=lambda p:tuple(int(n) for n in p.name.split('.'))
dotnet=Path(shutil.which('dotnet'));sdk=sorted((dotnet.parent/'sdk').glob('8.*'),key=version)[-1];refs=sorted((dotnet.parent/'packs/Microsoft.NETCore.App.Ref').glob('8.*'),key=version)[-1]/'ref/net8.0'
sources=sorted((ROOT/'Assets/AshenVoxel/Core').rglob('*.cs'))+[ROOT/'Assets/AshenVoxel/Rendering/GreedyChunkMesher.cs',ROOT/'Tools/ExplorationPrototypes/UnityShim.cs',HERE/'UpperSeamCheck.cs']
inputs=sources+[visual/f'wood-{i}-baseline.bin' for i in [10,11]]+[visual/f'continuous-shaft/expected-{i}.bin' for i in [10,11]]
hashes={str(p):hashlib.sha256(p.read_bytes()).hexdigest() for p in inputs};assembly=HERE/'UpperSeamCheck.dll';q=lambda p:'"'+str(p)+'"'
opts=['-nologo','-warnaserror+','-langversion:9.0','-optimize+','-target:exe','-out:'+q(assembly)]+['-r:'+q(p) for p in refs.glob('*.dll')]+[q(p) for p in sources]
rsp=HERE/'upper.rsp';rsp.write_text('\n'.join(opts),encoding='utf-8');subprocess.run([str(dotnet),str(sdk/'Roslyn/bincore/csc.dll'),'@'+str(rsp)],check=True)
assembly.with_suffix('.runtimeconfig.json').write_text(json.dumps({'runtimeOptions':{'tfm':'net8.0','framework':{'name':'Microsoft.NETCore.App','version':'8.0.0'}}}))
subprocess.run([str(dotnet),str(assembly),str(visual),str(HERE/'upper-seam.json')],check=True)
assert hashes=={str(p):hashlib.sha256(p.read_bytes()).hexdigest() for p in inputs}
(HERE/'upper-seam-provenance.json').write_text(json.dumps({'utc':datetime.datetime.now(datetime.timezone.utc).isoformat(),'source_and_input_sha256':hashes},indent=2),encoding='utf-8')
