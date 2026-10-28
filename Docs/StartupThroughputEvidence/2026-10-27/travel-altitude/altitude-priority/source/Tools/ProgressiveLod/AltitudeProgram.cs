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

// Focused scheduling experiment, not a game performance benchmark.
static class Program
{
    sealed class Field:IWorldFieldBounds,IWorldFieldSnapshotProvider
    {
        public int Queries;
        public string SourceIdentity=>"altitude-flat10-v1";
        public SurfaceSample Sample(double x,double z)=>new SurfaceSample(10,6,6,true);
        public HeightBounds QueryBounds(double a,double b,double c,double d){Interlocked.Increment(ref Queries);return new HeightBounds(10,10);}
        public IWorldField CaptureSnapshot()=>this;
    }
    sealed class Unbounded:IWorldField
    {public string SourceIdentity=>"unbounded-altitude-fixture";public SurfaceSample Sample(double x,double z)=>new SurfaceSample(10,6,6,true);}
    const BindingFlags Flags=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
    static object Get(object o,string n)=>o.GetType().GetField(n,Flags).GetValue(o);
    static void Set(object o,string n,object v)=>o.GetType().GetField(n,Flags).SetValue(o,v);
    static object Call(WorldExplorer w,string n,params object[] a)=>typeof(WorldExplorer).GetMethod(n,Flags).Invoke(w,a);
    static int checks,frame;
    static bool Candidate=>typeof(WorldExplorer).GetMethod("ContactWorkIsUrgent",Flags)!=null;
    static void Check(bool v,string n){if(!v)throw new Exception(n);checks++;Console.WriteLine("PASS "+n);}
    static LodTile Tile(long x,long z,int level)
    {double size=25.6*(1<<level);return new LodTile(new TileKey(x,z,level),x*size,z*size,size,10,10);}
    static void Tick(WorldExplorer w)
    {
        Time.unscaledTime=++frame*.02f;
        Call(w,"QueueNextVoxelRefinement");Call(w,"AdvanceBuilds");Call(w,"ActivateReadyPlan");Thread.Sleep(1);
    }
    static void Until(WorldExplorer w,Func<bool> condition)
    {var watch=Stopwatch.StartNew();while(!condition()){Tick(w);if(watch.Elapsed.TotalSeconds>25)throw new Exception("Focused altitude fixture timed out");}}
    static object Run(double y)
    {
        var root=new GameObject("bounded contact urgency experiment");var w=root.AddComponent<WorldExplorer>();
        var field=new Field();
        try
        {
            w.UseVoxelLodTerrain=true;w.AsyncPlanning=false;w.MaximumTiles=64;w.MeshBuildsPerFrame=1;w.ShowVisualWater=false;
            w.ConfigureExternal(field,new SurfaceProfile(enableSnow:false),new WorldPoint(12,y,12),Quaternion.Euler(20,0,0));
            Call(w,"ClearTerrain");Set(w,"coarseStartup",true);
            var tiles=new[]{Tile(0,0,5),Tile(-1,0,5),Tile(0,-1,5),Tile(-1,-1,5)};
            Call(w,"ApplyPlan",new LodPlan{Tiles=tiles,SourceIdentity=field.SourceIdentity},25.6);
            Until(w,()=>w.HasActiveCoverage);
            Call(w,"RefreshContactPlan");
            int pending=((ICollection)Get(w,"contactPending")).Count;
            bool urgent=(bool)Call(w,"HasUrgentContactWork");
            Check(pending==98,"same 98 pending full-resolution contacts at eye height "+y);
            Check(urgent==(!Candidate||y<42.1),"altitude urgency policy at eye height "+y);
            if(Candidate&&y==1010)
            {
                var contact=new LodTile(new TileKey(0,0,0),0,0,25.6,-12000,48000);
                var negative=new LodTile(new TileKey(-1,-1,0),-25.6,-25.6,25.6,-12000,48000);
                Check(!(bool)Call(w,"ContactWorkIsUrgent",negative),"negative-grid ancestor bounds are reused");
                w.SetObserverPose(new WorldPoint(12,11.65,12),Quaternion.Euler(20,0,0));
                Check((bool)Call(w,"HasUrgentContactWork"),"immediate descent restores urgency without rebuilding contact plan");
                w.SetObserverPose(new WorldPoint(12,1010,12),Quaternion.Euler(20,0,0));
                int revision=(int)Get(w,"revision");Set(w,"revision",revision+1);
                Check((bool)Call(w,"HasUrgentContactWork"),"stale source revision keeps arrival-safe urgency");Set(w,"revision",revision);
                object original=Get(w,"field");Set(w,"field",new Unbounded());
                Check((bool)Call(w,"HasUrgentContactWork"),"unbounded source keeps arrival-safe urgency");Set(w,"field",original);
                var desired=(IDictionary)Get(w,"desiredTiles");var saved=desired.Values.Cast<LodTile>().ToArray();desired.Clear();
                Check((bool)Call(w,"ContactWorkIsUrgent",contact),"missing enclosure keeps arrival-safe urgency");
                foreach(var tile in saved)desired.Add(tile.Key,tile);
                var urgentCall=(Func<bool>)Delegate.CreateDelegate(typeof(Func<bool>),w,typeof(WorldExplorer).GetMethod("HasUrgentContactWork",Flags));
                for(int i=0;i<100;i++)urgentCall();
                int queries=field.Queries;long bytes=GC.GetAllocatedBytesForCurrentThread();var timer=Stopwatch.StartNew();
                for(int i=0;i<10000;i++)urgentCall();
                double elapsed=timer.Elapsed.TotalMilliseconds;long allocated=GC.GetAllocatedBytesForCurrentThread()-bytes;
                // Stopwatch.StartNew itself allocates40 bytes; priority calls allocate none.
                Check(field.Queries==queries&&allocated<=64,"10000 urgency scans add no terrain queries or per-call allocations");
                Console.WriteLine("URGENCY_SCAN_10000_MS "+elapsed+" bytes "+allocated);
            }
            int before=frame;var clock=Stopwatch.StartNew();
            Until(w,()=>((IDictionary)Get(w,"surfaces")).Values.Cast<object>().Any(s=>(int)Get(s,"Quads")>8));
            int contactsBeforeQuality=w.ContactTiles;
            Check(contactsBeforeQuality==(Candidate&&y>=42.1?2:9),"first far improvement follows expected contact priority at eye height "+y);
            Check(((IDictionary)Get(w,"contactSurfaces")).Values.Cast<object>().All(s=>(int)Get(s,"Quads")==256),"published contacts retain10cm at eye height "+y);
            return new{eyeHeight=y,altitude=y-10,pendingContacts=pending,urgent,contactsBeforeFirstFarQuality=contactsBeforeQuality,
                firstFarQualityTicks=frame-before,cpuWallMs=clock.Elapsed.TotalMilliseconds,contactBytes=w.ContactMeshBytes,
                pendingAfterFirstQuality=w.ContactPendingTiles};
        }
        finally{UnityEngine.Object.Destroy(root);}
    }
    static void Main(string[] args)
    {
        var cases=new List<object>();string status="PASS",error=null;
        try{foreach(double y in new[]{11.65,110,1010})cases.Add(Run(y));}
        catch(Exception ex){status="FAIL";error=ex.ToString();Console.WriteLine(error);}
        File.WriteAllText(args[0],JsonSerializer.Serialize(new{status,error,checks,candidate=Candidate,cases,
            scope="Four complete coarse tiles,98 actual pending contact tiles, fixed flat source, real geometry worker and publication policy with numerical Unity substitutes. Same geometry/budgets; priority experiment only. CPU wall times are not native Start latency or FPS."},new JsonSerializerOptions{WriteIndented=true}));
        if(status!="PASS")Environment.ExitCode=1;
    }
}
