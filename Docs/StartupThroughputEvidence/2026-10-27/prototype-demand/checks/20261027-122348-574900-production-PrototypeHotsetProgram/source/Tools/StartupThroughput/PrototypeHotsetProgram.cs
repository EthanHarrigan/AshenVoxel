using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using AshenVoxel.WorldScale;
using AshenVoxel.Exploration;
using UnityEngine;

// Measurement only: actual pack/population/selection code, metadata Mesh objects.
// No lazy loader, native upload, asset edits or generation changes.
static class Program
{
    const BindingFlags Flags=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
    static object Get(object o,string n)=>o.GetType().GetField(n,Flags).GetValue(o);
    static void Set(object o,string n,object v)=>o.GetType().GetField(n,Flags).SetValue(o,v);
    static object Call(object o,string n,params object[] a)=>o.GetType().GetMethod(n,Flags).Invoke(o,a);
    static string Hash(string p){using(var h=SHA256.Create())using(var f=File.OpenRead(p))return Convert.ToHexString(h.ComputeHash(f)).ToLowerInvariant();}
    sealed class Meta
    {
        public ExplorationPrototype Data;
        public long[] Bytes,DecodedBytes;
        public int[] Triangles;
        public double DecodeMs;
    }
    static readonly Dictionary<int,Meta> metadata=new Dictionary<int,Meta>();
    static readonly Dictionary<string,string> hashes=new Dictionary<string,string>();
    static int checks;
    static void Check(bool b,string text){if(!b)throw new Exception(text);checks++;}
    static SurfaceMaterialSampler Load(string root)
    {
        string d=Path.Combine(root,"Assets/AshenVoxel/Exploration/Samples");
        string P(string n){string p=Path.Combine(d,n);hashes[n]=Hash(p);return p;}
        HeightAtlas macro,fine;ClimateAtlas mc,fc;ExplorationBiomeAtlas biomes;
        using(var f=File.OpenRead(P("ExplorationMacro.bytes")))macro=AtlasIO.Read(f);
        using(var f=File.OpenRead(P("ExplorationFine.bytes")))fine=AtlasIO.Read(f);
        using(var f=File.OpenRead(P("ExplorationMacro.climate.bytes")))mc=ClimateArchiveIO.Read(f);
        using(var f=File.OpenRead(P("ExplorationFine.climate.bytes")))fc=ClimateArchiveIO.Read(f);
        using(var f=File.OpenRead(P("ExplorationBiomes.bytes")))biomes=ExplorationBiomeAtlasIO.Read(f);
        var source=new ExplorationAtlasField(macro,mc,fine,fc,512,macro.ExtentX,macro.ExtentZ,1,biomes,macro.ExtentX,macro.ExtentZ);
        return new SurfaceMaterialSampler(new TerrainDetailField(source,714103),new SurfaceProfile(seed:714103,maxSnowDepthMeters:2.4f,winterFraction:.2f));
    }
    static object Summary(string label,IEnumerable<int> ids,IEnumerable<(int,int)> pairs)
    {
        var unique=ids.Distinct().OrderBy(x=>x).ToArray();var lods=pairs.Distinct().OrderBy(p=>p.Item1).ThenBy(p=>p.Item2).ToArray();
        var fallback=lods.Concat(unique.Select(id=>(id,metadata[id].Bytes.Length-1))).Distinct().ToArray();
        return new{label,model_count=unique.Length,ids=unique,all_lods_of_models_bytes=unique.Sum(id=>metadata[id].Bytes.Sum()),
            selected_lod_bytes=lods.Sum(p=>metadata[p.Item1].Bytes[p.Item2]),
            selected_plus_cheapest_fallback_bytes=fallback.Sum(p=>metadata[p.Item1].Bytes[p.Item2]),
            models=unique.Select(id=>new{id,name=metadata[id].Data.Name,hard=metadata[id].Data.HasCollision,resistance=metadata[id].Data.HasResistance,
                all_lod_bytes=metadata[id].Bytes.Sum(),selected_lods=lods.Where(p=>p.Item1==id).Select(p=>p.Item2).ToArray()}).ToArray()};
    }
    static List<PopulationKey> SafetyKeys(WorldPoint eye,float maximumRadius)
    {
        var result=new List<PopulationKey>();
        for(int layer=0;layer<3;layer++)
        {
            double size=layer==2?16:64,margin=16+(layer==2?3:maximumRadius);
            long x0=(long)Math.Floor((eye.X-margin)/size),x1=(long)Math.Floor((eye.X+margin)/size),z0=(long)Math.Floor((eye.Z-margin)/size),z1=(long)Math.Floor((eye.Z+margin)/size);
            for(long z=z0;z<=z1;z++)for(long x=x0;x<=x1;x++)
            {double dx=Math.Max(x*size-eye.X,Math.Max(0,eye.X-(x+1)*size)),dz=Math.Max(z*size-eye.Z,Math.Max(0,eye.Z-(z+1)*size));if(dx*dx+dz*dz<=margin*margin)result.Add(new PopulationKey(x,z,layer));}
        }
        return result;
    }
    static object Measure(SurfaceMaterialSampler sampler,WorldPoint eye,bool small)
    {
        var go=new GameObject("metadata-only prototype hotset");var v=go.AddComponent<VegetationStreaming>();
        try
        {
            v.EnableForestHorizon=false;v.PlantDensity=1;v.RockDistance=small?64:220;v.TreeDistance=small?500:1400;v.GroundCoverDistance=small?20:64;
            v.MaxInstances=small?4096:40000;v.TriangleBudget=small?1000000:32000000;v.WorkMilliseconds=small?.5f:2;
            v.TreeDetailDistance=small?20:100;v.RedwoodDetailDistance=small?6:40;v.TreeMidDistance=small?100:300;
            var population=new ExplorationPopulation(sampler,714103){TreeDensity=1,PlantDensity=1,RockDensity=1};
            Set(v,"surface",sampler);Set(v,"population",population);Set(v,"material",new Material());Set(v,"impostorMaterial",new Material());
            Set(v,"initialized",true);Set(v,"<Ready>k__BackingField",true);
            var models=(IDictionary)Get(v,"models");var modelType=typeof(VegetationStreaming).GetNestedType("Model",BindingFlags.NonPublic);
            float maxRadius=24,maxHeight=90;
            foreach(var pair in metadata)
            {
                var m=pair.Value;var model=Activator.CreateInstance(modelType,true);Set(model,"Data",m.Data);Set(model,"Triangles",m.Triangles);
                var meshes=new Mesh[m.Bytes.Length];
                for(int lod=0;lod<meshes.Length;lod++)meshes[lod]=new Mesh{bounds=new Bounds(new Vector3((m.Data.MinX+m.Data.MaxX)*.5f,(m.Data.MinY+m.Data.MaxY)*.5f,(m.Data.MinZ+m.Data.MaxZ)*.5f),new Vector3(m.Data.MaxX-m.Data.MinX,m.Data.MaxY-m.Data.MinY,m.Data.MaxZ-m.Data.MinZ))};
                Set(model,"Meshes",meshes);models.Add(pair.Key,model);maxRadius=Math.Max(maxRadius,m.Data.Radius*1.415f+2);maxHeight=Math.Max(maxHeight,m.Data.MaxY);
            }
            Set(v,"maximumRadius",maxRadius);Set(v,"maximumHeight",maxHeight);v.SetObserver(eye,new WorldPoint(eye.X,0,eye.Z));
            Call(v,"Plan");var wanted=(HashSet<PopulationKey>)Get(v,"desired");var queue=(List<PopulationKey>)Get(v,"queue");
            var keys=SafetyKeys(eye,maxRadius);Check(keys.All(k=>wanted.Contains(k)),"All spawn safety cells belong to actual plan");
            var first64=queue.AsEnumerable().Reverse().Take(64).ToArray();
            var groups=new List<object>();
            foreach(var g in new[]{(name:"spawn16_required_cells",keys:keys.ToArray()),(name:"first64_nearest_planned_cells",keys:first64)})
            {
                var instances=g.keys.SelectMany(k=>population.Build(k).Instances).ToArray();
                Check(instances.All(i=>metadata.ContainsKey(i.PrototypeId)),"All deterministic IDs exist in frozen pack");
                var pairs=new List<(int,int)>();
                foreach(var i in instances)
                {var model=models[i.PrototypeId];double distance=(double)Call(v,"InstanceDistance",i,metadata[i.PrototypeId].Data);double range=i.PrototypeId<100?v.TreeDistance:i.PrototypeId<200?v.GroundCoverDistance:v.RockDistance;
                 if(distance<=range)pairs.Add((i.PrototypeId,(int)Call(v,"Lod",i,model,distance)));}
                groups.Add(new{g.name,cells=g.keys.Length,instances=instances.Length,summary=Summary(g.name,instances.Select(i=>i.PrototypeId),pairs),
                    caveat="All model interaction records for required cells; desired LOD pairs before triangle-budget demotion, not native visible-frustum culling."});
            }
            object first=null,ready=null;int firstTick=-1,readyTick=-1;var camera=new Camera();
            var clock=Stopwatch.StartNew();
            for(int tick=0;tick<10000&&clock.Elapsed.TotalSeconds<25;tick++)
            {
                Time.unscaledTime=tick/60f;v.Tick(camera);var batches=((IList)Get(v,"batches")).Cast<object>().ToArray();
                var pairs=batches.Select(b=>((int)Get(b,"ModelId"),(int)Get(b,"Lod"))).ToArray();
                if(first==null&&pairs.Length>0){firstTick=tick;first=Summary("first_published_snapshot",pairs.Select(p=>p.Item1),pairs);}
                if(v.IsReady(eye.X,eye.Z,16))
                {
                    readyTick=tick;ready=Summary("snapshot_when_spawn16_ready",pairs.Select(p=>p.Item1),pairs);
                    var cells=(IDictionary)Get(v,"cells");Check(keys.All(k=>cells.Contains(k)&&(bool)Get(cells[k],"Published")),"Actual readiness has every safety cell published");
                    foreach(var key in keys){object cell=cells[key];cells.Remove(key);Check(!v.IsReady(eye.X,eye.Z,16),"Removing each required safety cell blocks actual readiness");cells.Add(key,cell);}
                    break;
                }
            }
            Check(first!=null&&ready!=null,"Bounded metadata run reaches first publication and actual population readiness");
            return new{profile=small?"coordinator_smoke_small":"main_authored_settings",groups,first_published=first,spawn_ready_snapshot=ready,
                first_tick=firstTick,ready_tick=readyTick,population_only_wall_ms=clock.Elapsed.TotalMilliseconds,
                planned_cells=wanted.Count,safety_cells=keys.Count,v.ResidentCells,v.ResidentInstances,v.BudgetDemotions,v.BudgetOmittedSoftInstances,v.MatrixBufferBytes,
                caveat="CPU publication scheduling with tiny metadata Mesh placeholders and all collision/resistance data. No native render/upload, source-load timing, full Bootstrap Ready or lazy-loading speedup."};
        }
        finally{UnityEngine.Object.Destroy(go);}
    }
    static void Main(string[] args)
    {
        string error=null;var results=new List<object>();var rows=new List<object>();
        try
        {
            string root=args[1],pack=Path.Combine(root,"Assets/AshenVoxel/Exploration/Prototypes/ExplorationPrototypes.bytes");hashes["prototype_pack"]=Hash(pack);
            using(var reader=new ExplorationPrototypeReader(File.ReadAllBytes(pack)))
            {
                for(;;)
                {
                    var clock=Stopwatch.StartNew();bool more=reader.TryRead(out ExplorationPrototype p);clock.Stop();if(!more)break;
                    var m=new Meta{Data=p,Bytes=new long[p.LodCount],DecodedBytes=new long[p.LodCount],Triangles=new int[p.LodCount],DecodeMs=clock.Elapsed.TotalMilliseconds};
                    for(int lod=0;lod<p.LodCount;lod++)
                    {var mesh=p.Mesh(lod);m.DecodedBytes[lod]=mesh.VertexCount*11L+mesh.Indices.LongLength*4;m.Bytes[lod]=p.Kind==0&&lod>=3?136:mesh.VertexCount*28L+mesh.Indices.LongLength*4;m.Triangles[lod]=p.Kind==0&&lod>=3?2:mesh.Indices.Length/3;}
                    metadata.Add(p.Id,m);rows.Add(new{p.Id,p.Name,p.Kind,bytes=m.Bytes,decoded_geometry_bytes=m.DecodedBytes,decode_ms=m.DecodeMs,p.CollisionColumns,p.HasResistance});p.ReleaseMeshData();
                }
            }
            Check(metadata.Count==99,"Frozen99 model pack");Check(metadata.Values.Sum(m=>m.Bytes.Sum())==436717216,"Arithmetic full mesh payload matches native smoke counter");
            var sampler=Load(root);var eye=new WorldPoint(-19065,sampler.EvaluateColumn(-19065,-10200).SurfaceHeightMeters+1.65,-10200);
            results.Add(Measure(sampler,eye,true));results.Add(Measure(sampler,eye,false));
        }
        catch(Exception e){error=e.ToString();Console.WriteLine(error);}
        File.WriteAllText(args[0],JsonSerializer.Serialize(new{status=error==null?"PASS":"FAIL",checks,error,data_sha256=hashes,
            scope="Read-only actual-world prototype-demand audit. All99 records decoded once for exact metadata; no lazy loader implemented or measured. Metadata Mesh placeholders retain dimensions/triangle costs and original interaction data; render/upload/FPS are untested.",
            all_runtime_mesh_bytes=metadata.Values.Sum(m=>m.Bytes.Sum()),results,records=rows},new JsonSerializerOptions{WriteIndented=true}));
        Console.WriteLine($"{checks} checks; {metadata.Count} models; {metadata.Values.Sum(m=>m.Bytes.Sum())/1048576.0:F2}MiB full payload");
        if(error!=null)Environment.ExitCode=1;
    }
}
