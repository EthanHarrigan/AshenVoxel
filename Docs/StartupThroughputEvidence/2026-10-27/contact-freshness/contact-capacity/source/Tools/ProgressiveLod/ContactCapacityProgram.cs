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
    sealed class Field:IWorldFieldBounds
    {public string SourceIdentity=>"contact-admission-metadata-fixture";public SurfaceSample Sample(double x,double z)=>new SurfaceSample(10,6,6,true);public HeightBounds QueryBounds(double a,double b,double c,double d)=>new HeightBounds(10,10);}
    const BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
    static int checks;static bool passed=true;
    static object Get(object obj,string name)=>obj.GetType().GetField(name,Private|BindingFlags.Public).GetValue(obj);
    static void Set(object obj,string name,object value)=>obj.GetType().GetField(name,Private|BindingFlags.Public).SetValue(obj,value);
    static object Call(WorldExplorer w,string name,params object[] args)=>typeof(WorldExplorer).GetMethod(name,Private).Invoke(w,args);
    static void Check(bool value,string name){checks++;passed&=value;Console.WriteLine((value?"PASS ":"FAIL ")+name);}
    static int Count(WorldExplorer w,string name){object o=Get(w,name);return (int)o.GetType().GetProperty("Count").GetValue(o);}
    static LodTile Tile(long x)=>new LodTile(new TileKey(x,0,0),x*25.6,0,25.6,10,10);
    static object Work(WorldExplorer w,LodTile tile)=>Activator.CreateInstance(typeof(WorldExplorer).GetNestedType("Work",BindingFlags.NonPublic),new object[]{tile,(int)Get(w,"revision"),true,true,256,false});
    static object Surface(LodTile tile,long byteCount)
    {
        object surface=Activator.CreateInstance(typeof(WorldExplorer).GetNestedType("Surface",BindingFlags.NonPublic),true);
        Set(surface,"Object",new GameObject("contact payload metadata fixture"));Set(surface,"Mesh",new Mesh());
        Set(surface,"Tile",tile);Set(surface,"Quads",256);Set(surface,"VoxelColumns",true);Set(surface,"TriangleCount",(int)(byteCount/60));
        return surface;
    }
    static void Publish(WorldExplorer w,LodTile tile,long bytes)=>Call(w,"PublishContact",Surface(tile,bytes),Work(w,tile));
    static void Main(string[] args)
    {
        var root=new GameObject("contact capacity boundaries");var w=root.AddComponent<WorldExplorer>();
        w.UseVoxelLodTerrain=true;w.MaximumTiles=64;w.AsyncPlanning=false;w.VoxelContactBudgetMB=128;
        w.ConfigureExternal(new Field(),new SurfaceProfile(enableSnow:false),new WorldPoint(1,12,1),Quaternion.Euler(0,0,0));
        Call(w,"ClearTerrain");Set(w,"coarseStartup",false);Call(w,"RefreshContactPlan");
        ((IList)Get(w,"contactPending")).Clear();
        var a=Tile(0);var b=Tile(1);var c=Tile(2);long big=100L*1024*1024,small=20L*1024*1024;
        var desired=(HashSet<TileKey>)Get(w,"contactDesired");
        Check(desired.Contains(a.Key)&&desired.Contains(b.Key)&&desired.Contains(c.Key),"all metadata fixtures belong to the actual stationary contact plan");
        Publish(w,b,big);Publish(w,a,big);Publish(w,c,big);
        Check(w.ContactTiles==1&&Count(w,"contactDeferred")==2&&Count(w,"contactPending")==0,"contact admission retains two requests exceeding the unchanged128MiB allowance");
        long before=w.ContactMeshBytes;Publish(w,b,big);
        Check(w.ContactMeshBytes==before&&Count(w,"contactDeferred")==2&&Count(w,"contactPending")==0,"same-size contact replacement cannot wake requests during temporary release");
        Publish(w,b,small);
        Check(w.ContactMeshBytes<before&&Count(w,"contactDeferred")==0&&Count(w,"contactPending")==2,"completed contact payload reduction wakes measured requests which individually fit");
        while(Count(w,"contactPending")>0)
        {
            object work=Call(w,"NextContactWork");var tile=(LodTile)Get(work,"Tile");
            Call(w,"PublishContact",Surface(tile,big),work);
        }
        Check(w.ContactTiles==2&&Count(w,"contactDeferred")==1&&w.ContactMeshBytes<=128L*1024*1024,"actual admission keeps the second simultaneous wake deferred when shared capacity runs out");
        for(int i=0;i<100;i++)Call(w,"RefreshContactPlan");
        Check(Count(w,"contactPending")==0&&Count(w,"contactDeferred")>0,"unchanged stopped contact plan does not repeatedly retry over-budget work");
        int revision=(int)Get(w,"revision");w.VoxelContactBudgetMB=256;Call(w,"RefreshContactPlan");
        Check((int)Get(w,"revision")==revision&&Count(w,"contactPending")==1&&Count(w,"contactDeferred")==0,"increased effective contact allowance wakes an admissible request without movement or source change");
        if(Count(w,"contactPending")>0)
        {object work=Call(w,"NextContactWork");Call(w,"PublishContact",Surface((LodTile)Get(work,"Tile"),big),work);}
        Check(w.ContactTiles==3&&Count(w,"contactDeferred")==0&&w.ContactMeshBytes<=256L*1024*1024,"woken contact request publishes at full256-column resolution within the new explicit limit");
        int queued=Count(w,"contactPending");for(int i=0;i<50;i++)Call(w,"RefreshContactPlan");
        Check(Count(w,"contactPending")==queued,"settled contact allowance does not generate repeated queue entries");
        UnityEngine.Object.Destroy(root);
        Check(w.ContactMeshBytes==0&&w.ContactTiles==0&&Count(w,"contactDeferred")==0,"contact shutdown releases accounting and deferred records");
        File.WriteAllText(args[0],JsonSerializer.Serialize(new{status=passed?"PASS":"FAIL",checks,
            scope="Actual PublishContact/admission/refresh methods with metadata-only synthetic mesh payload counts under numerical Unity substitutes. No100MiB native buffers are allocated; this validates scheduling/accounting, not geometry or GPU residency."},new JsonSerializerOptions{WriteIndented=true}));
        if(!passed)Environment.ExitCode=1;
    }
}
