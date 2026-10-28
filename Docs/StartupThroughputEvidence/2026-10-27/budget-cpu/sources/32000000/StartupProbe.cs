using System;using System.Collections;using System.Collections.Generic;using System.Diagnostics;using System.IO;using System.Linq;using System.Reflection;using System.Text.Json;using System.Threading;
using AshenVoxel.WorldScale;using AshenVoxel.WorldScaleRuntime;using AshenVoxel.Exploration;using UnityEngine;
public static class StartupProbe
{
    public static int MatrixRebuilds,CellsBuilt,MatrixVisits;
    const BindingFlags Flags=BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public;
    static object Get(object x,string n)=>x.GetType().GetField(n,Flags).GetValue(x);
    static object Call(object x,string n,params object[] a)=>x.GetType().GetMethod(n,Flags).Invoke(x,a);
    static void Save(string p,object d)=>File.WriteAllText(p,JsonSerializer.Serialize(d,new JsonSerializerOptions{WriteIndented=true,IncludeFields=true}));
    sealed class Forest:IWorldField,IClimateWorldField
    {public string SourceIdentity=>"controlled-startup-forest";public SurfaceSample Sample(double x,double z)=>new SurfaceSample(120,6,6,true);public bool TrySampleClimate(double x,double z,out SurfaceClimate c){c=new SurfaceClimate(15,3,1300,30,-.0065);return true;}}
    static void Main(string[] args)
    {
        if(args[2]=="lod")
        {
            var sampler=LodMeasurements.Load(args[0]);var eye=LodMeasurements.Eye(sampler);var sw=Stopwatch.StartNew();var plan=LodMeasurements.Plan(sampler,eye);double planMs=sw.Elapsed.TotalMilliseconds;
            var rows=new List<LodMeasurements.Row>();var failures=new List<string>();var cases=LodMeasurements.Cases(plan,eye);
            foreach(var c in cases)for(int r=0;r<3;r++)try{rows.Add(LodMeasurements.Measure(sampler,eye,c,r));}catch(VoxelLodCapacityException e){failures.Add(c.Item1.Key+"/"+c.Item2+":"+e.Message);break;}
            Save(args[1],new{scope="Windows.NET8 single-tile builder rates, no upload/render FPS",plan_ms=planMs,plan.PendingSplits,plan=LodMeasurements.Describe(plan,eye),rows,failures});return;
        }
        if(args[2]=="terrain"){Terrain(args[0],args[1]);return;}
        Population(args[0],args[1],args[2]=="population-real");
    }
    static void Population(string root,string output,bool actual)
    {
        var sampler=actual?LodMeasurements.Load(root):new SurfaceMaterialSampler(new Forest(),new SurfaceProfile(enableSnow:false));var eye=actual?LodMeasurements.Eye(sampler):new WorldPoint(31,121.65,31);
        var game=new GameObject("Bounded population probe");var v=game.AddComponent<VegetationStreaming>();
        v.TriangleBudget=32000000;v.TreeDistance=1400;v.GroundCoverDistance=64;v.RockDistance=220;v.TreeDensity=1;v.PlantDensity=1;v.MaxInstances=40000;v.EnableForestHorizon=false;
        v.Initialize(sampler.BaseField,sampler.Profile,new TextAsset{bytes=File.ReadAllBytes(Path.Combine(root,"Assets/AshenVoxel/Exploration/Prototypes/ExplorationPrototypes.bytes"))},new Material(),714103);
        var camera=new Camera();int frame=0,modelsFrame=0,firstVisibleFrame=0,nearReadyFrame=0;var sw=Stopwatch.StartNew();double modelsMs=0,nearMs=0;var rows=new List<object>();var cpu=new List<double>();
        for(;frame<60000&&sw.Elapsed.TotalSeconds<40;frame++)
        {
            Time.unscaledTime=frame/60f;v.SetObserver(eye,new WorldPoint(eye.X,0,eye.Z));v.Tick(camera);cpu.Add(v.LastWorkMilliseconds);
            if(v.Ready&&modelsFrame==0){modelsFrame=frame+1;modelsMs=sw.Elapsed.TotalMilliseconds;}
            if(v.VisibleInstances>0&&firstVisibleFrame==0)firstVisibleFrame=frame+1;
            if(v.IsReady(eye.X,eye.Z,16)&&nearReadyFrame==0){nearReadyFrame=frame+1;nearMs=sw.Elapsed.TotalMilliseconds;}
            if(frame%300==0)rows.Add(new{frame,ms=sw.Elapsed.TotalMilliseconds,v.Ready,v.ResidentCells,v.ResidentInstances,v.PendingCells,v.VisibleInstances,CellsBuilt,MatrixVisits,MatrixRebuilds});
            if(CellsBuilt>0&&v.Ready&&v.PendingCells==0&&!(bool)Get(v,"buildingMatrices"))break;
        }
        var cells=(IDictionary)Get(v,"cells");var desired=(HashSet<PopulationKey>)Get(v,"desired");var deferred=(HashSet<PopulationKey>)Get(v,"deferred");
        var rings=new List<object>();foreach(int radius in new[]{128,256,512,768,1024,1400})
        {int want=0,loaded=0,published=0,empty=0;foreach(var k in desired.Where(k=>k.Layer==0)){double dx=Math.Max(0,Math.Max(k.X*64-eye.X,eye.X-(k.X+1)*64)),dz=Math.Max(0,Math.Max(k.Z*64-eye.Z,eye.Z-(k.Z+1)*64));if(dx*dx+dz*dz>radius*radius)continue;want++;if(!cells.Contains(k))continue;loaded++;var cell=cells[k];if((bool)Get(cell,"Published"))published++;if(((PopulationCell)Get(cell,"Data")).Instances.Length==0)empty++;}rings.Add(new{radius,want,loaded,published,empty});}
        var batches=((IList)Get(v,"batches")).Cast<object>().ToArray();
        var draws=batches.GroupBy(b=>(int)Get(b,"Lod")).Select(g=>new{lod=g.Key,batches=g.Count(),instances=g.Sum(b=>(int)Get(b,"Count")),triangles=g.Sum(b=>(long)(int)Get(b,"Count")*(int)Get(b,"TriangleCount"))}).ToArray();
        var kindDraws=batches.GroupBy(b=>new{kind=(int)Get(b,"ModelId")<100?0:(int)Get(b,"ModelId")<200?1:2,lod=(int)Get(b,"Lod")}).Select(g=>new{g.Key.kind,g.Key.lod,instances=g.Sum(b=>(int)Get(b,"Count")),triangles=g.Sum(b=>(long)(int)Get(b,"Count")*(int)Get(b,"TriangleCount"))}).ToArray();
        var placements=cells.Values.Cast<object>().SelectMany(c=>((PopulationCell)Get(c,"Data")).Instances).OrderBy(i=>i.PrototypeId).ThenBy(i=>i.StableKey).ThenBy(i=>i.Position.X).ThenBy(i=>i.Position.Z).ToArray();string placementHash;
        using(var stream=new MemoryStream())using(var writer=new BinaryWriter(stream)){foreach(var i in placements){writer.Write(i.PrototypeId);writer.Write(i.StableKey);writer.Write(i.Position.X);writer.Write(i.Position.Y);writer.Write(i.Position.Z);writer.Write(i.YawDegrees);writer.Write(i.Scale);}writer.Flush();using(var sha=System.Security.Cryptography.SHA256.Create())placementHash=Convert.ToHexString(sha.ComputeHash(stream.ToArray())).ToLowerInvariant();}
        int trees=cells.Values.Cast<object>().Sum(c=>((PopulationCell)Get(c,"Data")).Instances.Count(i=>i.PrototypeId<100));
        int drawnTrees=batches.Where(b=>(int)Get(b,"ModelId")<100).Sum(b=>(int)Get(b,"Count"));
        double seconds=sw.Elapsed.TotalSeconds;var ordered=cpu.OrderBy(x=>x).ToArray();
        Save(output,new{scope="Actual population code/pack with numerical renderer substitutes. Frames are scheduling iterations, not native FPS; 60Hz floors are arithmetic.",actual_source=actual,frames=frame+1,seconds,models_frame=modelsFrame,models_ms=modelsMs,models_frame_floor_60hz=modelsFrame/60.0,first_visible_frame=firstVisibleFrame,near_ready_frame=nearReadyFrame,near_ready_ms=nearMs,v.ResidentCells,v.ResidentInstances,v.PendingCells,v.VisibleInstances,v.VisibleTriangles,v.BudgetDemotions,v.MeshBytes,v.MatrixBufferBytes,trees,drawnTrees,draws,kind_draws=kindDraws,placement_sha256=placementHash,deferred_cells=deferred.Count,CellsBuilt,MatrixVisits,MatrixRebuilds,cells_per_cpu_second=CellsBuilt/seconds,p95_tick_ms=ordered[(int)(ordered.Length*.95)],max_tick_ms=ordered.Last(),rings,rows});v.DisposeRuntime();
        Console.WriteLine($"Population frames={frame+1}, models={modelsFrame}, near={nearReadyFrame}, built={CellsBuilt}, rebuilds={MatrixRebuilds}, visits={MatrixVisits}, seconds={seconds:F2}");
    }
    static void Terrain(string root,string output)
    {
        var sampler=LodMeasurements.Load(root);var eye=LodMeasurements.Eye(sampler);var game=new GameObject("Bounded terrain startup probe");var w=game.AddComponent<WorldExplorer>();
        w.UseVoxelLodTerrain=true;w.MaximumTiles=64;w.ViewDistanceKm=4;w.VoxelFullDetailRadius=16;w.VoxelContactBudgetMB=128;
        w.ConfigureExternal(sampler.BaseField,sampler.Profile,eye,Quaternion.Euler(8,20,0));var rows=new List<object>();var sw=Stopwatch.StartNew();int frame=0,firstCoverage=0,firstContact=0;double firstMs=0,contactMs=0;
        var seen=new HashSet<object>();var built=new List<object>();
        while(frame<3600&&sw.Elapsed.TotalSeconds<65)
        {
            Time.unscaledTime=frame/60f;Time.unscaledDeltaTime=1f/60;Call(w,"Update");
            foreach(string set in new[]{"surfaces","contactSurfaces"})foreach(object s in ((IDictionary)Get(w,set)).Values)if(seen.Add(s))
            {var t=(LodTile)Get(s,"Tile");int n=(int)Get(s,"Quads");built.Add(new{frame,ms=sw.Elapsed.TotalMilliseconds,level=t.Key.Level,n,contact=set=="contactSurfaces",tile_m=t.SizeMeters,cell_m=t.SizeMeters/n,visible=((GameObject)Get(s,"Object")).activeSelf});}
            if(w.HasActiveCoverage&&firstCoverage==0){firstCoverage=frame;firstMs=sw.Elapsed.TotalMilliseconds;}
            if(w.ContactTiles>0&&firstContact==0){firstContact=frame;contactMs=sw.Elapsed.TotalMilliseconds;}
            if(frame%120==0)rows.Add(new{frame,ms=sw.Elapsed.TotalMilliseconds,w.ResidentTiles,w.PendingTiles,w.ContactTiles,w.ContactPendingTiles,w.LastVoxelWorkerMilliseconds,w.LastMeshBuildMilliseconds,w.HasActiveCoverage});
            frame++;Thread.Sleep(1);if(frame>100&&w.PendingTiles==0&&!w.PlanningInProgress)break;
        }
        Save(output,new{scope="64-tile/4km bounded actual-source streamer, numerical upload/render substitutes; 1ms worker pacing, not native frames per second",frames=frame,seconds=sw.Elapsed.TotalSeconds,first_coverage_frame=firstCoverage,first_coverage_ms=firstMs,first_contact_frame=firstContact,first_contact_ms=contactMs,built,rows});UnityEngine.Object.Destroy(game);
    }
}
