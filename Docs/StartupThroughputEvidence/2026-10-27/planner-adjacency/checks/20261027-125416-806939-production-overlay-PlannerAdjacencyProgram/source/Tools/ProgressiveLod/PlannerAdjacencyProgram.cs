using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using AshenVoxel.WorldScale;
using BigInteger=System.Numerics.BigInteger;

static class Program
{
    sealed class Flat:IWorldFieldBounds
    {public string SourceIdentity=>"adjacency-flat";public SurfaceSample Sample(double x,double z)=>new SurfaceSample(10,6,6,true);public HeightBounds QueryBounds(double a,double b,double c,double d)=>new HeightBounds(10,10);}
    static int checks,failedChecks;static readonly List<string> failures=new List<string>();
    static void Check(bool ok,string name){checks++;if(!ok){failedChecks++;if(failures.Count<40)failures.Add(name);}}
    static LodTile Tile(TileKey k)
    {double size=25.6*Math.Pow(2,k.Level);return new LodTile(k,k.X*size,k.Z*size,size,10,10);}
    static bool Oracle(TileKey a,TileKey b)
    {
        BigInteger sa=BigInteger.One<<a.Level,sb=BigInteger.One<<b.Level;
        BigInteger ax=(BigInteger)a.X*sa,az=(BigInteger)a.Z*sa,bx=(BigInteger)b.X*sb,bz=(BigInteger)b.Z*sb;
        return ((ax+sa==bx||bx+sb==ax)&&BigInteger.Min(az+sa,bz+sb)>BigInteger.Max(az,bz))||
               ((az+sa==bz||bz+sb==az)&&BigInteger.Min(ax+sa,bx+sb)>BigInteger.Max(ax,bx));
    }
    static void Pair(TileKey a,TileKey b,string label)
    {
        bool expected=Oracle(a,b);var ta=Tile(a);var tb=Tile(b);
        Check(LodPlanner.Adjacent(ta,tb)==expected,label+" forward");
        Check(LodPlanner.Adjacent(tb,ta)==expected,label+" reverse");
    }
    static void Reject(Action action,string label)
    {try{action();Check(false,label);}catch(Exception e){Check(e is ArgumentException,label+" exception type "+e.GetType().Name);}}
    readonly struct Rect
    {
        public readonly long X,Z,E,F;public readonly TileKey Key;
        public Rect(TileKey k){Key=k;checked{long size=1L<<k.Level;X=k.X*size;Z=k.Z*size;E=X+size;F=Z+size;}}
    }
    static bool Touch(Rect a,Rect b)=>((a.E==b.X||b.E==a.X)&&Math.Min(a.F,b.F)>Math.Max(a.Z,b.Z))||
        ((a.F==b.Z||b.F==a.Z)&&Math.Min(a.E,b.E)>Math.Max(a.X,b.X));
    static object Audit(WorldPoint eye,int limit)
    {
        var settings=new LodSettings(25.6,14,20000,2.2,limit,-100,100);var field=new Flat();
        var warm=LodPlanner.Build(field,eye,settings);var times=new List<double>();LodPlan plan=warm;
        for(int repeat=0;repeat<3;repeat++){var clock=Stopwatch.StartNew();plan=LodPlanner.Build(field,eye,settings);times.Add(clock.Elapsed.TotalMilliseconds);}
        var rs=plan.Tiles.Select(t=>new Rect(t.Key)).ToArray();int overlaps=0,unbalanced=0,misses=0,falseEdges=0;
        for(int i=0;i<rs.Length;i++)for(int j=i+1;j<rs.Length;j++)
        {
            Rect a=rs[i],b=rs[j];bool adjacent=Touch(a,b),actual=LodPlanner.Adjacent(plan.Tiles[i],plan.Tiles[j]);
            if(Math.Min(a.E,b.E)>Math.Max(a.X,b.X)&&Math.Min(a.F,b.F)>Math.Max(a.Z,b.Z))overlaps++;
            if(adjacent&&Math.Abs(a.Key.Level-b.Key.Level)>1)unbalanced++;
            if(adjacent&&!actual)misses++;if(!adjacent&&actual)falseEdges++;
        }
        Check(overlaps==0,"Plan has no positive-area overlaps");Check(unbalanced==0,"Plan neighboring LOD gap <=1");
        Check(misses==0&&falseEdges==0,"Plan predicate agrees with independent integer rectangle oracle");Check(rs.Length<=limit,"Plan retains requested tile cap");
        var areas=new Dictionary<(long,long),BigInteger>();
        foreach(Rect r in rs)
        {var root=(r.Key.X>>(14-r.Key.Level),r.Key.Z>>(14-r.Key.Level));areas.TryGetValue(root,out BigInteger area);areas[root]=area+(BigInteger)(r.E-r.X)*(r.F-r.Z);}
        double rootSize=25.6*(1L<<14);long cx=(long)Math.Floor(eye.X/rootSize),cz=(long)Math.Floor(eye.Z/rootSize);int roots=0;
        for(long z=cz-1;z<=cz+1;z++)for(long x=cx-1;x<=cx+1;x++)
        {
            double dx=Math.Max(0,Math.Max(x*rootSize-eye.X,eye.X-(x+1)*rootSize)),dz=Math.Max(0,Math.Max(z*rootSize-eye.Z,eye.Z-(z+1)*rootSize));
            if(dx*dx+dz*dz>20000.0*20000)continue;roots++;
            Check(areas.TryGetValue((x,z),out BigInteger area)&&area==(BigInteger.One<<28),"Every expected root is exactly tiled by area without overlap");
        }
        Check(areas.Count==roots,"No unexpected root coverage");
        var near=plan.Tiles.Single(t=>eye.X>=t.OriginXMeters&&eye.X<t.OriginXMeters+t.SizeMeters&&eye.Z>=t.OriginZMeters&&eye.Z<t.OriginZMeters+t.SizeMeters);
        times.Sort();return new{x=eye.X,z=eye.Z,limit,tiles=rs.Length,roots,near_level=near.Key.Level,near_tile_m=near.SizeMeters,near_initial_spacing_m=near.SizeMeters/8,
            overlap_pairs=overlaps,unbalanced_pairs=unbalanced,missed_adjacencies=misses,false_adjacencies=falseEdges,median_plan_ms=times[1],plan_ms=times};
    }
    static void Main(string[] args)
    {
        string error=null;var plans=new List<object>();long normalBytes=-1;double normalMs=0;
        try
        {
            Pair(new TileKey(-1,0,0),new TileKey(0,0,0),"negative shared edge");
            Pair(new TileKey(-1,-1,0),new TileKey(0,0,0),"negative corner only");
            Pair(new TileKey(-187,-97,2),new TileKey(-24,-12,5),"reported192 violation edge");
            Pair(new TileKey(-94,-49,3),new TileKey(-24,-12,5),"reported missed parent edge");
            Pair(new TileKey(0,0,5),new TileKey(0,0,0),"contained tiles not adjacent");
            Pair(new TileKey(0,0,0),new TileKey(0,0,0),"identical tile not adjacent");
            foreach(int level in new[]{0,1,14,24})foreach(long x in new[]{long.MinValue,long.MinValue+1,-(1L<<60),-1L,0L,1L,1L<<60,long.MaxValue-1})
            {
                Pair(new TileKey(x,0,level),new TileKey(x+1,0,level),"extreme X edge "+x+"/"+level);
                Pair(new TileKey(0,x,level),new TileKey(0,x+1,level),"extreme Z edge "+x+"/"+level);
                Pair(new TileKey(x,x,level),new TileKey(x+1,x+1,level),"extreme corner "+x+"/"+level);
            }
            foreach(int level in new[]{-1,25,64})Reject(()=>LodPlanner.Adjacent(Tile(new TileKey(0,0,level)),Tile(new TileKey(1,0,0))),"Reject unsupported level before shift "+level);
            Reject(()=>LodPlanner.Adjacent(null,Tile(new TileKey(0,0,0))),"Reject null tile");
            var random=new Random(714103);
            for(int i=0;i<1000;i++)
            {
                int coarse=random.Next(1,25),delta=random.Next(1,coarse+1),fine=coarse-delta;long ratio=1L<<delta,x=random.Next(-1000000,1000000),z=random.Next(-1000000,1000000);
                var b=new TileKey(x,z,coarse);var a=new TileKey((x+1)*ratio,z*ratio+(long)(random.NextDouble()*ratio),fine);
                Pair(a,b,"mixed-level edge "+i);Pair(new TileKey((x+1)*ratio,(z+1)*ratio,fine),b,"mixed-level corner "+i);
            }
            var left=Tile(new TileKey(-94,-49,3));var right=Tile(new TileKey(-24,-12,5));
            for(int i=0;i<1000;i++)LodPlanner.Adjacent(left,right);
            long before=GC.GetAllocatedBytesForCurrentThread();var sw=Stopwatch.StartNew();int adjacentCount=0;
            for(int i=0;i<100000;i++)if(LodPlanner.Adjacent(left,right))adjacentCount++;
            sw.Stop();normalBytes=GC.GetAllocatedBytesForCurrentThread()-before;normalMs=sw.Elapsed.TotalMilliseconds;
            // Stopwatch is allocated once outside the predicate; zero-allocation assertion excludes it below.
            Check(adjacentCount==100000,"Repeated normal adjacency remains correct");
            before=GC.GetAllocatedBytesForCurrentThread();for(int i=0;i<100000;i++)LodPlanner.Adjacent(left,right);
            normalBytes=GC.GetAllocatedBytesForCurrentThread()-before;Check(normalBytes==0,"Normal adjacency predicate has zero measured allocation");
            foreach(var eye in new[]{new WorldPoint(-19065,11.65,-10200),new WorldPoint(0,11.65,0),new WorldPoint(-25.6,11.65,-25.6),new WorldPoint(-100000000,11.65,100000000),new WorldPoint(-999999000000,11.65,999999000000)})
                foreach(int limit in new[]{64,192,512})plans.Add(Audit(eye,limit));
        }
        catch(Exception e){error=e.ToString();Console.WriteLine(error);}
        File.WriteAllText(args[0],JsonSerializer.Serialize(new{status=error==null&&failedChecks==0?"PASS":"FAIL",checks,failedChecks,error,failures,
            normal_predicate_100k_bytes=normalBytes,normal_predicate_100k_ms=normalMs,plans,
            scope="Isolated planner correctness: independent BigInteger predicate oracle; exhaustive pairwise integer non-overlap/balance and root area coverage for15 synthetic plans. Actual-world cap comparison is separate. CPU stopwatch only, not native Start/FPS. Extreme fallback may allocate OverflowException; ordinary valid project coordinates stay on checked Int64 fast path."},new JsonSerializerOptions{WriteIndented=true}));
        Console.WriteLine($"{checks} assertions; {failedChecks} failures ({failures.Count} messages retained); {plans.Count} plans; normal predicate {normalBytes} bytes/{normalMs:F2}ms");
        foreach(string f in failures)Console.WriteLine("FAIL "+f);if(error!=null||failures.Count>0)Environment.ExitCode=1;
    }
}
