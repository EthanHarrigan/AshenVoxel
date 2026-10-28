using System;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
namespace AshenVoxel.WorldScale
{
    /// <summary>Pre-baked whole-world landform reshape (Tools/TerrainReshape): elevation remap of
    /// the broad mountain mass, talus aprons under cliffs and hydraulic gullies. Stored as one
    /// additive height delta on a regular grid plus coarse flow/talus masks for materials.
    /// Immutable after load; sampling is a bilinear lookup with no allocation.</summary>
    public sealed class TerrainReshapeAtlas
    {
        public const string Magic="AVRS0001";
        public const double DeltaUnitMeters=.25;
        const int Tile=32;
        public readonly int Columns,Rows,AuxColumns,AuxRows,AuxDivisor;
        public readonly double OriginX,OriginZ,SpacingMeters;
        public readonly string Parameters,Identity;
        public readonly double MinimumDelta,MaximumDelta;
        readonly short[] delta;readonly byte[] flow,talus;readonly float[] tileMin,tileMax;readonly int tileColumns,tileRows;
        public long PayloadBytes=>delta.LongLength*2+flow.LongLength+talus.LongLength;

        public TerrainReshapeAtlas(int columns,int rows,double originX,double originZ,double spacing,int auxDivisor,short[] delta,byte[] flow,byte[] talus,string parameters)
        {
            if(columns<2||rows<2||columns>20000||rows>20000)throw new ArgumentOutOfRangeException(nameof(columns));
            Numeric.Finite(originX,nameof(originX));Numeric.Finite(originZ,nameof(originZ));Numeric.Positive(spacing,nameof(spacing));
            if(auxDivisor<1||auxDivisor>16)throw new ArgumentOutOfRangeException(nameof(auxDivisor));
            Columns=columns;Rows=rows;OriginX=originX;OriginZ=originZ;SpacingMeters=spacing;AuxDivisor=auxDivisor;
            AuxColumns=(columns-1)/auxDivisor+1;AuxRows=(rows-1)/auxDivisor+1;
            if(delta==null||delta.Length!=columns*rows)throw new ArgumentException("Delta grid size");
            if(flow==null||flow.Length!=AuxColumns*AuxRows||talus==null||talus.Length!=flow.Length)throw new ArgumentException("Mask grid size");
            this.delta=delta;this.flow=flow;this.talus=talus;Parameters=parameters??"";
            tileColumns=(columns+Tile-1)/Tile;tileRows=(rows+Tile-1)/Tile;tileMin=new float[tileColumns*tileRows];tileMax=new float[tileColumns*tileRows];
            double lo=double.MaxValue,hi=double.MinValue;
            for(int tz=0;tz<tileRows;tz++)for(int tx=0;tx<tileColumns;tx++)
            {
                // Tiles overlap their neighbours by one sample so bilinear values between tiles are covered.
                int x0=tx*Tile,z0=tz*Tile,x1=Math.Min(columns-1,x0+Tile),z1=Math.Min(rows-1,z0+Tile);short a=short.MaxValue,b=short.MinValue;
                for(int z=z0;z<=z1;z++){int row=z*columns;for(int x=x0;x<=x1;x++){short v=delta[row+x];if(v<a)a=v;if(v>b)b=v;}}
                tileMin[tx+tz*tileColumns]=(float)(a*DeltaUnitMeters);tileMax[tx+tz*tileColumns]=(float)(b*DeltaUnitMeters);lo=Math.Min(lo,a*DeltaUnitMeters);hi=Math.Max(hi,b*DeltaUnitMeters);
            }
            MinimumDelta=lo;MaximumDelta=hi;
            using var sha=SHA256.Create();var head=Encoding.UTF8.GetBytes(Magic+"|"+Columns+"|"+Rows+"|"+OriginX.ToString("R",CultureInfo.InvariantCulture)+"|"+OriginZ.ToString("R",CultureInfo.InvariantCulture)+"|"+SpacingMeters.ToString("R",CultureInfo.InvariantCulture)+"|"+AuxDivisor+"|"+Parameters);
            sha.TransformBlock(head,0,head.Length,null,0);var raw=new byte[delta.Length*2];Buffer.BlockCopy(delta,0,raw,0,raw.Length);sha.TransformBlock(raw,0,raw.Length,null,0);
            sha.TransformBlock(flow,0,flow.Length,null,0);sha.TransformFinalBlock(talus,0,talus.Length);
            var hex=new StringBuilder(64);foreach(byte v in sha.Hash)hex.Append(v.ToString("x2",CultureInfo.InvariantCulture));Identity="terrain-reshape-v1/"+hex;
        }
        /// <summary>Additive height change in metres at a world position (0 outside the baked domain).</summary>
        public double Delta(double x,double z)
        {
            double px=(x-OriginX)/SpacingMeters,pz=(z-OriginZ)/SpacingMeters;
            if(!(px>=0&&pz>=0&&px<=Columns-1&&pz<=Rows-1))return 0;
            int ix=Math.Min(Columns-2,(int)px),iz=Math.Min(Rows-2,(int)pz);double fx=px-ix,fz=pz-iz;int a=ix+iz*Columns;
            return ((delta[a]*(1-fx)+delta[a+1]*fx)*(1-fz)+(delta[a+Columns]*(1-fx)+delta[a+Columns+1]*fx)*fz)*DeltaUnitMeters;
        }
        double Aux(byte[] values,double x,double z)
        {
            double s=SpacingMeters*AuxDivisor,px=(x-OriginX)/s,pz=(z-OriginZ)/s;
            if(!(px>=0&&pz>=0&&px<=AuxColumns-1&&pz<=AuxRows-1))return 0;
            int ix=Math.Min(AuxColumns-2,(int)px),iz=Math.Min(AuxRows-2,(int)pz);double fx=px-ix,fz=pz-iz;int a=ix+iz*AuxColumns;
            return ((values[a]*(1-fx)+values[a+1]*fx)*(1-fz)+(values[a+AuxColumns]*(1-fx)+values[a+AuxColumns+1]*fx)*fz)/255.0;
        }
        /// <summary>0..1 drainage intensity (log of upstream droplet passes): gullies and couloirs.</summary>
        public double Flow(double x,double z)=>Aux(flow,x,z);
        /// <summary>0..1 fresh talus/alluvial deposit thickness: scree aprons and fans.</summary>
        public double Talus(double x,double z)=>Aux(talus,x,z);
        /// <summary>Conservative delta range over a world rectangle.</summary>
        public void DeltaBounds(double x0,double z0,double x1,double z1,out double lo,out double hi)
        {
            double t=SpacingMeters*Tile;
            int ax=(int)Math.Floor((x0-OriginX)/t),bx=(int)Math.Floor((x1-OriginX)/t),az=(int)Math.Floor((z0-OriginZ)/t),bz=(int)Math.Floor((z1-OriginZ)/t);
            lo=0;hi=0;// outside the domain the delta is 0
            if(bx<0||bz<0||ax>=tileColumns||az>=tileRows)return;
            bool outside=ax<0||az<0||bx>=tileColumns||bz>=tileRows;
            ax=Math.Max(0,ax);az=Math.Max(0,az);bx=Math.Min(tileColumns-1,bx);bz=Math.Min(tileRows-1,bz);
            if((long)(bx-ax+1)*(bz-az+1)>65536){lo=Math.Min(0,MinimumDelta);hi=Math.Max(0,MaximumDelta);return;}
            double a=double.MaxValue,b=double.MinValue;
            for(int z=az;z<=bz;z++)for(int x=ax;x<=bx;x++){int i=x+z*tileColumns;a=Math.Min(a,tileMin[i]);b=Math.Max(b,tileMax[i]);}
            lo=outside?Math.Min(0,a):a;hi=outside?Math.Max(0,b):b;
        }
    }

    public static class TerrainReshapeIO
    {
        public const long MaximumUncompressedBytes=512L*1024*1024;
        public static TerrainReshapeAtlas Read(Stream input)
        {
            using var gz=new GZipStream(input,CompressionMode.Decompress,true);using var r=new BinaryReader(gz,Encoding.UTF8,true);
            if(Encoding.ASCII.GetString(r.ReadBytes(8))!=TerrainReshapeAtlas.Magic)throw new InvalidDataException("Not a terrain reshape atlas");
            int cols=r.ReadInt32(),rows=r.ReadInt32();double ox=r.ReadDouble(),oz=r.ReadDouble(),spacing=r.ReadDouble();int aux=r.ReadInt32();string parameters=r.ReadString();
            if(cols<2||rows<2||cols>20000||rows>20000||aux<1||aux>16||(long)cols*rows*4>MaximumUncompressedBytes)throw new InvalidDataException("Reshape dimensions");
            int auxCols=(cols-1)/aux+1,auxRows=(rows-1)/aux+1;
            var delta=new short[cols*rows];var raw=ReadExactly(r,delta.Length*2);Buffer.BlockCopy(raw,0,delta,0,raw.Length);
            var flow=ReadExactly(r,auxCols*auxRows);var talus=ReadExactly(r,auxCols*auxRows);
            uint expected=r.ReadUInt32();uint crc=Crc(raw,Crc(flow,Crc(talus,0xffffffffu)));if((crc^0xffffffffu)!=expected)throw new InvalidDataException("Reshape checksum mismatch");
            return new TerrainReshapeAtlas(cols,rows,ox,oz,spacing,aux,delta,flow,talus,parameters);
        }
        public static void Write(Stream output,int cols,int rows,double ox,double oz,double spacing,int aux,short[] delta,byte[] flow,byte[] talus,string parameters)
        {
            using var gz=new GZipStream(output,CompressionLevel.Optimal,true);using var w=new BinaryWriter(gz,Encoding.UTF8,true);
            w.Write(Encoding.ASCII.GetBytes(TerrainReshapeAtlas.Magic));w.Write(cols);w.Write(rows);w.Write(ox);w.Write(oz);w.Write(spacing);w.Write(aux);w.Write(parameters??"");
            var raw=new byte[delta.Length*2];Buffer.BlockCopy(delta,0,raw,0,raw.Length);w.Write(raw);w.Write(flow);w.Write(talus);
            w.Write(Crc(raw,Crc(flow,Crc(talus,0xffffffffu)))^0xffffffffu);
        }
        static byte[] ReadExactly(BinaryReader r,int count){var b=r.ReadBytes(count);if(b.Length!=count)throw new EndOfStreamException("Truncated reshape atlas");return b;}
        static uint[] table;
        static uint Crc(byte[] data,uint crc)
        {
            var t=table;if(t==null){t=new uint[256];for(uint i=0;i<256;i++){uint c=i;for(int k=0;k<8;k++)c=(c&1)!=0?0xedb88320u^(c>>1):c>>1;t[i]=c;}table=t;}
            foreach(byte b in data)crc=t[(crc^b)&255]^(crc>>8);return crc;
        }
    }

    /// <summary>Applies the baked reshape on top of every height source (baked atlas, overview
    /// windows and decoded 5 m patches) so collision, rendering, population and LODs agree.
    /// Climate follows the new height through the source lapse rate, so lowered mountain
    /// flanks warm up and the treeline/snowline move with the landform.</summary>
    public sealed class TerrainReshapeField:IWorldFieldBounds,IClimateWorldField,IWorldFieldSnapshotProvider
    {
        readonly IWorldFieldBounds basis;
        public readonly TerrainReshapeAtlas Atlas;
        public IWorldField BaseField=>basis;
        public string SourceIdentity=>basis.SourceIdentity+"/"+Atlas.Identity;
        public TerrainReshapeField(IWorldField source,TerrainReshapeAtlas atlas)
        {
            basis=source as IWorldFieldBounds??throw new ArgumentException("Reshape requires conservative source bounds.",nameof(source));
            Atlas=atlas??throw new ArgumentNullException(nameof(atlas));
        }
        /// <summary>Never moves land below or sea above the shoreline.</summary>
        internal static double Apply(double h,double d)
        {
            double r=h+d;
            if(h>0&&r<.5)r=Math.Min(h,.5);
            else if(h<=0&&r>0)r=h;
            return r;
        }
        public SurfaceSample Sample(double x,double z)
        {
            SurfaceSample a=basis.Sample(x,z);
            return new SurfaceSample(Apply(a.HeightMeters,Atlas.Delta(x,z)),a.SourceBiome,a.EffectiveBiome,a.IsLand);
        }
        public HeightBounds QueryBounds(double x0,double z0,double x1,double z1)
        {
            HeightBounds b=basis.QueryBounds(x0,z0,x1,z1);Atlas.DeltaBounds(x0,z0,x1,z1,out double lo,out double hi);
            // Apply() returns h+d, h, or min(h,.5): all within [min(h+lo,h), max(h+hi,h)].
            return new HeightBounds(Math.Min(b.Minimum+lo,b.Minimum),Math.Max(b.Maximum+hi,b.Maximum));
        }
        public bool TrySampleClimate(double x,double z,out SurfaceClimate climate)
        {
            if(!(basis is IClimateWorldField c)||!c.TrySampleClimate(x,z,out SurfaceClimate a)){climate=default;return false;}
            double h=basis.Sample(x,z).HeightMeters,n=Apply(h,Atlas.Delta(x,z));
            double t=Numeric.Clamp(a.TemperatureC+a.LapseRateCPerMeter*(Math.Max(n,0)-Math.Max(h,0)),-150,100);
            climate=new SurfaceClimate(t,a.SeasonalityC,a.PrecipitationMm,a.PrecipitationCvPercent,a.LapseRateCPerMeter);return true;
        }
        public IWorldField CaptureSnapshot()=>new TerrainReshapeField(basis is IWorldFieldSnapshotProvider p?p.CaptureSnapshot():basis,Atlas);
        /// <summary>Finds the reshape atlas below known wrappers (surface relief), or null.</summary>
        public static TerrainReshapeAtlas Find(IWorldField field)
        {
            for(int i=0;i<8&&field!=null;i++)
            {
                if(field is TerrainReshapeField r)return r.Atlas;
                if(field is TerrainDetailField d){field=d.BaseField;continue;}
                if(field is SurfaceWorldField s){field=s.BaseField;continue;}
                break;
            }
            return null;
        }
    }
}
