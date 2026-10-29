using System;
using System.IO;
using System.Text.Json;
using AshenVoxel.WorldScale;
static class Program
{
    const string Id="aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    sealed class Constant:IWorldFieldBounds,IClimateWorldField
    {
        readonly SurfaceClimate climate;readonly double height;
        public Constant(SurfaceClimate value,double h=100){climate=value;height=h;}
        public string SourceIdentity=>"climate-regression/"+Id;
        public SurfaceSample Sample(double x,double z)=>new SurfaceSample(height,6,6,true);
        public HeightBounds QueryBounds(double a,double b,double c,double d)=>new HeightBounds(height,height);
        public bool TrySampleClimate(double x,double z,out SurfaceClimate value){value=climate;return true;}
    }
    static int checks;
    static void Require(bool value,string message){if(!value)throw new InvalidDataException(message);checks++;}
    static ExplorationRefinementField Blend(SurfaceClimate baseline,SurfaceClimate fine,int a,int b)
    {
        var field=new ExplorationRefinementField(new Constant(baseline),Id);
        int[] distances={0,a,b};
        for(int i=0;i<distances.Length;i++)
        {
            var core=new ExplorationRefinementBounds(distances[i],-16,32,16);
            var patch=ExplorationRefinementPatch.CopySnapshot(new Constant(fine),"patch"+i,Id,core,128,32,5);
            Require(field.TryPublish(patch,new WorldPoint(10000,200,10000),out _),"Publish test patch");
        }
        return field;
    }
    static void Reject(Action action,string name)
    {bool rejected=false;try{action();}catch(ArgumentException){rejected=true;}Require(rejected,"Invalid climate accepted: "+name);}
    static void Run()
    {
        var baseline=new SurfaceClimate(12,.91,.91,.91,0);
        var lower=Blend(baseline,new SurfaceClimate(12,0,0,0,0),1,2);
        Require(lower.TrySampleClimate(0,0,out var zero),"Lower boundary unavailable");
        Require(zero.SeasonalityC==0&&zero.PrecipitationMm==0&&zero.PrecipitationCvPercent==0,"Zero boundary must remain zero");
        foreach(var pair in new[]{(1,6),(1,1),(6,2)})
        {
            var upper=Blend(baseline,new SurfaceClimate(12,150,100000,10000,0),pair.Item1,pair.Item2);
            Require(upper.TrySampleClimate(0,0,out var value),"Upper boundary unavailable");
            Require(Math.Abs(value.SeasonalityC-150)<1e-10&&Math.Abs(value.PrecipitationMm-100000)<1e-8&&Math.Abs(value.PrecipitationCvPercent-10000)<1e-9,"Upper boundary drift");
        }
        var mixed=Blend(baseline,new SurfaceClimate(12,23,1500,42,-.006),1,2);
        Require(mixed.TrySampleClimate(0,0,out var normal),"Interior climate unavailable");
        double OldBlend(double fine)
        {double sum=0,delta=0;foreach(int d in new[]{0,1,2}){double w=new ExplorationRefinementBounds(d,-16,32,16).Weight(0,0,128);sum+=w;delta+=(fine-.91)*w;}return .91+delta/Math.Max(1,sum);}
        Require(BitConverter.DoubleToInt64Bits(normal.SeasonalityC)==BitConverter.DoubleToInt64Bits(OldBlend(23))&&
            BitConverter.DoubleToInt64Bits(normal.PrecipitationMm)==BitConverter.DoubleToInt64Bits(OldBlend(1500))&&
            BitConverter.DoubleToInt64Bits(normal.PrecipitationCvPercent)==BitConverter.DoubleToInt64Bits(OldBlend(42)),"Valid interior result changed bits");
        var thermal=new ExplorationRefinementField(new Constant(new SurfaceClimate(99,1,1,1,-.006),1000),Id);
        var patch=ExplorationRefinementPatch.CopySnapshot(new Constant(new SurfaceClimate(99,1,1,1,0)),"thermal",Id,new ExplorationRefinementBounds(64,-16,96,16),128,32,5);
        Require(thermal.TryPublish(patch,new WorldPoint(10000,200,10000),out _),"Publish thermal fixture");
        Require(!thermal.TrySampleClimate(0,0,out _),"Physically out-of-range temperature must retain unavailable result");
        Reject(()=>new SurfaceClimate(12,-1,1,1,0),"negative seasonality");
        Reject(()=>new SurfaceClimate(12,1,100001,1,0),"rain above maximum");
        Reject(()=>new SurfaceClimate(12,1,1,double.PositiveInfinity,0),"infinity");
        Reject(()=>new SurfaceClimate(12,1,1,1,double.NaN),"NaN");
    }
    static void Main(string[] args)
    {
        string status="PASS",error=null;try{Run();}catch(Exception e){status="FAIL";error=e.ToString();Environment.ExitCode=1;}
        File.WriteAllText(args[0],JsonSerializer.Serialize(new{status,checks,error,scope="Engine-free production live-refinement climate interpolation, actual CopySnapshot/Weight/publication path; no Unity execution"},new JsonSerializerOptions{WriteIndented=true}));
        Console.WriteLine(status+" "+checks+" climate blend checks"+(error==null?"":"\n"+error));
    }
}
