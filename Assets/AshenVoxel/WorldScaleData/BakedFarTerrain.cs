using System;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;

namespace AshenVoxel.WorldScale
{
    /// <summary>Immutable derived far-height cache. Uncovered samples carry no height.
    /// V2 uses half-metre int32 units to preserve the supported source height domain.</summary>
    public sealed class BakedFarTerrain
    {
        public const string Magic="AVFAR002";
        public const double HeightUnitMeters=.5;
        public readonly int Columns,Rows;
        public readonly double OriginX,OriginZ,SpacingMeters;
        public readonly string BaseIdentity,SourceFingerprint,Identity;
        readonly int[] heights;
        readonly byte[] coverage;
        public long PayloadBytes=>heights.LongLength*4+coverage.LongLength;
        internal BakedFarTerrain(int columns,int rows,double x,double z,double spacing,string basis,string source,string identity,int[] h,byte[] c)
        {Columns=columns;Rows=rows;OriginX=x;OriginZ=z;SpacingMeters=spacing;BaseIdentity=basis;SourceFingerprint=source;Identity=identity;heights=h;coverage=c;}
        public double Sample(double x,double z,out double weight)
        {
            Numeric.Finite(x,nameof(x));Numeric.Finite(z,nameof(z));
            double px=(x-OriginX)/SpacingMeters,pz=(z-OriginZ)/SpacingMeters;weight=0;
            if(!(px>=0&&pz>=0&&px<=Columns-1&&pz<=Rows-1))return 0;
            int ix=Math.Min(Columns-2,(int)px),iz=Math.Min(Rows-2,(int)pz),a=ix+iz*Columns;
            double fx=px-ix,fz=pz-iz;
            double wa=coverage[a]*(1-fx)*(1-fz),wb=coverage[a+1]*fx*(1-fz),wc=coverage[a+Columns]*(1-fx)*fz,wd=coverage[a+Columns+1]*fx*fz;
            double total=wa+wb+wc+wd;weight=total/255;
            return total>0?(heights[a]*wa+heights[a+1]*wb+heights[a+Columns]*wc+heights[a+Columns+1]*wd)*HeightUnitMeters/total:0;
        }
    }

    /// <summary>Bounded, integrity-checked derived cache; AVFAR001 was never shipped.
    /// Read requires a seekable file or memory stream containing one complete cache.
    /// Re-bake old/unknown versions. No savegame migration is implied.</summary>
    public static class BakedFarTerrainIO
    {
        public const long MaximumUncompressedBytes=160L*1024*1024;
        public const int MaximumSamples=32000000,MaximumIdentityBytes=16384;
        static readonly Encoding Utf8=new UTF8Encoding(false,true);
        static readonly uint[] CrcTable=CreateCrcTable();
        static uint AddCrc(uint crc,byte[] bytes,int count)
        {for(int i=0;i<count;i++)crc=CrcTable[(crc^bytes[i])&255]^(crc>>8);return crc;}
        static uint[] CreateCrcTable()
        {var t=new uint[256];for(uint i=0;i<256;i++){uint c=i;for(int k=0;k<8;k++)c=(c&1)!=0?0xedb88320u^(c>>1):c>>1;t[i]=c;}return t;}
        sealed class Integrity:IDisposable
        {
            readonly SHA256 sha=SHA256.Create();uint crc=0xffffffffu;
            public void Add(byte[] b,int n){sha.TransformBlock(b,0,n,null,0);crc=AddCrc(crc,b,n);}
            public byte[] Finish(){sha.TransformFinalBlock(Array.Empty<byte>(),0,0);return sha.Hash;}
            public uint Crc=>crc^0xffffffffu;
            public void Dispose()=>sha.Dispose();
        }
        static bool Finite(double x)=>!double.IsNaN(x)&&!double.IsInfinity(x);
        static int Validate(int columns,int rows,double x,double z,double spacing)
        {
            long n=(long)columns*rows;
            if(columns<2||rows<2||columns>16384||rows>16384||n>MaximumSamples||n*5+MaximumIdentityBytes+128>MaximumUncompressedBytes)
                throw new InvalidDataException("Far terrain dimensions/size limit");
            if(!Finite(x)||!Finite(z)||!Finite(spacing)||Math.Abs(x)>1e12||Math.Abs(z)>1e12||spacing<=0||spacing*(columns-1)>1e9||spacing*(rows-1)>1e9)
                throw new InvalidDataException("Far terrain grid bounds");
            return (int)n;
        }
        static void ValidateSamples(int[] h,byte[] c,int n)
        {
            if(h==null||c==null||h.Length!=n||c.Length!=n)throw new InvalidDataException("Far terrain payload length");
            for(int i=0;i<n;i++)if(h[i]< -24000||h[i]>96000||(c[i]==0&&h[i]!=0))throw new InvalidDataException("Far terrain height/coverage domain");
        }
        static byte[] Header(int columns,int rows,double x,double z,double spacing,string basis,byte[] source)
        {
            byte[] identity=Utf8.GetBytes(basis??"");
            if(identity.Length==0||identity.Length>MaximumIdentityBytes||source==null||source.Length!=32)throw new InvalidDataException("Far terrain identity bounds");
            using var ms=new MemoryStream();using(var w=new BinaryWriter(ms,Utf8,true))
            {w.Write(Encoding.ASCII.GetBytes(BakedFarTerrain.Magic));w.Write(columns);w.Write(rows);w.Write(x);w.Write(z);w.Write(spacing);w.Write(BakedFarTerrain.HeightUnitMeters);w.Write(identity.Length);w.Write(identity);w.Write(source);w.Write((long)columns*rows*5);}
            return ms.ToArray();
        }
        public static void Write(Stream output,int columns,int rows,double x,double z,double spacing,string basis,byte[] sourceFingerprint,int[] heights,byte[] coverage)
        {
            int n=Validate(columns,rows,x,z,spacing);ValidateSamples(heights,coverage,n);byte[] header=Header(columns,rows,x,z,spacing,basis,sourceFingerprint);
            using var gz=new GZipStream(output,CompressionLevel.Optimal,true);using var w=new BinaryWriter(gz,Utf8,true);using var integrity=new Integrity();
            void Put(byte[] bytes,int count){w.Write(bytes,0,count);integrity.Add(bytes,count);}
            Put(header,header.Length);var buffer=new byte[65536];
            for(int offset=0;offset<n;)
            {
                int count=Math.Min(buffer.Length/4,n-offset);Buffer.BlockCopy(heights,offset*4,buffer,0,count*4);
                if(!BitConverter.IsLittleEndian)for(int k=0;k<count*4;k+=4)Array.Reverse(buffer,k,4);
                Put(buffer,count*4);offset+=count;
            }
            for(int offset=0;offset<n;){int count=Math.Min(buffer.Length,n-offset);Buffer.BlockCopy(coverage,offset,buffer,0,count);Put(buffer,count);offset+=count;}
            w.Write(integrity.Finish());w.Write(integrity.Crc);
        }
        static byte[] Exact(BinaryReader r,int count)
        {var bytes=r.ReadBytes(count);if(bytes.Length!=count)throw new EndOfStreamException("Truncated far terrain");return bytes;}
        public static BakedFarTerrain Read(Stream input)
        {
            // Mono GZipStream can accept a truncated footer. All cache callers use
            // bounded files or memory streams; verify the footer independently.
            if(input==null||!input.CanRead||!input.CanSeek)throw new ArgumentException("Readable seekable far terrain stream required");
            long start=input.Position,compressedBytes=input.Length-start;
            if(compressedBytes<18||compressedBytes>MaximumUncompressedBytes+1024*1024)throw new InvalidDataException("Far terrain compressed size limit");
            input.Seek(-8,SeekOrigin.End);uint gzipCrc,gzipLength;
            using(var footer=new BinaryReader(input,Utf8,true)){gzipCrc=footer.ReadUInt32();gzipLength=footer.ReadUInt32();}
            input.Position=start;
            using var gz=new GZipStream(input,CompressionMode.Decompress,true);using var r=new BinaryReader(gz,Utf8,true);using var integrity=new Integrity();
            if(Encoding.ASCII.GetString(Exact(r,8))!=BakedFarTerrain.Magic)throw new InvalidDataException("Unsupported far terrain format; re-bake AVFAR002");
            int columns=r.ReadInt32(),rows=r.ReadInt32();double x=r.ReadDouble(),z=r.ReadDouble(),spacing=r.ReadDouble(),unit=r.ReadDouble();
            int n=Validate(columns,rows,x,z,spacing);if(unit!=BakedFarTerrain.HeightUnitMeters)throw new InvalidDataException("Far terrain quantization");
            int identityLength=r.ReadInt32();if(identityLength<1||identityLength>MaximumIdentityBytes)throw new InvalidDataException("Far terrain identity length");
            string basis=Utf8.GetString(Exact(r,identityLength));byte[] source=Exact(r,32);
            if(r.ReadInt64()!=(long)n*5)throw new InvalidDataException("Far terrain declared payload length");
            byte[] header=Header(columns,rows,x,z,spacing,basis,source);integrity.Add(header,header.Length);
            var h=new int[n];var c=new byte[n];var buffer=new byte[65536];
            void ReadBlock(int count)
            {int got=0;while(got<count){int read=r.Read(buffer,got,count-got);if(read==0)throw new EndOfStreamException("Truncated far terrain payload");got+=read;}integrity.Add(buffer,count);}
            for(int offset=0;offset<n;)
            {
                int count=Math.Min(buffer.Length/4,n-offset);ReadBlock(count*4);
                if(!BitConverter.IsLittleEndian)for(int k=0;k<count*4;k+=4)Array.Reverse(buffer,k,4);
                Buffer.BlockCopy(buffer,0,h,offset*4,count*4);offset+=count;
            }
            for(int offset=0;offset<n;){int count=Math.Min(buffer.Length,n-offset);ReadBlock(count);Buffer.BlockCopy(buffer,0,c,offset,count);offset+=count;}
            byte[] expected=Exact(r,32),actual=integrity.Finish();int mismatch=0;for(int i=0;i<32;i++)mismatch|=expected[i]^actual[i];
            uint payloadCrc=r.ReadUInt32();
            if(mismatch!=0||payloadCrc!=integrity.Crc)throw new InvalidDataException("Far terrain header/payload checksum mismatch");
            if(gz.ReadByte()!=-1)throw new InvalidDataException("Extra far terrain decoded bytes");
            byte[] crcBytes=BitConverter.GetBytes(payloadCrc);if(!BitConverter.IsLittleEndian)Array.Reverse(crcBytes);
            uint completeCrc=AddCrc(AddCrc(~payloadCrc,expected,expected.Length),crcBytes,crcBytes.Length)^0xffffffffu;
            if(gzipCrc!=completeCrc||gzipLength!=header.Length+(long)n*5+36)throw new InvalidDataException("Far terrain gzip footer mismatch/truncation");
            ValidateSamples(h,c,n);
            return new BakedFarTerrain(columns,rows,x,z,spacing,basis,Hex(source),"baked-far-v2/"+Hex(actual),h,c);
        }
        static string Hex(byte[] bytes)=>BitConverter.ToString(bytes).Replace("-","").ToLowerInvariant();
    }
}
