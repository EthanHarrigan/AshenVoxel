using System;
using AshenVoxel.Core;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Collections.Generic;
namespace AshenVoxel.WorldScale
{
    public sealed class PrototypeMeshData
    {
        public readonly short[] Positions;
        public readonly byte[] Normals;
        public readonly uint[] Colors;
        public readonly int[] Indices;
        public int VertexCount=>Colors.Length;
        internal PrototypeMeshData(short[] p,byte[] n,uint[] c,int[] indices){Positions=p;Normals=n;Colors=c;Indices=indices;}
    }
    public readonly struct PrototypeCollisionColumn
    {
        public readonly short X,Z;
        readonly short[] spans;
        public ReadOnlySpan<short> Spans=>spans;
        internal PrototypeCollisionColumn(short x,short z,short[] spans){X=x;Z=z;this.spans=spans;}
    }
    public sealed class ExplorationPrototype
    {
        public readonly int Id;
        public readonly byte Kind;
        public readonly string Name;
        public readonly float MinX,MinY,MinZ,MaxX,MaxY,MaxZ;
        readonly Dictionary<int,PrototypeCollisionColumn> collision;
        readonly Dictionary<int,PrototypeCollisionColumn> leaves,sticks;
        PrototypeMeshData[] meshes;
        public int LodCount=>meshes==null?0:meshes.Length;
        public PrototypeMeshData Mesh(int lod)=>meshes[lod];
        public int CollisionColumns=>collision.Count;
        public bool HasCollision=>collision.Count>0;
        public float Radius=>Math.Max(Math.Max(Math.Abs(MinX),Math.Abs(MaxX)),Math.Max(Math.Abs(MinZ),Math.Abs(MaxZ)));
        public bool HasResistance=>leaves.Count+sticks.Count>0;
        internal ExplorationPrototype(int id,byte kind,string name,float[] bounds,PrototypeMeshData[] meshes,Dictionary<int,PrototypeCollisionColumn> columns,
            Dictionary<int,PrototypeCollisionColumn> leafColumns=null,Dictionary<int,PrototypeCollisionColumn> stickColumns=null)
        {Id=id;Kind=kind;Name=name;MinX=bounds[0];MinY=bounds[1];MinZ=bounds[2];MaxX=bounds[3];MaxY=bounds[4];MaxZ=bounds[5];this.meshes=meshes;collision=columns;
            leaves=leafColumns??new Dictionary<int,PrototypeCollisionColumn>();sticks=stickColumns??new Dictionary<int,PrototypeCollisionColumn>();}
        /// <summary>Fractional 10cm cells intersecting the body, with sticks weighted
        /// four times leaves. Span intersections avoid scanning every vertical cell.</summary>
        public double ResistanceLocal(double x0,double y0,double z0,double x1,double y1,double z1)
        {
            Query(x0);Query(y0);Query(z0);Query(x1);Query(y1);Query(z1);
            if(x0>=x1||y0>=y1||z0>=z1||x1-x0>20||y1-y0>20||z1-z0>20)throw new ArgumentOutOfRangeException("resistance body bounds");
            return Overlap(leaves,x0,y0,z0,x1,y1,z1)+4*Overlap(sticks,x0,y0,z0,x1,y1,z1);
        }
        static double Overlap(Dictionary<int,PrototypeCollisionColumn> source,double x0,double y0,double z0,double x1,double y1,double z1)
        {
            double volume=0;
            int ix0=(int)Math.Floor(x0*10),ix1=(int)Math.Ceiling(x1*10)-1,iz0=(int)Math.Floor(z0*10),iz1=(int)Math.Ceiling(z1*10)-1;
            for(int z=iz0;z<=iz1;z++)for(int x=ix0;x<=ix1;x++)if(source.TryGetValue(Key(x,z),out PrototypeCollisionColumn column))
            {
                double area=(Math.Min(x1,(x+1)*.1)-Math.Max(x0,x*.1))*(Math.Min(z1,(z+1)*.1)-Math.Max(z0,z*.1));
                ReadOnlySpan<short> spans=column.Spans;
                for(int i=0;i<spans.Length;i+=2)volume+=area*Math.Max(0,Math.Min(y1,spans[i+1]*.1)-Math.Max(y0,spans[i]*.1));
            }
            return volume*1000;
        }
        /// <summary>Unity meshes are replaceable caches; release decoded geometry after uploading.
        /// Collision columns remain immutable exact0.1m material spans.</summary>
        public void ReleaseMeshData(){meshes=null;}
        static int Key(int x,int z)=>unchecked((x&65535)|z<<16);
        public bool BlocksLocal(double minX,double minY,double minZ,double maxX,double maxY,double maxZ)
        {
            Query(minX);Query(minY);Query(minZ);Query(maxX);Query(maxY);Query(maxZ);
            if(minX>=maxX||minY>=maxY||minZ>=maxZ)throw new ArgumentException("Prototype body bounds must be positive.");
            int x0=(int)Math.Floor(minX*10),x1=(int)Math.Ceiling(maxX*10)-1,z0=(int)Math.Floor(minZ*10),z1=(int)Math.Ceiling(maxZ*10)-1;
            if(x1-x0>1024||z1-z0>1024)throw new ArgumentOutOfRangeException("prototype query extent");
            for(int z=z0;z<=z1;z++)for(int x=x0;x<=x1;x++)if(collision.TryGetValue(Key(x,z),out PrototypeCollisionColumn column))
            {
                ReadOnlySpan<short> spans=column.Spans;
                for(int i=0;i<spans.Length;i+=2)if(maxY>spans[i]*.1+1e-8&&minY<spans[i+1]*.1-1e-8)return true;
            }
            return false;
        }
        static void Query(double v){if(double.IsNaN(v)||double.IsInfinity(v)||Math.Abs(v)>1000)throw new ArgumentOutOfRangeException("prototype local coordinates");}
        public bool SupportLocal(double x,double z,double radius,double minY,double maxY,out double height)
        {
            Query(x);Query(z);Query(minY);Query(maxY);if(double.IsNaN(radius)||double.IsInfinity(radius)||radius<0||radius>20||minY>maxY)throw new ArgumentOutOfRangeException("prototype support query");
            height=double.NegativeInfinity;int x0=(int)Math.Floor((x-radius)*10),x1=(int)Math.Floor((x+radius)*10),z0=(int)Math.Floor((z-radius)*10),z1=(int)Math.Floor((z+radius)*10);
            if(x1-x0>1024||z1-z0>1024)throw new ArgumentOutOfRangeException("prototype query extent");
            for(int iz=z0;iz<=z1;iz++)for(int ix=x0;ix<=x1;ix++)if(collision.TryGetValue(Key(ix,iz),out PrototypeCollisionColumn c))
            {
                double dx=x-Math.Max(ix*.1,Math.Min((ix+1)*.1,x)),dz=z-Math.Max(iz*.1,Math.Min((iz+1)*.1,z));
                if(dx*dx+dz*dz>radius*radius+1e-10)continue;ReadOnlySpan<short> spans=c.Spans;
                for(int i=1;i<spans.Length;i+=2){double top=spans[i]*.1;if(top>=minY-1e-7&&top<=maxY+1e-7&&top>height)height=top;}
            }
            return !double.IsNegativeInfinity(height);
        }
    }
    /// <summary>Checksummed independently compressed model records. Decoding one record per
    /// startup frame bounds peak staging memory; model growth never occurs in play mode.</summary>
    public sealed class ExplorationPrototypeReader:IDisposable
    {
        const int MaxRecordBytes=192*1024*1024,MaxPackBytes=192*1024*1024;
        readonly BinaryReader input;readonly HashSet<int> ids=new HashSet<int>();
        long expanded,decodedVertices,decodedIndices;
        public int FormatVersion {get;}
        public int Count {get;}
        public int ReadCount {get;private set;}
        public ExplorationPrototypeReader(byte[] bytes)
        {
            if(bytes==null||bytes.Length<12||bytes.Length>MaxPackBytes)throw new InvalidDataException("Prototype pack length exceeds bounds.");
            input=new BinaryReader(new MemoryStream(bytes,false));
            string magic=Encoding.ASCII.GetString(input.ReadBytes(8));
            FormatVersion=magic=="AVPM0001"?1:magic=="AVPM0002"?2:magic=="AVPM0003"?3:magic=="AVPM0004"?4:0;
            if(FormatVersion==0)throw new InvalidDataException("Unknown prototype pack format.");
            Count=input.ReadInt32();if(Count<1||Count>128)throw new InvalidDataException("Invalid prototype count.");
        }
        public bool TryRead(out ExplorationPrototype prototype)
        {
            prototype=null;
            if(ReadCount==Count){if(input.BaseStream.Position!=input.BaseStream.Length)throw new InvalidDataException("Trailing prototype data.");return false;}
            int packed=input.ReadInt32(),raw=input.ReadInt32();uint crc=input.ReadUInt32();
            if(packed<1||packed>MaxRecordBytes||raw<24||raw>MaxRecordBytes||packed>input.BaseStream.Length-input.BaseStream.Position||expanded+raw>1024L*1024*1024)throw new InvalidDataException("Prototype record exceeds bounds.");
            byte[] payload=new byte[raw];byte[] compressed=input.ReadBytes(packed);
            using(var gzip=new GZipStream(new MemoryStream(compressed,false),CompressionMode.Decompress))
            {int offset=0;while(offset<raw){int n=gzip.Read(payload,offset,raw-offset);if(n==0)throw new EndOfStreamException("Truncated prototype record.");offset+=n;}if(gzip.ReadByte()!=-1)throw new InvalidDataException("Excess expanded prototype data.");}
            if(Crc(payload)!=crc)throw new InvalidDataException("Prototype checksum mismatch.");
            expanded+=raw;
            using(var r=new BinaryReader(new MemoryStream(payload,false)))
            {
                int id=r.ReadInt32();byte kind=r.ReadByte();int chars=r.ReadUInt16();
                if(id<0||id>999||!ids.Add(id)||kind>2||chars<1||chars>256)throw new InvalidDataException("Invalid model identity.");
                string name=Encoding.UTF8.GetString(r.ReadBytes(chars));var bounds=new float[6];for(int i=0;i<6;i++){bounds[i]=r.ReadSingle();if(float.IsNaN(bounds[i])||float.IsInfinity(bounds[i])||Math.Abs(bounds[i])>250)throw new InvalidDataException("Invalid model bounds.");}
                if(bounds[0]>=bounds[3]||bounds[1]>=bounds[4]||bounds[2]>=bounds[5])throw new InvalidDataException("Empty model bounds.");
                int lods=r.ReadByte();if(lods<1||lods>6)throw new InvalidDataException("Invalid LOD count.");var meshes=new PrototypeMeshData[lods];
                for(int lod=0;lod<lods;lod++)
                {
                    if(FormatVersion>=2){meshes[lod]=ReadQuadMesh(r,bounds);continue;}
                    int count=r.ReadInt32(),indices=r.ReadInt32();if(count<1||count>4000000||indices<3||indices>18000000||indices%3!=0||(long)count*11+(long)indices*4>r.BaseStream.Length-r.BaseStream.Position)throw new InvalidDataException("Invalid model geometry length.");
                    CountDecoded(count,indices);
                    var positions=new short[count*3];var normals=new byte[count];var colors=new uint[count];
                    for(int i=0;i<count;i++){positions[i*3]=r.ReadInt16();positions[i*3+1]=r.ReadInt16();positions[i*3+2]=r.ReadInt16();normals[i]=r.ReadByte();colors[i]=r.ReadUInt32(); colors[i]=(colors[i]&0xffffff)|((uint)VertexAppearance.Legacy((byte)(colors[i]>>24))<<24);
                    // Coarser occupancy proxies can extend by less than their6.4m cell.
                    for(int axis=0;axis<3;axis++){double value=positions[i*3+axis]*.1;if(value<bounds[axis]-6.5||value>bounds[axis+3]+6.5)throw new InvalidDataException("Model vertex exceeds declared conservative bounds.");}
                    if(normals[i]>5)throw new InvalidDataException("Invalid cube normal.");}
                    var triangles=new int[indices];for(int i=0;i<indices;i++){triangles[i]=r.ReadInt32();if(triangles[i]<0||triangles[i]>=count)throw new InvalidDataException("Invalid model index.");}
                    meshes[lod]=new PrototypeMeshData(positions,normals,colors,triangles);
                }
                var collision=ReadColumns(r,bounds);
                var leaves=FormatVersion>=3?ReadColumns(r,bounds):null;
                var sticks=FormatVersion>=3?ReadColumns(r,bounds):null;
                if(r.BaseStream.Position!=r.BaseStream.Length)throw new InvalidDataException("Trailing model data.");
                prototype=new ExplorationPrototype(id,kind,name,bounds,meshes,collision,leaves,sticks);
            }
            ReadCount++;return true;
        }
        static Dictionary<int,PrototypeCollisionColumn> ReadColumns(BinaryReader r,float[] bounds)
        {
                int columns=r.ReadInt32();if(columns<0||columns>2000000||(long)columns*10>r.BaseStream.Length-r.BaseStream.Position)throw new InvalidDataException("Invalid collision columns.");var collision=new Dictionary<int,PrototypeCollisionColumn>(columns);
                for(int i=0;i<columns;i++)
                {
                    short x=r.ReadInt16(),z=r.ReadInt16();int n=r.ReadUInt16();if(n<1||n>1024||n*4>r.BaseStream.Length-r.BaseStream.Position)throw new InvalidDataException("Invalid collision span count.");
                    var spans=new short[n*2];short last=short.MinValue;
                    for(int s=0;s<spans.Length;s+=2){spans[s]=r.ReadInt16();spans[s+1]=r.ReadInt16();if(spans[s]>=spans[s+1]||spans[s]<last)throw new InvalidDataException("Overlapping collision spans.");last=spans[s+1];}
                    if(x*.1<bounds[0]-.11||x*.1>bounds[3]+.01||z*.1<bounds[2]-.11||z*.1>bounds[5]+.01||spans[0]*.1<bounds[1]-.11||spans[spans.Length-1]*.1>bounds[4]+.11)throw new InvalidDataException("Interaction column exceeds model bounds.");
                    int key=unchecked((x&65535)|z<<16);if(collision.ContainsKey(key))throw new InvalidDataException("Duplicate interaction column.");
                    collision.Add(key,new PrototypeCollisionColumn(x,z,spans));
                }
                return collision;
        }
        void CountDecoded(int vertices,int indices)
        {decodedVertices+=vertices;decodedIndices+=indices;if(decodedVertices>20000000||decodedIndices>60000000)throw new InvalidDataException("Prototype decoded geometry budget exceeded.");}
        static uint Var(BinaryReader r)
        {
            uint value=0;
            for(int shift=0;shift<=28;shift+=7){byte b=r.ReadByte();if(shift==28&&(b&240)!=0)throw new InvalidDataException("Prototype varint overflow.");value|=(uint)(b&127)<<shift;if((b&128)==0)return value;}
            throw new InvalidDataException("Prototype varint exceeds32bits.");
        }
        static int Delta(BinaryReader r){uint n=Var(r);return unchecked((int)((n>>1)^(uint)-(int)(n&1)));}
        static readonly int[][] Patterns={new[]{0,1,2,0,2,3},new[]{0,2,1,0,3,2},new[]{0,1,3,1,2,3},new[]{0,3,1,1,3,2}};
        PrototypeMeshData ReadQuadMesh(BinaryReader r,float[] bounds)
        {
            int quads=r.ReadInt32(),paletteCount=r.ReadInt32();
            if(quads<1||quads>1000000||paletteCount<1||paletteCount>65536||(long)quads*8+(long)paletteCount*3>r.BaseStream.Length-r.BaseStream.Position)throw new InvalidDataException("Invalid quad mesh lengths.");
            int count=checked(quads*4),indexCount=checked(quads*6);CountDecoded(count,indexCount);
            var palette=new uint[paletteCount];for(int i=0;i<paletteCount;i++)palette[i]=(uint)(r.ReadByte()|r.ReadByte()<<8|r.ReadByte()<<16);
            var positions=new short[count*3];var normals=new byte[count];var colors=new uint[count];var indices=new int[indexCount];
            var previous=new int[3];
            for(int q=0;q<quads;q++)
            {
                for(int axis=0;axis<3;axis++){long coordinate=(long)previous[axis]+Delta(r);if(coordinate<short.MinValue||coordinate>short.MaxValue)throw new InvalidDataException("Quad coordinate overflow.");previous[axis]=(int)coordinate;}
                uint width=Var(r),height=Var(r);byte flags=r.ReadByte();uint color=Var(r);byte ao=r.ReadByte();
                int material=FormatVersion>=4?r.ReadByte():VertexAppearance.UnknownMaterial;
                if(FormatVersion>=4&&(material<1||material>=MaterialCatalog.Count))throw new InvalidDataException("Invalid quad material.");
                byte normal=(byte)(flags&7);int pattern=(flags>>3)&3;
                if(normal>5||(flags>>5)!=0||width<1||width>32767||height<1||height>32767||color>=paletteCount)throw new InvalidDataException("Invalid quad attributes.");
                int u=(normal/2+1)%3,v=(normal/2+2)%3;
                for(int corner=0;corner<4;corner++)
                {
                    int vertex=q*4+corner;
                    for(int axis=0;axis<3;axis++)
                    {
                        long value=previous[axis]+(axis==u&&(corner==1||corner==2)?width:0)+(axis==v&&(corner==2||corner==3)?height:0);
                        if(value<short.MinValue||value>short.MaxValue||value*.1<bounds[axis]-6.5||value*.1>bounds[axis+3]+6.5)throw new InvalidDataException("Quad vertex exceeds conservative bounds.");
                        positions[vertex*3+axis]=(short)value;
                    }
                    normals[vertex]=normal;uint alpha=(uint)((material<<2)|((ao>>(corner*2))&3));colors[vertex]=palette[color]|alpha<<24;
                }
                for(int i=0;i<6;i++)indices[q*6+i]=q*4+Patterns[pattern][i];
            }
            return new PrototypeMeshData(positions,normals,colors,indices);
        }
        static readonly uint[] CrcTable=MakeCrc();
        static uint[] MakeCrc(){var table=new uint[256];for(int i=0;i<256;i++){uint n=(uint)i;for(int b=0;b<8;b++)n=(n>>1)^(0xedb88320u&(uint)-(int)(n&1));table[i]=n;}return table;}
        public static uint Crc(byte[] bytes){uint value=0xffffffff;foreach(byte b in bytes)value=CrcTable[(value^b)&255]^(value>>8);return ~value;}
        public void Dispose()=>input.Dispose();
    }
}
