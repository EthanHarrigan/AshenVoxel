from pathlib import Path
import hashlib,json,shutil,subprocess,datetime
HERE=Path(__file__).resolve().parent;ROOT=HERE.parents[1]
source=ROOT/'Assets/AshenVoxel/Rendering/GreedyChunkMesher.cs';original=source.read_bytes();text=original.decode('utf-8').replace('\r\n','\n')
text=text.replace('public sealed class GreedyChunkMesher','public sealed class RootBaseChunkMesher')
text=text.replace('public int Build(VoxelWorld world, Int3 coordinate, Mesh destination)','public int Build(VoxelWorld world, Int3 coordinate, Mesh destination, int maximumEmittedCellY = int.MaxValue)')
before='p[axis] = slice;\n                            colorMask[maskIndex]'
assert text.count(before)==1
text=text.replace(before,'p[axis] = slice;\n                            if (by + p[1] >= maximumEmittedCellY) continue;\n                            colorMask[maskIndex]')
before='else if (facing == 1 && b != MaterialId.Air && a != b && slice < Side - 1)\n                        {\n                            colorMask[maskIndex]'
assert text.count(before)==1
text=text.replace(before,'else if (facing == 1 && b != MaterialId.Air && a != b && slice < Side - 1)\n                        {\n                            if (by + p[1] >= maximumEmittedCellY) continue;\n                            colorMask[maskIndex]')
candidate=HERE/'RootBaseChunkMesher.cs';candidate.write_text(text,encoding='utf-8')
assert source.read_bytes()==original
version=lambda p:tuple(int(n) for n in p.name.split('.'))
dotnet=Path(shutil.which('dotnet'));sdk=sorted((dotnet.parent/'sdk').glob('8.*'),key=version)[-1]
refs=sorted((dotnet.parent/'packs/Microsoft.NETCore.App.Ref').glob('8.*'),key=version)[-1]/'ref/net8.0'
sources=sorted((ROOT/'Assets/AshenVoxel/Core').rglob('*.cs'))+[source,candidate,ROOT/'Tools/ExplorationPrototypes/UnityShim.cs',HERE/'Check.cs']
hashes={str(p):hashlib.sha256(p.read_bytes()).hexdigest() for p in sources};assembly=HERE/'Check.dll';q=lambda p:'"'+str(p)+'"'
opts=['-nologo','-warnaserror+','-langversion:9.0','-optimize+','-target:exe','-out:'+q(assembly)]+['-r:'+q(p) for p in refs.glob('*.dll')]+[q(p) for p in sources]
rsp=HERE/'compile.rsp';rsp.write_text('\n'.join(opts),encoding='utf-8')
subprocess.run([str(dotnet),str(sdk/'Roslyn/bincore/csc.dll'),'@'+str(rsp)],check=True)
assembly.with_suffix('.runtimeconfig.json').write_text(json.dumps({'runtimeOptions':{'tfm':'net8.0','framework':{'name':'Microsoft.NETCore.App','version':'8.0.0'}}}))
subprocess.run([str(dotnet),str(assembly),str(ROOT/'.terrain-cache/grounding-root-visual/continuous-shaft'),str(HERE/'validation.json')],check=True)
assert hashes=={str(p):hashlib.sha256(p.read_bytes()).hexdigest() for p in sources},'Source changed'
(HERE/'provenance.json').write_text(json.dumps({'utc':datetime.datetime.now(datetime.timezone.utc).isoformat(),'source_sha256':hashes,'status':'isolated CPU only, no Assets changes'},indent=2),encoding='utf-8')
