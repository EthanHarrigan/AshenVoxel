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
    static void Publish(WorldExplorer w,LodTile tile,long bytes)=>Call(w,"PublishContact",Surface(w,tile,bytes,256),Work(w,tile,true,256));
    static WorldExplorer Make(out GameObject root)
    {
        root=new GameObject("isolated budget policy");var w=root.AddComponent<WorldExplorer>();w.UseVoxelLodTerrain=true;w.AsyncPlanning=false;w.MaximumTiles=64;w.VoxelFullDetailRadius=16;w.ConfigureInitialVoxelMeshBudgets(64,256);
        w.ConfigureExternal(new Field(),new SurfaceProfile(enableSnow:false),new WorldPoint(1,12,1),Quaternion.Euler(0,0,0));Call(w,"ClearTerrain");Set(w,"coarseStartup",false);Call(w,"RefreshContactPlan");Contacts(w).Clear();return w;
    }
    static void StartupPolicy()
    {
        var root=new GameObject("startup-only budget configuration");var w=root.AddComponent<WorldExplorer>();var boot=root.AddComponent<ExplorationBootstrap>();
        var settings=ScriptableObject.CreateInstance<ExplorationSettings>();boot.Settings=settings;boot.LoadSavedOptions=false;
        try
        {
            Check(settings.VoxelDetailBudgetMB==1536&&settings.VoxelContactBudgetMB==1024&&w.TerrainDetailLimitBytes==1536*MiB&&w.ContactMeshLimitBytes==1024*MiB,"settings and standalone renderer retain shipping defaults");
            settings.VoxelDetailBudgetMB=64;settings.VoxelContactBudgetMB=128;
            var bootSteps=(IEnumerator)Call(boot,"Boot");try{Check(bootSteps.MoveNext()&&boot.ActiveSettings.VoxelDetailBudgetMB==64&&boot.ActiveSettings.VoxelContactBudgetMB==128&&boot.Vista==null,"Boot clones startup quotas before its first yield and before renderer allocation");}finally{(bootSteps as IDisposable)?.Dispose();}
            Set(boot,"<Vista>k__BackingField",w);w.ConfigureInitialVoxelMeshBudgets(boot.ActiveSettings.VoxelDetailBudgetMB,boot.ActiveSettings.VoxelContactBudgetMB);
            Check(w.TerrainDetailLimitBytes==64*MiB&&w.ContactMeshLimitBytes==128*MiB&&Get(w,"field")==null,"initial budget API applies effective quotas before field/plan/build");
            string identity=settings.Surface().Identity;settings.VoxelDetailBudgetMB=1536;settings.VoxelContactBudgetMB=2048;Check(identity==settings.Surface().Identity,"budget settings do not change generation identity");
            w.ConfigureInitialVoxelMeshBudgets(-1,-1);Check(w.TerrainDetailLimitBytes==64*MiB&&w.ContactMeshLimitBytes==128*MiB,"initial effective lower clamps are stable");
            w.ConfigureInitialVoxelMeshBudgets(int.MaxValue,int.MaxValue);Check(w.TerrainDetailLimitBytes==1536*MiB&&w.ContactMeshLimitBytes==2048*MiB,"initial effective upper clamps are stable");
            w.ConfigureInitialVoxelMeshBudgets(64,128);w.UseVoxelLodTerrain=true;w.AsyncPlanning=false;w.MaximumTiles=64;
            w.ConfigureExternal(new Field(),new SurfaceProfile(enableSnow:false),new WorldPoint(1,12,1),Quaternion.Euler(0,0,0));
            bool rejected=false;try{w.ConfigureInitialVoxelMeshBudgets(1536,2048);}catch(InvalidOperationException){rejected=true;}
            Check(rejected&&w.TerrainDetailLimitBytes==64*MiB&&w.ContactMeshLimitBytes==128*MiB,"reconfiguration after initialization is rejected atomically and requests Play restart");
            boot.ActiveSettings.VoxelDetailBudgetMB=1536;boot.ActiveSettings.VoxelContactBudgetMB=2048;Call(boot,"ApplyVistaSettings",false);
            Check(w.TerrainDetailLimitBytes==64*MiB&&w.ContactMeshLimitBytes==128*MiB,"periodic runtime settings cannot silently change startup quotas");
            w.VoxelContactBudgetMB=256;Call(boot,"ApplyVistaSettings",false);Check(w.ContactMeshLimitBytes==256*MiB,"legacy direct contact debug allowance is not overwritten by periodic settings");
            Check((bool)Call(w,"AdmitVoxelMesh",Tile(9),8,Reserve),"reserved coarse coverage remains admissible with the small initial detail quota");
            Check(!(bool)Call(w,"AdmitVoxelMesh",Tile(9),256,64*MiB+Reserve+120)&&Dict(w,"voxelDeferredQuads").Contains(Tile(9).Key),"initial far quota rejects oversized detail before upload and retains demand");
        }
        finally{UnityEngine.Object.Destroy(root);UnityEngine.Object.Destroy(settings);}
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
        string error=null;try{StartupPolicy();ContactExpansion();}catch(Exception e){error=e.ToString();Console.WriteLine(error);}
        File.WriteAllText(args[0],JsonSerializer.Serialize(new{status=error==null?"PASS":"FAIL",checks,error,
            scope="Startup-only quota configuration and actual contact pre-expansion/mid-expansion admission under numerical Unity substitutes. Large existing payloads are metadata only; no native memory/FPS claim."},new JsonSerializerOptions{WriteIndented=true}));
        if(error!=null)Environment.ExitCode=1;
    }
}
