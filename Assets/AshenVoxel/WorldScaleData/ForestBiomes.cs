using System;
using AshenVoxel.Core.Trees;
namespace AshenVoxel.WorldScale
{
    public enum ForestHabitat { None, OakWoodland, BirchGrove, PineTaiga, RedwoodRainforest, SeasonalBroadleaf }
    public enum ForestStage { YoungDense, MatureOpen }
    public readonly struct ForestStand
    {
        public readonly ForestHabitat Habitat;
        public readonly ForestStage Stage;
        public readonly TreeSpecies Species;
        public readonly double Density,Scale;
        public bool HasTrees=>Habitat!=ForestHabitat.None&&Density>0;
        public ForestStand(ForestHabitat habitat,ForestStage stage,TreeSpecies species,double density,double scale)
        {Habitat=habitat;Stage=stage;Species=species;Density=density;Scale=scale;}
        public string DisplayName=>Habitat+" / "+(Stage==ForestStage.YoungDense?"young dense":"mature open");
    }
    /// <summary>Static habitat/stand-age assignment. A stand has one dominant species;
    /// broad patches do not indiscriminately mix all four trees. Source biome controls
    /// habitat while coherent3–6km fields control age, openness and species subregions.</summary>
    public static class ForestBiomes
    {
        public const int Version=2;
        public static ForestStand Sample(SurfaceColumn surface,double x,double z,int seed)
        {
            Numeric.Finite(x,nameof(x));Numeric.Finite(z,nameof(z));
            byte b=surface.BiomeUsed;
            if(!surface.IsLand||surface.HasWater||b==0||b==1||b==2||b==10||b==11||b==13||surface.SlopeDegrees>42||surface.TemperatureC< -22||surface.SnowDepthMeters>2.2)
                return new ForestStand(ForestHabitat.None,ForestStage.MatureOpen,TreeSpecies.Oak,0,1);
            double region=Noise(x/5600,z/5600,unchecked(seed+101));
            ForestHabitat habitat;TreeSpecies species;double cover;
            switch(b)
            {
                case 6:habitat=region<.45?ForestHabitat.BirchGrove:ForestHabitat.OakWoodland;species=region<.45?TreeSpecies.Birch:TreeSpecies.Oak;cover=.92;break;
                case 8:habitat=surface.TemperatureC>1&&surface.Wetness>.48?ForestHabitat.RedwoodRainforest:ForestHabitat.PineTaiga;species=habitat==ForestHabitat.RedwoodRainforest?TreeSpecies.Redwood:TreeSpecies.Pine;cover=.94;break;
                case 9:habitat=ForestHabitat.PineTaiga;species=TreeSpecies.Pine;cover=.80;break;
                case 5:case 7:habitat=ForestHabitat.SeasonalBroadleaf;species=TreeSpecies.Oak;cover=b==7?.95:.66;break;
                case 12:habitat=region<.48?ForestHabitat.BirchGrove:ForestHabitat.OakWoodland;species=region<.48?TreeSpecies.Birch:TreeSpecies.Oak;cover=.31;break;
                case 3:case 4:habitat=ForestHabitat.OakWoodland;species=TreeSpecies.Oak;cover=b==3?.075:.035;break;
                default:habitat=ForestHabitat.OakWoodland;species=TreeSpecies.Oak;cover=.3;break;
            }
            double age=Noise(x/3700,z/3700,unchecked(seed+211));
            ForestStage stage=age<.46?ForestStage.YoungDense:ForestStage.MatureOpen;
            double openness=.30+.70*Noise(x/1250,z/1250,unchecked(seed+307));
            // v2: denser stands overall, plus kilometre-scale thickets where young regrowth packs in
            // beyond one tree per 6 m grid point (Density>1 drives the population's infill grid).
            double density=stage==ForestStage.YoungDense?cover*(.55+.45*openness):cover*(.40+.60*openness)*.38;
            if(stage==ForestStage.YoungDense)
            {
                double thicket=Noise(x/760,z/760,unchecked(seed+331));
                if(thicket>.5)density*=1+(thicket-.5)*1.5;
            }
            if(species==TreeSpecies.Redwood)density*=stage==ForestStage.YoungDense?.68:.42;
            return new ForestStand(habitat,stage,species,density,1);
        }
        public static double Noise(double x,double z,int seed)
        {
            long ix=(long)Math.Floor(x),iz=(long)Math.Floor(z);double u=x-ix,v=z-iz;u=u*u*(3-2*u);v=v*v*(3-2*v);
            double a=Unit(Hash(ix,iz,seed)),b=Unit(Hash(ix+1,iz,seed)),c=Unit(Hash(ix,iz+1,seed)),d=Unit(Hash(ix+1,iz+1,seed));
            return (a+(b-a)*u)*(1-v)+(c+(d-c)*u)*v;
        }
        internal static ulong Hash(long x,long z,int seed){unchecked{return Mix((ulong)x*0x9e3779b185ebca87UL^(ulong)z*0xc2b2ae3d27d4eb4fUL^(uint)seed);}}
        internal static ulong Mix(ulong n){unchecked{n^=n>>30;n*=0xbf58476d1ce4e5b9UL;n^=n>>27;n*=0x94d049bb133111ebUL;return n^(n>>31);}}
        internal static double Unit(ulong n)=>(n>>11)*(1.0/9007199254740992.0);
    }
}
