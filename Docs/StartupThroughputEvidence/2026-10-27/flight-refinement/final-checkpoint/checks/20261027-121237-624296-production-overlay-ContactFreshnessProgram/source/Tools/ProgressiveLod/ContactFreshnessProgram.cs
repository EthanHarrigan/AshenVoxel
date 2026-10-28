using System;
using System.Collections;
using System.Collections.Generic;
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
    {public double Height=10;public string SourceIdentity=>"contact-freshness-"+Height;public SurfaceSample Sample(double x,double z)=>new SurfaceSample(Height,6,6,true);public HeightBounds QueryBounds(double a,double b,double c,double d)=>new HeightBounds(Height,Height);public IWorldField CaptureSnapshot()=>new Field{Height=Height};}
    const BindingFlags Flags=BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public;
    const long MiB=1024*1024;
    static int checks,frame;static bool passed=true;
    static object Get(object o,string n)=>o.GetType().GetField(n,Flags).GetValue(o);
    static void Set(object o,string n,object v)=>o.GetType().GetField(n,Flags).SetValue(o,v);
    static object Call(WorldExplorer w,string n,params object[] a)=>typeof(WorldExplorer).GetMethod(n,Flags).Invoke(w,a);
    static void Check(bool value,string label){checks++;passed&=value;Console.WriteLine((value?"PASS ":"FAIL ")+label);}
    static IList Pending(WorldExplorer w)=>(IList)Get(w,"contactPending");
    static IDictionary Surfaces(WorldExplorer w)=>(IDictionary)Get(w,"contactSurfaces");
    static IDictionary Deferred(WorldExplorer w)=>(IDictionary)Get(w,"contactDeferred");
    static LodTile Tile(TileKey key)=>new LodTile(key,key.X*25.6,key.Z*25.6,25.6,-12000,48000);
    static object Work(WorldExplorer w,TileKey key)=>Activator.CreateInstance(typeof(WorldExplorer).GetNestedType("Work",BindingFlags.NonPublic),new object[]{Tile(key),(int)Get(w,"revision"),true,true,256,false});
    static object Surface(TileKey key,long bytes,double height)
    {
        object surface=Activator.CreateInstance(typeof(WorldExplorer).GetNestedType("Surface",BindingFlags.NonPublic),true);
        Set(surface,"Object",new GameObject("synthetic contact payload, real height query"));Set(surface,"Mesh",new Mesh());
        Set(surface,"Tile",Tile(key));Set(surface,"Quads",256);Set(surface,"VoxelColumns",true);Set(surface,"TriangleCount",(int)(bytes/60));
        Set(surface,"AnchorY",height);Set(surface,"LocalHeights",new float[256*256]);return surface;
    }
    static void Publish(WorldExplorer w,TileKey key,long bytes,double height,object work=null)=>Call(w,"PublishContact",Surface(key,bytes,height),work??Work(w,key));
    static bool Queued(WorldExplorer w,TileKey key)=>Pending(w).Cast<object>().Any(x=>((LodTile)Get(x,"Tile")).Key.Equals(key));
    static object Take(WorldExplorer w,TileKey key)
    {var queue=Pending(w);for(int i=0;i<queue.Count;i++)if(((LodTile)Get(queue[i],"Tile")).Key.Equals(key)){object result=queue[i];queue.RemoveAt(i);return result;}return null;}
    static void CompleteOthers(WorldExplorer w,TileKey except,TileKey large,double height)
    {
        foreach(object work in Pending(w).Cast<object>().ToArray())
        {var key=((LodTile)Get(work,"Tile")).Key;if(key.Equals(except))continue;Pending(w).Remove(work);Publish(w,key,key.Equals(large)?20*MiB:120,height,work);}
    }
    static void Edit(WorldExplorer w,Field field,double height)
    {field.Height=height;w.RefreshExternalRegion(-20,-20,60,60);Call(w,"RefreshContactPlan");}
    static bool Height(WorldExplorer w,double expected)=>w.TrySampleRenderedHeight(1,1,out double value)&&Math.Abs(value-expected)<1e-6;
    static void Move(WorldExplorer w,double x)
    {w.SetObserverPose(new WorldPoint(x,12,1),Quaternion.Euler(0,0,0));Call(w,"RefreshContactPlan");}
    static void MetadataCases()
    {
        var root=new GameObject("contact freshness bounded metadata fixture");var w=root.AddComponent<WorldExplorer>();var field=new Field();
        try
        {
            w.UseVoxelLodTerrain=true;w.AsyncPlanning=false;w.MaximumTiles=64;w.VoxelFullDetailRadius=32;w.VoxelContactBudgetMB=128;
            w.ConfigureExternal(field,new SurfaceProfile(enableSnow:false),new WorldPoint(1,12,1),Quaternion.Euler(0,0,0));
            Call(w,"ClearTerrain");Set(w,"coarseStartup",false);Call(w,"RefreshContactPlan");
            var a=new TileKey(0,0,0);var b=new TileKey(1,0,0);
            foreach(var key in ((HashSet<TileKey>)Get(w,"contactDesired")).ToArray())Publish(w,key,key.Equals(a)?100*MiB:key.Equals(b)?20*MiB:120,10);
            Pending(w).Clear();Check(Height(w,10),"old contact support is available before source edit");
            Edit(w,field,20);CompleteOthers(w,a,b,20);
            object replacement=Take(w,a);Check(replacement!=null,"source edit queues existing contact replacement");
            if(replacement!=null)Publish(w,a,120*MiB,20,replacement);
            Check(Height(w,10)&&Deferred(w).Contains(a),"denied replacement preserves visible old contact and refresh obligation");
            Move(w,26.6);
            Check(Surfaces(w).Contains(a)&&Height(w,10),"overlap movement retains fallback surface");
            Check(Queued(w,a),"overlap movement retains stale source replacement despite existing surface");
            replacement=Take(w,a);if(replacement!=null)Publish(w,a,120*MiB,20,replacement);
            CompleteOthers(w,a,b,20);
            int pending=Pending(w).Count,deferred=Deferred(w).Count;
            for(int i=0;i<100;i++)Call(w,"RefreshContactPlan");
            Check(Pending(w).Count==pending&&Deferred(w).Count==deferred,"static over-budget refresh does not busy-retry");
            Publish(w,b,120,20);replacement=Take(w,a);
            Check(replacement!=null,"net memory release wakes overlap's retained source replacement");
            if(replacement!=null)Publish(w,a,120*MiB,20,replacement);
            Check(Height(w,20)&&!Deferred(w).Contains(a),"replacement publishes current source after overlap/free-capacity sequence");
            Check(replacement!=null&&(int)Get(Surfaces(w)[a],"SourceRevision")== (int)Get(replacement,"Revision"),"admitted surface stamp equals its originating work revision");

            Edit(w,field,30);CompleteOthers(w,a,b,30);replacement=Take(w,a);if(replacement!=null)Publish(w,a,140*MiB,30,replacement);
            Check(Deferred(w).Contains(a),"next source replacement can defer independently");
            Move(w,2000);Check(!Surfaces(w).Contains(a)&&!Deferred(w).Contains(a),"eviction retires old contact and obsolete deferred accounting");
            Move(w,1);replacement=Take(w,a);Check(replacement!=null,"away/back queues current contact even after eviction");
            if(replacement!=null)Publish(w,a,4*MiB,30,replacement);
            Check(Height(w,30),"return publishes current source height");
            CompleteOthers(w,a,b,30);

            Edit(w,field,40);CompleteOthers(w,a,b,40);replacement=Take(w,a);if(replacement!=null)Publish(w,a,140*MiB,40,replacement);
            w.VoxelFullDetailRadius=48;Call(w,"RefreshContactPlan");
            Check(Queued(w,a),"radius replan preserves stale source replacement just like overlap movement");
            replacement=Take(w,a);if(replacement!=null)Publish(w,a,140*MiB,40,replacement);
            Edit(w,field,50);replacement=Take(w,a);
            Check(replacement!=null&&(int)Get(replacement,"Revision")== (int)Get(w,"revision"),"another source revision replaces deferred work with current revision");
            if(replacement!=null)Publish(w,a,4*MiB,50,replacement);
            Check(Height(w,50),"multiple deferred revisions cannot publish an earlier source as current");
            Check(Surfaces(w).Values.Cast<object>().All(s=>(int)Get(s,"Quads")==256)&&w.ContactMeshBytes<=128*MiB,"resolution and admission budget remain unchanged");
        }
        finally{UnityEngine.Object.Destroy(root);}
        Check(w.ContactTiles==0&&w.ContactMeshBytes==0&&Deferred(w).Count==0,"metadata fixture shutdown clears caches and obligations");
    }
    static void RealWorkerCancellation()
    {
        var root=new GameObject("actual contact worker cancellation fixture");var w=root.AddComponent<WorldExplorer>();var field=new Field();
        try
        {
            w.UseVoxelLodTerrain=true;w.AsyncPlanning=false;w.MaximumTiles=64;w.VoxelFullDetailRadius=16;w.VoxelContactBudgetMB=128;w.MeshBuildsPerFrame=1;
            w.ConfigureExternal(field,new SurfaceProfile(enableSnow:false),new WorldPoint(1,12,1),Quaternion.Euler(0,0,0));
            Call(w,"ClearTerrain");Set(w,"coarseStartup",false);Call(w,"RefreshContactPlan");Call(w,"AdvanceBuilds");
            Check(Get(w,"currentBuild")!=null,"real immutable geometry work starts before cancellation");
            Edit(w,field,20);Check(Get(w,"currentBuild")==null,"source revision cancels old in-flight iterator");
            var end=DateTime.UtcNow.AddSeconds(20);
            while(DateTime.UtcNow<end&&!Height(w,20))
            {Time.unscaledTime=++frame*.02f;Call(w,"AdvanceBuilds");Check(w.MeshUploadsLastFrame<=1,"real publication cap");Thread.Sleep(1);}
            Check(Height(w,20)&&w.VoxelBuildFailures==0,"real canceled source cannot overwrite new 10cm support");
            Check(Surfaces(w).Count>0&&Surfaces(w).Values.Cast<object>().All(s=>(int)Get(s,"SourceRevision")== (int)Get(w,"revision")),"all published contact surfaces carry the current revision after cancellation");
            Check(Surfaces(w).Values.Cast<object>().All(s=>(int)Get(s,"Quads")==256),"real replacement retains256-column contact detail");
        }
        finally{UnityEngine.Object.Destroy(root);}
        Check(Get(w,"voxelBuildJob")==null&&w.ContactTiles==0&&w.VoxelScratchBytes==0,"cancellation/disposal leaves no worker handle or owned scratch");
    }
    static void Main(string[] args)
    {
        string error=null;try{MetadataCases();RealWorkerCancellation();}catch(Exception ex){passed=false;error=ex.ToString();Console.WriteLine(error);}
        File.WriteAllText(args[0],JsonSerializer.Serialize(new{status=passed?"PASS":"FAIL",checks,error,realWorkerTicks=frame,
            scope="Actual contact refresh/admission/publication on synthetic byte-accounting meshes plus a separate real geometry-worker cancellation case. No large native payloads, Unity rendering, full-world performance or peak-RAM guarantee."},new JsonSerializerOptions{WriteIndented=true}));
        if(!passed)Environment.ExitCode=1;
    }
}
