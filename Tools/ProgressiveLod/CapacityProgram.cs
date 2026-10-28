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
    {
        public string SourceIdentity=>"bounded-capacity-wakeup-flat-material-fixture";
        public SurfaceSample Sample(double x,double z)=>new SurfaceSample(10,6,6,true);
        public HeightBounds QueryBounds(double a,double b,double c,double d)=>new HeightBounds(10,10);
        public IWorldField CaptureSnapshot()=>this;
    }
    const BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
    static int checks,frame;static bool passed=true;
    static void Check(bool value,string name){checks++;passed&=value;Console.WriteLine((value?"PASS ":"FAIL ")+name);}
    static object Get(object obj,string name)=>obj.GetType().GetField(name,Private|BindingFlags.Public).GetValue(obj);
    static void Set(object obj,string name,object value)=>obj.GetType().GetField(name,Private|BindingFlags.Public).SetValue(obj,value);
    static object Call(WorldExplorer w,string method,params object[] args)=>typeof(WorldExplorer).GetMethod(method,Private).Invoke(w,args);
    static LodTile Tile(long x,long z,int level=5)
    {double size=25.6*(1<<level);return new LodTile(new TileKey(x,z,level),x*size,z*size,size,10,10);}
    static IDictionary Surfaces(WorldExplorer w)=>(IDictionary)Get(w,"surfaces");
    static HashSet<TileKey> Active(WorldExplorer w)=>(HashSet<TileKey>)Get(w,"active");
    static int Q(WorldExplorer w,LodTile tile)=>(int)Get(Surfaces(w)[tile.Key],"Quads");
    static bool Busy(WorldExplorer w)=>Get(w,"currentBuild")!=null||((ICollection)Get(w,"pending")).Count>0;
    static long Bytes(WorldExplorer w)=>Surfaces(w).Values.Cast<object>().Sum(s=>(long)(int)Get(s,"TriangleCount")*60);
    static int FaceCaps(WorldExplorer w)=>((IDictionary)Get(w,"voxelCapacityQuads")).Count;
    static void Tick(WorldExplorer w,bool auto)
    {Time.unscaledTime=++frame*.02f;Time.unscaledDeltaTime=.02f;if(auto)Call(w,"QueueNextVoxelRefinement");Call(w,"AdvanceBuilds");Call(w,"ActivateReadyPlan");Thread.Sleep(1);}
    static void Until(WorldExplorer w,Func<bool> done,string name,bool auto=false)
    {var end=DateTime.UtcNow.AddSeconds(25);do{Tick(w,auto);if(done())return;}while(DateTime.UtcNow<end);throw new Exception("Timeout "+name+" pending="+w.PendingTiles);}
    static void Apply(WorldExplorer w,params LodTile[] tiles)=>Call(w,"ApplyPlan",new LodPlan{Tiles=tiles,SourceIdentity="bounded capacity topology"},25.6);
    static void Exact(WorldExplorer w,LodTile tile,int n,bool progressive=false)
    {
        Type work=typeof(WorldExplorer).GetNestedType("Work",BindingFlags.NonPublic);
        object request=Activator.CreateInstance(work,new object[]{tile,(int)Get(w,"revision"),true,false,n,progressive});
        object queue=Get(w,"pending");queue.GetType().GetMethod("Enqueue").Invoke(queue,new[]{request});
    }
    static void Build(WorldExplorer w,LodTile tile,int n)
    {Exact(w,tile,n);Until(w,()=>!Busy(w),"exact seed mesh");}
    static void Settle(WorldExplorer w)=>Until(w,()=>!Busy(w)&&!(bool)Get(w,"voxelRefinementScanPending"),"bounded idle",true);
    static void Reset(WorldExplorer w,params LodTile[] tiles)
    {Call(w,"ClearTerrain");Set(w,"coarseStartup",true);w.SetObserverPose(new WorldPoint(0,12,0),Quaternion.Euler(0,0,0));Apply(w,tiles);Until(w,()=>w.HasActiveCoverage,"coarse seed");}
    static void Main(string[] args)
    {
        var root=new GameObject("Bounded real-mesh memory-admission fixture");var w=root.AddComponent<WorldExplorer>();
        w.UseVoxelLodTerrain=true;w.MaximumTiles=64;w.AsyncPlanning=false;w.MeshBuildsPerFrame=1;w.MeshWorkUnitsPerFrame=2048;w.MeshBuildMilliseconds=8;
        w.ConfigureExternal(new Field(),new SurfaceProfile(enableSnow:false),new WorldPoint(0,12,0),Quaternion.Euler(0,0,0));
        long budget=(long)typeof(WorldExplorer).GetField("VoxelDetailBudgetBytes",BindingFlags.Static|BindingFlags.NonPublic).GetRawConstantValue();
        Check(budget==10L*1024*1024,"fixture uses a test-only10MiB allowance; shipping1536MiB cap is not changed");
        LodTile a=Tile(0,2),b=Tile(-1,2),c=Tile(1,2),d=Tile(2,2);
        Reset(w,a,b,c);Build(w,b,256);Build(w,c,256);Build(w,a,128);
        Console.WriteLine("Seed mesh bytes="+Bytes(w)+" detail bytes="+Get(w,"voxelDetailMeshBytes"));
        Check(Q(w,b)==256&&Q(w,c)==256&&Q(w,a)==128&&w.VoxelRefinementDeferredTiles==0,"real seed meshes fill most of the small detail allowance");
        Exact(w,a,256,true);Tick(w,false);
        Check(Get(w,"currentBuild")!=null,"a high-detail refinement is actually in flight before travel");
        w.SetObserverPose(new WorldPoint(160,12,0),Quaternion.Euler(0,0,0));Apply(w,a,d);
        Until(w,()=>w.VoxelRefinementDeferredTiles==1,"in-flight request rejected against old coverage");
        Check(Active(w).Contains(b.Key)&&Active(w).Contains(c.Key),"retained in-flight refinement rejects while old coverage still occupies capacity");
        Until(w,()=>Active(w).Count==2&&Active(w).Contains(d.Key),"new coverage retires the old high-detail tiles");
        Settle(w);
        Check(Q(w,a)==256&&w.VoxelRefinementDeferredTiles==0,"stopped pose retries a now-admissible request after obsolete coverage releases memory");
        Check(w.TerrainResidentMeshBytes==Bytes(w)&&FaceCaps(w)==0,"accounting remains exact and memory admission is distinct from geometric face capacity");
        int quiet=w.VoxelRefinementBuilds;for(int i=0;i<50;i++)Tick(w,true);
        Check(w.VoxelRefinementBuilds==quiet,"completed stopped pose does not busy-retry");

        // A source refresh can require another mesh at the SAME column count.
        // Supply its measured-byte request directly to isolate that scheduling
        // distinction, then free actual generated mesh payload on a neighbor.
        long reserve=(long)typeof(WorldExplorer).GetField("VoxelCoverageTileReserveBytes",BindingFlags.Static|BindingFlags.NonPublic).GetRawConstantValue();
        long aDetail=Math.Max(0,(long)(int)Get(Surfaces(w)[a.Key],"TriangleCount")*60-reserve);
        long otherDetail=(long)Get(w,"voxelDetailMeshBytes")-aDetail;
        long requestBytes=reserve+budget-otherDetail+120;
        object oldA=Get(Surfaces(w)[a.Key],"Mesh");
        bool denied=!(bool)Call(w,"AdmitVoxelMesh",a,256,requestBytes);
        Build(w,d,32);
        Check(denied&&Q(w,a)==256&&!ReferenceEquals(oldA,Get(Surfaces(w)[a.Key],"Mesh")),"capacity wake requeues a same-resolution replacement instead of losing it in the quality scan");

        Reset(w,a,b,c,d);Build(w,b,256);Build(w,c,256);Build(w,a,128);
        Build(w,a,256);Build(w,d,256);
        Check(w.VoxelRefinementDeferredTiles==2,"two measured over-budget refinements are retained");
        long prior=(long)Get(w,"voxelDetailMeshBytes");Build(w,b,256);
        Check((long)Get(w,"voxelDetailMeshBytes")==prior&&w.VoxelRefinementDeferredTiles==2,"same-size replacement does not wake deferred work during transient Release-before-add");
        Build(w,b,128);
        Check((long)Get(w,"voxelDetailMeshBytes")<prior,"a completed cheaper replacement actually releases detail payload");
        Settle(w);
        Check((Q(w,a)==256||Q(w,d)==256)&&w.VoxelRefinementDeferredTiles>0,"freed capacity admits useful detail while an over-budget request stays deferred");
        quiet=w.VoxelRefinementBuilds;for(int i=0;i<100;i++)Tick(w,true);
        Check(w.VoxelRefinementBuilds==quiet&&w.PendingTiles>0,"persistent over-budget demand remains observable without retry thrash after wake");
        Check((long)Get(w,"voxelDetailMeshBytes")<=budget&&FaceCaps(w)==0,"bounded real-mesh scenario never raises the cap or invokes face-limit fallback");

        // A distant stopped observer can still see the same terrain. Its desired
        // pixel resolution drops; surplus retained detail must not monopolize RAM.
        w.SetObserverPose(new WorldPoint(0,12,-12000),Quaternion.Euler(0,0,0));Apply(w,a,b,c,d);Settle(w);
        var wanted=(Func<LodTile,int>)typeof(WorldExplorer).GetMethod("VoxelQuadsFor",Private).CreateDelegate(typeof(Func<LodTile,int>),w);
        Check(new[]{a,b,c,d}.All(t=>Q(w,t)<=wanted(t)),"flight releases visible detail above the newly requested pixel resolution");
        long farStopBytes=Bytes(w);
        w.SetObserverPose(new WorldPoint(0,12,-12000),Quaternion.Euler(0,180,0));Apply(w,a,b,c,d);Settle(w);
        Check(new[]{a,b,c,d}.All(t=>Q(w,t)==32),"turning away retains the bounded rear-view representation");
        w.SetObserverPose(new WorldPoint(0,12,0),Quaternion.Euler(0,0,0));Apply(w,a,b,c,d);Settle(w);
        Check(new[]{a,b,c,d}.Any(t=>Q(w,t)==256)&&Bytes(w)>farStopBytes,"returning along the route restores near-view quality within the same allowance");
        quiet=w.VoxelRefinementBuilds;for(int i=0;i<100;i++)Tick(w,true);
        Check(w.VoxelRefinementBuilds==quiet&&(long)Get(w,"voxelDetailMeshBytes")<=budget,"final stopped return converges or defers without cycling or increasing the cap");
        int memoryDeferred=w.VoxelRefinementDeferredTiles,geometricFaceCaps=FaceCaps(w);
        UnityEngine.Object.Destroy(root);
        Check(w.TerrainResidentMeshBytes==0&&w.VoxelRefinementDeferredTiles==0,"shutdown clears all retained byte accounting and requests");
        File.WriteAllText(args[0],JsonSerializer.Serialize(new{status=passed?"PASS":"FAIL",checks,frames=frame,detailBudgetBytes=budget,memoryDeferred,geometricFaceCaps,
            scope="Real tile generation and lifecycle under numerical Unity substitutes with only the private detail allowance lowered to10MiB in a test-only source overlay. Shipping cap remains1536MiB. No native memory/FPS claim."},new JsonSerializerOptions{WriteIndented=true}));
        if(!passed)Environment.ExitCode=1;
    }
}
