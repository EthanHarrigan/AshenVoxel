"""Freeze sources/data and instrument a private copy; never edit gameplay to profile it."""
from pathlib import Path
import argparse,datetime,hashlib,json,re,shutil,subprocess
ROOT=Path(__file__).resolve().parents[2]
HERE=Path(__file__).resolve().parent
CACHE=ROOT/'.terrain-cache/terrain-performance'
def replace_once(s,a,b):
    assert s.count(a)==1,(a,s.count(a))
    return s.replace(a,b)
def main():
    p=argparse.ArgumentParser();p.add_argument('--experiment',action='store_true');p.add_argument('--unbox',action='store_true');p.add_argument('--run',action='store_true');args=p.parse_args()
    frozen=CACHE/'frozen'
    if not frozen.exists():
        paths=list((ROOT/'Assets/AshenVoxel/Core').rglob('*.cs'))+list((ROOT/'Assets/AshenVoxel/WorldScaleData').glob('*.cs'))
        paths+=list((ROOT/'Assets/AshenVoxel/Exploration/Samples').glob('*.bytes'))
        paths += [ROOT/f for f in ['Assets/AshenVoxel/Settings/ExplorationSettings.asset','Assets/AshenVoxel/Exploration/Samples/exploration-world.json','Assets/AshenVoxel/WorldScaleRuntime/WorldExplorerVoxels.cs','Assets/AshenVoxel/WorldScaleRuntime/GroundSurface.shader','Packages/manifest.json','ProjectSettings/ProjectVersion.txt']]
        hashes={}
        for f in paths:
            dest=frozen/f.relative_to(ROOT);dest.parent.mkdir(parents=True,exist_ok=True);shutil.copy2(f,dest);hashes[f.relative_to(ROOT).as_posix()]=hashlib.sha256(f.read_bytes()).hexdigest()
        settings=(frozen/'Assets/AshenVoxel/Settings/ExplorationSettings.asset').read_text()
        for name,value in {'Seed':'714103','EnableSurfaceRelief':'1','VerticalScale':'1','FineBlendMeters':'512','EnableSnow':'1','MaximumSnowDepth':'2.4','WinterFraction':'0.2','TemperatureBias':'0','SeaLevel':'0'}.items():
            m=re.search(r'^  '+name+r': (.+)$',settings,re.M);assert m and float(m[1])==float(value),(name,m)
        meta=json.loads((frozen/'Assets/AshenVoxel/Exploration/Samples/exploration-world.json').read_text())
        assert meta['recommended_spawn']['x_m']==-19065 and meta['recommended_spawn']['z_m']==-10200
        (CACHE/'snapshot.json').write_text(json.dumps({'utc':datetime.datetime.now(datetime.timezone.utc).isoformat(),'source_sha256':hashes,'world_id':meta['world_id']},indent=2))
    variant=('parallel' if args.experiment else 'baseline')+('-unboxed' if args.unbox else '');out=CACHE/variant;out.mkdir(parents=True,exist_ok=True)
    s=(frozen/'Assets/AshenVoxel/WorldScaleData/VoxelLodSurface.cs').read_text()
    s=replace_once(s,'            var columns=new SurfaceColumn[pitch*pitch];','            TerrainProbe.Begin();\n            var columns=new SurfaceColumn[pitch*pitch];')
    s=replace_once(s,'            long anchorCell=tops[At(0,0)]+1;','            TerrainProbe.Mark(0);\n            long anchorCell=tops[At(0,0)]+1;')
    s=replace_once(s,'            long floorCell=VoxelTerrainPage.TopCell(seamFloor)-1;','            TerrainProbe.Mark(1);\n            long floorCell=VoxelTerrainPage.TopCell(seamFloor)-1;')
    s=replace_once(s,'            int unmerged=faces.Count;','            TerrainProbe.Mark(2);\n            int unmerged=faces.Count;')
    s=replace_once(s,'            return new VoxelLodSurface(n,step,anchor,heights,faces,','            TerrainProbe.Mark(3);\n            return new VoxelLodSurface(n,step,anchor,heights,faces,')
    generated_helpers=[]
    for name,unboxed in [('BaselineSurfaceBuilder',False),('UnboxedSurfaceBuilder',True)]:
        body=s[s.index('        public static VoxelLodSurface Build('):s.index('\n\n    }\n\n    /// <summary>Quality')]
        if unboxed:body=replace_once(body,'a.Material.CompareTo(b.Material)','((byte)a.Material).CompareTo((byte)b.Material)')
        helper=out/(name+'.cs');helper.write_text('using System; using System.Collections.Generic; using System.Threading; using AshenVoxel.Core;\nnamespace AshenVoxel.WorldScale { public static class '+name+' { const int Version=1,MaximumQuads=256,MaximumFaces=1048576;\n'+body+'\n}}')
        generated_helpers.append(helper)
    s=replace_once(s,'        VoxelLodSurface(int n,','        public VoxelLodSurface(int n,')
    if args.experiment:
        s=replace_once(s,'            for(int z=-1;z<=n;z++)for(int x=-1;x<=n;x++)','            TerrainProbe.Rows(n,cancel,z=>{for(int x=-1;x<=n;x++)')
        s=replace_once(s,'            TerrainProbe.Mark(0);','            });\n            TerrainProbe.Mark(0);')
    if args.unbox:
        s=replace_once(s,'a.Material.CompareTo(b.Material)','((byte)a.Material).CompareTo((byte)b.Material)')
    generated=out/'VoxelLodSurface.cs';generated.write_text(s)
    dotnet=Path(shutil.which('dotnet')).resolve();version=lambda p:tuple(map(int,p.name.split('.')))
    sdk=sorted((dotnet.parent/'sdk').glob('8.*'),key=version)[-1];refs=sorted((dotnet.parent/'packs/Microsoft.NETCore.App.Ref').glob('8.*'),key=version)[-1]/'ref/net8.0'
    sources=sorted((frozen/'Assets/AshenVoxel/Core').rglob('*.cs'))+[p for p in sorted((frozen/'Assets/AshenVoxel/WorldScaleData').glob('*.cs')) if p.name!='VoxelLodSurface.cs']+[generated,HERE/'Probe.cs',HERE/'CpuBenchmark.cs']+generated_helpers
    assembly=out/'TerrainBenchmark.dll';q=lambda p:'"'+str(p)+'"';rsp=out/'compile.rsp'
    rsp.write_text('\n'.join(['-nologo','-langversion:9.0','-optimize+','-target:exe','-out:'+q(assembly)]+['-r:'+q(p) for p in refs.glob('*.dll')]+[q(p) for p in sources]))
    subprocess.run([str(dotnet),str(sdk/'Roslyn/bincore/csc.dll'),'@'+str(rsp)],check=True)
    assembly.with_suffix('.runtimeconfig.json').write_text(json.dumps({'runtimeOptions':{'tfm':'net8.0','framework':{'name':'Microsoft.NETCore.App','version':'8.0.0'}}}))
    if args.run:
        result=out/('cpu-'+datetime.datetime.now().strftime('%Y%m%d-%H%M%S')+'.json')
        subprocess.run([str(dotnet),str(assembly),str(frozen/'Assets/AshenVoxel/Exploration/Samples'),str(result),'1,0,2,4' if args.experiment else '1'],check=True)
        print('RESULT',result)
if __name__=='__main__':main()
