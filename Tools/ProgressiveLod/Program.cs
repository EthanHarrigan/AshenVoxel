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
    static int coarseFirstTiles=0,requestedTilesAfterCoverage=0;
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
#if !BASELINE
        ConnectedSurfaceChecks();
        StartupMemoryChecks();
        CoarseFirstChecks();
        SubtreePublicationChecks();
        DistantSweepChecks();
        PhysicalHorizonChecks();
#endif
        var root=new GameObject("bounded progressive terrain fixture");
        var w=root.AddComponent<WorldExplorer>();w.ConnectedTerrainSurface=false;
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
        Check(observed.SetEquals(new[]{8,16,32,64,128,256}),"outer terrain progressively publishes8,16,32,64,128,256 columns");
        Check(Quads(w,near.Key)==256&&Quads(w,rear.Key)==256,"outer rear terrain earns the same camera-independent pixel target as the front");
        Check(Quads(w,front.Key)==256,"visible pixel-limited terrain advances beyond the previous128 ceiling");
        Set(w,"availableCommitBytes",3L*1024*1024*1024);Set(w,"nextCommitProbe",double.MaxValue);
        Call(w,"DeferVoxelCommit",front,256,false,1024L*1024*1024);
        Check(w.VoxelCommitDeferredTiles==1&&!(bool)Call(w,"HasVoxelUploadHeadroom",1024L*1024*1024),"large replacement retains exact queued demand and still obeys unchanged commit safety reserve");
        Call(w,"SetVoxelTravelDeferral",true);
        Until(w,()=>initial.All(t=>Quads(w,t.Key)<=32)&&!Building(w),"downshift past commit-deferred upgrade");
        Check(w.VoxelCommitDeferredTiles==1&&Queued(w)>0,"safe small downshifts bypass a commit-deferred large upgrade without discarding it");
        Set(w,"availableCommitBytes",8L*1024*1024*1024);Call(w,"SetVoxelTravelDeferral",false);Settle(w);
        Check(w.VoxelCommitDeferredTiles==0&&Quads(w,front.Key)==256,"sufficient real headroom wakes retained quality demand");
        Set(w,"availableCommitBytes",-1L);Set(w,"nextCommitProbe",0d);
        long beforeTravelBytes=w.TerrainResidentMeshBytes;
        Call(w,"SetVoxelTravelDeferral",true);Settle(w);
        Check(initial.All(t=>Quads(w,t.Key)<=32)&&w.TerrainResidentMeshBytes<beforeTravelBytes&&initial.All(t=>Active(w).Contains(t.Key)),"fast travel reclaims fine mesh payload without a new plan or losing active coverage");
        Call(w,"SetVoxelTravelDeferral",false);Settle(w);
        Check(Quads(w,front.Key)==256&&Quads(w,near.Key)==256,"slowing restores progressive detail on the same retained plan");
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
#if BASELINE
        Check(w.HasActiveCoverage&&Active(w).Contains(front.Key),"old complete coverage remains while replacement tiles build");
#else
        Check(initial.All(t=>!Active(w).Contains(t.Key))&&w.PendingTiles>0,"teleport retires only old footprints outside the new desired coverage while new frontier work remains queued");
#endif
        Check(w.RenderOrigin.X!=0,"travel rebases the retained terrain immediately");
        Until(w,()=>arrival.All(t=>Active(w).Contains(t.Key)),"travel coverage");
        var travelMs=clock.Elapsed.TotalMilliseconds;int travelFrames=frame-travelFrame;
#if !BASELINE
        Check(arrival.All(t=>Quads(w,t.Key)>=8)&&BalancedDisjoint(w),"travel publishes valid coarse-or-refined coverage while completed frontier tiles share refinement slots");
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
        Check(arrival.Where(t=>t.Key.Level==arrival[1].Key.Level&&!t.Key.Equals(arrival[1].Key)).All(t=>Quads(w,t.Key)>8),"one capacity-denied tile still lets its same-spacing peers complete the current physical sweep band");
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
        // Spinning the camera must preserve completed global outer sweeps.
        w.SetObserverPose(new WorldPoint(2060,12,12),Quaternion.Euler(0,180,0));Apply(w,arrival);Settle(w);
        Check(Quads(w,arrival[1].Key)==256&&Quads(w,arrival[3].Key)==256,"view turn preserves completed outer detail across all directions");
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
            oldCoverageRetained&=Active(w).Contains(arrival[0].Key);
            previousPartial=Surfaces(w)[Surfaces(w).Contains(rapid[1].Key)?rapid[1].Key:rapid[2].Key];
        }
        Check(boundedAcrossReplans&&oldCoverageRetained,"eight incomplete rapid replans preserve overlapping coverage and retain only active plus latest desired caches");
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
        var report=new{status="PASS",checks,frames=frame,coarseFirstTiles,requestedTilesAfterCoverage,refinementMs,refinementFrames,travelMs,travelFrames,observedFrontQuads=observed.OrderBy(x=>x).ToArray(),scope="Frozen-source Windows .NET8 numerical Unity substitutes, bounded fixtures including actual30km coarse-first planning. Worker time, cooperative ticks and mesh payload are not native rendering/FPS. Capacity boundaries use hypothetical byte counts without allocating huge meshes."};
        File.WriteAllText(args[0],JsonSerializer.Serialize(report,new JsonSerializerOptions{WriteIndented=true}));
        Console.WriteLine("All "+checks+" progressive terrain checks passed; travel coverage "+travelFrames+" ticks / "+travelMs.ToString("F1")+"ms.");
    }
#if !BASELINE
    static void ConnectedSurfaceChecks()
    {
        var root=new GameObject("connected terrain fixture");var w=root.AddComponent<WorldExplorer>();
        Check(w.ConnectedTerrainSurface,"connected terrain is the default visual mode");
        w.UseVoxelLodTerrain=true;w.MaximumTiles=64;w.AsyncPlanning=false;w.MeshBuildsPerFrame=1;w.MeshWorkUnitsPerFrame=2048;w.MeshBuildMilliseconds=8;
        var source=new Field{Height=10.037};w.ConfigureExternal(source,new SurfaceProfile(enableSnow:false),new WorldPoint(12,12,12),Quaternion.Euler(0,0,0));
        Call(w,"ClearTerrain");Set(w,"coarseStartup",true);
        var near=Tile(0,0,0);var far=Tile(0,2,5);Apply(w,near,far);
        Until(w,()=>w.HasActiveCoverage,"connected initial coverage");
        Check((bool)Get(Surfaces(w)[far.Key],"HeightProxy")&&(bool)Get(Surfaces(w)[near.Key],"HeightProxy"),"near and far use indexed connected surfaces");
        Call(w,"RefreshContactPlan");
        var contacts=(IDictionary)Get(w,"contactSurfaces");
        Until(w,()=>contacts.Contains(near.Key),"connected contact publication");
        Check((bool)Get(contacts[near.Key],"HeightProxy")&&(int)Get(contacts[near.Key],"Quads")==64,"contact uses bounded0.4m connected surface");
        Check(w.IsTerrainReady(12,12),"connected contact satisfies startup ground readiness");
        Check(w.TrySampleRenderedHeight(12,12,out double h)&&Math.Abs(h-10.037)<.00001,"visible triangle query retains unquantized surface height");
        Check(HeightProxySurface.Version==2,"continuous heights invalidate version1 disk cache");
        source.Height=12.063;
        w.RefreshExternalRegion(near.OriginXMeters,near.OriginZMeters,near.OriginXMeters+near.SizeMeters,near.OriginZMeters+near.SizeMeters);
        Call(w,"RefreshContactPlan");
        Until(w,()=>w.TrySampleRenderedHeight(12,12,out double updated)&&Math.Abs(updated-12.063)<.00001,"connected source refresh");
        Check(w.TrySampleRenderedHeight(12,12,out h)&&Math.Abs(h-12.063)<.00001,"connected contact refresh publishes new immutable continuous source");
        w.SetObserverPose(new WorldPoint(5012,12,12),Quaternion.Euler(0,0,0));
        Check(w.TrySampleRenderedHeight(12,12,out double after)&&Math.Abs(after-h)<.00001,"render height query is invariant under origin rebase");
        UnityEngine.Object.Destroy(root);
        Check(w.ContactMeshBytes==0&&w.TerrainResidentMeshBytes==0,"connected contact and distant memory retire cleanly");
    }
    static void DistantSweepChecks()
    {
        var root=new GameObject("global distant sweep fixture");var w=root.AddComponent<WorldExplorer>();w.ConnectedTerrainSurface=false;
        w.UseVoxelLodTerrain=true;w.MaximumTiles=64;w.AsyncPlanning=false;w.MeshBuildsPerFrame=1;w.MeshWorkUnitsPerFrame=2048;w.MeshBuildMilliseconds=8;
        w.ConfigureExternal(new Field(),new SurfaceProfile(enableSnow:false),new WorldPoint(12,12,12),Quaternion.Euler(0,0,0));Call(w,"ClearTerrain");Set(w,"coarseStartup",true);
        var outer=new[]{Tile(0,3,5),Tile(0,-4,5),Tile(3,0,5),Tile(-4,0,5),Tile(20,20,4)};var near=Tile(0,0,0);var all=outer.Concat(new[]{near}).ToArray();
        Apply(w,all);Until(w,()=>w.HasActiveCoverage,"sweep initial coverage");
        var targets=outer.ToDictionary(t=>t.Key,t=>(int)Call(w,"VoxelQuadsFor",t));
        Set(w,"availableCommitBytes",8L*1024*1024*1024);Set(w,"nextCommitProbe",double.MaxValue);
        Call(w,"DeferVoxelCommit",outer[0],16,false,32L*1024*1024*1024);
        Until(w,()=>outer.Skip(1).Where(t=>t.Key.Level==5).All(t=>Quads(w,t.Key)>=16),"all eligible directions finish the first physical sweep band");
        for(int i=0;i<40;i++)Tick(w);
        Check(Quads(w,outer[0].Key)==8&&outer.Skip(1).Where(t=>t.Key.Level==5).All(t=>Quads(w,t.Key)==16)&&Quads(w,outer[4].Key)==8&&w.DistantSweepSpacingMeters==80&&w.DistantSweepDeferredTiles==1,"a blocked coarse tile holds the80m mesh band while the smaller outer level correctly waits at8columns");
        Check(w.DistantSweepTiles==outer.Length&&w.DistantSweepCompletedTiles==outer.Length-1,"sweep HUD counts both outer levels and honest incomplete-rung progress");
        Set(w,"availableCommitBytes",256L*1024*1024*1024);
        bool ordered=true,oneRung=true;int updates=0;
        Until(w,()=>
        {
            var before=outer.ToDictionary(t=>t.Key,t=>Quads(w,t.Key));Tick(w);updates++;
            foreach(var tile in outer)
            {
                int previous=before[tile.Key],current=Quads(w,tile.Key);if(current<=previous)continue;
                oneRung&=current==Math.Min(targets[tile.Key],previous*2);
                double previousSpacing=tile.SizeMeters/previous;
                ordered&=outer.All(other=>other.SizeMeters/before[other.Key]<=previousSpacing+.000001||before[other.Key]>=targets[other.Key]&&other.SizeMeters/before[other.Key]<=160.000001);
            }
            return outer.All(t=>Quads(w,t.Key)>=targets[t.Key]);
        },"successive complete camera-independent outer sweeps");
        Check(updates>0&&ordered&&oneRung,"actual worker publications advance every required physical band before beginning finer spacing across both outer levels");
        Settle(w);var finished=outer.Select(t=>Quads(w,t.Key)).ToArray();int builds=w.VoxelRefinementBuilds;
        w.SetObserverPose(new WorldPoint(12,12,12),Quaternion.Euler(0,180,0));Apply(w,all);Settle(w);
        Check(outer.Select(t=>Quads(w,t.Key)).SequenceEqual(finished)&&outer.All(t=>(int)Call(w,"VoxelQuadsFor",t)==targets[t.Key])&&w.VoxelRefinementBuilds==builds,"camera rotation neither lowers outer targets nor restarts completed sweep meshes");
        Check(finished.Any(q=>q<256)&&w.DistantSweepCompletedTiles==w.DistantSweepTiles,"pixel-satisfied distant tiles stop below256 instead of paying for a forced maximum");UnityEngine.Object.Destroy(root);

        root=new GameObject("active fallback fair sweep fixture");w=root.AddComponent<WorldExplorer>();w.ConnectedTerrainSurface=false;w.UseVoxelLodTerrain=true;w.MaximumTiles=64;w.AsyncPlanning=false;w.MeshBuildsPerFrame=1;w.MeshWorkUnitsPerFrame=2048;w.MeshBuildMilliseconds=8;
        var source=new Field();w.ConfigureExternal(source,new SurfaceProfile(enableSnow:false),new WorldPoint(12,12,12),Quaternion.Euler(0,0,0));Call(w,"ClearTerrain");Set(w,"coarseStartup",true);
        var fallback=Tile(0,3,5);var other=Tile(-4,0,5);Apply(w,fallback,other,near);Until(w,()=>w.HasActiveCoverage,"active fallback initial plan");
        var children=new[]{Tile(0,6,4),Tile(1,6,4),Tile(0,7,4),Tile(1,7,4)};
        var coverage=Enumerable.Range(0,24).Select(i=>Tile(20+i*2,20,5)).ToArray();Apply(w,children.Concat(coverage).Concat(new[]{other,near}).ToArray());
        Set(w,"availableCommitBytes",8L*1024*1024*1024);Set(w,"nextCommitProbe",double.MaxValue);foreach(var tile in children)Call(w,"DeferVoxelCommit",tile,8,false,32L*1024*1024*1024);
        Until(w,()=>Active(w).Contains(fallback.Key)&&Quads(w,fallback.Key)>=16,"active fallback fair share during runnable replacement coverage");
        Check(!((HashSet<TileKey>)Get(w,"desired")).Contains(fallback.Key)&&(bool)Get(w,"planActivationPending")&&w.PendingTiles>0,"active fallback surface gains a sweep rung before its pending replacement plan completes");
        int preserved=Quads(w,fallback.Key);source.Height=12;w.RefreshExternalRegion(fallback.OriginXMeters,fallback.OriginZMeters,fallback.OriginXMeters+fallback.SizeMeters,fallback.OriginZMeters+fallback.SizeMeters);
        foreach(var tile in children)Call(w,"DeferVoxelCommit",tile,8,false,32L*1024*1024*1024);
        Until(w,()=>!((HashSet<TileKey>)Get(w,"externallyDirty")).Contains(fallback.Key),"source-dirty active fallback refresh");
        Check(Quads(w,fallback.Key)==preserved&&w.TrySampleRenderedHeight(fallback.OriginXMeters+10,fallback.OriginZMeters+10,out double height)&&Math.Abs(height-12)<1e-6,"source-dirty active fallback refreshes at its preserved rung before ordinary sweep upgrades");
        Set(w,"availableCommitBytes",256L*1024*1024*1024);
        Until(w,()=>!Active(w).Contains(fallback.Key)&&children.All(t=>Active(w).Contains(t.Key)),"fallback retires after exact completed child coverage");
        for(int i=0;i<4;i++)Tick(w);
        var running=Get(w,"currentBuild");bool oldWork=running!=null&&((LodTile)Get(Get(running,"Work"),"Tile")).Key.Equals(fallback.Key);
        Check(!Surfaces(w).Contains(fallback.Key)&&!oldWork&&BalancedDisjoint(w),"swept fallback retires without an obsolete running job, resurrection or overlap");UnityEngine.Object.Destroy(root);
    }
    static void PhysicalHorizonChecks()
    {
        var root=new GameObject("physical320-to160 horizon fixture");var w=root.AddComponent<WorldExplorer>();
        w.UseVoxelLodTerrain=true;w.MaximumTiles=64;w.AsyncPlanning=false;w.MeshBuildsPerFrame=1;w.MeshWorkUnitsPerFrame=2048;w.MeshBuildMilliseconds=8;w.VoxelTargetPixels=8;
        w.ConfigureExternal(new Field(),new SurfaceProfile(enableSnow:false),new WorldPoint(12,12,12),Quaternion.Euler(0,0,0));Call(w,"ClearTerrain");Set(w,"coarseStartup",true);
        var far=new[]{Tile(0,1,10),Tile(1,0,10)};var inner=Tile(0,1,9);var horizon=far.Concat(new[]{inner}).ToArray();Apply(w,horizon.Concat(new[]{Tile(0,0,0)}).ToArray());
        Until(w,()=>w.HasActiveCoverage,"cheap whole-view baseline before physical sweeps");
        Check(horizon.All(t=>Quads(w,t.Key)==8),"physical sweep policy preserves cheap8-column initial coverage and does not label it320m mesh spacing");
        int first160Level=-1;bool physicalOrder=true;var bands=new HashSet<double>();
        Until(w,()=>
        {
            var before=horizon.ToDictionary(t=>t.Key,t=>t.SizeMeters/Quads(w,t.Key));Tick(w);if(w.DistantSweepSpacingMeters>0)bands.Add(w.DistantSweepSpacingMeters);
            foreach(var tile in horizon)
            {
                double spacing=tile.SizeMeters/Quads(w,tile.Key);if(spacing>=before[tile.Key]-.000001)continue;
                physicalOrder&=horizon.All(other=>before[other.Key]<=before[tile.Key]+.000001||before[other.Key]<=160.000001&&Quads(w,other.Key)>=(int)Call(w,"VoxelQuadsFor",other));
                if(first160Level<0&&spacing<=160.000001)first160Level=tile.Key.Level;
            }
            return horizon.All(t=>t.SizeMeters/Quads(w,t.Key)<=160.000001);
        },"whole-horizon physical mesh passes through320m then160m");
        Check(physicalOrder&&bands.Contains(320)&&bands.Contains(160),"large mixed-LOD horizon meshes complete broad320m and160m bands without a column-count shortcut");
        Check(first160Level==10&&Quads(w,far[0].Key)==256&&Quads(w,inner.Key)==128,"farther horizon reaches160m first while the inner level needs fewer columns for identical physical detail");
        Check(horizon.All(t=>t.SizeMeters/Quads(w,t.Key)>100)&&w.DistantSweepSpacingLimitedTiles==0,"160m target uses attainable102.4m dyadic mesh spacing without claiming new learned samples");
        UnityEngine.Object.Destroy(root);

        root=new GameObject("explicit spacing-limit fixture");w=root.AddComponent<WorldExplorer>();w.UseVoxelLodTerrain=true;w.MaximumTiles=64;w.AsyncPlanning=false;w.MeshBuildsPerFrame=1;w.MeshWorkUnitsPerFrame=2048;w.MeshBuildMilliseconds=8;
        w.ConfigureExternal(new Field(),new SurfaceProfile(enableSnow:false),new WorldPoint(12,12,12),Quaternion.Euler(0,0,0));Call(w,"ClearTerrain");Set(w,"coarseStartup",true);
        var huge=Tile(0,1,12);Apply(w,huge);Until(w,()=>w.HasActiveCoverage,"spacing-limit fixture coverage");
        // Scheduling-only metadata fixture: no huge256-column mesh is allocated.
        Set(Surfaces(w)[huge.Key],"Quads",256);Set(w,"voxelRefinementScanPending",true);Call(w,"QueueNextVoxelRefinement");
        Check(w.DistantSweepSpacingLimitedTiles==1&&w.DistantSweepCompletedTiles==0&&w.DistantSweepSpacingMeters==320&&w.DistantSweepLargestSpacingMeters>400,"256-column limit honestly blocks a too-large tile above320m instead of claiming160m completion");
        UnityEngine.Object.Destroy(root);
    }
    static int ActiveAt(WorldExplorer w,double x,double z)=>Active(w).Count(key=>
    {var tile=(LodTile)Get(Surfaces(w)[key],"Tile");return x>=tile.OriginXMeters&&x<tile.OriginXMeters+tile.SizeMeters&&z>=tile.OriginZMeters&&z<tile.OriginZMeters+tile.SizeMeters;});
    static bool BalancedDisjoint(WorldExplorer w)
    {
        var tiles=Active(w).Select(key=>(LodTile)Get(Surfaces(w)[key],"Tile")).ToArray();
        for(int i=0;i<tiles.Length;i++)for(int j=i+1;j<tiles.Length;j++)
        {
            var a=tiles[i];var b=tiles[j];
            if(a.OriginXMeters<b.OriginXMeters+b.SizeMeters&&b.OriginXMeters<a.OriginXMeters+a.SizeMeters&&a.OriginZMeters<b.OriginZMeters+b.SizeMeters&&b.OriginZMeters<a.OriginZMeters+a.SizeMeters)return false;
            if(LodPlanner.Adjacent(a,b)&&Math.Abs(a.Key.Level-b.Key.Level)>1)return false;
        }
        return true;
    }
    static void SubtreePublicationChecks()
    {
        var root=new GameObject("atomic subtree publication fixture");var w=root.AddComponent<WorldExplorer>();w.ConnectedTerrainSurface=false;
        w.UseVoxelLodTerrain=true;w.MaximumTiles=64;w.AsyncPlanning=false;w.MeshBuildsPerFrame=1;w.MeshWorkUnitsPerFrame=2048;w.MeshBuildMilliseconds=8;
        w.ConfigureExternal(new Field(),new SurfaceProfile(enableSnow:false),new WorldPoint(12,12,12),Quaternion.Euler(0,0,0));Call(w,"ClearTerrain");Set(w,"coarseStartup",true);
        var parent=Tile(-1,-1,1);var retained=Tile(4,0,1);Apply(w,parent,retained);Until(w,()=>w.HasActiveCoverage,"old subtree fixture");
        var children=new[]{Tile(-2,-2,0),Tile(-1,-2,0),Tile(-2,-1,0),Tile(-1,-1,0)};
        var frontier=Tile(6,0,1);var blocked=Tile(10,0,1);var desired=children.Concat(new[]{retained,frontier,blocked}).ToArray();
        var oldMesh=(Mesh)Get(Surfaces(w)[parent.Key],"Mesh");Apply(w,desired);
        Set(w,"availableCommitBytes",3L*1024*1024*1024);Set(w,"nextCommitProbe",double.MaxValue);Call(w,"DeferVoxelCommit",blocked,8,false,32L*1024*1024*1024);
        bool bounded=true;Until(w,()=>
        {
            bounded&=w.LastCoveragePublicationChecks<=512&&w.LastCoverageGroupPublications<=4;
            return children.All(t=>Active(w).Contains(t.Key))&&Active(w).Contains(frontier.Key);
        },"ready subtree and new frontier while unrelated work is blocked");
        Check(!Active(w).Contains(parent.Key)&&oldMesh.WasDestroyed&&Active(w).Contains(retained.Key)&&!Active(w).Contains(blocked.Key)&&w.PendingTiles>0,"completed negative-coordinate subtree and new frontier publish before unrelated pending coverage");
        Check(bounded&&BalancedDisjoint(w)&&children.All(t=>ActiveAt(w,t.OriginXMeters+1,t.OriginZMeters+1)==1),"atomic replacement retains exactly one surface per old footprint with bounded per-frame checks/publications");
        var childMeshes=children.Select(t=>(Mesh)Get(Surfaces(w)[t.Key],"Mesh")).ToArray();Apply(w,parent,retained,frontier,blocked);
        Until(w,()=>Active(w).Contains(parent.Key),"completed coarsening group");
        Check(childMeshes.All(m=>m.WasDestroyed)&&!children.Any(t=>Active(w).Contains(t.Key))&&BalancedDisjoint(w)&&w.PendingTiles>0,"coarsening replaces every overlapping child atomically before the rest of the plan completes");
        // A frontier that no longer intersects any requested coverage can retire;
        // repeated replans must not accumulate every place the camera has visited.
        for(int i=0;i<12;i++)
        {
            var next=Tile(20+i*2,0,1);Apply(w,parent,next,blocked);
            Until(w,()=>Active(w).Contains(next.Key),"bounded new frontier travel");
            Check(w.ActiveTerrainTiles<=64&&w.ResidentTiles<=w.ActiveTerrainTiles+((IDictionary)Get(w,"desiredTiles")).Count&&BalancedDisjoint(w),"frontier travel preserves active/desired residency bound and disjoint coverage");
        }
        UnityEngine.Object.Destroy(root);

        root=new GameObject("balanced neighbor closure fixture");w=root.AddComponent<WorldExplorer>();w.ConnectedTerrainSurface=false;w.UseVoxelLodTerrain=true;w.MaximumTiles=64;w.AsyncPlanning=false;w.MeshBuildsPerFrame=1;w.MeshWorkUnitsPerFrame=2048;w.MeshBuildMilliseconds=8;
        w.ConfigureExternal(new Field(),new SurfaceProfile(enableSnow:false),new WorldPoint(12,12,12),Quaternion.Euler(0,0,0));Call(w,"ClearTerrain");Set(w,"coarseStartup",true);
        var left=Tile(0,0,2);var right=Tile(1,0,2);Apply(w,left,right);Until(w,()=>w.HasActiveCoverage,"adjacent old parents");
        var leftChildren=Enumerable.Range(0,4).SelectMany(z=>Enumerable.Range(0,4).Select(x=>Tile(x,z,0))).ToArray();
        var rightChildren=Enumerable.Range(0,4).SelectMany(z=>Enumerable.Range(4,4).Select(x=>Tile(x,z,0))).ToArray();
        blocked=Tile(10,0,2);Apply(w,leftChildren.Concat(rightChildren).Concat(new[]{blocked}).ToArray());
        Set(w,"availableCommitBytes",3L*1024*1024*1024);Set(w,"nextCommitProbe",double.MaxValue);
        foreach(var tile in rightChildren)Call(w,"DeferVoxelCommit",tile,8,false,1024L*1024*1024);
        Call(w,"DeferVoxelCommit",blocked,8,false,32L*1024*1024*1024);
        Until(w,()=>leftChildren.All(t=>Surfaces(w).Contains(t.Key)),"first dependent subtree is cached");
        for(int i=0;i<4;i++)Tick(w);
        Check(Active(w).Contains(left.Key)&&Active(w).Contains(right.Key)&&!leftChildren.Any(t=>Active(w).Contains(t.Key)),"completed subtree waits for its neighboring closure rather than creating a greater-than2:1 edge");
        Set(w,"availableCommitBytes",8L*1024*1024*1024);
        Until(w,()=>leftChildren.Concat(rightChildren).All(t=>Active(w).Contains(t.Key)),"balanced dependent subtree publication");
        Check(!Active(w).Contains(left.Key)&&!Active(w).Contains(right.Key)&&!Active(w).Contains(blocked.Key)&&w.PendingTiles>0&&BalancedDisjoint(w),"dependent neighboring replacements publish together with no overlap or unbalanced edge while unrelated frontier remains pending");
        UnityEngine.Object.Destroy(root);
    }
    static void CoarseFirstChecks()
    {
        var root=new GameObject("whole-view coarse-first fixture");var w=root.AddComponent<WorldExplorer>();w.ConnectedTerrainSurface=false;
        w.UseVoxelLodTerrain=true;w.MaximumTiles=4096;w.VoxelPlanSplitFactor=18;w.ViewDistanceKm=30;w.FarHorizonDistanceKm=0;w.AsyncPlanning=false;
        w.MeshBuildsPerFrame=8;w.MeshWorkUnitsPerFrame=2048;w.MeshBuildMilliseconds=8;
        w.ConfigureExternal(new Field(),new SurfaceProfile(enableSnow:false),new WorldPoint(12,12,12),Quaternion.Euler(0,0,0));
        var first=((IDictionary)Get(w,"desiredTiles")).Values.Cast<LodTile>().ToArray();
        coarseFirstTiles=first.Length;
        Check(w.StartupCoveragePending&&w.CurrentPlanTileLimit==128&&first.Length<=128&&first.Length>0,"Maximum4096 request starts with at most128 whole-view tiles");
        bool diskCovered=true;
        for(int i=0;i<128;i++)
        {
            double angle=i*Math.PI*2/128,x=12+Math.Cos(angle)*29999,z=12+Math.Sin(angle)*29999;
            diskCovered&=first.Any(t=>x>=t.OriginXMeters&&x<t.OriginXMeters+t.SizeMeters&&z>=t.OriginZMeters&&z<t.OriginZMeters+t.SizeMeters);
        }
        Check(diskCovered&&w.ViewDistanceKm==30&&w.MainCamera.farClipPlane>=54000,"coarse first plan covers the requested30km perimeter without shortening view distance");
        Until(w,()=>w.HasActiveCoverage,"bounded whole-view first coverage");
        var firstActive=new HashSet<TileKey>(Active(w));
        Check(firstActive.Count==first.Length&&firstActive.All(key=>Quads(w,key)==8),"all bounded first-plan meshes actually publish together at8 columns");
        Check(!w.StartupCoveragePending&&w.MaximumTiles==4096&&w.VoxelPlanSplitFactor==18&&w.CurrentPlanTileLimit==4096,"first full coverage restores the exact requested4096 limit and split tuning");
        Call(w,"RequestPlan");
        requestedTilesAfterCoverage=((IDictionary)Get(w,"desiredTiles")).Count;
        Check(((IDictionary)Get(w,"desiredTiles")).Count>128&&Active(w).SetEquals(firstActive)&&w.HasActiveCoverage&&w.PendingTiles>0,"requested larger plan returns while the complete first coverage stays active during replacement");
        UnityEngine.Object.Destroy(root);
    }
    static void StartupMemoryChecks()
    {
        var root=new GameObject("startup memory fixture");var w=root.AddComponent<WorldExplorer>();w.ConnectedTerrainSurface=false;
        w.UseVoxelLodTerrain=true;w.MaximumTiles=64;w.AsyncPlanning=false;w.MeshBuildsPerFrame=1;w.MeshWorkUnitsPerFrame=2048;w.MeshBuildMilliseconds=8;
        w.ConfigureExternal(new Field(),new SurfaceProfile(enableSnow:false),new WorldPoint(12,12,12),Quaternion.Euler(0,0,0));
        Call(w,"ClearTerrain");Set(w,"coarseStartup",true);w.VoxelFullDetailRadius=100;
        Apply(w,Tile(0,0,0),Tile(0,2,5),Tile(1,2,5),Tile(0,-3,5));
        Call(w,"RefreshContactPlan");
        Until(w,()=>w.IsTerrainReadyArea(12,12,2),"local startup floor before distant coverage");
        Check(!w.HasActiveCoverage&&(bool)Get(w,"coarseStartup")&&w.PendingTiles>0,"local contact floor publishes while distant startup coverage remains pending");
        Check(((HashSet<TileKey>)Get(w,"contactDesired")).Count<=16,"startup100m preference is deferred behind a bounded small arrival contact neighborhood");
        // Leave a real completed worker waiting to be polled, then simulate the
        // documented low system-commit condition without allocating huge payloads.
        Tick(w);var task=Get(w,"voxelBuildJob") as System.Threading.Tasks.Task;
        if(task==null){Tick(w);task=Get(w,"voxelBuildJob") as System.Threading.Tasks.Task;}
        if(task!=null)task.Wait(5000);
        Call(w,"EnsureVoxelBuffers",1024,1536);long scratch=w.VoxelScratchBytes;
        int contacts=w.ContactTiles;Set(w,"availableCommitBytes",0L);Set(w,"nextCommitProbe",double.MaxValue);
        Call(w,"AdvanceBuilds");
        Check(w.VoxelMemoryPressureBlocked&&Get(w,"currentBuild")==null&&Get(w,"voxelBuildJob")==null&&w.MeshUploadsLastFrame==0,"commit pause drains completed worker ownership and performs no unsafe upload");
        Check(w.VoxelScratchBytes==0&&w.VoxelPressureScratchReleasedBytes>=scratch&&w.ContactPendingTiles>0&&w.ContactTiles==contacts,"pressure releases reusable scratch while retaining exact pending work and urgent local floor");
        Set(w,"availableCommitBytes",8L*1024*1024*1024);
        Until(w,()=>w.HasActiveCoverage,"startup recovers after commit headroom returns");
        Check(w.IsTerrainReadyArea(12,12,2)&&!w.VoxelMemoryPressureBlocked,"real headroom recovery completes remaining coarse coverage without bypassing readiness");
        UnityEngine.Object.Destroy(root);
    }
#endif
}
