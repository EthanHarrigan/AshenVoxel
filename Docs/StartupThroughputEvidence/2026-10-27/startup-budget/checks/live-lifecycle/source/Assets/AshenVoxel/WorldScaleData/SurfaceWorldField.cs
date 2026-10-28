using System;
namespace AshenVoxel.WorldScale
{
    /// <summary>Derived visual height field. The input remains unsnowed source ground;
    /// material columns and proxy geometry share the exact same snow depth.</summary>
    public sealed class SurfaceWorldField : IWorldFieldBounds,IClimateWorldField
    {
        public readonly IWorldField BaseField;
        public readonly SurfaceMaterialSampler Sampler;
        readonly HeightBounds? fallbackBounds;
        public SurfaceProfile Profile=>Sampler.Profile;
        public string SourceIdentity=>Sampler.SourceIdentity;
        public SurfaceWorldField(IWorldField baseField,SurfaceProfile profile=null,HeightBounds? fallbackBounds=null)
        {BaseField=baseField??throw new ArgumentNullException(nameof(baseField));Sampler=new SurfaceMaterialSampler(baseField,profile);this.fallbackBounds=fallbackBounds;}
        public SurfaceSample Sample(double xMeters,double zMeters)
        {SurfaceColumn c=Sampler.EvaluateColumn(xMeters,zMeters);return new SurfaceSample(c.SurfaceHeightMeters,c.SourceBiome,c.EffectiveBiome,c.IsLand);}
        public HeightBounds QueryBounds(double minimumX,double minimumZ,double maximumX,double maximumZ)
        {
            HeightBounds b=BaseField is IWorldFieldBounds bounded?bounded.QueryBounds(minimumX,minimumZ,maximumX,maximumZ):
                fallbackBounds??throw new InvalidOperationException("SurfaceWorldField requires conservative source bounds for LOD queries.");
            return new HeightBounds(b.Minimum,b.Maximum+Profile.MaxSnowDepthMeters);
        }
        public bool TrySampleClimate(double xMeters,double zMeters,out SurfaceClimate climate)
        {if(BaseField is IClimateWorldField field)return field.TrySampleClimate(xMeters,zMeters,out climate);climate=default;return false;}
    }
}
