using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;

namespace AshenVoxel.WorldScale
{
    /// <summary>Capture a coherent immutable view before multi-frame or worker work.</summary>
    public interface IWorldFieldSnapshotProvider { IWorldField CaptureSnapshot(); }

    public readonly struct ExplorationRefinementBounds
    {
        public readonly double MinimumX,MinimumZ,MaximumX,MaximumZ;
        public ExplorationRefinementBounds(double minX,double minZ,double maxX,double maxZ)
        {
            Numeric.Finite(minX,nameof(minX));Numeric.Finite(minZ,nameof(minZ));Numeric.Finite(maxX,nameof(maxX));Numeric.Finite(maxZ,nameof(maxZ));
            if(minX>=maxX||minZ>=maxZ)throw new ArgumentException("Refinement bounds must have positive area.");
            MinimumX=minX;MinimumZ=minZ;MaximumX=maxX;MaximumZ=maxZ;
        }
        public bool Contains(double x,double z)=>x>=MinimumX&&x<=MaximumX&&z>=MinimumZ&&z<=MaximumZ;
        public bool Intersects(ExplorationRefinementBounds b)=>MaximumX>=b.MinimumX&&b.MaximumX>=MinimumX&&MaximumZ>=b.MinimumZ&&b.MaximumZ>=MinimumZ;
        public ExplorationRefinementBounds Expanded(double meters)=>new ExplorationRefinementBounds(MinimumX-meters,MinimumZ-meters,MaximumX+meters,MaximumZ+meters);
        public double DistanceSquared(double x,double z)
        {double dx=Math.Max(0,Math.Max(MinimumX-x,x-MaximumX)),dz=Math.Max(0,Math.Max(MinimumZ-z,z-MaximumZ));return dx*dx+dz*dz;}
        public double Weight(double x,double z,double blend)
        {
            double dx=Math.Max(0,Math.Max(MinimumX-x,x-MaximumX)),dz=Math.Max(0,Math.Max(MinimumZ-z,z-MaximumZ));
            double t=Math.Max(0,Math.Min(1,1-Math.Max(dx,dz)/blend));return t*t*(3-2*t);
        }
    }

    /// <summary>Owns an exact regular-grid copy of a checksum-verified immutable
    /// transport snapshot. No evictable HTTP cache is sampled after construction.</summary>
    public sealed class ExplorationRefinementPatch:IWorldFieldBounds,IClimateWorldField
    {
        readonly float[] heights,climate;
        readonly double minimumHeight,maximumHeight;
        public readonly string Key,WorldId;
        public readonly int Columns,Rows;
        public readonly double OriginX,OriginZ,SpacingMeters,BlendMeters;
        public readonly ExplorationRefinementBounds CoreBounds,SampleBounds,InfluenceBounds;
        public long PayloadBytes=>(long)Columns*Rows*24;
        public string SourceIdentity {get;}
        ExplorationRefinementPatch(string key,string worldId,ExplorationRefinementBounds core,double blend,
            double originX,double originZ,int columns,int rows,double spacing,float[] h,float[] c,string sourceIdentity)
        {
            Key=key;WorldId=worldId;CoreBounds=core;BlendMeters=blend;OriginX=originX;OriginZ=originZ;Columns=columns;Rows=rows;SpacingMeters=spacing;
            heights=h;climate=c;SampleBounds=new ExplorationRefinementBounds(originX,originZ,originX+(columns-1)*spacing,originZ+(rows-1)*spacing);
            InfluenceBounds=core.Expanded(blend);minimumHeight=double.MaxValue;maximumHeight=double.MinValue;
            for(int i=0;i<h.Length;i++){minimumHeight=Math.Min(minimumHeight,h[i]);maximumHeight=Math.Max(maximumHeight,h[i]);}
            SourceIdentity=sourceIdentity+"/owned-live-grid/"+key+"/"+columns+"x"+rows+"/"+spacing.ToString("R",CultureInfo.InvariantCulture);
        }
        public static ExplorationRefinementPatch CopySnapshot(IWorldField snapshot,string key,string worldId,
            ExplorationRefinementBounds core,double blendMeters=128,double queryHaloMeters=32,double spacingMeters=5)
        {
            if(snapshot==null)throw new ArgumentNullException(nameof(snapshot));
            if(string.IsNullOrEmpty(key)||!ValidId(worldId)||!(snapshot is IClimateWorldField)||snapshot.SourceIdentity==null||snapshot.SourceIdentity.IndexOf(worldId,StringComparison.Ordinal)<0)
                throw new ArgumentException("A keyed physical-climate snapshot with the matching model identity is required.");
            Numeric.Positive(spacingMeters,nameof(spacingMeters));Numeric.Positive(blendMeters,nameof(blendMeters));Numeric.Finite(queryHaloMeters,nameof(queryHaloMeters));
            if(spacingMeters>10000||blendMeters>2048||queryHaloMeters<0||queryHaloMeters>256)throw new ArgumentOutOfRangeException("Refinement sampling controls");
            double pad=blendMeters+queryHaloMeters;
            long col0=(long)Math.Floor((core.MinimumX-pad)/spacingMeters)-1,row0=(long)Math.Floor((core.MinimumZ-pad)/spacingMeters)-1;
            long cols=(long)Math.Ceiling((core.MaximumX+pad)/spacingMeters)+2-col0,rows=(long)Math.Ceiling((core.MaximumZ+pad)/spacingMeters)+2-row0;
            if(cols<2||rows<2||cols>4096||rows>4096||cols*rows>262144)throw new ArgumentOutOfRangeException("Refinement grid exceeds bounded region samples.");
            int count=checked((int)(cols*rows));var height=new float[count];var climates=new float[count*5];var source=(IClimateWorldField)snapshot;
            double ox=col0*spacingMeters,oz=row0*spacingMeters;
            for(int z=0;z<(int)rows;z++)for(int x=0;x<(int)cols;x++)
            {
                double px=ox+x*spacingMeters,pz=oz+z*spacingMeters;SurfaceSample sample=snapshot.Sample(px,pz);
                if(sample.HeightMeters< -12000||sample.HeightMeters>48000||!source.TrySampleClimate(px,pz,out SurfaceClimate c))throw new ArgumentException("Complete supported physical height/climate snapshot required.");
                int i=x+z*(int)cols;height[i]=(float)sample.HeightMeters;
                climates[i]=(float)c.TemperatureC;climates[count+i]=(float)c.SeasonalityC;climates[count*2+i]=(float)c.PrecipitationMm;
                climates[count*3+i]=(float)c.PrecipitationCvPercent;climates[count*4+i]=(float)c.LapseRateCPerMeter;
            }
            return new ExplorationRefinementPatch(key,worldId,core,blendMeters,ox,oz,(int)cols,(int)rows,spacingMeters,height,climates,snapshot.SourceIdentity);
        }
        internal static bool ValidId(string value)
        {if(value==null||value.Length!=64)return false;foreach(char c in value)if(!(c>='0'&&c<='9'||c>='a'&&c<='f'))return false;return true;}
        double Interpolate(float[] values,int offset,double x,double z)
        {
            if(!SampleBounds.Contains(x,z))throw new ArgumentOutOfRangeException("Sample leaves the retained immutable live grid.");
            double px=(x-OriginX)/SpacingMeters,pz=(z-OriginZ)/SpacingMeters;int ix=Math.Min(Columns-1,(int)Math.Floor(px)),iz=Math.Min(Rows-1,(int)Math.Floor(pz));
            int xx=Math.Min(ix+1,Columns-1),zz=Math.Min(iz+1,Rows-1);double fx=px-ix,fz=pz-iz;
            return (values[offset+ix+iz*Columns]*(1-fx)+values[offset+xx+iz*Columns]*fx)*(1-fz)+
                (values[offset+ix+zz*Columns]*(1-fx)+values[offset+xx+zz*Columns]*fx)*fz;
        }
        public SurfaceSample Sample(double x,double z){double h=Interpolate(heights,0,x,z);return new SurfaceSample(h,0,0,h>=0);}
        public bool TrySampleClimate(double x,double z,out SurfaceClimate c)
        {
            if(!SampleBounds.Contains(x,z)){c=default;return false;}int n=Columns*Rows;
            c=new SurfaceClimate(Interpolate(climate,0,x,z),Interpolate(climate,n,x,z),Interpolate(climate,n*2,x,z),Interpolate(climate,n*3,x,z),Interpolate(climate,n*4,x,z));return true;
        }
        public HeightBounds QueryBounds(double x0,double z0,double x1,double z1)=>new HeightBounds(minimumHeight,maximumHeight);
    }

    /// <summary>Stable shared world authority with atomic immutable patch states.
    /// Publishing/retiring is forbidden wherever full influence/query/prop bounds
    /// could reach the protected observer disk. Sampling performs no I/O.</summary>
    public sealed class ExplorationRefinementField:IWorldFieldBounds,IClimateWorldField,IWorldFieldSnapshotProvider
    {
        readonly object gate=new object();readonly List<Record> records=new List<Record>();readonly IWorldField baseField;
        readonly int maximumPatches;readonly long maximumBytes;readonly double protectionRadius,guardPadding;
        State current;long uses;int version;
        sealed class Record {public ExplorationRefinementPatch Patch;public long Use;}
        sealed class State:IWorldFieldBounds,IClimateWorldField
        {
            public readonly IWorldField Base;public readonly ExplorationRefinementPatch[] Patches;public readonly long Bytes;public readonly int Revision;
            public string SourceIdentity {get;}
            public State(IWorldField baseField,ExplorationRefinementPatch[] patches,int revision,string worldId)
            {Base=baseField;Patches=patches;Revision=revision;foreach(var p in patches)Bytes+=p.PayloadBytes;SourceIdentity=baseField.SourceIdentity+"/safe-refinement-v1/"+worldId+"/revision="+revision;}
            public SurfaceSample Sample(double x,double z)
            {
                SurfaceSample basis=Base.Sample(x,z);double sum=0,delta=0;
                foreach(var p in Patches)
                {
                    if(!p.InfluenceBounds.Contains(x,z))continue;double w=p.CoreBounds.Weight(x,z,p.BlendMeters);if(w<=0)continue;
                    sum+=w;delta+=(p.Sample(x,z).HeightMeters-basis.HeightMeters)*w;
                }
                double h=basis.HeightMeters+delta/Math.Max(1,sum);
                // Authored/source biome categories remain the map's authority.
                return new SurfaceSample(h,basis.SourceBiome,basis.EffectiveBiome,h>=0);
            }
            public bool TrySampleClimate(double x,double z,out SurfaceClimate result)
            {
                if(!(Base is IClimateWorldField c)||!c.TrySampleClimate(x,z,out SurfaceClimate basis)){result=default;return false;}
                double baseHeight=Base.Sample(x,z).HeightMeters,finalHeight=Sample(x,z).HeightMeters;
                double baseTemperature=basis.TemperatureC+basis.LapseRateCPerMeter*(Math.Max(finalHeight,0)-Math.Max(baseHeight,0));
                double sum=0,t=0,s=0,r=0,v=0,b=0;
                foreach(var p in Patches)
                {
                    if(!p.InfluenceBounds.Contains(x,z))continue;double w=p.CoreBounds.Weight(x,z,p.BlendMeters);if(w<=0||!p.TrySampleClimate(x,z,out SurfaceClimate f))continue;
                    double fineHeight=p.Sample(x,z).HeightMeters;
                    double fineTemperature=f.TemperatureC+f.LapseRateCPerMeter*(Math.Max(finalHeight,0)-Math.Max(fineHeight,0));
                    sum+=w;t+=(fineTemperature-baseTemperature)*w;s+=(f.SeasonalityC-basis.SeasonalityC)*w;r+=(f.PrecipitationMm-basis.PrecipitationMm)*w;
                    v+=(f.PrecipitationCvPercent-basis.PrecipitationCvPercent)*w;b+=(f.LapseRateCPerMeter-basis.LapseRateCPerMeter)*w;
                }
                double n=Math.Max(1,sum),temperature=baseTemperature+t/n;
                if(temperature< -150||temperature>100){result=default;return false;}
                result=new SurfaceClimate(temperature,basis.SeasonalityC+s/n,basis.PrecipitationMm+r/n,basis.PrecipitationCvPercent+v/n,basis.LapseRateCPerMeter+b/n);return true;
            }
            public HeightBounds QueryBounds(double x0,double z0,double x1,double z1)
            {
                if(!(Base is IWorldFieldBounds bounds))throw new InvalidOperationException("Live base field requires conservative bounds.");
                HeightBounds a=bounds.QueryBounds(x0,z0,x1,z1);double lo=a.Minimum,hi=a.Maximum;
                foreach(var p in Patches)if(p.InfluenceBounds.MaximumX>=x0&&p.InfluenceBounds.MinimumX<=x1&&p.InfluenceBounds.MaximumZ>=z0&&p.InfluenceBounds.MinimumZ<=z1)
                {HeightBounds b=p.QueryBounds(x0,z0,x1,z1);lo=Math.Min(lo,b.Minimum);hi=Math.Max(hi,b.Maximum);}
                double margin=Math.Max(1e-6,Math.Max(Math.Abs(lo),Math.Abs(hi))*1e-9);return new HeightBounds(lo-margin,hi+margin);
            }
        }
        public string WorldId {get;}
        public double SpacingMeters {get;}
        public string SourceIdentity=>Volatile.Read(ref current).SourceIdentity;
        public int ResidentPatches=>Volatile.Read(ref current).Patches.Length;
        public long ResidentPayloadBytes=>Volatile.Read(ref current).Bytes;
        public int Revision=>Volatile.Read(ref current).Revision;
        public string LastPublishRefusal {get;private set;}
        public ExplorationRefinementField(IWorldField baseField,string worldId,double spacingMeters=5,int maximumPatches=16,long maximumBytes=16L*1024*1024,double protectionRadiusMeters=512,double queryAndPropGuardMeters=96)
        {
            this.baseField=baseField??throw new ArgumentNullException(nameof(baseField));
            if(!(baseField is IWorldFieldBounds)||!(baseField is IClimateWorldField)||!ExplorationRefinementPatch.ValidId(worldId))throw new ArgumentException("A bounded physical-climate base and model identity are required.");
            Numeric.Positive(spacingMeters,nameof(spacingMeters));Numeric.Positive(protectionRadiusMeters,nameof(protectionRadiusMeters));Numeric.Finite(queryAndPropGuardMeters,nameof(queryAndPropGuardMeters));
            if(maximumPatches<1||maximumPatches>32||maximumBytes<1024||maximumBytes>128L*1024*1024||protectionRadiusMeters<256||queryAndPropGuardMeters<32||queryAndPropGuardMeters>512)throw new ArgumentOutOfRangeException("Live refinement limits");
            WorldId=worldId;SpacingMeters=spacingMeters;this.maximumPatches=maximumPatches;this.maximumBytes=maximumBytes;protectionRadius=protectionRadiusMeters;guardPadding=queryAndPropGuardMeters;
            current=new State(baseField,Array.Empty<ExplorationRefinementPatch>(),0,worldId);
        }
        bool Safe(ExplorationRefinementPatch patch,WorldPoint observer)=>patch.InfluenceBounds.Expanded(guardPadding).DistanceSquared(observer.X,observer.Z)>protectionRadius*protectionRadius;
        public bool ContainsPatch(string key){foreach(var p in Volatile.Read(ref current).Patches)if(p.Key==key)return true;return false;}
        public double LiveWeightAt(double x,double z)
        {double sum=0;foreach(var p in Volatile.Read(ref current).Patches)if(p.InfluenceBounds.Contains(x,z))sum+=p.CoreBounds.Weight(x,z,p.BlendMeters);return Math.Min(1,sum);}
        public bool HasActiveDetail(double x,double z)=>LiveWeightAt(x,z)>0;
        public double ResolutionMetersAt(double x,double z)=>LiveWeightAt(x,z)>=.999999?SpacingMeters:
            baseField is ExplorationAtlasField baked?baked.ModelResolutionMeters(x,z):double.NaN;
        public void Touch(WorldPoint observer){lock(gate)foreach(var r in records)if(r.Patch.InfluenceBounds.Contains(observer.X,observer.Z))r.Use=++uses;}
        void Commit(List<Record> next)
        {
            records.Clear();records.AddRange(next);var patches=new ExplorationRefinementPatch[next.Count];for(int i=0;i<patches.Length;i++)patches[i]=next[i].Patch;
            Array.Sort(patches,(a,b)=>string.CompareOrdinal(a.Key,b.Key));int revision=++version;Volatile.Write(ref current,new State(baseField,patches,revision,WorldId));
        }
        public bool TryPublish(ExplorationRefinementPatch patch,WorldPoint observer,out ExplorationRefinementBounds[] changes)
        {
            if(patch==null)throw new ArgumentNullException(nameof(patch));
            if(patch.WorldId!=WorldId||Math.Abs(patch.SpacingMeters-SpacingMeters)>1e-7)throw new ArgumentException("Live patch identity or physical grid differs from this world.");
            changes=Array.Empty<ExplorationRefinementBounds>();
            lock(gate)
            {
                LastPublishRefusal=null;
                if(!Safe(patch,observer)){LastPublishRefusal="Patch influence reaches protected player/query/prop area.";return false;}
                if(patch.PayloadBytes>maximumBytes){LastPublishRefusal="Patch exceeds the resident payload budget.";return false;}
                var next=new List<Record>(records);var dirty=new List<ExplorationRefinementBounds>();long bytes=0;
                for(int i=next.Count-1;i>=0;i--)
                {
                    if(next[i].Patch.Key==patch.Key)
                    {if(!Safe(next[i].Patch,observer)){LastPublishRefusal="Replaced patch is protected.";return false;}dirty.Add(next[i].Patch.InfluenceBounds.Expanded(guardPadding));next.RemoveAt(i);}
                    else bytes+=next[i].Patch.PayloadBytes;
                }
                while(next.Count>=maximumPatches||bytes+patch.PayloadBytes>maximumBytes)
                {
                    int victim=-1;for(int i=0;i<next.Count;i++)if(Safe(next[i].Patch,observer)&&(victim<0||next[i].Use<next[victim].Use))victim=i;
                    if(victim<0){LastPublishRefusal="All eviction candidates are protected; coarse world continues.";return false;}
                    bytes-=next[victim].Patch.PayloadBytes;dirty.Add(next[victim].Patch.InfluenceBounds.Expanded(guardPadding));next.RemoveAt(victim);
                }
                next.Add(new Record{Patch=patch,Use=++uses});dirty.Add(patch.InfluenceBounds.Expanded(guardPadding));Commit(next);changes=dirty.ToArray();return true;
            }
        }
        public bool TryRetire(string key,WorldPoint observer,out ExplorationRefinementBounds bounds)
        {
            lock(gate)
            {
                var next=new List<Record>(records);for(int i=0;i<next.Count;i++)if(next[i].Patch.Key==key)
                {if(!Safe(next[i].Patch,observer)){bounds=default;return false;}bounds=next[i].Patch.InfluenceBounds.Expanded(guardPadding);next.RemoveAt(i);Commit(next);return true;}
            }
            bounds=default;return false;
        }
        public IWorldField CaptureSnapshot()=>Volatile.Read(ref current);
        public SurfaceSample Sample(double x,double z)=>Volatile.Read(ref current).Sample(x,z);
        public bool TrySampleClimate(double x,double z,out SurfaceClimate climate)=>Volatile.Read(ref current).TrySampleClimate(x,z,out climate);
        public HeightBounds QueryBounds(double x0,double z0,double x1,double z1)=>Volatile.Read(ref current).QueryBounds(x0,z0,x1,z1);
    }
}
