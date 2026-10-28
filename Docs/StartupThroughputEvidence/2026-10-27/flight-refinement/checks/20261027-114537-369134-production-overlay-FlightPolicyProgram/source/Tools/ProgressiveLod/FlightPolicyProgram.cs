using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.Json;
using AshenVoxel.WorldScale;
using AshenVoxel.WorldScaleRuntime;
using UnityEngine;

static class Program
{
    sealed class Field:IWorldFieldBounds,IWorldFieldSnapshotProvider
    {public int Queries;public string SourceIdentity=>"flight-policy-flat";public SurfaceSample Sample(double x,double z)=>new SurfaceSample(10,6,6,true);public HeightBounds QueryBounds(double a,double b,double c,double d){Queries++;return new HeightBounds(10,10);}public IWorldField CaptureSnapshot()=>this;}
    sealed class Unknown:IWorldField{public string SourceIdentity=>"unknown";public SurfaceSample Sample(double x,double z)=>new SurfaceSample(10,6,6,true);}
    const BindingFlags F=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
    static object Get(object o,string n)=>o.GetType().GetField(n,F).GetValue(o);
    static void Set(object o,string n,object v)=>o.GetType().GetField(n,F).SetValue(o,v);
    static object Call(object o,string n,params object[] a)=>o.GetType().GetMethod(n,F).Invoke(o,a);
    static int checks,frame;
    static void Check(bool ok,string s){if(!ok)throw new Exception(s);checks++;Console.WriteLine("PASS "+s);}
    static void Motion(WorldExplorer w,double vx,double vy=0,double vz=0,int ticks=1)
    {for(int i=0;i<ticks;i++){var p=w.GlobalCamera;w.SetObserverPose(new WorldPoint(p.X+vx*.05,p.Y+vy*.05,p.Z+vz*.05),Quaternion.Euler(20,90,0));Time.unscaledTime=++frame*.05f;Call(w,"UpdateVoxelTravelMotion");}}
    static void Reset(WorldExplorer w,double y=1010)
    {w.SetObserverPose(new WorldPoint(12,y,12),Quaternion.Euler(20,90,0));Call(w,"ResetVoxelTravelMotion");Motion(w,0);}
    static void Fast(WorldExplorer w){Motion(w,640,ticks:8);Check(w.VoxelTravelDetailDeferred,"sustained640m/s elevated travel enters deferral");}
    static object Work(WorldExplorer w,LodTile t,int quads,bool progressive=true)=>Activator.CreateInstance(typeof(WorldExplorer).GetNestedType("Work",BindingFlags.NonPublic),new object[]{t,(int)Get(w,"revision"),true,false,quads,progressive});
    static void AddSurface(WorldExplorer w,LodTile t,int n)
    {
        object s=Activator.CreateInstance(typeof(WorldExplorer).GetNestedType("Surface",BindingFlags.NonPublic),true);
        Set(s,"Object",new GameObject("queue policy metadata"));Set(s,"Mesh",new Mesh());Set(s,"Tile",t);Set(s,"Quads",n);Set(s,"VoxelColumns",true);
        ((IDictionary)Get(w,"surfaces")).Add(t.Key,s);((HashSet<TileKey>)Get(w,"active")).Add(t.Key);
    }
    static void Main(string[] args)
    {
        string error=null;var root=new GameObject("flight policy isolated");var w=root.AddComponent<WorldExplorer>();var source=new Field();
        try
        {
            w.UseVoxelLodTerrain=true;w.AsyncPlanning=false;w.MaximumTiles=64;w.ShowVisualWater=false;
            w.ConfigureExternal(source,new SurfaceProfile(enableSnow:false),new WorldPoint(12,1010,12),Quaternion.Euler(20,90,0));
            Set(w,"coarseStartup",false);Set(w,"planActivationPending",false);Call(Get(w,"pending"),"Clear");Call(w,"RefreshContactPlan");
            Reset(w);Motion(w,80,ticks:12);Check(!w.VoxelTravelDetailDeferred,"ordinary80m/s motion remains unchanged");
            Motion(w,640,ticks:3);Check(!w.VoxelTravelDetailDeferred,"brief speed spikes do not enter deferral");Motion(w,640,ticks:5);Check(w.VoxelTravelDetailDeferred,"entry hysteresis waits for sustained fast travel");
            Motion(w,120,ticks:10);Check(w.VoxelTravelDetailDeferred,"96–160m/s band retains the existing state");
            Motion(w,0,ticks:3);Check(w.VoxelTravelDetailDeferred,"brief pause does not flap detail scheduling");Motion(w,0,ticks:5);
            Check(!w.VoxelTravelDetailDeferred&&(bool)Get(w,"voxelRefinementScanPending")&&(bool)Get(w,"planForced"),"stationary destination wakes refinement and requests current plan within0.35s virtual time");
            Reset(w);Fast(w);Motion(w,-640);Check(!w.VoxelTravelDetailDeferred,"direction reversal restores detail immediately");
            Motion(w,-640,ticks:8);Check(!w.VoxelTravelDetailDeferred,"reversal recovery hold prevents immediate reentry");
            Reset(w);Fast(w);Motion(w,640,-20);Check(!w.VoxelTravelDetailDeferred,"descent releases deferred contacts/detail immediately");
            Reset(w,11.65);Motion(w,640,ticks:12);Check(!w.VoxelTravelDetailDeferred,"fast ground movement never defers safe contact work");
            Reset(w);Fast(w);int revision=(int)Get(w,"revision");Set(w,"revision",revision+1);Motion(w,640);Check(!w.VoxelTravelDetailDeferred,"stale planner bounds disable deferral");Set(w,"revision",revision);
            Reset(w);Fast(w);object field=Get(w,"field");Set(w,"field",new Unknown());Motion(w,640);Check(!w.VoxelTravelDetailDeferred,"unknown terrain enclosure disables deferral");Set(w,"field",field);
            Reset(w);Fast(w);double beforeX=w.GlobalCamera.X;Call(w,"Rebase",true);Motion(w,640);
            Check(w.VoxelTravelDetailDeferred&&Math.Abs((double)Get(w,"voxelMotionX")-640)<.1&&w.GlobalCamera.X>beforeX,"floating-origin rebase does not alter global velocity");
            Reset(w);Call(w,"Rebase",true);Motion(w,0,ticks:10);Check(!w.VoxelTravelDetailDeferred,"stationary rebases cannot create false high-speed travel");
            Reset(w);Fast(w);Time.unscaledTime+=2;Call(w,"UpdateVoxelTravelMotion");Check(!w.VoxelTravelDetailDeferred,"long sampling gap fails open to normal detail work");

            Reset(w);Fast(w);var a=new LodTile(new TileKey(0,0,5),0,0,819.2,10,10);var b=new LodTile(new TileKey(1,0,5),819.2,0,819.2,10,10);
            var tiles=(IDictionary)Get(w,"desiredTiles");tiles.Clear();tiles.Add(a.Key,a);tiles.Add(b.Key,b);
            var desired=(HashSet<TileKey>)Get(w,"desired");desired.Clear();desired.Add(a.Key);desired.Add(b.Key);AddSurface(w,a,32);AddSurface(w,b,8);
            object queue=Get(w,"pending");Call(queue,"Clear");Call(Get(w,"contactPending"),"Clear");Set(w,"voxelRefinementScanPending",true);
            object held=Work(w,a,64);Call(queue,"Enqueue",held);Call(w,"QueueNextVoxelRefinement");
            Check(((ICollection)queue).Count==2,"held fine-detail request does not block a32-column coverage improvement");
            object[] output={null};bool found=(bool)typeof(WorldExplorer).GetMethod("TryNextWork",F).Invoke(w,output);
            Check(found&&(int)Get(output[0],"Quads")==32&&((ICollection)queue).Count==1,"existing queue selects eligible32-column work while retaining finer request");
            output[0]=null;found=(bool)typeof(WorldExplorer).GetMethod("TryNextWork",F).Invoke(w,output);
            Check(!found&&((ICollection)queue).Count==1,"only-held queue backs off without discarding its request");
            Check(!(bool)Call(w,"HoldVoxelDetailWork",Work(w,a,8,false)),"coarse coverage is always eligible");
            ((HashSet<TileKey>)Get(w,"externallyDirty")).Add(a.Key);Check(!(bool)Call(w,"HoldVoxelDetailWork",held),"source refresh bypasses travel deferral");
            ((HashSet<TileKey>)Get(w,"externallyDirty")).Clear();Call(w,"SetVoxelTravelDeferral",false);output[0]=null;
            found=(bool)typeof(WorldExplorer).GetMethod("TryNextWork",F).Invoke(w,output);Check(found&&(int)Get(output[0],"Quads")==64,"destination recovery resumes the retained exact quality request");
            Call(w,"ClearTerrain");Check(!w.VoxelTravelDetailDeferred&&(double)Get(w,"voxelMotionTime")<0&&w.ResidentTiles==0,"clear/shutdown resets motion state and owned queues");
        }
        catch(Exception e){error=e.ToString();Console.WriteLine(error);}
        finally{UnityEngine.Object.Destroy(root);}
        File.WriteAllText(args[0],JsonSerializer.Serialize(new{status=error==null?"PASS":"FAIL",error,checks,
            scope="Focused motion hysteresis, conservative safety and queue ownership with numerical Unity substitutes and metadata surfaces. Virtual .05s ticks; no game timing/native memory or visual claim."},new JsonSerializerOptions{WriteIndented=true}));
        if(error!=null)Environment.ExitCode=1;
    }
}
