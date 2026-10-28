using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
    {
        public string SourceIdentity=>"budget-policy-flat";
        public SurfaceSample Sample(double x,double z)=>new SurfaceSample(10,6,6,true);
        public HeightBounds QueryBounds(double a,double b,double c,double d)=>new HeightBounds(10,10);
        public IWorldField CaptureSnapshot()=>this;
    }
    const BindingFlags Flags=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
    const long MiB=1048576,Reserve=668160;
    static int checks;
    static object Get(object o,string name)=>o.GetType().GetField(name,Flags).GetValue(o);
    static void Set(object o,string name,object value)=>o.GetType().GetField(name,Flags).SetValue(o,value);
    static object Call(object o,string name,params object[] args)=>o.GetType().GetMethod(name,Flags).Invoke(o,args);
    static void Check(bool condition,string message){if(!condition)throw new Exception(message);checks++;Console.WriteLine("PASS "+message);}
    static IDictionary Dict(WorldExplorer w,string name)=>(IDictionary)Get(w,name);
    static IList Contacts(WorldExplorer w)=>(IList)Get(w,"contactPending");
    static LodTile Tile(long x,int level=0){double size=25.6*(1L<<level);return new LodTile(new TileKey(x,0,level),x*size,0,size,10,10);}
    static object Work(WorldExplorer w,LodTile tile,bool contact,int quads)=>Activator.CreateInstance(typeof(WorldExplorer).GetNestedType("Work",BindingFlags.NonPublic),new object[]{tile,(int)Get(w,"revision"),true,contact,quads,false});
    static object Surface(WorldExplorer w,LodTile tile,long bytes,int quads)
    {
        object s=Activator.CreateInstance(typeof(WorldExplorer).GetNestedType("Surface",BindingFlags.NonPublic),true);
        Set(s,"Object",new GameObject("budget metadata payload"));Set(s,"Mesh",new Mesh());Set(s,"Tile",tile);Set(s,"VoxelColumns",true);Set(s,"Quads",quads);
        Set(s,"TriangleCount",(int)(bytes/120)*2);Set(s,"SourceRevision",(int)Get(w,"revision"));Set(s,"LocalHeights",new float[quads*quads]);Set(s,"AnchorY",10d);return s;
    }
    static long Bytes(object s)=>(long)(int)Get(s,"TriangleCount")*60;
    static void AddFar(WorldExplorer w,LodTile tile,long bytes,int quads,bool active=true)
    {
        var s=Surface(w,tile,bytes,quads);Dict(w,"surfaces").Add(tile.Key,s);
        ((HashSet<TileKey>)Get(w,"desired")).Add(tile.Key);((Dictionary<TileKey,LodTile>)Get(w,"desiredTiles"))[tile.Key]=tile;
        if(active)((HashSet<TileKey>)Get(w,"active")).Add(tile.Key);
        Set(w,"<TerrainResidentMeshBytes>k__BackingField",w.TerrainResidentMeshBytes+Bytes(s));Set(w,"voxelDetailMeshBytes",w.TerrainDetailMeshBytes+Math.Max(0,Bytes(s)-Reserve));
    }
    static void Publish(WorldExplorer w,LodTile tile,long bytes)=>Call(w,"PublishContact",Surface(w,tile,bytes,256),Work(w,tile,true,256));
    static WorldExplorer Make(out GameObject root)
    {
        root=new GameObject("isolated budget policy");var w=root.AddComponent<WorldExplorer>();w.UseVoxelLodTerrain=true;w.AsyncPlanning=false;w.MaximumTiles=64;w.VoxelFullDetailRadius=16;w.VoxelDetailBudgetMB=64;w.VoxelContactBudgetMB=256;
        w.ConfigureExternal(new Field(),new SurfaceProfile(enableSnow:false),new WorldPoint(1,12,1),Quaternion.Euler(0,0,0));Call(w,"ClearTerrain");Set(w,"coarseStartup",false);Call(w,"RefreshContactPlan");Contacts(w).Clear();return w;
    }
    static void PreBoot()
    {
        var root=new GameObject("settings pass-through before configuration");var w=root.AddComponent<WorldExplorer>();var boot=root.AddComponent<ExplorationBootstrap>();var settings=ScriptableObject.CreateInstance<ExplorationSettings>();
        try
        {
            Check(settings.VoxelDetailBudgetMB==1536&&settings.VoxelContactBudgetMB==1024&&w.VoxelDetailBudgetMB==1536&&w.VoxelContactBudgetMB==1024,"shipping default quotas unchanged");
            settings.VoxelDetailBudgetMB=64;settings.VoxelContactBudgetMB=128;Set(boot,"<ActiveSettings>k__BackingField",settings);Set(boot,"<Vista>k__BackingField",w);
            Call(boot,"ApplyVistaSettings",false);
            Check(w.TerrainDetailLimitBytes==64*MiB&&w.ContactMeshLimitBytes==128*MiB&&Get(w,"field")==null&&w.ResidentTiles==0,"bootstrap passes quotas before field/plan/build allocation");
            string identity=settings.Surface().Identity;settings.VoxelDetailBudgetMB=1536;settings.VoxelContactBudgetMB=1024;Check(identity==settings.Surface().Identity,"cache budgets do not change source identity");
            w.VoxelDetailBudgetMB=-1;w.VoxelContactBudgetMB=-1;Check(w.TerrainDetailLimitBytes==64*MiB&&w.ContactMeshLimitBytes==128*MiB,"lower effective clamps are stable");
            w.VoxelDetailBudgetMB=int.MaxValue;w.VoxelContactBudgetMB=int.MaxValue;Check(w.TerrainDetailLimitBytes==1536*MiB&&w.ContactMeshLimitBytes==2048*MiB,"upper effective clamps are stable");
        }
        finally{UnityEngine.Object.Destroy(root);}
    }
    static void FarPolicy()
    {
        var w=Make(out var root);
        try
        {
            var a=Tile(0,2);var b=Tile(1,2);var c=Tile(2,2);AddFar(w,a,80*MiB,256);AddFar(w,b,80*MiB,256);
            Check(w.TerrainDetailDebtBytes>0&&w.TerrainBudgetReductionPending,"lower quota exposes retained detail debt");
            Check((bool)Call(w,"AdmitVoxelMesh",c,8,Reserve),"new topology coarse key remains admissible while detail debt exists");
            Check((bool)Call(w,"AdmitVoxelMesh",a,128,60*MiB),"strictly smaller replacement can repay debt without requiring all other tiles to fit first");
            Check(!(bool)Call(w,"AdmitVoxelMesh",c,256,90*MiB)&&w.VoxelRefinementDeferredTiles==1,"positive new detail above quota is deferred");
            // Remove test-only c request: c is not in the desired topology.
            Dict(w,"voxelDeferredQuads").Clear();Call(w,"RefreshVoxelBudget");
            Check(((ICollection)Get(w,"pending")).Count==1&&Dict(w,"surfaces").Count==2&&((GameObject)Get(Dict(w,"surfaces")[b.Key],"Object")).activeSelf,"coarsening queues one replacement and retains visible old coverage");
            var old=Dict(w,"surfaces")[b.Key];var deadline=DateTime.UtcNow.AddSeconds(15);int ticks=0;bool sameVisible=true;
            while(ReferenceEquals(old,Dict(w,"surfaces")[b.Key])&&DateTime.UtcNow<deadline)
            {Call(w,"AdvanceBuilds");ticks++;if(ReferenceEquals(old,Dict(w,"surfaces")[b.Key]))sameVisible&=((GameObject)Get(old,"Object")).activeSelf;Thread.Sleep(1);}
            Check(sameVisible&&!ReferenceEquals(old,Dict(w,"surfaces")[b.Key])&&(int)Get(Dict(w,"surfaces")[b.Key],"Quads")==8,"real worker swaps coarse replacement without retiring old coverage early");
            deadline=DateTime.UtcNow.AddSeconds(15);while(w.TerrainDetailDebtBytes>0&&DateTime.UtcNow<deadline){Call(w,"AdvanceBuilds");Thread.Sleep(1);}
            Check(w.TerrainDetailDebtBytes==0&&Dict(w,"surfaces").Count==2&&((HashSet<TileKey>)Get(w,"active")).Count==2,"bounded coarsening converges below quota without dropping active keys");
            var desired=(HashSet<TileKey>)Get(w,"desired");desired.Add(c.Key);((Dictionary<TileKey,LodTile>)Get(w,"desiredTiles"))[c.Key]=c;
            Check(!(bool)Call(w,"AdmitVoxelMesh",c,256,90*MiB),"larger demand stays deferred at settled low quota");
            for(int i=0;i<30;i++)Call(w,"RefreshVoxelBudget");Check(((ICollection)Get(w,"pending")).Count==0&&w.VoxelRefinementDeferredTiles==1,"static low quota does not busy-retry deferred detail");
            w.VoxelDetailBudgetMB=128;Call(w,"RefreshVoxelBudget");Check(((ICollection)Get(w,"pending")).Count==1&&w.VoxelRefinementDeferredTiles==0,"increasing quota wakes eligible far demand without moving");
        }
        finally{UnityEngine.Object.Destroy(root);}
        Check(w.TerrainResidentMeshBytes==0&&w.TerrainDetailMeshBytes==0&&!w.TerrainBudgetReductionPending,"far shutdown clears owned accounting and debt");
    }
    static void ContactPolicy()
    {
        var w=Make(out var root);
        try
        {
            var a=Tile(0);var b=Tile(1);var cover=Tile(0,1);Publish(w,a,90*MiB);Publish(w,b,90*MiB);AddFar(w,cover,120,8,false);
            w.VoxelContactBudgetMB=128;Call(w,"RefreshContactPlan");
            Check(w.ContactTiles==2&&w.ContactBudgetDebtBytes>0&&w.TerrainBudgetReductionPending,"inactive far cache cannot justify contact retirement or hide debt");
            ((HashSet<TileKey>)Get(w,"active")).Add(cover.Key);Call(w,"RefreshContactPlan");
            Check(!(bool)Call(w,"HasActiveFarCoverage",Tile(-1).Key),"positive parent cannot cover an adjacent negative contact key");
            AddFar(w,Tile(-1,1),120,8);Check((bool)Call(w,"HasActiveFarCoverage",Tile(-1).Key),"negative contact ancestor uses floor division and covers the entire aligned tile");
            Check(w.ContactTiles==1&&Dict(w,"contactSurfaces").Contains(a.Key)&&!Dict(w,"contactSurfaces").Contains(b.Key)&&w.ContactBudgetDebtBytes==0,"active aligned far coverage permits farthest contact retirement");
            Check(Dict(w,"contactDeferred").Contains(b.Key)&&Contacts(w).Count==0,"retired desired contact retains its measured obligation without immediate retry");
            w.VoxelContactBudgetMB=256;Call(w,"RefreshContactPlan");
            Check(Contacts(w).Count==1&&!Dict(w,"contactDeferred").Contains(b.Key),"contact allowance increase wakes the retired full-detail request");
            object work=Contacts(w)[0];Contacts(w).Clear();Call(w,"PublishContact",Surface(w,b,90*MiB,256),work);
            Check((int)Get(Dict(w,"contactSurfaces")[b.Key],"SourceRevision")== (int)Get(work,"Revision")&&w.ContactTiles==2,"contact re-admission retains10cm detail and originating source revision");
        }
        finally{UnityEngine.Object.Destroy(root);}
    }
    static void NewTopologyDuringDebt()
    {
        var w=Make(out var root);
        try
        {
            var a=Tile(0,2);var b=Tile(1,2);var c=Tile(4,2);var d=Tile(5,2);AddFar(w,a,80*MiB,256);AddFar(w,b,80*MiB,256);
            Call(w,"ApplyPlan",new LodPlan{Tiles=new[]{c,d},SourceIdentity="replacement topology while over quota"},25.6);
            var active=(HashSet<TileKey>)Get(w,"active");Check(active.Contains(a.Key)&&w.TerrainDetailDebtBytes>0,"new plan starts with old complete coverage and explicit debt");
            bool complete=true;var end=DateTime.UtcNow.AddSeconds(15);
            while(!active.Contains(c.Key)&&DateTime.UtcNow<end)
            {Call(w,"AdvanceBuilds");complete&=active.Count>0&&active.All(k=>Dict(w,"surfaces").Contains(k));Call(w,"ActivateReadyPlan");Thread.Sleep(1);}
            Check(complete&&active.SetEquals(new[]{c.Key,d.Key}),"new coarse topology publishes completely while previous detail exceeds quota");
            Check(w.TerrainDetailDebtBytes==0&&!Dict(w,"surfaces").Contains(a.Key)&&!Dict(w,"surfaces").Contains(b.Key),"topology swap retires obsolete debt without holes");
        }
        finally{UnityEngine.Object.Destroy(root);}
    }
    static void FarExpansion()
    {
        var w=Make(out var root);
        try
        {
            AddFar(w,Tile(0),40*MiB,256);AddFar(w,Tile(1),40*MiB,256);var c=Tile(2);w.VoxelDetailBudgetMB=128;
            using(var steps=(IEnumerator<object>)Call(w,"BuildVoxelSurfaceSteps",c,256,false))
            {
                var end=DateTime.UtcNow.AddSeconds(15);bool expanded=false,published=false;
                while(steps.MoveNext())
                {published|=steps.Current!=null;if(w.VoxelScratchBytes>0&&!expanded){expanded=true;w.VoxelDetailBudgetMB=64;}if(DateTime.UtcNow>end)throw new Exception("far quota-change timeout");Thread.Sleep(1);}
                Check(expanded&&!published&&Dict(w,"voxelDeferredQuads").Contains(c.Key),"far quota lowered during expansion rejects new charge before mesh upload");
                Check(Dict(w,"surfaces").Count==2&&w.TerrainDetailDebtBytes>0,"rejected in-flight far work preserves prior coverage and truthful debt");
            }
        }
        finally{UnityEngine.Object.Destroy(root);}
    }
    static void ContactExpansion()
    {
        var w=Make(out var root);
        try
        {
            var a=Tile(0);var b=Tile(1);var c=Tile(-1);Publish(w,a,64*MiB);Publish(w,b,64*MiB);w.VoxelContactBudgetMB=128;
            using(var steps=(IEnumerator<object>)Call(w,"BuildVoxelSurfaceSteps",c,256,true))
            {
                var deadline=DateTime.UtcNow.AddSeconds(15);bool published=false;
                while(steps.MoveNext()){published|=steps.Current!=null;if(DateTime.UtcNow>deadline)throw new Exception("contact pre-expansion timeout");Thread.Sleep(1);}
                Check(!published&&w.VoxelScratchBytes==0&&Dict(w,"contactDeferred").Contains(c.Key),"over-budget real contact geometry is rejected before scratch expansion or mesh publication");
            }
            Dict(w,"contactDeferred").Clear();w.VoxelContactBudgetMB=256;
            using(var steps=(IEnumerator<object>)Call(w,"BuildVoxelSurfaceSteps",c,256,true))
            {
                var deadline=DateTime.UtcNow.AddSeconds(15);bool expanded=false,published=false;
                while(steps.MoveNext())
                {
                    published|=steps.Current!=null;
                    if(w.VoxelScratchBytes>0&&!expanded){expanded=true;w.VoxelContactBudgetMB=128;}
                    if(DateTime.UtcNow>deadline)throw new Exception("contact quota-change timeout");Thread.Sleep(1);
                }
                Check(expanded&&!published&&Dict(w,"contactDeferred").Contains(c.Key),"quota lowered during expansion is rechecked before native mesh creation");
            }
            Check(w.ContactTiles==2&&w.ContactMeshBytes<=128*MiB,"denied in-flight work leaves existing contacts and quota accounting intact");
        }
        finally{UnityEngine.Object.Destroy(root);}
        Check(w.VoxelScratchBytes==0&&Get(w,"voxelBuildJob")==null,"worker disposal releases scratch and async handle");
    }
    static void Main(string[] args)
    {
        string error=null;try{PreBoot();FarPolicy();ContactPolicy();NewTopologyDuringDebt();ContactExpansion();FarExpansion();}catch(Exception e){error=e.ToString();Console.WriteLine(error);}
        File.WriteAllText(args[0],JsonSerializer.Serialize(new{status=error==null?"PASS":"FAIL",checks,error,
            scope="Focused budget transitions under numerical Unity substitutes: metadata-only large payloads plus real bounded coarsening/contact geometry. No native RAM/VRAM/FPS proof."},new JsonSerializerOptions{WriteIndented=true}));
        if(error!=null)Environment.ExitCode=1;
    }
}
