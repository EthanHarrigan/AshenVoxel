from pathlib import Path
import hashlib,json,math,shutil,struct,subprocess,datetime
import compare as c
from seam_check import build
HERE=Path(__file__).resolve().parent;OUT=HERE/'continuous-shaft'
for site in json.loads((HERE/'sites.json').read_text()):
    terr=c.terrain(site['id']);sample=c.overlap_sampler(site,terr[0],terr[1]);original,base,result,extra=build(site,sample)
    cols=c.columns({p:v for p,v in base.items() if p[1]<=c.SEAM})
    with (OUT/f"cost-{site['id']}.bin").open('wb') as f:
        f.write(struct.pack('<i',len(cols)))
        for (x,z),col in sorted(cols.items()):
            target=math.floor((sample(x,z)[0]-site['original_y'])/.1+1e-7)
            f.write(struct.pack('<hhhi',x,z,target,len(col)))
            for y,color in col:f.write(struct.pack('<hI',y,color))
    cells={p:v for p,v in result.items() if p[1]<=c.SEAM}
    with (OUT/f"expected-{site['id']}.bin").open('wb') as f:
        f.write(struct.pack('<i',len(cells)))
        for (x,y,z),color in sorted(cells.items()):f.write(struct.pack('<hhhI',x,y,z,color))
version=lambda p:tuple(int(n) for n in p.name.split('.'))
dotnet=Path(shutil.which('dotnet'));sdk=sorted((dotnet.parent/'sdk').glob('8.*'),key=version)[-1]
refs=sorted((dotnet.parent/'packs/Microsoft.NETCore.App.Ref').glob('8.*'),key=version)[-1]/'ref/net8.0'
sources=sorted((c.ROOT/'Assets/AshenVoxel/Core').rglob('*.cs'))+[c.ROOT/'Assets/AshenVoxel/Rendering/GreedyChunkMesher.cs',c.ROOT/'Tools/ExplorationPrototypes/UnityShim.cs',HERE/'BaseCost.cs']
hashes={str(p):hashlib.sha256(p.read_bytes()).hexdigest() for p in sources}
assembly=OUT/'BaseCost.dll';q=lambda p:'"'+str(p)+'"'
opts=['-nologo','-warnaserror+','-langversion:9.0','-optimize+','-target:exe','-out:'+q(assembly)]+['-r:'+q(p) for p in refs.glob('*.dll')]+[q(p) for p in sources]
rsp=OUT/'cost.rsp';rsp.write_text('\n'.join(opts),encoding='utf-8')
subprocess.run([str(dotnet),str(sdk/'Roslyn/bincore/csc.dll'),'@'+str(rsp)],check=True)
assembly.with_suffix('.runtimeconfig.json').write_text(json.dumps({'runtimeOptions':{'tfm':'net8.0','framework':{'name':'Microsoft.NETCore.App','version':'8.0.0'}}}))
subprocess.run([str(dotnet),str(assembly),str(OUT)],check=True)
assert hashes=={str(p):hashlib.sha256(p.read_bytes()).hexdigest() for p in sources},'Inputs changed'
(OUT/'cost-provenance.json').write_text(json.dumps({'utc':datetime.datetime.now(datetime.timezone.utc).isoformat(),'source_sha256':hashes,'driver_sha256':hashlib.sha256(Path(__file__).read_bytes()).hexdigest()},indent=2),encoding='utf-8')
