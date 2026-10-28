// Benchmark-only instrumentation. Never compiled into gameplay assemblies.
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Security.Cryptography;
using AshenVoxel.WorldScale;

public static class TerrainProbe
{
    public static int Workers=1;
    [ThreadStatic] public static double[] Stages;
    [ThreadStatic] static long stamp;
    public static void Begin(){Stages=new double[4];stamp=Stopwatch.GetTimestamp();}
    public static void Mark(int stage){long now=Stopwatch.GetTimestamp();Stages[stage]=(now-stamp)*1000.0/Stopwatch.Frequency;stamp=now;}
    public static void Rows(int n,CancellationToken cancel,Action<int> row)
    {
        if(Workers<=1||n<128){for(int z=-1;z<=n;z++)row(z);return;}
        Parallel.For(-1,n+1,new ParallelOptions{MaxDegreeOfParallelism=Workers,CancellationToken=cancel},row);
    }
    public static SurfaceMaterialSampler Load(string samples)
    {
        HeightAtlas macro,fine;ClimateAtlas mc,fc;ExplorationBiomeAtlas biomes;
        using(var f=File.OpenRead(Path.Combine(samples,"ExplorationMacro.bytes")))macro=AtlasIO.Read(f);
        using(var f=File.OpenRead(Path.Combine(samples,"ExplorationFine.bytes")))fine=AtlasIO.Read(f);
        using(var f=File.OpenRead(Path.Combine(samples,"ExplorationMacro.climate.bytes")))mc=ClimateArchiveIO.Read(f);
        using(var f=File.OpenRead(Path.Combine(samples,"ExplorationFine.climate.bytes")))fc=ClimateArchiveIO.Read(f);
        using(var f=File.OpenRead(Path.Combine(samples,"ExplorationBiomes.bytes")))biomes=ExplorationBiomeAtlasIO.Read(f);
        var field=new ExplorationAtlasField(macro,mc,fine,fc,512,macro.ExtentX,macro.ExtentZ,1,biomes,macro.ExtentX,macro.ExtentZ);
        // Snapshot's settings are checked by prepare.py before using these constants.
        return new SurfaceMaterialSampler(new TerrainDetailField(field,714103),new SurfaceProfile(seed:714103,maxSnowDepthMeters:2.4,enableSnow:true,winterFraction:.2,temperatureBiasC:0));
    }
    public sealed class Case
    {
        public string label;public LodTile tile;public int n;
        public Case(string label,double x,double z,int level,int n){this.label=label;double size=25.6*(1L<<level);long tx=(long)Math.Floor(x/size),tz=(long)Math.Floor(z/size);tile=new LodTile(new TileKey(tx,tz,level),tx*size,tz*size,size,-12000,16000);this.n=n;}
    }
    public static Case[] Cases()=>new[]{
        new Case("spawn-10cm",-19065,-10200,0,256),new Case("spawn-east-10cm",-19039,-10200,0,256),
        new Case("decoded-edge-10cm",-21720,-10200,0,256),new Case("macro-10cm",-13000,-10200,0,256),
        new Case("near-lod",-19065,-10200,2,128),new Case("middle-lod",-18000,-10000,5,128),
        new Case("distant-lod",-14000,-6000,8,128),new Case("rear-lod",-24000,-17000,9,32)};
    public static VoxelLodSurface Build(SurfaceMaterialSampler sampler,Case c)
    {
        double x=c.tile.OriginXMeters,z=c.tile.OriginZMeters,size=c.tile.SizeMeters;
        double floor=((IWorldFieldBounds)sampler.BaseField).QueryBounds(x-size*2,z-size*2,x+size*3,z+size*3).Minimum-1;
        if(Workers==1)return BaselineSurfaceBuilder.Build(sampler,c.tile,c.n,floor);
        if(Workers==0)return UnboxedSurfaceBuilder.Build(sampler,c.tile,c.n,floor);
        return VoxelLodSurface.Build(sampler,c.tile,c.n,floor);
    }
    public static string Hash(VoxelLodSurface d)
    {
        using(var hash=SHA256.Create())using(var stream=new CryptoStream(Stream.Null,hash,CryptoStreamMode.Write))using(var w=new BinaryWriter(stream))
        {
            w.Write(d.AnchorY);w.Write(d.CellStride);w.Write(d.Quads);
            foreach(float h in d.Heights)w.Write(h);
            foreach(var f in d.Faces){w.Write(f.X);w.Write(f.Y);w.Write(f.Z);w.Write(f.U);w.Write(f.V);w.Write(f.Axis);w.Write(f.Positive);w.Write(f.Tint);w.Write((byte)f.Material);w.Write(f.Wetness);w.Write(f.A0);w.Write(f.A1);w.Write(f.A2);w.Write(f.A3);}
            w.Flush();stream.FlushFinalBlock();return BitConverter.ToString(hash.Hash).Replace("-","").ToLowerInvariant();
        }
    }
    public static double Median(IEnumerable<double> a){var x=a.OrderBy(n=>n).ToArray();return x[x.Length/2];}
}
