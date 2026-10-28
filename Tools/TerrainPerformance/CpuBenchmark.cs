using System;
using System.IO;
using System.Linq;
using System.Diagnostics;
using System.Text.Json;
using System.Collections.Generic;
using System.Threading.Tasks;

static class CpuBenchmark
{
    static void Main(string[] args)
    {
        var sampler=TerrainProbe.Load(args[0]);var cases=TerrainProbe.Cases();
        int[] modes=args.Length>2?args[2].Split(',').Select(int.Parse).ToArray():new[]{1};
        var results=new List<object>();var hashes=new Dictionary<string,string>();
        // Warm each location and JIT path; retain no mesh output between operations.
        foreach(var c in cases){var d=TerrainProbe.Build(sampler,c);hashes[c.label]=TerrainProbe.Hash(d);}
        foreach(int workers in modes){TerrainProbe.Workers=workers;foreach(var c in cases)TerrainProbe.Build(sampler,c);}
        for(int repeat=0;repeat<5;repeat++)foreach(int workers in (repeat%2==0?modes:modes.Reverse().ToArray()))
        {
            TerrainProbe.Workers=workers;TerrainProbe.Build(sampler,cases[0]);
            GC.Collect();GC.WaitForPendingFinalizers();GC.Collect();
            foreach(var c in (repeat%2==0?cases:cases.Reverse().ToArray()))
            {
                long allocated=GC.GetTotalAllocatedBytes(true);var watch=Stopwatch.StartNew();
                var data=TerrainProbe.Build(sampler,c);watch.Stop();
                long bytes=GC.GetTotalAllocatedBytes(true)-allocated;var stages=TerrainProbe.Stages;
                string digest=TerrainProbe.Hash(data);if(digest!=hashes[c.label])throw new Exception("Geometry/appearance mismatch "+c.label);
                results.Add(new{workers,repeat,label=c.label,n=c.n,level=c.tile.Key.Level,total_ms=watch.Elapsed.TotalMilliseconds,
                    sampling_ms=stages[0],tops_ms=stages[1],sides_ms=stages[2],merge_ms=stages[3],allocated_bytes=bytes,
                    faces=data.Faces.Count,unmerged=data.UnmergedFaceCount,sha256=digest});
                Console.WriteLine($"workers={workers} repeat={repeat} {c.label}: {watch.Elapsed.TotalMilliseconds:F2}ms");
            }
        }
        var report=new{scope="Windows .NET8 actual current world tile microbenchmark; no Unity, GPU or end-to-end FPS claim",source=sampler.SourceIdentity,
            processor_count=Environment.ProcessorCount,results,private_bytes=Process.GetCurrentProcess().PrivateMemorySize64,peak_working_set=Process.GetCurrentProcess().PeakWorkingSet64};
        File.WriteAllText(args[1],JsonSerializer.Serialize(report,new JsonSerializerOptions{WriteIndented=true}));
    }
}
