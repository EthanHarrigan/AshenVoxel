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
using AshenVoxel.WorldScaleRuntime;
using UnityEngine;

// Read-only structural comparison; no candidate policy or geometry uploads.
static class Program
{
    const BindingFlags Flags=BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public;
    static object Get(object o,string n)=>o.GetType().GetField(n,Flags).GetValue(o);
    static string Hash(string p){using(var h=SHA256.Create())using(var s=File.OpenRead(p))return Convert.ToHexString(h.ComputeHash(s)).ToLowerInvariant();}
    static bool GridAdjacent(TileKey a,TileKey b)
    {
        long sa=1L<<a.Level,sb=1L<<b.Level,ax=a.X*sa,az=a.Z*sa,bx=b.X*sb,bz=b.Z*sb;
        return ((ax+sa==bx||bx+sb==ax)&&Math.Min(az+sa,bz+sb)>Math.Max(az,bz))||
               ((az+sa==bz||bz+sb==az)&&Math.Min(ax+sa,bx+sb)>Math.Max(ax,bx));
    }
    static void Main(string[] args)
    {
        string error=null;int checks=0;var rows=new List<object>();var hashes=new Dictionary<string,string>();
        try
        {
            string d=Path.Combine(args[1],"Assets/AshenVoxel/Exploration/Samples");
            string P(string n){string p=Path.Combine(d,n);hashes[n]=Hash(p);return p;}
            HeightAtlas macro,fine;ClimateAtlas mc,fc;ExplorationBiomeAtlas biomes;
            using(var f=File.OpenRead(P("ExplorationMacro.bytes")))macro=AtlasIO.Read(f);
            using(var f=File.OpenRead(P("ExplorationFine.bytes")))fine=AtlasIO.Read(f);
            using(var f=File.OpenRead(P("ExplorationMacro.climate.bytes")))mc=ClimateArchiveIO.Read(f);
            using(var f=File.OpenRead(P("ExplorationFine.climate.bytes")))fc=ClimateArchiveIO.Read(f);
            using(var f=File.OpenRead(P("ExplorationBiomes.bytes")))biomes=ExplorationBiomeAtlasIO.Read(f);
            var source=new ExplorationAtlasField(macro,mc,fine,fc,512,macro.ExtentX,macro.ExtentZ,1,biomes,macro.ExtentX,macro.ExtentZ);
            var field=new TerrainDetailField(source,714103);var profile=new SurfaceProfile(seed:714103,maxSnowDepthMeters:2.4f,winterFraction:.2f);
            var sampler=new SurfaceMaterialSampler(field,profile);var eye=new WorldPoint(-19065,sampler.EvaluateColumn(-19065,-10200).SurfaceHeightMeters+1.65,-10200);
            int unbalancedTotal=0;
            foreach(int limit in new[]{64,192,512})
            {
                var go=new GameObject("initial plan shape only");var w=go.AddComponent<WorldExplorer>();
                try
                {
                    w.UseVoxelLodTerrain=true;w.AsyncPlanning=false;w.MaximumTiles=limit;w.ViewDistanceKm=20;w.VoxelFullDetailRadius=16;w.ShowVisualWater=false;
                    var clock=Stopwatch.StartNew();w.ConfigureExternal(field,profile,eye,Quaternion.Euler(20,20,0));clock.Stop();
                    var tiles=((IDictionary)Get(w,"desiredTiles")).Values.Cast<LodTile>().ToArray();
                    if(tiles.Length==0||tiles.Length>limit)throw new Exception("Plan count outside configured cap");checks++;
                    bool Contains(LodTile t,double x,double z)=>x>=t.OriginXMeters&&x<t.OriginXMeters+t.SizeMeters&&z>=t.OriginZMeters&&z<t.OriginZMeters+t.SizeMeters;
                    for(int ring=0;ring<=4;ring++)for(int a=0;a<72;a++)
                    {double radius=20000.0*ring/4,x=eye.X+radius*Math.Sin(a*Math.PI/36),z=eye.Z+radius*Math.Cos(a*Math.PI/36);if(tiles.Count(t=>Contains(t,x,z))!=1)throw new Exception("Sampled disk coverage missing or overlaps");checks++;}
                    int adjacent=0,floatMissed=0;var unbalanced=new List<object>();foreach(var a in tiles)foreach(var b in tiles)if(GridAdjacent(a.Key,b.Key))
                    {
                        adjacent++;bool detected=LodPlanner.Adjacent(a,b);if(!detected)floatMissed++;
                        if(Math.Abs(a.Key.Level-b.Key.Level)>1)
                        {
                            var small=a.Key.Level<b.Key.Level?a:b;var large=a.Key.Level<b.Key.Level?b:a;
                            var pk=new TileKey(small.Key.X>>1,small.Key.Z>>1,small.Key.Level+1);double size=25.6*(1L<<pk.Level);
                            var parent=new LodTile(pk,pk.X*size,pk.Z*size,size,0,10);
                            unbalanced.Add(new{a=a.Key.ToString(),b=b.Key.ToString(),a_size=a.SizeMeters,b_size=b.SizeMeters,floating_detected=detected,
                                finer_parent=pk.ToString(),parent_integer_adjacent=GridAdjacent(pk,large.Key),parent_float_adjacent=LodPlanner.Adjacent(parent,large)});
                        }
                    }
                    unbalancedTotal+=unbalanced.Count;
                    checks++;
                    if(w.HasActiveCoverage||w.ResidentTiles!=0||!(bool)Get(w,"coarseStartup"))throw new Exception("Unexpected early active surface");checks++;
                    var near=tiles.Single(t=>Contains(t,eye.X,eye.Z));
                    rows.Add(new{maximum_tiles=limit,desired_tiles=tiles.Length,pending_tiles=w.PendingTiles,initial_plan_ms=clock.Elapsed.TotalMilliseconds,
                        near_level=near.Key.Level,near_tile_m=near.SizeMeters,near_initial_spacing_m=near.SizeMeters/8,
                        levels=tiles.GroupBy(t=>t.Key.Level).Select(g=>new{level=g.Key,tiles=g.Count(),tile_m=g.First().SizeMeters,initial_spacing_m=g.First().SizeMeters/8}).ToArray(),
                        sampled_coverage_points=360,adjacent_directed_pairs=adjacent,float_missed_adjacencies=floatMissed,unbalanced_neighbors=unbalanced,has_active_before_any_build=w.HasActiveCoverage});
                }
                finally{UnityEngine.Object.Destroy(go);}
            }
            if(unbalancedTotal>0)throw new Exception("Existing planner reported "+unbalancedTotal+" directed adjacent pairs differing by more than one LOD level; diagnostics preserved per plan.");
        }
        catch(Exception e){error=e.ToString();Console.WriteLine(error);}
        File.WriteAllText(args[0],JsonSerializer.Serialize(new{status=error==null?"PASS":"FAIL",checks,error,data_sha256=hashes,rows,
            scope="Actual source initial planner at one spawn/20km with64/192/512 caps. Sampled XZ disk and adjacency check; no geometry built or native upload, no startup policy implemented. Configure/plan stopwatch is CPU observation, not Ready or full Start speed."},new JsonSerializerOptions{WriteIndented=true}));
        Console.WriteLine($"{checks} structural checks; {rows.Count} initial plans; no terrain builds");if(error!=null)Environment.ExitCode=1;
    }
}
