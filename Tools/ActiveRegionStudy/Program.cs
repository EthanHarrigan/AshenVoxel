using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using AshenVoxel.Core;
using AshenVoxel.Core.Experimental;
using AshenVoxel.Exploration.Experimental;

static class Program
{
    static int assertions;
    static long sink;
    static readonly List<object> records=new List<object>();
    static void Check(bool value,string message){assertions++;if(!value)throw new Exception(message);}
    interface IGravity:IDisposable
    {
        int Active {get;} int Processed {get;} long Moves {get;} long Tick {get;} long PendingWake {get;} int Pages {get;}
        void BeginWake(); void Wake(int budget); void Step(int budget);
    }
    sealed class Original:IGravity
    {
        readonly Simulation s;
        public Original(VoxelWorld w){s=new Simulation(w);}
        public int Active=>s.ActiveCount;public int Processed=>s.LastProcessed;public long Moves=>s.TotalMoves;public long Tick=>s.Tick;public long PendingWake=>s.PendingWakeCandidates;public int Pages=>0;
        public void BeginWake()=>s.BeginWakeAll();public void Wake(int n)=>s.WakeBudget(n);public void Step(int n)=>s.Step(n);public void Dispose()=>s.Dispose();
    }
    sealed class Candidate:IGravity
    {
        readonly PagedSimulation s;
        public Candidate(VoxelWorld w){s=new PagedSimulation(w);}
        public int Active=>s.ActiveCount;public int Processed=>s.LastProcessed;public long Moves=>s.TotalMoves;public long Tick=>s.Tick;public long PendingWake=>s.PendingWakeCandidates;public int Pages=>s.RetainedMembershipPages;
        public void BeginWake()=>s.BeginWakeAll();public void Wake(int n)=>s.WakeBudget(n);public void Step(int n)=>s.Step(n);public void Dispose()=>s.Dispose();
    }
    static IGravity Solver(VoxelWorld world,bool candidate)=>candidate?(IGravity)new Candidate(world):new Original(world);
    static VoxelWorld Fixture(string name)
    {
        int side=name=="dispersed"?256:64;
        var w=new VoxelWorld(side,64,side,.1f,714103);
        if(name=="dense")for(int z=4;z<28;z++)for(int y=20;y<26;y++)for(int x=4;x<28;x++)w.Set(x,y,z,MaterialId.Sand,0xff786543);
        else if(name=="dispersed")for(int i=0;i<512;i++)w.Set((i%16)*16+8,30+4*(i/256),((i/16)%16)*16+8,MaterialId.Sand,0xff987654);
        else for(int i=0;i<7;i++)w.Set(15+i%3,30+i,15+i%2,MaterialId.Sand,0xff123456);
        return w;
    }
    static Dictionary<CargoKey,int> Totals(VoxelWorld world)
    {
        var result=new Dictionary<CargoKey,int>();
        foreach(var pair in world.Chunks)for(int i=0;i<Chunk.Volume;i++)if(pair.Value.Cells[i]!=MaterialId.Air)
        {
            var local=Chunk.Decode(i);var p=new Int3(pair.Key.x*16+local.x,pair.Key.y*16+local.y,pair.Key.z*16+local.z);
            var key=new CargoKey(world.Get(p),world.GetTint(p));result.TryGetValue(key,out int n);result[key]=n+1;
        }
        return result;
    }
    static bool EqualTotals(Dictionary<CargoKey,int> a,Dictionary<CargoKey,int> b)=>a.Count==b.Count&&a.All(x=>b.TryGetValue(x.Key,out int n)&&n==x.Value);
    static void EqualWorld(VoxelWorld a,VoxelWorld b)
    {
        Check(a.Chunks.Count==b.Chunks.Count,"chunk count");
        foreach(var pair in a.Chunks)
        {
            Check(b.Chunks.TryGetValue(pair.Key,out var other),"chunk key");
            for(int i=0;i<Chunk.Volume;i++)
            {
                Check(pair.Value.Cells[i]==other.Cells[i],"material equality");
                var p=Chunk.Decode(i);Check(pair.Value.GetTint(p.x,p.y,p.z)==other.GetTint(p.x,p.y,p.z),"tint equality");
            }
        }
        Check(a.DirtyChunks.SetEquals(b.DirtyChunks),"dirty boundary equality");
    }
    static void MembershipChecks()
    {
        var a=new HashSet<Int3>();var b=new PagedCellSet();var rng=new Random(734);
        for(int i=0;i<20000;i++)
        {
            var p=new Int3(rng.Next(-33,34),rng.Next(-33,34),rng.Next(-33,34));
            switch(i%5)
            {
                case 0:Check(a.Remove(p)==b.Remove(p),"remove");break;
                case 1:Check(a.Contains(p)==b.Contains(p),"contains");break;
                default:Check(a.Add(p)==b.Add(p),"add");Check(!b.Add(p),"dedupe");break;
            }
            Check(a.Count==b.Count,"membership count");
            if(i%997==0){a.Clear();b.Clear();Check(!b.Contains(p)&&b.Count==0,"epoch clear");}
        }
        b.Release();Check(b.PageCount==0&&b.Count==0,"release retained membership pages");
    }
    static void Equivalence()
    {
        var a=new VoxelWorld(48,48,48,.1f,3);var b=new VoxelWorld(48,48,48,.1f,3);
        using(var original=Solver(a,false))using(var candidate=Solver(b,true))
        {
            var ea=new List<(Int3,MaterialId,uint)>();var eb=new List<(Int3,MaterialId,uint)>();
            a.Changed+=p=>ea.Add((p,a.Get(p),a.GetTint(p)));b.Changed+=p=>eb.Add((p,b.Get(p),b.GetTint(p)));
            void Set(int x,int y,int z,MaterialId material,uint tint=0){a.Set(x,y,z,material,tint);b.Set(x,y,z,material,tint);}
            for(int z=14;z<18;z++)for(int y=14;y<19;y++)for(int x=14;x<18;x++)Set(x,y,z,MaterialId.Sand,0xff123456);
            for(int z=14;z<18;z++)for(int x=14;x<18;x++)Set(x,13,z,MaterialId.Stone);
            int[] budgets={0,1,7,31,128};
            for(int tick=0;tick<900;tick++)
            {
                ea.Clear();eb.Clear();
                if(tick==30)Set(15,13,15,MaterialId.Air);
                if(tick==45){int count=original.Active;Check(a.Get(14,13,14)==MaterialId.Stone,"tint fixture remains solid");Set(14,13,14,MaterialId.Stone,0xffabcdef);Check(original.Active==count&&candidate.Active==count,"tint-only does not wake");}
                if(tick==70){original.BeginWake();candidate.BeginWake();}
                if(tick==180){a.Clear();b.Clear();Set(16,32,16,MaterialId.Snow,0xffabcdef);original.BeginWake();candidate.BeginWake();}
                if(tick%37==0)Set(31+(tick%2),25,31,MaterialId.Sand,0xff135790);
                if(original.PendingWake>0){original.Wake(71);candidate.Wake(71);}
                int budget=budgets[tick%budgets.Length];original.Step(budget);candidate.Step(budget);
                Check(original.Processed<=budget&&candidate.Processed<=budget,"candidate bound");
                Check(original.Active==candidate.Active&&original.Moves==candidate.Moves&&original.Tick==candidate.Tick&&original.PendingWake==candidate.PendingWake,"same per-call state");
                Check(ea.SequenceEqual(eb),"identical ordered material events");
                if(tick%100==0)EqualWorld(a,b);
            }
            for(int i=0;i<4000&&(original.Active>0||original.PendingWake>0);i++)
            {original.Wake(8192);candidate.Wake(8192);original.Step(8192);candidate.Step(8192);}
            Check(original.Active==0&&candidate.Active==0&&original.PendingWake==0&&candidate.PendingWake==0,"backlog drained");EqualWorld(a,b);
            int active=original.Active;Set(31,1,31,MaterialId.Sand,0xffaaaaaa); // target may change material; compare both event paths
            Check(original.Active==candidate.Active,"sleep wake equivalence");sink+=active;
        }
    }
    static object Stats(List<double> raw)
    {
        var sorted=raw.OrderBy(v=>v).ToArray();
        return new{samples=raw.Count,p50_ms=sorted[sorted.Length/2],p95_ms=sorted[(int)((sorted.Length-1)*.95)],maximum_ms=sorted.Last(),raw_ms=raw};
    }
    static double Milliseconds(long ticks)=>ticks*1000.0/Stopwatch.Frequency;
    static string Fingerprint(VoxelWorld world)
    {using(var stream=new MemoryStream()){Persistence.Save(stream,world);return Convert.ToHexString(SHA256.HashData(stream.ToArray())).ToLowerInvariant();}}
    static object DropRun(string fixture,bool candidate,int trial)
    {
        var world=Fixture(fixture);var expected=Totals(world);using(var sim=Solver(world,candidate))
        {
            int peak=0,calls=0;long candidates=0;var raw=new List<double>(2048);
            long allocated=GC.GetAllocatedBytesForCurrentThread(),start=Stopwatch.GetTimestamp();sim.BeginWake();
            do
            {
                long step=Stopwatch.GetTimestamp();if(sim.PendingWake>0)sim.Wake(8192);sim.Step(4096);
                raw.Add(Milliseconds(Stopwatch.GetTimestamp()-step));peak=Math.Max(peak,sim.Active);candidates+=sim.Processed;calls++;
                if(calls>20000)throw new Exception("drop backlog did not drain");
            }while(sim.Active>0||sim.PendingWake>0);
            double total=Milliseconds(Stopwatch.GetTimestamp()-start);long allocation=GC.GetAllocatedBytesForCurrentThread()-allocated;
            Check(EqualTotals(expected,Totals(world)),"drop material+tint conservation");
            return new{kind="granular_drop",fixture,variant=candidate?"paged_membership_fifo":"current_hashset_fifo",trial,total_ms=total,measured_thread_allocated_bytes=allocation,processed=candidates,moves=sim.Moves,calls,peak_active=peak,final_active=sim.Active,final_snapshot_sha256=Fingerprint(world),retained_membership_pages=sim.Pages,membership_bitmap_payload_bytes=sim.Pages*512,step_including_wake=Stats(raw)};
        }
    }
    static void RegionLifecycleChecks()
    {
        var index=new DormantRegionIndex(1.6f);var points=new Dictionary<int,Vector3>();var rng=new Random(389);var found=new List<int>();
        for(int i=0;i<600;i++){var p=new Vector3((float)rng.NextDouble()*30-15,(float)rng.NextDouble()*10-5,(float)rng.NextDouble()*30-15);points.Add(i,p);index.Set(i,p);}
        for(int i=0;i<200;i++)
        {
            int id=i*2;var next=new Vector3(-1.6f+i*.01f,0,-3.2f);points[id]=next;index.Set(id,next);
            if(i%3==0){points.Remove(id);Check(index.Remove(id),"remove spatial entry");}
            var center=new Vector3((i%11)-5,0,(i%17)-8);index.Query(center,4,found);
            var expected=points.Where(p=>Vector3.DistanceSquared(p.Value,center)<16).Select(p=>p.Key).OrderBy(x=>x);
            Check(expected.SequenceEqual(found.OrderBy(x=>x)),"region query exact after move/remove; negative boundaries");
        }
    }
    static void DebrisProbe()
    {
        var w=new VoxelWorld(100,12,100,.1f,1);w.Set(0,0,0,MaterialId.Wood);w.Set(0,1,0,MaterialId.Wood);
        for(int y=2;y<12;y++)for(int z=0;z<100;z++)for(int x=0;x<100;x++)w.Set(x,y,z,MaterialId.Wood,0xff735231);
        var p=new NaturalTreePhysics(w,new Int3(0,0,0),0){SuctionEnabled=false,TerrainHeight=(x,z)=>0};Check(p.Cut(new Int3(0,1,0)),"debris fixture stem cut");
        for(int i=0;i<2000;i++)p.Step(1f/60,8192,8192);
        Check(p.IslandCells==0&&p.SourceCells==1&&p.IndividualCells==100001&&p.Conserved,"actual current fixture detached and released");
        var sleep=(bool[])typeof(NaturalTreePhysics).GetField("sleeping",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(p);
        int asleep=0;var points=new List<Vector3>();for(int i=0;i<p.PendingDebris;i++){int id=p.LooseIndex(i);points.Add(p.Read(id).Position);if(sleep[id])asleep++;}
        Check(asleep==p.PendingDebris,"actual current parcels all asleep");
        var minimum=new Vector3(float.PositiveInfinity);var maximum=new Vector3(float.NegativeInfinity);
        foreach(var point in points){minimum=Vector3.Min(minimum,point);maximum=Vector3.Max(maximum,point);}
        for(int suction=0;suction<2;suction++)
        {
            p.SuctionEnabled=suction!=0;p.Hand=new Vector3(100,1,100);
            var raw=new List<double>();long visits=0;int revision=p.PoseRevision;
            for(int i=0;i<600;i++){long start=Stopwatch.GetTimestamp();p.Step(1f/60,0,8192);raw.Add(Milliseconds(Stopwatch.GetTimestamp()-start));visits+=p.LastBodyCandidates;}
            Check(p.PoseRevision==revision&&p.CargoCells==0&&p.Conserved,"dormant probe no pose or cargo changes");
            records.Add(new{kind="actual_debris_idle",suction_enabled=p.SuctionEnabled,logical_cells=p.IndividualCells,representatives=p.PendingDebris,asleep,candidate_visits=visits,per_call_visits=visits/600,steps=Stats(raw)});
        }
        foreach(float side in new[]{1.6f,3.2f,6.4f})
        {
            var index=new DormantRegionIndex(side);long bytes=GC.GetAllocatedBytesForCurrentThread(),start=Stopwatch.GetTimestamp();
            for(int i=0;i<points.Count;i++)index.Set(i,points[i]);
            double buildMs=Milliseconds(Stopwatch.GetTimestamp()-start);long buildAllocation=GC.GetAllocatedBytesForCurrentThread()-bytes;
            foreach(var item in new[]{("far",new Vector3(100,1,100)),("edge",new Vector3(-2,1,0)),("center",new Vector3(5,1,5))})
            {
                var list=new List<int>(points.Count);index.Query(item.Item2,4,list);
                var expected=points.Select((v,i)=>(v,i)).Where(v=>Vector3.DistanceSquared(v.v,item.Item2)<16).Select(v=>v.i).OrderBy(i=>i).ToArray();
                Check(expected.SequenceEqual(list.OrderBy(i=>i)),"snapshot collection eligibility exact");
                var scan=new List<double>();var region=new List<double>();var bounded=new List<double>();var scanMatches=new List<int>(points.Count);
                int buckets=index.LastBuckets,candidates=index.LastCandidates;
                for(int trial=0;trial<7;trial++)
                {
                    void Scan(){scanMatches.Clear();for(int i=0;i<points.Count;i++)if(Vector3.DistanceSquared(points[i],item.Item2)<16)scanMatches.Add(i);sink+=scanMatches.Count;}
                    void Query(){index.Query(item.Item2,4,list);sink+=list.Count;}
                    void Bounds(){scanMatches.Clear();if(Vector3.DistanceSquared(Vector3.Clamp(item.Item2,minimum,maximum),item.Item2)>=16)return;Scan();}
                    // Alternate timed order; 1,000 queries per batch, reporting per-query milliseconds.
                    double Time(Action action){long s=Stopwatch.GetTimestamp();for(int j=0;j<1000;j++)action();return Milliseconds(Stopwatch.GetTimestamp()-s)/1000;}
                    if(trial%2==0){scan.Add(Time(Scan));region.Add(Time(Query));}else{region.Add(Time(Query));scan.Add(Time(Scan));}
                    bounded.Add(Time(Bounds));Check(expected.SequenceEqual(scanMatches.OrderBy(i=>i)),"bounds query eligibility exact");
                }
                records.Add(new{kind="snapshot_spatial_query_only",region_side_m=side,hand=item.Item1,source_parcels=points.Count,buckets=index.BucketCount,queried_buckets=buckets,queried_candidates=candidates,matches=expected.Length,build_ms=buildMs,build_allocated_bytes=buildAllocation,scan=Stats(scan),bounds_then_scan=Stats(bounded),region=Stats(region)});
            }
        }
    }
    static void Main(string[] args)
    {
        MembershipChecks();Equivalence();RegionLifecycleChecks();
        // Warm both code paths before measured runs. Each recorded trial uses a fresh world.
        DropRun("sparse",false,-1);DropRun("sparse",true,-1);
        foreach(string fixture in new[]{"sparse","dense","dispersed"})for(int trial=0;trial<5;trial++)
        {
            bool first=trial%2==1;records.Add(DropRun(fixture,first,trial));records.Add(DropRun(fixture,!first,trial));
            Console.WriteLine("Completed "+fixture+" pair "+trial);
        }
        DebrisProbe();
        File.WriteAllText(args[0],JsonSerializer.Serialize(new{generated_utc=DateTime.UtcNow.ToString("O"),framework=RuntimeInformation.FrameworkDescription,assertions,records,sink},new JsonSerializerOptions{WriteIndented=true}));
        Console.WriteLine("PASS "+assertions+" assertions; "+records.Count+" measured records. CPU-only; no runtime integration.");
    }
}
