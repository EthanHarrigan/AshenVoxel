using System;
namespace AshenVoxel.Core
{
    /// <summary>Opaque sRGB appearance values, independent of material identity. Zero means no explicit tint.</summary>
    public static class VoxelColor
    {
        public static uint Pack(byte r,byte g,byte b)=>0xff000000u|((uint)r<<16)|((uint)g<<8)|b;
        public static bool IsValid(uint tint)=>tint==0||(tint&0xff000000u)==0xff000000u;
        public static uint Base(MaterialId material)
        {
            switch(material)
            {
                case MaterialId.Air:return 0;
                case MaterialId.Stone:return Pack(111,117,124);
                case MaterialId.Mud:return Pack(84,71,55);
                case MaterialId.Dirt:return Pack(103,79,54);
                case MaterialId.Litter:return Pack(80,66,39);
                case MaterialId.Grass:return Pack(81,101,52);
                case MaterialId.Wood:return Pack(103,79,56);
                case MaterialId.Leaves:return Pack(65,91,65);
                case MaterialId.Sand:return Pack(158,143,110);
                case MaterialId.Ember:return Pack(234,112,42);
                case MaterialId.Fruit:return Pack(211,78,106);
                case MaterialId.Granite:return Pack(141,136,132);
                case MaterialId.Limestone:return Pack(160,155,135);
                case MaterialId.Sandstone:return Pack(170,139,102);
                case MaterialId.Shale:return Pack(94,98,100);
                case MaterialId.Gravel:return Pack(116,112,103);
                case MaterialId.Clay:return Pack(123,91,69);
                case MaterialId.BlackSand:return Pack(38,43,46);
                case MaterialId.Snow:return Pack(224,234,236);
                case MaterialId.Ice:return Pack(98,150,164);
                case MaterialId.Silt:return Pack(116,105,85);
                case MaterialId.Peat:return Pack(53,47,36);
                case MaterialId.Fungus:return Pack(163,136,101);
                case MaterialId.Petal:return Pack(181,131,172);
                case MaterialId.Web:return Pack(213,219,222);
                default:throw new ArgumentOutOfRangeException(nameof(material));
            }
        }
    }
}
