using System;using System.Collections.Generic;using System.Diagnostics;using System.IO;using System.Linq;
using AshenVoxel.WorldScale;
public static class LodMeasurements
{
    [Serializable] public sealed class Row{public string key,phase;public int level,columns,faces,repeat;public double tile_m,cell_m,build_ms,tiles_per_second,payload_mib,distance,projected_pixels;}
    [Serializable] public sealed class PlanRow{public int level,count,in_view,limited;public double tile_m,spacing_min,spacing_max,pixels_max;}
    public static SurfaceMaterialSampler Load(string root)
    {
        string p=Path.Combine(root,"Assets/AshenVoxel/Exploration/Samples");
        HeightAtlas macro,fine;ClimateAtlas mc,fc;ExplorationBiomeAtlas biomes;
        using(var f=File.OpenRead(Path.Combine(p,"ExplorationMacro.bytes")))macro=AtlasIO.Read(f);
        using(var f=File.OpenRead(Path.Combine(p,"ExplorationFine.bytes")))fine=AtlasIO.Read(f);
        using(var f=File.OpenRead(Path.Combine(p,"ExplorationMacro.climate.bytes")))mc=ClimateArchiveIO.Read(f);
        using(var f=File.OpenRead(Path.Combine(p,"ExplorationFine.climate.bytes")))fc=ClimateArchiveIO.Read(f);
        using(var f=File.OpenRead(Path.Combine(p,"ExplorationBiomes.bytes")))biomes=ExplorationBiomeAtlasIO.Read(f);
        var field=new ExplorationAtlasField(macro,mc,fine,fc,512,macro.ExtentX,macro.ExtentZ,1,biomes,macro.ExtentX,macro.ExtentZ);
        return new SurfaceMaterialSampler(new TerrainDetailField(field,714103),new SurfaceProfile(seed:714103,maxSnowDepthMeters:2.4f,winterFraction:.2f));
    }
    public static WorldPoint Eye(SurfaceMaterialSampler s)=>new WorldPoint(-19065,s.EvaluateColumn(-19065,-10200).SurfaceHeightMeters+1.65,-10200);
    public static double Distance(LodTile t,WorldPoint p){double x=Math.Max(0,Math.Max(t.OriginXMeters-p.X,p.X-t.OriginXMeters-t.SizeMeters)),z=Math.Max(0,Math.Max(t.OriginZMeters-p.Z,p.Z-t.OriginZMeters-t.SizeMeters)),y=Math.Max(0,Math.Max(t.MinimumHeight-p.Y,p.Y-t.MaximumHeight));return Math.Sqrt(x*x+y*y+z*z);}
    public const double Focal=900*.5/.7673269879789604;
    public static bool InView(LodTile t,WorldPoint p)=>VoxelLodQuality.InView(t,p,Math.Sin(20*Math.PI/180),Math.Cos(20*Math.PI/180),Math.Atan(.7673269879789604*1.6)+20*Math.PI/180);
    public static int Columns(LodTile t,WorldPoint p)=>VoxelLodQuality.Select(t.SizeMeters,Distance(t,p),Focal,1.5,32,InView(t,p),false);
    public static LodPlan Plan(SurfaceMaterialSampler s,WorldPoint p)
    {
        var field=new SurfaceWorldField(s.BaseField,s.Profile,new HeightBounds(-20000,30000));
        return LodPlanner.Build(field,p,new LodSettings(25.6,14,30000,2.2,512,-20000,30000));
    }
    public static List<PlanRow> Describe(LodPlan plan,WorldPoint p)=>plan.Tiles.GroupBy(t=>t.Key.Level).Select(g=>new PlanRow{level=g.Key,count=g.Count(),in_view=g.Count(t=>InView(t,p)),limited=g.Count(t=>InView(t,p)&&t.SizeMeters/Columns(t,p)*Focal/Math.Max(1,Distance(t,p))>1.5&&t.SizeMeters/Columns(t,p)>.10001),tile_m=g.First().SizeMeters,spacing_min=g.Min(t=>t.SizeMeters/Columns(t,p)),spacing_max=g.Max(t=>t.SizeMeters/Columns(t,p)),pixels_max=g.Max(t=>t.SizeMeters/Columns(t,p)*Focal/Math.Max(1,Distance(t,p)))}).ToList();
    public static List<Tuple<LodTile,int,string>> Cases(LodPlan plan,WorldPoint p)
    {
        var list=new List<Tuple<LodTile,int,string>>();
        foreach(var group in plan.Tiles.GroupBy(t=>t.Key.Level))
        {
            var tile=group.OrderBy(t=>InView(t,p)?0:1).ThenBy(t=>Distance(t,p)).First();int n=Columns(tile,p);
            list.Add(Tuple.Create(tile,8,"startup"));list.Add(Tuple.Create(tile,n,"refined"));
            if(n==128)list.Add(Tuple.Create(tile,256,"double-grid-experiment"));
        }
        return list;
    }
    public static Row Measure(SurfaceMaterialSampler s,WorldPoint p,Tuple<LodTile,int,string> c,int repeat)
    {
        var t=c.Item1;int n=c.Item2;double x=t.OriginXMeters,z=t.OriginZMeters,size=t.SizeMeters;
        double floor=((IWorldFieldBounds)s.BaseField).QueryBounds(x-size*2,z-size*2,x+size*3,z+size*3).Minimum-1;
        var clock=Stopwatch.StartNew();var data=VoxelLodSurface.Build(s,t,n,floor,parallelSampling:true);clock.Stop();
        return new Row{key=t.Key.ToString(),phase=c.Item3,level=t.Key.Level,columns=n,repeat=repeat,tile_m=size,cell_m=size/n,faces=data.Faces.Count,build_ms=clock.Elapsed.TotalMilliseconds,tiles_per_second=1000/clock.Elapsed.TotalMilliseconds,payload_mib=data.Faces.Count*120.0/1048576,distance=Distance(t,p),projected_pixels=size/n*Focal/Math.Max(1,Distance(t,p))};
    }
}
