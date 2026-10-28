using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using AshenVoxel.WorldScale;
using RootContactPrototype;

static class Repro
{
    sealed class Counter { public int Calls; }
    sealed class Field:IWorldFieldBounds,IWorldFieldSnapshotProvider
    {
        public readonly double Height;readonly Counter count;
        public Field(double height,Counter counter){Height=height;count=counter;}
        public string SourceIdentity=>"immutable-analytical/"+Height.ToString("R",System.Globalization.CultureInfo.InvariantCulture);
        public SurfaceSample Sample(double x,double z){count.Calls++;return new SurfaceSample(Height,6,6,true);}
        public HeightBounds QueryBounds(double a,double b,double c,double d)=>new HeightBounds(Height,Height);
        public IWorldField CaptureSnapshot()=>this;
    }
    static void Main(string[] args)
    {
        var rows=new List<object>();var snow=new SurfaceProfile(enableSnow:true,winterFraction:1,climateOverride:new SurfaceClimate(-25,0,2200,0,0));
        foreach(string mode in new[]{"standard","detail-over-surface","outer-surface-detail-inner-surface"})
        {
            var count=new Counter();var bare=new Field(50,count);var expectedDetail=new TerrainDetailField(bare,714103);
            IWorldField source=mode=="standard"?new SurfaceWorldField(expectedDetail,snow):new TerrainDetailField(new SurfaceWorldField(bare,snow),714103);
            if(mode=="outer-surface-detail-inner-surface")source=new SurfaceWorldField(source,snow);
            var center=new WorldCell(0,0,0).Center;double expected=VoxelTerrainPage.SurfaceTop(expectedDetail.Sample(center.X,center.Z).HeightMeters);count.Calls=0;
            bool accepted=RootTerrainSnapshot.TryCapture(source,0,0,0,0,1,out var adapter,out string reason);double got=0;bool sampled=accepted&&adapter.TryGetBareTop(0,0,out got);
            rows.Add(new{mode,capture_accepted=accepted,reason,sampled,got,expected_bare=expected,snow_contamination_m=got-expected,backing_calls=count.Calls,adapter_calls=adapter?.SampleCalls,identity=adapter?.SourceIdentity});
            if(!accepted||!sampled||mode=="standard"&&(got!=expected||count.Calls!=1)||mode!="standard"&&(got<=expected||count.Calls<=1))throw new Exception("Reproducer result changed");
        }
        int finiteChecks=0;
        foreach(double height in new[]{double.NaN,double.PositiveInfinity,double.NegativeInfinity,double.MaxValue,-double.MaxValue,1e12+1,-1e12-1,-1e12,1e12,-.05,.05,0})
        {
            var field=new Field(height,new Counter());if(!RootTerrainSnapshot.TryCapture(field,0,0,0,0,1,out var adapter,out _))throw new Exception("Unexpected capture failure");
            bool success=adapter.TryGetBareTop(0,0,out double value);bool valid=!Double.IsNaN(height)&&!Double.IsInfinity(height)&&Math.Abs(height)<=1e12;
            if(success!=valid||!success&&(value!=0||!adapter.Failed)||success&&value!=VoxelTerrainPage.SurfaceTop(height))throw new Exception("Finite/range failure");finiteChecks++;
        }
        foreach(var rectangle in new[]{(long.MinValue,long.MaxValue),(long.MaxValue,long.MaxValue),(long.MinValue,long.MinValue),(-RootTerrainSnapshot.MaxGlobalCell,RootTerrainSnapshot.MaxGlobalCell)})
        {if(RootTerrainSnapshot.TryCapture(new Field(0,new Counter()),rectangle.Item1,rectangle.Item2,0,0,1,out _,out _))throw new Exception("Rectangle should reject");finiteChecks++;}
        File.WriteAllText(args[0],JsonSerializer.Serialize(new{status="REPRODUCED",scope="Small analytical CPU-only fields using actual frozen adapter and MAIN types; no world/assets/pack load or generation.",wrapper_cases=rows,finite_and_overflow_checks=finiteChecks},new JsonSerializerOptions{WriteIndented=true}));
        Console.WriteLine("Reproduced nested snow contamination; "+finiteChecks+" finite/range checks pass.");
    }
}
