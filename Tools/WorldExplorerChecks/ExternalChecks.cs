using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using AshenVoxel.Core;
using AshenVoxel.WorldScale;
using AshenVoxel.WorldScaleRuntime;
using UnityEngine;
using UnityEngine.InputSystem;

static class ExternalChecks
{
    const BindingFlags Flags = BindingFlags.NonPublic | BindingFlags.Instance;
    static object Get(object value,string name)=>value.GetType().GetField(name,Flags|BindingFlags.Public).GetValue(value);
    static object Call(object value,string name,params object[] args)=>value.GetType().GetMethod(name,Flags).Invoke(value,args);
    sealed class Field:IWorldFieldBounds,IClimateWorldField
    {
        public int Queries;
        static double Height(double x,double z)=>100+Math.Max(-1000,Math.Min(1000,x))*.05+Math.Max(-1000,Math.Min(1000,z))*.02;
        public string SourceIdentity=>"controlled-external-terrain-not-neural";
        public SurfaceSample Sample(double x,double z){Queries++;return new SurfaceSample(Height(x,z),6,6,true);}
        public HeightBounds QueryBounds(double x0,double z0,double x1,double z1)=>new HeightBounds(Height(x0,z0),Height(x1,z1));
        public bool TrySampleClimate(double x,double z,out SurfaceClimate climate){climate=new SurfaceClimate(12,4,1300,30,-.0065);return true;}
    }
    sealed class GatedField:IWorldFieldBounds
    {
        public readonly ManualResetEventSlim Entered=new ManualResetEventSlim(),Release=new ManualResetEventSlim();
        public int BoundsCalls,WorkerThread;
        public string SourceIdentity=>"controlled-gated-immutable-field";
        public SurfaceSample Sample(double x,double z)=>new SurfaceSample(100,6,6,true);
        public HeightBounds QueryBounds(double x0,double z0,double x1,double z1)
        {Interlocked.Increment(ref BoundsCalls);WorkerThread=Thread.CurrentThread.ManagedThreadId;Entered.Set();if(!Release.Wait(5000))throw new Exception("Test planner gate timed out");return new HeightBounds(100,100);}
    }
    public static void Run(Action<bool,string> check)
    {
        var source=new Field();var profile=new SurfaceProfile(enableSnow:false,forcedBedrockMaterial:MaterialId.Granite);
        var scene=new WorldExplorer{gameObject=new GameObject("External integration"),MinimumTileSizeMeters=32,NearTileQuads=32,
            ShowVisualWater=false,AsyncPlanning=false,TileQuads=16,ViewDistanceKm=30,MaximumTiles=192,MeshWorkUnitsPerFrame=32,MeshBuildMilliseconds=8,MeshBuildsPerFrame=1};
        var eye=new WorldPoint(123.25,111,567.75);
        scene.ConfigureExternal(source,profile,eye,Quaternion.Euler(4,17,0));
        check(scene.ExternalNavigation&&!scene.ShowResearchHud&&object.ReferenceEquals(scene.BareTerrainField,source)&&scene.SurfaceSampler!=null,
            "external world binds the actual caller field and disables research navigation/HUD without a fallback");
        check(scene.MainCamera!=null&&scene.MainCamera==scene.ViewCamera&&scene.GlobalCamera.X==eye.X&&scene.GlobalCamera.Y==eye.Y,
            "external setup retains the requested observer rather than teleporting to a mountain study viewpoint");
        Camera initialCamera=scene.MainCamera;Call(scene,"Start");
        check(object.ReferenceEquals(initialCamera,scene.MainCamera)&&object.ReferenceEquals(scene.BareTerrainField,source)&&scene.GlobalCamera.X==eye.X,
            "Unity Start after caller configuration preserves the actual source, camera ownership and observer");
        int initial=scene.PendingTiles,queryBefore=source.Queries;
        Time.unscaledTime=10;Call(scene,"Update");
        check(scene.LastMeshWorkUnits<=64&&scene.MeshUploadsLastFrame<=1&&source.Queries-queryBefore<=64*11,
            "incremental mesh sampling obeys work units and upload cap before a full tile exists");
        check(scene.ResidentTiles==0&&scene.PendingTiles==initial,
            "an incomplete mesh keeps its work state rather than publishing half a terrain tile");
        object partial=Get(scene,"currentBuild");Time.unscaledTime=20;Call(scene,"Update");
        check(scene.PendingTiles<=initial&&object.ReferenceEquals(partial,Get(scene,"currentBuild")),
            "elapsed planning timers do not repeatedly discard a partially built stationary tile");
        bool bounded=true,coarseVisible=false;int frames=0;
        scene.MeshWorkUnitsPerFrame=2048;
        while(!scene.HasActiveCoverage&&frames++<2000)
        {Time.unscaledTime+=1f/60;Call(scene,"Update");bounded&=scene.LastMeshWorkUnits<=2048&&scene.MeshUploadsLastFrame<=1&&scene.ResidentTiles<=384;}
        coarseVisible=scene.HasActiveCoverage;
        check(coarseVisible&&bounded,"coarse startup reaches complete visible coverage with bounded residency and per-frame work");
        check(!scene.IsTerrainReadyArea(eye.X,eye.Z),"coarse visibility alone does not release the player before fine ground coverage");
        int visible=scene.ResidentTiles;frames=0;
        while(!scene.IsTerrainReadyArea(eye.X,eye.Z)&&frames++<3000)
        {Time.unscaledTime+=1f/60;Call(scene,"Update");bounded&=scene.ResidentTiles>=visible&&scene.ResidentTiles<=384;}
        if(!scene.IsTerrainReadyArea(eye.X,eye.Z)||!bounded)
        {
            object at=Call(scene,"ActiveSurfaceAt",eye.X,eye.Z);
            Console.WriteLine("EXTERNAL_READY_DIAGNOSTIC frames="+frames+" pending="+scene.PendingTiles+" residents="+scene.ResidentTiles+" bounded="+bounded+" tile="+(at==null?"none":((LodTile)Get(at,"Tile")).SizeMeters.ToString())+" quads="+(at==null?"none":Get(at,"Quads").ToString()));
        }
        check(scene.IsTerrainReadyArea(eye.X,eye.Z)&&bounded,"near-first refinement reaches submeter-ready spawn area while retaining complete old coverage");
        double target=source.Sample(eye.X,eye.Z).HeightMeters;
        check(scene.TrySampleRenderedHeight(eye.X,eye.Z,out double rendered)&&Math.Abs(rendered-target)<.0001,
            "rendered triangle height agrees with the smooth source plane rather than voxel stair quantization");
        Keyboard.current=new Keyboard();Mouse.current=new Mouse();
        Keyboard.current.pKey.wasPressedThisFrame=true;Keyboard.current.rKey.wasPressedThisFrame=true;Keyboard.current.wKey.isPressed=true;
        Mouse.current.rightButton.isPressed=true;Mouse.current.delta.value=new Vector2{x=20,y=30};
        Call(scene,"Update");
        check(!scene.InspectingVoxels&&scene.GlobalCamera.X==eye.X&&scene.GlobalCamera.Y==eye.Y&&scene.GlobalCamera.Z==eye.Z,
            "external control cannot trigger P editing, research regeneration or duplicate keyboard movement");
        Keyboard.current=null;Mouse.current=null;
        var moved=new WorldPoint(1024.125,129.5,-2048.375);scene.SetObserverPose(moved,Quaternion.Euler(5,28,0));
        check(Math.Abs(scene.MainCamera.transform.position.x-(moved.X-scene.RenderOrigin.X))<1e-6&&
            Math.Abs(scene.MainCamera.transform.position.z-(moved.Z-scene.RenderOrigin.Z))<1e-6,
            "observer pose synchronously rebases camera and render origin in the same call");
        int before=scene.ResidentTiles;scene.FarHorizonDistanceKm=80;scene.ForceReplan();
        Call(scene,"Update");
        check(scene.MainCamera.farClipPlane>=80000&&scene.ResidentTiles>=before,
            "editable distant horizon expands the camera while preserving prior complete terrain coverage");
        check(!scene.IsTerrainReady(double.NaN,0)&&!scene.TrySampleRenderedHeight(double.NaN,0,out rendered),
            "invalid external terrain queries do not claim render readiness");
        int oldRevision=(int)Get(scene,"revision");scene.MinimumTileSizeMeters=64;scene.ForceReplan();Call(scene,"Update");
        check((int)Get(scene,"revision")>oldRevision&&scene.ResidentTiles==0&&scene.PendingTiles>0,
            "changing base tile size invalidates old level-coordinate geometry instead of reusing wrong physical tiles");
        Call(scene,"OnDestroy");
        check(scene.ResidentTiles==0&&scene.PendingTiles==0&&Get(scene,"currentBuild")==null,
            "external source cleanup retires complete and incomplete mesh state");
        Time.unscaledTime=0;
        bool stressBounded=true,stressCoverage=true,stressBalanced=true;
        var settings=new LodSettings(baseTileSizeMeters:32,maxLevel:14,farDistanceMeters:80000,maxTiles:512,minimumHeightMeters:-12000,maximumHeightMeters:48000);
        foreach(var observer in new[]{new WorldPoint(0,2,0),new WorldPoint(64000,8000,-32000),new WorldPoint(-150000,15000,70000),new WorldPoint(149000,64000,-74500)})
        {
            LodPlan plan=LodPlanner.Build(source,observer,settings);stressBounded&=plan.Tiles.Count<=512;
            for(int i=0;i<plan.Tiles.Count;i++)for(int j=i+1;j<plan.Tiles.Count;j++)
                if(LodPlanner.Adjacent(plan.Tiles[i],plan.Tiles[j]))stressBalanced&=Math.Abs(plan.Tiles[i].Key.Level-plan.Tiles[j].Key.Level)<=1;
            for(int i=0;i<16;i++)
            {
                double x=observer.X+Math.Cos(i*Math.PI/8)*20000,z=observer.Z+Math.Sin(i*Math.PI/8)*20000;bool covered=false;
                foreach(var tile in plan.Tiles)covered|=x>=tile.OriginXMeters&&x<tile.OriginXMeters+tile.SizeMeters&&z>=tile.OriginZMeters&&z<tile.OriginZMeters+tile.SizeMeters;
                stressCoverage&=covered;
            }
        }
        check(stressBounded&&stressBalanced&&stressCoverage,
            "80km vista plans remain bounded,2:1 balanced and cover surrounding terrain across150km travel and64km altitude");
        var gate=new GatedField();int callerThread=Thread.CurrentThread.ManagedThreadId;
        var asynchronous=new WorldExplorer{gameObject=new GameObject("Background planner"),ShowVisualWater=false,MaximumTiles=64,MeshWorkUnitsPerFrame=2048,MeshBuildMilliseconds=8};
        asynchronous.ConfigureExternal(gate,profile,new WorldPoint(123,102,567),Quaternion.Euler(0,0,0));
        check(asynchronous.PlanningInProgress&&gate.Entered.Wait(5000)&&gate.WorkerThread!=callerThread,
            "immutable external LOD planning runs on a background worker instead of blocking native movement frames");
        for(int i=0;i<5;i++){asynchronous.ForceReplan();Time.unscaledTime+=1;Call(asynchronous,"Update");}
        check(asynchronous.PlannerJobsStarted==1&&gate.BoundsCalls==1,
            "repeated settings requests cannot launch unbounded concurrent terrain planners");
        var replacement=new Field();asynchronous.ConfigureExternal(replacement,profile,new WorldPoint(200,114,700),Quaternion.Euler(0,0,0));
        check(asynchronous.PlannerJobsStarted==1&&object.ReferenceEquals(asynchronous.BareTerrainField,replacement),
            "source changes preserve a single in-flight old job while binding the replacement immediately");
        gate.Release.Set();int iterations=0;
        while(asynchronous.PendingTiles>0&&iterations++<5000)
        {Time.unscaledTime+=1f/60;Call(asynchronous,"Update");Thread.Sleep(1);}
        check(asynchronous.HasActiveCoverage&&!asynchronous.PlanningInProgress&&object.ReferenceEquals(asynchronous.BareTerrainField,replacement)&&asynchronous.PlannerJobsStarted<=3,
            "stale planner results are discarded and the latest immutable source reaches bounded visible coverage");
        Call(asynchronous,"OnDestroy");gate.Entered.Dispose();gate.Release.Dispose();Time.unscaledTime=0;
    }
}
