"""Compile and execute whole-world voxel surface and quality invariants."""
from pathlib import Path
import datetime,hashlib,json,shutil,subprocess
ROOT=Path(__file__).resolve().parents[1]
def version(p):return tuple(map(int,p.name.split('.')))
def main():
    dotnet=Path(shutil.which('dotnet')).resolve();sdk=sorted((dotnet.parent/'sdk').glob('8.*'),key=version)[-1]
    refs=sorted((dotnet.parent/'packs/Microsoft.NETCore.App.Ref').glob('8.*'),key=version)[-1]/'ref/net8.0'
    out=ROOT/'Tools/VoxelTerrainChecks/bin/lod';out.mkdir(parents=True,exist_ok=True)
    sources=sorted((ROOT/'Assets/AshenVoxel/Core').rglob('*.cs'))+sorted((ROOT/'Assets/AshenVoxel/WorldScaleData').glob('*.cs'))+[ROOT/'Tools/VoxelTerrainChecks/LodProgram.cs']
    hashes={p.relative_to(ROOT).as_posix():hashlib.sha256(p.read_bytes()).hexdigest() for p in sources}
    q=lambda p:'"'+str(p)+'"';assembly=out/'VoxelLodChecks.dll'
    rsp=out/'compile.rsp';rsp.write_text('\n'.join(['-nologo','-warnaserror+','-langversion:9.0','-optimize+','-target:exe','-out:'+q(assembly)]+['-r:'+q(p) for p in sorted(refs.glob('*.dll'))]+[q(p) for p in sources]),encoding='utf-8')
    subprocess.run([str(dotnet),str(sdk/'Roslyn/bincore/csc.dll'),'@'+str(rsp)],check=True)
    assembly.with_suffix('.runtimeconfig.json').write_text(json.dumps({'runtimeOptions':{'tfm':'net8.0','framework':{'name':'Microsoft.NETCore.App','version':'8.0.0'}}}))
    report=ROOT/'Docs/WorldVoxelEvidence/data.json';report.parent.mkdir(parents=True,exist_ok=True)
    subprocess.run([str(dotnet),str(assembly),str(report)],check=True)
    assert hashes=={p.relative_to(ROOT).as_posix():hashlib.sha256(p.read_bytes()).hexdigest() for p in sources},'Source changed during checks'
    data=json.loads(report.read_text());data.update(source_sha256=hashes,generated_utc=datetime.datetime.now(datetime.timezone.utc).isoformat());report.write_text(json.dumps(data,indent=2)+'\n')
if __name__=='__main__':main()
