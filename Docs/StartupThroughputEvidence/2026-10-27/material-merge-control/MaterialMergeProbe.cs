using System;
using System.IO;
using System.Linq;
using System.Diagnostics;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text.Json;
using AshenVoxel.WorldScale;

static class MaterialMergeProbe
{
    sealed class Case
    {
        public string label; public LodTile tile; public int n;
        public Case(string label,double x,double z,int level,int n)
        {
            this.label=label;this.n=n;double size=25.6*(1L<<level);
            long tx=(long)Math.Floor(x/size),tz=(long)Math.Floor(z/size);
            tile=new LodTile(new TileKey(tx,tz,level),tx*size,tz*size,size,-12000,16000);
        }
    }
    static VoxelLodSurface Build(SurfaceMaterialSampler sampler,Case c)
    {
        double x=c.tile.OriginXMeters,z=c.tile.OriginZMeters,size=c.tile.SizeMeters;
        double floor=((IWorldFieldBounds)sampler.BaseField).QueryBounds(x-size*2,z-size*2,x+size*3,z+size*3).Minimum-1;
        return VoxelLodSurface.Build(sampler,c.tile,c.n,floor,parallelSampling:true);
    }
    static string HeightHash(VoxelLodSurface d)
    {
        using(var h=SHA256.Create())using(var s=new MemoryStream())using(var w=new BinaryWriter(s))
        {w.Write(d.AnchorY);w.Write(d.CellStride);w.Write(d.Quads);foreach(float v in d.Heights)w.Write(v);w.Flush();return Convert.ToHexString(h.ComputeHash(s.ToArray())).ToLowerInvariant();}
    }
    static void Main(string[] args)
    {
        var sampler=LodMeasurements.Load(args[0]);
        var refined=new[]{new Case("spawn-10cm",-19065,-10200,0,256),new Case("spawn-east-10cm",-19039,-10200,0,256),
            new Case("decoded-edge-10cm",-21720,-10200,0,256),new Case("macro-10cm",-13000,-10200,0,256),
            new Case("near-lod",-19065,-10200,2,128),new Case("middle-lod",-18000,-10000,5,128),
            new Case("distant-lod",-14000,-6000,8,128),new Case("rear-lod",-24000,-17000,9,32)};
        var cases=refined.SelectMany(c=>new[]{new Case(c.label+"-coverage",c.tile.OriginXMeters+.01,c.tile.OriginZMeters+.01,c.tile.Key.Level,8),c}).ToArray();
        var rows=new List<object>();var failures=new List<object>();var fingerprints=new Dictionary<string,string>();
        foreach(var c in cases)try{Build(sampler,c);}catch(VoxelLodCapacityException){/* Record repeated capacity failure below. */}
        for(int repeat=0;repeat<3;repeat++)foreach(var c in (repeat%2==0?cases:cases.Reverse().ToArray()))
        {
            GC.Collect();GC.WaitForPendingFinalizers();GC.Collect();long before=GC.GetTotalAllocatedBytes(true);var sw=Stopwatch.StartNew();
            try
            {
                var d=Build(sampler,c);sw.Stop();long allocated=GC.GetTotalAllocatedBytes(true)-before;
                string hh=HeightHash(d);if(fingerprints.TryGetValue(c.label,out string previous)&&previous!=hh)throw new Exception("Nondeterministic heights");fingerprints[c.label]=hh;
                var areas=new double[6];foreach(var f in d.Faces)areas[f.Axis*2+(f.Positive?1:0)]+=(double)f.U*f.V;
                var materials=d.Faces.GroupBy(f=>f.Material).Select(g=>new{material=g.Key.ToString(),faces=g.Count(),area_m2=g.Sum(f=>(double)f.U*f.V)}).ToArray();
                rows.Add(new{c.label,repeat,key=c.tile.Key.ToString(),level=c.tile.Key.Level,columns=c.n,tile_m=c.tile.SizeMeters,cell_m=c.tile.SizeMeters/c.n,
                    build_ms=sw.Elapsed.TotalMilliseconds,allocated_bytes=allocated,faces=d.Faces.Count,unmerged=d.UnmergedFaceCount,
                    mesh_payload_bytes=(long)d.Faces.Count*120,height_sha256=hh,axis_areas_m2=areas,materials});
                Console.WriteLine($"{repeat} {c.label}: {d.Faces.Count} faces, {sw.Elapsed.TotalMilliseconds:F2} ms");
            }
            catch(VoxelLodCapacityException ex){sw.Stop();failures.Add(new{c.label,repeat,error=ex.Message,elapsed_ms=sw.Elapsed.TotalMilliseconds});}
        }
        var process=Process.GetCurrentProcess();process.Refresh();
        File.WriteAllText(args[1],JsonSerializer.Serialize(new{status=failures.Count==0?"PASS":"CAPACITY_LIMIT",scope="Windows .NET8 actual14fb32 tile microbenchmark. One tile at a time, two sampling workers, same frozen data/settings; no Unity/upload/FPS or appearance acceptance. Memory allocation is cumulative per build, not native residency.",
            source=sampler.SourceIdentity,rows,failures,private_bytes=process.PrivateMemorySize64,peak_working_set=process.PeakWorkingSet64},new JsonSerializerOptions{WriteIndented=true}));
    }
}
