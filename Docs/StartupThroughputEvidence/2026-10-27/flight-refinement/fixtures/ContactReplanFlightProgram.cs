using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using AshenVoxel.Exploration;
using AshenVoxel.WorldScale;
using AshenVoxel.WorldScaleRuntime;
using UnityEngine;

static class Program
{
    sealed class Field:IWorldFieldBounds,IWorldFieldSnapshotProvider
    {public string SourceIdentity=>"contact-replan-flat";public SurfaceSample Sample(double x,double z)=>new SurfaceSample(10,6,6,true);public HeightBounds QueryBounds(double a,double b,double c,double d)=>new HeightBounds(10,10);public IWorldField CaptureSnapshot()=>this;}
    const BindingFlags F=BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public;
    static object Get(object o,string n)=>o.GetType().GetField(n,F).GetValue(o);
    static void Set(object o,string n,object v)=>o.GetType().GetField(n,F).SetValue(o,v);
    static object Call(object o,string n,params object[] a)=>o.GetType().GetMethod(n,F).Invoke(o,a);
    static int checks,ticks;
    static void Check(bool ok,string s){if(!ok)throw new Exception(s);checks++;Console.WriteLine("PASS "+s);}
    static object StartFar(WorldExplorer w,LodTile tile,int n)
    {
        Call(w,"CancelBuild");Call(Get(w,"pending"),"Clear");Call(Get(w,"contactPending"),"Clear");
        object work=Activator.CreateInstance(typeof(WorldExplorer).GetNestedType("Work",BindingFlags.NonPublic),new object[]{tile,(int)Get(w,"revision"),true,false,n,true});
        Call(Get(w,"pending"),"Enqueue",work);Call(w,"AdvanceBuilds");
        object build=Get(w,"currentBuild");if(build==null)throw new Exception("Far work did not remain in flight");return build;
    }
    static void Main(string[] args)
    {
        bool retain=args.Length>1&&args[1]=="retain";string error=null;var root=new GameObject("contact-only crossing repro");var w=root.AddComponent<WorldExplorer>();bool reproduced=false;
        var tile=new LodTile(new TileKey(0,0,7),0,0,3276.8,10,10);
        try
        {
            w.UseVoxelLodTerrain=true;w.AsyncPlanning=false;w.MaximumTiles=64;w.MeshBuildsPerFrame=1;w.VoxelFullDetailRadius=16;w.ShowVisualWater=false;
            w.ConfigureExternal(new Field(),new SurfaceProfile(enableSnow:false),new WorldPoint(12,110,12),Quaternion.Euler(20,90,0));
            var enclosures=new List<LodTile>();for(long z=-1;z<=1;z++)for(long x=-1;x<=1;x++)enclosures.Add(new LodTile(new TileKey(x,z,7),x*3276.8,z*3276.8,3276.8,10,10));
            Call(w,"ClearTerrain");Set(w,"coarseStartup",false);Call(w,"ApplyPlan",new LodPlan{Tiles=enclosures.ToArray(),SourceIdentity="contact-replan-flat"},25.6);Call(w,"RefreshContactPlan");
            int revision=(int)Get(w,"revision");var keys=new HashSet<TileKey>((HashSet<TileKey>)Get(w,"desired"));object build=StartFar(w,tile,128);
            w.SetObserverPose(new WorldPoint(44,110,12),Quaternion.Euler(20,90,0));Call(w,"RefreshContactPlan");
            bool kept=ReferenceEquals(build,Get(w,"currentBuild"));reproduced=!kept;
            Check((int)Get(w,"revision")==revision&&keys.SetEquals((HashSet<TileKey>)Get(w,"desired")),"crossing invokes contact-only replan with unchanged far plan/source revision");
            Check(kept==retain,retain?"still-desired far iterator survives contact-cell crossing":"installed baseline reproduces cancellation of still-desired far iterator");
            if(retain)
            {
                var deadline=DateTime.UtcNow.AddSeconds(10);
                while(!((IDictionary)Get(w,"surfaces")).Contains(tile.Key))
                {
                    w.SetObserverPose(new WorldPoint(44+32*++ticks,110,12),Quaternion.Euler(20,90,0));Time.unscaledTime=ticks*.05f;
                    Call(w,"RefreshContactPlan");Check(ReferenceEquals(build,Get(w,"currentBuild")),"repeated contact boundary preserves same far iterator");
                    Call(w,"AdvanceBuilds");if(DateTime.UtcNow>deadline)throw new Exception("Publication timed out");Thread.Sleep(1);
                }
                Check(w.MeshUploadsLastFrame<=1&&(int)Get(((IDictionary)Get(w,"surfaces"))[tile.Key],"Quads")==128,"retained actual geometry completes and publishes through repeated crossings");
            }
            var controller=root.AddComponent<ExplorationController>();controller.Configure(w,new FieldExplorationCollision(new Field()),new WorldPoint(24.9,108.35,12),capturePointer:false);
            controller.ResetAt(new WorldPoint(24.9,108.35,12),true);Call(w,"RefreshContactPlan");build=StartFar(w,tile,128);
            double start=controller.Feet.X;controller.Advance(new ExplorationMoveInput(1,0,0,true,false),1.0/60);Call(w,"RefreshContactPlan");
            Check(Math.Abs(controller.Feet.X-start-160.0/60)<.001&&ReferenceEquals(build,Get(w,"currentBuild"))==retain,
                retain?"actual default40x4 controller boost preserves far work across boundary":"actual default40x4 controller boost reproduces far cancellation");
            w.SetObserverPose(new WorldPoint(12,110,12),Quaternion.Euler(20,90,0));Call(w,"RefreshContactPlan");build=StartFar(w,tile,128);
            w.SetObserverPose(new WorldPoint(44,11.65,12),Quaternion.Euler(20,90,0));Call(w,"RefreshContactPlan");
            Check(Get(w,"currentBuild")==null&&(bool)Call(w,"HasUrgentContactWork"),"ground arrival immediately preempts far work for physical32m contacts");
            Call(w,"AdvanceBuilds");Check(Get(w,"currentBuild")!=null&&(bool)Get(Get(Get(w,"currentBuild"),"Work"),"Contact"),"next build slot serves urgent full-resolution contact");
            w.SetObserverPose(new WorldPoint(12,110,12),Quaternion.Euler(20,270,0));Call(w,"RefreshContactPlan");build=StartFar(w,tile,128);
            w.RefreshExternalRegion(0,0,100,100);Check(Get(w,"currentBuild")==null&&(int)Get(w,"revision")>revision,"actual source-region invalidation cancels earlier-source work");
            build=StartFar(w,tile,128);Call(w,"ApplyPlan",new LodPlan{Tiles=new[]{new LodTile(new TileKey(-1,-1,7),-3276.8,-3276.8,3276.8,10,10)},SourceIdentity="contact-replan-flat"},25.6);
            Check(Get(w,"currentBuild")==null,"far-plan removal still cancels an undesired tile");
            Call(w,"ClearTerrain");Check(Get(w,"currentBuild")==null&&w.ContactTiles==0&&w.ResidentTiles==0,"clear removes both lanes");
        }
        catch(Exception e){error=e.ToString();Console.WriteLine(error);}
        finally{UnityEngine.Object.Destroy(root);}
        File.WriteAllText(args[0],JsonSerializer.Serialize(new{status=error==null?"PASS":"FAIL",error,checks,expect_retention=retain,installed_cancellation_reproduced=reproduced,contact_crossing_ticks=ticks,
            scope="Real in-flight geometry/iterator and publication; explicitly contact-only replans versus separate far-plan/source invalidation. Numerical Unity substitutes, no native frame/input claim."},new JsonSerializerOptions{WriteIndented=true}));
        if(error!=null)Environment.ExitCode=1;
    }
}
