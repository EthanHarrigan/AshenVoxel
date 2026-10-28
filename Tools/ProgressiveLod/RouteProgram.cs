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

// Independent lifecycle regression through Update and the real asynchronous
// planner/geometry worker. Virtual route time is deliberately accelerated:
// these results measure correctness under backlog, never game FPS or travel speed.
static class Program
{
    sealed class Field:IWorldFieldBounds,IWorldFieldSnapshotProvider
    {
        public string SourceIdentity=>"bounded-route-flat10-v1";
        public SurfaceSample Sample(double x,double z)=>new SurfaceSample(10,6,6,true);
        public HeightBounds QueryBounds(double a,double b,double c,double d)=>new HeightBounds(10,10);
        public IWorldField CaptureSnapshot()=>this; // Immutable.
    }
    const BindingFlags Flags=BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public;
    static readonly Stopwatch Clock=Stopwatch.StartNew();
    static readonly List<object> Stops=new List<object>(),Altitude=new List<object>();
    static readonly List<Task> GeometryJobs=new List<Task>();
    static int checks,frame,maxResident,maxContact,maxUploads,maxConcurrentGeometry;
    static long maxTerrainBytes,maxContactBytes,maxScratchBytes;
    static string phase="setup";
    static object Get(object o,string n)=>o.GetType().GetField(n,Flags).GetValue(o);
    static void Set(object o,string n,object v)=>o.GetType().GetField(n,Flags).SetValue(o,v);
    static object Call(WorldExplorer w,string n,params object[] a)=>typeof(WorldExplorer).GetMethod(n,Flags).Invoke(w,a);
    static IDictionary Surfaces(WorldExplorer w,string n)=>(IDictionary)Get(w,n);
    static void Require(bool value,string name){if(!value)throw new Exception(name+" at "+phase+", tick "+frame);}
    static void Check(bool value,string name){Require(value,name);checks++;Console.WriteLine("PASS "+name);}
    static int Count(WorldExplorer w,string n)=>((ICollection)Get(w,n)).Count;
    static void Observe(WorldExplorer w)
    {
        var active=(HashSet<TileKey>)Get(w,"active");var desired=(HashSet<TileKey>)Get(w,"desired");
        Require(w.ResidentTiles<=128&&desired.Count<=64&&active.Count<=64,"at most two bounded terrain plans");
        Require(active.All(k=>Surfaces(w,"surfaces").Contains(k)),"every active coverage key has a published mesh");
        Require(Surfaces(w,"surfaces").Keys.Cast<TileKey>().All(k=>active.Contains(k)||desired.Contains(k)),"no obsolete third-plan mesh residency");
        Require(w.MeshUploadsLastFrame<=1,"one publication per tick");
        Require(w.ContactMeshBytes<=256L*1024*1024&&w.TerrainResidentMeshBytes<=w.TerrainTransitionMeshLimitBytes,"payload admission limits");
        Require(Surfaces(w,"contactSurfaces").Values.Cast<object>().All(s=>(int)Get(s,"Quads")==256),"all contact meshes retain 10cm detail");
        Require(w.VoxelBuildFailures==0,"no worker failure");
        var task=Get(w,"voxelBuildJob") as Task;
        GeometryJobs.RemoveAll(t=>t.IsCompleted);
        if(task!=null&&!task.IsCompleted&&!GeometryJobs.Contains(task))GeometryJobs.Add(task);
        maxConcurrentGeometry=Math.Max(maxConcurrentGeometry,GeometryJobs.Count);
        Require(GeometryJobs.Count<=1,"cancellation waits before replacing geometry worker");
        maxResident=Math.Max(maxResident,w.ResidentTiles);maxContact=Math.Max(maxContact,w.ContactTiles);
        maxUploads=Math.Max(maxUploads,w.MeshUploadsLastFrame);
        maxTerrainBytes=Math.Max(maxTerrainBytes,w.TerrainResidentMeshBytes);
        maxContactBytes=Math.Max(maxContactBytes,w.ContactMeshBytes);
        maxScratchBytes=Math.Max(maxScratchBytes,w.VoxelScratchBytes);
        Require(Clock.Elapsed.TotalSeconds<210,"bounded 210-second suite deadline");
    }
    static void Tick(WorldExplorer w)
    {
        Time.unscaledTime=++frame*.1f;Time.unscaledDeltaTime=.1f;
        Call(w,"Update");Observe(w);Thread.Sleep(1);
    }
    static void Settle(WorldExplorer w,string label)
    {
        phase=label;int first=frame,readyAt=-1,coverageAt=-1,clean=0;
        var clock=Stopwatch.StartNew();
        while(clock.Elapsed.TotalSeconds<35)
        {
            Tick(w);
            if(readyAt<0&&w.IsTerrainReadyArea(w.GlobalCamera.X,w.GlobalCamera.Z,2))readyAt=frame-first;
            if(coverageAt<0&&!(bool)Get(w,"planActivationPending")&&w.HasActiveCoverage&&!w.PlanningInProgress)coverageAt=frame-first;
            if(w.PendingTiles==0&&!w.PlanningInProgress&&w.HasActiveCoverage){if(++clean>=4)break;}else clean=0;
        }
        Require(clean>=4,"stationary queues converge: pending="+w.PendingTiles+" contact="+w.ContactPendingTiles);
        Require(readyAt>=0,"arrival support becomes ready");
        Require(w.TrySampleRenderedHeight(w.GlobalCamera.X,w.GlobalCamera.Z,out double h)&&Math.Abs(h-10)<1e-6,"arrival support remains exact flat-field height");
        Require(w.ResidentTiles<=64,"retire old plan after convergence");
        Stops.Add(new{label,ticks=frame-first,cpuWallMs=clock.Elapsed.TotalMilliseconds,readyTick=readyAt,coverageTick=coverageAt,
            resident=w.ResidentTiles,contacts=w.ContactTiles,farBytes=w.TerrainResidentMeshBytes,contactBytes=w.ContactMeshBytes,
            pixelTargetLimited=w.PixelTargetLimitedTiles,plannerJobs=w.PlannerJobsStarted});
        Console.WriteLine("STOP "+label+": "+(frame-first)+" ticks, "+w.ResidentTiles+" far / "+w.ContactTiles+" contact");
    }
    static void Flight(WorldExplorer w,double destinationX,double y,double speed,string label)
    {
        phase=label;double start=w.GlobalCamera.X,z=w.GlobalCamera.Z,dx=destinationX-start;
        int ticks=(int)Math.Ceiling(Math.Abs(dx)/(speed*.1));
        for(int i=1;i<=ticks;i++)
        {
            w.SetObserverPose(new WorldPoint(start+dx*i/ticks,y,z),Quaternion.Euler(20,dx>0?90:270,0));
            Tick(w);Require(w.HasActiveCoverage,"complete published coverage is retained during travel");
        }
        w.ForceReplan();Settle(w,label+" stop");
    }
    static void MeasureAltitude()
    {
        foreach(double aboveGround in new[]{1.65,20,32,100,1000})
        {
            var root=new GameObject("contact urgency diagnostic");var w=root.AddComponent<WorldExplorer>();
            w.UseVoxelLodTerrain=true;w.MaximumTiles=64;w.AsyncPlanning=false;w.ShowVisualWater=false;
            w.ConfigureExternal(new Field(),new SurfaceProfile(enableSnow:false),new WorldPoint(12,10+aboveGround,12),Quaternion.Euler(20,0,0));
            // Keep the real captured plan so an urgency candidate can reuse its
            // bounds; no geometry job has been advanced at this point.
            Set(w,"coarseStartup",false);Call(w,"RefreshContactPlan");
            int reported=0,physical=0;
            foreach(object work in (IEnumerable)Get(w,"contactPending"))
            {
                var tile=(LodTile)Get(work,"Tile");double d=(double)Call(w,"DistanceSquared",tile);
                var policy=typeof(WorldExplorer).GetMethod("ContactWorkIsUrgent",Flags);
                if(policy!=null?(bool)policy.Invoke(w,new object[]{tile}):d<=32*32)reported++;
                double dx=Math.Max(tile.OriginXMeters-12,Math.Max(0,12-tile.OriginXMeters-tile.SizeMeters));
                double dz=Math.Max(tile.OriginZMeters-12,Math.Max(0,12-tile.OriginZMeters-tile.SizeMeters));
                if(dx*dx+dz*dz+aboveGround*aboveGround<=32*32)physical++;
            }
            Altitude.Add(new{aboveGroundMeters=aboveGround,queued=Count(w,"contactPending"),reportedUrgent=reported,physicallyWithin32m=physical});
            UnityEngine.Object.Destroy(root);
        }
        Check(Altitude.Count==5,"contact urgency measured at five altitudes without changing runtime priority");
    }
    static void Main(string[] args)
    {
        GameObject root=null;WorldExplorer w=null;string status="PASS",error=null;
        try
        {
            MeasureAltitude();
            root=new GameObject("bounded repeated-route regression");w=root.AddComponent<WorldExplorer>();
            w.UseVoxelLodTerrain=true;w.MaximumTiles=64;w.AsyncPlanning=true;w.MeshBuildsPerFrame=1;
            w.ViewDistanceKm=4;w.FarHorizonDistanceKm=0;w.VoxelFullDetailRadius=16;w.VoxelContactBudgetMB=256;
            w.ShowVisualWater=false;w.MeshBuildMilliseconds=2;w.MeshWorkUnitsPerFrame=256;
            w.ConfigureExternal(new Field(),new SurfaceProfile(enableSnow:false),new WorldPoint(12,11.65,12),Quaternion.Euler(20,90,0));
            Settle(w,"initial ground");
            for(int repeat=0;repeat<3;repeat++)
            {
                double y=repeat==1?1010:11.65;
                Flight(w,10012,y,80,"route "+repeat+" normal 10km");
                int jobs=w.PlannerJobsStarted;w.SetObserverPose(w.GlobalCamera,Quaternion.Euler(20,270,0));w.ForceReplan();
                Settle(w,"route "+repeat+" 180-degree turn");
                Check(w.PlannerJobsStarted>jobs,"turn replans after route "+repeat);
                Flight(w,12,y,640,"route "+repeat+" boosted 10km return");
                w.SetObserverPose(new WorldPoint(12,11.65,12),Quaternion.Euler(20,90,0));w.ForceReplan();
                Settle(w,"route "+repeat+" ground arrival");
                Check(w.IsTerrainReadyArea(12,12,2),"safe 10cm arrival support after route "+repeat);
            }
            int builds=w.VoxelRefinementBuilds,jobsBefore=w.PlannerJobsStarted;
            for(int i=0;i<40;i++)Tick(w);
            Check(w.PendingTiles==0&&w.VoxelRefinementBuilds==builds&&w.PlannerJobsStarted==jobsBefore,"stationary state does not rebuild or replan");
            // Interrupt an in-flight destination change and verify explicit disposal.
            w.SetObserverPose(new WorldPoint(-20000,1010,-20000),Quaternion.Euler(35,0,0));w.ForceReplan();
            for(int i=0;i<12;i++)Tick(w);
            UnityEngine.Object.Destroy(root);root=null;
            Check(w.ResidentTiles==0&&w.ContactTiles==0&&Get(w,"voxelBuildJob")==null&&w.VoxelScratchBytes==0,"shutdown clears all owned caches and worker handle during travel");
            Check(maxConcurrentGeometry<=1&&maxUploads==1&&maxResident<=128,"all route observations obey worker, upload and residency bounds");
        }
        catch(Exception ex){status="FAIL";error=ex.ToString();Console.WriteLine(error);}
        finally{if(root!=null)UnityEngine.Object.Destroy(root);}
        File.WriteAllText(args[0],JsonSerializer.Serialize(new{status,error,checks,phase,frames=frame,wallMs=Clock.Elapsed.TotalMilliseconds,
            fixture=new{tiles=64,viewKm=4,contactRadius=16,contactBudgetMiB=256,virtualTickSeconds=.1,wallYieldMs=1,normalMetersPerSecond=80,boostMetersPerSecond=640,repeats=3},
            maxResident,maxContact,maxUploads,maxConcurrentGeometry,maxTerrainBytes,maxContactBytes,maxScratchBytes,altitude=Altitude,stops=Stops,
            scope="Frozen flat source, .NET numerical Unity substitutes, real asynchronous Update/planner/builder. Accelerated virtual routes stress scheduling; CPU wall times are not game Start/travel latency, FPS, GPU or memory residency. No runtime changes."},new JsonSerializerOptions{WriteIndented=true}));
        if(status!="PASS")Environment.ExitCode=1;
    }
}
