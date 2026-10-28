using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text.Json;
using AshenVoxel.Core;

public static class Benchmark
{
    sealed class Samples
    {
        public readonly List<double> Ms = new List<double>();
        public readonly List<long> Alloc = new List<long>();
        public T Measure<T>(Func<T> f)
        {
            long before=GC.GetAllocatedBytesForCurrentThread();var sw=Stopwatch.StartNew();
            T result=f();sw.Stop();Ms.Add(sw.Elapsed.TotalMilliseconds);
            Alloc.Add(GC.GetAllocatedBytesForCurrentThread()-before);return result;
        }
        public object Summary()
        {
            var sorted=Ms.OrderBy(v=>v).ToArray();
            return new {count=sorted.Length,p50_ms=Quantile(sorted,.5),p95_ms=Quantile(sorted,.95),p99_ms=Quantile(sorted,.99),max_ms=sorted.Length==0?0:sorted.Last(),mean_ms=sorted.Length==0?0:sorted.Average(),allocated_bytes_mean=Alloc.Count==0?0:Alloc.Average(),raw_ms=Ms};
        }
        static double Quantile(double[] a,double q)
        {
            if(a.Length==0)return 0;
            double x=(a.Length-1)*q;int lo=(int)x,hi=Math.Min(lo+1,a.Length-1);
            return a[lo]+(a[hi]-a[lo])*(x-lo);
        }
    }
    static VoxelWorld NewWorld() => new VoxelWorld(256,128,256,.1f,714103);
    static long[] Counts(VoxelWorld w)
    {
        long[] c=new long[MaterialCatalog.Count];
        foreach(var chunk in w.Chunks.Values)foreach(var id in chunk.Cells)c[(int)id]++;
        // Air includes unallocated cells, not only the cells in sparse chunks.
        c[0]+=(long)w.SizeX*w.SizeY*w.SizeZ-(long)w.Chunks.Count*Chunk.Volume;
        return c;
    }
    static long Occupied(VoxelWorld w) => Counts(w).Skip(1).Sum();
    static ulong Hash(VoxelWorld w)
    {
        unchecked
        {
            ulong h=14695981039346656037UL;
            foreach(var kv in w.Chunks.OrderBy(v=>v.Key.z).ThenBy(v=>v.Key.y).ThenBy(v=>v.Key.x))
            {
                h=(h^(uint)kv.Key.x)*1099511628211UL;h=(h^(uint)kv.Key.y)*1099511628211UL;h=(h^(uint)kv.Key.z)*1099511628211UL;
                foreach(var id in kv.Value.Cells)h=(h^(byte)id)*1099511628211UL;
                foreach(uint tint in kv.Value.Tints)h=(h^tint)*1099511628211UL;
            }
            return h;
        }
    }
    public static void Main(string[] args)
    {
        var warm = new VoxelWorld(64,96,64,.1f,714103);Generation.Generate(warm);
        using(var s=new Simulation(warm)){WorldEdits.CarveSphere(warm,3.2f,3,3.2f,.45f,10);s.Step(1800);}
        var gen=new Samples();VoxelWorld w=null;
        for(int i=0;i<5;i++)w=gen.Measure(()=>{var next=NewWorld();Generation.Generate(next);return next;});
        int initialChunks=w.Chunks.Count;int initialTintChunks=w.Chunks.Values.Count(c=>c.HasTints);var initial=Counts(w);long expected=initial.Skip(1).Sum(),carved=0,placed=0;
        var edit=new Samples();var activeSim=new Samples();var idleSim=new Samples();
        long processed=0,moves=0;int peakActive=0,maxProcessed=0;bool budgetRespected=true;
        using(var sim=new Simulation(w))
        {
            for(int i=0;i<32;i++)
            {
                double a=i*2.399963229728653;
                float x=12.8f+(float)Math.Cos(a)*2.4f,z=12.8f+(float)Math.Sin(a)*2.4f;
                float y=Generation.SurfaceHeight(w,(int)(x/.1f),(int)(z/.1f))*.1f;
                int removed=edit.Measure(()=>WorldEdits.CarveSphere(w,x,y-.25f,z,.85f,10));
                int added=edit.Measure(()=>WorldEdits.PlaceSphere(w,x,y+2.4f,z,.45f,MaterialId.Sand));
                carved+=removed;placed+=added;expected+=added-removed;
                for(int j=0;j<4;j++)Tick(sim,activeSim,idleSim,ref processed,ref moves,ref peakActive,ref maxProcessed,ref budgetRespected);
            }
            if(Occupied(w)!=expected)throw new Exception("Edit+gravity occupancy mismatch");
            long[] beforeGravity=Counts(w);
            for(int i=0;i<480&&sim.ActiveCount>0;i++)Tick(sim,activeSim,idleSim,ref processed,ref moves,ref peakActive,ref maxProcessed,ref budgetRespected);
            long[] afterGravity=Counts(w);
            if(!beforeGravity.SequenceEqual(afterGravity))throw new Exception("Simulation changed material mass");
            // Isolate an explicit idle cost sample set; do not mix those into active percentiles.
            while(sim.ActiveCount>0 && sim.Tick<10000)Tick(sim,activeSim,idleSim,ref processed,ref moves,ref peakActive,ref maxProcessed,ref budgetRespected);
            for(int i=0;i<32;i++)Tick(sim,activeSim,idleSim,ref processed,ref moves,ref peakActive,ref maxProcessed,ref budgetRespected);
            if(!beforeGravity.SequenceEqual(Counts(w)))throw new Exception("Final settling changed material mass");
            var ray=new Samples();int hitCount=0;
            for(int batch=0;batch<16;batch++)
            {
                int b=batch;
                hitCount+=ray.Measure(()=>
                {
                    int hits=0;
                    for(int i=0;i<1000;i++)
                    {
                        float x=((i*73+b*19)%250+3)*.1f,z=((i*151+b*7)%250+3)*.1f;
                        if(VoxelRaycast.TryCast(w,x,14,z,.1f,-1,.06f,30,out VoxelHit hit))hits++;
                    }
                    return hits;
                });
            }
            var save=new Samples();var load=new Samples();byte[] bytes=null;bool exact=true;ulong h=Hash(w);
            for(int i=0;i<3;i++)
            {
                bytes=save.Measure(()=>{using(var stream=new MemoryStream()){Persistence.Save(stream,w);return stream.ToArray();}});
                var loaded=load.Measure(()=>Persistence.Load(new MemoryStream(bytes,false)));
                exact&=Hash(loaded)==h && Counts(loaded).SequenceEqual(Counts(w)) && loaded.SizeX==w.SizeX && loaded.SizeY==w.SizeY && loaded.SizeZ==w.SizeZ && loaded.Seed==w.Seed && loaded.VoxelSize==w.VoxelSize;
            }
            if(!exact||!budgetRespected)throw new Exception("Benchmark correctness gate failed");
            var result=new {
                schema=1,generated_utc=DateTime.UtcNow.ToString("O"),framework=System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
                generator_version=Generation.Version,scope="Pure .NET core only; excludes Unity, meshes, graphics, colliders and GPU. Not FPS.",
                fixture=new{seed=w.Seed,size_cells=new[]{w.SizeX,w.SizeY,w.SizeZ},voxel_m=w.VoxelSize,dimensions_m=new[]{25.6,12.8,25.6},initial_occupied_cells=initial.Skip(1).Sum(),initial_allocated_chunks=initialChunks,initial_allocated_material_bytes=(long)initialChunks*Chunk.Volume,initial_allocated_tint_bytes=(long)initialTintChunks*Chunk.Volume*4,resident_chunks_final=w.Chunks.Count},
                generation=gen.Summary(),brush_calls=edit.Summary(),active_simulation_calls=activeSim.Summary(),idle_simulation_calls=idleSim.Summary(),ray_batches_1000=ray.Summary(),snapshot_save=save.Summary(),snapshot_load=load.Summary(),
                workload=new{brush_pairs=32,carve_radius_m=.85,place_radius_m=.45,candidate_budget_per_step=1800,total_candidates_processed=processed,total_moves=moves,peak_active=peakActive,max_candidates_one_step=maxProcessed,final_active=sim.ActiveCount,total_carved=carved,total_placed=placed,occupied_final=Occupied(w),snapshot_bytes=bytes.Length,ray_count=16000,ray_hits=hitCount,material_counts_final=Counts(w)},
                assertions=new{occupancy_matches_edits=true,per_material_gravity_mass_conserved=true,candidate_budget_respected=budgetRespected,snapshot_exact_hash_and_material_counts=exact}
            };
            File.WriteAllText(args[0],JsonSerializer.Serialize(result,new JsonSerializerOptions{WriteIndented=true}));
            Console.WriteLine("Core benchmark written: "+args[0]);
            Console.WriteLine("Generation p50 and active simulation p95 are JSON metrics; no Unity frame-rate claim.");
        }
    }
    static void Tick(Simulation s,Samples active,Samples idle,ref long processed,ref long moves,ref int peak,ref int max,ref bool valid)
    {
        peak=Math.Max(peak,s.ActiveCount);var measure=s.ActiveCount>0?active:idle;
        measure.Measure(()=>s.Step(1800));processed+=s.LastProcessed;moves+=s.LastMoves;
        max=Math.Max(max,s.LastProcessed);valid&=s.LastProcessed<=1800;
    }
}
