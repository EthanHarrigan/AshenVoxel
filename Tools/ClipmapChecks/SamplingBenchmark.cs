using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text.Json;
using AshenVoxel.WorldScale;

static class SamplingBenchmark
{
    static string Root;
    static T Read<T>(string name,Func<Stream,T> reader)
    {using var stream=File.OpenRead(Path.Combine(Root,"Assets/AshenVoxel/Exploration/Samples",name));return reader(stream);}
    static void Main(string[] args)
    {
        Root=args[0];string output=args[1];bool allLevels=args.Length>2&&args[2]=="--all-levels";var loading=Stopwatch.StartNew();
        var macro=Read("ExplorationMacro.bytes",AtlasIO.Read);
        var macroClimate=Read("ExplorationMacro.climate.bytes",ClimateArchiveIO.Read);
        var fine=Read("ExplorationFine.bytes",AtlasIO.Read);
        var fineClimate=Read("ExplorationFine.climate.bytes",ClimateArchiveIO.Read);
        var biomes=Read("ExplorationBiomes.bytes",ExplorationBiomeAtlasIO.Read);
        var basis=new ExplorationAtlasField(macro,macroClimate,fine,fineClimate,512,macro.ExtentX,macro.ExtentZ,1,biomes,macro.ExtentX,macro.ExtentZ);
        var live=new ExplorationRefinementField(basis,"0647786260a982ef6793a9e77fac850f0d9e23b8ae8ec3319fe6978b6538c84a");
        var far=Read("ExplorationFar40.bytes",BakedFarTerrainIO.Read);
        var baked=new BakedOverviewField(basis,live,far);
        var reshape=Read("ExplorationReshape.bytes",TerrainReshapeIO.Read);
        IWorldField field=new TerrainDetailField(new TerrainReshapeField(baked,reshape),714103);
        field=((IWorldFieldSnapshotProvider)field).CaptureSnapshot();
        var profile=new SurfaceProfile(seed:714103,seaLevelMeters:0,maxSnowDepthMeters:2.4,enableSnow:true,winterFraction:.2,temperatureBiasC:0,experimentalBroadLithology:false);
        var sampler=new SurfaceMaterialSampler(field,profile);
        double loadMs=loading.Elapsed.TotalMilliseconds;GC.Collect();GC.WaitForPendingFinalizers();GC.Collect();
        long loadedManaged=GC.GetTotalMemory(false),loadedWorking=Process.GetCurrentProcess().WorkingSet64;
        const int size=512;const double spawnX=-32555,spawnZ=-13458;
        var records=new List<object>();var microseconds=new List<double>();var counts=new List<int>();double checksum=0;var total=Stopwatch.StartNew();
        foreach(int level in allLevels?Enumerable.Range(0,15):new[]{0,6,10})
        {
            double spacing=.2*Math.Pow(2,level),cx=allLevels&&level==14?0:spawnX,cz=allLevels&&level==14?0:spawnZ;
            long ox=(long)(2*Math.Floor(Math.Floor(cx/spacing)/2))-size/2,oz=(long)(2*Math.Floor(Math.Floor(cz/spacing)/2))-size/2;
            // Sample a distributed subset of actual row-major lattice texels.
            // A coprime stride covers the entire level window, avoiding one unusually cheap row.
            int wanted=allLevels?(level<10?6667:6666):(level==0?33334:33333);
            for(int i=0;i<2048;i++){int k=(i*7919)&(size*size-1);var c=sampler.EvaluateColumn((ox+k%size)*spacing,(oz+k/size)*spacing,spacing);checksum+=c.SurfaceHeightMeters;}
            var watch=new Stopwatch();long allocated=GC.GetAllocatedBytesForCurrentThread();int g0=GC.CollectionCount(0),g1=GC.CollectionCount(1),g2=GC.CollectionCount(2);
            watch.Start();int samples=0;double min=double.MaxValue,max=double.MinValue;
            for(int i=0;i<wanted;i++)
            {
                if((i&255)==0&&watch.Elapsed.TotalSeconds>20)break;
                int k=(i*7919)&(size*size-1);var c=sampler.EvaluateColumn((ox+k%size)*spacing,(oz+k/size)*spacing,spacing);
                checksum+=c.SurfaceHeightMeters+c.SurfaceTint*.000000001+c.Wetness;min=Math.Min(min,c.SurfaceHeightMeters);max=Math.Max(max,c.SurfaceHeightMeters);samples++;
            }
            watch.Stop();long bytes=GC.GetAllocatedBytesForCurrentThread()-allocated;double us=watch.Elapsed.TotalMilliseconds*1000/samples;microseconds.Add(us);counts.Add(samples);
            var record=new{level,spacing_m=spacing,origin_x=ox,origin_z=oz,samples,requested_samples=wanted,elapsed_ms=watch.Elapsed.TotalMilliseconds,mean_us=us,samples_per_second=1000000/us,allocated_bytes=bytes,allocated_bytes_per_sample=bytes/(double)samples,gc0=GC.CollectionCount(0)-g0,gc1=GC.CollectionCount(1)-g1,gc2=GC.CollectionCount(2)-g2,minimum_height=min,maximum_height=max};
            records.Add(record);Console.WriteLine(JsonSerializer.Serialize(record));Console.Out.Flush();
        }
        // Diagnostic only: direct field heights omit snow/material evaluation and
        // are NOT a replacement for SurfaceColumn.SurfaceHeightMeters.
        var rawWatch=Stopwatch.StartNew();long rawBytes=GC.GetAllocatedBytesForCurrentThread();
        for(int i=0;i<10000;i++)checksum+=field.Sample(spawnX+(i%100)*.2,spawnZ+(i/100)*.2).HeightMeters;
        double rawUs=rawWatch.Elapsed.TotalMilliseconds/10;rawBytes=GC.GetAllocatedBytesForCurrentThread()-rawBytes;
        const int levels=15;long startupSamples=(long)levels*size*size;
        double weighted=allLevels?microseconds.Average():(6*microseconds[0]+2*microseconds[1]+7*microseconds[2])/levels;
        var report=new{status="MEASURED",scope="Single-thread Windows .NET 8; actual bundled atlases + empty-live snapshot + static far + reshape + relief + production SurfaceMaterialSampler. No Unity import, GPU, worker scaling or native frame measurement.",basis_identity=basis.SourceIdentity,far_identity=far.Identity,field_identity=field.SourceIdentity,profile_identity=profile.Identity,spawn_x=spawnX,spawn_z=spawnZ,load_ms=loadMs,loaded_managed_bytes=loadedManaged,loaded_working_set_bytes=loadedWorking,process_peak_working_set_bytes=Process.GetCurrentProcess().PeakWorkingSet64,total_measured_samples=counts.Sum(),records,raw_height_diagnostic_us=rawUs,raw_height_diagnostic_allocated_bytes=rawBytes,raw_height_warning="Omits snow and material evaluation; cannot be used as an exact drop-in.",startup_levels=levels,startup_samples=startupSamples,startup_weighted_single_thread_seconds=startupSamples*weighted/1000000,startup_min_observed_rate_seconds=startupSamples*microseconds.Max()/1000000,startup_max_observed_rate_seconds=startupSamples*microseconds.Min()/1000000,extrapolation="Representative L0/L6/L10 windows, weights6/2/7 approximate footprint regimes; outermost fixed anchor covers mostly outside world and can be cheaper. Excludes upload/copy/snapshot work. No measured parallel speedup.",checksum,total_seconds=total.Elapsed.TotalSeconds};
        File.WriteAllText(output,JsonSerializer.Serialize(report,new JsonSerializerOptions{WriteIndented=true}));Console.WriteLine("REPORT "+output);
    }
}
