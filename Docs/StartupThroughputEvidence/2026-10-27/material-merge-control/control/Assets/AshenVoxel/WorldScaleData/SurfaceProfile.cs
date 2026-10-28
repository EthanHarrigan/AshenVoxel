using System;
using System.Globalization;
using AshenVoxel.Core;
namespace AshenVoxel.WorldScale
{
    /// <summary>Surface climate at the actual terrain elevation. The model's first
    /// climate channel has ALREADY applied its beta/elevation adjustment.</summary>
    public readonly struct SurfaceClimate
    {
        public readonly double TemperatureC,SeasonalityC,PrecipitationMm,PrecipitationCvPercent,LapseRateCPerMeter;
        public SurfaceClimate(double temperatureC,double seasonalityC,double precipitationMm,double precipitationCvPercent,double lapseRateCPerMeter)
        {
            Numeric.Finite(temperatureC,nameof(temperatureC));Numeric.Finite(seasonalityC,nameof(seasonalityC));Numeric.Finite(precipitationMm,nameof(precipitationMm));
            Numeric.Finite(precipitationCvPercent,nameof(precipitationCvPercent));Numeric.Finite(lapseRateCPerMeter,nameof(lapseRateCPerMeter));
            if(temperatureC< -150||temperatureC>100||seasonalityC<0||seasonalityC>150||precipitationMm<0||precipitationMm>100000||precipitationCvPercent<0||precipitationCvPercent>10000||Math.Abs(lapseRateCPerMeter)>.1)throw new ArgumentOutOfRangeException("climate");
            TemperatureC=temperatureC;SeasonalityC=seasonalityC;PrecipitationMm=precipitationMm;PrecipitationCvPercent=precipitationCvPercent;LapseRateCPerMeter=lapseRateCPerMeter;
        }
    }
    public interface IClimateWorldField
    {
        bool TrySampleClimate(double xMeters,double zMeters,out SurfaceClimate climate);
    }
    /// <summary>Immutable static surface-generation settings, in meters/degrees/Celsius.
    /// This is a reproducible terrain-attribute heuristic, not an erosion or snow-energy solver.</summary>
    public sealed class SurfaceProfile
    {
        public const int Version=2;
        public const double SampleRadiusMax=32;
        public readonly int Seed;
        public readonly double SeaLevelMeters,SnowlineMeters,MaxSnowDepthMeters,WindStrength,WinterFraction,TemperatureBiasC;
        /// <summary>Wind transport direction; zero=+X,90=+Z. Solar azimuth points toward the sun in the same world axes.</summary>
        public readonly double WindAzimuthDegrees,SolarAzimuthDegrees;
        public readonly bool EnableSnow;
        public readonly MaterialId? ForcedBedrockMaterial;
        public readonly SurfaceClimate? ClimateOverride;
        public string Identity {get;}
        public SurfaceProfile(int seed=714103,double seaLevelMeters=0,double snowlineMeters=4200,double maxSnowDepthMeters=2.4,
            bool enableSnow=true,double windStrength=.7,double winterFraction=.2,double temperatureBiasC=0,
            MaterialId? forcedBedrockMaterial=null,SurfaceClimate? climateOverride=null,double windAzimuthDegrees=36.86989764584402,double solarAzimuthDegrees=287.3540246362613)
        {
            Numeric.Finite(seaLevelMeters,nameof(seaLevelMeters));Numeric.Finite(snowlineMeters,nameof(snowlineMeters));Numeric.Finite(maxSnowDepthMeters,nameof(maxSnowDepthMeters));
            Numeric.Finite(windStrength,nameof(windStrength));Numeric.Finite(winterFraction,nameof(winterFraction));Numeric.Finite(temperatureBiasC,nameof(temperatureBiasC));
            Numeric.Finite(windAzimuthDegrees,nameof(windAzimuthDegrees));Numeric.Finite(solarAzimuthDegrees,nameof(solarAzimuthDegrees));
            if(Math.Abs(seaLevelMeters)>12000||snowlineMeters< -1000||snowlineMeters>20000||maxSnowDepthMeters<0||maxSnowDepthMeters>8||windStrength<0||windStrength>2||winterFraction< -1||winterFraction>1||Math.Abs(temperatureBiasC)>60)throw new ArgumentOutOfRangeException("surface profile");
            if(forcedBedrockMaterial.HasValue&&!IsRock(forcedBedrockMaterial.Value))throw new ArgumentException("A geology override must be a supported rock material.");
            Seed=seed;SeaLevelMeters=seaLevelMeters;SnowlineMeters=snowlineMeters;MaxSnowDepthMeters=maxSnowDepthMeters;EnableSnow=enableSnow;
            WindStrength=windStrength;WinterFraction=winterFraction;TemperatureBiasC=temperatureBiasC;ForcedBedrockMaterial=forcedBedrockMaterial;ClimateOverride=climateOverride;
            WindAzimuthDegrees=NormalizeAngle(windAzimuthDegrees);SolarAzimuthDegrees=NormalizeAngle(solarAzimuthDegrees);
            var inv=CultureInfo.InvariantCulture;
            Identity="surface-v"+Version+"/catalog-v"+MaterialCatalog.Version+"/"+seed+"/"+seaLevelMeters.ToString("R",inv)+"/"+snowlineMeters.ToString("R",inv)+"/"+maxSnowDepthMeters.ToString("R",inv)+"/"+enableSnow+
                "/"+windStrength.ToString("R",inv)+"/"+winterFraction.ToString("R",inv)+"/"+temperatureBiasC.ToString("R",inv)+"/rock="+(forcedBedrockMaterial.HasValue?((byte)forcedBedrockMaterial.Value).ToString(inv):"auto");
            Identity+="/wind="+WindAzimuthDegrees.ToString("R",inv)+"/sun="+SolarAzimuthDegrees.ToString("R",inv);
            if(climateOverride.HasValue){SurfaceClimate c=climateOverride.Value;Identity+="/climate="+c.TemperatureC.ToString("R",inv)+","+c.SeasonalityC.ToString("R",inv)+","+c.PrecipitationMm.ToString("R",inv)+","+c.PrecipitationCvPercent.ToString("R",inv)+","+c.LapseRateCPerMeter.ToString("R",inv);}
        }
        public SurfaceProfile With(int? seed=null,double? seaLevelMeters=null,double? snowlineMeters=null,double? maxSnowDepthMeters=null,bool? enableSnow=null,
            double? windStrength=null,double? winterFraction=null,double? temperatureBiasC=null,MaterialId? forcedBedrockMaterial=null,SurfaceClimate? climateOverride=null,
            bool clearForcedBedrock=false,bool clearClimateOverride=false,double? windAzimuthDegrees=null,double? solarAzimuthDegrees=null)
        {
            if(clearForcedBedrock&&forcedBedrockMaterial.HasValue||clearClimateOverride&&climateOverride.HasValue)throw new ArgumentException("A profile override cannot be set and cleared in the same call.");
            return new SurfaceProfile(seed??Seed,seaLevelMeters??SeaLevelMeters,snowlineMeters??SnowlineMeters,maxSnowDepthMeters??MaxSnowDepthMeters,
                enableSnow??EnableSnow,windStrength??WindStrength,winterFraction??WinterFraction,temperatureBiasC??TemperatureBiasC,
                clearForcedBedrock?null:forcedBedrockMaterial??ForcedBedrockMaterial,clearClimateOverride?null:climateOverride??ClimateOverride,
                windAzimuthDegrees??WindAzimuthDegrees,solarAzimuthDegrees??SolarAzimuthDegrees);
        }
        static double NormalizeAngle(double n){double a=n%360;return a<0?a+360:a;}
        public static bool IsRock(MaterialId m)=>m==MaterialId.Stone||m==MaterialId.Granite||m==MaterialId.Limestone||m==MaterialId.Sandstone||m==MaterialId.Shale;
    }
}
