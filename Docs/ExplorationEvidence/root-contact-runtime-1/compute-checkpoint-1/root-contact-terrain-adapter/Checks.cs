using System;
using System.IO;
using System.Text.Json;
using AshenVoxel.WorldScale;
using RootContactPrototype;

static class Checks
{
    static int checks,comparisons,snowCases,detailDifferences;
    static long directCalls,fullCalls;
    static void Assert(bool value,string label){checks++;if(!value)throw new Exception(label);}
    sealed class Stats {public int Calls;public double MaxRadius;public double X,Z;}
    sealed class Field:IWorldFieldBounds,IWorldFieldSnapshotProvider
    {
        public double Height=50,Gx,Gz;public int Revision,Pattern;
        public bool ThrowSample,ThrowIdentity,ThrowCapture,MutateCapture,MutateSample,NullCapture;
        public string OverrideIdentity;
        public readonly Stats Stats;
        public Field LastSnapshot;
        public Field(Stats stats=null){Stats=stats??new Stats();}
        public string SourceIdentity=>ThrowIdentity?throw new Exception("identity"):OverrideIdentity??("fixture/"+Revision);
        public HeightBounds QueryBounds(double a,double b,double c,double d)=>new HeightBounds(-10000,10000);
        public SurfaceSample Sample(double x,double z)
        {
            Stats.Calls++;Stats.MaxRadius=Math.Max(Stats.MaxRadius,Math.Sqrt((x-Stats.X)*(x-Stats.X)+(z-Stats.Z)*(z-Stats.Z)));
            if(ThrowSample)throw new Exception("sample");if(MutateSample)Revision++;
            double h=Pattern==1?(((long)Math.Floor(x*10)&1)==0?50.04:50.16):Height+Gx*x+Gz*z;
            return new SurfaceSample(h,6,6,true);
        }
        public IWorldField CaptureSnapshot()
        {
            if(ThrowCapture)throw new Exception("capture");if(NullCapture)return null;
            LastSnapshot=new Field(Stats){Height=Height,Gx=Gx,Gz=Gz,Revision=Revision,Pattern=Pattern,
                ThrowSample=ThrowSample,OverrideIdentity=OverrideIdentity,MutateSample=MutateSample};
            if(MutateCapture)Revision++;
            return LastSnapshot;
        }
    }
    sealed class NoSnapshot:IWorldField
    {public string SourceIdentity=>"static-uncontracted";public SurfaceSample Sample(double x,double z)=>new SurfaceSample(0,6,6,true);}
    static RootTerrainSnapshot Capture(IWorldField field,long min=-100,long max=100,int queries=10000)
    {
        Assert(RootTerrainSnapshot.TryCapture(field,min,max,min,max,queries,out var value,out var reason),"capture:"+reason);return value;
    }
    static void RejectCapture(IWorldField field,string reason)
    {Assert(!RootTerrainSnapshot.TryCapture(field,-1,1,-1,1,10,out var value,out var why)&&value==null&&why==reason,"reject:"+reason+"/"+why);}
    static void Compare(Field basis,bool detail,SurfaceProfile profile)
    {
        IWorldField bare=detail?new TerrainDetailField(basis,714103):basis;
        IWorldField visual=new SurfaceWorldField(new SurfaceWorldField(bare,profile),profile);
        var adapter=Capture(visual);var sampler=new SurfaceMaterialSampler(bare,profile);
        foreach(long x in new long[]{-100,-17,-1,0,1,13,100})foreach(long z in new long[]{-100,-2,-1,0,1,26,100})
        {
            var center=new WorldCell(x,0,z).Center;basis.Stats.X=center.X;basis.Stats.Z=center.Z;
            int before=basis.Stats.Calls;
            Assert(adapter.TryGetBareTop(x,z,out double got),"sample accepted");
            int direct=basis.Stats.Calls-before;directCalls+=direct;Assert(direct==1,"one backing height query");
            before=basis.Stats.Calls;basis.Stats.MaxRadius=0;SurfaceColumn column=sampler.EvaluateColumn(center.X,center.Z);
            int full=basis.Stats.Calls-before;fullCalls+=full;Assert(full==11,"fullcolumn11 explicit override queries");
            Assert(Math.Abs(basis.Stats.MaxRadius-32)<1e-10,"fullcolumn radius32");
            Assert(got==VoxelTerrainPage.SurfaceTop(column.GroundHeightMeters),"exact bare top");comparisons++;
            if(column.SnowDepthMeters>0){snowCases++;Assert(column.SurfaceHeightMeters>column.GroundHeightMeters,"snow present");}
            if(detail&&got!=VoxelTerrainPage.SurfaceTop(basis.Height+basis.Gx*center.X+basis.Gz*center.Z))detailDifferences++;
            Assert(WorldCell.FromMeters(center).X==x&&WorldCell.FromMeters(center).Z==z,"center roundtrip");
        }
        Assert(adapter.SampleCalls==49&&adapter.QueryAttempts==49,"observable query counts");
    }
    static void Main(string[] args)
    {
        var dry=new SurfaceProfile(enableSnow:false,climateOverride:new SurfaceClimate(18,0,800,0,0));
        var snow=new SurfaceProfile(enableSnow:true,winterFraction:1,climateOverride:new SurfaceClimate(-25,0,2200,0,0));
        foreach(double h in new[]{-10.05,-.15,-.05000000000000001,-.05,-.04999999999999999,0,.04999999999999999,.05,.05000000000000001,.15,50.05,5000.05})
            foreach(var profile in new[]{dry,snow})Compare(new Field{Height=h},false,profile);
        foreach(double slope in new[]{-.84,-.2,0,.2,.84})foreach(var profile in new[]{dry,snow})
            Compare(new Field{Height=5000,Gx=slope,Gz=slope*.73},true,profile);
        Compare(new Field{Pattern=1},false,dry);
        Assert(snowCases>0&&detailDifferences>0,"snow and detail cases exercised");
        var patterned=new Field{Pattern=1};var patternAdapter=Capture(patterned);
        Assert(patternAdapter.TryGetBareTop(0,0,out var even)&&patternAdapter.TryGetBareTop(1,0,out var odd)&&even!=odd,"adjacent10cm retained");
        var identityField=new Field();string id1=Capture(new SurfaceWorldField(identityField,dry)).SourceIdentity;
        Assert(Capture(new SurfaceWorldField(identityField,snow)).SourceIdentity==id1,"snow profile irrelevant to bare identity");
        Assert(Capture(identityField,-99,100).SourceIdentity!=id1,"rectangle identity");
        Assert(Capture(identityField,queries:1).SourceIdentity==id1,"query budget not content");
        identityField.Revision++;Assert(Capture(identityField).SourceIdentity!=id1,"revision identity");
        var live=new Field();var stale=Capture(live);live.Height=500;live.Revision++;
        Assert(live.LastSnapshot.Sample(0,0).HeightMeters==50,"capture remains immutable");int n=live.Stats.Calls;
        Assert(!stale.TryGetBareTop(0,0,out var no)&&no==0&&stale.Failure=="stale-source"&&live.Stats.Calls==n,"live freshness distinct from snapshot");
        var changedSnapshot=new Field();var rejected=Capture(changedSnapshot);changedSnapshot.LastSnapshot.Revision++;
        Assert(!rejected.IsCurrent()&&rejected.Failure=="stale-source","mutable snapshot identity fail");
        var during=new Field{MutateSample=true};var race=Capture(during);
        Assert(!race.TryGetBareTop(0,0,out no)&&no==0&&race.SampleCalls==1&&race.Failure=="stale-source","postquery stale failclosed");
        var thrower=new Field{ThrowSample=true};var bad=Capture(thrower);
        Assert(!bad.TryGetBareTop(0,0,out no)&&no==0&&bad.Failure=="sample-error","sample error");
        var huge=Capture(new Field{Height=1e13});Assert(!huge.TryGetBareTop(0,0,out no)&&no==0,"height range");
        var identityError=new Field();var identityAdapter=Capture(identityError);identityError.ThrowIdentity=true;
        Assert(!identityAdapter.IsCurrent()&&identityAdapter.Failure=="identity-error","identity error");
        var exhausted=Capture(new Field(),queries:2);Assert(exhausted.TryGetBareTop(0,0,out no)&&exhausted.TryGetBareTop(0,0,out no),"budget2");
        Assert(!exhausted.TryGetBareTop(0,0,out no)&&exhausted.Failure=="query-limit"&&exhausted.SampleCalls==2&&exhausted.RemainingQueries==0,"finite budget");
        var outside=Capture(new Field(),-1,1);Assert(!outside.TryGetBareTop(2,0,out no)&&outside.SampleCalls==0&&outside.Failure=="outside-rectangle","outside before query");
        var cancelled=Capture(new Field());cancelled.Cancel();Assert(!cancelled.TryGetBareTop(0,0,out no)&&cancelled.SampleCalls==0,"cancel");
        RejectCapture(null,"capture-error-or-contract");RejectCapture(new NoSnapshot(),"snapshot-provider-required");
        RejectCapture(new Field{ThrowCapture=true},"capture-error-or-contract");RejectCapture(new Field{NullCapture=true},"capture-error-or-contract");
        RejectCapture(new Field{MutateCapture=true},"capture-stale");RejectCapture(new Field{ThrowIdentity=true},"capture-error-or-contract");
        RejectCapture(new Field{OverrideIdentity=new string('x',4097)},"capture-error-or-contract");
        Assert(Capture(new Field{OverrideIdentity=new string('x',4096)}).SourceIdentity.Length<256,"bounded hashed identity");
        IWorldField many=new Field();for(int i=0;i<9;i++)many=new SurfaceWorldField(many);RejectCapture(many,"capture-error-or-contract");
        foreach(var limits in new[]{(0L,1024L,0L,1L,10),(0L,1023L,0L,1023L,10),(-10000001001L,0L,0L,0L,10),(1L,0L,0L,0L,10),(0L,0L,0L,0L,0),(0L,0L,0L,0L,1048577)})
            Assert(!RootTerrainSnapshot.TryCapture(new Field(),limits.Item1,limits.Item2,limits.Item3,limits.Item4,limits.Item5,out _,out _),"limit rejection");
        foreach(long coordinate in new[]{-10000000000L,10000000000L})
        {
            Assert(RootTerrainSnapshot.TryCapture(new Field{Height=5.05},coordinate,coordinate,coordinate,coordinate,1,out var far,out _),"far capture");
            Assert(far.TryGetBareTop(coordinate,coordinate,out no)&&no==VoxelTerrainPage.SurfaceTop(5.05),"large exact center");
        }
        var output=new{status="PASS",checks,bare_top_exact_comparisons=comparisons,snow_cases=snowCases,detail_changed_quantized_top_cases=detailDifferences,
            direct_backing_sample_calls=directCalls,full_column_backing_sample_calls=fullCalls,direct_calls_per_query=1,full_column_calls_per_query=11,full_column_max_radius_meters=32,
            direct_external_halo_meters=0,query_cell_size_meters=.1,tests="Synthetic analytical slopes, diagonal relief, quantization boundaries, negative and large coordinates, snow, adjacent10cm signal, snapshot/live changes, midquery changes, errors, cancellation and limits."};
        File.WriteAllText(Path.Combine(args[0],"checks.json"),JsonSerializer.Serialize(output,new JsonSerializerOptions{WriteIndented=true}));
        Console.WriteLine(JsonSerializer.Serialize(output));
    }
}
