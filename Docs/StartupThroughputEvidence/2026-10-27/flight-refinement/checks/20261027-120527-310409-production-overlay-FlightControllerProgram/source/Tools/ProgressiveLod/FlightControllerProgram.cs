using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.Json;
using AshenVoxel.Exploration;
using AshenVoxel.WorldScale;
using AshenVoxel.WorldScaleRuntime;
using UnityEngine;

static class Program
{
    sealed class Field:IWorldFieldBounds
    {public string SourceIdentity=>"actual-controller-flight";public SurfaceSample Sample(double x,double z)=>new SurfaceSample(10,6,6,true);public HeightBounds QueryBounds(double a,double b,double c,double d)=>new HeightBounds(10,10);}
    const BindingFlags F=BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public;
    static object Call(object o,string n,params object[] a)=>o.GetType().GetMethod(n,F).Invoke(o,a);
    static void Set(object o,string n,object v)=>o.GetType().GetField(n,F).SetValue(o,v);
    static double Velocity(WorldExplorer w)=>(double)typeof(WorldExplorer).GetField("voxelMotionX",F).GetValue(w);
    static void Main(string[] args)
    {
        string error=null;int checks=0;var rows=new List<object>();var failures=new List<string>();
        foreach(int hz in new[]{15,30,60,144,240})
        {
            var root=new GameObject("real controller default boost");var w=root.AddComponent<WorldExplorer>();var c=root.AddComponent<ExplorationController>();var field=new Field();
            try
            {
                Time.unscaledTime=0;w.UseVoxelLodTerrain=true;w.AsyncPlanning=false;w.MaximumTiles=64;w.ShowVisualWater=false;
                w.ConfigureExternal(field,new SurfaceProfile(enableSnow:false),new WorldPoint(12,110,12),Quaternion.Euler(20,90,0));
                Set(w,"coarseStartup",false);c.Configure(w,new FieldExplorationCollision(field),new WorldPoint(12,108.35,12),new ExplorationMovementSettings(),capturePointer:false);
                c.ResetAt(new WorldPoint(12,108.35,12),true);Call(w,"UpdateVoxelTravelMotion");
                int enter=-1,held=0,threshold=0,transitions=0;bool previous=false;double start=c.Feet.X;
                for(int i=1;i<=hz*2;i++)
                {
                    Time.unscaledTime=(float)(i/(double)hz);c.Advance(new ExplorationMoveInput(1,0,0,true,false),1.0/hz);Call(w,"UpdateVoxelTravelMotion");
                    bool deferred=w.VoxelTravelDetailDeferred;if(deferred){held++;if(enter<0)enter=i;}if(deferred!=previous)transitions++;previous=deferred;
                    if(Velocity(w)>=160)threshold++;
                }
                double speed=(c.Feet.X-start)/2;int release=-1;
                for(int i=1;i<=hz;i++)
                {Time.unscaledTime=2+(float)(i/(double)hz);c.Advance(new ExplorationMoveInput(0,0,0,false,false),1.0/hz);Call(w,"UpdateVoxelTravelMotion");if(release<0&&!w.VoxelTravelDetailDeferred)release=i;}
                bool pass=enter>0&&enter/(double)hz<=.5&&release>0&&release/(double)hz<=.4&&transitions==1&&Math.Abs(speed-160)<.01;
                rows.Add(new{hz,default_flight_speed=c.Motor.Settings.FlightSpeed,default_boost=c.Motor.Settings.FlightBoost,actual_meters_per_second=speed,
                    entry_tick=enter,entry_virtual_seconds=enter/(double)hz,held_ticks=held,transitions,measured_velocity_at_least160_ticks=threshold,release_tick=release,pass});
                if(pass)checks++;else failures.Add("Default controller boost/recovery failed at "+hz+" Hz");
                c.ResetAt(new WorldPoint(12,10,12),false);Call(w,"UpdateVoxelTravelMotion");
                for(int i=1;i<=hz;i++){Time.unscaledTime=3+(float)(i/(double)hz);c.Advance(new ExplorationMoveInput(1,0,0,true,false),1.0/hz);Call(w,"UpdateVoxelTravelMotion");if(w.VoxelTravelDetailDeferred)throw new Exception("Ground sprint entered transit");}
                checks++;Console.WriteLine(hz+"Hz boost entry "+enter+" ticks; speed "+speed.ToString("F4")+"; pass="+pass);
            }
            catch(Exception e){failures.Add(e.ToString());}
            finally{UnityEngine.Object.Destroy(root);}
        }
        if(failures.Count>0)error=string.Join("\n",failures);
        File.WriteAllText(args[0],JsonSerializer.Serialize(new{status=error==null?"PASS":"FAIL",error,checks,rows,scope="Actual ExplorationController.Advance and motor with default40x4 boost, clear flat source,15/30/60/144/240Hz virtual input. No native input/FPS latency claim."},new JsonSerializerOptions{WriteIndented=true}));
        if(error!=null)Environment.ExitCode=1;
    }
}
