using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using AshenVoxel.WorldScale;
using AshenVoxel.WorldScaleRuntime;
using UnityEngine;

static class Program
{
    const BindingFlags Flags=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
    static object Get(object o,string n)=>o.GetType().GetField(n,Flags).GetValue(o);
    static void Set(object o,string n,object v)=>o.GetType().GetField(n,Flags).SetValue(o,v);
    static object Call(object o,string n,params object[] a)=>o.GetType().GetMethod(n,Flags).Invoke(o,a);
    static string Hash(string p){using(var h=SHA256.Create())using(var s=File.OpenRead(p))return Convert.ToHexString(h.ComputeHash(s)).ToLowerInvariant();}
    static void Main(string[] args)
    {
        string error=null;int checks=0;var rows=new List<object>();var hashes=new Dictionary<string,string>();
        long admitted=0,total=0;int readyAfter=-1,planned=0,deferred=0;double elapsed=0;string identity=null;
        var root=new GameObject("actual spawn small contact profile");var w=root.AddComponent<WorldExplorer>();
        try
        {
            string samples=Path.Combine(args[1],"Assets/AshenVoxel/Exploration/Samples");
            string PathFor(string name){string path=Path.Combine(samples,name);hashes[name]=Hash(path);return path;}
            HeightAtlas macro,fine;ClimateAtlas mc,fc;ExplorationBiomeAtlas biomes;
            using(var f=File.OpenRead(PathFor("ExplorationMacro.bytes")))macro=AtlasIO.Read(f);
            using(var f=File.OpenRead(PathFor("ExplorationFine.bytes")))fine=AtlasIO.Read(f);
            using(var f=File.OpenRead(PathFor("ExplorationMacro.climate.bytes")))mc=ClimateArchiveIO.Read(f);
            using(var f=File.OpenRead(PathFor("ExplorationFine.climate.bytes")))fc=ClimateArchiveIO.Read(f);
            using(var f=File.OpenRead(PathFor("ExplorationBiomes.bytes")))biomes=ExplorationBiomeAtlasIO.Read(f);
            var source=new ExplorationAtlasField(macro,mc,fine,fc,512,macro.ExtentX,macro.ExtentZ,1,biomes,macro.ExtentX,macro.ExtentZ);
            var field=new TerrainDetailField(source,714103);var profile=new SurfaceProfile(seed:714103,maxSnowDepthMeters:2.4f,winterFraction:.2f);
            var sampler=new SurfaceMaterialSampler(field,profile);identity=sampler.SourceIdentity;
            const double x=-19065,z=-10200;double y=sampler.EvaluateColumn(x,z).SurfaceHeightMeters+1.65;
            w.UseVoxelLodTerrain=true;w.AsyncPlanning=false;w.MaximumTiles=192;w.VoxelFullDetailRadius=16;
            w.ConfigureInitialVoxelMeshBudgets(64,128);w.ConfigureExternal(field,profile,new WorldPoint(x,y,z),Quaternion.Euler(20,20,0));
            Call(w,"ClearTerrain");Set(w,"coarseStartup",false);Call(w,"RefreshContactPlan");
            var queue=(IList)Get(w,"contactPending");planned=queue.Count;
            while(queue.Count>0)
            {
                object work=Call(w,"NextContactWork");var tile=(LodTile)Get(work,"Tile");double a=tile.OriginXMeters,b=tile.OriginZMeters,size=tile.SizeMeters;
                var clock=Stopwatch.StartNew();double floor=((IWorldFieldBounds)field).QueryBounds(a-size*2,b-size*2,a+size*3,b+size*3).Minimum-1;
                var data=VoxelLodSurface.Build(sampler,tile,256,floor,parallelSampling:true);clock.Stop();elapsed+=clock.Elapsed.TotalMilliseconds;
                long bytes=(long)data.Faces.Count*120;total+=bytes;
                object surface=Activator.CreateInstance(typeof(WorldExplorer).GetNestedType("Surface",BindingFlags.NonPublic),true);
                Set(surface,"Object",new GameObject("geometry payload metadata"));Set(surface,"Mesh",new Mesh());Set(surface,"Tile",tile);Set(surface,"VoxelColumns",true);
                Set(surface,"Quads",256);Set(surface,"TriangleCount",data.Faces.Count*2);Set(surface,"LocalHeights",data.Heights);Set(surface,"AnchorY",data.AnchorY);
                long before=w.ContactMeshBytes;Call(w,"PublishContact",surface,work);bool accepted=w.ContactMeshBytes>before;
                if(readyAfter<0&&w.IsTerrainReadyArea(x,z,2))readyAfter=rows.Count+1;
                rows.Add(new{key=tile.Key.ToString(),faces=data.Faces.Count,payload_bytes=bytes,accepted,builder_ms=clock.Elapsed.TotalMilliseconds,cell_m=size/256});
                if(w.ContactMeshBytes>128L*1048576)throw new Exception("Admission exceeded initial quota");
            }
            admitted=w.ContactMeshBytes;deferred=w.ContactDeferredTiles;
            if(planned!=17)throw new Exception("Actual radius16 contact plan changed");checks++;
            if(readyAfter<1)throw new Exception("Small profile did not prepare 2m spawn terrain footprint");checks++;
            if(w.ContactTiles+deferred!=planned)throw new Exception("Contact demand was lost");checks++;
            if(w.ContactMeshLimitBytes!=128L*1048576||w.TerrainDetailLimitBytes!=64L*1048576)throw new Exception("Quotas changed");checks++;
        }
        catch(Exception e){error=e.ToString();Console.WriteLine(error);}
        finally{UnityEngine.Object.Destroy(root);}
        File.WriteAllText(args[0],JsonSerializer.Serialize(new{status=error==null?"PASS":"FAIL",checks,error,source=identity,data_sha256=hashes,
            scope="Actual14fb32 spawn radius16 geometry, one tile at a time/two sampling workers, actual contact priority/admission under numerical Unity substitutes. Mesh objects contain metadata only. No native upload/full Bootstrap Ready/Start/FPS/peak memory claim.",
            planned,admitted_payload_bytes=admitted,total_requested_payload_bytes=total,deferred,terrain_ready_after_contact_builds=readyAfter,
            cold_single_pass_builder_ms=elapsed,rows},new JsonSerializerOptions{WriteIndented=true}));
        Console.WriteLine($"{checks} checks; {planned} requested, {deferred} deferred, {admitted/1048576.0:F2} MiB admitted, terrain ready after {readyAfter}; {elapsed:F1} ms geometry only");
        if(error!=null)Environment.ExitCode=1;
    }
}
