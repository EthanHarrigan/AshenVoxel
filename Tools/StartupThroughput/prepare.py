"""Freeze the startup revision and compile bounded, renderer-free experiments."""
from pathlib import Path
import argparse,datetime,hashlib,json,shutil,subprocess
ROOT=Path(__file__).resolve().parents[2];HERE=Path(__file__).resolve().parent
CACHE=ROOT/'.terrain-cache/startup-throughput';FROZEN=CACHE/'frozen'
def once(s,a,b):
    assert s.count(a)==1,(a,s.count(a));return s.replace(a,b)
def freeze():
    if FROZEN.exists():return
    files=[]
    for name in ['Core','WorldScaleData','WorldScaleRuntime','Exploration']:
        files+=list((ROOT/'Assets/AshenVoxel'/name).rglob('*.cs'))
    files+=[ROOT/'Tools/ExplorationSmokeChecks/UnityShim.cs',ROOT/'Assets/AshenVoxel/Settings/ExplorationSettings.asset']
    files+=list((ROOT/'Assets/AshenVoxel/Exploration/Samples').glob('*.bytes'))
    files+=[ROOT/'Assets/AshenVoxel/Exploration/Samples/exploration-world.json',ROOT/'Assets/AshenVoxel/Exploration/Prototypes/ExplorationPrototypes.bytes']
    for p in files:
        target=FROZEN/p.relative_to(ROOT);target.parent.mkdir(parents=True,exist_ok=True);shutil.copy2(p,target)
    (CACHE/'snapshot.json').write_text(json.dumps({'utc':datetime.datetime.now(datetime.timezone.utc).isoformat(),'sha256':{p.relative_to(ROOT).as_posix():hashlib.sha256(p.read_bytes()).hexdigest() for p in files}},indent=2))
def main():
    p=argparse.ArgumentParser();p.add_argument('--variant',default='baseline');p.add_argument('--task',default='population');p.add_argument('--run',action='store_true');p.add_argument('--triangle-budget',type=int);a=p.parse_args();freeze()
    if a.triangle_budget is not None and not 1000000<=a.triangle_budget<=64000000:p.error('triangle budget must be within the existing supported1M–64M range')
    out=CACHE/(a.variant+('-budget-'+str(a.triangle_budget) if a.triangle_budget is not None else ''));out.mkdir(exist_ok=True)
    s=(FROZEN/'Assets/AshenVoxel/Exploration/VegetationStreaming.cs').read_text()
    # Instrument only private benchmark copies.
    s=once(s,'        void BeginMatrices()\n        {','        void BeginMatrices()\n        {\n            StartupProbe.MatrixRebuilds++;')
    s=once(s,'PopulationCell data=population.Build(key);builds++;','PopulationCell data=population.Build(key);builds++;StartupProbe.CellsBuilt++;')
    s=once(s,'PopulationInstance instance=cell.Instances[matrixInstance++];visited++;','PopulationInstance instance=cell.Instances[matrixInstance++];visited++;StartupProbe.MatrixVisits++;')
    if a.variant in ['load-batch','combined','coverage','compact']:
        s=once(s,'try{if(!loading.MoveNext())loading=null;}','try{int steps=0;do{if(!loading.MoveNext()){loading=null;break;}}while(++steps<8&&Ms(Now-started)<Math.Max(.25,WorkMilliseconds));}')
    if a.variant in ['publish-batch','combined','coverage','compact']:
        # Publish existing snapshots while allowing generation of new cells.
        s=once(s,'while(!buildingMatrices&&queue.Count>0&&builds<Math.Max(1,CellsPerFrame)&&Ms(Now-started)<Math.Max(.25,WorkMilliseconds))','while(queue.Count>0&&builds<Math.Max(1,CellsPerFrame)&&Ms(Now-started)<Math.Max(.25,WorkMilliseconds)*.5)')
        s=once(s,'if(!buildingMatrices&&(matricesDirty||matrixTravel>16))BeginMatrices();','if(!buildingMatrices&&(matricesDirty||matrixTravel>16))BeginMatrices();')
    if a.variant in ['coverage','compact']:
        s=once(s,'int lod=Lod(instance,model,distance),original=lod;',
            'int lod=Lod(instance,model,distance),original=lod;\n                long totalLimit=Math.Max(100000,TriangleBudget),reserve=impostorMaterial==null?0:Math.Min(totalLimit/4,ResidentInstances*2L);\n                long Limit(int level)=>totalLimit-(model.Data.Kind==0&&impostorMaterial!=null&&level>=3?0:reserve);')
        s=once(s,'buildTriangles+model.Triangles[lod]>Math.Max(100000,TriangleBudget))lod++;','buildTriangles+model.Triangles[lod]>Limit(lod))lod++;')
        s=once(s,'buildTriangles+model.Triangles[lod]>Math.Max(100000,TriangleBudget)&&distance>80','buildTriangles+model.Triangles[lod]>Limit(lod)&&distance>80')
    if a.variant=='compact':
        # Reserve the actual lowest-LOD cost of all captured entries, including
        # nearby hard props. Out-of-range entries release their reservation too.
        s=once(s,'bool buildingMatrices,matricesDirty;long buildTriangles;','bool buildingMatrices,matricesDirty;long treeCoverageReserve,buildTriangles;')
        s=once(s,'            matrixCells.Sort((a,b)=>Distance(a.Data.Key).CompareTo(Distance(b.Data.Key)));',
            '            matrixCells.Sort((a,b)=>Distance(a.Data.Key).CompareTo(Distance(b.Data.Key)));\n            treeCoverageReserve=0;\n            if(impostorMaterial!=null)foreach(var cell in matrixCells)foreach(var instance in cell.Data.Instances)\n                if(models.TryGetValue(instance.PrototypeId,out Model model))treeCoverageReserve+=model.Triangles[model.Meshes.Length-1];')
        s=once(s,'                double distance=InstanceDistance(instance,model.Data);',
            '                if(impostorMaterial!=null)treeCoverageReserve-=model.Triangles[model.Meshes.Length-1];\n                double distance=InstanceDistance(instance,model.Data);')
        s=once(s,'long totalLimit=Math.Max(100000,TriangleBudget),reserve=impostorMaterial==null?0:Math.Min(totalLimit/4,ResidentInstances*2L);\n                long Limit(int level)=>totalLimit-(model.Data.Kind==0&&impostorMaterial!=null&&level>=3?0:reserve);',
            'long available=Math.Max(0,Math.Max(100000,TriangleBudget)-treeCoverageReserve);')
        s=s.replace('>Limit(lod)', '>available')
        s=s.replace('buildingMatrices=false;finalizing=false;','buildingMatrices=false;treeCoverageReserve=0;finalizing=false;')
        s=once(s,'lastPlanAt=0;buildingMatrices=false;', 'lastPlanAt=0;buildingMatrices=false;treeCoverageReserve=0;')
        s=s.replace('treeCoverageReserve','coverageReserve')
        s=once(s,'public readonly Matrix4x4[] Matrices=new Matrix4x4[1023];','public Matrix4x4[] Matrices=Array.Empty<Matrix4x4>();')
        s=once(s,'public long MatrixBufferBytes=>(batches.Count+nextBatches.Count+batchPool.Count)*1023L*64;','public long MatrixBufferBytes {get{long bytes=0;foreach(var b in batches)bytes+=b.Matrices.LongLength*64;foreach(var b in nextBatches)bytes+=b.Matrices.LongLength*64;foreach(var b in batchPool)bytes+=b.Matrices.LongLength*64;return bytes;}}')
        s=once(s,'page.Matrices.CopyTo(finalizeOffset,batch.Matrices,0,count);','if(batch.Matrices.Length<count){int capacity=16;while(capacity<count)capacity*=2;batch.Matrices=new Matrix4x4[Math.Min(1023,capacity)];}page.Matrices.CopyTo(finalizeOffset,batch.Matrices,0,count);')
        s=once(s,'            int builds=0;','            bool advancedMatrices=buildingMatrices;\n            if(advancedMatrices)BuildMatrices(started,Math.Max(.25,WorkMilliseconds)*.5);\n            int builds=0;')
        s=once(s,'Ms(Now-started)<Math.Max(.25,WorkMilliseconds)*.5)','Ms(Now-started)<Math.Max(.25,WorkMilliseconds))')
        s=once(s,'if(buildingMatrices)BuildMatrices(started);','if(buildingMatrices&&!advancedMatrices)BuildMatrices(started,Math.Max(.25,WorkMilliseconds));')
        s=once(s,'void BuildMatrices(long frameStart)','void BuildMatrices(long frameStart,double milliseconds)')
        s=s.replace('Ms(Now-frameStart)<Math.Max(.25,WorkMilliseconds)','Ms(Now-frameStart)<milliseconds')
    (out/'VegetationStreaming.cs').write_text(s)
    sources=list((FROZEN/'Assets/AshenVoxel/Core').rglob('*.cs'))+list((FROZEN/'Assets/AshenVoxel/WorldScaleData').glob('*.cs'))
    sources += [FROZEN/'Assets/AshenVoxel/WorldScaleRuntime'/n for n in ['WorldExplorer.cs','WorldExplorerIntegration.cs','WorldExplorerVoxels.cs','RemoteTerrainField.cs','VisualWater.cs']]
    sources += [p for p in (FROZEN/'Assets/AshenVoxel/Exploration').glob('*.cs') if p.name!='VegetationStreaming.cs']
    shim=(FROZEN/'Tools/ExplorationSmokeChecks/UnityShim.cs').read_text()
    shim=once(shim,'public static T Load<T>(string path)where T:class=>Materials.TryGetValue(path,out Material m)?m as T:null;',
        'public static T Load<T>(string path)where T:class {if(path=="ForestAtlasVersion")return new TextAsset{bytes=new byte[]{51}} as T;if(path=="ForestImpostor")return new Material{name="Forest impostor"} as T;return Materials.TryGetValue(path,out Material m)?m as T:null;}')
    (out/'UnityShim.cs').write_text(shim)
    probe=(HERE/'StartupProbe.cs').read_text()
    if a.triangle_budget is not None:probe=once(probe,'v.TreeDistance=1400;',f'v.TriangleBudget={a.triangle_budget};v.TreeDistance=1400;')
    (out/'StartupProbe.cs').write_text(probe)
    sources += [out/'VegetationStreaming.cs',out/'UnityShim.cs',out/'StartupProbe.cs',HERE/'LodMeasurements.cs']
    dotnet=Path(shutil.which('dotnet')).resolve();ver=lambda p:tuple(map(int,p.name.split('.')))
    sdk=sorted((dotnet.parent/'sdk').glob('8.*'),key=ver)[-1];refs=sorted((dotnet.parent/'packs/Microsoft.NETCore.App.Ref').glob('8.*'),key=ver)[-1]/'ref/net8.0'
    dll=out/'StartupProbe.dll';q=lambda p:'"'+str(p)+'"';rsp=out/'compile.rsp';rsp.write_text('\n'.join(['-nologo','-langversion:9.0','-optimize+','-target:exe','-out:'+q(dll)]+['-r:'+q(p) for p in refs.glob('*.dll')]+[q(p) for p in sources]))
    subprocess.run([str(dotnet),str(sdk/'Roslyn/bincore/csc.dll'),'@'+str(rsp)],check=True)
    dll.with_suffix('.runtimeconfig.json').write_text(json.dumps({'runtimeOptions':{'tfm':'net8.0','framework':{'name':'Microsoft.NETCore.App','version':'8.0.0'}}}))
    if a.run:
        output=out/(a.task+'-'+datetime.datetime.now().strftime('%Y%m%d-%H%M%S')+'.json')
        hashes={str(p.relative_to(ROOT)):hashlib.sha256(p.read_bytes()).hexdigest() for p in sources}
        subprocess.run([str(dotnet),str(dll),str(FROZEN),str(output),a.task],check=True)
        data=json.loads(output.read_text());data.update(source_sha256=hashes,triangle_budget=a.triangle_budget or 32000000,generated_utc=datetime.datetime.now(datetime.timezone.utc).isoformat());output.write_text(json.dumps(data,indent=2)+'\n');print(output)
if __name__=='__main__':main()
