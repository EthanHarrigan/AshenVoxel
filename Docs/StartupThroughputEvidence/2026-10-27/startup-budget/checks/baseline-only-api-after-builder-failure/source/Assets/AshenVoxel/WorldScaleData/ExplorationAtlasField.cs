using System;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Security.Cryptography;
namespace AshenVoxel.WorldScale
{
    /// <summary>Immutable world-space blend of actual learned coarse and decoded fine
    /// bakes. Weight depends only on position, never the observer or frame.</summary>
    public sealed class ExplorationAtlasField : IWorldFieldBounds,IClimateWorldField
    {
        public readonly HeightAtlas Macro,Fine;
        public readonly ClimateAtlas MacroClimate,FineClimate;
        public readonly ExplorationBiomeAtlas BiomeAtlas;
        public readonly double WorldWidthMeters,WorldDepthMeters,VerticalScale,FineBlendMeters;
        public readonly double OriginalWidthMeters,OriginalDepthMeters;
        public string WorldId {get;}
        public string SourceIdentity {get;}
        public bool HasDecodedDetail=>Fine!=null;
        public double FineMinimumX=>Fine==null?0:Fine.OriginX*WorldWidthMeters/OriginalWidthMeters;
        public double FineMaximumX=>Fine==null?0:(Fine.OriginX+Fine.ExtentX)*WorldWidthMeters/OriginalWidthMeters;
        public double FineMinimumZ=>Fine==null?0:Fine.OriginZ*WorldDepthMeters/OriginalDepthMeters;
        public double FineMaximumZ=>Fine==null?0:(Fine.OriginZ+Fine.ExtentZ)*WorldDepthMeters/OriginalDepthMeters;
        public ExplorationAtlasField(HeightAtlas macro,ClimateAtlas macroClimate,HeightAtlas fine=null,ClimateAtlas fineClimate=null,
            double fineBlendMeters=512,double worldWidthMeters=299520,double worldDepthMeters=149760,double verticalScale=1,
            ExplorationBiomeAtlas biomeAtlas=null,double originalWidthMeters=299520,double originalDepthMeters=149760)
        {
            Macro=macro??throw new ArgumentNullException(nameof(macro));MacroClimate=macroClimate??throw new ArgumentNullException(nameof(macroClimate));
            if((fine==null)!=(fineClimate==null))throw new ArgumentException("Decoded height and climate must be supplied together.");
            Numeric.Positive(worldWidthMeters,nameof(worldWidthMeters));Numeric.Positive(worldDepthMeters,nameof(worldDepthMeters));
            Numeric.Positive(originalWidthMeters,nameof(originalWidthMeters));Numeric.Positive(originalDepthMeters,nameof(originalDepthMeters));
            Numeric.Positive(fineBlendMeters,nameof(fineBlendMeters));Numeric.Positive(verticalScale,nameof(verticalScale));
            if(worldWidthMeters>1e9||worldDepthMeters>1e9||originalWidthMeters>1e9||originalDepthMeters>1e9||verticalScale>16)throw new ArgumentOutOfRangeException("Exploration field scale");
            if(macro.OriginX> -originalWidthMeters*.5+1e-6||macro.OriginZ> -originalDepthMeters*.5+1e-6||macro.OriginX+macro.ExtentX<originalWidthMeters*.5-1e-6||macro.OriginZ+macro.ExtentZ<originalDepthMeters*.5-1e-6)throw new ArgumentException("Macro archive must cover the declared centered original map domain.");
            Pair(macro,macroClimate);WorldId=ReadWorldId(macro.SourceIdentity);
            if(ReadWorldId(macroClimate.SourceIdentity)!=WorldId)throw new ArgumentException("Macro climate has a different model world identity.");
            if(fine!=null)
            {
                Pair(fine,fineClimate);if(ReadWorldId(fine.SourceIdentity)!=WorldId||ReadWorldId(fineClimate.SourceIdentity)!=WorldId)throw new ArgumentException("Decoded detail belongs to another model world.");
                if(fine.OriginX<macro.OriginX||fine.OriginZ<macro.OriginZ||fine.OriginX+fine.ExtentX>macro.OriginX+macro.ExtentX||fine.OriginZ+fine.ExtentZ>macro.OriginZ+macro.ExtentZ)throw new ArgumentException("Decoded detail must be inside the macro coverage.");
            }
            if(biomeAtlas!=null&&biomeAtlas.WorldId!=WorldId)throw new ArgumentException("Source biome atlas belongs to a different model world.");
            Fine=fine;FineClimate=fineClimate;BiomeAtlas=biomeAtlas;FineBlendMeters=fineBlendMeters;WorldWidthMeters=worldWidthMeters;WorldDepthMeters=worldDepthMeters;VerticalScale=verticalScale;
            OriginalWidthMeters=originalWidthMeters;OriginalDepthMeters=originalDepthMeters;
            var inv=CultureInfo.InvariantCulture;
            SourceIdentity="exploration-blend-v1/"+WorldId+"/macro="+macroClimate.HeightAtlasSha256+"/fine="+(fineClimate==null?"none":fineClimate.HeightAtlasSha256)+
                "/width="+worldWidthMeters.ToString("R",inv)+"/depth="+worldDepthMeters.ToString("R",inv)+"/height="+verticalScale.ToString("R",inv)+"/blend="+fineBlendMeters.ToString("R",inv)+
                "/source-width="+originalWidthMeters.ToString("R",inv)+"/source-depth="+originalDepthMeters.ToString("R",inv)+"/biomes="+(biomeAtlas==null?"atlas-node-fallback":biomeAtlas.Identity);
        }
        static string ReadWorldId(string identity)
        {
            Match m=Regex.Match(identity,"\\\"world_id\\\"\\s*:\\s*\\\"([0-9a-f]{64})\\\"");
            if(!m.Success)throw new ArgumentException("A checked baked model world_id is required.");return m.Groups[1].Value;
        }
        static void Pair(HeightAtlas h,ClimateAtlas c)
        {
            if(h.Columns!=c.Columns||h.Rows!=c.Rows||h.OriginX!=c.OriginX||h.OriginZ!=c.OriginZ||h.ExtentX!=c.ExtentX||h.ExtentZ!=c.ExtentZ||ClimateArchiveIO.HeightHash(h)!=c.HeightAtlasSha256)throw new ArgumentException("Climate is not paired to this exact exploration height archive.");
        }
        bool World(double x,double z,out double sx,out double sz)
        {
            Numeric.Finite(x,nameof(x));Numeric.Finite(z,nameof(z));sx=x*OriginalWidthMeters/WorldWidthMeters;sz=z*OriginalDepthMeters/WorldDepthMeters;
            return x>=-WorldWidthMeters*.5&&x<=WorldWidthMeters*.5&&z>=-WorldDepthMeters*.5&&z<=WorldDepthMeters*.5;
        }
        public double FineWeightAt(double x,double z)
        {if(!World(x,z,out double sx,out double sz))return 0;return Weight(sx,sz);}
        double Weight(double sx,double sz)
        {
            if(Fine==null)return 0;
            double edgeX=Math.Min(sx-Fine.OriginX,Fine.OriginX+Fine.ExtentX-sx)*WorldWidthMeters/OriginalWidthMeters;
            double edgeZ=Math.Min(sz-Fine.OriginZ,Fine.OriginZ+Fine.ExtentZ-sz)*WorldDepthMeters/OriginalDepthMeters;
            double t=Numeric.Clamp(Math.Min(edgeX,edgeZ)/FineBlendMeters,0,1);return t*t*(3-2*t);
        }
        public double ModelResolutionMeters(double x,double z)
        {
            double macro=Math.Max(Macro.ExtentX/(Macro.Columns-1)*WorldWidthMeters/OriginalWidthMeters,Macro.ExtentZ/(Macro.Rows-1)*WorldDepthMeters/OriginalDepthMeters);
            if(Fine==null||FineWeightAt(x,z)<=0)return macro;
            return Math.Max(Fine.ExtentX/(Fine.Columns-1)*WorldWidthMeters/OriginalWidthMeters,Fine.ExtentZ/(Fine.Rows-1)*WorldDepthMeters/OriginalDepthMeters);
        }
        public SurfaceSample Sample(double x,double z)
        {
            if(!World(x,z,out double sx,out double sz))return new SurfaceSample(-1000*VerticalScale,0,0,false);
            SurfaceSample a=SampleAtlas(Macro,sx,sz);double w=Weight(sx,sz),height=a.HeightMeters;byte biome=a.SourceBiome;
            if(w>0){SurfaceSample b=SampleAtlas(Fine,sx,sz);height=height*(1-w)+b.HeightMeters*w;if(w>=.5)biome=b.SourceBiome;}
            if(BiomeAtlas!=null)biome=BiomeAtlas.Sample(sx,sz);
            height*=VerticalScale;return new SurfaceSample(height,biome,biome,height>=0);
        }
        static SurfaceSample SampleAtlas(HeightAtlas h,double x,double z)
        {
            Grid(h,x,z,out int ix,out int iz,out int xx,out int zz,out double fx,out double fz);
            double value=(h.HeightAt(ix,iz)*(1-fx)+h.HeightAt(xx,iz)*fx)*(1-fz)+(h.HeightAt(ix,zz)*(1-fx)+h.HeightAt(xx,zz)*fx)*fz;
            int nx=fx<.5?ix:xx,nz=fz<.5?iz:zz;byte biome=h.BiomeAt(nx,nz);return new SurfaceSample(value,biome,biome,value>=0);
        }
        static void Grid(HeightAtlas h,double x,double z,out int ix,out int iz,out int xx,out int zz,out double fx,out double fz)
        {
            double px=Numeric.Clamp((x-h.OriginX)/h.ExtentX,0,1)*(h.Columns-1),pz=Numeric.Clamp((z-h.OriginZ)/h.ExtentZ,0,1)*(h.Rows-1);
            ix=(int)Math.Floor(px);iz=(int)Math.Floor(pz);xx=Math.Min(ix+1,h.Columns-1);zz=Math.Min(iz+1,h.Rows-1);fx=px-ix;fz=pz-iz;
        }
        static SurfaceClimate ClimateAt(HeightAtlas h,ClimateAtlas c,double x,double z)
        {
            Grid(h,x,z,out int ix,out int iz,out int xx,out int zz,out double fx,out double fz);
            double Channel(int ch)=>(c.Value(ch,ix+iz*c.Columns)*(1-fx)+c.Value(ch,xx+iz*c.Columns)*fx)*(1-fz)+(c.Value(ch,ix+zz*c.Columns)*(1-fx)+c.Value(ch,xx+zz*c.Columns)*fx)*fz;
            return new SurfaceClimate(Channel(0),Channel(1),Channel(2),Channel(3),Channel(4));
        }
        public bool TrySampleClimate(double x,double z,out SurfaceClimate climate)
        {
            if(!World(x,z,out double sx,out double sz)){climate=default;return false;}
            SurfaceSample a=SampleAtlas(Macro,sx,sz);SurfaceClimate ca=ClimateAt(Macro,MacroClimate,sx,sz);double w=Weight(sx,sz);
            double height=a.HeightMeters;SurfaceSample b=default;SurfaceClimate cb=default;
            if(w>0){b=SampleAtlas(Fine,sx,sz);cb=ClimateAt(Fine,FineClimate,sx,sz);height=height*(1-w)+b.HeightMeters*w;}
            height*=VerticalScale;
            double ta=ca.TemperatureC+ca.LapseRateCPerMeter*(Math.Max(height,0)-Math.Max(a.HeightMeters,0));
            double temperature=ta,season=ca.SeasonalityC,rain=ca.PrecipitationMm,cv=ca.PrecipitationCvPercent,beta=ca.LapseRateCPerMeter;
            if(w>0)
            {
                double tb=cb.TemperatureC+cb.LapseRateCPerMeter*(Math.Max(height,0)-Math.Max(b.HeightMeters,0));temperature=ta*(1-w)+tb*w;
                season=season*(1-w)+cb.SeasonalityC*w;rain=rain*(1-w)+cb.PrecipitationMm*w;cv=cv*(1-w)+cb.PrecipitationCvPercent*w;beta=beta*(1-w)+cb.LapseRateCPerMeter*w;
            }
            if(temperature< -150||temperature>100){climate=default;return false;}
            climate=new SurfaceClimate(temperature,season,rain,cv,beta);return true;
        }
        public HeightBounds QueryBounds(double minimumX,double minimumZ,double maximumX,double maximumZ)
        {
            Numeric.Finite(minimumX,nameof(minimumX));Numeric.Finite(minimumZ,nameof(minimumZ));Numeric.Finite(maximumX,nameof(maximumX));Numeric.Finite(maximumZ,nameof(maximumZ));
            if(minimumX>maximumX||minimumZ>maximumZ)throw new ArgumentException("Inverted exploration bounds.");
            double lo=double.MaxValue,hi=double.MinValue;
            void Include(HeightAtlas h)
            {
                double x0=minimumX*OriginalWidthMeters/WorldWidthMeters,z0=minimumZ*OriginalDepthMeters/WorldDepthMeters,x1=maximumX*OriginalWidthMeters/WorldWidthMeters,z1=maximumZ*OriginalDepthMeters/WorldDepthMeters;
                if(x1<h.OriginX||z1<h.OriginZ||x0>h.OriginX+h.ExtentX||z0>h.OriginZ+h.ExtentZ)return;
                int ax=(int)Math.Floor(Numeric.Clamp((x0-h.OriginX)/h.ExtentX,0,1)*(h.Columns-1)),az=(int)Math.Floor(Numeric.Clamp((z0-h.OriginZ)/h.ExtentZ,0,1)*(h.Rows-1));
                int bx=(int)Math.Ceiling(Numeric.Clamp((x1-h.OriginX)/h.ExtentX,0,1)*(h.Columns-1)),bz=(int)Math.Ceiling(Numeric.Clamp((z1-h.OriginZ)/h.ExtentZ,0,1)*(h.Rows-1));
                HeightBounds b=h.QueryBounds(ax,az,bx,bz);lo=Math.Min(lo,b.Minimum*VerticalScale);hi=Math.Max(hi,b.Maximum*VerticalScale);
            }
            Include(Macro);if(Fine!=null)Include(Fine);
            if(minimumX< -WorldWidthMeters*.5||maximumX>WorldWidthMeters*.5||minimumZ< -WorldDepthMeters*.5||maximumZ>WorldDepthMeters*.5||lo==double.MaxValue){lo=Math.Min(lo,-1000*VerticalScale);hi=Math.Max(hi,-1000*VerticalScale);}
            double roundingMargin=Math.Max(1e-7,Math.Max(Math.Abs(lo),Math.Abs(hi))*1e-12);
            return new HeightBounds(lo-roundingMargin,hi+roundingMargin);
        }
    }
    /// <summary>Cell-centered imported biome categories, independent of height-node interpolation.</summary>
    public sealed class ExplorationBiomeAtlas
    {
        public readonly int Columns,Rows;public readonly double OriginX,OriginZ,SpacingMeters;public readonly string WorldId,Identity,DataHash;readonly byte[] values;
        public ExplorationBiomeAtlas(int columns,int rows,double originX,double originZ,double spacingMeters,string worldId,byte[] data)
        {
            if(columns<1||rows<1||(long)columns*rows>4194304||data==null||data.Length!=columns*rows)throw new ArgumentException("Invalid source biome dimensions.");
            Numeric.Finite(originX,nameof(originX));Numeric.Finite(originZ,nameof(originZ));Numeric.Positive(spacingMeters,nameof(spacingMeters));
            if(!ClimateArchiveIO.IsHash(worldId))throw new ArgumentException("Source biome world identity required.");
            Columns=columns;Rows=rows;OriginX=originX;OriginZ=originZ;SpacingMeters=spacingMeters;WorldId=worldId.ToLowerInvariant();values=(byte[])data.Clone();
            using(var hash=SHA256.Create()){var text=new StringBuilder(64);foreach(byte value in hash.ComputeHash(values))text.Append(value.ToString("x2"));DataHash=text.ToString();}
            Identity="biome-cells-v1/"+WorldId+"/"+DataHash+"/"+columns+"/"+rows+"/"+originX.ToString("R",CultureInfo.InvariantCulture)+"/"+originZ.ToString("R",CultureInfo.InvariantCulture)+"/"+spacingMeters.ToString("R",CultureInfo.InvariantCulture);
        }
        public byte Sample(double x,double z){Numeric.Finite(x,nameof(x));Numeric.Finite(z,nameof(z));double px=Math.Floor((x-OriginX)/SpacingMeters),pz=Math.Floor((z-OriginZ)/SpacingMeters);if(px<0||px>=Columns||pz<0||pz>=Rows)return 0;return values[(int)px+(int)pz*Columns];}
        internal byte At(int i)=>values[i];
    }
    public static class ExplorationBiomeAtlasIO
    {
        public static ExplorationBiomeAtlas Read(Stream source)
        {
            if(source==null||!source.CanRead)throw new ArgumentException("Readable biome archive required.");
            using(var buffer=new MemoryStream())
            {
                var block=new byte[8192];int n;while((n=source.Read(block,0,block.Length))>0){if(buffer.Length+n>4194400)throw new InvalidDataException("Source biome payload exceeds budget.");buffer.Write(block,0,n);}byte[] raw=buffer.ToArray();
                if(raw.Length<109)throw new InvalidDataException("Truncated source biome archive.");uint stored=(uint)(raw[raw.Length-4]|raw[raw.Length-3]<<8|raw[raw.Length-2]<<16|raw[raw.Length-1]<<24);if(AtlasIO.Crc32(raw,raw.Length-4)!=stored)throw new InvalidDataException("Source biome CRC mismatch.");
                using(var r=new BinaryReader(new MemoryStream(raw,0,raw.Length-4,false),Encoding.UTF8))
                {
                    if(Encoding.ASCII.GetString(r.ReadBytes(8))!="AVBI0001")throw new InvalidDataException("Unknown source biome archive.");int cols=r.ReadInt32(),rows=r.ReadInt32();double x=r.ReadDouble(),z=r.ReadDouble(),spacing=r.ReadDouble();string id=Encoding.ASCII.GetString(r.ReadBytes(64));
                    if(cols<1||rows<1||(long)cols*rows>4194304||raw.Length!=108L+cols*rows)throw new InvalidDataException("Invalid source biome payload length.");
                    return new ExplorationBiomeAtlas(cols,rows,x,z,spacing,id,r.ReadBytes(cols*rows));
                }
            }
        }
    }
}
