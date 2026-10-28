using System;
using AshenVoxel.Core;

namespace RootContactArrayMesher
{
    // Blittable managed values; this assembly has no Unity reference.
    public struct Float3
    {
        public float x,y,z;
        public Float3(float x,float y,float z){this.x=x;this.y=y;this.z=z;}
        public static Float3 zero=>default;
        public float this[int i]{get=>i==0?x:i==1?y:i==2?z:throw new IndexOutOfRangeException();set{if(i==0)x=value;else if(i==1)y=value;else if(i==2)z=value;else throw new IndexOutOfRangeException();}}
        public static Float3 operator +(Float3 a,Float3 b)=>new Float3(a.x+b.x,a.y+b.y,a.z+b.z);
        public static Float3 operator *(Float3 a,float b)=>new Float3(a.x*b,a.y*b,a.z*b);
    }
    public struct Rgba32
    {
        public byte r,g,b,a;
        public Rgba32(byte r,byte g,byte b,byte a){this.r=r;this.g=g;this.b=b;this.a=a;}
    }
    public readonly struct MeshBounds
    {
        public readonly bool HasVertices;public readonly Float3 Minimum,Maximum;
        internal MeshBounds(Float3[] p,int count)
        {
            HasVertices=count!=0;Float3 min=count==0?default:p[0],max=min;
            for(int i=1;i<count;i++)for(int d=0;d<3;d++){min[d]=Math.Min(min[d],p[i][d]);max[d]=Math.Max(max[d],p[i][d]);}
            Minimum=min;Maximum=max;
        }
    }
    public sealed class MeshCapacity
    {
        // Six directed faces per solid cell is the absolute per-chunk face ceiling.
        public const int MaximumQuads=6*16*16*16;
        public readonly int Quads;
        public long WorkingBufferBytes=>136L*Quads;
        // Final compaction can coexist with all working arrays; GC backlog is separate.
        public long MaximumLiveElementBytes=>2*WorkingBufferBytes;
        public MeshCapacity(int quads=4096,long maximumBufferBytes=4096L*136)
        {
            if(quads<0||quads>MaximumQuads||maximumBufferBytes<0||136L*quads>maximumBufferBytes)
                throw new ArgumentOutOfRangeException(nameof(quads),"Explicit quad/buffer reservation exceeded.");
            Quads=quads;
        }
    }
    // Caller snapshots the 18^3 neighborhood on its owning lane, then checks its
    // source revision. Arrays are copied, so later caller mutation cannot affect work.
    public sealed class ChunkInput
    {
        public const int HaloCells=18*18*18;
        readonly MaterialId[] materials;readonly uint[] colors;
        public readonly Int3 Coordinate;public readonly float VoxelSize;
        public readonly int MaximumEmittedCellY;public readonly string Identity;
        public long PayloadBytes=>5L*HaloCells;
        public ChunkInput(Int3 coordinate,float voxelSize,int maximumEmittedCellY,string identity,MaterialId[] materialHalo,uint[] colorHalo)
        {
            if(materialHalo==null||colorHalo==null||materialHalo.Length!=HaloCells||colorHalo.Length!=HaloCells||string.IsNullOrEmpty(identity)||identity.Length>4096||float.IsNaN(voxelSize)||float.IsInfinity(voxelSize)||voxelSize<=0)
                throw new ArgumentException("Complete bounded immutable halo required.");
            for(int d=0;d<3;d++)
            {
                int c=d==0?coordinate.x:d==1?coordinate.y:coordinate.z;
                long low=(long)c*16-1,high=(long)c*16+16;
                if(low<int.MinValue||high>int.MaxValue||float.IsInfinity((float)low*voxelSize)||float.IsInfinity((float)high*voxelSize))throw new ArgumentException("Nonfinite or overflowing chunk bounds.");
            }
            materials=(MaterialId[])materialHalo.Clone();colors=(uint[])colorHalo.Clone();
            for(int i=0;i<HaloCells;i++)
                if(!MaterialCatalog.IsValid(materials[i])||(materials[i]==MaterialId.Air?colors[i]!=0:colors[i]>>24!=255))throw new ArgumentException("Invalid material/effective opaque pigment in halo.");
            Coordinate=coordinate;VoxelSize=voxelSize;MaximumEmittedCellY=maximumEmittedCellY;Identity=identity;
        }
        internal MaterialId Material(int x,int y,int z)=>materials[(x+1)+18*((y+1)+18*(z+1))];
        internal uint Color(int x,int y,int z)=>colors[(x+1)+18*((y+1)+18*(z+1))];
    }
    // Immutable output; arrays transfer exclusively from the private worker.
    // Success compacts to populated elements; no uncharged oversized backing arrays remain.
    public sealed class MeshData
    {
        readonly Float3[] vertices,normals;readonly Rgba32[] colors;readonly int[] indices;
        public readonly int VertexCount,IndexCount;public readonly string Identity;public readonly MeshBounds Bounds;
        public ReadOnlySpan<Float3> Vertices=>new ReadOnlySpan<Float3>(vertices,0,VertexCount);
        public ReadOnlySpan<Float3> Normals=>new ReadOnlySpan<Float3>(normals,0,VertexCount);
        public ReadOnlySpan<Rgba32> Colors=>new ReadOnlySpan<Rgba32>(colors,0,VertexCount);
        public ReadOnlySpan<int> Indices=>new ReadOnlySpan<int>(indices,0,IndexCount);
        public long PayloadBytes=>28L*VertexCount+4L*IndexCount;
        public long RetainedBufferBytes=>28L*vertices.Length+4L*indices.Length;
        internal MeshData(string identity,Float3[] v,Float3[] n,Rgba32[] c,int[] t,int count,int triangles)
        {Identity=identity;vertices=v;normals=n;colors=c;indices=t;VertexCount=count;IndexCount=triangles;Bounds=new MeshBounds(v,count);}
    }
    public enum BuildStatus{Complete,Cancelled,Stale,CapacityRejected,Failed}
    public sealed class MeshAttempt
    {
        public readonly BuildStatus Status;public readonly MeshData Output;public readonly string Failure;
        public readonly int CompletedMaskPlanes;
        internal MeshAttempt(BuildStatus status,MeshData output,string failure,int planes){Status=status;Output=output;Failure=failure;CompletedMaskPlanes=planes;}
    }
}
