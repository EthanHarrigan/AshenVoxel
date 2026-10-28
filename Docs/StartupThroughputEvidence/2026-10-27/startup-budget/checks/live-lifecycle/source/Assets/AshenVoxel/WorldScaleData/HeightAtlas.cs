using System;
using System.IO;
using System.Text;
namespace AshenVoxel.WorldScale
{
    /// <summary>Immutable, bounded source atlas. Sampling extent changes do not rewrite its data.</summary>
    public sealed class HeightAtlas
    {
        public const int MaximumSamples=4194304;
        public readonly int Columns,Rows;
        public readonly double OriginX,OriginZ,ExtentX,ExtentZ;
        public readonly string SourceIdentity;
        readonly float[] heights;readonly byte[] biomes;readonly bool[] land;
        const int BlockSize=32;
        readonly float[] blockMinimum,blockMaximum;
        readonly int blockColumns,blockRows;
        public HeightAtlas(int columns,int rows,double originX,double originZ,double extentX,double extentZ,float[] heights,byte[] biomes,bool[] land,string sourceIdentity)
        {
            if(columns<2||rows<2||(long)columns*rows>MaximumSamples)throw new ArgumentOutOfRangeException(nameof(columns));
            Numeric.Finite(originX,nameof(originX));Numeric.Finite(originZ,nameof(originZ));Numeric.Positive(extentX,nameof(extentX));Numeric.Positive(extentZ,nameof(extentZ));
            int count=columns*rows;if(heights==null||biomes==null||land==null||heights.Length!=count||biomes.Length!=count||land.Length!=count)throw new ArgumentException("Atlas channel lengths differ from dimensions.");
            if(string.IsNullOrWhiteSpace(sourceIdentity)||Encoding.UTF8.GetByteCount(sourceIdentity)>4096)throw new ArgumentException("Atlas identity must contain 1..4096 UTF-8 bytes.",nameof(sourceIdentity));
            for(int i=0;i<count;i++)if(float.IsNaN(heights[i])||float.IsInfinity(heights[i])||Math.Abs(heights[i])>1000000)throw new ArgumentException("Atlas heights must be finite meters within +/-1000000.");
            Columns=columns;Rows=rows;OriginX=originX;OriginZ=originZ;ExtentX=extentX;ExtentZ=extentZ;SourceIdentity=sourceIdentity;
            this.heights=(float[])heights.Clone();this.biomes=(byte[])biomes.Clone();this.land=(bool[])land.Clone();
            blockColumns=(Columns+BlockSize-1)/BlockSize;blockRows=(Rows+BlockSize-1)/BlockSize;
            blockMinimum=new float[blockColumns*blockRows];blockMaximum=new float[blockMinimum.Length];
            for(int i=0;i<blockMinimum.Length;i++){blockMinimum[i]=float.MaxValue;blockMaximum[i]=float.MinValue;}
            for(int z=0;z<Rows;z++)for(int x=0;x<Columns;x++)
            {int block=z/BlockSize*blockColumns+x/BlockSize;float h=this.heights[z*Columns+x];blockMinimum[block]=Math.Min(blockMinimum[block],h);blockMaximum[block]=Math.Max(blockMaximum[block],h);}
        }
        public HeightBounds QueryBounds(int minimumX,int minimumZ,int maximumX,int maximumZ)
        {
            if(minimumX<0||minimumZ<0||maximumX>=Columns||maximumZ>=Rows||minimumX>maximumX||minimumZ>maximumZ)throw new ArgumentOutOfRangeException();
            double lo=double.MaxValue,hi=double.MinValue;
            for(int bz=minimumZ/BlockSize;bz<=maximumZ/BlockSize;bz++)for(int bx=minimumX/BlockSize;bx<=maximumX/BlockSize;bx++)
            {
                int ax=bx*BlockSize,az=bz*BlockSize,ex=Math.Min(Columns-1,ax+BlockSize-1),ez=Math.Min(Rows-1,az+BlockSize-1);
                if(minimumX<=ax&&maximumX>=ex&&minimumZ<=az&&maximumZ>=ez)
                {int index=bz*blockColumns+bx;lo=Math.Min(lo,blockMinimum[index]);hi=Math.Max(hi,blockMaximum[index]);}
                else for(int z=Math.Max(az,minimumZ);z<=Math.Min(ez,maximumZ);z++)for(int x=Math.Max(ax,minimumX);x<=Math.Min(ex,maximumX);x++)
                {float h=heights[z*Columns+x];lo=Math.Min(lo,h);hi=Math.Max(hi,h);}
            }
            return new HeightBounds(lo,hi);
        }
        int Index(int x,int z){if(x<0||z<0||x>=Columns||z>=Rows)throw new ArgumentOutOfRangeException();return z*Columns+x;}
        public float HeightAt(int x,int z)=>heights[Index(x,z)];
        public byte BiomeAt(int x,int z)=>biomes[Index(x,z)];
        public bool LandAt(int x,int z)=>land[Index(x,z)];
    }
    public static class AtlasIO
    {
        static readonly byte[] Magic=Encoding.ASCII.GetBytes("AVWF0001");
        const int MaximumBytes=HeightAtlas.MaximumSamples*6+4200;
        public static void Write(Stream destination,HeightAtlas atlas)
        {
            if(destination==null||!destination.CanWrite)throw new ArgumentException("Writable destination required.",nameof(destination));
            if(atlas==null)throw new ArgumentNullException(nameof(atlas));
            using(var buffer=new MemoryStream())
            {
                using(var writer=new BinaryWriter(buffer,Encoding.UTF8,true))
                {
                    writer.Write(Magic);writer.Write(atlas.Columns);writer.Write(atlas.Rows);
                    writer.Write(atlas.OriginX);writer.Write(atlas.OriginZ);writer.Write(atlas.ExtentX);writer.Write(atlas.ExtentZ);
                    byte[] id=Encoding.UTF8.GetBytes(atlas.SourceIdentity);writer.Write((ushort)id.Length);writer.Write(id);
                    for(int z=0;z<atlas.Rows;z++)for(int x=0;x<atlas.Columns;x++)
                    {writer.Write(atlas.HeightAt(x,z));writer.Write(atlas.BiomeAt(x,z));writer.Write((byte)(atlas.LandAt(x,z)?1:0));}
                }
                byte[] raw=buffer.ToArray();destination.Write(raw,0,raw.Length);
                uint crc=Crc32(raw,raw.Length);for(int shift=0;shift<32;shift+=8)destination.WriteByte((byte)(crc>>shift));
            }
        }
        public static HeightAtlas Read(Stream source)
        {
            if(source==null||!source.CanRead)throw new ArgumentException("Readable source required.",nameof(source));
            using(var buffer=new MemoryStream())
            {
                byte[] block=new byte[8192];int n;
                while((n=source.Read(block,0,block.Length))>0)
                {if(buffer.Length+n>MaximumBytes)throw new InvalidDataException("Atlas exceeds bounded payload.");buffer.Write(block,0,n);}
                byte[] raw=buffer.ToArray();if(raw.Length<58)throw new InvalidDataException("Truncated atlas.");
                uint stored=(uint)(raw[raw.Length-4]|raw[raw.Length-3]<<8|raw[raw.Length-2]<<16|raw[raw.Length-1]<<24);
                if(Crc32(raw,raw.Length-4)!=stored)throw new InvalidDataException("Atlas checksum mismatch.");
                try
                {
                    using(var reader=new BinaryReader(new MemoryStream(raw,0,raw.Length-4,false),new UTF8Encoding(false,true)))
                    {
                        for(int i=0;i<Magic.Length;i++)if(reader.ReadByte()!=Magic[i])throw new InvalidDataException("Unsupported atlas format.");
                        int columns=reader.ReadInt32(),rows=reader.ReadInt32();
                        if(columns<2||rows<2||(long)columns*rows>HeightAtlas.MaximumSamples)throw new InvalidDataException("Invalid atlas dimensions.");
                        double ox=reader.ReadDouble(),oz=reader.ReadDouble(),ex=reader.ReadDouble(),ez=reader.ReadDouble();
                        int size=reader.ReadUInt16();if(size<1||size>4096)throw new InvalidDataException("Invalid source identity length.");
                        byte[] id=reader.ReadBytes(size);if(id.Length!=size)throw new InvalidDataException("Truncated identity.");
                        string identity=new UTF8Encoding(false,true).GetString(id);
                        int count=columns*rows;
                        if(raw.Length!=50L+size+count*6L+4)throw new InvalidDataException("Atlas size does not match dimensions.");
                        var h=new float[count];var b=new byte[count];var land=new bool[count];
                        for(int i=0;i<count;i++){h[i]=reader.ReadSingle();b[i]=reader.ReadByte();byte mask=reader.ReadByte();if(mask>1)throw new InvalidDataException("Invalid land mask.");land[i]=mask==1;}
                        return new HeightAtlas(columns,rows,ox,oz,ex,ez,h,b,land,identity);
                    }
                }
                catch(ArgumentException e){throw new InvalidDataException("Invalid atlas metadata or channels.",e);}
                catch(EndOfStreamException e){throw new InvalidDataException("Truncated atlas payload.",e);}
            }
        }
        public static uint Crc32(byte[] bytes,int length)
        {if(bytes==null||length<0||length>bytes.Length)throw new ArgumentOutOfRangeException(nameof(length));uint crc=0xffffffff;for(int i=0;i<length;i++){crc^=bytes[i];for(int bit=0;bit<8;bit++)crc=(crc>>1)^((crc&1)!=0?0xedb88320u:0);}return ~crc;}
    }
}
