using System;
namespace AshenVoxel.WorldScale
{
    public interface IWorldField
    {
        string SourceIdentity { get; }
        SurfaceSample Sample(double xMeters,double zMeters);
    }
    public readonly struct HeightBounds
    {
        public readonly double Minimum,Maximum;
        public HeightBounds(double minimum,double maximum){Numeric.Finite(minimum,nameof(minimum));Numeric.Finite(maximum,nameof(maximum));if(minimum>maximum)throw new ArgumentException("Inverted height bounds.");Minimum=minimum;Maximum=maximum;}
    }
    public interface IWorldFieldBounds : IWorldField
    {
        HeightBounds QueryBounds(double minimumX,double minimumZ,double maximumX,double maximumZ);
    }
    public readonly struct SurfaceSample
    {
        public const byte ForestBiome=6;
        public readonly double HeightMeters;
        public readonly byte SourceBiome,EffectiveBiome;
        public readonly bool IsLand;
        public SurfaceSample(double heightMeters,byte sourceBiome,byte effectiveBiome,bool isLand)
        {Numeric.Finite(heightMeters,nameof(heightMeters));HeightMeters=heightMeters;SourceBiome=sourceBiome;EffectiveBiome=effectiveBiome;IsLand=isLand;}
    }
    public sealed class WorldMapSettings
    {
        public readonly double WidthMeters,DepthMeters,VerticalScale;
        public readonly bool ForestOverride;
        public WorldMapSettings(double widthMeters=100000,double depthMeters=100000,double verticalScale=1,bool forestOverride=true)
        {
            Numeric.Positive(widthMeters,nameof(widthMeters));Numeric.Positive(depthMeters,nameof(depthMeters));Numeric.Positive(verticalScale,nameof(verticalScale));
            if(widthMeters>1e9||depthMeters>1e9||verticalScale>1000)throw new ArgumentOutOfRangeException("World map settings exceed supported bounds.");
            WidthMeters=widthMeters;DepthMeters=depthMeters;VerticalScale=verticalScale;ForestOverride=forestOverride;
        }
    }
    /// <summary>Deterministic analytic fallback. This is NOT learned diffusion output.</summary>
    public sealed class ProceduralMountainField : IWorldFieldBounds
    {
        public readonly int Seed;
        public readonly double MaxHeightMeters,LandmassWidthMeters;
        public readonly bool ForestOverride;
        public string SourceIdentity=>"analytic-mountains-v1/"+Seed+"/"+MaxHeightMeters.ToString("R",System.Globalization.CultureInfo.InvariantCulture)+"/"+LandmassWidthMeters.ToString("R",System.Globalization.CultureInfo.InvariantCulture);
        public ProceduralMountainField(int seed=714103,double maxHeightMeters=6500,double landmassWidthMeters=100000,bool forestOverride=true)
        {Numeric.Positive(maxHeightMeters,nameof(maxHeightMeters));Numeric.Positive(landmassWidthMeters,nameof(landmassWidthMeters));if(maxHeightMeters>20000||landmassWidthMeters<100)throw new ArgumentOutOfRangeException();Seed=seed;MaxHeightMeters=maxHeightMeters;LandmassWidthMeters=landmassWidthMeters;ForestOverride=forestOverride;}
        public SurfaceSample Sample(double xMeters,double zMeters)
        {
            Numeric.Finite(xMeters,nameof(xMeters));Numeric.Finite(zMeters,nameof(zMeters));
            double x=xMeters/LandmassWidthMeters,z=zMeters/LandmassWidthMeters;
            double radial=Math.Sqrt(x*x+z*z),island=1-Numeric.Clamp((radial-.30)/.24,0,1);
            double continental=Noise(x*3,z*3,Seed)*.13;
            double ridge=1-Math.Abs(Noise(x*21+Noise(x*7,z*7,Seed+11)*2,z*21,Seed+17));
            double fine=Noise(x*130,z*130,Seed+23)*.06+Noise(x*520,z*520,Seed+29)*.012;
            // A broad central massif supplies genuinely kilometer-high observation points.
            double massif=Math.Exp(-(x*x+z*z)*230)*.62;
            double elevation=(ridge*ridge*.49+massif+continental+fine)*island;
            double h=Numeric.Clamp(MaxHeightMeters*(elevation-.08)-Math.Pow(1-island,2)*1300,-2000,MaxHeightMeters);
            bool land=h>=0;byte biome=land?(byte)6:(byte)0;
            return new SurfaceSample(h,biome,land&&ForestOverride?SurfaceSample.ForestBiome:biome,land);
        }
        public HeightBounds QueryBounds(double minimumX,double minimumZ,double maximumX,double maximumZ)
        {
            Numeric.Finite(minimumX,nameof(minimumX));Numeric.Finite(minimumZ,nameof(minimumZ));Numeric.Finite(maximumX,nameof(maximumX));Numeric.Finite(maximumZ,nameof(maximumZ));
            if(minimumX>maximumX||minimumZ>maximumZ)throw new ArgumentException("Inverted region.");
            double dx=Math.Max(0,Math.Max(minimumX,-maximumX))/LandmassWidthMeters,dz=Math.Max(0,Math.Max(minimumZ,-maximumZ))/LandmassWidthMeters;
            double radial=Math.Sqrt(dx*dx+dz*dz),island=1-Numeric.Clamp((radial-.30)/.24,0,1);
            double massif=Math.Exp(-(dx*dx+dz*dz)*230)*.62;
            // Ridge^2 <=1; coherent noise terms remain within their authored amplitudes.
            double upper=MaxHeightMeters*((.49+massif+.202)*island-.08)-Math.Pow(1-island,2)*1300;
            return new HeightBounds(-2000,Numeric.Clamp(upper,-2000,MaxHeightMeters));
        }
        static double Noise(double x,double z,int seed)
        {
            long ix=(long)Math.Floor(x),iz=(long)Math.Floor(z);double u=x-ix,v=z-iz;u=u*u*(3-2*u);v=v*v*(3-2*v);
            double a=Hash(ix,iz,seed),b=Hash(ix+1,iz,seed),c=Hash(ix,iz+1,seed),d=Hash(ix+1,iz+1,seed);
            return (a+(b-a)*u)*(1-v)+(c+(d-c)*u)*v;
        }
        static double Hash(long x,long z,int seed)
        {unchecked{ulong n=(ulong)x*0x9e3779b185ebca87UL^(ulong)z*0xc2b2ae3d27d4eb4fUL^(uint)seed;n^=n>>30;n*=0xbf58476d1ce4e5b9UL;n^=n>>27;n*=0x94d049bb133111ebUL;n^=n>>31;return (n>>11)*(1.0/9007199254740992.0)*2-1;}}
    }
    public sealed class AtlasWorldField : IWorldFieldBounds
    {
        public readonly HeightAtlas Atlas;
        public readonly WorldMapSettings Settings;
        public string SourceIdentity=>Atlas.SourceIdentity+"/world-map-v1/"+Settings.WidthMeters.ToString("R",System.Globalization.CultureInfo.InvariantCulture)+"/"+Settings.DepthMeters.ToString("R",System.Globalization.CultureInfo.InvariantCulture)+"/"+Settings.VerticalScale.ToString("R",System.Globalization.CultureInfo.InvariantCulture)+"/forest="+Settings.ForestOverride;
        public AtlasWorldField(HeightAtlas atlas,WorldMapSettings settings=null){Atlas=atlas??throw new ArgumentNullException(nameof(atlas));Settings=settings??new WorldMapSettings(atlas.ExtentX,atlas.ExtentZ);}
        public HeightBounds QueryBounds(double minimumX,double minimumZ,double maximumX,double maximumZ)
        {
            Numeric.Finite(minimumX,nameof(minimumX));Numeric.Finite(minimumZ,nameof(minimumZ));Numeric.Finite(maximumX,nameof(maximumX));Numeric.Finite(maximumZ,nameof(maximumZ));
            if(minimumX>maximumX||minimumZ>maximumZ)throw new ArgumentException("Inverted region.");
            double u0=minimumX/Settings.WidthMeters+.5,u1=maximumX/Settings.WidthMeters+.5,v0=minimumZ/Settings.DepthMeters+.5,v1=maximumZ/Settings.DepthMeters+.5;
            double outside=-1000*Settings.VerticalScale;
            if(u1<0||u0>1||v1<0||v0>1)return new HeightBounds(outside,outside);
            int x0=(int)Math.Floor(Numeric.Clamp(u0,0,1)*(Atlas.Columns-1)),x1=(int)Math.Ceiling(Numeric.Clamp(u1,0,1)*(Atlas.Columns-1));
            int z0=(int)Math.Floor(Numeric.Clamp(v0,0,1)*(Atlas.Rows-1)),z1=(int)Math.Ceiling(Numeric.Clamp(v1,0,1)*(Atlas.Rows-1));
            HeightBounds range=Atlas.QueryBounds(x0,z0,x1,z1);double lo=range.Minimum*Settings.VerticalScale,hi=range.Maximum*Settings.VerticalScale;
            if(u0<0||u1>1||v0<0||v1>1){lo=Math.Min(lo,outside);hi=Math.Max(hi,outside);}
            return new HeightBounds(lo,hi);
        }
        public SurfaceSample Sample(double xMeters,double zMeters)
        {
            Numeric.Finite(xMeters,nameof(xMeters));Numeric.Finite(zMeters,nameof(zMeters));
            double u=xMeters/Settings.WidthMeters+.5,v=zMeters/Settings.DepthMeters+.5;
            if(u<0||u>1||v<0||v>1)return new SurfaceSample(-1000*Settings.VerticalScale,0,0,false);
            double px=u*(Atlas.Columns-1),pz=v*(Atlas.Rows-1);int x=(int)Math.Floor(px),z=(int)Math.Floor(pz),xx=Math.Min(x+1,Atlas.Columns-1),zz=Math.Min(z+1,Atlas.Rows-1);
            double fx=px-x,fz=pz-z;
            double h=(Atlas.HeightAt(x,z)*(1-fx)+Atlas.HeightAt(xx,z)*fx)*(1-fz)+(Atlas.HeightAt(x,zz)*(1-fx)+Atlas.HeightAt(xx,zz)*fx)*fz;
            int nx=(int)Math.Round(px),nz=(int)Math.Round(pz);byte source=Atlas.BiomeAt(nx,nz);bool land=Atlas.LandAt(nx,nz);
            return new SurfaceSample(h*Settings.VerticalScale,source,land&&Settings.ForestOverride?SurfaceSample.ForestBiome:source,land);
        }
    }
}
