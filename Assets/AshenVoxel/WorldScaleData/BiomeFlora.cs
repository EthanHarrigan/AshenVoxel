using System;
using AshenVoxel.Core;
namespace AshenVoxel.WorldScale
{
    /// <summary>Foliage-lab prototype IDs appended to the AVPM0004 pack (generated from
    /// Tools/ExplorationPrototypes/lab/lab-manifest.json). Every key has two baked seeds:
    /// the listed ID and ID+1.</summary>
    public static class LabFlora
    {
        public const int First=300,Last=479;
        public static bool IsLab(int id)=>id>=First&&id<=Last;
        public const int AgaveRosette=300;
        public const int AlpineCushion=302;
        public const int BarrelShort=304;
        public const int BarrelTall=306;
        public const int BeachBitterPanicum=308;
        public const int BeachPurslane=310;
        public const int BeachRailroadVine=312;
        public const int BeachSeaOats=314;
        public const int BeachSunflower=316;
        public const int BearberryAlpineAutumn=318;
        public const int BearberryCreepingRed=320;
        public const int BromeliadGroundTank=322;
        public const int BromeliadRedBracts=324;
        public const int ChollaCandelabra=326;
        public const int ChollaLow=328;
        public const int ChollaTree=330;
        public const int CoralDigitateFingers=332;
        public const int CoralElkhornFlattened=334;
        public const int CoralEncrustingRock=336;
        public const int CoralMassiveRidges=338;
        public const int CoralSeafanNet=340;
        public const int CoralStaghornBranching=342;
        public const int CoralTieredPlates=344;
        public const int CreosoteOpenPaired=346;
        public const int FungiBrownLayered=348;
        public const int FungiOchreOverlap=350;
        public const int FungiPaleRounded=352;
        public const int HeatherSparseTips=354;
        public const int HorsetailFieldShort=356;
        public const int HorsetailOpenTall=358;
        public const int HorsetailScouringCones=360;
        public const int JungleBeehiveGinger=362;
        public const int JungleBirdsNestFern=364;
        public const int JungleHeliconiaPendant=366;
        public const int JungleHeliconiaUpright=368;
        public const int JungleYoungBanana=370;
        public const int LichenReindeerMat=372;
        public const int LowBerryTrail=374;
        public const int MangroveBlackPencils=376;
        public const int MangroveRedSapling=378;
        public const int MangroveRedSpreading=380;
        public const int MangroveRedSweptTangle=382;
        public const int MangroveWhiteUpland=384;
        public const int MarineBullKelp=386;
        public const int MarineEelgrass=388;
        public const int MarineGiantKelpYoung=390;
        public const int MarineManateegrass=392;
        public const int MarineSeaLettuce=394;
        public const int MarineTurtlegrass=396;
        public const int PalmCabbageFan=398;
        public const int PalmCoconutLeaning=400;
        public const int PalmCoconutYoung=402;
        public const int PalmDateCompact=404;
        public const int PalmSawPalmetto=406;
        public const int PricklyPearEleven=408;
        public const int PricklyPearSeven=410;
        public const int PricklyPearThree=412;
        public const int ReefIsolatedPatch=414;
        public const int ReefRuggedGarden=416;
        public const int ReefShelfTerraces=418;
        public const int ReefSpurGrooveSlice=420;
        public const int SagebrushGnarledOpen=422;
        public const int SagebrushLowGray=424;
        public const int SaguaroFourArm=426;
        public const int SaguaroJuvenile=428;
        public const int SaguaroTall=430;
        public const int SaguaroTwoArm=432;
        public const int SedgeThreeRank=434;
        public const int SteppeDrySeedBunch=436;
        public const int StoneBasaltTalus=438;
        public const int StoneFracturedSpire=440;
        public const int StoneGraniteJointed=442;
        public const int StoneLimestoneLedges=444;
        public const int StoneSandstoneGrey=446;
        public const int StoneShalePlates=448;
        public const int TinyGrassDense=450;
        public const int TinyGrassDry=452;
        public const int TinyGrassSparse=454;
        public const int WetlandArrowhead=456;
        public const int WetlandCattail=458;
        public const int WetlandLotus=460;
        public const int WetlandPapyrus=462;
        public const int WetlandPickerelweed=464;
        public const int WetlandPinkLilyStages=466;
        public const int WetlandRush=468;
        public const int WetlandWhiteLily=470;
        public const int WillowProstrateCatkins=472;
        public const int WoodlandClover=474;
        public const int YuccaBasalSwords=476;
        public const int YuccaTrunkSkirt=478;
    }
    /// <summary>Plant communities layered over the source biomes. One biome yields several
    /// communities (e.g. hot desert: cactus forest, creosote flats, yucca highland, oasis),
    /// chosen by coherent kilometre-scale fields plus landform (coast, scree, hollows, depth).</summary>
    public enum FloraCommunity
    {
        None,Meadow,Steppe,SagebrushSteppe,Savanna,CactusForest,CreosoteFlats,YuccaHighland,Dunes,Oasis,
        Beach,TropicalBeach,TropicalSeasonal,Jungle,TemperateWoodland,TemperateRainforest,BorealFloor,Tundra,Alpine,Scree,
        Marsh,SeasonalPond,MangroveSwamp,SeagrassWarm,SeagrassCold,KelpForest,CoralReef
    }
    /// <summary>Understory: 1.4 m candidate grid in 16 m plant cells. Midstory: 3 m grid in 64 m
    /// rock cells (shrubs, small trees, boulders, reefs). Canopy: 7 m grid in 64 m tree cells.</summary>
    public enum FloraTier { Understory, Midstory, Canopy }
    public readonly struct FloraSite
    {
        public readonly FloraCommunity Community;
        /// <summary>Local fill multiplier (patchiness): 0 = bare gap, ~1.6 = dense clump.</summary>
        public readonly double Patch;
        /// <summary>Stand age 0 (young, regenerating) .. 1 (old).</summary>
        public readonly double Age;
        public FloraSite(FloraCommunity community,double patch,double age){Community=community;Patch=patch;Age=age;}
    }
    public static class BiomeFlora
    {
        public const int Version=1;
        public const double UnderstorySpacing=1.4,MidstorySpacing=3,CanopySpacing=7;
        readonly struct Entry
        {
            public readonly int Id;public readonly float Weight,AgeBias;
            public Entry(int id,float weight,float ageBias=0){Id=id;Weight=weight;AgeBias=ageBias;}
        }
        static Entry E(int id,float w,float age=0)=>new Entry(id,w,age);
        static readonly int Communities=Enum.GetValues(typeof(FloraCommunity)).Length;
        static readonly Entry[][][] Table=new Entry[3][][];
        static readonly double[][] Fill=new double[3][];
        /// <summary>Largest fill probability per tier, used to reject candidates before any terrain sampling.</summary>
        public static readonly double[] MaximumFill=new double[3];
        static void Set(FloraTier tier,FloraCommunity c,double fill,params Entry[] entries){Table[(int)tier][(int)c]=entries;Fill[(int)tier][(int)c]=fill;}
        static BiomeFlora()
        {
            for(int t=0;t<3;t++){Table[t]=new Entry[Communities][];Fill[t]=new double[Communities];for(int c=0;c<Communities;c++)Table[t][c]=Array.Empty<Entry>();}
            const FloraTier U=FloraTier.Understory,M=FloraTier.Midstory,C=FloraTier.Canopy;
            // ---- Understory (ground layer; most are soft, walk-through tissue) ----
            Set(U,FloraCommunity.Meadow,.46,E(LabFlora.TinyGrassDense,5),E(LabFlora.WoodlandClover,3),E(LabFlora.SteppeDrySeedBunch,1),E(LabFlora.HeatherSparseTips,.5f));
            Set(U,FloraCommunity.Steppe,.34,E(LabFlora.TinyGrassDry,5),E(LabFlora.SteppeDrySeedBunch,4),E(LabFlora.TinyGrassSparse,2),E(LabFlora.SagebrushLowGray,1));
            Set(U,FloraCommunity.SagebrushSteppe,.40,E(LabFlora.SagebrushLowGray,4,-.5f),E(LabFlora.TinyGrassDry,3),E(LabFlora.SteppeDrySeedBunch,2),E(LabFlora.TinyGrassSparse,2));
            Set(U,FloraCommunity.Savanna,.48,E(LabFlora.TinyGrassDry,6),E(LabFlora.SteppeDrySeedBunch,3),E(LabFlora.TinyGrassSparse,1),E(LabFlora.PricklyPearThree,.3f));
            Set(U,FloraCommunity.CactusForest,.17,E(LabFlora.TinyGrassSparse,2),E(LabFlora.BarrelShort,2,-.6f),E(LabFlora.PricklyPearThree,2,-.6f),E(LabFlora.AgaveRosette,1.5f),E(LabFlora.SaguaroJuvenile,1.5f,-.8f),E(LabFlora.ChollaLow,1.5f));
            Set(U,FloraCommunity.CreosoteFlats,.08,E(LabFlora.TinyGrassSparse,3),E(LabFlora.TinyGrassDry,2),E(LabFlora.BarrelShort,.7f),E(LabFlora.ChollaLow,.6f));
            Set(U,FloraCommunity.YuccaHighland,.18,E(LabFlora.AgaveRosette,3),E(LabFlora.YuccaBasalSwords,3),E(LabFlora.TinyGrassDry,3),E(LabFlora.SteppeDrySeedBunch,2),E(LabFlora.PricklyPearThree,1));
            Set(U,FloraCommunity.Dunes,.03,E(LabFlora.TinyGrassSparse,3),E(LabFlora.TinyGrassDry,1),E(LabFlora.SteppeDrySeedBunch,1));
            Set(U,FloraCommunity.Oasis,.55,E(LabFlora.SedgeThreeRank,4),E(LabFlora.WetlandRush,3),E(LabFlora.TinyGrassDense,3),E(LabFlora.HorsetailFieldShort,1));
            Set(U,FloraCommunity.Beach,.14,E(LabFlora.BeachPurslane,2),E(LabFlora.BeachRailroadVine,1.5f),E(LabFlora.BeachSunflower,1.5f),E(LabFlora.BeachBitterPanicum,2),E(LabFlora.TinyGrassSparse,1));
            Set(U,FloraCommunity.TropicalBeach,.18,E(LabFlora.BeachRailroadVine,3),E(LabFlora.BeachPurslane,2),E(LabFlora.BeachBitterPanicum,2),E(LabFlora.BeachSunflower,1));
            Set(U,FloraCommunity.TropicalSeasonal,.36,E(LabFlora.TinyGrassDense,3),E(LabFlora.BromeliadGroundTank,1),E(LabFlora.JungleBirdsNestFern,1.5f),E(LabFlora.FungiOchreOverlap,.4f,.8f),E(LabFlora.WoodlandClover,1));
            Set(U,FloraCommunity.Jungle,.52,E(LabFlora.JungleBirdsNestFern,4),E(LabFlora.BromeliadGroundTank,2),E(LabFlora.BromeliadRedBracts,1.5f),E(LabFlora.FungiBrownLayered,.8f,.8f),E(LabFlora.FungiOchreOverlap,.6f,.8f),E(LabFlora.FungiPaleRounded,.4f,.8f),E(LabFlora.HorsetailOpenTall,.5f));
            Set(U,FloraCommunity.TemperateWoodland,.32,E(LabFlora.WoodlandClover,4),E(LabFlora.LowBerryTrail,2,.3f),E(LabFlora.FungiBrownLayered,.7f,.8f),E(LabFlora.FungiPaleRounded,.5f,.8f),E(LabFlora.HorsetailFieldShort,.8f),E(LabFlora.TinyGrassDense,2,-.6f));
            Set(U,FloraCommunity.TemperateRainforest,.44,E(LabFlora.HorsetailOpenTall,2),E(LabFlora.HorsetailScouringCones,1.5f),E(LabFlora.FungiBrownLayered,1.2f,.6f),E(LabFlora.FungiOchreOverlap,1.2f,.6f),E(LabFlora.FungiPaleRounded,.8f,.6f),E(LabFlora.LowBerryTrail,1.5f),E(LabFlora.WoodlandClover,1));
            Set(U,FloraCommunity.BorealFloor,.40,E(LabFlora.LichenReindeerMat,3,.5f),E(LabFlora.BearberryCreepingRed,2.5f),E(LabFlora.LowBerryTrail,2),E(LabFlora.HeatherSparseTips,1.5f),E(LabFlora.FungiPaleRounded,.6f,.7f),E(LabFlora.HorsetailFieldShort,.6f));
            Set(U,FloraCommunity.Tundra,.38,E(LabFlora.LichenReindeerMat,3),E(LabFlora.AlpineCushion,2.5f),E(LabFlora.BearberryAlpineAutumn,2.5f),E(LabFlora.WillowProstrateCatkins,2),E(LabFlora.SedgeThreeRank,1.5f),E(LabFlora.HeatherSparseTips,1.5f),E(LabFlora.TinyGrassSparse,1));
            Set(U,FloraCommunity.Alpine,.26,E(LabFlora.AlpineCushion,4),E(LabFlora.HeatherSparseTips,2),E(LabFlora.BearberryAlpineAutumn,2),E(LabFlora.LichenReindeerMat,2),E(LabFlora.TinyGrassSparse,2),E(LabFlora.WillowProstrateCatkins,1));
            Set(U,FloraCommunity.Scree,.05,E(LabFlora.LichenReindeerMat,2),E(LabFlora.AlpineCushion,1.5f),E(LabFlora.TinyGrassSparse,1));
            Set(U,FloraCommunity.Marsh,.56,E(LabFlora.SedgeThreeRank,3),E(LabFlora.WetlandRush,3),E(LabFlora.WetlandArrowhead,1.5f),E(LabFlora.WetlandPickerelweed,1.5f),E(LabFlora.HorsetailFieldShort,1),E(LabFlora.TinyGrassDense,2));
            Set(U,FloraCommunity.SeasonalPond,.42,E(LabFlora.WetlandWhiteLily,3),E(LabFlora.WetlandPinkLilyStages,3),E(LabFlora.WetlandLotus,1.5f),E(LabFlora.WetlandPickerelweed,1),E(LabFlora.WetlandArrowhead,1));
            Set(U,FloraCommunity.MangroveSwamp,.26,E(LabFlora.MangroveRedSapling,2,-.8f),E(LabFlora.SedgeThreeRank,1),E(LabFlora.WetlandRush,1));
            Set(U,FloraCommunity.SeagrassWarm,.55,E(LabFlora.MarineTurtlegrass,4),E(LabFlora.MarineManateegrass,3),E(LabFlora.MarineSeaLettuce,1));
            Set(U,FloraCommunity.SeagrassCold,.50,E(LabFlora.MarineEelgrass,5),E(LabFlora.MarineSeaLettuce,1.5f));
            Set(U,FloraCommunity.KelpForest,.12,E(LabFlora.MarineSeaLettuce,2),E(LabFlora.MarineEelgrass,1));
            Set(U,FloraCommunity.CoralReef,.40,E(LabFlora.CoralEncrustingRock,3),E(LabFlora.CoralMassiveRidges,2,.6f),E(LabFlora.CoralDigitateFingers,2),E(LabFlora.CoralTieredPlates,1.5f),E(LabFlora.CoralSeafanNet,1),E(LabFlora.CoralStaghornBranching,1.5f),E(LabFlora.CoralElkhornFlattened,1.2f),E(LabFlora.MarineTurtlegrass,1));
            // ---- Midstory (shrubs, small trees, cacti, boulders, reef blocks) ----
            Set(M,FloraCommunity.Steppe,.02,E(LabFlora.SagebrushGnarledOpen,1));
            Set(M,FloraCommunity.SagebrushSteppe,.14,E(LabFlora.SagebrushGnarledOpen,3,.7f),E(LabFlora.SagebrushLowGray,1,-.7f));
            Set(M,FloraCommunity.Savanna,.02,E(LabFlora.PricklyPearSeven,1),E(LabFlora.YuccaTrunkSkirt,.5f));
            Set(M,FloraCommunity.CactusForest,.17,E(LabFlora.ChollaCandelabra,3),E(LabFlora.PricklyPearSeven,2),E(LabFlora.PricklyPearEleven,1.5f,.6f),E(LabFlora.BarrelTall,1.5f,.6f),E(LabFlora.CreosoteOpenPaired,2),E(LabFlora.ChollaTree,1.2f,.5f));
            Set(M,FloraCommunity.CreosoteFlats,.14,E(LabFlora.CreosoteOpenPaired,5),E(LabFlora.ChollaCandelabra,.8f),E(LabFlora.PricklyPearSeven,.5f));
            Set(M,FloraCommunity.YuccaHighland,.10,E(LabFlora.YuccaTrunkSkirt,3,.5f),E(LabFlora.PricklyPearSeven,1.5f),E(LabFlora.PricklyPearEleven,1,.5f),E(LabFlora.SagebrushGnarledOpen,1));
            Set(M,FloraCommunity.Dunes,.01,E(LabFlora.CreosoteOpenPaired,1));
            Set(M,FloraCommunity.Oasis,.22,E(LabFlora.PalmDateCompact,3),E(LabFlora.WetlandPapyrus,2),E(LabFlora.WetlandCattail,2));
            Set(M,FloraCommunity.Beach,.12,E(LabFlora.BeachSeaOats,4));
            Set(M,FloraCommunity.TropicalBeach,.12,E(LabFlora.BeachSeaOats,2),E(LabFlora.PalmSawPalmetto,2),E(LabFlora.PalmCoconutYoung,2,-.8f));
            Set(M,FloraCommunity.TropicalSeasonal,.18,E(LabFlora.PalmSawPalmetto,3),E(LabFlora.JungleBeehiveGinger,1.5f),E(LabFlora.JungleYoungBanana,1,-.5f),E(LabFlora.PalmCoconutYoung,1,-.6f),E(LabFlora.JungleHeliconiaUpright,1));
            Set(M,FloraCommunity.Jungle,.34,E(LabFlora.JungleHeliconiaUpright,3),E(LabFlora.JungleHeliconiaPendant,2),E(LabFlora.JungleBeehiveGinger,3),E(LabFlora.JungleYoungBanana,3,-.4f),E(LabFlora.PalmCoconutYoung,.6f,-.7f),E(LabFlora.PalmSawPalmetto,1));
            Set(M,FloraCommunity.TemperateRainforest,.04,E(LabFlora.HorsetailScouringCones,1));
            Set(M,FloraCommunity.Alpine,.015,E(LabFlora.StoneGraniteJointed,1),E(LabFlora.StoneFracturedSpire,.4f),E(LabFlora.StoneBasaltTalus,1));
            Set(M,FloraCommunity.Scree,.07,E(LabFlora.StoneBasaltTalus,3),E(LabFlora.StoneShalePlates,2),E(LabFlora.StoneGraniteJointed,2),E(LabFlora.StoneLimestoneLedges,1.5f),E(LabFlora.StoneSandstoneGrey,1.5f),E(LabFlora.StoneFracturedSpire,.6f));
            Set(M,FloraCommunity.Marsh,.16,E(LabFlora.WetlandCattail,4),E(LabFlora.WetlandPapyrus,1));
            Set(M,FloraCommunity.SeasonalPond,.05,E(LabFlora.WetlandCattail,2),E(LabFlora.WetlandPapyrus,1));
            Set(M,FloraCommunity.MangroveSwamp,.32,E(LabFlora.MangroveRedSweptTangle,3,-.3f),E(LabFlora.MangroveBlackPencils,3),E(LabFlora.MangroveRedSpreading,2,.6f),E(LabFlora.MangroveWhiteUpland,1,.4f));
            Set(M,FloraCommunity.KelpForest,.30,E(LabFlora.MarineBullKelp,3),E(LabFlora.MarineGiantKelpYoung,3));
            Set(M,FloraCommunity.CoralReef,.07,E(LabFlora.ReefIsolatedPatch,1),E(LabFlora.ReefRuggedGarden,1,.6f),E(LabFlora.ReefShelfTerraces,1,.6f),E(LabFlora.ReefSpurGrooveSlice,1),E(LabFlora.CoralStaghornBranching,1.5f),E(LabFlora.CoralElkhornFlattened,1.5f),E(LabFlora.CoralSeafanNet,1));
            // ---- Canopy (tall lab plants that read at distance; visible to the tree radius) ----
            Set(C,FloraCommunity.CactusForest,.24,E(LabFlora.SaguaroTall,2,-.3f),E(LabFlora.SaguaroTwoArm,2,.4f),E(LabFlora.SaguaroFourArm,1.5f,.9f));
            Set(C,FloraCommunity.CreosoteFlats,.015,E(LabFlora.SaguaroTall,1),E(LabFlora.SaguaroTwoArm,.5f,.5f));
            Set(C,FloraCommunity.Oasis,.24,E(LabFlora.PalmCabbageFan,1),E(LabFlora.PalmCoconutLeaning,.4f));
            Set(C,FloraCommunity.TropicalBeach,.26,E(LabFlora.PalmCoconutLeaning,3));
            Set(C,FloraCommunity.TropicalSeasonal,.07,E(LabFlora.PalmCabbageFan,2),E(LabFlora.PalmCoconutLeaning,.5f));
            Set(C,FloraCommunity.Jungle,.07,E(LabFlora.PalmCabbageFan,2));
            for(int t=0;t<3;t++)foreach(double f in Fill[t])MaximumFill[t]=Math.Max(MaximumFill[t],f*PatchCeiling);
        }
        const double PatchCeiling=1.6;
        public static double FillFor(FloraTier tier,FloraCommunity c)=>Fill[(int)tier][(int)c];
        /// <summary>Classify the plant community at a column. Pure function of the column, position and seed.</summary>
        public static FloraSite Site(SurfaceColumn c,double x,double z,int seed)=>Site(Classify(c,x,z,seed),x,z,seed);
        /// <summary>Patch and age fields at a point for an already classified community.</summary>
        public static FloraSite Site(FloraCommunity community,double x,double z,int seed)
        {
            double age=Clamp(.78*ForestBiomes.Noise(x/900,z/900,unchecked(seed+611))+.22*ForestBiomes.Noise(x/90,z/90,unchecked(seed+617)),0,1);
            // Patchiness: broad clumps with bare gaps; squared so dense clumps and openings both occur.
            double n=ForestBiomes.Noise(x/38,z/38,unchecked(seed+619))*.7+ForestBiomes.Noise(x/9,z/9,unchecked(seed+631))*.3;
            double patch=Clamp(n*n*2.6,0,PatchCeiling);
            return new FloraSite(community,patch,age);
        }
        public static bool IsMarine(FloraCommunity c)=>c>=FloraCommunity.SeagrassWarm;
        public static FloraCommunity Classify(SurfaceColumn c,double x,double z,int seed)
        {
            double depth=c.WaterLevelMeters-c.GroundHeightMeters,t=c.TemperatureC;
            if(c.HasWater||!c.IsLand)
            {
                if(depth<.25)return FloraCommunity.None;
                double reef=ForestBiomes.Noise(x/140,z/140,unchecked(seed+641));
                if(t>19&&depth>=1&&depth<=10&&c.SlopeDegrees<30&&reef>.42)return FloraCommunity.CoralReef;
                if(t<=19&&depth>=2.5&&depth<=14&&reef>.38)return FloraCommunity.KelpForest;
                if(depth<=4.5&&c.SlopeDegrees<14)return t>19?FloraCommunity.SeagrassWarm:FloraCommunity.SeagrassCold;
                return FloraCommunity.None;
            }
            if(c.SnowDepthMeters>=.099||c.SurfaceMaterial==MaterialId.Ice||c.BiomeUsed==11||c.SlopeDegrees>52)return FloraCommunity.None;
            byte b=c.BiomeUsed;double above=c.GroundHeightMeters-c.WaterLevelMeters;
            MaterialId m=c.BaseSurfaceMaterial;
            if(above<3.2&&(m==MaterialId.Sand||m==MaterialId.BlackSand||m==MaterialId.Gravel))
            {
                if(b==12||c.Wetness>.82&&t>20)return t>20?FloraCommunity.MangroveSwamp:FloraCommunity.Marsh;
                return t>21?FloraCommunity.TropicalBeach:t<-2?FloraCommunity.None:FloraCommunity.Beach;
            }
            if(SurfaceProfile.IsRock(m)||c.SlopeDegrees>40)return t<4?FloraCommunity.Alpine:FloraCommunity.Scree;
            double region=ForestBiomes.Noise(x/2600,z/2600,unchecked(seed+653));
            if(b!=10&&b!=9&&t<1.5&&c.GroundHeightMeters>900)return FloraCommunity.Alpine;
            switch(b)
            {
                case 1:
                    if(c.Wetness>.5&&c.Curvature>.05)return FloraCommunity.Oasis;
                    if(c.GroundHeightMeters>1300||region>.72)return FloraCommunity.YuccaHighland;
                    return region<.42?FloraCommunity.CactusForest:FloraCommunity.CreosoteFlats;
                case 13:return c.Wetness>.5&&c.Curvature>.05?FloraCommunity.Oasis:FloraCommunity.Dunes;
                case 2:return region<.6?FloraCommunity.SagebrushSteppe:FloraCommunity.Steppe;
                case 3:return FloraCommunity.Savanna;
                case 4:return region<.55?FloraCommunity.Meadow:FloraCommunity.Steppe;
                case 5:return FloraCommunity.TropicalSeasonal;
                case 7:return FloraCommunity.Jungle;
                case 6:return c.Wetness>.62&&c.Curvature>.12?FloraCommunity.Marsh:FloraCommunity.TemperateWoodland;
                case 8:return FloraCommunity.TemperateRainforest;
                case 9:return FloraCommunity.BorealFloor;
                case 10:return FloraCommunity.Tundra;
                case 12:
                    if(t>20&&above<5)return FloraCommunity.MangroveSwamp;
                    if(c.Curvature>.18&&c.Wetness>.7&&c.SurfaceMaterial==MaterialId.Mud||c.Curvature>.3&&c.Wetness>.75)return FloraCommunity.SeasonalPond;
                    return FloraCommunity.Marsh;
                default:return FloraCommunity.Meadow;
            }
        }
        /// <summary>Pick a prototype for a candidate (variant included) or -1. <paramref name="u"/> is the
        /// fill draw, <paramref name="v"/> the species draw and <paramref name="w"/> the variant draw.</summary>
        public static int Pick(FloraTier tier,FloraSite site,SurfaceColumn c,double u,double v,ulong w,double densityScale)
        {
            if(site.Community==FloraCommunity.None)return -1;
            double fill=Fill[(int)tier][(int)site.Community]*site.Patch*densityScale;
            if(site.Community==FloraCommunity.Scree&&tier==FloraTier.Midstory)fill*=.6+c.SlopeDegrees/60;
            if(u>=fill)return -1;
            Entry[] entries=Table[(int)tier][(int)site.Community];if(entries.Length==0)return -1;
            if(tier==FloraTier.Midstory&&(site.Community==FloraCommunity.Scree||site.Community==FloraCommunity.Alpine))
            {
                int stone=StoneFor(c.BedrockMaterial,v);return stone+(int)(w&1);
            }
            double total=0;foreach(Entry e in entries)total+=Weight(e,site.Age,c);
            if(total<=0)return -1;double pick=v*total;
            foreach(Entry e in entries){pick-=Weight(e,site.Age,c);if(pick<0)return e.Id+(int)(w&1);}
            return entries[entries.Length-1].Id+(int)(w&1);
        }
        static double Weight(Entry e,double age,SurfaceColumn c)
        {
            double w=e.Weight*Math.Max(0,1+e.AgeBias*(age*2-1));
            // Papyrus and date palms are warm-climate plants; cattail tolerates cold.
            if((e.Id==LabFlora.WetlandPapyrus||e.Id==LabFlora.PalmDateCompact)&&c.TemperatureC<14)w=0;
            return w;
        }
        static int StoneFor(MaterialId rock,double v)
        {
            switch(rock)
            {
                case MaterialId.Granite:return v<.75?LabFlora.StoneGraniteJointed:LabFlora.StoneFracturedSpire;
                case MaterialId.Limestone:return v<.85?LabFlora.StoneLimestoneLedges:LabFlora.StoneFracturedSpire;
                case MaterialId.Sandstone:return v<.9?LabFlora.StoneSandstoneGrey:LabFlora.StoneFracturedSpire;
                case MaterialId.Shale:return LabFlora.StoneShalePlates;
                default:return v<.85?LabFlora.StoneBasaltTalus:LabFlora.StoneFracturedSpire;
            }
        }
        static double Clamp(double x,double a,double b)=>x<a?a:x>b?b:x;
    }
}
