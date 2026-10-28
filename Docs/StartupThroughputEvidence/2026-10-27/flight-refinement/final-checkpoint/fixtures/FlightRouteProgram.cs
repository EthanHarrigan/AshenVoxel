using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AshenVoxel.WorldScale;
using AshenVoxel.WorldScaleRuntime;
using UnityEngine;

// A bounded topology fixture, not a native travel or planner performance test.
static class Program
{
    sealed class Field:IWorldFieldBounds,IWorldFieldSnapshotProvider
    {public string SourceIdentity=>"flight-route-flat10";public SurfaceSample Sample(double x,double z)=>new SurfaceSample(10,6,6,true);public HeightBounds QueryBounds(double a,double b,double c,double d)=>new HeightBounds(10,10);public IWorldField CaptureSnapshot()=>this;}
    sealed class Publication
    {public object Surface;public bool Contact;public int Quads,Tick;public long Bytes;public string Phase;}
    const BindingFlags F=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
    static readonly List<object> Results=new List<object>();
    static readonly List<Publication> Published=new List<Publication>();
    static readonly HashSet<object> Seen=new HashSet<object>();
    static readonly List<Task> Workers=new List<Task>();
    static readonly Stopwatch Whole=Stopwatch.StartNew();
    static object Get(object o,string n)=>o.GetType().GetField(n,F).GetValue(o);
    static void Set(object o,string n,object v)=>o.GetType().GetField(n,F).SetValue(o,v);
    static object Call(object o,string n,params object[] a)=>o.GetType().GetMethod(n,F).Invoke(o,a);
    static IDictionary Surfaces(WorldExplorer w,bool contact=false)=>(IDictionary)Get(w,contact?"contactSurfaces":"surfaces");
    static int frame,checks,maxFar,maxContact,maxWorkers,maxUploads,travelDeferredTicks;
    static long maxFarBytes,maxContactBytes,maxScratch;
    static long planX=long.MinValue,planZ=long.MinValue;
    static string phase="setup";
    static bool Flight=>typeof(WorldExplorer).GetProperty("VoxelTravelDetailDeferred")!=null;
    static bool Deferred(WorldExplorer w)=>Flight&&(bool)typeof(WorldExplorer).GetProperty("VoxelTravelDetailDeferred").GetValue(w);
    static void Require(bool condition,string name){if(!condition)throw new Exception(name+" at "+phase+" tick "+frame);}
    static void Check(bool condition,string name){Require(condition,name);checks++;Console.WriteLine("PASS "+name);}
    static void Plan(WorldExplorer w,bool force=false)
    {
        const double size=819.2;var p=w.GlobalCamera;long cx=(long)Math.Floor(p.X/size),cz=(long)Math.Floor(p.Z/size);
        if(!force&&cx==planX&&cz==planZ&&!(bool)Get(w,"planForced")&&!(bool)Call(w,"ViewTurned"))return;
        planX=cx;planZ=cz;var tiles=new List<LodTile>();
        for(long z=cz-1;z<=cz+1;z++)for(long x=cx-1;x<=cx+1;x++)tiles.Add(new LodTile(new TileKey(x,z,5),x*size,z*size,size,10,10));
        Set(w,"plannedForward",w.ViewCamera.transform.forward);Set(w,"planForced",false);
        Call(w,"ApplyPlan",new LodPlan{Tiles=tiles.ToArray(),SourceIdentity="flight-route-flat10"},25.6);
    }
    static void Tick(WorldExplorer w)
    {
        Time.unscaledTime=++frame*.05f;Time.unscaledDeltaTime=.05f;
        if(Flight)Call(w,"UpdateVoxelTravelMotion");Plan(w);
        Call(w,"RefreshContactPlan");Call(w,"QueueNextVoxelRefinement");Call(w,"AdvanceBuilds");Call(w,"ActivateReadyPlan");
        if(Deferred(w))travelDeferredTicks++;
        var active=(HashSet<TileKey>)Get(w,"active");var desired=(HashSet<TileKey>)Get(w,"desired");
        Require(w.ResidentTiles<=18&&desired.Count<=9&&active.Count<=9,"two bounded nine-tile coverage plans");
        Require(active.All(k=>Surfaces(w).Contains(k)),"complete old coverage stays resident");
        Require(Surfaces(w).Keys.Cast<TileKey>().All(k=>active.Contains(k)||desired.Contains(k)),"no third-plan cache growth");
        Require(w.MeshUploadsLastFrame<=1,"one upload per tick");
        Require(w.ContactMeshBytes<=256L*1048576&&w.TerrainDetailMeshBytes<=256L*1048576,"unchanged payload quotas");
        Require(w.VoxelBuildFailures==0,"no geometry failures");
        foreach(bool contact in new[]{false,true})foreach(object surface in Surfaces(w,contact).Values)
        {
            int quads=(int)Get(surface,"Quads");if(contact)Require(quads==256,"10cm contact remains exact");
            if(Seen.Add(surface))Published.Add(new Publication{Surface=surface,Contact=contact,Quads=quads,Tick=frame,Bytes=(long)(int)Get(surface,"TriangleCount")*60,Phase=phase});
        }
        Workers.RemoveAll(t=>t.IsCompleted);var task=Get(w,"voxelBuildJob") as Task;
        if(task!=null&&!task.IsCompleted&&!Workers.Contains(task))Workers.Add(task);
        maxWorkers=Math.Max(maxWorkers,Workers.Count);Require(Workers.Count<=1,"single geometry worker through cancellation");
        maxFar=Math.Max(maxFar,w.ResidentTiles);maxContact=Math.Max(maxContact,w.ContactTiles);maxUploads=Math.Max(maxUploads,w.MeshUploadsLastFrame);
        maxFarBytes=Math.Max(maxFarBytes,w.TerrainResidentMeshBytes);maxContactBytes=Math.Max(maxContactBytes,w.ContactMeshBytes);maxScratch=Math.Max(maxScratch,w.VoxelScratchBytes);
        Require(Whole.Elapsed.TotalSeconds<150,"bounded suite deadline");Thread.Sleep(2);
    }
    static void Settle(WorldExplorer w,string label)
    {
        phase=label;int first=frame,ready=-1,quality=-1,release=-1,clean=0;var clock=Stopwatch.StartNew();
        int firstPub=Published.Count;
        while(clock.Elapsed.TotalSeconds<20)
        {
            Tick(w);
            if(release<0&&!Deferred(w))release=frame-first;
            if(ready<0&&w.IsTerrainReadyArea(w.GlobalCamera.X,w.GlobalCamera.Z,2))ready=frame-first;
            if(quality<0&&Published.Skip(firstPub).Any(s=>!s.Contact&&s.Quads>32))quality=frame-first;
            if(w.PendingTiles==0&&w.HasActiveCoverage&&!Deferred(w)){if(++clean==4)break;}else clean=0;
        }
        Require(clean==4,"destination queue convergence pending="+w.PendingTiles);
        Require(ready>=0,"arrival terrain support");
        Results.Add(new{phase=label,recovery_virtual_ticks=frame-first,deferral_release_tick=release,terrain_ready_tick=ready,first_new_fine_detail_tick=quality,
            cpu_wall_ms=clock.Elapsed.TotalMilliseconds,far=w.ResidentTiles,contacts=w.ContactTiles,far_bytes=w.TerrainResidentMeshBytes,contact_bytes=w.ContactMeshBytes});
        Console.WriteLine("STOP "+label+": "+(frame-first)+" ticks / "+clock.Elapsed.TotalMilliseconds.ToString("F1")+" ms CPU wall");
    }
    static void Leg(WorldExplorer w,double destination,double y,double speed,string label,bool settle=true)
    {
        phase=label;var p=w.GlobalCamera;double dx=destination-p.X;int ticks=(int)Math.Ceiling(Math.Abs(dx)/(speed*.05));
        int first=Published.Count,start=frame,held=travelDeferredTicks;var clock=Stopwatch.StartNew();
        for(int i=1;i<=ticks;i++)
        {w.SetObserverPose(new WorldPoint(p.X+dx*i/ticks,y,p.Z),Quaternion.Euler(20,dx>=0?90:270,0));Tick(w);Require(w.HasActiveCoverage,"coverage survives fast travel");}
        var travel=Published.Skip(first).Where(v=>v.Phase==label).ToArray();
        if(settle)Settle(w,label+" stop");
        var resident=new HashSet<object>(Surfaces(w).Values.Cast<object>().Concat(Surfaces(w,true).Values.Cast<object>()));
        var expensive=travel.Where(v=>v.Contact||v.Quads>32).ToArray();var retired=expensive.Where(v=>!resident.Contains(v.Surface)).ToArray();
        Results.Add(new{phase=label,travel_virtual_ticks=ticks,including_recovery_ticks=frame-start,held_virtual_ticks=travelDeferredTicks-held,
            cpu_wall_ms_including_recovery=clock.Elapsed.TotalMilliseconds,publications=travel.Length,contacts=travel.Count(v=>v.Contact),
            far_by_columns=travel.Where(v=>!v.Contact).GroupBy(v=>v.Quads).ToDictionary(g=>g.Key.ToString(),g=>g.Count()),
            expensive_publications=expensive.Length,expensive_retired_by_end=retired.Length,retired_mesh_payload_bytes=retired.Sum(v=>v.Bytes),
            note=settle?"Retired by stationary destination convergence":"Retired by immediate reversal point; later loss not included"});
        Console.WriteLine("LEG "+label+": expensive "+expensive.Length+", retired "+retired.Length);
    }
    static void Main(string[] args)
    {
        string error=null;GameObject root=null;WorldExplorer w=null;
        try
        {
            root=new GameObject("bounded flight route");w=root.AddComponent<WorldExplorer>();w.UseVoxelLodTerrain=true;w.AsyncPlanning=false;
            w.MaximumTiles=64;w.MeshBuildsPerFrame=1;w.MeshBuildMilliseconds=2;w.MeshWorkUnitsPerFrame=256;w.VoxelFullDetailRadius=16;w.ShowVisualWater=false;
            w.ConfigureInitialVoxelMeshBudgets(256,256);w.ConfigureExternal(new Field(),new SurfaceProfile(enableSnow:false),new WorldPoint(12,110,12),Quaternion.Euler(20,90,0));
            Call(w,"ClearTerrain");Set(w,"coarseStartup",true);Plan(w,true);Settle(w,"initial elevated");
            Leg(w,4108,110,640,"fast outbound");Check(!Deferred(w)&&w.PendingTiles==0,"same-position stop restores complete destination detail");
            w.SetObserverPose(w.GlobalCamera,Quaternion.Euler(20,270,0));Settle(w,"stopped180turn");Check(w.PendingTiles==0,"stopped turn refines its new view");
            Leg(w,12,110,640,"fast reverse return",false);Leg(w,2060,110,640,"immediate direction reversal");
            Check(!Deferred(w)&&w.PendingTiles==0,"reversal destination recovers");
            Leg(w,4108,110,640,"pre-descent transit",false);phase="descent";
            for(int i=1;i<=10;i++){w.SetObserverPose(new WorldPoint(4108,110-(110-11.65)*i/10,12),Quaternion.Euler(45,90,0));Tick(w);if(Flight)Require(!Deferred(w),"descent immediately restores scheduling");}
            Settle(w,"ground descent arrival");Check(w.IsTerrainReadyArea(4108,12,2),"ground contact ready after descent");
            int held=travelDeferredTicks;Leg(w,4620,11.65,80,"ordinary ground travel");Check(held==travelDeferredTicks,"ordinary ground route never enters travel deferral");
            int pubs=Published.Count;for(int i=0;i<12;i++)Tick(w);Check(Published.Count==pubs&&w.PendingTiles==0,"stationary destination does not rebuild");
            UnityEngine.Object.Destroy(root);root=null;Check(w.ResidentTiles==0&&w.ContactTiles==0&&Get(w,"voxelBuildJob")==null&&w.VoxelScratchBytes==0,"shutdown releases caches and worker");
        }
        catch(Exception e){error=e.ToString();Console.WriteLine(error);}
        finally{if(root!=null)UnityEngine.Object.Destroy(root);}
        File.WriteAllText(args[0],JsonSerializer.Serialize(new{status=error==null?"PASS":"FAIL",error,checks,candidate=Flight,frames=frame,wall_ms=Whole.Elapsed.TotalMilliseconds,
            fixture=new{bounded_plan_tiles=9,level=5,plan_source="fixed conservative flat enclosures; real ApplyPlan and queue/geometry paths",virtual_tick_seconds=.05,wall_yield_ms=2,quota_detail_mib=256,quota_contact_mib=256},
            maxFar,maxContact,maxWorkers,maxUploads,maxFarBytes,maxContactBytes,maxScratch,travelDeferredTicks,results=Results,
            scope="Small .NET/shim topology-route experiment. Real builds/publications/cache retirement, synthetic flat source. Virtual tick recovery and CPU wall time are reported separately; neither is native input latency, FPS or world-generation throughput. Retired published detail is a waste proxy, not all canceled CPU work."},new JsonSerializerOptions{WriteIndented=true}));
        if(error!=null)Environment.ExitCode=1;
    }
}
