using System;
using System.Globalization;
namespace AshenVoxel.WorldScale
{
    /// <summary>Versioned, globally anchored procedural surface relief. This does
    /// not increase the resolution of the underlying learned height atlas.
    /// Rendering, habitat placement and collision share this same field.</summary>
    public sealed class TerrainDetailField:IWorldFieldBounds,IClimateWorldField,IWorldFieldSnapshotProvider
    {
        public const int Version=1;
        public const double MaximumDisplacementMeters=.63;
        readonly IWorldFieldBounds basis;
        /// <summary>Read-only source for validating supported wrapper order without dropping relief.</summary>
        public IWorldField BaseField=>basis;
        public int Seed {get;}
        public string SourceIdentity=>basis.SourceIdentity+"/surface-relief-v1/seed="+Seed.ToString(CultureInfo.InvariantCulture);
        public TerrainDetailField(IWorldField source,int seed)
        {
            basis=source as IWorldFieldBounds??throw new ArgumentException("Surface relief requires conservative source bounds.",nameof(source));
            Seed=seed;
        }
        static double Smooth(double t)=>t*t*t*(t*(t*6-15)+10);
        double Corner(long x,long z,uint channel)
        {
            unchecked
            {
                ulong h=(ulong)x*0x9e3779b185ebca87UL ^ (ulong)z*0xc2b2ae3d27d4eb4fUL ^ (uint)Seed ^ channel;
                h^=h>>30;h*=0xbf58476d1ce4e5b9UL;h^=h>>27;h*=0x94d049bb133111ebUL;h^=h>>31;
                return (h>>11)*(2.0/9007199254740991.0)-1;
            }
        }
        double Noise(double x,double z,double meters,uint channel)
        {
            double px=x/meters,pz=z/meters;long ix=(long)Math.Floor(px),iz=(long)Math.Floor(pz);
            double u=Smooth(px-ix),v=Smooth(pz-iz),a=Corner(ix,iz,channel),b=Corner(ix+1,iz,channel),c=Corner(ix,iz+1,channel),d=Corner(ix+1,iz+1,channel);
            return (a+(b-a)*u)*(1-v)+(c+(d-c)*u)*v;
        }
        double Relief(double x,double z,double height)
        {
            // Keep the coastline and underwater bathymetry intact. Rotated bands
            // avoid aligning the small earth forms with the voxel tile edges.
            double coast=Smooth(Math.Min(1,Math.Max(0,height)/3));
            return coast*(.44*Noise(x*.8+z*.6,z*.8-x*.6,9.7,0x84ab1u)
                +.145*Noise(x*.6-z*.8,z*.6+x*.8,2.9,0x239b3u)
                +.045*Noise(x,z,.83,0x518d7u));
        }
        public SurfaceSample Sample(double x,double z)
        {
            SurfaceSample a=basis.Sample(x,z);double h=a.HeightMeters+Relief(x,z,a.HeightMeters);
            return new SurfaceSample(h,a.SourceBiome,a.EffectiveBiome,a.IsLand);
        }
        public HeightBounds QueryBounds(double x0,double z0,double x1,double z1)
        {
            HeightBounds b=basis.QueryBounds(x0,z0,x1,z1);
            return new HeightBounds(b.Minimum-MaximumDisplacementMeters,b.Maximum+MaximumDisplacementMeters);
        }
        public bool TrySampleClimate(double x,double z,out SurfaceClimate climate)
        {
            if(!(basis is IClimateWorldField c)||!c.TrySampleClimate(x,z,out SurfaceClimate a)){climate=default;return false;}
            double h=basis.Sample(x,z).HeightMeters;
            climate=new SurfaceClimate(a.TemperatureC+a.LapseRateCPerMeter*Relief(x,z,h),a.SeasonalityC,a.PrecipitationMm,a.PrecipitationCvPercent,a.LapseRateCPerMeter);
            return true;
        }
        public IWorldField CaptureSnapshot()=>new TerrainDetailField(basis is IWorldFieldSnapshotProvider p?p.CaptureSnapshot():basis,Seed);
    }
}
