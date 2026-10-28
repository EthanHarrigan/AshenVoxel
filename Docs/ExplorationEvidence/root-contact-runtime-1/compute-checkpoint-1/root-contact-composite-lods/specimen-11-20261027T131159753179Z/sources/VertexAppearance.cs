using System;
namespace AshenVoxel.Core
{
    /// <summary>Render-cache encoding only. Intrinsic voxel colors remain opaque
    /// 0xFFRRGGBB in matter/saves. Five material bits plus two AO bits fit in
    /// vertex alpha without adding a GPU vertex stream.31 denotes legacy unknown.</summary>
    public static class VertexAppearance
    {
        public const int Version=1,UnknownMaterial=31;
        public static byte Pack(MaterialId material,int occlusion)
        {
            if(!MaterialCatalog.IsValid(material)||(int)material>=UnknownMaterial||occlusion<0||occlusion>3)throw new ArgumentOutOfRangeException();
            return (byte)(((int)material<<2)|occlusion);
        }
        public static byte Legacy(byte oldAo)=>(byte)((UnknownMaterial<<2)|Math.Min(3,Math.Max(0,(int)Math.Round((oldAo-64)*3.0/191))));
        public static byte Occlusion(byte packed)=>(byte)(64+(packed&3)*191/3);
        public static int Material(byte packed)=>Math.Min(UnknownMaterial,packed>>2);
    }
}
