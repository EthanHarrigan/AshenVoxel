using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using AshenVoxel.WorldScale;
using AshenVoxel.WorldScaleRuntime;
using UnityEngine;

static class Program
{
    sealed class Field:IWorldFieldBounds,IWorldFieldSnapshotProvider
    {
        public double Height=10;
        public string SourceIdentity=>"progressive-lod-fixture-height-"+Height;
        public SurfaceSample Sample(double x,double z)=>new SurfaceSample(Height,6,6,true);
        public HeightBounds QueryBounds(double a,double b,double c,double d)=>new HeightBounds(Height,Height);
        public IWorldField CaptureSnapshot()=>new Field{Height=Height};
    }
    const BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
    static int checks,frame;
    static object Get(object obj,string name)=>obj.GetType().GetField(name,Private|BindingFlags.Public).GetValue(obj);
    static void Set(object obj,string name,object value)=>obj.GetType().GetField(name,Private|BindingFlags.Public).SetValue(obj,value);
    static object Call(WorldExplorer w,string name,params object[] args)=>typeof(WorldExplorer).GetMethod(name,Private).Invoke(w,args);
    static void Check(bool value,string message){if(!value)throw new Exception("FAIL "+message);checks++;Console.WriteLine("PASS "+message);}
    static LodTile Tile(long x,long z,int level)
    {double size=25.6*(1<<level);return new LodTile(new TileKey(x,z,level),x*size,z*size,size,10,10);}
    static IDictionary Surfaces(WorldExplorer w)=>(IDictionary)Get(w,"surfaces");
    static HashSet<TileKey> Active(WorldExplorer w)=>(HashSet<TileKey>)Get(w,"active");
    static int Quads(WorldExplorer w,TileKey key)=>(int)Get(Surfaces(w)[key],"Quads");
    static int Queued(WorldExplorer w)=>((ICollection)Get(w,"pending")).Count;
    static bool Building(WorldExplorer w)=>Get(w,"currentBuild")!=null;
    static long MeshBytes(WorldExplorer w)=>Surfaces(w).Values.Cast<object>().Sum(s=>(long)(int)Get(s,"TriangleCount")*60);
    static void Apply(WorldExplorer w,params LodTile[] tiles)
    {Call(w,"ApplyPlan",new LodPlan{Tiles=tiles,SourceIdentity="explicit bounded test topology"},25.6);}
    static void Tick(WorldExplorer w)
    {
        Time.unscaledTime=++frame*.02f;Time.unscaledDeltaTime=.02f;
#if !BASELINE
        Call(w,"QueueNextVoxelRefinement");
#endif
        Call(w,"AdvanceBuilds");
#if BASELINE
        if(Queued(w)==0&&!Building(w))Call(w,"ActivateReadyPlan");
#else
        Call(w,"ActivateReadyPlan");
#endif
        Thread.Sleep(1);
    }
    static void Until(WorldExplorer w,Func<bool> done,string context)
    {
        var end=DateTime.UtcNow.AddSeconds(40);
        do{Tick(w);if(done())return;}while(DateTime.UtcNow<end);
        throw new Exception("Timeout: "+context+" queue="+Queued(w)+" pending="+w.PendingTiles);
    }
    static void Settle(WorldExplorer w)
    {
#if BASELINE
        Until(w,()=>Queued(w)==0&&!Building(w),"steady target");
#else
        Until(w,()=>Queued(w)==0&&!Building(w)&&!(bool)Get(w,"voxelRefinementScanPending"),"steady target");
#endif
    }
    static void Main(string[] args)
    {
        var root=new GameObject("bounded progressive terrain fixture");
        var w=root.AddComponent<WorldExplorer>();
        w.UseVoxelLodTerrain=true;w.MaximumTiles=64;w.AsyncPlanning=false;w.MeshBuildsPerFrame=1;w.MeshWorkUnitsPerFrame=2048;w.MeshBuildMilliseconds=8;
        var source=new Field();w.ConfigureExternal(source,new SurfaceProfile(enableSnow:false),new WorldPoint(12,12,12),Quaternion.Euler(0,0,0));
        Call(w,"ClearTerrain");Set(w,"coarseStartup",true);
        var near=Tile(0,0,0);var front=Tile(0,2,5);var frontEast=Tile(1,2,5);var rear=Tile(0,-3,5);
        var initial=new[]{near,front,frontEast,rear};
        Apply(w,initial);
        Until(w,()=>w.HasActiveCoverage,"initial coverage");
        Check(initial.All(t=>Active(w).Contains(t.Key)&&Quads(w,t.Key)==8),"initial bounded coverage publishes all8-column tiles");
        Check(!(bool)Get(w,"coarseStartup"),"initial coverage opens the refinement phase");
        int startFrame=frame;
        var clock=Stopwatch.StartNew();
        // A plan during ongoing work must retain useful completed coverage.
        Apply(w,initial);
        var observed=new HashSet<int>{8};
        Until(w,()=>
        {
            observed.Add(Quads(w,front.Key));
#if BASELINE
            return Queued(w)==0&&!Building(w);
#else
            return Queued(w)==0&&!Building(w)&&!(bool)Get(w,"voxelRefinementScanPending");
#endif
        },"progressive refinement");
        var refinementMs=clock.Elapsed.TotalMilliseconds;
        int refinementFrames=frame-startFrame;
#if !BASELINE
        Check(observed.SetEquals(new[]{8,32,64,128,256}),"visible terrain progressively publishes8,32,64,128,256 columns");
        Check(Quads(w,near.Key)==256&&Quads(w,rear.Key)==32,"near full detail and rear bounded quality stay distinct");
        Check(Quads(w,front.Key)==256,"visible pixel-limited terrain advances beyond the previous128 ceiling");
        Check(w.TerrainResidentMeshBytes==MeshBytes(w),"resident payload includes every live far mesh exactly once");
        int stableBuilds=w.VoxelRefinementBuilds,stableMeshes=UnityEngine.Object.Created.OfType<Mesh>().Count();
        for(int i=0;i<40;i++)Tick(w);
        Check(w.VoxelRefinementBuilds==stableBuilds&&UnityEngine.Object.Created.OfType<Mesh>().Count()==stableMeshes&&w.PendingTiles==0,"static settled scene terminates without rebuilding tiles or allocating meshes");
#endif
        // Same finite replacement plan/camera for both strategies: no native FPS claim.
        w.SetObserverPose(new WorldPoint(2060,12,12),Quaternion.Euler(0,0,0));
        var arrival=new[]{Tile(80,0,0),Tile(2,2,5),Tile(3,2,5),Tile(2,-3,5)};
        int travelFrame=frame;clock.Restart();
        Apply(w,arrival);
        Check(w.HasActiveCoverage&&Active(w).Contains(front.Key),"old complete coverage remains while replacement tiles build");
        Check(w.RenderOrigin.X!=0,"travel rebases the retained terrain immediately");
        Until(w,()=>arrival.All(t=>Active(w).Contains(t.Key)),"travel coverage");
        var travelMs=clock.Elapsed.TotalMilliseconds;int travelFrames=frame-travelFrame;
#if !BASELINE
        Check(arrival.All(t=>Quads(w,t.Key)==8),"travel publishes complete coarse replacement before expensive quality work");
        Check(w.VoxelRefinementPendingTiles>0,"unfinished quality remains observable after coverage publication");
        Check(!Surfaces(w).Contains(front.Key),"obsolete high-detail coverage is retired at replacement publication");
        // Exercise real admission/backoff using an oversized hypothetical payload;
        // no huge mesh or process-memory allocation is needed for this boundary test.
        long detailBudget=w.TerrainDetailLimitBytes;
        long reserve=(long)typeof(WorldExplorer).GetField("VoxelCoverageTileReserveBytes",BindingFlags.Static|BindingFlags.NonPublic).GetRawConstantValue();
        Check(!(bool)Call(w,"AdmitVoxelMesh",arrival[1],256,detailBudget+reserve+120),"mesh admission rejects a payload exceeding the detail limit before upload");
        Check(w.VoxelRefinementDeferredTiles==1&&w.VoxelRefinementPendingTiles>0,"capacity rejection retains an observable quality request");
        Settle(w);
        stableBuilds=w.VoxelRefinementBuilds;
        for(int i=0;i<40;i++)Tick(w);
        Check(w.VoxelRefinementBuilds==stableBuilds&&w.VoxelRefinementDeferredTiles==1&&w.PendingTiles>0,"capacity-limited work backs off rather than retrying unchanged requests forever");
        Check(arrival.Where(t=>!t.Key.Equals(arrival[1].Key)).All(t=>Quads(w,t.Key)>8),"one capacity-denied tile does not starve other refinements");
        Check(w.TerrainResidentMeshBytes<=w.TerrainSteadyMeshLimitBytes&&w.TerrainSteadyMeshLimitBytes<2L*1024*1024*1024&&
            w.TerrainTransitionMeshLimitBytes<5L*1024*1024*1024/2,"steady far payload is below2GiB and both bounded plans below2.5GiB");
        // Tight exact boundary: replacement credit counts the replaced tile only.
        long detailUsed=(long)Get(w,"voxelDetailMeshBytes");
        long oldBytes=(long)(int)Get(Surfaces(w)[arrival[1].Key],"TriangleCount")*60;
        long available=detailBudget-detailUsed+Math.Max(0,oldBytes-reserve);
        Check((bool)Call(w,"AdmitVoxelMesh",arrival[1],256,reserve+available)&&
            !(bool)Call(w,"AdmitVoxelMesh",arrival[1],256,reserve+available+1),"byte admission accepts the exact ceiling and rejects one byte above it");
        Apply(w,arrival);Settle(w);
        Check(w.VoxelRefinementDeferredTiles==0&&Quads(w,arrival[1].Key)==256,"a new plan reconsiders retained capacity requests and completes them");
        // A turn reprioritizes within the same resident set; rear detail releases capacity.
        w.SetObserverPose(new WorldPoint(2060,12,12),Quaternion.Euler(0,180,0));Apply(w,arrival);Settle(w);
        Check(Quads(w,arrival[1].Key)==32&&Quads(w,arrival[3].Key)==256,"view turn releases rear detail and improves the newly visible tile");
        // Source revision cancellation: keep old tile until one immutable replacement is ready.
        source.Height=12;
        w.RefreshExternalRegion(arrival[3].OriginXMeters,arrival[3].OriginZMeters,arrival[3].OriginXMeters+arrival[3].SizeMeters,arrival[3].OriginZMeters+arrival[3].SizeMeters);
        Check(w.HasActiveCoverage&&w.PendingTiles>0,"source invalidation retains coverage and observable replacement work");
        Settle(w);
        Check(w.TrySampleRenderedHeight(arrival[3].OriginXMeters+10,arrival[3].OriginZMeters+10,out double height)&&Math.Abs(height-12)<1e-6,"source invalidation replaces terrain with the new immutable source height");
        // Rapid replans must discard partial *previous desired* plans, preserving
        // only the still-active plan and the newest desired plan. Leave each plan
        // half-built, with another tile queued, then replace it again.
        bool boundedAcrossReplans=true,obsoleteReleased=true,accountingExact=true,oldCoverageRetained=true;
        object previousPartial=null;LodTile[] rapid=null;
        for(int iteration=0;iteration<8;iteration++)
        {
            rapid=new[]{arrival[0],Tile(20+iteration*2,2,5),Tile(21+iteration*2,2,5)};
            Apply(w,rapid);
            if(previousPartial!=null)obsoleteReleased&=((Mesh)Get(previousPartial,"Mesh")).WasDestroyed;
            Until(w,()=>Surfaces(w).Contains(rapid[1].Key)||Surfaces(w).Contains(rapid[2].Key),"partial rapid plan");
            var latest=(HashSet<TileKey>)Get(w,"desired");
            boundedAcrossReplans&=Surfaces(w).Keys.Cast<TileKey>().All(key=>Active(w).Contains(key)||latest.Contains(key))&&
                w.ResidentTiles<=Active(w).Count+latest.Count;
            accountingExact&=w.TerrainResidentMeshBytes==MeshBytes(w)&&w.TerrainResidentMeshBytes<=w.TerrainTransitionMeshLimitBytes;
            oldCoverageRetained&=arrival.All(tile=>Active(w).Contains(tile.Key));
            previousPartial=Surfaces(w)[Surfaces(w).Contains(rapid[1].Key)?rapid[1].Key:rapid[2].Key];
        }
        Check(boundedAcrossReplans&&oldCoverageRetained,"eight incomplete rapid replans retain only active plus latest desired coverage");
        Check(obsoleteReleased&&accountingExact,"superseded partial meshes are destroyed and byte accounting remains exact across rapid replans");
        Until(w,()=>rapid.All(tile=>Active(w).Contains(tile.Key)),"latest rapid plan publication");
        Check(w.ResidentTiles==rapid.Length&&w.TerrainResidentMeshBytes==MeshBytes(w),"latest rapid plan publishes and retires all obsolete active coverage");
        // Same-key replan must not discard a still-useful asynchronous job.
        var next=Tile(4,2,5);Apply(w,next);Tick(w);
        object inFlight=Get(w,"currentBuild");Apply(w,next);
        Check(inFlight!=null&&ReferenceEquals(inFlight,Get(w,"currentBuild")),"replanning retains an in-flight tile still needed by the new plan");
        // Real small contact ring, real worker builds: exercise the two-contact /
        // one-far policy rather than merely checking its counter arithmetic.
        w.VoxelFullDetailRadius=48;Call(w,"RefreshContactPlan");
        Until(w,()=>
        {
            object build=Get(w,"currentBuild");
            return build!=null&&(bool)Get(Get(build,"Work"),"Progressive")&&w.ContactPendingTiles>0;
        },"distant fairness while outer contact ring is pending");
        var contacts=(IDictionary)Get(w,"contactSurfaces");
        var contactDesired=(HashSet<TileKey>)Get(w,"contactDesired");
        bool innerReady=contactDesired.Where(key=>
        {
            var tile=Tile(key.X,key.Z,0);
            return (double)Call(w,"DistanceSquared",tile)<=32*32;
        }).All(key=>contacts.Contains(key));
        Check(innerReady&&!(bool)Call(w,"HasUrgentContactWork"),"all urgent32m contact tiles publish before the scheduler shares a far refinement slot");
        Check(w.ContactPendingTiles>0&&w.ContactTiles>0,"distant refinement begins while the outer contact ring still has work");
        Check(w.ContactMeshBytes<128L*1024*1024,"fairness fixture uses a bounded small contact ring rather than a full-world allocation");
        // Shutdown during active work must release all cache accounting.
#endif
        UnityEngine.Object.Destroy(root);
#if !BASELINE
        Check(w.ResidentTiles==0&&w.TerrainResidentMeshBytes==0&&w.VoxelRefinementPendingTiles==0&&w.VoxelRefinementDeferredTiles==0&&Get(w,"voxelBuildJob")==null,"shutdown cancels active work and clears meshes, deferred requests and byte accounting");
#endif
        var report=new{status="PASS",checks,frames=frame,refinementMs,refinementFrames,travelMs,travelFrames,observedFrontQuads=observed.OrderBy(x=>x).ToArray(),scope="Frozen-source Windows .NET8 numerical Unity substitutes, bounded four-tile fixture. Worker time, cooperative ticks and mesh payload are not native rendering/FPS. Capacity boundaries use hypothetical byte counts without allocating huge meshes."};
        File.WriteAllText(args[0],JsonSerializer.Serialize(report,new JsonSerializerOptions{WriteIndented=true}));
        Console.WriteLine("All "+checks+" progressive terrain checks passed; travel coverage "+travelFrames+" ticks / "+travelMs.ToString("F1")+"ms.");
    }
}
