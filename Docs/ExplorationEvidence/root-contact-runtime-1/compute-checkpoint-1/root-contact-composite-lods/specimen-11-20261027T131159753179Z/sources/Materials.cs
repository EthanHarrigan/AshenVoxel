using System;
namespace AshenVoxel.Core
{
    // Append only. IDs0..10 are persisted by the original Format1/2 snapshots.
    public enum MaterialId : byte
    {
        Air=0, Stone=1, Mud=2, Dirt=3, Litter=4, Grass=5, Wood=6, Leaves=7,
        Sand=8, Ember=9, Fruit=10,
        Granite=11, Limestone=12, Sandstone=13, Shale=14, Gravel=15, Clay=16,
        BlackSand=17, Snow=18, Ice=19, Silt=20, Peat=21,
        Fungus=22, Petal=23, Web=24
    }
    public readonly struct MaterialDefinition
    {
        public readonly string Name;
        public readonly bool Solid, Granular, Flammable;
        public readonly float Hardness, Density, Opacity, Transmission;
        public MaterialDefinition(string name, bool solid, bool granular, bool flammable, float hardness, float density,float opacity=1,float transmission=0)
        { Name=name; Solid=solid; Granular=granular; Flammable=flammable; Hardness=hardness; Density=density;
            if(float.IsNaN(opacity)||float.IsNaN(transmission)||opacity<0||opacity>1||transmission<0||transmission>1)throw new ArgumentOutOfRangeException("material optics");Opacity=opacity;Transmission=transmission; }
    }
    public static class MaterialCatalog
    {
        /// <summary>Append-only material catalogue revision, independent of terrain-generator and snapshot container versions.</summary>
        public const int Version = 4;
        public const int Count = 25;
        private static readonly MaterialDefinition[] Definitions = {
            new MaterialDefinition("Air", false,false,false,0,0,0,0),
            new MaterialDefinition("Basalt",true,false,false,5,2.8f),
            new MaterialDefinition("Mud",true,true,false,0.7f,1.8f),
            new MaterialDefinition("Soil",true,true,false,1,1.5f),
            new MaterialDefinition("Leaf litter",true,true,true,0.25f,0.3f),
            new MaterialDefinition("Moss / grass",true,true,true,0.35f,0.4f),
            new MaterialDefinition("Wood",true,false,true,2,0.8f),
            new MaterialDefinition("Leaves",true,false,true,0.2f,0.15f,.94f,.55f),
            new MaterialDefinition("Sand",true,true,false,0.5f,1.6f),
            new MaterialDefinition("Ember",true,true,false,0.3f,0.25f),
            new MaterialDefinition("Fruit",true,true,true,0.2f,0.6f),
            // Hardness is a game tool threshold, not a Mohs or engineering strength measurement.
            // Density is an approximate bulk-density cue; granular motion is still the shared simple solver.
            new MaterialDefinition("Granite",true,false,false,6.5f,2.7f),
            new MaterialDefinition("Limestone",true,false,false,3,2.4f),
            new MaterialDefinition("Sandstone",true,false,false,2.5f,2.2f),
            new MaterialDefinition("Shale",true,false,false,1.8f,2.3f),
            new MaterialDefinition("Gravel",true,true,false,0.65f,1.8f),
            new MaterialDefinition("Clay",true,true,false,0.85f,1.7f),
            new MaterialDefinition("Black sand",true,true,false,0.55f,1.9f),
            new MaterialDefinition("Snow",true,true,false,0.15f,0.3f),
            new MaterialDefinition("Ice",true,false,false,1.4f,0.92f,.48f,.7f),
            new MaterialDefinition("Silt",true,true,false,0.45f,1.45f),
            new MaterialDefinition("Peat",true,true,true,0.3f,0.35f),
            // Attached tissues remain static until edited, like existing Leaves.
            // A distinct material is not a new Unity Material or texture per plant.
            new MaterialDefinition("Fungus",true,false,true,0.2f,0.35f),
            new MaterialDefinition("Petal",true,false,true,0.1f,0.1f,.85f,.6f),
            new MaterialDefinition("Web",true,false,true,.05f,.01f,.3f,.65f)
        };
        public static MaterialDefinition Get(MaterialId id)
        {
            int n=(int)id;
            if(n<0 || n>=Count) throw new ArgumentOutOfRangeException(nameof(id));
            return Definitions[n];
        }
        public static bool IsValid(MaterialId id) => (byte)id < Count;
        /// <summary>Actor-body policy is separate from matter/tool/mesh solidity.
        /// Soft foliage remains targetable and saved but is normally passable.</summary>
        public static bool BlocksActorBody(MaterialId id,bool collideWithSoftFoliage=false)
        {
            if(!Get(id).Solid)return false;
            return collideWithSoftFoliage||(id!=MaterialId.Leaves&&id!=MaterialId.Fungus&&id!=MaterialId.Petal&&id!=MaterialId.Web);
        }
    }
}
