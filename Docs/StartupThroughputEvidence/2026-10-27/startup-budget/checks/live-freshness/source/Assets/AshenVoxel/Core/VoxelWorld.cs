using System;
using System.Collections.Generic;
namespace AshenVoxel.Core
{
    public sealed class Chunk
    {
        public const int Size=16, Volume=Size*Size*Size;
        public readonly Int3 Coordinate;
        internal readonly MaterialId[] Data = new MaterialId[Volume];
        /// <summary>Allocation-free read-only material view. Mutations must go through VoxelWorld.Set.</summary>
        public ReadOnlySpan<MaterialId> Cells=>Data;
        internal uint[] TintData;
        internal int TintCount;
        public ReadOnlySpan<uint> Tints=>TintData;
        public bool HasTints=>TintData!=null;
        public uint GetTint(int x,int y,int z)=>TintData==null?0:TintData[Index(x,y,z)];
        public int NonAirCount { get; internal set; }
        public Chunk(Int3 coordinate) { Coordinate=coordinate; }
        public static int Index(int x,int y,int z) => x + Size*(y+Size*z);
        public static Int3 Decode(int index) => new Int3(index%Size,(index/Size)%Size,index/(Size*Size));
        public MaterialId Get(int x,int y,int z) => Data[Index(x,y,z)];
    }
    /// <summary>Finite simulation domain backed by lazily allocated 16^3 material chunks. Outside is air. Brushes and gravity preserve y=0 as the floor.</summary>
    public sealed class VoxelWorld
    {
        private readonly Dictionary<Int3,Chunk> chunks = new Dictionary<Int3,Chunk>();
        public IReadOnlyDictionary<Int3,Chunk> Chunks => chunks;
        public HashSet<Int3> DirtyChunks { get; } = new HashSet<Int3>();
        public int SizeX { get; } public int SizeY { get; } public int SizeZ { get; }
        public float VoxelSize { get; } public int Seed { get; }
        /// <summary>Provenance of generated cells; snapshots preserve this without regenerating historical worlds.</summary>
        public int GenerationVersion { get; internal set; }
        public long Revision { get; private set; }
        public event Action<Int3> Changed;
        /// <summary>Tint-only edit notification; these edits dirty meshes without waking material physics.</summary>
        public event Action<Int3> AppearanceChanged;
        public VoxelWorld(int sizeX,int sizeY,int sizeZ,float voxelSize,int seed)
        {
            if(sizeX<1 || sizeY<1 || sizeZ<1 || sizeX>4096 || sizeY>4096 || sizeZ>4096) throw new ArgumentOutOfRangeException("World dimensions must be 1..4096.");
            if(float.IsNaN(voxelSize)||float.IsInfinity(voxelSize)||voxelSize<=0) throw new ArgumentOutOfRangeException(nameof(voxelSize));
            SizeX=sizeX;SizeY=sizeY;SizeZ=sizeZ;VoxelSize=voxelSize;Seed=seed;GenerationVersion=Generation.Version;
        }
        public bool InBounds(int x,int y,int z) => x>=0&&y>=0&&z>=0&&x<SizeX&&y<SizeY&&z<SizeZ;
        public bool InBounds(Int3 p) => InBounds(p.x,p.y,p.z);
        public MaterialId Get(Int3 p) => Get(p.x,p.y,p.z);
        public MaterialId Get(int x,int y,int z)
        {
            if(!InBounds(x,y,z)) return MaterialId.Air;
            return chunks.TryGetValue(new Int3(x>>4,y>>4,z>>4),out Chunk c) ? c.Get(x&15,y&15,z&15) : MaterialId.Air;
        }
        public uint GetTint(Int3 p)=>GetTint(p.x,p.y,p.z);
        public uint GetTint(int x,int y,int z)
        {
            if(!InBounds(x,y,z))return 0;
            return chunks.TryGetValue(new Int3(x>>4,y>>4,z>>4),out Chunk c)?c.GetTint(x&15,y&15,z&15):0;
        }
        public uint GetColor(Int3 p)=>GetColor(p.x,p.y,p.z);
        public uint GetColor(int x,int y,int z)
        {
            if(!InBounds(x,y,z)||!chunks.TryGetValue(new Int3(x>>4,y>>4,z>>4),out Chunk c))return 0;
            int index=Chunk.Index(x&15,y&15,z&15);MaterialId material=c.Data[index];
            if(material==MaterialId.Air)return 0;
            uint tint=c.TintData==null?0:c.TintData[index];
            return tint==0?VoxelColor.Base(material):tint;
        }
        public bool Set(Int3 p,MaterialId value)=>Set(p.x,p.y,p.z,value);
        public bool Set(Int3 p,MaterialId value,uint tint)=>Set(p.x,p.y,p.z,value,tint);
        public bool Set(int x,int y,int z,MaterialId value)=>SetInternal(x,y,z,value,0,false);
        public bool Set(int x,int y,int z,MaterialId value,uint tint)=>SetInternal(x,y,z,value,tint,true);
        bool SetInternal(int x,int y,int z,MaterialId value,uint tint,bool explicitTint)
        {
            if(!MaterialCatalog.IsValid(value))throw new ArgumentOutOfRangeException(nameof(value));
            if(!VoxelColor.IsValid(tint))throw new ArgumentOutOfRangeException(nameof(tint),"Tint must be zero or opaque 0xFFRRGGBB.");
            if(!InBounds(x,y,z))return false;
            if(value==MaterialId.Air)tint=0;
            Int3 key=new Int3(x>>4,y>>4,z>>4);
            if(!chunks.TryGetValue(key,out Chunk c))
            {
                if(value==MaterialId.Air)return false;
                c=new Chunk(key);chunks.Add(key,c);
            }
            int index=Chunk.Index(x&15,y&15,z&15);
            MaterialId old=c.Data[index];uint oldTint=c.TintData==null?0:c.TintData[index];
            if(!explicitTint&&old==value)return false; // Material-only no-op preserves appearance.
            if(old==value&&oldTint==tint)return false;
            c.Data[index]=value;
            if(old==MaterialId.Air&&value!=MaterialId.Air)c.NonAirCount++;
            if(value==MaterialId.Air&&old!=MaterialId.Air)c.NonAirCount--;
            if(oldTint!=tint)
            {
                if(tint!=0&&c.TintData==null)c.TintData=new uint[Chunk.Volume];
                if(c.TintData!=null)c.TintData[index]=tint;
                if(oldTint==0)c.TintCount++;
                if(tint==0)c.TintCount--;
                if(c.TintCount==0)c.TintData=null;
            }
            if(c.NonAirCount==0)chunks.Remove(key);
            Revision++;
            // Mesh visibility and vertex AO sample a one-cell halo, including edge/corner neighbors.
            // Cartesian boundary offsets invalidate precisely the 1, 2, 4 or 8 affected chunks.
            int xmin=(x&15)==0?-1:0,xmax=(x&15)==15?1:0;
            int ymin=(y&15)==0?-1:0,ymax=(y&15)==15?1:0;
            int zmin=(z&15)==0?-1:0,zmax=(z&15)==15?1:0;
            for(int dz=zmin;dz<=zmax;dz++)for(int dy=ymin;dy<=ymax;dy++)for(int dx=xmin;dx<=xmax;dx++)
                MarkDirty(new Int3(key.x+dx,key.y+dy,key.z+dz));
            if(old!=value)Changed?.Invoke(new Int3(x,y,z));
            else AppearanceChanged?.Invoke(new Int3(x,y,z));
            return true;
        }
        private void MarkDirty(Int3 c)
        { if(c.x>=0&&c.y>=0&&c.z>=0&&c.x*16<SizeX&&c.y*16<SizeY&&c.z*16<SizeZ) DirtyChunks.Add(c); }
        public static int FloorDiv(int n,int divisor)
        { if(divisor<=0)throw new ArgumentOutOfRangeException(nameof(divisor)); int q=n/divisor; return n%divisor<0?q-1:q; }
        public void ClearDirty() => DirtyChunks.Clear();
        public void Clear()
        {
            foreach(var key in chunks.Keys) DirtyChunks.Add(key);
            chunks.Clear(); Revision++;
            Cleared?.Invoke();
        }
        public event Action Cleared;
    }
}
