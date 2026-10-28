using System;
using System.IO;
using System.Text;
using System.Security.Cryptography;
namespace AshenVoxel.WorldScale
{
    /// <summary>Immutable paired physical-unit climate planes. Temperature is already
    /// evaluated at the original height surface; seasonality is Celsius, never centi-C.</summary>
    public sealed class ClimateAtlas
    {
        public readonly int Columns,Rows;
        public readonly double OriginX,OriginZ,ExtentX,ExtentZ;
        public readonly string HeightAtlasSha256,SourceIdentity;
        readonly float[] values;
        internal int Count=>Columns*Rows;
        public ClimateAtlas(int columns,int rows,double originX,double originZ,double extentX,double extentZ,
            string heightAtlasSha256,string sourceIdentity,float[] physicalPlanes)
        {
            if(columns<2||rows<2||(long)columns*rows>HeightAtlas.MaximumSamples)throw new ArgumentOutOfRangeException(nameof(columns));
            Numeric.Finite(originX,nameof(originX));Numeric.Finite(originZ,nameof(originZ));Numeric.Positive(extentX,nameof(extentX));Numeric.Positive(extentZ,nameof(extentZ));
            if(!ClimateArchiveIO.IsHash(heightAtlasSha256))throw new ArgumentException("Paired height SHA256 is required.");
            if(string.IsNullOrWhiteSpace(sourceIdentity)||Encoding.UTF8.GetByteCount(sourceIdentity)>4096)throw new ArgumentException("Climate identity must contain1..4096 UTF8 bytes.");
            int count=columns*rows;
            if(physicalPlanes==null||physicalPlanes.Length!=count*5)throw new ArgumentException("Five matching climate planes are required.");
            for(int i=0;i<count;i++)new SurfaceClimate(physicalPlanes[i],physicalPlanes[count+i],physicalPlanes[count*2+i],physicalPlanes[count*3+i],physicalPlanes[count*4+i]);
            Columns=columns;Rows=rows;OriginX=originX;OriginZ=originZ;ExtentX=extentX;ExtentZ=extentZ;
            HeightAtlasSha256=heightAtlasSha256.ToLowerInvariant();SourceIdentity=sourceIdentity;values=(float[])physicalPlanes.Clone();
        }
        internal float Value(int channel,int index)=>values[channel*Count+index];
        public SurfaceClimate ClimateAt(int x,int z)
        {
            if(x<0||z<0||x>=Columns||z>=Rows)throw new ArgumentOutOfRangeException();int i=x+z*Columns;
            return new SurfaceClimate(Value(0,i),Value(1,i),Value(2,i),Value(3,i),Value(4,i));
        }
    }
    public static class ClimateArchiveIO
    {
        static readonly byte[] Magic=Encoding.ASCII.GetBytes("AVCL0001");
        const int MaximumBytes=HeightAtlas.MaximumSamples*20+4200;
        public static bool IsHash(string s)
        {if(s==null||s.Length!=64)return false;foreach(char c in s)if(!((c>='0'&&c<='9')||(c>='a'&&c<='f')||(c>='A'&&c<='F')))return false;return true;}
        public static string HeightHash(HeightAtlas atlas)
        {
            using(var buffer=new MemoryStream())using(var sha=SHA256.Create())
            {AtlasIO.Write(buffer,atlas);byte[] digest=sha.ComputeHash(buffer.ToArray());var text=new StringBuilder(64);foreach(byte b in digest)text.Append(b.ToString("x2"));return text.ToString();}
        }
        public static void Write(Stream destination,ClimateAtlas atlas)
        {
            if(destination==null||!destination.CanWrite)throw new ArgumentException("Writable destination required.");if(atlas==null)throw new ArgumentNullException(nameof(atlas));
            using(var buffer=new MemoryStream())
            {
                using(var writer=new BinaryWriter(buffer,Encoding.UTF8,true))
                {
                    writer.Write(Magic);writer.Write(atlas.Columns);writer.Write(atlas.Rows);writer.Write(atlas.OriginX);writer.Write(atlas.OriginZ);writer.Write(atlas.ExtentX);writer.Write(atlas.ExtentZ);
                    for(int i=0;i<32;i++)writer.Write(Convert.ToByte(atlas.HeightAtlasSha256.Substring(i*2,2),16));
                    byte[] identity=Encoding.UTF8.GetBytes(atlas.SourceIdentity);writer.Write((ushort)identity.Length);writer.Write(identity);
                    for(int c=0;c<5;c++)for(int i=0;i<atlas.Count;i++)writer.Write(atlas.Value(c,i));
                }
                byte[] raw=buffer.ToArray();destination.Write(raw,0,raw.Length);uint crc=AtlasIO.Crc32(raw,raw.Length);
                for(int shift=0;shift<32;shift+=8)destination.WriteByte((byte)(crc>>shift));
            }
        }
        public static ClimateAtlas Read(Stream source)
        {
            if(source==null||!source.CanRead)throw new ArgumentException("Readable source required.");
            using(var buffer=new MemoryStream())
            {
                byte[] block=new byte[8192];int n;while((n=source.Read(block,0,block.Length))>0)
                {if(buffer.Length+n>MaximumBytes)throw new InvalidDataException("Climate archive exceeds its bounded size.");buffer.Write(block,0,n);}
                byte[] raw=buffer.ToArray();if(raw.Length<87)throw new InvalidDataException("Truncated climate archive.");
                uint stored=(uint)(raw[raw.Length-4]|raw[raw.Length-3]<<8|raw[raw.Length-2]<<16|raw[raw.Length-1]<<24);
                if(AtlasIO.Crc32(raw,raw.Length-4)!=stored)throw new InvalidDataException("Climate archive checksum mismatch.");
                try
                {
                    using(var reader=new BinaryReader(new MemoryStream(raw,0,raw.Length-4,false),new UTF8Encoding(false,true)))
                    {
                        for(int i=0;i<8;i++)if(reader.ReadByte()!=Magic[i])throw new InvalidDataException("Unknown climate archive version.");
                        int columns=reader.ReadInt32(),rows=reader.ReadInt32();if(columns<2||rows<2||(long)columns*rows>HeightAtlas.MaximumSamples)throw new InvalidDataException("Invalid climate grid dimensions.");
                        double ox=reader.ReadDouble(),oz=reader.ReadDouble(),ex=reader.ReadDouble(),ez=reader.ReadDouble();
                        var hash=new StringBuilder(64);for(int i=0;i<32;i++)hash.Append(reader.ReadByte().ToString("x2"));
                        int size=reader.ReadUInt16();if(size<1||size>4096||raw.Length!=86L+size+(long)columns*rows*20)throw new InvalidDataException("Climate payload does not match dimensions.");
                        byte[] identity=reader.ReadBytes(size);if(identity.Length!=size)throw new InvalidDataException("Truncated climate identity.");
                        string text=new UTF8Encoding(false,true).GetString(identity);var values=new float[columns*rows*5];for(int i=0;i<values.Length;i++)values[i]=reader.ReadSingle();
                        return new ClimateAtlas(columns,rows,ox,oz,ex,ez,hash.ToString(),text,values);
                    }
                }
                catch(ArgumentException e){throw new InvalidDataException("Invalid physical climate values or metadata.",e);}
                catch(EndOfStreamException e){throw new InvalidDataException("Truncated climate payload.",e);}
            }
        }
    }
    /// <summary>Applies paired measured/model climate to the same centered atlas transform.
    /// Horizontal rescaling preserves alignment. Vertical rescaling applies beta only
    /// to the elevation DIFFERENCE, never twice to the original model temperature.</summary>
    public sealed class ClimateAtlasField : IWorldFieldBounds,IClimateWorldField
    {
        public readonly AtlasWorldField HeightField;
        public readonly ClimateAtlas Climate;
        public string SourceIdentity=>HeightField.SourceIdentity+"/climate-physical-v1/"+Climate.HeightAtlasSha256+"/"+Climate.SourceIdentity;
        public ClimateAtlasField(AtlasWorldField heightField,ClimateAtlas climate)
        {
            HeightField=heightField??throw new ArgumentNullException(nameof(heightField));Climate=climate??throw new ArgumentNullException(nameof(climate));HeightAtlas h=heightField.Atlas;
            if(h.Columns!=climate.Columns||h.Rows!=climate.Rows||h.OriginX!=climate.OriginX||h.OriginZ!=climate.OriginZ||h.ExtentX!=climate.ExtentX||h.ExtentZ!=climate.ExtentZ||ClimateArchiveIO.HeightHash(h)!=climate.HeightAtlasSha256)
                throw new ArgumentException("Climate archive is not paired to this exact height atlas.");
        }
        public SurfaceSample Sample(double x,double z)=>HeightField.Sample(x,z);
        public HeightBounds QueryBounds(double x0,double z0,double x1,double z1)=>HeightField.QueryBounds(x0,z0,x1,z1);
        public bool TrySampleClimate(double x,double z,out SurfaceClimate climate)
        {
            Numeric.Finite(x,nameof(x));Numeric.Finite(z,nameof(z));double u=x/HeightField.Settings.WidthMeters+.5,v=z/HeightField.Settings.DepthMeters+.5;
            if(u<0||u>1||v<0||v>1){climate=default;return false;}
            double px=u*(Climate.Columns-1),pz=v*(Climate.Rows-1);int ix=(int)Math.Floor(px),iz=(int)Math.Floor(pz),xx=Math.Min(ix+1,Climate.Columns-1),zz=Math.Min(iz+1,Climate.Rows-1);double fx=px-ix,fz=pz-iz;
            double Channel(int c)=>(Climate.Value(c,ix+iz*Climate.Columns)*(1-fx)+Climate.Value(c,xx+iz*Climate.Columns)*fx)*(1-fz)+
                (Climate.Value(c,ix+zz*Climate.Columns)*(1-fx)+Climate.Value(c,xx+zz*Climate.Columns)*fx)*fz;
            double beta=Channel(4),height=HeightField.Sample(x,z).HeightMeters;
            double temperature=Channel(0)+beta*(Math.Max(height,0)-Math.Max(height/HeightField.Settings.VerticalScale,0));
            // Extreme fantasy rescaling can leave the physical climate contract;
            // report unavailable so the caller explicitly chooses its fallback.
            if(temperature< -150||temperature>100){climate=default;return false;}
            climate=new SurfaceClimate(temperature,Channel(1),Channel(2),Channel(3),beta);return true;
        }
    }
}
