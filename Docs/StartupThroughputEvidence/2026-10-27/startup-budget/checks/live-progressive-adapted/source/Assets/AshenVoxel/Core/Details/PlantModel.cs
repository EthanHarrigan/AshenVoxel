using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace AshenVoxel.Core.Details
{
    public enum PlantKind { Fern,Shrub,GrassTuft,Reed,BroadleafHerb,Flower,Mushroom,Cactus,Succulent,Cushion }
    [Flags] public enum PlantTruncation { None=0,CandidateBudget=1,CellBudget=2,WorldBoundary=4,Occupied=8 }

    /// <summary>Immutable constructive plant profile. Dimensions are meters on the required10 cm grid,
    /// not a botanical growth simulation. Living stems reuse Leaves; only woody shrubs use Wood.</summary>
    public sealed class PlantProfile
    {
        public readonly PlantKind Kind;
        public readonly double HeightMeters,RadiusMeters,Density,FruitProbability,Variation;
        public readonly int Stems,PaletteSteps;
        public readonly uint FoliageColor,WoodColor,AccentColor;
        public PlantProfile(PlantKind kind,double heightMeters=.6,double radiusMeters=.4,int stems=7,double density=.8,
            double fruitProbability=0,uint foliageColor=0xff45653a,uint woodColor=0xff65503a,uint accentColor=0xffb47d44,
            double variation=.35,int paletteSteps=4)
        {
            if(!Enum.IsDefined(typeof(PlantKind),kind))throw new ArgumentOutOfRangeException(nameof(kind));
            Range(heightMeters,.04,4,nameof(heightMeters));Range(radiusMeters,.025,2.5,nameof(radiusMeters));
            Range(density,0,1,nameof(density));Range(fruitProbability,0,1,nameof(fruitProbability));Range(variation,0,1,nameof(variation));
            if(stems<1||stems>32)throw new ArgumentOutOfRangeException(nameof(stems));
            if(paletteSteps<2||paletteSteps>6)throw new ArgumentOutOfRangeException(nameof(paletteSteps));
            if((foliageColor>>24)!=255||(woodColor>>24)!=255||(accentColor>>24)!=255)throw new ArgumentException("Plant colors must be opaque sRGB.");
            Kind=kind;HeightMeters=heightMeters;RadiusMeters=radiusMeters;Stems=stems;Density=density;FruitProbability=fruitProbability;
            FoliageColor=foliageColor;WoodColor=woodColor;AccentColor=accentColor;Variation=variation;PaletteSteps=paletteSteps;
        }
        static void Range(double n,double low,double high,string name)
        {if(double.IsNaN(n)||double.IsInfinity(n)||n<low||n>high)throw new ArgumentOutOfRangeException(name);}
        public PlantProfile With(double? heightMeters=null,double? radiusMeters=null,int? stems=null,double? density=null,
            double? fruitProbability=null,uint? foliageColor=null,uint? woodColor=null,uint? accentColor=null,double? variation=null,int? paletteSteps=null)
            =>new PlantProfile(Kind,heightMeters??HeightMeters,radiusMeters??RadiusMeters,stems??Stems,density??Density,
                fruitProbability??FruitProbability,foliageColor??FoliageColor,woodColor??WoodColor,accentColor??AccentColor,variation??Variation,paletteSteps??PaletteSteps);
    }
    public sealed class PlantGenerationBudget
    {
        public readonly long MaxCandidates;
        public readonly int MaxCells;
        public PlantGenerationBudget(long maxCandidates=250000,int maxCells=32768)
        {
            if(maxCandidates<1||maxCandidates>4000000)throw new ArgumentOutOfRangeException(nameof(maxCandidates));
            if(maxCells<1||maxCells>200000)throw new ArgumentOutOfRangeException(nameof(maxCells));
            MaxCandidates=maxCandidates;MaxCells=maxCells;
        }
    }
    public sealed class PlantResult
    {
        public PlantKind Kind {get;internal set;}
        public int Seed {get;internal set;}
        /// <summary>Inclusive conservative envelope of the requested complete specimen, including quantization.</summary>
        public Int3 RequestedMinCell {get;internal set;}
        public Int3 RequestedMaxCell {get;internal set;}
        /// <summary>Inclusive actual occupied write bounds; zero when WrittenCells is zero.</summary>
        public Int3 MinCell {get;internal set;}
        public Int3 MaxCell {get;internal set;}
        public int WrittenCells {get;internal set;}
        public long Candidates {get;internal set;}
        public int BlockedCells {get;internal set;}
        public PlantTruncation Truncation {get;internal set;}
        public bool Truncated=>Truncation!=PlantTruncation.None;
        public IReadOnlyDictionary<MaterialId,int> MaterialCounts {get;internal set;}
        internal static IReadOnlyDictionary<MaterialId,int> Freeze(Dictionary<MaterialId,int> counts)=>new ReadOnlyDictionary<MaterialId,int>(counts);
    }
    public static class PlantPresets
    {
        public static PlantProfile For(PlantKind kind)
        {
            switch(kind)
            {
                // Five open fronds leave room for a readable two-cell pinna cadence on the10 cm grid.
                case PlantKind.Fern:return new PlantProfile(kind,.65,.95,5,.85,foliageColor:0xff4d7440);
                case PlantKind.Shrub:return new PlantProfile(kind,1.35,.95,6,.84,.15,0xff4b633e,0xff62503d,0xff9f343a);
                case PlantKind.GrassTuft:return new PlantProfile(kind,.5,.34,9,.75,foliageColor:0xff697443);
                case PlantKind.Reed:return new PlantProfile(kind,1.45,.43,7,.8,foliageColor:0xff777d46,accentColor:0xff8b6337);
                case PlantKind.BroadleafHerb:return new PlantProfile(kind,.45,.4,6,.9,foliageColor:0xff607945);
                case PlantKind.Flower:return new PlantProfile(kind,.5,.31,5,.8,foliageColor:0xff667844,accentColor:0xffc1a0b6);
                case PlantKind.Mushroom:return new PlantProfile(kind,.35,.28,1,1,foliageColor:0xffe0d6b4,accentColor:0xffa94c37);
                case PlantKind.Cactus:return new PlantProfile(kind,1.25,.48,3,.9,foliageColor:0xff6c8560,accentColor:0xffc8a571);
                case PlantKind.Succulent:return new PlantProfile(kind,.35,.48,10,.85,foliageColor:0xff81967a);
                case PlantKind.Cushion:return new PlantProfile(kind,.18,.42,9,.88,foliageColor:0xff7f8857,accentColor:0xffc1b294);
                default:throw new ArgumentOutOfRangeException(nameof(kind));
            }
        }
    }
}
