using System;
using System.Threading;

namespace AshenVoxel.WorldScale
{
    /// <summary>Immutable far cache over atomically captured live terrain. Far
    /// data never schedules work; live fine publications require explicit refresh.</summary>
    public sealed class BakedOverviewField:IWorldFieldBounds,IClimateWorldField,IWorldFieldSnapshotProvider
    {
        readonly object gate=new object();
        readonly ExplorationAtlasField baked;
        readonly ExplorationRefinementField live;
        readonly BakedFarTerrain far;
        State current;
        sealed class State:IWorldFieldBounds,IClimateWorldField
        {
            readonly IWorldFieldBounds basis;
            readonly ExplorationAtlasField baked;
            readonly BakedFarTerrain far;
            readonly ExplorationPublishedPatchInfo[] fine;
            internal readonly int Revision;
            public string SourceIdentity{get;}
            internal State(IWorldFieldBounds basis,ExplorationAtlasField baked,BakedFarTerrain far,ExplorationPublishedPatchInfo[] fine,int revision)
            {
                this.basis=basis;this.baked=baked;this.far=far;this.fine=(ExplorationPublishedPatchInfo[])fine.Clone();Revision=revision;
                SourceIdentity="baked-overview-v1/"+far.Identity+"/"+basis.SourceIdentity;
            }
            double FineWeight(double x,double z)
            {
                double total=0;foreach(var p in fine){double blend=p.CoreBounds.MinimumX-p.InfluenceBounds.MinimumX;if(blend>0)total+=p.CoreBounds.Weight(x,z,blend);}
                return Math.Max(baked.FineWeightAt(x,z),Math.Min(1,total));
            }
            public SurfaceSample Sample(double x,double z)
            {
                Numeric.Finite(x,nameof(x));Numeric.Finite(z,nameof(z));
                var b=basis.Sample(x,z);double h=far.Sample(x,z,out double coverage),weight=coverage*(1-FineWeight(x,z));
                double height=b.HeightMeters+(h-b.HeightMeters)*weight;
                return new SurfaceSample(height,b.SourceBiome,b.EffectiveBiome,height>=0);
            }
            internal double Resolution(double x,double z)
            {
                double fineWeight=FineWeight(x,z);
                if(fineWeight>=.999999)
                {double resolution=baked.ModelResolutionMeters(x,z);foreach(var p in fine)if(p.CoreBounds.Contains(x,z))resolution=Math.Min(resolution,p.SpacingMeters);return resolution;}
                far.Sample(x,z,out double coverage);return coverage*(1-fineWeight)>=.999999?far.SpacingMeters:baked.ModelResolutionMeters(x,z);
            }
            public bool TrySampleClimate(double x,double z,out SurfaceClimate climate)
            {
                if(!(basis is IClimateWorldField source)||!source.TrySampleClimate(x,z,out var b)){climate=default;return false;}
                double oldHeight=basis.Sample(x,z).HeightMeters,newHeight=Sample(x,z).HeightMeters;
                double temperature=Numeric.Clamp(b.TemperatureC+b.LapseRateCPerMeter*(Math.Max(newHeight,0)-Math.Max(oldHeight,0)),-150,100);
                climate=new SurfaceClimate(temperature,b.SeasonalityC,b.PrecipitationMm,b.PrecipitationCvPercent,b.LapseRateCPerMeter);return true;
            }
            public HeightBounds QueryBounds(double x0,double z0,double x1,double z1)
            {
                Numeric.Finite(x0,nameof(x0));Numeric.Finite(z0,nameof(z0));Numeric.Finite(x1,nameof(x1));Numeric.Finite(z1,nameof(z1));
                if(x0>x1||z0>z1)throw new ArgumentException("Inverted baked overview bounds");
                var b=basis.QueryBounds(x0,z0,x1,z1);
                if(!far.TryHeightBounds(x0,z0,x1,z1,out var f))return b;
                // Every final sample is a convex blend of base and covered far
                // heights. Retain base extrema through zero-coverage/fine edges.
                return new HeightBounds(Math.Min(b.Minimum,f.Minimum),Math.Max(b.Maximum,f.Maximum));
            }
        }
        public BakedOverviewField(ExplorationAtlasField baked,ExplorationRefinementField live,BakedFarTerrain far)
        {
            this.baked=baked??throw new ArgumentNullException(nameof(baked));this.live=live??throw new ArgumentNullException(nameof(live));this.far=far??throw new ArgumentNullException(nameof(far));
            if(far.BaseIdentity!=baked.SourceIdentity||live.BaseSourceIdentity!=baked.SourceIdentity)throw new ArgumentException("Far cache/live source belongs to another atlas; re-bake or pair the original source");
            CaptureBase(out var basis,out var fine);current=new State(basis,baked,far,fine,0);
        }
        void CaptureBase(out IWorldFieldBounds basis,out ExplorationPublishedPatchInfo[] fine)
        {
            for(int attempt=0;attempt<8;attempt++)
            {int revision=live.Revision;basis=live.CaptureSnapshot() as IWorldFieldBounds??throw new InvalidOperationException("Live snapshot requires conservative bounds");fine=live.CapturePublishedCoverage();if(fine.Length>32)throw new InvalidOperationException("Too many live fine footprints");if(live.Revision==revision)return;}
            throw new InvalidOperationException("Live source changed throughout baked overview snapshot capture");
        }
        public string SourceIdentity=>Volatile.Read(ref current).SourceIdentity;
        public int Revision=>Volatile.Read(ref current).Revision;
        public long ResidentPayloadBytes=>far.PayloadBytes+far.BoundsBytes;
        public SurfaceSample Sample(double x,double z)=>Volatile.Read(ref current).Sample(x,z);
        public bool TrySampleClimate(double x,double z,out SurfaceClimate climate)=>Volatile.Read(ref current).TrySampleClimate(x,z,out climate);
        public HeightBounds QueryBounds(double x0,double z0,double x1,double z1)=>Volatile.Read(ref current).QueryBounds(x0,z0,x1,z1);
        public double ResolutionMetersAt(double x,double z)=>Volatile.Read(ref current).Resolution(x,z);
        public IWorldField CaptureSnapshot()=>Volatile.Read(ref current);
        public void RefreshBaseSnapshot()
        {lock(gate){CaptureBase(out var basis,out var fine);Volatile.Write(ref current,new State(basis,baked,far,fine,current.Revision+1));}}
    }
}
