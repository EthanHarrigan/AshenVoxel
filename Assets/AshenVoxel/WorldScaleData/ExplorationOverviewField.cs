using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
namespace AshenVoxel.WorldScale
{
    public sealed class ExplorationOverviewPatch
    {
        public const double CoreMeters=2560,BlendMeters=320;
        public readonly long CellX,CellZ;public readonly int SpacingMeters,Columns;
        public readonly string NativeModelWorldId,SourceIdentity;
        public readonly ExplorationRefinementBounds CoreBounds,InfluenceBounds;
        readonly float[] heights;internal readonly double MinimumHeight,MaximumHeight;
        public long PayloadBytes=>heights.LongLength*4;
        public ExplorationOverviewPatch(long cellX,long cellZ,int spacingMeters,float[] values,string nativeId)
        {
            if(spacingMeters!=160&&spacingMeters!=80)throw new ArgumentOutOfRangeException(nameof(spacingMeters));
            if(Math.Abs((double)cellX)>1000000||Math.Abs((double)cellZ)>1000000)throw new ArgumentOutOfRangeException(nameof(cellX));
            if(!ExplorationRefinementPatch.ValidId(nativeId))throw new ArgumentException("Verified native model identity required");
            CellX=cellX;CellZ=cellZ;SpacingMeters=spacingMeters;Columns=(int)((CoreMeters+BlendMeters*2)/spacingMeters)+1;
            if(Columns>65||values==null||values.Length!=Columns*Columns)throw new ArgumentException("Complete canonical overview grid required");
            NativeModelWorldId=nativeId.ToLowerInvariant();heights=(float[])values.Clone();double lo=double.MaxValue,hi=double.MinValue;
            foreach(float h in heights){if(float.IsNaN(h)||float.IsInfinity(h)||h< -12000||h>48000)throw new ArgumentException("Unsupported overview height");lo=Math.Min(lo,h);hi=Math.Max(hi,h);}
            MinimumHeight=lo;MaximumHeight=hi;
            CoreBounds=new ExplorationRefinementBounds(cellX*CoreMeters,cellZ*CoreMeters,(cellX+1)*CoreMeters,(cellZ+1)*CoreMeters);InfluenceBounds=CoreBounds.Expanded(BlendMeters);
            using var buffer=new MemoryStream();using(var w=new BinaryWriter(buffer,Encoding.UTF8,true)){w.Write("overview-height-v1");w.Write(NativeModelWorldId);w.Write(cellX);w.Write(cellZ);w.Write(spacingMeters);foreach(float h in heights)w.Write(h);}
            using var sha=SHA256.Create();SourceIdentity="overview-height-v1/"+Hex(sha.ComputeHash(buffer.ToArray()));
        }
        internal static string Hex(byte[] bytes){var b=new StringBuilder(bytes.Length*2);foreach(byte v in bytes)b.Append(v.ToString("x2",CultureInfo.InvariantCulture));return b.ToString();}
        internal double Height(double x,double z)
        {
            double px=Numeric.Clamp((x-InfluenceBounds.MinimumX)/SpacingMeters,0,Columns-1),pz=Numeric.Clamp((z-InfluenceBounds.MinimumZ)/SpacingMeters,0,Columns-1);
            int ix=Math.Min(Columns-2,(int)Math.Floor(px)),iz=Math.Min(Columns-2,(int)Math.Floor(pz));double fx=px-ix,fz=pz-iz;int a=ix+iz*Columns;
            return (heights[a]*(1-fx)+heights[a+1]*fx)*(1-fz)+(heights[a+Columns]*(1-fx)+heights[a+Columns+1]*fx)*fz;
        }
    }
    public readonly struct ExplorationOverviewPatchInfo
    {
        public readonly long CellX,CellZ,PayloadBytes;public readonly int SpacingMeters;public readonly string SourceIdentity;
        public readonly ExplorationRefinementBounds CoreBounds,InfluenceBounds;
        internal ExplorationOverviewPatchInfo(ExplorationOverviewPatch p){CellX=p.CellX;CellZ=p.CellZ;PayloadBytes=p.PayloadBytes;SpacingMeters=p.SpacingMeters;SourceIdentity=p.SourceIdentity;CoreBounds=p.CoreBounds;InfluenceBounds=p.InfluenceBounds;}
    }
    /// <summary>Height-only, immutable-snapshot overview over the original baked/live
    /// field. Nine cell lookups per sample; no overview population scan.</summary>
    public sealed class ExplorationOverviewField:IWorldFieldBounds,IClimateWorldField,IWorldFieldSnapshotProvider
    {
        public const int MaximumResidentPatches=8192;public const long MaximumResidentHeightBytes=64L*1024*1024;
        public const double ProtectionMeters=2048;
        readonly object gate=new object();readonly ExplorationAtlasField baked;readonly ExplorationRefinementField live;
        readonly string nativeId;State current;
        sealed class State:IWorldFieldBounds,IClimateWorldField
        {
            internal readonly IWorldField Base;internal readonly ExplorationAtlasField Baked;internal readonly ExplorationPublishedPatchInfo[] Fine;
            internal readonly Dictionary<(long,long),ExplorationOverviewPatch> Patches;internal readonly long Bytes;internal readonly int Revision;
            public string SourceIdentity{get;}
            internal State(IWorldField basis,ExplorationAtlasField baked,ExplorationPublishedPatchInfo[] fine,Dictionary<(long,long),ExplorationOverviewPatch> patches,int revision,string native)
            {
                Base=basis;Baked=baked;Fine=(ExplorationPublishedPatchInfo[])fine.Clone();Patches=patches;Revision=revision;long bytes=0;
                var identities=new List<string>();foreach(var p in patches.Values){bytes+=p.PayloadBytes;identities.Add(p.SourceIdentity);}identities.Sort(StringComparer.Ordinal);Bytes=bytes;
                using var data=new MemoryStream();using(var w=new BinaryWriter(data,Encoding.UTF8,true)){w.Write("overview-field-v2");w.Write(native);w.Write(basis.SourceIdentity);w.Write(baked.SourceIdentity);foreach(var id in identities)w.Write(id);}
                using var sha=SHA256.Create();SourceIdentity="overview-field-v2/"+ExplorationOverviewPatch.Hex(sha.ComputeHash(data.ToArray()));
            }
            double FineWeight(double x,double z)
            {
                double total=0;foreach(var p in Fine){double blend=p.CoreBounds.MinimumX-p.InfluenceBounds.MinimumX;if(blend>0)total+=p.CoreBounds.Weight(x,z,blend);}
                return Math.Max(Baked.FineWeightAt(x,z),Math.Min(1,total));
            }
            double Overlay(double x,double z,out double spacing,out double weight)
            {
                long cx=(long)Math.Floor(x/ExplorationOverviewPatch.CoreMeters),cz=(long)Math.Floor(z/ExplorationOverviewPatch.CoreMeters);double sum=0,total=0;spacing=double.MaxValue;
                for(int dz=-1;dz<=1;dz++)for(int dx=-1;dx<=1;dx++)if(Patches.TryGetValue((cx+dx,cz+dz),out var p))
                {double w=p.CoreBounds.Weight(x,z,ExplorationOverviewPatch.BlendMeters);if(w<=0)continue;sum+=p.Height(x,z)*w;total+=w;spacing=Math.Min(spacing,p.SpacingMeters);}
                weight=Math.Min(1,total)*(1-FineWeight(x,z));return total>0?sum/total:0;
            }
            public SurfaceSample Sample(double x,double z)
            {
                Numeric.Finite(x,nameof(x));Numeric.Finite(z,nameof(z));var b=Base.Sample(x,z);double h=Overlay(x,z,out _,out double w);double height=b.HeightMeters+(h-b.HeightMeters)*w;return new SurfaceSample(height,b.SourceBiome,b.EffectiveBiome,height>=0);
            }
            public double Resolution(double x,double z)
            {
                double fine=FineWeight(x,z);if(fine>=.999999)
                {double resolution=Baked.ModelResolutionMeters(x,z);foreach(var p in Fine)if(p.CoreBounds.Contains(x,z))resolution=Math.Min(resolution,p.SpacingMeters);return resolution;}
                Overlay(x,z,out double spacing,out double w);return w>=.999999?spacing:Baked.ModelResolutionMeters(x,z);
            }
            public bool TrySampleClimate(double x,double z,out SurfaceClimate climate)
            {
                if(!(Base is IClimateWorldField source)||!source.TrySampleClimate(x,z,out var b)){climate=default;return false;}
                double oldHeight=Base.Sample(x,z).HeightMeters,newHeight=Sample(x,z).HeightMeters;
                double temperature=b.TemperatureC+b.LapseRateCPerMeter*(Math.Max(newHeight,0)-Math.Max(oldHeight,0));
                temperature=Numeric.Clamp(temperature,-150,100);
                climate=new SurfaceClimate(temperature,b.SeasonalityC,b.PrecipitationMm,b.PrecipitationCvPercent,b.LapseRateCPerMeter);return true;
            }
            public HeightBounds QueryBounds(double x0,double z0,double x1,double z1)
            {
                Numeric.Finite(x0,nameof(x0));Numeric.Finite(z0,nameof(z0));Numeric.Finite(x1,nameof(x1));Numeric.Finite(z1,nameof(z1));
                if(x0>x1||z0>z1)throw new ArgumentException("Inverted query bounds");
                var b=((IWorldFieldBounds)Base).QueryBounds(x0,z0,x1,z1);double lo=b.Minimum,hi=b.Maximum;
                // Include cores whose320m influence touches the rectangle. Small
                // planner queries use cell lookups; huge queries visit residents
                // once, never an unbounded empty world-coordinate rectangle.
                const double core=ExplorationOverviewPatch.CoreMeters,blend=ExplorationOverviewPatch.BlendMeters;
                double ax=Math.Ceiling((x0-blend)/core)-1,bx=Math.Floor((x1+blend)/core),az=Math.Ceiling((z0-blend)/core)-1,bz=Math.Floor((z1+blend)/core);
                double count=(bx-ax+1)*(bz-az+1);
                void Include(ExplorationOverviewPatch p){if(p.InfluenceBounds.MaximumX>=x0&&p.InfluenceBounds.MinimumX<=x1&&p.InfluenceBounds.MaximumZ>=z0&&p.InfluenceBounds.MinimumZ<=z1){lo=Math.Min(lo,p.MinimumHeight);hi=Math.Max(hi,p.MaximumHeight);}}
                if(count>Patches.Count||ax<long.MinValue||bx>=long.MaxValue||az<long.MinValue||bz>=long.MaxValue)
                {foreach(var p in Patches.Values)Include(p);}
                else for(long z=(long)az;z<=(long)bz;z++)for(long x=(long)ax;x<=(long)bx;x++)if(Patches.TryGetValue((x,z),out var p))Include(p);
                return new HeightBounds(lo,hi);
            }
        }
        public ExplorationOverviewField(ExplorationAtlasField baked,ExplorationRefinementField live,string nativeId)
        {
            this.baked=baked??throw new ArgumentNullException(nameof(baked));this.live=live??throw new ArgumentNullException(nameof(live));
            if(!ExplorationRefinementPatch.ValidId(nativeId))throw new ArgumentException("Verified native model identity required");this.nativeId=nativeId.ToLowerInvariant();
            CaptureBase(out var basis,out var fine);current=new State(basis,baked,fine,new Dictionary<(long,long),ExplorationOverviewPatch>(),0,this.nativeId);
        }
        void CaptureBase(out IWorldField basis,out ExplorationPublishedPatchInfo[] fine)
        {
            // Controller serializes publications on its owner thread. The revision
            // check also catches accidental concurrent mutation instead of mixing.
            for(int attempt=0;attempt<8;attempt++){int revision=live.Revision;basis=live.CaptureSnapshot();fine=live.CapturePublishedCoverage();if(fine.Length>32)throw new InvalidOperationException("Overview supports at most32 live fine footprints");if(live.Revision==revision)return;}
            throw new InvalidOperationException("Live source changed throughout overview snapshot capture");
        }
        public string NativeModelWorldId=>nativeId;public string SourceIdentity=>Volatile.Read(ref current).SourceIdentity;
        public int Revision=>Volatile.Read(ref current).Revision;public int ResidentPatches=>Volatile.Read(ref current).Patches.Count;public long ResidentHeightBytes=>Volatile.Read(ref current).Bytes;
        public SurfaceSample Sample(double x,double z)=>Volatile.Read(ref current).Sample(x,z);
        public bool TrySampleClimate(double x,double z,out SurfaceClimate c)=>Volatile.Read(ref current).TrySampleClimate(x,z,out c);
        public HeightBounds QueryBounds(double x0,double z0,double x1,double z1)=>Volatile.Read(ref current).QueryBounds(x0,z0,x1,z1);
        public double ResolutionMetersAt(double x,double z)=>Volatile.Read(ref current).Resolution(x,z);
        public IWorldField CaptureSnapshot()=>Volatile.Read(ref current);
        public void RefreshBaseSnapshot(){lock(gate){CaptureBase(out var basis,out var fine);var old=current;Volatile.Write(ref current,new State(basis,baked,fine,old.Patches,old.Revision+1,nativeId));}}
        public ExplorationOverviewPatchInfo[] CapturePublishedCoverage(){var state=Volatile.Read(ref current);var result=new ExplorationOverviewPatchInfo[state.Patches.Count];int i=0;foreach(var p in state.Patches.Values)result[i++]=new ExplorationOverviewPatchInfo(p);Array.Sort(result,(a,b)=>a.CellX==b.CellX?a.CellZ.CompareTo(b.CellZ):a.CellX.CompareTo(b.CellX));return result;}
        public bool HasCoreCoverage(long x,long z,int maxSpacing)=>Volatile.Read(ref current).Patches.TryGetValue((x,z),out var p)&&p.SpacingMeters<=maxSpacing;
        public bool TryPublish(ExplorationOverviewPatch patch,WorldPoint observer,out ExplorationRefinementBounds changed,out string reason)
        {
            changed=default;if(patch==null){reason="missing patch";return false;}Numeric.Finite(observer.X,nameof(observer));Numeric.Finite(observer.Z,nameof(observer));
            if(patch.NativeModelWorldId!=nativeId){reason="native model identity mismatch";return false;}
            lock(gate)
            {
                var old=current;old.Patches.TryGetValue((patch.CellX,patch.CellZ),out var previous);
                if(previous!=null&&previous.SourceIdentity==patch.SourceIdentity){reason="already current";return true;}
                if(patch.InfluenceBounds.DistanceSquared(observer.X,observer.Z)<=ProtectionMeters*ProtectionMeters){reason="observer protected influence";return false;}
                if(previous!=null&&patch.SpacingMeters>previous.SpacingMeters){reason="coarser replacement refused";return false;}
                long bytes=old.Bytes-(previous?.PayloadBytes??0)+patch.PayloadBytes;
                if(old.Patches.Count+(previous==null?1:0)>MaximumResidentPatches||bytes>MaximumResidentHeightBytes){reason="resident overview capacity";return false;}
                var next=new Dictionary<(long,long),ExplorationOverviewPatch>(old.Patches);next[(patch.CellX,patch.CellZ)]=patch;
                Volatile.Write(ref current,new State(old.Base,baked,old.Fine,next,old.Revision+1,nativeId));changed=patch.InfluenceBounds;reason="published";return true;
            }
        }
        public bool TryRetire(long x,long z,WorldPoint observer,out ExplorationRefinementBounds changed,out string reason)
        {
            changed=default;Numeric.Finite(observer.X,nameof(observer));Numeric.Finite(observer.Z,nameof(observer));lock(gate)
            {
                var old=current;if(!old.Patches.TryGetValue((x,z),out var p)){reason="not resident";return false;}
                if(p.InfluenceBounds.DistanceSquared(observer.X,observer.Z)<=ProtectionMeters*ProtectionMeters){reason="observer protected influence";return false;}
                var next=new Dictionary<(long,long),ExplorationOverviewPatch>(old.Patches);next.Remove((x,z));Volatile.Write(ref current,new State(old.Base,baked,old.Fine,next,old.Revision+1,nativeId));changed=p.InfluenceBounds;reason="retired";return true;
            }
        }
    }
}
