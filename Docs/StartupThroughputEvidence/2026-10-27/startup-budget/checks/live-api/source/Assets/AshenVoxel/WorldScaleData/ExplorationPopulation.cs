using System;
using System.Collections.Generic;
using AshenVoxel.Core;
using AshenVoxel.Core.Details;
namespace AshenVoxel.WorldScale
{
    public readonly struct PopulationKey:IEquatable<PopulationKey>
    {
        public readonly long X,Z;public readonly int Layer;
        public PopulationKey(long x,long z,int layer){if(layer<0||layer>2)throw new ArgumentOutOfRangeException(nameof(layer));X=x;Z=z;Layer=layer;}
        public bool Equals(PopulationKey b)=>X==b.X&&Z==b.Z&&Layer==b.Layer;
        public override bool Equals(object value)=>value is PopulationKey p&&Equals(p);
        public override int GetHashCode(){unchecked{return (X.GetHashCode()*397^Z.GetHashCode())*397^Layer;}}
        public double SizeMeters=>Layer==2?16:64;
    }
    public readonly struct PopulationInstance
    {
        public readonly int PrototypeId;public readonly WorldPoint Position;public readonly float YawDegrees,Scale;
        public readonly ulong StableKey;
        public PopulationInstance(int prototypeId,WorldPoint position,float yawDegrees,float scale,ulong stableKey)
        {PrototypeId=prototypeId;Position=position;YawDegrees=yawDegrees;Scale=scale;StableKey=stableKey;}
    }
    public sealed class PopulationCell
    {
        public readonly PopulationKey Key;public readonly PopulationInstance[] Instances;public readonly int SampledCandidates;
        public PopulationCell(PopulationKey key,PopulationInstance[] instances,int candidates){Key=key;Instances=instances;SampledCandidates=candidates;}
    }
    /// <summary>Bounded deterministic global population. Cell requests never share RNG state.
    /// Source terrain/climate sampling is synchronous data only, never network/inference.</summary>
    public sealed class ExplorationPopulation
    {
        public const int Version=1;
        readonly SurfaceMaterialSampler surface;readonly int seed;
        public double TreeDensity=1,PlantDensity=.72,RockDensity=1;
        public ExplorationPopulation(SurfaceMaterialSampler surface,int seed){this.surface=surface??throw new ArgumentNullException(nameof(surface));this.seed=seed;}
        public ForestStand StandAt(double x,double z)=>ForestBiomes.Sample(surface.EvaluateColumn(x,z),x,z,seed);
        public PopulationCell Build(PopulationKey key)
        {
            if(Math.Abs((double)key.X)>10000000000||Math.Abs((double)key.Z)>10000000000)throw new ArgumentOutOfRangeException(nameof(key));
            double size=key.SizeMeters,x0=key.X*size,z0=key.Z*size;var items=new List<PopulationInstance>();int candidates=0;
            if(key.Layer==0)
            {
                const double spacing=6;
                long ix0=(long)Math.Floor(x0/spacing)-1,ix1=(long)Math.Floor((x0+size)/spacing)+1,iz0=(long)Math.Floor(z0/spacing)-1,iz1=(long)Math.Floor((z0+size)/spacing)+1;
                for(long iz=iz0;iz<=iz1;iz++)for(long ix=ix0;ix<=ix1;ix++)
                {
                    candidates++;ulong h=ForestBiomes.Hash(ix,iz,seed);double x=(ix+.13+ForestBiomes.Unit(ForestBiomes.Mix(h+7))*.74)*spacing;
                    double z=(iz+.13+ForestBiomes.Unit(ForestBiomes.Mix(h+13))*.74)*spacing;
                    if(x<x0||x>=x0+size||z<z0||z>=z0+size)continue;
                    SurfaceColumn c=surface.EvaluateColumn(x,z);ForestStand stand=ForestBiomes.Sample(c,x,z,seed);
                    if(!stand.HasTrees||ForestBiomes.Unit(ForestBiomes.Mix(h+31))>=Math.Min(1,stand.Density*Clamp(TreeDensity,0,2)))continue;
                    int variant=(int)(ForestBiomes.Mix(h+37)&1),stage=stand.Stage==ForestStage.YoungDense?0:1;
                    int prototype=(int)stand.Species*4+stage*2+variant;
                    float scale=1;
                    items.Add(new PopulationInstance(prototype,new WorldPoint(x,c.GroundHeightMeters-.05,z),(float)(ForestBiomes.Unit(ForestBiomes.Mix(h+43))*360),scale,h));
                }
            }
            else
            {
                double density=key.Layer==2?Clamp(PlantDensity,0,2):Clamp(RockDensity,0,2);
                var scatter=new GroundCoverScatter(surface,new GroundCoverProfile(seed,density,key.Layer==2,key.Layer==1,maxCandidates:20000,maxPlacements:4096));
                GroundCoverPlan plan=scatter.Plan(x0,z0,x0+size,z0+size);candidates=plan.ScannedCandidates;
                if(plan.Truncated)throw new InvalidOperationException("Population cell exceeds its explicit placement budget.");
                foreach(GroundCoverPlacement p in plan.Placements)
                {
                    ulong h=ForestBiomes.Hash(p.GridX,p.GridZ,unchecked(seed+p.Layer*9817));int prototype;float scale;
                    if(p.IsRock)
                    {
                        int material=p.Rock.Material==MaterialId.Stone?0:p.Rock.Material==MaterialId.Granite?1:p.Rock.Material==MaterialId.Limestone?2:p.Rock.Material==MaterialId.Sandstone?3:4;
                        int sizeVariant=p.Rock.WidthMeters<1.25?0:1;prototype=200+material*12+((int)p.Rock.ResolvedShape-1)*2+sizeVariant;
                        // Small/large shapes are separately rasterized at10cm; instances
                        // never scale physical voxels away from the required cell size.
                        scale=1;
                    }
                    else{prototype=p.Plant.Kind==PlantKind.Mushroom&&p.Plant.HeightMeters<=.10001?120:100+(int)p.Plant.Kind*2+(int)(h&1);scale=1;}
                    items.Add(new PopulationInstance(prototype,new WorldPoint(p.Anchor.Center.X,p.Anchor.Center.Y-.05,p.Anchor.Center.Z),(float)(ForestBiomes.Unit(ForestBiomes.Mix(h+61))*360),scale,h));
                }
            }
            return new PopulationCell(key,items.ToArray(),candidates);
        }
        static double Clamp(double x,double a,double b){if(double.IsNaN(x)||double.IsInfinity(x))return a;return Math.Max(a,Math.Min(b,x));}
    }
}
