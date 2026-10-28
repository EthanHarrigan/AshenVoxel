using System;
namespace AshenVoxel.WorldScale
{
    public readonly struct WorldPoint
    {
        public readonly double X,Y,Z;
        public WorldPoint(double x,double y,double z){Numeric.Finite(x,nameof(x));Numeric.Finite(y,nameof(y));Numeric.Finite(z,nameof(z));X=x;Y=y;Z=z;}
        public WorldPoint RelativeTo(WorldPoint origin)=>new WorldPoint(X-origin.X,Y-origin.Y,Z-origin.Z);
        public static WorldPoint operator +(WorldPoint a,WorldPoint b)=>new WorldPoint(a.X+b.X,a.Y+b.Y,a.Z+b.Z);
    }
    public readonly struct WorldCell : IEquatable<WorldCell>
    {
        public const double SizeMeters=.1;
        public readonly long X,Y,Z;
        public WorldCell(long x,long y,long z){X=x;Y=y;Z=z;}
        public static WorldCell FromMeters(WorldPoint p)=>new WorldCell(Floor(p.X),Floor(p.Y),Floor(p.Z));
        static long Floor(double meters){if(Math.Abs(meters)>1e12)throw new ArgumentOutOfRangeException(nameof(meters),"10cm coordinate conversion is limited to +/-1e12 meters to retain double precision.");double n=Math.Floor(meters*10);if(n<long.MinValue||n>=9223372036854775808.0)throw new ArgumentOutOfRangeException(nameof(meters));return checked((long)n);}
        public WorldPoint Center=>new WorldPoint((X+.5)*SizeMeters,(Y+.5)*SizeMeters,(Z+.5)*SizeMeters);
        public static long FloorDivide(long value,int divisor){if(divisor<=0)throw new ArgumentOutOfRangeException(nameof(divisor));long q=value/divisor;return value%divisor<0?q-1:q;}
        public bool Equals(WorldCell other)=>X==other.X&&Y==other.Y&&Z==other.Z;
        public override bool Equals(object other)=>other is WorldCell c&&Equals(c);
        public override int GetHashCode(){unchecked{return ((X.GetHashCode()*397)^Y.GetHashCode())*397^Z.GetHashCode();}}
    }
    public static class FloatingOrigin
    {
        public static WorldPoint Snap(WorldPoint camera,double quantumMeters=1024)
        {Numeric.Positive(quantumMeters,nameof(quantumMeters));return new WorldPoint(Math.Floor(camera.X/quantumMeters)*quantumMeters,Math.Floor(camera.Y/quantumMeters)*quantumMeters,Math.Floor(camera.Z/quantumMeters)*quantumMeters);}
        // Convert to float only AFTER subtracting these double coordinates in the renderer.
        public static WorldPoint Local(WorldPoint global,WorldPoint origin)=>global.RelativeTo(origin);
    }
    internal static class Numeric
    {
        public static void Finite(double n,string name){if(double.IsNaN(n)||double.IsInfinity(n))throw new ArgumentOutOfRangeException(name);}
        public static void Positive(double n,string name){Finite(n,name);if(n<=0)throw new ArgumentOutOfRangeException(name);}
        public static double Clamp(double x,double a,double b)=>Math.Max(a,Math.Min(b,x));
    }
}
