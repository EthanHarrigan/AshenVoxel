using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using AshenVoxel.WorldScale;
using AshenVoxel.WorldScaleRuntime;
using UnityEngine;

static class Program
{
    const BindingFlags Flags=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
    static object Get(object o,string n)=>o.GetType().GetField(n,Flags).GetValue(o);
    static void Set(object o,string n,object v)=>o.GetType().GetField(n,Flags).SetValue(o,v);
    static object Call(object o,string n,params object[] a)=>o.GetType().GetMethod(n,Flags).Invoke(o,a);
    static string Hash(string p){using(var h=SHA256.Create())using(var f=File.OpenRead(p))return Convert.ToHexString(h.ComputeHash(f)).ToLowerInvariant();}
    static double Distance(LodTile t,WorldPoint eye)
    {double dx=Math.Max(t.OriginXMeters-eye.X,Math.Max(0,eye.X-t.OriginXMeters-t.SizeMeters)),dz=Math.Max(t.OriginZMeters-eye.Z,Math.Max(0,eye.Z-t.OriginZMeters-t.SizeMeters));return Math.Sqrt(dx*dx+dz*dz);}
    static void Main(string[] args)
    {
        string error=null;object result=null;var hashes=new Dictionary<string,string>();var go=new GameObject("shipping planner only");var w=go.AddComponent<WorldExplorer>();
        try
        {
            string root=args[1],asset=Path.Combine(root,"Assets/AshenVoxel/Settings/ExplorationSettings.asset"),scene=Path.Combine(root,"Assets/Scenes/Exploration.unity");
            string yaml=File.ReadAllText(asset);hashes["settings_asset"]=Hash(asset);hashes["scene"]=Hash(scene);hashes["saved_options"]=Hash(args[2]);
            double Setting(string n){var m=Regex.Match(yaml,@"(?m)^\s*"+Regex.Escape(n)+@":\s*([^\r\n]+)");if(!m.Success)throw new Exception("Missing setting "+n);return double.Parse(m.Groups[1].Value,CultureInfo.InvariantCulture);}
            int limit=(int)Setting("MaximumTerrainTiles");double view=Setting("ViewDistanceKm"),fov=Setting("FieldOfView"),radius=Setting("VoxelFullDetailRadius"),target=Setting("VoxelTargetPixels"),pitch=Setting("SpawnPitch"),yaw=Setting("SpawnYaw");
            double authoredView=view,authoredFov=fov;bool saved=Regex.IsMatch(File.ReadAllText(scene),@"(?m)^\s*LoadSavedOptions:\s*1\s*$");
            if(saved){using(var doc=JsonDocument.Parse(File.ReadAllText(args[2]))){view=Math.Max(20,Math.Min(40,doc.RootElement.GetProperty("view").GetDouble()));fov=Math.Max(45,Math.Min(110,doc.RootElement.GetProperty("fov").GetDouble()));}}
            string d=Path.Combine(root,"Assets/AshenVoxel/Exploration/Samples");
            string P(string n){string p=Path.Combine(d,n);hashes[n]=Hash(p);return p;}
            HeightAtlas macro,fine;ClimateAtlas mc,fc;ExplorationBiomeAtlas biomes;
            using(var f=File.OpenRead(P("ExplorationMacro.bytes")))macro=AtlasIO.Read(f);
            using(var f=File.OpenRead(P("ExplorationFine.bytes")))fine=AtlasIO.Read(f);
            using(var f=File.OpenRead(P("ExplorationMacro.climate.bytes")))mc=ClimateArchiveIO.Read(f);
            using(var f=File.OpenRead(P("ExplorationFine.climate.bytes")))fc=ClimateArchiveIO.Read(f);
            using(var f=File.OpenRead(P("ExplorationBiomes.bytes")))biomes=ExplorationBiomeAtlasIO.Read(f);
            var source=new ExplorationAtlasField(macro,mc,fine,fc,512,macro.ExtentX,macro.ExtentZ,1,biomes,macro.ExtentX,macro.ExtentZ);
            var field=new TerrainDetailField(source,714103);var profile=new SurfaceProfile(seed:714103,maxSnowDepthMeters:2.4f,winterFraction:.2f);
            var sampler=new SurfaceMaterialSampler(field,profile);var eye=new WorldPoint(-19065,sampler.EvaluateColumn(-19065,-10200).SurfaceHeightMeters+Setting("EyeHeight"),-10200);
            w.UseVoxelLodTerrain=true;w.AsyncPlanning=false;w.MaximumTiles=limit;w.ViewDistanceKm=(float)view;w.VoxelFullDetailRadius=(float)radius;w.VoxelTargetPixels=(float)target;w.ShowVisualWater=false;
            w.ConfigureExternal(field,profile,eye,Quaternion.Euler((float)pitch,(float)yaw,0));w.MainCamera.fieldOfView=(float)fov;
            var rows=new List<object>();var minima=new List<object>();
            int NearLevel()
            {
                long gx=(long)Math.Floor(eye.X/25.6),gz=(long)Math.Floor(eye.Z/25.6);
                var tiles=((IDictionary)Get(w,"desiredTiles")).Values.Cast<LodTile>();
                return tiles.Single(t=>gx>=t.Key.X*(1L<<t.Key.Level)&&gx<(t.Key.X+1)*(1L<<t.Key.Level)&&gz>=t.Key.Z*(1L<<t.Key.Level)&&gz<(t.Key.Z+1)*(1L<<t.Key.Level)).Key.Level;
            }
            foreach(float distance in new[]{20f,(float)view}.Distinct())
            {
                w.ViewDistanceKm=distance;
                foreach(int cap in new[]{192,256,384,limit}.Distinct())
                {
                    w.MaximumTiles=cap;Call(w,"RequestPlan");int level=NearLevel();
                    rows.Add(new{view_km=distance,cap,tiles=((IDictionary)Get(w,"desiredTiles")).Count,near_level=level,near_tile_m=25.6*(1L<<level),initial_near_spacing_m=3.2*(1L<<level)});
                }
                int minimum=-1,calls=0;var transitions=new List<object>();int prior=-1;
                // Exhaust supported integer caps to avoid assuming greedy plans are monotonic.
                for(int cap=64;cap<=limit;cap++)
                {
                    w.MaximumTiles=cap;Call(w,"RequestPlan");calls++;int level=NearLevel();
                    if(level!=prior){transitions.Add(new{cap,near_level=level,tiles=((IDictionary)Get(w,"desiredTiles")).Count});prior=level;}
                    if(level==0){minimum=cap;break;}
                }
                minima.Add(new{view_km=distance,minimum_supported_cap_for_observer_level0=minimum,plan_calls=calls,transitions});
            }
            result=new{authored_maximum_tiles=limit,authored_view_km=authoredView,effective_view_km=view,saved_options_applied=saved,
                source_identity=sampler.SourceIdentity,geometry_builds=0,rows,minima};
        }
        catch(Exception e){error=e.ToString();Console.WriteLine(error);}
        finally{UnityEngine.Object.Destroy(go);}
        File.WriteAllText(args[0],JsonSerializer.Serialize(new{status=error==null?"PASS":"FAIL",error,data_sha256=hashes,result,
            scope="Read-only capacity feasibility at actual spawn:20km smoke and serialized shipping view,192/256/384/authored cap. Exhaustive supported integer caps64..authored until first observer L0; no assumption of monotonic greedy allocation, no geometry or policy/budget adoption."},new JsonSerializerOptions{WriteIndented=true}));
        Console.WriteLine(error==null?"PASS capacity feasibility; no geometry builds":"FAIL");if(error!=null)Environment.ExitCode=1;
    }
}
