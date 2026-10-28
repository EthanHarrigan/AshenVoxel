using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Text.Json;
using AshenVoxel.WorldScale;
using AshenVoxel.WorldScaleRuntime;
using UnityEngine;

static class Program
{
    sealed class Field:IWorldFieldBounds
    {public bool FailSample;public string SourceIdentity=>"explicit-world-voxel-runtime-fixture";public SurfaceSample Sample(double x,double z){if(FailSample)throw new InvalidOperationException("Injected source failure");return new SurfaceSample(10,6,6,true);}public HeightBounds QueryBounds(double a,double b,double c,double d)=>new HeightBounds(10,10);}
    static int checks,frame,maxUploads;const BindingFlags Flags=BindingFlags.Instance|BindingFlags.NonPublic;
    static void Check(bool v,string name){if(!v)throw new Exception("FAIL "+name);checks++;Console.WriteLine("PASS "+name);}
    static object Get(object o,string name)=>o.GetType().GetField(name,Flags).GetValue(o);
    static void Tick(WorldExplorer w)
    {Time.unscaledTime=++frame*.02f;Time.unscaledDeltaTime=.02f;typeof(WorldExplorer).GetMethod("Update",Flags).Invoke(w,null);maxUploads=Math.Max(maxUploads,w.MeshUploadsLastFrame);Thread.Sleep(1);}
    static void Settle(WorldExplorer w)
    {var end=DateTime.UtcNow.AddSeconds(45);int clean=0;while(DateTime.UtcNow<end){Tick(w);if(w.PendingTiles==0&&!w.PlanningInProgress&&w.HasActiveCoverage){if(++clean>=4)return;}else clean=0;}throw new Exception("Settle timed out "+w.PendingTiles+" "+w.Status);}
    static void Main(string[] args)
    {
        var root=new GameObject("Global voxel runtime test");var w=root.AddComponent<WorldExplorer>();w.UseVoxelLodTerrain=true;w.MaximumTiles=192;w.AsyncPlanning=true;w.MeshBuildsPerFrame=1;
        var source=new Field();w.ConfigureExternal(source,new SurfaceProfile(enableSnow:false),new WorldPoint(1,11.65,1),Quaternion.Euler(20,20,0));
        Settle(w);
        Check(w.VoxelLodTiles==w.ResidentTiles&&w.VoxelLodTiles>0,"all active world terrain tiles use voxel geometry");
        Check(maxUploads==1,"main-thread publication retains one-upload frame cap");
        Check(w.ResidentTiles<=192&&w.PendingTiles==0,"steady terrain residency and pending work respect tile capacity");
        Check(w.TerrainTriangles>0&&w.LastVoxelWorkerMilliseconds>0,"triangle totals and real worker latency are observable");
        Check(w.TrySampleRenderedHeight(1,1,out double h)&&Math.Abs(h-10)<1e-6,"render cache query returns voxel column support without triangle interpolation");
        var meshes=UnityEngine.Object.Created.OfType<Mesh>().Where(m=>!m.WasDestroyed&&m.name.StartsWith("Voxel LOD")).ToArray();
        Check(meshes.All(m=>m.Uploaded&&!m.CpuReadable&&m.VertexCount==m.NormalCount&&m.VertexCount==m.ColorCount&&m.VertexCount==m.UvCount&&m.indexFormat==UnityEngine.Rendering.IndexFormat.UInt32),"voxel tile uploads preserve normals, AO/pigment, surface properties and32bit indices");
        Check(meshes.Sum(m=>(long)m.TriangleIndices/3)==w.TerrainTriangles+w.ContactTriangles,"reported resident triangles match published mesh buffers");
        Check(Get(w,"voxelBuildJob")==null,"completed geometry leaves no orphan worker");
        var packed=typeof(WorldExplorer).GetNestedType("PackedVoxelVertex",BindingFlags.NonPublic);
        Check(System.Runtime.InteropServices.Marshal.SizeOf(packed)==24&&w.VoxelScratchBytes>0&&w.VoxelScratchBytes<=128*1024*1024,"24byte packed vertices preserve geometry with one bounded reusable upload buffer pair");
        Check(w.ContactTiles>0&&w.ContactPendingTiles==0&&w.ContactMeshBytes>0,"independent10cm contact lane settles within its byte admission budget");
        var contacts=(System.Collections.IDictionary)Get(w,"contactSurfaces");
        Check(contacts.Values.Cast<object>().All(v=>(int)v.GetType().GetField("Quads").GetValue(v)==256),"every contact tile uses25.6m/256 =10cm cells");
        int jobs=w.PlannerJobsStarted;w.SetObserverPose(new WorldPoint(1,11.65,1),Quaternion.Euler(20,200,0));
        for(int i=0;i<80&&w.PlannerJobsStarted==jobs;i++)Tick(w);
        Check(w.PlannerJobsStarted>jobs&&w.HasActiveCoverage,"turning in place schedules view detail while preserving complete coverage");
        Settle(w);
        w.SetObserverPose(new WorldPoint(2000,20,0),Quaternion.Euler(10,180,0));Tick(w);
        Check(w.RenderOrigin.X!=0&&w.HasActiveCoverage,"large travel rebases existing voxel meshes before replacement coverage arrives");
        bool earlyContact=false;
        var arrival=DateTime.UtcNow.AddSeconds(30);
        while(DateTime.UtcNow<arrival&&w.PendingTiles>0){Tick(w);if(w.ContactTiles>0&&w.ContactPendingTiles>0){earlyContact=true;break;}}
        Check(earlyContact,"first contact tiles become visible while the wider replacement plan still has pending work");
        Settle(w);
        Check(w.ResidentTiles<=192&&w.TrySampleRenderedHeight(2000,0,out h)&&Math.Abs(h-10)<1e-6,"travel publishes new voxel coverage and evicts obsolete tiles");
        w.RefreshExternalRegion(1990,-10,2010,10);Tick(w);
        Check(w.HasActiveCoverage&&w.PendingTiles>0,"source invalidation retains old complete tiles until replacements are ready");
        source.FailSample=true;int failures=w.VoxelBuildFailures;
        for(int i=0;i<300&&w.VoxelBuildFailures==failures;i++)Tick(w);
        Check(w.VoxelBuildFailures>failures&&w.PendingTiles>0&&w.HasActiveCoverage&&w.LastVoxelBuildError.Contains("Injected"),"worker failure preserves coverage, exposes the cause, and keeps queued retry work");
        source.FailSample=false;Settle(w);
        Check(w.PendingTiles==0&&w.TrySampleRenderedHeight(2000,0,out h),"recoverable source failure drains after retry without a missing tile");
        UnityEngine.Object.Destroy(root);
        Check(w.ResidentTiles==0&&Get(w,"voxelBuildJob")==null&&w.VoxelScratchBytes==0&&w.ContactTiles==0&&w.ContactMeshBytes==0,"scene shutdown cancels worker and destroys all owned tile caches");
        File.WriteAllText(args[0],JsonSerializer.Serialize(new{status="PASS",checks,frames=frame,scope="Actual whole-world streamer with Unity API substitutes: asynchronous plan/build, mesh attributes, view turn, rebase, invalidation, bounded publication and disposal. Not native rendering or FPS."},new JsonSerializerOptions{WriteIndented=true}));
        Console.WriteLine("All "+checks+" voxel LOD runtime checks passed.");
    }
}
