using System;
using AshenVoxel.Core;
namespace AshenVoxel.WorldScale
{
    public readonly struct SurfaceColumn
    {
        public readonly double GroundHeightMeters,SnowDepthMeters,SurfaceHeightMeters,IceDepthMeters,GroundCoverDepthMeters,OrganicDepthMeters,SoilDepthMeters,SedimentDepthMeters;
        public readonly double SlopeDegrees,Curvature,Exposure,Wetness,TemperatureC,PrecipitationMm,WaterLevelMeters;
        public readonly MaterialId SurfaceMaterial,BaseSurfaceMaterial,BedrockMaterial,OrganicMaterial,SoilMaterial,SedimentMaterial;
        public readonly uint SurfaceTint;
        public readonly byte SourceBiome,EffectiveBiome,BiomeUsed;
        public readonly bool HasWater,IsLand,ClimateFromField;
        internal SurfaceColumn(double ground,double snow,double ice,double cover,double organic,double soil,double sediment,double slope,double curvature,double exposure,double wetness,
            double temperature,double precipitation,double sea,MaterialId top,MaterialId baseSurface,MaterialId rock,MaterialId soilMaterial,MaterialId sedimentMaterial,
            uint tint,byte sourceBiome,byte effectiveBiome,byte biomeUsed,bool water,bool land,bool climateFromField)
        {GroundHeightMeters=ground;SnowDepthMeters=snow;SurfaceHeightMeters=ground+snow;IceDepthMeters=ice;GroundCoverDepthMeters=cover;OrganicDepthMeters=organic;SoilDepthMeters=soil;SedimentDepthMeters=sediment;
            SlopeDegrees=slope;Curvature=curvature;Exposure=exposure;Wetness=wetness;TemperatureC=temperature;PrecipitationMm=precipitation;WaterLevelMeters=sea;
            SurfaceMaterial=top;BaseSurfaceMaterial=baseSurface;BedrockMaterial=rock;OrganicMaterial=MaterialId.Litter;SoilMaterial=soilMaterial;SedimentMaterial=sedimentMaterial;SurfaceTint=tint;
            SourceBiome=sourceBiome;EffectiveBiome=effectiveBiome;BiomeUsed=biomeUsed;HasWater=water;IsLand=land;ClimateFromField=climateFromField;}
    }
    /// <summary>Shared, bounded meter-space geology, sediment and static snow heuristics.
    /// A column takes at most15 height queries. SampleMatter then needs no source queries.</summary>
    public sealed class SurfaceMaterialSampler
    {
        public readonly IWorldField BaseField;
        public readonly SurfaceProfile Profile;
        readonly double windX,windZ,solarX,solarZ;
        public string SourceIdentity=>BaseField.SourceIdentity+"/"+Profile.Identity;
        public SurfaceMaterialSampler(IWorldField baseField,SurfaceProfile profile=null)
        {BaseField=baseField??throw new ArgumentNullException(nameof(baseField));Profile=profile??new SurfaceProfile();
            double w=Profile.WindAzimuthDegrees*Math.PI/180,s=Profile.SolarAzimuthDegrees*Math.PI/180;windX=Math.Cos(w);windZ=Math.Sin(w);solarX=Math.Cos(s);solarZ=Math.Sin(s);}
        public SurfaceColumn EvaluateColumn(double x,double z)
        {
            Coordinate(x,z);SurfaceSample center=BaseField.Sample(x,z);double h=center.HeightMeters;
            SurfaceSample east=BaseField.Sample(x+2,z),west=BaseField.Sample(x-2,z),north=BaseField.Sample(x,z+2),south=BaseField.Sample(x,z-2);
            double dx=(east.HeightMeters-west.HeightMeters)/4,dz=(north.HeightMeters-south.HeightMeters)/4;
            double up=1/Math.Sqrt(1+dx*dx+dz*dz),slope=Math.Acos(up)*180/Math.PI;
            double broad=(BaseField.Sample(x+24,z).HeightMeters+BaseField.Sample(x-24,z).HeightMeters+BaseField.Sample(x,z+24).HeightMeters+BaseField.Sample(x,z-24).HeightMeters)*.25;
            double curvature=Clamp((broad-h)/6,-1,1); // positive = sheltered hollow; not flow accumulation
            double upstream=BaseField.Sample(x-windX*32,z-windZ*32).HeightMeters,downstream=BaseField.Sample(x+windX*32,z+windZ*32).HeightMeters;
            double shelter=Clamp((upstream-h)/12,-1,1),windFacing=Clamp((dx*windX+dz*windZ)*up,-1,1);
            double windCrest=Clamp((h-(upstream+downstream)*.5)/12,-1,1);
            double exposure=Clamp(.45-curvature*.3+windFacing*.2-shelter*.22+windCrest*.12,0,1);
            byte biome=center.SourceBiome==0&&center.IsLand?(byte)6:center.SourceBiome;
            SurfaceClimate climate;bool fromField=false;
            if(Profile.ClimateOverride.HasValue)climate=Profile.ClimateOverride.Value;
            else if(BaseField is IClimateWorldField climateField&&climateField.TrySampleClimate(x,z,out climate))fromField=true;
            else climate=FallbackClimate(biome,h);
            double temperature=climate.TemperatureC+Profile.TemperatureBiasC-climate.SeasonalityC*Profile.WinterFraction;
            double rainfall=climate.PrecipitationMm;
            bool water=h<Profile.SeaLevelMeters||(!center.IsLand&&h<=Profile.SeaLevelMeters+.02);
            bool neighboringWater=!east.IsLand||!west.IsLand||!north.IsLand||!south.IsLand||Math.Min(Math.Min(east.HeightMeters,west.HeightMeters),Math.Min(north.HeightMeters,south.HeightMeters))<Profile.SeaLevelMeters;
            double nearSea=Clamp(1-Math.Abs(h-Profile.SeaLevelMeters)/3,0,1);
            double wetness=Clamp(rainfall/(rainfall+850)+curvature*.18+(biome==12?.16:0)+(neighboringWater?nearSea*.25:0)-(slope/90)*.2,0,1);
            MaterialId rock=RockAt(x,TopCellCenter(h),z),surface,soilMat=MaterialId.Dirt,sedimentMat=MaterialId.Clay;
            double retention=Math.Pow(Clamp(1-slope/48,0,1),1.6),soil=(.18+wetness*.8)*retention,cover=.1,sediment=0;
            soil*=rock==MaterialId.Granite?.75:rock==MaterialId.Shale?1.3:rock==MaterialId.Sandstone?1.1:rock==MaterialId.Limestone?.9:.8;
            bool organic=biome==5||biome==6||biome==7||biome==8||biome==9||biome==12;
            bool arid=biome==1||biome==2||biome==13||(rainfall<250&&temperature>5);
            if(water)
            {
                double depth=Profile.SeaLevelMeters-h;
                surface=depth<12?(rock==MaterialId.Stone?MaterialId.BlackSand:MaterialId.Sand):slope<8?MaterialId.Silt:MaterialId.Gravel;
                cover=slope<25?.35:0;soil=0;sediment=slope<12?1.8:.2;sedimentMat=MaterialId.Clay;
            }
            else if(slope>51||retention<.035){surface=rock;cover=soil=sediment=0;}
            else if(neighboringWater&&nearSea>0&&slope<13)
            {surface=wetness>.8&&slope<3?MaterialId.Mud:rock==MaterialId.Stone?MaterialId.BlackSand:MaterialId.Sand;cover=.3+nearSea*.7;soil=0;sediment=1.2;sedimentMat=MaterialId.Gravel;}
            else if(slope>29)
            {surface=MaterialId.Gravel;cover=.1+retention*.25;soil*=.25;sediment=.4*retention;sedimentMat=rock;}
            else if(arid)
            {surface=biome==13||slope<13?MaterialId.Sand:MaterialId.Gravel;cover=surface==MaterialId.Sand?.45+1.2*Noise01(x/90,z/90,Profile.Seed+139):.2;soil=.05;sediment=.8;sedimentMat=rock==MaterialId.Shale?MaterialId.Clay:MaterialId.Gravel;}
            else if(wetness>.75&&slope<9&&(biome==12||curvature>.22))
            {surface=organic?MaterialId.Peat:wetness>.87?MaterialId.Mud:MaterialId.Clay;cover=surface==MaterialId.Peat?.3+wetness*.5:.2;soil=.6;soilMat=MaterialId.Mud;sediment=1.2;sedimentMat=MaterialId.Clay;}
            else if(curvature>.45&&wetness>.53&&slope<12)
            {surface=MaterialId.Silt;cover=.2;soilMat=MaterialId.Mud;sediment=.65;sedimentMat=MaterialId.Clay;}
            else
            {surface=MaterialId.Grass;cover=.1;soilMat=wetness>.65?MaterialId.Mud:MaterialId.Dirt;sediment=organic?.18:.08;sedimentMat=MaterialId.Clay;}
            if(!water&&surface==MaterialId.Grass&&(climate.TemperatureC< -2||h>Profile.SnowlineMeters-400))
            {surface=retention>.5?MaterialId.Gravel:rock;cover=surface==rock?0:.1;soil*=.3;sediment=.05;sedimentMat=rock;}
            else if(!water&&surface==MaterialId.Grass&&organic&&Noise01(x/8,z/8,Profile.Seed+151)>.67)
            {surface=MaterialId.Litter;cover=.1+.15*wetness;}
            if(!water&&temperature< -6&&biome==11&&slope<24&&curvature>.15&&wetness>.4)
            {surface=MaterialId.Ice;cover=.2;soil=0;sediment=0;}
            double snow=Snow(x,z,temperature,rainfall,slope,curvature,exposure,dx,dz,water);
            double ice=!water&&biome==11&&temperature< -5&&slope<24&&snow>.7&&curvature>.1?Math.Min(1.4,snow*.65):0;
            double organicDepth=!water&&organic&&(surface==MaterialId.Grass||surface==MaterialId.Litter)? .10+wetness*.18:0;
            MaterialId top=snow>=.099?MaterialId.Snow:surface;
            uint tint=Color(top,x,TopCellCenter(h+snow),z,biome,wetness,rock);
            return new SurfaceColumn(h,snow,ice,cover,organicDepth,soil,sediment,slope,curvature,exposure,wetness,temperature,rainfall,Profile.SeaLevelMeters,top,surface,rock,soilMat,sedimentMat,tint,
                center.SourceBiome,center.EffectiveBiome,biome,water,center.IsLand,fromField);
        }
        public MatterSample SampleMatter(WorldPoint position,SurfaceColumn column)
        {
            Coordinate(position.X,position.Z);Numeric.Finite(position.Y,nameof(position));
            double depth=column.GroundHeightMeters-position.Y;MaterialId material;
            if(position.Y>column.SurfaceHeightMeters)return new MatterSample(MaterialId.Air);
            if(depth<0)material=MaterialId.Snow;
            else if(depth<column.IceDepthMeters)material=MaterialId.Ice;
            else if(depth<column.GroundCoverDepthMeters)material=column.BaseSurfaceMaterial;
            else if(depth<column.GroundCoverDepthMeters+column.OrganicDepthMeters)material=column.OrganicMaterial;
            else if(depth<column.GroundCoverDepthMeters+column.OrganicDepthMeters+column.SoilDepthMeters)material=column.SoilMaterial;
            else if(depth<column.GroundCoverDepthMeters+column.OrganicDepthMeters+column.SoilDepthMeters+column.SedimentDepthMeters)material=column.SedimentMaterial;
            else material=RockAt(position.X,position.Y,position.Z);
            return new MatterSample(material,Color(material,position.X,position.Y,position.Z,column.BiomeUsed,column.Wetness,column.BedrockMaterial));
        }
        public MatterSample SampleMatter(WorldCell cell,SurfaceColumn column)=>SampleMatter(cell.Center,column);
        double Snow(double x,double z,double temp,double precipitation,double slope,double curvature,double exposure,double dx,double dz,bool water)
        {
            if(!Profile.EnableSnow||Profile.MaxSnowDepthMeters<.1||water||temp>2.5||slope>=78)return 0;
            double cold=Clamp((1.5-temp)/9,0,1),supply=Clamp((precipitation-60)/1100,0,1);
            double slopeRetention=Math.Pow(Clamp((55-slope)/35,0,1),1.4);
            double solarExposure=Clamp((-dx*solarX-dz*solarZ)/Math.Sqrt(1+dx*dx+dz*dz),-1,1);
            double drift=Clamp(.57+curvature*.3-exposure*Profile.WindStrength*.46-solarExposure*.11,.04,1);
            double patch=Noise01(x/14,z/14,Profile.Seed+911)*.65+Noise01(x/47,z/47,Profile.Seed+919)*.35;
            // Coherent bare patches remain even high above snowline, especially on crests.
            double threshold=.20+exposure*Profile.WindStrength*.42+Clamp(slope/55,0,1)*.17+(1-cold)*.25;
            if(patch<threshold)return 0;
            if(slope>55)slopeRetention=Clamp((curvature-.15)/.85,0,1)*Clamp((patch-.62)/.38,0,1)*.35;
            double amount=Profile.MaxSnowDepthMeters*cold*(.12+.88*supply)*slopeRetention*drift*(.32+.96*patch);
            double snow=Math.Floor(Clamp(amount,0,Profile.MaxSnowDepthMeters)*10+1e-7)*.1;
            return snow<.099?0:snow;
        }
        public SurfaceClimate FallbackClimate(byte biome,double heightMeters)
        {
            double t=23,season=7,rain=1100;
            switch(biome)
            {
                case 1:t=30;season=5;rain=80;break;case 2:t=15;season=16;rain=150;break;
                case 3:t=27;season=6;rain=800;break;case 4:t=18;season=12;rain=650;break;
                case 5:t=25;season=5;rain=1500;break;case 6:t=24;season=9;rain=1100;break;
                case 7:t=27;season=2;rain=2600;break;case 8:t=20;season=6;rain=2300;break;
                case 9:t=7;season=15;rain=650;break;case 10:t=1;season=10;rain=350;break;
                case 11:t=-7;season=9;rain=850;break;case 12:t=23;season=7;rain=1900;break;
                case 13:t=31;season=7;rain=65;break;
            }
            // Snowline is an artist climate reference, not a hard painted altitude.
            t+=(Profile.SnowlineMeters-4200)*.0065;
            t-=(heightMeters-Profile.SeaLevelMeters)*.0065;
            return new SurfaceClimate(Clamp(t,-140,90),season,rain,35,-.0065);
        }
        public MaterialId RockAt(double x,double y,double z)
        {
            Coordinate(x,z);Numeric.Finite(y,nameof(y));if(Math.Abs(y)>1e12)throw new ArgumentOutOfRangeException(nameof(y));
            if(Profile.ForcedBedrockMaterial.HasValue)return Profile.ForcedBedrockMaterial.Value;
            double province=Noise01(x/1900,z/1900,Profile.Seed+11)*.68+Noise01(x/7400,z/7400,Profile.Seed+17)*.32;
            if(province<.29)return MaterialId.Stone;
            if(province<.49)return MaterialId.Granite;
            // Gently deformed sedimentary beds persist across adjacent columns and cuts.
            // Isolated control: remove the exact global54m vertical period.
            // Projected coherent noise is a cheap visual heuristic, not a
            // reconstructed geology volume or calibrated shipping policy.
            double lithology=Noise01((x+y*.15)/900,(z-y*.08)/900,Profile.Seed+41);
            return lithology<.42?MaterialId.Sandstone:lithology<.70?MaterialId.Limestone:MaterialId.Shale;
        }
        public uint Color(MaterialId material,double x,double y,double z,byte biome,double wetness,MaterialId parentRock)
        {
            Coordinate(x,z);Numeric.Finite(y,nameof(y));if(Math.Abs(y)>1e12||!MaterialCatalog.IsValid(material)||double.IsNaN(wetness)||wetness<0||wetness>1)throw new ArgumentOutOfRangeException("appearance input");
            if(material==MaterialId.Air)return 0;
            uint basis=VoxelColor.Base(material);
            if(material==MaterialId.Grass)
            {
                switch(biome){case 3:basis=0xff988b43;break;case 4:basis=0xff898443;break;case 7:basis=0xff376e36;break;
                    case 8:basis=0xff365f49;break;case 9:basis=0xff526347;break;case 10:basis=0xff74794e;break;case 12:basis=0xff426d49;break;default:basis=0xff486331;break;}
            }
            if(material==MaterialId.Gravel&&SurfaceProfile.IsRock(parentRock))basis=VoxelColor.Base(parentRock);
            double broad=Noise01(x/9,z/9,Profile.Seed+211)*2-1,layer=Noise01(x*.43+y*.13,z*.43-y*.07,Profile.Seed+223)*2-1;
            long cx=(long)Math.Floor(x*5),cy=(long)Math.Floor(y*5),cz=(long)Math.Floor(z*5);
            double grain=Unit(Hash(cx,cy,cz,Profile.Seed+227))*2-1;
            double variation=material==MaterialId.Snow?.022:material==MaterialId.Ice?.035:.08;
            double brightness=1+broad*variation+layer*variation*.45+grain*variation*.28;
            if(material==MaterialId.Mud||material==MaterialId.Clay||material==MaterialId.Silt||material==MaterialId.Peat)brightness*=1-wetness*.1;
            if(material==MaterialId.Granite)
            {
                double mineral=Unit(Hash(cx,cy,cz,Profile.Seed+241));
                // Individual granite crystals are normally much smaller than10cm.
                // Voxel pigment represents an aggregate, not isolated20cm black/white cubes.
                brightness*=mineral>.93?.88:mineral<.08?1.10:1;
            }
            double r=(basis>>16)&255,g=(basis>>8)&255,b=basis&255;
            brightness=Math.Round(brightness*24)/24;
            return VoxelColor.Pack(Channel(r*brightness),Channel(g*brightness),Channel(b*brightness));
        }
        static byte Channel(double v)=>(byte)Clamp(Math.Round(v),0,255);
        static double TopCellCenter(double h)=>(Math.Floor(h/WorldCell.SizeMeters-.5+1e-8)+.5)*WorldCell.SizeMeters;
        static void Coordinate(double x,double z){Numeric.Finite(x,nameof(x));Numeric.Finite(z,nameof(z));if(Math.Abs(x)>1e12||Math.Abs(z)>1e12)throw new ArgumentOutOfRangeException("Surface coordinates limited to +/-1e12m.");}
        static double Clamp(double v,double a,double b)=>Math.Max(a,Math.Min(b,v));
        static double Unit(ulong n)=>(n>>11)*(1.0/9007199254740992.0);
        static ulong Hash(long x,long y,long z,int seed)
        {unchecked{ulong n=(ulong)x*0x9e3779b185ebca87UL^(ulong)y*0x165667b19e3779f9UL^(ulong)z*0xc2b2ae3d27d4eb4fUL^(uint)seed;n^=n>>30;n*=0xbf58476d1ce4e5b9UL;n^=n>>27;n*=0x94d049bb133111ebUL;n^=n>>31;return n;}}
        static double Noise01(double x,double z,int seed)
        {
            long ix=(long)Math.Floor(x),iz=(long)Math.Floor(z);double u=x-ix,v=z-iz;u=u*u*(3-2*u);v=v*v*(3-2*v);
            double a=Unit(Hash(ix,0,iz,seed)),b=Unit(Hash(ix+1,0,iz,seed)),c=Unit(Hash(ix,0,iz+1,seed)),d=Unit(Hash(ix+1,0,iz+1,seed));
            return (a+(b-a)*u)*(1-v)+(c+(d-c)*u)*v;
        }
    }
}
