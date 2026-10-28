using System;

namespace AshenVoxel.Core
{
    /// <summary>Seeded sRGB material variation in physical space. These are intrinsic colors, never baked lighting.</summary>
    public static class EnvironmentColors
    {
        /// <summary>Coherent deposits and weathering with restrained quantized grain; material identity is unchanged.</summary>
        public static uint Sample(MaterialId material,int x,int y,int z,float voxelSize,int seed,float strength=1,uint baseColor=0)
        {
            if(!WorldEdits.Finite(strength)||strength<0||strength>2)throw new ArgumentOutOfRangeException(nameof(strength));
            if(!WorldEdits.Finite(voxelSize)||voxelSize<=0)throw new ArgumentOutOfRangeException(nameof(voxelSize));
            if(!MaterialCatalog.IsValid(material))throw new ArgumentOutOfRangeException(nameof(material));
            uint basis=baseColor==0?VoxelColor.Base(material):baseColor;
            if(material==MaterialId.Air)return 0;
            if((basis>>24)!=255)throw new ArgumentException("Base color must be opaque packed sRGB.",nameof(baseColor));
            if(strength==0)return basis;
            double px=(x+.5)*voxelSize,py=(y+.5)*voxelSize,pz=(z+.5)*voxelSize;
            double broad=Generation.Noise(px*.43+py*.17,pz*.43-py*.11,seed+149);
            double detail=Generation.Noise(px*1.8+py*.27,pz*1.8-py*.41,seed+293);
            // A small shared grain patch allows useful face merges, rather than confetti per voxel.
            double grain=(Generation.Hash(x>>1,(z>>1)^((y>>1)*103),seed+421)&255)/255.0-.5;
            double value=(broad*.105+detail*.045+grain*.045)*strength;
            double warm=Generation.Noise(px*.71+py*.09,pz*.71,seed+71)*strength;
            double r=(basis>>16)&255,g=(basis>>8)&255,b=basis&255;
            if(material==MaterialId.Stone)
            {
                double vein=Generation.Noise(px*.35+py*1.1,pz*.45-py*.2,seed+509);
                value+=Math.Max(0,vein-.18)*.13*strength;
                r+=warm*5;g+=warm*1.5;b-=warm*4;
            }
            else if(material==MaterialId.Grass)
            {r+=warm*8;g+=warm*6;b-=warm*5;value+=broad*.06*strength;}
            else if(material==MaterialId.Dirt||material==MaterialId.Mud||material==MaterialId.Litter)
            {r+=warm*6;g+=warm*2;b-=warm*3;}
            else if(material==MaterialId.Granite)
            {
                // Aggregate mineral patches are deliberately enlarged enough to read at10cm.
                // One shared neutral/pink signal avoids independent RGB confetti.
                double mineral=Generation.Noise(px*4.5+py*2.1,pz*4.5-py*1.7,seed+631);
                value+=(mineral>.35?.12:mineral<-.38?-.18:mineral*.045)*strength;
                r+=warm*7;g+=warm*2;b-=warm*2;
            }
            else if(material==MaterialId.Limestone)
            {
                double bedding=Generation.Noise(px*.15+py*1.4,pz*.17-py*.3,seed+641);
                value+=Math.Round(bedding*4)*.015*strength;
                r+=warm*3;b-=warm*3;
            }
            else if(material==MaterialId.Sandstone)
            {
                double drift=Generation.Noise(px*.14,pz*.14,seed+653)*.35;
                double layers=Math.Sin((py+drift)*Math.PI*3.4);
                value+=Math.Round(layers*3)*.018*strength;
                r+=warm*5;g+=warm*2;b-=warm*2;
            }
            else if(material==MaterialId.Shale)
            {
                double bedding=Generation.Noise(px*.12+py*5.0,pz*.12,seed+659);
                value+=Math.Round(bedding*5)*.016*strength;
                b+=warm*3;
            }
            else if(material==MaterialId.Gravel)
            {
                double clast=Generation.Noise(px*2.4+py*1.1,pz*2.4-py*.8,seed+673);
                value+=Math.Round(clast*4)*.025*strength;
                r+=warm*3;b-=warm*2;
            }
            else if(material==MaterialId.Clay||material==MaterialId.Silt||material==MaterialId.Peat)
            {r+=warm*4;g+=warm*2;b-=warm*2;value+=broad*.025*strength;}
            else if(material==MaterialId.BlackSand)
            {value*=.72;r+=warm*1.5;b-=warm*1.5;}
            else if(material==MaterialId.Snow)
            {
                // Intrinsic snow remains pale; shadow blue comes from lighting, not pigment.
                value=(broad*.018+detail*.009+grain*.009)*strength;
                b+=warm*.8;
            }
            else if(material==MaterialId.Ice)
            {
                double vein=Generation.Noise(px*.75+py*2.2,pz*.8-py*.5,seed+683);
                value=(broad*.04+detail*.02+grain*.01)*strength+Math.Max(0,vein-.28)*.15*strength;
            }
            r*=1+value;g*=1+value;b*=1+value;
            return VoxelColor.Pack(Quantize(r),Quantize(g),Quantize(b));
        }
        static byte Quantize(double value)=> (byte)Math.Max(0,Math.Min(255,Math.Round(value/4)*4));
    }
}
