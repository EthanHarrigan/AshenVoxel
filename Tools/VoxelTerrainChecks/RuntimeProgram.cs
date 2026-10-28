using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Text.Json;
using AshenVoxel.WorldScale;
using AshenVoxel.WorldScaleRuntime;
using AshenVoxel.Exploration;
using UnityEngine;

static class RuntimeProgram
{
    sealed class Field:IWorldField
    {public string SourceIdentity=>"explicit-runtime-fixture";public SurfaceSample Sample(double x,double z)=>new SurfaceSample(10,6,6,true);}
    static int checks;
    static void Check(bool value,string name){if(!value)throw new Exception("FAIL "+name);checks++;Console.WriteLine("PASS "+name);}
    static int MaskCount(Material material)=>material.LastVectorArray==null?0:material.LastVectorArray.Sum(v=>(v.x>.5?1:0)+(v.y>.5?1:0)+(v.z>.5?1:0)+(v.w>.5?1:0));
    static void Main(string[] args)
    {
        var root=new GameObject("voxel runtime check");var vista=root.AddComponent<WorldExplorer>();var proxy=new Material();
        typeof(WorldExplorer).GetField("ownedMaterial",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(vista,proxy);
        var component=root.AddComponent<ExplorationVoxelTerrain>();var shared=new Material();component.Initialize(vista,new Field(),new SurfaceProfile(enableSnow:false),shared);
        var observer=new WorldPoint(.1,12,.1);var origin=new WorldPoint(0,0,0);
        component.Tick(observer,origin);
        Check(component.ResidentPages==0&&MaskCount(proxy)==0&&component.PendingPages==49,"unbuilt49-page queue keeps all original proxy pixels visible");
        int maximum=0;var deadline=DateTime.UtcNow.AddSeconds(15);
        while(component.ResidentPages<4&&DateTime.UtcNow<deadline)
        {int before=component.ResidentPages;Thread.Sleep(2);component.Tick(observer,origin);maximum=Math.Max(maximum,component.ResidentPages-before);}
        Check(component.ResidentPages>=4&&maximum==1,"worker completion publishes at most one complete page per Tick");
        Check(component.ResidentPages==MaskCount(proxy),"proxy cutouts correspond exactly to completed resident pages");
        Check(component.ResidentCells==component.ResidentPages*10240&&component.Triangles>0,"runtime retains actual material cells and uploaded cubic geometry");
        Check(proxy.LastVector.w==1&&Math.Abs(proxy.LastVector.z-3.2)<.000001&&proxy.LastVectorArray.Length==16,"bounded8by8 shader grid carries at most64 readiness flags");
        component.Tick(observer,new WorldPoint(256,256,256));
        Check(Math.Abs(proxy.LastVector.x-(-9.6-256))<.0001&&Math.Abs(proxy.LastVector.y-(-9.6-256))<.0001,"floating-origin rebase updates proxy-mask coordinates in the same Tick");
        var meshes=UnityEngine.Object.Created.OfType<Mesh>().Where(m=>m.name=="10cm terrain page").ToArray();
        Check(meshes.All(m=>m.Uploaded&&!m.CpuReadable&&m.VertexCount%4==0&&m.TriangleIndices==m.VertexCount/4*6),"complete page uploads own correct-sized vertex, normal and index buffers");
        component.InvalidateRegion(-100,-100,100,100);component.Tick(observer,origin);
        Check(component.ResidentPages==0&&MaskCount(proxy)==0&&meshes.All(m=>m.WasDestroyed),"source invalidation restores proxy coverage and removes stale voxel meshes");
        deadline=DateTime.UtcNow.AddSeconds(15);while(component.ResidentPages<1&&DateTime.UtcNow<deadline){Thread.Sleep(2);component.Tick(observer,origin);}
        Check(component.ResidentPages>0,"canceled obsolete work does not lose the replacement queue");
        component.Tick(new WorldPoint(1000,12,1000),new WorldPoint(768,0,768));
        Check(component.ResidentPages==0&&MaskCount(proxy)==0&&component.PendingPages==49,"fast travel evicts old pages without leaving proxy holes or unbounded queues");
        UnityEngine.Object.Destroy(root);
        Check(MaskCount(proxy)==0&&component.ResidentPages==0&&!shared.WasDestroyed,"shutdown cancels work, clears cutouts and preserves the shared material");
        File.WriteAllText(args[0],JsonSerializer.Serialize(new{status="PASS",checks,scope="Actual runtime page queue, Unity mesh-data API substitutes, masks, rebase and cleanup. Not native Unity or shader execution."}));
        Console.WriteLine("All "+checks+" voxel runtime checks passed.");
    }
}
