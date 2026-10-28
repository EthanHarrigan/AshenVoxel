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
        /// <summary>Landform context used for appearance (all 0 for synthetic/legacy columns):
        /// Ridge/Crease = metre-scale convexity/concavity 0..1, SunFacing = cosine to a 40-degree-high
        /// sun -1..1, Talus = foot of a steeper face 0..1, Flow = gully/channel concentration 0..1.</summary>
        public readonly float Ridge,Crease,SunFacing,Talus,Flow;
        internal SurfaceColumn WithTint(uint tint)=>new SurfaceColumn(GroundHeightMeters,SnowDepthMeters,IceDepthMeters,GroundCoverDepthMeters,OrganicDepthMeters,SoilDepthMeters,SedimentDepthMeters,
            SlopeDegrees,Curvature,Exposure,Wetness,TemperatureC,PrecipitationMm,WaterLevelMeters,SurfaceMaterial,BaseSurfaceMaterial,BedrockMaterial,SoilMaterial,SedimentMaterial,
            tint,SourceBiome,EffectiveBiome,BiomeUsed,HasWater,IsLand,ClimateFromField,Ridge,Crease,SunFacing,Talus,Flow);
        internal SurfaceColumn(double ground,double snow,double ice,double cover,double organic,double soil,double sediment,double slope,double curvature,double exposure,double wetness,
            double temperature,double precipitation,double sea,MaterialId top,MaterialId baseSurface,MaterialId rock,MaterialId soilMaterial,MaterialId sedimentMaterial,
            uint tint,byte sourceBiome,byte effectiveBiome,byte biomeUsed,bool water,bool land,bool climateFromField,float ridge,float crease,float sun,float talus,float flow)
        {GroundHeightMeters=ground;SnowDepthMeters=snow;SurfaceHeightMeters=ground+snow;IceDepthMeters=ice;GroundCoverDepthMeters=cover;OrganicDepthMeters=organic;SoilDepthMeters=soil;SedimentDepthMeters=sediment;
            SlopeDegrees=slope;Curvature=curvature;Exposure=exposure;Wetness=wetness;TemperatureC=temperature;PrecipitationMm=precipitation;WaterLevelMeters=sea;
            SurfaceMaterial=top;BaseSurfaceMaterial=baseSurface;BedrockMaterial=rock;OrganicMaterial=MaterialId.Litter;SoilMaterial=soilMaterial;SedimentMaterial=sedimentMaterial;SurfaceTint=tint;
            SourceBiome=sourceBiome;EffectiveBiome=effectiveBiome;BiomeUsed=biomeUsed;HasWater=water;IsLand=land;ClimateFromField=climateFromField;
            Ridge=ridge;Crease=crease;SunFacing=sun;Talus=talus;Flow=flow;}
        internal SurfaceColumn(double ground,double snow,double ice,double cover,double organic,double soil,double sediment,double slope,double curvature,double exposure,double wetness,
            double temperature,double precipitation,double sea,MaterialId top,MaterialId baseSurface,MaterialId rock,MaterialId soilMaterial,MaterialId sedimentMaterial,
            uint tint,byte sourceBiome,byte effectiveBiome,byte biomeUsed,bool water,bool land,bool climateFromField)
            :this(ground,snow,ice,cover,organic,soil,sediment,slope,curvature,exposure,wetness,temperature,precipitation,sea,top,baseSurface,rock,soilMaterial,sedimentMaterial,tint,sourceBiome,effectiveBiome,biomeUsed,water,land,climateFromField,0,0,0,0,0){}
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
        public SurfaceColumn EvaluateColumn(double x,double z)=>Evaluate(x,z,0);
        /// <summary>Column for a render vertex that stands for a <paramref name="footprintMeters"/>-wide patch.
        /// Appearance is prefiltered: detail finer than the footprint fades to its average, and wide
        /// footprints average three spread evaluations, so distant slopes show their true mix of rock,
        /// scree, grass and snow instead of point-sampled speckle. Height and materials stay exact.</summary>
        public SurfaceColumn EvaluateColumn(double x,double z,double footprintMeters)
        {
            if(double.IsNaN(footprintMeters)||footprintMeters<=0)return Evaluate(x,z,0);
            double f=Math.Min(footprintMeters,512);var c=Evaluate(x,z,f);
            if(f<8)return c;
            // Supersample the patch (rotated grid): rock, scree, turf and snow mix by area, not by luck.
            double q=f*.42;
            // Very wide footprints (far rings, fewer pixels each) use three taps instead of five.
            bool five=f<32;
            var a=Evaluate(x+q,z+q*.5,f);var d=Evaluate(x-q,z-q*.5,f);
            var b=five?Evaluate(x-q*.5,z+q,f):c;var e=five?Evaluate(x+q*.5,z-q,f):c;
            uint Mix(int shift)=>(uint)Math.Round(((c.SurfaceTint>>shift&255)+(a.SurfaceTint>>shift&255)+(b.SurfaceTint>>shift&255)+(d.SurfaceTint>>shift&255)+(e.SurfaceTint>>shift&255))*.2);
            return c.WithTint(0xff000000u|Mix(16)<<16|Mix(8)<<8|Mix(0));
        }
        SurfaceColumn Evaluate(double x,double z,double footprint)
        {
            // Low-pass a threshold noise for wide footprints: a distant vertex takes the patch's majority
            // class instead of an arbitrary point decision (no salt-and-pepper material at distance).
            double L(double n,double scale)=>footprint<=0?n:.5+(n-.5)*Clamp(1-footprint/(scale*1.5),0,1);
            Coordinate(x,z);SurfaceSample center=BaseField.Sample(x,z);double h=center.HeightMeters;
            SurfaceSample east=BaseField.Sample(x+2,z),west=BaseField.Sample(x-2,z),north=BaseField.Sample(x,z+2),south=BaseField.Sample(x,z-2);
            double dx=(east.HeightMeters-west.HeightMeters)/4,dz=(north.HeightMeters-south.HeightMeters)/4;
            double fine=(east.HeightMeters+west.HeightMeters+north.HeightMeters+south.HeightMeters)*.25-h;
            double broad=(BaseField.Sample(x+24,z).HeightMeters+BaseField.Sample(x-24,z).HeightMeters+BaseField.Sample(x,z+24).HeightMeters+BaseField.Sample(x,z-24).HeightMeters)*.25;
            double curvatureScale=6,fineScale=.9,footprintBlend=0;
            if(footprint>=4)
            {
                // A distant vertex stands for a wide patch: classify and shade it from the terrain as seen
                // at that scale (slope, aspect, convexity), not from metre-scale bumps that alias into speckle.
                double f=footprint;footprintBlend=Clamp((f-4)/20,0,1);
                double eL=BaseField.Sample(x+f,z).HeightMeters,wL=BaseField.Sample(x-f,z).HeightMeters,nL=BaseField.Sample(x,z+f).HeightMeters,sL=BaseField.Sample(x,z-f).HeightMeters;
                double t=footprintBlend;
                dx=dx*(1-t)+(eL-wL)/(2*f)*t;dz=dz*(1-t)+(nL-sL)/(2*f)*t;
                fine=fine*(1-t)+((eL+wL+nL+sL)*.25-h)*t;fineScale=fineScale*(1-t)+(.12*f+.5)*t;
                if(f>24){broad=(eL+wL+nL+sL)*.25;curvatureScale=6*f/24;}
            }
            double up=1/Math.Sqrt(1+dx*dx+dz*dz),slope=Math.Acos(up)*180/Math.PI;
            double curvature=Clamp((broad-h)/curvatureScale,-1,1); // positive = sheltered hollow; not flow accumulation
            double upstream=BaseField.Sample(x-windX*32,z-windZ*32).HeightMeters,downstream=BaseField.Sample(x+windX*32,z+windZ*32).HeightMeters;
            double shelter=Clamp((upstream-h)/12,-1,1),windFacing=Clamp((dx*windX+dz*windZ)*up,-1,1);
            double windCrest=Clamp((h-(upstream+downstream)*.5)/12,-1,1);
            double exposure=Clamp(.45-curvature*.3+windFacing*.2-shelter*.22+windCrest*.12,0,1);
            // ---- Landform masks (as in Gaia/World Creator style terrain shaders) ----
            // Metre-scale convexity: crests and edges vs creases and cracks.
            double ridge=Clamp(-fine/fineScale,0,1),crease=Clamp(fine/fineScale,0,1);
            // Aspect against a 40-degree-high sun: sunlit faces bleach and dry, shaded faces stay damp.
            double sunFacing=Clamp((-dx*solarX*.766-dz*solarZ*.766+.643)*up,-1,1);
            double talus=0,flow=0;
            if(slope>6&&slope<60)
            {
                double g=Math.Sqrt(dx*dx+dz*dz),ux=dx/g,uz=dz/g;
                // Talus: debris collects where the ground just uphill is much steeper (cliff feet).
                double k=Math.Max(1,footprint/7);
                double h7=BaseField.Sample(x+ux*7*k,z+uz*7*k).HeightMeters,h16=BaseField.Sample(x+ux*16*k,z+uz*16*k).HeightMeters;
                double above=Math.Atan(Math.Max(0,(h16-h7)/(9*k)))*180/Math.PI;
                talus=Clamp((above-slope-10)/18,0,1)*Clamp((44-slope)/14,0,1);
                // Flow: concave across the contour = a gully that gathers runoff and debris.
                double left=BaseField.Sample(x-uz*10*k,z+ux*10*k).HeightMeters,right=BaseField.Sample(x+uz*10*k,z-ux*10*k).HeightMeters;
                flow=Clamp(((left+right)*.5-h-.4*k)/(2.6*k),0,1)*Clamp((slope-10)/12,0,1);
            }
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
            double wetness=Clamp(rainfall/(rainfall+850)+curvature*.18+(biome==12?.16:0)+(neighboringWater?nearSea*.25:0)-(slope/90)*.2+flow*.2+crease*.05-Math.Max(0,sunFacing-.6)*.12,0,1);
            MaterialId rock=footprint>=8?RockAtBroad(x,TopCellCenter(h),z):RockAt(x,TopCellCenter(h),z),surface,soilMat=MaterialId.Dirt,sedimentMat=MaterialId.Clay;
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
            // Autoshader-style thresholds: broken by coherent noise and landform, never one global cutoff.
            // Crests and sun-baked faces lose soil earlier; creases and shaded faces hold it longer.
            double rockCut=Clamp(51+(L(Noise01(x/38,z/38,Profile.Seed+401),38)-.5)*12+Math.Min(0,curvature)*10-ridge*7+crease*5-sunFacing*2.5,38,60);
            double screeCut=Clamp(29+(L(Noise01(x/27,z/27,Profile.Seed+409),27)-.5)*10+(curvature>0?curvature*6:0)-talus*12,16,40);
            bool outcrop=curvature< -.12&&slope>screeCut&&L(Noise01(x/11,z/11,Profile.Seed+419),11)>.58;
            bool meadow=!arid&&wetness>.5&&temperature>-1&&slope<screeCut+9&&L(Noise01(x/19,z/19,Profile.Seed+421),19)>.42;
            if(!water&&(slope>rockCut||retention<.035||outcrop)){surface=rock;cover=soil=sediment=0;}
            else if(!water&&talus>.4&&slope>14&&!(neighboringWater&&nearSea>0))
            {
                // Talus apron below a cliff: angular debris from the face above.
                surface=MaterialId.Gravel;cover=.15+talus*.35;soil*=.2;sediment=.5*retention;sedimentMat=rock;
            }
            else if(!water&&flow>.55&&slope>24&&(arid||temperature<4||slope>34))
            {
                // Erosion chute: runoff strips the gully floor to gravel.
                surface=MaterialId.Gravel;cover=.12;soil*=.3;sediment=.3;sedimentMat=rock;
            }
            else if(neighboringWater&&nearSea>0&&slope<13)
            {surface=wetness>.8&&slope<3?MaterialId.Mud:rock==MaterialId.Stone?MaterialId.BlackSand:MaterialId.Sand;cover=.3+nearSea*.7;soil=0;sediment=1.2;sedimentMat=MaterialId.Gravel;}
            else if(slope>screeCut&&!(meadow&&slope<screeCut+9))
            {
                // Scree collects in hollows and at slope feet; convex shoulders keep thinner debris.
                surface=MaterialId.Gravel;cover=.1+retention*.25+Math.Max(0,curvature)*.5;soil*=.25;sediment=.4*retention;sedimentMat=rock;
            }
            else if(arid)
            {surface=biome==13||slope<13?MaterialId.Sand:MaterialId.Gravel;cover=surface==MaterialId.Sand?.45+1.2*Noise01(x/90,z/90,Profile.Seed+139):.2;soil=.05;sediment=.8;sedimentMat=rock==MaterialId.Shale?MaterialId.Clay:MaterialId.Gravel;}
            else if(wetness>.75&&slope<9&&(biome==12||curvature>.22))
            {surface=organic?MaterialId.Peat:wetness>.87?MaterialId.Mud:MaterialId.Clay;cover=surface==MaterialId.Peat?.3+wetness*.5:.2;soil=.6;soilMat=MaterialId.Mud;sediment=1.2;sedimentMat=MaterialId.Clay;}
            else if(curvature>.45&&wetness>.53&&slope<12)
            {surface=MaterialId.Silt;cover=.2;soilMat=MaterialId.Mud;sediment=.65;sedimentMat=MaterialId.Clay;}
            else
            {surface=MaterialId.Grass;cover=.1;soilMat=wetness>.65?MaterialId.Mud:MaterialId.Dirt;sediment=organic?.18:.08;sedimentMat=MaterialId.Clay;}
            double alpine=Clamp((-2-climate.TemperatureC)/4+.5,0,1)+Clamp((h-(Profile.SnowlineMeters-400))/300+.5,0,1);
            if(!water&&surface==MaterialId.Grass&&alpine>.6+.5*(L(Noise01(x/23,z/23,Profile.Seed+481),23)-.5)-ridge*.25-Math.Max(0,sunFacing)*.1)
            {surface=retention>.5?MaterialId.Gravel:rock;cover=surface==rock?0:.1;soil*=.3;sediment=.05;sedimentMat=rock;}
            else if(!water&&surface==MaterialId.Grass&&organic&&L(Noise01(x/8,z/8,Profile.Seed+151),8)>.67)
            {surface=MaterialId.Litter;cover=.1+.15*wetness;}
            if(!water&&temperature< -6&&biome==11&&slope<24&&curvature>.15&&wetness>.4)
            {surface=MaterialId.Ice;cover=.2;soil=0;sediment=0;}
            double snow=Snow(x,z,temperature,rainfall,slope,curvature,exposure,dx,dz,water);
            double ice=!water&&biome==11&&temperature< -5&&slope<24&&snow>.7&&curvature>.1?Math.Min(1.4,snow*.65):0;
            double organicDepth=!water&&organic&&(surface==MaterialId.Grass||surface==MaterialId.Litter)? .10+wetness*.18:0;
            MaterialId top=snow>=.099?MaterialId.Snow:surface;
            var land=new Landform(ridge,crease,sunFacing,talus,flow,footprint,temperature);
            uint tint=Tint(top,x,TopCellCenter(h+snow),z,biome,wetness,rock,slope,curvature,land);
            if(footprint>=12&&!water)
            {
                // Distant vertex: blend snow and bare ground by the snow-covered fraction of the footprint.
                double coverage=SnowCoverage(x,z,temperature,rainfall,slope,curvature,exposure,dx,dz,water);
                if(coverage>0&&coverage<1||(coverage==0)!=(top!=MaterialId.Snow))
                {
                    uint snowTint=top==MaterialId.Snow?tint:Tint(MaterialId.Snow,x,TopCellCenter(h+.5),z,biome,wetness,rock,slope,curvature,land);
                    uint groundTint=top!=MaterialId.Snow?tint:Tint(surface,x,TopCellCenter(h),z,biome,wetness,rock,slope,curvature,land);
                    tint=VoxelColor.Pack(Channel(((groundTint>>16)&255)*(1-coverage)+((snowTint>>16)&255)*coverage),
                        Channel(((groundTint>>8)&255)*(1-coverage)+((snowTint>>8)&255)*coverage),Channel((groundTint&255)*(1-coverage)+(snowTint&255)*coverage));
                }
            }
            return new SurfaceColumn(h,snow,ice,cover,organicDepth,soil,sediment,slope,curvature,exposure,wetness,temperature,rainfall,Profile.SeaLevelMeters,top,surface,rock,soilMat,sedimentMat,tint,
                center.SourceBiome,center.EffectiveBiome,biome,water,center.IsLand,fromField,(float)ridge,(float)crease,(float)sunFacing,(float)talus,(float)flow);
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
            return new MatterSample(material,Tint(material,position.X,position.Y,position.Z,column.BiomeUsed,column.Wetness,column.BedrockMaterial,column.SlopeDegrees,column.Curvature,
                new Landform(column.Ridge,column.Crease,column.SunFacing,column.Talus,column.Flow,0,column.TemperatureC)));
        }
        public MatterSample SampleMatter(WorldCell cell,SurfaceColumn column)=>SampleMatter(cell.Center,column);
        double Snow(double x,double z,double temp,double precipitation,double slope,double curvature,double exposure,double dx,double dz,bool water)
            =>SnowWith(Noise01(x/14,z/14,Profile.Seed+911)*.65+Noise01(x/47,z/47,Profile.Seed+919)*.35,temp,precipitation,slope,curvature,exposure,dx,dz,water);
        /// <summary>Fraction of a wide footprint under snow: the 14 m patch noise is integrated over
        /// four quantiles instead of point-sampled, so distant snowfields read as soft coverage.</summary>
        double SnowCoverage(double x,double z,double temp,double precipitation,double slope,double curvature,double exposure,double dx,double dz,bool water)
        {
            double broad=Noise01(x/47,z/47,Profile.Seed+919)*.35;int covered=0;
            foreach(double q in new[]{.3,.43,.57,.7})if(SnowWith(broad+.65*q,temp,precipitation,slope,curvature,exposure,dx,dz,water)>=.099)covered++;
            return covered*.25;
        }
        double SnowWith(double patch,double temp,double precipitation,double slope,double curvature,double exposure,double dx,double dz,bool water)
        {
            if(!Profile.EnableSnow||Profile.MaxSnowDepthMeters<.1||water||temp>2.5||slope>=78)return 0;
            double cold=Clamp((1.5-temp)/9,0,1),supply=Clamp((precipitation-60)/1100,0,1);
            double slopeRetention=Math.Pow(Clamp((55-slope)/35,0,1),1.4);
            double solarExposure=Clamp((-dx*solarX-dz*solarZ)/Math.Sqrt(1+dx*dx+dz*dz),-1,1);
            double drift=Clamp(.57+curvature*.3-exposure*Profile.WindStrength*.46-solarExposure*.11,.04,1);
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
            // Optional beta control from the isolated sedimentary-band comparison.
            // Existing material IDs/province map stay unchanged; this is a broad
            // appearance heuristic, not reconstructed or calibrated geology.
            if(Profile.ExperimentalBroadLithology)
            {
                double lithology=Noise01((x+y*.15)/900,(z-y*.08)/900,Profile.Seed+41);
                return lithology<.42?MaterialId.Sandstone:lithology<.70?MaterialId.Limestone:MaterialId.Shale;
            }
            // High cores of large ranges expose massive crystalline basement (granite/gneiss),
            // interfingering with the sedimentary cover through a broad, irregular contact.
            if(y>BasementContact(x,z))return Noise01(x/2600,z/2600,Profile.Seed+461)<.38?MaterialId.Stone:MaterialId.Granite;
            return Bed(x,y,z).Rock;
        }
        /// <summary>Rock type as seen from afar: a formation's dominant lithology instead of its
        /// metre-scale interbeds, so distant flanks are not speckled by alternating beds.</summary>
        MaterialId RockAtBroad(double x,double y,double z)
        {
            var rock=RockAt(x,y,z);
            if(rock==MaterialId.Stone||rock==MaterialId.Granite||Profile.ForcedBedrockMaterial.HasValue||Profile.ExperimentalBroadLithology)return rock;
            return Dominant(Bed(x,y,z).Kind);
        }
        double BasementContact(double x,double z)=>2300+(Noise01(x/4200,z/4200,Profile.Seed+449)-.5)*1600+(Noise01(x/700,z/700,Profile.Seed+457)-.5)*260;

        // ---- Geology v2 ------------------------------------------------------------------
        // Sedimentary provinces are a non-periodic stratigraphic column: formations 18-100 m
        // thick, each with its own lithology mix and colour family, holding beds 1.5-8 m thick
        // that pinch and swell laterally. The column is tilted by a slowly varying regional
        // dip, gently folded and broken into fault blocks with individual throws. Nothing
        // repeats on a fixed period, so mountains no longer wear regular horizontal stripes.
        readonly struct Stratum
        {
            public readonly MaterialId Rock;public readonly ulong Formation,BedKey;public readonly bool Massive;public readonly int Kind;
            public Stratum(MaterialId rock,ulong formation,ulong bed,bool massive,int kind){Rock=rock;Formation=formation;BedKey=bed;Massive=massive;Kind=kind;}
        }
        /// <summary>Stratigraphic height: elevation corrected for regional dip, folds and fault throw.</summary>
        double Stratigraphic(double x,double y,double z)
        {
            double regional=(Noise01(x/3400,z/3400,Profile.Seed+301)-.5)*280+(Noise01(x/9100,z/9100,Profile.Seed+307)-.5)*700;
            double fold=(Noise01(x/650,z/650,Profile.Seed+311)-.5)*36+(Noise01(x/170,z/170,Profile.Seed+313)-.5)*5;
            double wx=x+(Noise01(x/500,z/500,Profile.Seed+317)-.5)*260,wz=z+(Noise01(x/500,z/500,Profile.Seed+331)-.5)*260;
            long bx=(long)Math.Floor(wx/1200),bz=(long)Math.Floor(wz/1200);
            double faultThrow=(Unit(Hash(bx,7,bz,Profile.Seed+337))-.5)*60;
            return y+regional+fold+faultThrow;
        }
        /// <summary>Non-periodic partition of a coordinate: unit cells with one hashed boundary each.</summary>
        static long Partition(double s,double unit,int seed,long lane)
        {
            long i=(long)Math.Floor(s/unit);double boundary=(.15+.7*Unit(Hash(i,lane,0,seed)))*unit;
            return s-i*unit<boundary?i*2-1:i*2;
        }
        Stratum Bed(double x,double y,double z)
        {
            double s=Stratigraphic(x,y,z);
            long formation=Partition(s,60,Profile.Seed+347,11);
            ulong fh=Hash(formation,13,0,Profile.Seed+353);
            // Most formations share their ~500 m group's lithology mix: thick coherent sequences
            // rather than every 60 m unit flipping between limestone and sandstone.
            long group=Partition(s,520,Profile.Seed+463,29);ulong gh=Hash(group,31,0,Profile.Seed+467);
            int kind=Unit(fh>>44)<.7?(int)(gh%4):(int)(fh%4);bool massive=(fh>>8)%5<2;
            // Beds pinch and swell; the lateral warp is small relative to bed thickness.
            double sb=s+(Noise01(x/60,z/60,Profile.Seed+359)-.5)*2.4+(Noise01(x/14,z/14,Profile.Seed+367)-.5)*.6;
            double bedUnit=3+6*Unit(fh>>16);
            long bed=massive?0:Partition(sb,bedUnit,Profile.Seed+373,formation);
            ulong bh=Hash(formation,bed,17,Profile.Seed+379);double r=Unit(bh);
            MaterialId rock;
            switch(kind)
            {
                case 0:rock=massive||r<.78?MaterialId.Sandstone:MaterialId.Shale;break;                       // sandstone dominant
                case 1:rock=massive||r<.70?MaterialId.Limestone:r<.90?MaterialId.Shale:MaterialId.Sandstone;break; // limestone with partings
                case 2:rock=massive||r<.66?MaterialId.Shale:MaterialId.Sandstone;break;                        // mudrock dominant
                default:rock=r<.45?MaterialId.Sandstone:r<.70?MaterialId.Limestone:MaterialId.Shale;break;    // interbedded
            }
            return new Stratum(rock,fh,bh,massive,kind);
        }
        static readonly uint[] SandstoneFamily={0xffb09670,0xffbeac8a,0xffb28060,0xffa0684a,0xff8c7e6c,0xffa89070};
        static readonly uint[] LimestoneFamily={0xffa8a69c,0xffb2aa94,0xff8c9296,0xff9e9688,0xffb4b0a4};
        static readonly uint[] ShaleFamily={0xff6a6c6e,0xff666e66,0xff6e6468,0xff746656,0xff7a7268};
        static readonly uint[] BasaltFamily={0xff6f757c,0xff665f5a,0xff5a5e64,0xff76706a};
        static readonly uint[] GraniteFamily={0xff8d8884,0xffa08a80,0xffa5a29c,0xff96908a};
        static uint Pick(uint[] family,ulong h)=>family[(int)(h%(ulong)family.Length)];
        /// <summary>Formation/bed pigment for a rock cell, before surface weathering.</summary>
        static MaterialId Dominant(int kind)=>kind==1?MaterialId.Limestone:kind==2?MaterialId.Shale:MaterialId.Sandstone;
        uint FamilyColor(MaterialId rock,ulong formation)=>rock==MaterialId.Sandstone?Pick(SandstoneFamily,formation>>24):rock==MaterialId.Limestone?Pick(LimestoneFamily,formation>>28):
            rock==MaterialId.Shale?Pick(ShaleFamily,formation>>32):VoxelColor.Base(rock);
        /// <summary>Formation palettes mostly inherit their ~500 m stratigraphic group's palette, giving
        /// broad coherent colour zones from afar instead of dense multicoloured banding.</summary>
        ulong PaletteKey(Stratum st,double s)
        {
            long group=Partition(s,520,Profile.Seed+463,29);ulong gh=Hash(group,31,0,Profile.Seed+467);
            return Unit(st.Formation>>40)<.72?gh:st.Formation;
        }
        uint RockPigment(MaterialId rock,double x,double y,double z,out double jointShade)=>RockPigment(rock,x,y,z,out jointShade,out _);
        uint RockPigment(MaterialId rock,double x,double y,double z,out double jointShade,out uint weathered)
        {
            jointShade=1;weathered=0;
            if(Profile.ForcedBedrockMaterial.HasValue||Profile.ExperimentalBroadLithology)return VoxelColor.Base(rock);
            if(rock==MaterialId.Stone||rock==MaterialId.Granite)
            {
                // Crystalline provinces: subtype per ~3 km region, two warped vertical joint sets.
                long rx=(long)Math.Floor(x/3000),rz=(long)Math.Floor(z/3000);ulong rh=Hash(rx,23,rz,Profile.Seed+383);
                // Subtype tone blends smoothly between ~3 km regions (warped bilinear lattice): no hard region edges.
                double wx=x/3000+(Noise01(x/1700,z/1700,Profile.Seed+403)-.5)*.8-.5,wz=z/3000+(Noise01(x/1700,z/1700,Profile.Seed+409)-.5)*.8-.5;
                long gx=(long)Math.Floor(wx),gz=(long)Math.Floor(wz);double tx=wx-gx,tz=wz-gz;tx=tx*tx*(3-2*tx);tz=tz*tz*(3-2*tz);
                // Basalt/granite contacts get a weathered transition band instead of a paint-like edge.
                double contact=y>BasementContact(x,z)?Smooth(.34,.42,Noise01(x/2600,z/2600,Profile.Seed+461)):Smooth(.26,.32,Noise01(x/1900,z/1900,Profile.Seed+11)*.68+Noise01(x/7400,z/7400,Profile.Seed+17)*.32);
                double br=0,bg=0,bb=0;
                for(int k=0;k<4;k++)
                {
                    long ax=gx+(k&1),az=gz+(k>>1);ulong ch=Hash(ax,23,az,Profile.Seed+383);double w=((k&1)==1?tx:1-tx)*((k>>1)==1?tz:1-tz);
                    uint dark=Pick(BasaltFamily,ch),light=Pick(GraniteFamily,ch);
                    br+=(((dark>>16)&255)*(1-contact)+((light>>16)&255)*contact)*w;bg+=(((dark>>8)&255)*(1-contact)+((light>>8)&255)*contact)*w;bb+=((dark&255)*(1-contact)+(light&255)*contact)*w;
                }
                uint basis=VoxelColor.Pack(Channel(br),Channel(bg),Channel(bb));
                double a1=Unit(rh>>20)*Math.PI,a2=a1+Math.PI*(.35+.3*Unit(rh>>40));
                double spacing=2.6+3.2*Unit(rh>>12);double warpJ=(Noise01(x/9,z/9,Profile.Seed+389)-.5)*1.4;
                double j1=(x*Math.Cos(a1)+z*Math.Sin(a1)+warpJ)/spacing,j2=(x*Math.Cos(a2)+z*Math.Sin(a2)-warpJ)/(spacing*1.6);
                j1-=Math.Floor(j1);j2-=Math.Floor(j2);
                double edge=Math.Min(Math.Min(j1,1-j1)*spacing,Math.Min(j2,1-j2)*spacing*1.6);
                if(edge<.07)jointShade=.84;else if(edge<.16)jointShade=.94;
                // Horizontal exfoliation sheets in granite, a few metres apart.
                if(rock==MaterialId.Granite){double sheet=(y+(Noise01(x/30,z/30,Profile.Seed+397)-.5)*6)/4.3;sheet-=Math.Floor(sheet);if(sheet<.03)jointShade=Math.Min(jointShade,.9);}
                return basis;
            }
            Stratum st=Bed(x,y,z);ulong palette=PaletteKey(st,Stratigraphic(x,y,z));
            uint family=FamilyColor(rock,palette),dominant=FamilyColor(Dominant(st.Kind),palette);
            weathered=dominant;
            double r=(family>>16)&255,g=(family>>8)&255,b=family&255;
            // Minority interbeds take on much of their formation's dominant tone: bedding stays legible
            // without alternating light/dark stripes across whole mountains.
            if(family!=dominant){r+=(((dominant>>16)&255)-r)*.45;g+=(((dominant>>8)&255)-g)*.45;b+=((dominant&255)-b)*.45;}
            double bed=st.Massive?1:.95+.10*Unit(st.BedKey>>9);r*=bed;g*=bed;b*=bed;
            // Occasional, restrained iron staining in sandstone.
            if(rock==MaterialId.Sandstone&&!st.Massive&&Unit(st.BedKey>>30)<.06){r*=1.04;g*=.96;b*=.91;}
            return VoxelColor.Pack(Channel(r),Channel(g),Channel(b));
        }
        /// <summary>Pigment without terrain context (flat, sheltered). Prefer the column-aware overload.</summary>
        public uint Color(MaterialId material,double x,double y,double z,byte biome,double wetness,MaterialId parentRock)=>Tint(material,x,y,z,biome,wetness,parentRock,0,0);
        /// <summary>Landform context for appearance; default = flat, sheltered, point sample.</summary>
        public readonly struct Landform
        {
            public readonly double Ridge,Crease,Sun,Talus,Flow,Footprint,TemperatureC;
            public Landform(double ridge,double crease,double sun,double talus,double flow,double footprint,double temperature)
            {Ridge=ridge;Crease=crease;Sun=sun;Talus=talus;Flow=flow;Footprint=Math.Max(0,footprint);TemperatureC=temperature;}
            public static Landform Neutral=>new Landform(0,0,.64,0,0,0,12);
        }
        /// <summary>Pigment with terrain context: steep exposed rock weathers, streaks and grows lichen.</summary>
        public uint Tint(MaterialId material,double x,double y,double z,byte biome,double wetness,MaterialId parentRock,double slopeDegrees,double curvature)
            =>Tint(material,x,y,z,biome,wetness,parentRock,slopeDegrees,curvature,Landform.Neutral);
        /// <summary>Gaia-style layered appearance. Material base colour, then: strata/joint pigment for rock,
        /// cavity occlusion in creases and hollows, pale weathered crests and edges, sun-bleached dry
        /// aspects versus cool damp shaded aspects with moss and lichen, dark runoff streaks down gullies,
        /// fresh talus, and vegetation that dries on sunny crests, greens in moist hollows and turns to
        /// straw-coloured alpine turf with altitude. Detail finer than the render footprint fades to
        /// its average so distant terrain is not speckled.</summary>
        public uint Tint(MaterialId material,double x,double y,double z,byte biome,double wetness,MaterialId parentRock,double slopeDegrees,double curvature,Landform land)
        {
            Coordinate(x,z);Numeric.Finite(y,nameof(y));if(Math.Abs(y)>1e12||!MaterialCatalog.IsValid(material)||double.IsNaN(wetness)||wetness<0||wetness>1)throw new ArgumentOutOfRangeException("appearance input");
            if(material==MaterialId.Air)return 0;
            double fp=land.Footprint;
            double Fade(double scale)=>fp<=0?1:Clamp(1-fp/scale,0,1);
            uint basis=VoxelColor.Base(material);
            bool arid=biome==1||biome==2||biome==13;
            if(material==MaterialId.Grass)
            {
                switch(biome){case 3:basis=0xff988b43;break;case 4:basis=0xff898443;break;case 7:basis=0xff376e36;break;
                    case 8:basis=0xff365f49;break;case 9:basis=0xff526347;break;case 10:basis=0xff74794e;break;case 12:basis=0xff426d49;break;default:basis=0xff486331;break;}
            }
            bool rockCell=SurfaceProfile.IsRock(material);double joint=1;uint weathered=0;
            if(rockCell)basis=RockPigment(material,x,y,z,out joint,out weathered);
            else if(material==MaterialId.Gravel&&SurfaceProfile.IsRock(parentRock))basis=RockPigment(parentRock,x,y,z,out _);
            joint=.965+(joint-.965)*Fade(6);
            // True 3D mottling replaces the former height-sheared 2D layer noise (visible diagonal stripes).
            double broad=(Noise01(x/9,z/9,Profile.Seed+211)*2-1)*Fade(30),layer=(Noise3(x/1.3,y/1.3,z/1.3,Profile.Seed+223)*2-1)*Fade(4);
            long cx=(long)Math.Floor(x*5),cy=(long)Math.Floor(y*5),cz=(long)Math.Floor(z*5);
            double grain=(Unit(Hash(cx,cy,cz,Profile.Seed+227))*2-1)*Fade(.6);
            double variation=material==MaterialId.Snow?.022:material==MaterialId.Ice?.035:.08;
            double brightness=1+broad*variation+layer*variation*.45+grain*variation*.28;
            if(material==MaterialId.Mud||material==MaterialId.Clay||material==MaterialId.Silt||material==MaterialId.Peat)brightness*=1-wetness*.1;
            if(material==MaterialId.Granite)
            {
                double mineral=Unit(Hash(cx,cy,cz,Profile.Seed+241));
                // Individual granite crystals are normally much smaller than10cm.
                // Voxel pigment represents an aggregate, not isolated20cm black/white cubes.
                brightness*=1+((mineral>.93?-.12:mineral<.08?.10:0))*Fade(.6);
            }
            double r=(basis>>16)&255,g=(basis>>8)&255,b=basis&255;
            void Toward(double tr,double tg,double tb,double amount){if(amount<=0)return;amount=Math.Min(1,amount);r+=(tr-r)*amount;g+=(tg-g)*amount;b+=(tb-b)*amount;}
            double sunLit=Math.Max(0,land.Sun),shade=Clamp(.35-land.Sun,0,1);
            // Cavity occlusion and edge highlights for every surface (ambient light reaches crests, not cracks).
            brightness*=1+.06*land.Ridge-.10*land.Crease-.07*Clamp(curvature,0,1);
            if(rockCell||material==MaterialId.Gravel)
            {
                brightness*=joint;
                // Exposure weathering: steep faces carry patina and vertical runoff streaks; damp or cold
                // rock grows lichen. Hollow-collecting scree stays fresher than convex faces.
                double face=Clamp((slopeDegrees-28)/40,0,1);
                // Weathered faces converge on one formation tone, as exposed cliffs do.
                if(weathered!=0&&face>0){double u=.3*face;r+=(((weathered>>16)&255)*.93-r)*u;g+=(((weathered>>8)&255)*.93-g)*u;b+=((weathered&255)*.93-b)*u;}
                // From afar, metre-scale beds blend into their formation tone.
                if(weathered!=0&&fp>0)Toward((weathered>>16)&255,(weathered>>8)&255,weathered&255,.8*(1-Fade(14)));
                double streak=.5+(Noise3(x/1.6,y/24,z/1.6,Profile.Seed+433)-.5)*Fade(5);
                double patina=face*(.10+.12*Clamp((streak-.45)/.4,0,1))*(1+.4*wetness)-Math.Max(0,curvature)*.04;
                brightness*=1-Math.Max(0,patina);
                // Crests and arêtes weather pale; the debris-filled creases between them stay dark.
                brightness*=1+.08*land.Ridge*(.5+face);
                // Sunlit faces bleach and warm; shaded faces are cooler and darker.
                if(sunLit>0){brightness*=1+.06*sunLit*face;r*=1+.03*sunLit;b*=1-.03*sunLit;}
                brightness*=1-.07*shade*face;b*=1+.03*shade;
                if(arid&&face>0){r*=1+.06*face;b*=1-.08*face;}                       // desert varnish warms faces
                // Runoff: dark wet streaks down gullies and below seeps.
                double run=land.Flow*(.4+.6*Clamp((Noise3(x/1.1,y/30,z/1.1,Profile.Seed+487)-.4)/.35,0,1)*Fade(4)+.6*(1-Fade(4))*.5);
                brightness*=1-.20*run*(.4+.6*face)-.06*wetness*face;
                // Talus is freshly broken: lighter and less weathered.
                if(material==MaterialId.Gravel)brightness*=1+.06*land.Talus;
                double lichenNoise=.5+(Noise3(x/2.7,y/2.7,z/2.7,Profile.Seed+439)-.5)*Fade(8);
                double lichenAmount=Clamp((lichenNoise-.66+.12*shade)/.2,0,1)*Clamp((wetness-.3+.15*shade)/.4,0,1)*(.25+.5*face);
                if(lichenAmount>0)Toward(146,154,124,lichenAmount*.45);
                // Moss on damp shaded rock in mild, wet climates; crustose orange lichen on cold sunny crests.
                double mossy=shade*Clamp((wetness-.5)/.3,0,1)*Clamp((land.TemperatureC+2)/10,0,1)*(1-.5*face)*(arid?0:1);
                if(mossy>0)Toward(84,102,58,mossy*.55*(.6+.4*(.5+(Noise01(x/3.1,z/3.1,Profile.Seed+491)-.5)*Fade(10))));
                double orange=sunLit*land.Ridge*Clamp((6-land.TemperatureC)/10,0,1)*Clamp((Noise3(x/4.3,y/4.3,z/4.3,Profile.Seed+499)-.6)/.25,0,1);
                if(orange>0)Toward(176,128,72,orange*.35*Fade(12)+orange*.12*(1-Fade(12)));
            }
            else if(material==MaterialId.Grass||material==MaterialId.Litter)
            {
                // Vegetation: dries to straw on sunny crests and steep sun faces, deep green in moist
                // hollows and gullies, olive-straw alpine turf near the tree line, patchy meadows.
                double dry=Clamp(.55*sunLit*Clamp((slopeDegrees-5)/25,0,1)+.35*land.Ridge-.6*(wetness-.5)-.5*land.Crease-.4*land.Flow+(arid?.25:0),0,1);
                double lush=Clamp(.6*land.Crease+.7*land.Flow+.6*Math.Max(0,wetness-.6)+.3*shade-.3*land.Ridge,0,1);
                double alpine=Clamp((7-land.TemperatureC)/9,0,1);
                double meadow=(Noise01(x/41,z/41,Profile.Seed+503)-.5)*Fade(120)+(Noise01(x/7,z/7,Profile.Seed+509)-.5)*.5*Fade(20);
                if(material==MaterialId.Grass)
                {
                    Toward(150,138,76,dry*.55+Math.Max(0,meadow)*.18);
                    Toward(118,118,74,alpine*.5);
                    Toward(46,86,38,lush*.45);
                    brightness*=1+meadow*.08;
                }
                else brightness*=1-lush*.08;
            }
            else if(material==MaterialId.Snow)
            {
                // Shaded snow reads cool and blue, sunlit snow warm; windblown crests show crust.
                Toward(214,226,242,shade*.35);brightness*=1+.03*sunLit-.04*land.Crease;
            }
            else if(material==MaterialId.Dirt||material==MaterialId.Sand||material==MaterialId.Clay||material==MaterialId.Silt)
            {
                brightness*=1+.05*sunLit-.06*shade-.10*land.Flow;
                if(material==MaterialId.Sand&&land.Ridge>0)brightness*=1+.05*land.Ridge; // wind-scoured crests
            }
            brightness=Math.Round(brightness*24)/24;
            return VoxelColor.Pack(Channel(r*brightness),Channel(g*brightness),Channel(b*brightness));
        }
        static byte Channel(double v)=>(byte)Clamp(Math.Round(v),0,255);
        static double TopCellCenter(double h)=>(Math.Floor(h/WorldCell.SizeMeters-.5+1e-8)+.5)*WorldCell.SizeMeters;
        static void Coordinate(double x,double z){Numeric.Finite(x,nameof(x));Numeric.Finite(z,nameof(z));if(Math.Abs(x)>1e12||Math.Abs(z)>1e12)throw new ArgumentOutOfRangeException("Surface coordinates limited to +/-1e12m.");}
        static double Clamp(double v,double a,double b)=>Math.Max(a,Math.Min(b,v));
        static double Smooth(double a,double b,double v){double t=Clamp((v-a)/(b-a),0,1);return t*t*(3-2*t);}
        static double Unit(ulong n)=>(n>>11)*(1.0/9007199254740992.0);
        static ulong Hash(long x,long y,long z,int seed)
        {unchecked{ulong n=(ulong)x*0x9e3779b185ebca87UL^(ulong)y*0x165667b19e3779f9UL^(ulong)z*0xc2b2ae3d27d4eb4fUL^(uint)seed;n^=n>>30;n*=0xbf58476d1ce4e5b9UL;n^=n>>27;n*=0x94d049bb133111ebUL;n^=n>>31;return n;}}
        static double Noise3(double x,double y,double z,int seed)
        {
            long ix=(long)Math.Floor(x),iy=(long)Math.Floor(y),iz=(long)Math.Floor(z);double u=x-ix,v=y-iy,w=z-iz;
            u=u*u*(3-2*u);v=v*v*(3-2*v);w=w*w*(3-2*w);
            double C(long a,long b,long c)=>Unit(Hash(a,b,c,seed));
            double x00=C(ix,iy,iz)+(C(ix+1,iy,iz)-C(ix,iy,iz))*u,x10=C(ix,iy+1,iz)+(C(ix+1,iy+1,iz)-C(ix,iy+1,iz))*u;
            double x01=C(ix,iy,iz+1)+(C(ix+1,iy,iz+1)-C(ix,iy,iz+1))*u,x11=C(ix,iy+1,iz+1)+(C(ix+1,iy+1,iz+1)-C(ix,iy+1,iz+1))*u;
            double y0=x00+(x10-x00)*v,y1=x01+(x11-x01)*v;return y0+(y1-y0)*w;
        }
        static double Noise01(double x,double z,int seed)
        {
            long ix=(long)Math.Floor(x),iz=(long)Math.Floor(z);double u=x-ix,v=z-iz;u=u*u*(3-2*u);v=v*v*(3-2*v);
            double a=Unit(Hash(ix,0,iz,seed)),b=Unit(Hash(ix+1,0,iz,seed)),c=Unit(Hash(ix,0,iz+1,seed)),d=Unit(Hash(ix+1,0,iz+1,seed));
            return (a+(b-a)*u)*(1-v)+(c+(d-c)*u)*v;
        }
    }
}
