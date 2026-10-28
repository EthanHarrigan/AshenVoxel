using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using AshenVoxel.WorldScale;
using RootContactPrototype;

static class LayeringChecks
{
    static int checks;static readonly List<object> cases=new List<object>();
    static void Check(bool ok,string label){checks++;if(!ok)throw new Exception(label);}
    sealed class Count{public int Samples;}
    sealed class Field:IWorldFieldBounds,IWorldFieldSnapshotProvider
    {
        public double Height=50;public int Revision;public readonly Count Counter=new Count();
        public IWorldField SnapshotOverride;
        public string SourceIdentity=>"field-revision/"+Revision;
        public HeightBounds QueryBounds(double a,double b,double c,double d)=>new HeightBounds(Height,Height);
        public SurfaceSample Sample(double x,double z){Counter.Samples++;return new SurfaceSample(Height,6,6,true);}
        public IWorldField CaptureSnapshot()=>SnapshotOverride??new Frozen(Height,SourceIdentity,Counter);
    }
    sealed class Frozen:IWorldFieldBounds
    {
        readonly double height;readonly Count counter;public string SourceIdentity{get;}
        public Frozen(double h,string id,Count c){height=h;SourceIdentity=id;counter=c;}
        public HeightBounds QueryBounds(double a,double b,double c,double d)=>new HeightBounds(height,height);
        public SurfaceSample Sample(double x,double z){counter.Samples++;return new SurfaceSample(height,6,6,true);}
    }
    static IWorldField Wrap(string sequence,IWorldField field,SurfaceProfile profile)
    {for(int i=sequence.Length-1;i>=0;i--)field=sequence[i]=='S'?(IWorldField)new SurfaceWorldField(field,profile):new TerrainDetailField(field,714103+i);return field;}
    static void Main(string[] args)
    {
        var snow=new SurfaceProfile(enableSnow:true,winterFraction:1,climateOverride:new SurfaceClimate(-25,0,2200,0,0));
        foreach(string sequence in new[]{"","S","SS","D","SD","SSDD","DDDD","SSSSDDDD","DDDDDDDD"})
        {
            var field=new Field();IWorldField wrapped=Wrap(sequence,field,snow),expected=wrapped;
            while(expected is SurfaceWorldField s)expected=s.BaseField;
            var center=new WorldCell(-1,0,1).Center;double top=VoxelTerrainPage.SurfaceTop(expected.Sample(center.X,center.Z).HeightMeters);field.Counter.Samples=0;
            bool accepted=RootTerrainSnapshot.TryCapture(wrapped,-2,2,-2,2,3,out var adapter,out string reason);
            Check(accepted,"valid chain "+sequence+"/"+reason);Check(adapter.TryGetBareTop(-1,1,out double actual)&&actual==top,"detail preserved "+sequence);
            Check(field.Counter.Samples==1&&adapter.SampleCalls==1,"one bare backing call "+sequence);
            Check(adapter.IsCurrent(),"valid chain current");cases.Add(new{sequence,accepted,top=actual,backing_calls=field.Counter.Samples});
            field.Revision++;Check(!adapter.IsCurrent()&&adapter.Failure=="stale-source","nested detail live revision");
        }
        foreach(string sequence in new[]{"DS","SDS","DDSD","SDDS","DSDS","DSSDD","SSDDDSS"})
        {
            var field=new Field();bool accepted=RootTerrainSnapshot.TryCapture(Wrap(sequence,field,snow),0,0,0,0,1,out var adapter,out string reason);
            Check(!accepted&&adapter==null&&reason=="nested-surface-wrapper","reject inner surface "+sequence+"/"+reason);
            Check(field.Counter.Samples==0,"reject before height sampling");cases.Add(new{sequence,accepted,reason});
        }
        foreach(string sequence in new[]{"SSSSSSSSS","DDDDDDDDD","SSSSSDDDD"})
        {Check(!RootTerrainSnapshot.TryCapture(Wrap(sequence,new Field(),snow),0,0,0,0,1,out _,out _),"whole chain depth limit "+sequence);}
        // Even dry inner surface wrappers are rejected structurally; no profile-based bypass.
        Check(!RootTerrainSnapshot.TryCapture(Wrap("DS",new Field(),new SurfaceProfile(enableSnow:false)),0,0,0,0,1,out _,out string dry)&&dry=="nested-surface-wrapper","dry inner Surface rejected");
        var owner=new Field();owner.SnapshotOverride=Wrap("DS",new Field(),snow);
        Check(!RootTerrainSnapshot.TryCapture(owner,0,0,0,0,1,out _,out string snapshotReason)&&snapshotReason=="nested-surface-wrapper","returned snapshot chain also validated");
        var immutableWithoutContract=new Frozen(50,"not-snapshot-provider",new Count());
        Check(!RootTerrainSnapshot.TryCapture(immutableWithoutContract,0,0,0,0,1,out _,out string unsupported)&&unsupported=="snapshot-provider-required","unsupported terminal provider");
        var basis=new Field();var detail=new TerrainDetailField(basis,714103);Check(ReferenceEquals(detail.BaseField,basis),"accessor exposes exact readonly basis");
        Check(RootTerrainSnapshot.TryCapture(new SurfaceWorldField(detail,snow),0,0,0,0,1,out var cancel,out _),"cancel fixture capture");cancel.Cancel();Check(!cancel.TryGetBareTop(0,0,out double zero)&&zero==0&&basis.Counter.Samples==0,"cancel before sample");
        File.WriteAllText(args[0],JsonSerializer.Serialize(new{status="PASS",checks,cases,scope="Small analytical source-chain tests only; no world load/generation/native. Readonly sealed wrapper constructors cannot form cycles without bypassing their constructors; cycle defense is present but no reflection-based cycle fixture was used."},new JsonSerializerOptions{WriteIndented=true}));
        Console.WriteLine("PASS "+checks+" layering/accessor/freshness checks");
    }
}
