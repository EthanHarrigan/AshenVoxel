using System;
using System.Collections.Generic;

namespace AshenVoxel.WorldScale
{
    /// <summary>Owner-thread clipmap scheduler. Jobs only return row-major values;
    /// visible mirrors change after explicit GPU publication acknowledgement.
    /// One spare mirror bounds memory and prevents writes into visible coverage.</summary>
    public sealed class TerrainClipmapState
    {
        sealed class Level
        {
            public ClipmapSample[] Samples;public long X,Z,DesiredX,DesiredZ,Generation,DirtyVersion;
            public bool Published;public readonly List<ClipmapRect> Dirty=new List<ClipmapRect>();public bool HasDirty=>Dirty.Count>0;
        }
        sealed class Candidate
        {
            public int Level;public long Generation,X,Z,DirtyVersion;public ClipmapRect Window;
            public readonly List<ClipmapRect> Pending=new List<ClipmapRect>();public ClipmapRect[] Changes;
            public ClipmapPublication Publication;
        }
        readonly Level[] levels;readonly double[] spacings;readonly Dictionary<long,ClipmapJob> running=new Dictionary<long,ClipmapJob>();
        readonly int maximumInFlight,maximumJobs;ClipmapSample[] spare;Candidate candidate;long nextJob,generation;
        ClipmapLevelInfo[] visibleCache;
        public int Size{get;}public int LevelCount=>levels.Length;public long DataRevision{get;private set;}
        public long ResidentBytes=>(long)(levels.Length+1)*Size*Size*12;
        public int InFlightJobs=>running.Count;
        public int InFlightSamples{get;private set;}
        public int PendingJobs=>candidate==null?0:candidate.Pending.Count;
        public long PendingSamples
        {
            get
            {
                long count=InFlightSamples;if(candidate!=null)foreach(var r in candidate.Pending)count+=r.Count;
                for(int i=0;i<levels.Length;i++)if(candidate==null||candidate.Level!=i)
                    foreach(var r in Changes(i,levels[i].DesiredX,levels[i].DesiredZ))count+=r.Count;
                return count;
            }
        }
        public TerrainClipmapState(int size,double finestSpacing,int levels,double anchorX=0,double anchorZ=0,int maxInFlightSamples=300000,int maxJobs=8)
        {
            TerrainClipmapMath.ValidateSize(size);Numeric.Positive(finestSpacing,nameof(finestSpacing));
            if(levels<1||levels>16||maxInFlightSamples<1||maxInFlightSamples>4194304||maxJobs<1||maxJobs>64)throw new ArgumentOutOfRangeException(nameof(levels));
            Size=size;maximumInFlight=maxInFlightSamples;maximumJobs=maxJobs;this.levels=new Level[levels];spacings=new double[levels];spare=new ClipmapSample[size*size];
            for(int i=0;i<levels;i++)
            {spacings[i]=finestSpacing*Math.Pow(2,i);long x=TerrainClipmapMath.WindowOrigin(anchorX,spacings[i],size),z=TerrainClipmapMath.WindowOrigin(anchorZ,spacings[i],size);this.levels[i]=new Level{Samples=new ClipmapSample[size*size],DesiredX=x,DesiredZ=z};}
        }
        public ClipmapLevelInfo GetLevel(int level)
        {if(level<0||level>=levels.Length)throw new ArgumentOutOfRangeException(nameof(level));var l=levels[level];return new ClipmapLevelInfo(level,spacings[level],l.X,l.Z,l.Published,l.Generation);}
        public void Move(double x,double z)
        {
            Numeric.Finite(x,nameof(x));Numeric.Finite(z,nameof(z));
            for(int i=0;i<levels.Length-1;i++){levels[i].DesiredX=TerrainClipmapMath.WindowOrigin(x,spacings[i],Size);levels[i].DesiredZ=TerrainClipmapMath.WindowOrigin(z,spacings[i],Size);}
            // Small moves do not perpetually cancel a useful in-flight window.
            // Large displacement/teleport rejects its completion generation.
            if(candidate!=null&&candidate.Level<levels.Length-1)
            {var l=levels[candidate.Level];if(Math.Abs(l.DesiredX-candidate.X)>=Size/4||Math.Abs(l.DesiredZ-candidate.Z)>=Size/4)CancelCandidate();}
        }
        static ClipmapRect Union(ClipmapRect a,ClipmapRect b)
        {long x=Math.Min(a.X,b.X),z=Math.Min(a.Z,b.Z);return new ClipmapRect(x,z,checked((int)(Math.Max(a.EndX,b.EndX)-x)),checked((int)(Math.Max(a.EndZ,b.EndZ)-z)));}
        public void Invalidate(double x0,double z0,double x1,double z1)
        {
            Numeric.Finite(x0,nameof(x0));Numeric.Finite(z0,nameof(z0));Numeric.Finite(x1,nameof(x1));Numeric.Finite(z1,nameof(z1));
            if(x0>x1||z0>z1)throw new ArgumentException("Inverted clipmap invalidation");
            for(int i=0;i<levels.Length;i++)
            {
                var l=levels[i];double s=spacings[i],halo=Math.Max(32,s*2);
                // Clamp to at most two resident/desired windows before converting
                // world doubles to lattice integers, including enormous inputs.
                void DirtyWindow(long wx,long wz)
                {
                    double ax=Math.Max(x0-halo,wx*s),az=Math.Max(z0-halo,wz*s),bx=Math.Min(x1+halo,(wx+Size-1)*s),bz=Math.Min(z1+halo,(wz+Size-1)*s);
                    if(ax>bx||az>bz)return;
                    long ix=Math.Max(wx,(long)Math.Floor(ax/s)),iz=Math.Max(wz,(long)Math.Floor(az/s));
                    long xx=Math.Min(wx+Size,(long)Math.Ceiling(bx/s)+1),zz=Math.Min(wz+Size,(long)Math.Ceiling(bz/s)+1);
                    var r=new ClipmapRect(ix,iz,(int)(xx-ix),(int)(zz-iz));if(r.Count==0)return;
                    bool merged=false;for(int d=0;d<l.Dirty.Count;d++)if(l.Dirty[d].Intersects(r)){l.Dirty[d]=Union(l.Dirty[d],r);merged=true;break;}
                    if(!merged)l.Dirty.Add(r);
                    // Bound obligations without dropping either distant edit.
                    // Separate old/desired windows avoid a giant teleport union.
                    if(l.Dirty.Count>32){l.Dirty.Clear();if(l.Published)l.Dirty.Add(new ClipmapRect(l.X,l.Z,Size,Size));l.Dirty.Add(new ClipmapRect(l.DesiredX,l.DesiredZ,Size,Size));}
                    l.DirtyVersion++;
                    if(candidate!=null&&candidate.Level==i&&candidate.Window.Intersects(r))CancelCandidate();
                }
                if(l.Published)DirtyWindow(l.X,l.Z);DirtyWindow(l.DesiredX,l.DesiredZ);
            }
        }
        void CancelCandidate(){candidate=null;running.Clear();InFlightSamples=0;generation++;}
        static void Difference(ClipmapRect a,ClipmapRect b,List<ClipmapRect> result)
        {
            var c=a.Intersection(b);if(c.Count==0){if(a.Count>0)result.Add(a);return;}
            void Add(long x,long z,int w,int h){if(w>0&&h>0)result.Add(new ClipmapRect(x,z,w,h));}
            Add(a.X,a.Z,a.Width,(int)(c.Z-a.Z));Add(a.X,c.EndZ,a.Width,(int)(a.EndZ-c.EndZ));
            Add(a.X,c.Z,(int)(c.X-a.X),c.Height);Add(c.EndX,c.Z,(int)(a.EndX-c.EndX),c.Height);
        }
        List<ClipmapRect> Changes(int index,long x,long z)
        {
            var l=levels[index];var window=new ClipmapRect(x,z,Size,Size);var result=new List<ClipmapRect>(8);
            if(!l.Published){result.Add(window);return result;}
            Difference(window,new ClipmapRect(l.X,l.Z,Size,Size),result);
            if(l.HasDirty)
            {
                foreach(var dirty in l.Dirty)
                {
                    var remaining=new List<ClipmapRect>{dirty.Intersection(window)};
                    foreach(var r in result)
                    {var next=new List<ClipmapRect>(8);foreach(var d in remaining)Difference(d,r,next);remaining=next;}
                    foreach(var r in remaining)if(r.Count>0)result.Add(r);
                }
            }
            return result;
        }
        void EnsureCandidate()
        {
            if(candidate!=null)return;
            for(int i=levels.Length-1;i>=0;i--)
            {
                var l=levels[i];var changes=Changes(i,l.DesiredX,l.DesiredZ);if(changes.Count==0){l.Dirty.Clear();continue;}
                if(l.Published)Array.Copy(l.Samples,spare,spare.Length);
                candidate=new Candidate{Level=i,Generation=++generation,X=l.DesiredX,Z=l.DesiredZ,DirtyVersion=l.DirtyVersion,Window=new ClipmapRect(l.DesiredX,l.DesiredZ,Size,Size),Changes=changes.ToArray()};
                candidate.Pending.AddRange(changes);return;
            }
        }
        public bool TryTakeJob(int maxSamples,out ClipmapJob job)
        {
            if(maxSamples<1)throw new ArgumentOutOfRangeException(nameof(maxSamples));job=default;EnsureCandidate();
            if(candidate==null||running.Count>=maximumJobs||candidate.Pending.Count==0)return false;
            int allowance=Math.Min(maxSamples,maximumInFlight-InFlightSamples);if(allowance<1)return false;
            var r=candidate.Pending[0];candidate.Pending.RemoveAt(0);int width=Math.Min(r.Width,allowance),height=Math.Min(r.Height,Math.Max(1,allowance/width));
            var taken=new ClipmapRect(r.X,r.Z,width,height);var rest=new List<ClipmapRect>(2);Difference(r,taken,rest);candidate.Pending.InsertRange(0,rest);
            job=new ClipmapJob(++nextJob,candidate.Generation,candidate.Level,taken);running.Add(job.Id,job);InFlightSamples+=taken.Count;return true;
        }
        bool CurrentJob(ClipmapJob job,out ClipmapJob held)
        {
            held=default;return candidate!=null&&job.Generation==candidate.Generation&&job.Level==candidate.Level&&running.TryGetValue(job.Id,out held)&&held.Generation==job.Generation&&held.Rect.X==job.Rect.X&&held.Rect.Z==job.Rect.Z&&held.Rect.Width==job.Rect.Width&&held.Rect.Height==job.Rect.Height;
        }
        public bool Complete(ClipmapJob job,ClipmapSample[] samples)
        {
            if(!CurrentJob(job,out var held))return false;
            if(samples==null||samples.Length!=held.SampleCount)throw new ArgumentException("Complete job sample count required");
            foreach(var value in samples)if(float.IsNaN(value.Height)||float.IsInfinity(value.Height))throw new ArgumentException("Nonfinite clipmap height");
            int n=0;for(long z=held.Rect.Z;z<held.Rect.EndZ;z++)for(long x=held.Rect.X;x<held.Rect.EndX;x++)spare[TerrainClipmapMath.ToroidalIndex(x,z,Size)]=samples[n++];
            running.Remove(job.Id);InFlightSamples-=held.SampleCount;return true;
        }
        public bool Fail(ClipmapJob job)
        {if(!CurrentJob(job,out var held))return false;running.Remove(job.Id);InFlightSamples-=held.SampleCount;candidate.Pending.Add(held.Rect);return true;}
        public bool TryTakePublication(out ClipmapPublication publication)
        {
            publication=null;if(candidate==null||candidate.Pending.Count!=0||running.Count!=0)return false;
            if(candidate.Publication==null)candidate.Publication=new ClipmapPublication(candidate.Level,candidate.Generation,candidate.X,candidate.Z,spare,candidate.Changes);
            publication=candidate.Publication;return true;
        }
        public bool Publish(ClipmapPublication publication)
        {
            if(candidate==null||publication==null||!ReferenceEquals(candidate.Publication,publication)||publication.Generation!=candidate.Generation||candidate.Pending.Count!=0||running.Count!=0)return false;
            var l=levels[candidate.Level];var previous=l.Samples;l.Samples=spare;spare=previous;l.X=candidate.X;l.Z=candidate.Z;l.Generation=candidate.Generation;l.Published=true;
            // Every dirty region intersecting this candidate would have cancelled
            // its generation; remaining older obligations are outside its window.
            l.Dirty.Clear();candidate=null;visibleCache=null;DataRevision++;return true;
        }
        ClipmapLevelInfo[] VisibleLevels()
        {
            if(visibleCache!=null)return visibleCache;
            var result=new List<ClipmapLevelInfo>(levels.Length);var parent=GetLevel(levels.Length-1);if(!parent.Published)return visibleCache=result.ToArray();result.Add(parent);
            for(int i=levels.Length-2;i>=0;i--){var child=GetLevel(i);if(!TerrainClipmapMath.CanNest(parent,child,Size))break;result.Add(child);parent=child;}
            result.Reverse();return visibleCache=result.ToArray();
        }
        public ClipmapLevelInfo[] GetVisibleLevels()=>(ClipmapLevelInfo[])VisibleLevels().Clone();
        public bool IsReady
        {
            get
            {if(candidate!=null||VisibleLevels().Length!=levels.Length)return false;foreach(var l in levels)if(!l.Published||l.HasDirty||l.X!=l.DesiredX||l.Z!=l.DesiredZ)return false;return true;}
        }
        public bool IsReadyArea(double x,double z,double radius)
        {
            Numeric.Finite(x,nameof(x));Numeric.Finite(z,nameof(z));Numeric.Finite(radius,nameof(radius));if(radius<0)throw new ArgumentOutOfRangeException(nameof(radius));
            var visible=VisibleLevels();if(visible.Length==0)return false;
            // Coverage can be supplied by an actually published ancestor while
            // finer detail catches up; no sample of an unfinished window occurs.
            foreach(var level in visible)if(TerrainClipmapMath.Contains(level,Size,x,z,radius))return true;return false;
        }
        double Raw(int level,long x,long z)=>levels[level].Samples[TerrainClipmapMath.ToroidalIndex(x,z,Size)].Height;
        double RawTriangle(int level,double x,double z)
        {long ix=(long)Math.Floor(x),iz=(long)Math.Floor(z);return TerrainClipmapMath.TriangleHeight(Raw(level,ix,iz),Raw(level,ix+1,iz),Raw(level,ix,iz+1),Raw(level,ix+1,iz+1),x-ix,z-iz);}
        double Vertex(int level,long x,long z)
        {
            double h=Raw(level,x,z);if(level==levels.Length-1)return h;
            var l=levels[level];double alpha=TerrainClipmapMath.MorphAlpha(x-l.X,z-l.Z,Size);
            return h+(RawTriangle(level+1,x*.5,z*.5)-h)*alpha;
        }
        public bool TryRenderedHeight(double x,double z,out double height)
        {
            Numeric.Finite(x,nameof(x));Numeric.Finite(z,nameof(z));height=0;
            foreach(var info in VisibleLevels())if(TerrainClipmapMath.Contains(info,Size,x,z))
            {
                double px=x/info.Spacing,pz=z/info.Spacing;long ix=(long)Math.Floor(px),iz=(long)Math.Floor(pz);
                // Interpolate actual emitted vertex heights. Interpolating alpha
                // at the query point is a different surface inside the morph band.
                height=TerrainClipmapMath.TriangleHeight(Vertex(info.Level,ix,iz),Vertex(info.Level,ix+1,iz),Vertex(info.Level,ix,iz+1),Vertex(info.Level,ix+1,iz+1),px-ix,pz-iz);return true;
            }
            return false;
        }
    }
}
