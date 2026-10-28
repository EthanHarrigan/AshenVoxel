using System;
namespace AshenVoxel.Core
{
    [Serializable]
    public readonly struct Int3 : IEquatable<Int3>
    {
        public readonly int x, y, z;
        public Int3(int x, int y, int z) { this.x=x; this.y=y; this.z=z; }
        public bool Equals(Int3 other) => x==other.x && y==other.y && z==other.z;
        public override bool Equals(object obj) => obj is Int3 p && Equals(p);
        public override int GetHashCode() { unchecked { return ((x*73856093) ^ (y*19349663) ^ (z*83492791)); } }
        public static bool operator ==(Int3 a, Int3 b) => a.Equals(b);
        public static bool operator !=(Int3 a, Int3 b) => !a.Equals(b);
        public static Int3 operator +(Int3 a, Int3 b) => new Int3(a.x+b.x,a.y+b.y,a.z+b.z);
        public static Int3 operator -(Int3 a, Int3 b) => new Int3(a.x-b.x,a.y-b.y,a.z-b.z);
        public override string ToString() => $"({x}, {y}, {z})";
    }
}
