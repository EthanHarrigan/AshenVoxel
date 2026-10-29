using System;
using System.Collections.Generic;

namespace AshenVoxel.WorldScale
{
    public readonly struct ClipmapRect
    {
        public readonly long X,Z;public readonly int Width,Height;
        public long EndX=>X+Width;public long EndZ=>Z+Height;public int Count=>checked(Width*Height);
        public ClipmapRect(long x,long z,int width,int height){if(width<0||height<0)throw new ArgumentOutOfRangeException(nameof(width));X=x;Z=z;Width=width;Height=height;}
        public bool Contains(long x,long z)=>x>=X&&z>=Z&&x<EndX&&z<EndZ;
        public bool Intersects(ClipmapRect b)=>X<b.EndX&&EndX>b.X&&Z<b.EndZ&&EndZ>b.Z;
        public ClipmapRect Intersection(ClipmapRect b)
        {long x=Math.Max(X,b.X),z=Math.Max(Z,b.Z),xx=Math.Min(EndX,b.EndX),zz=Math.Min(EndZ,b.EndZ);return new ClipmapRect(x,z,(int)Math.Max(0,xx-x),(int)Math.Max(0,zz-z));}
    }
    public readonly struct ClipmapSample
    {
        public readonly float Height;public readonly uint Tint,Appearance;
        public ClipmapSample(float height,uint tint,uint appearance){Height=height;Tint=tint;Appearance=appearance;}
    }
    public readonly struct ClipmapLevelInfo
    {
        public readonly int Level;public readonly double Spacing;public readonly long OriginX,OriginZ,Generation;public readonly bool Published;
        public ClipmapLevelInfo(int level,double spacing,long originX,long originZ,bool published,long generation)
        {Level=level;Spacing=spacing;OriginX=originX;OriginZ=originZ;Published=published;Generation=generation;}
    }
    public readonly struct ClipmapJob
    {
        public readonly long Id,Generation;public readonly int Level;public readonly ClipmapRect Rect;public int SampleCount=>Rect.Count;
        internal ClipmapJob(long id,long generation,int level,ClipmapRect rect){Id=id;Generation=generation;Level=level;Rect=rect;}
    }
    /// <summary>Borrowed staging arrays remain valid only until Publish/Invalidate/Move.
    /// Complete does not publish. Caller must dispatch every changed texel first.</summary>
    public sealed class ClipmapPublication
    {
        public readonly int Level;public readonly long Generation,OriginX,OriginZ;
        public readonly ClipmapSample[] Samples;public readonly ClipmapRect[] ChangedRegions;
        internal ClipmapPublication(int level,long generation,long x,long z,ClipmapSample[] samples,ClipmapRect[] regions)
        {Level=level;Generation=generation;OriginX=x;OriginZ=z;Samples=samples;ChangedRegions=regions;}
    }
    public static class TerrainClipmapMath
    {
        public static void ValidateSize(int size){if(size<16||size>1024||(size&(size-1))!=0)throw new ArgumentOutOfRangeException(nameof(size));}
        public static long WindowOrigin(double camera,double spacing,int size)
        {Numeric.Finite(camera,nameof(camera));Numeric.Positive(spacing,nameof(spacing));ValidateSize(size);double cell=Math.Floor(camera/spacing);if(Math.Abs(cell)>1e14)throw new ArgumentOutOfRangeException(nameof(camera));return checked((long)(2*Math.Floor(cell/2))-size/2);}
        public static int ToroidalIndex(long x,long z,int size)
        {int ix=(int)(x%size),iz=(int)(z%size);if(ix<0)ix+=size;if(iz<0)iz+=size;return ix+iz*size;}
        public static double TriangleHeight(double h00,double h10,double h01,double h11,double fx,double fz)
            =>fx>=fz?h00+fx*(h10-h00)+fz*(h11-h10):h00+fz*(h01-h00)+fx*(h11-h01);
        public static double MorphAlpha(double x,double z,int size)
        {double edge=Math.Min(Math.Min(x-2,size-2-x),Math.Min(z-2,size-2-z));return Math.Max(0,Math.Min(1,1-(edge-2)/(size/8.0)));}
        public static bool Contains(ClipmapLevelInfo level,int size,double x,double z,double radius=0)
        {double x0=(level.OriginX+2)*level.Spacing,z0=(level.OriginZ+2)*level.Spacing,x1=(level.OriginX+size-2)*level.Spacing,z1=(level.OriginZ+size-2)*level.Spacing;return level.Published&&x-radius>=x0&&x+radius<=x1&&z-radius>=z0&&z+radius<=z1;}
        public static bool CanNest(ClipmapLevelInfo parent,ClipmapLevelInfo child,int size)
        {
            if(!parent.Published||!child.Published||parent.Level!=child.Level+1||parent.Spacing!=child.Spacing*2)return false;
            int margin=size/8+5;long x0=(child.OriginX+2)/2-parent.OriginX,z0=(child.OriginZ+2)/2-parent.OriginZ,x1=(child.OriginX+size-2)/2-parent.OriginX,z1=(child.OriginZ+size-2)/2-parent.OriginZ;
            return x0>=margin&&z0>=margin&&x1<=size-margin&&z1<=size-margin;
        }
        public static ClipmapRect[] GetRingRects(ClipmapLevelInfo parent,ClipmapLevelInfo? child,int size)
        {
            ValidateSize(size);if(!child.HasValue)return new[]{new ClipmapRect(2,2,size-4,size-4)};
            var c=child.Value;if(!CanNest(parent,c,size))throw new ArgumentException("Child must fit parent unmorphed interior");
            long x0=(c.OriginX+2)/2-parent.OriginX,z0=(c.OriginZ+2)/2-parent.OriginZ,x1=(c.OriginX+size-2)/2-parent.OriginX,z1=(c.OriginZ+size-2)/2-parent.OriginZ;
            var result=new List<ClipmapRect>(4);
            void Add(long x,long z,long w,long h){if(w>0&&h>0)result.Add(new ClipmapRect(x,z,(int)w,(int)h));}
            Add(2,2,size-4,z0-2);Add(2,z1,size-4,size-2-z1);Add(2,z0,x0-2,z1-z0);Add(x1,z0,size-2-x1,z1-z0);return result.ToArray();
        }
        public static int ComputeLevelCount(int size,double finestSpacing,double viewRadius,double worldHalfX,double worldHalfZ)
        {
            ValidateSize(size);Numeric.Positive(finestSpacing,nameof(finestSpacing));Numeric.Positive(viewRadius,nameof(viewRadius));Numeric.Positive(worldHalfX,nameof(worldHalfX));Numeric.Positive(worldHalfZ,nameof(worldHalfZ));
            // The stationary anchor must contain a moving immediate child inside
            // its flat interior at either domain edge, plus one cell of snapping.
            double half=Math.Max(worldHalfX,worldHalfZ),drift=size/8.0-5;
            if(drift<=0)throw new ArgumentException("Grid too small for a moving world anchor");
            for(int n=2;n<=16;n++)
            {double s=finestSpacing*Math.Pow(2,n-1);if((size/2.0-2)*s>=half+viewRadius&&drift*s>=half)return n;}
            throw new ArgumentException("Clipmap configuration needs more than 16 levels for a complete world anchor; use larger grid or spacing");
        }
    }
}
