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
            Set(w,"coarseStartup",false); // Query final target math only; never tick/build/activate.
            var tiles=((IDictionary)Get(w,"desiredTiles")).Values.Cast<LodTile>().ToArray();
            var tileRows=new List<object>();var samples=new List<object>();
            foreach(var t in tiles)
            {
                bool visible=(bool)Call(w,"InVoxelView",t);int n=(int)Call(w,"VoxelQuadsFor",t);
                double farthest=0;foreach(double x in new[]{t.OriginXMeters,t.OriginXMeters+t.SizeMeters})foreach(double z in new[]{t.OriginZMeters,t.OriginZMeters+t.SizeMeters})farthest=Math.Max(farthest,Math.Sqrt((x-eye.X)*(x-eye.X)+(z-eye.Z)*(z-eye.Z)));
                tileRows.Add(new{key=t.Key.ToString(),level=t.Key.Level,tile_m=t.SizeMeters,distance_m=Distance(t,eye),eligible=visible,extends_outside_contact=farthest>radius,target_columns=n,target_spacing_m=t.SizeMeters/n});
            }
            foreach(double r in new[]{radius+1,128,256,512,1024,2048,4096,8192})for(int angle=0;angle<360;angle+=5)
            {
                double x=eye.X+Math.Sin(angle*Math.PI/180)*r,z=eye.Z+Math.Cos(angle*Math.PI/180)*r;long gx=(long)Math.Floor(x/25.6),gz=(long)Math.Floor(z/25.6);
                var t=tiles.Single(a=>gx>=a.Key.X*(1L<<a.Key.Level)&&gx<(a.Key.X+1)*(1L<<a.Key.Level)&&gz>=a.Key.Z*(1L<<a.Key.Level)&&gz<(a.Key.Z+1)*(1L<<a.Key.Level));
                bool visible=(bool)Call(w,"InVoxelView",t);int n=(int)Call(w,"VoxelQuadsFor",t);
                samples.Add(new{radius_m=r,angle_degrees=angle,key=t.Key.ToString(),level=t.Key.Level,tile_m=t.SizeMeters,eligible=visible,target_columns=n,target_spacing_m=t.SizeMeters/n});
            }
            result=new{settings_maximum_tiles=limit,authored_view_km=authoredView,authored_fov=authoredFov,saved_options_applied=saved,effective_view_km=view,effective_fov=fov,
                contact_radius_m=radius,target_pixels=target,spawn_pitch=pitch,spawn_yaw=yaw,viewport_width=Screen.width,viewport_height=Screen.height,
                source_identity=sampler.SourceIdentity,tiles=tiles.Length,geometry_builds=0,tile_rows=tileRows,ring_samples=samples};
        }
        catch(Exception e){error=e.ToString();Console.WriteLine(error);}
        finally{UnityEngine.Object.Destroy(go);}
        File.WriteAllText(args[0],JsonSerializer.Serialize(new{status=error==null?"PASS":"FAIL",error,data_sha256=hashes,result,
            scope="One actual serialized main512/30km profile with saved view/FOV when scene enables options. Static spawn and shim viewport; real current InVoxelView/target math, no geometry/upload/capacity admission or native visual/FPS claim. Ring eligibility is tile refinement eligibility, not exact point visibility/occlusion."},new JsonSerializerOptions{WriteIndented=true}));
        Console.WriteLine(error==null?"PASS serialized profile/ring measurement; no geometry builds":"FAIL");if(error!=null)Environment.ExitCode=1;
    }
}
