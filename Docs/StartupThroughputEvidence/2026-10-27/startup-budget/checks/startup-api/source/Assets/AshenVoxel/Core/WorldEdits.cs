using System;
namespace AshenVoxel.Core
{
    public static class WorldEdits
    {
        internal static bool Finite(float n)=>!float.IsNaN(n)&&!float.IsInfinity(n);
        static void Validate(float x,float y,float z,float r)
        {if(!Finite(x)||!Finite(y)||!Finite(z)||!Finite(r)||r<0)throw new ArgumentOutOfRangeException("Brush parameters must be finite; radius nonnegative.");}
        /// <summary>Erase cells whose centers lie in the world-meter sphere and whose hardness is <= power. Bedrock y=0 is preserved.</summary>
        public static int CarveSphere(VoxelWorld w,float x,float y,float z,float radius,float power)
        {if(!Finite(power)||power<0)throw new ArgumentOutOfRangeException(nameof(power));return Apply(w,x,y,z,radius,MaterialId.Air,power);}
        /// <summary>Fill air only. Existing solids are never overwritten; y=0 is preserved.</summary>
        public static int PlaceSphere(VoxelWorld w,float x,float y,float z,float radius,MaterialId material)
        {if(!MaterialCatalog.IsValid(material)||material==MaterialId.Air)throw new ArgumentOutOfRangeException(nameof(material));return Apply(w,x,y,z,radius,material,0);}
        static int Apply(VoxelWorld w,float x,float y,float z,float radius,MaterialId material,float power)
        {
            if(w==null)throw new ArgumentNullException(nameof(w));Validate(x,y,z,radius);
            double s=w.VoxelSize;
            // Clamp in floating point before converting; very large finite input cannot overflow int.
            int xmin=(int)Math.Max(0,Math.Min(w.SizeX,Math.Floor((x-radius)/s)));
            int ymin=(int)Math.Max(1,Math.Min(w.SizeY,Math.Floor((y-radius)/s)));
            int zmin=(int)Math.Max(0,Math.Min(w.SizeZ,Math.Floor((z-radius)/s)));
            int xmax=(int)Math.Max(-1,Math.Min(w.SizeX-1,Math.Ceiling(((double)x+radius)/s)));
            int ymax=(int)Math.Max(0,Math.Min(w.SizeY-1,Math.Ceiling(((double)y+radius)/s)));
            int zmax=(int)Math.Max(-1,Math.Min(w.SizeZ-1,Math.Ceiling(((double)z+radius)/s)));
            int changed=0;
            for(int iz=zmin;iz<=zmax;iz++)for(int iy=ymin;iy<=ymax;iy++)for(int ix=xmin;ix<=xmax;ix++)
            {
                double dx=(ix+.5)*s-x,dy=(iy+.5)*s-y,dz=(iz+.5)*s-z;
                if(dx*dx+dy*dy+dz*dz>(double)radius*radius)continue;
                MaterialId old=w.Get(ix,iy,iz);
                if(material==MaterialId.Air ? old!=MaterialId.Air&&MaterialCatalog.Get(old).Hardness<=power : old==MaterialId.Air)
                    if(w.Set(ix,iy,iz,material))changed++;
            }
            return changed;
        }
    }
}
