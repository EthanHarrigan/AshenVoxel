using System;
using System.Collections.Generic;

namespace AshenVoxel.WorldScale
{
    public readonly struct FarAppearanceSample
    {
        public readonly uint Tint,Appearance;
        public FarAppearanceSample(uint tint,uint appearance){Tint=tint;Appearance=appearance;}
    }
    public readonly struct FarAppearanceWindow
    {
        public readonly long OriginX,OriginZ,Generation;
        public readonly double Spacing;
        public readonly int Size;
        public FarAppearanceWindow(long originX,long originZ,double spacing,int size,long generation)
        {OriginX=originX;OriginZ=originZ;Spacing=spacing;Size=size;Generation=generation;}
        public ClipmapRect Rect=>new ClipmapRect(OriginX,OriginZ,Size,Size);
    }
    public readonly struct FarAppearanceJob
    {
        public readonly long Id,Generation;
        public readonly ClipmapRect Rect;
        public readonly FarAppearanceWindow Window;
        public int SampleCount=>Rect.Count;
        internal FarAppearanceJob(long id,FarAppearanceWindow window,ClipmapRect rect)
        {Id=id;Generation=window.Generation;Rect=rect;Window=window;}
    }
    /// <summary>Borrowed toroidal staging data; valid until Publish, Request or an
    /// intersecting Invalidate. The GPU must acknowledge the whole publication.</summary>
    public sealed class FarAppearancePublication
    {
        public readonly FarAppearanceWindow Window;
        public readonly FarAppearanceSample[] Samples;
        public readonly ClipmapRect[] ChangedRegions;
        public readonly bool CopyPublished;
        internal FarAppearancePublication(FarAppearanceWindow window,FarAppearanceSample[] samples,ClipmapRect[] changes,bool copy)
        {Window=window;Samples=samples;ChangedRegions=changes;CopyPublished=copy;}
    }
    /// <summary>One optional world-anchored appearance window, independent of terrain
    /// geometry/readiness. Owner-thread state, one worker, two bounded mirrors.</summary>
    public sealed class TerrainFarAppearanceState
    {
        sealed class Candidate
        {
            public FarAppearanceWindow Window;
            public readonly List<ClipmapRect> Changes=new List<ClipmapRect>();
            public readonly List<ClipmapRect> Pending=new List<ClipmapRect>();
            public FarAppearanceJob? Running;
            public FarAppearancePublication Publication;
            public bool Copy;
        }
        FarAppearanceSample[] active,spare;
        FarAppearanceWindow? desired;
        Candidate candidate;
        readonly List<ClipmapRect> dirty=new List<ClipmapRect>();
        long generation,nextJob;
        public int Size{get;}
        public FarAppearanceWindow? ActiveWindow{get;private set;}
        public long DataRevision{get;private set;}
        public long ResidentBytes=>(long)Size*Size*16;
        public int InFlightSamples=>candidate?.Running?.SampleCount??0;
        public int PendingJobs=>candidate==null?Changes().Count:candidate.Pending.Count+(candidate.Running.HasValue?1:0);
        public long PendingSamples
        {
            get
            {
                long n=InFlightSamples;
                foreach(var r in candidate==null?Changes():candidate.Pending)n+=r.Count;
                return n;
            }
        }
        public TerrainFarAppearanceState(int size)
        {
            if(size<16||size>1536)throw new ArgumentOutOfRangeException(nameof(size));
            Size=size;active=new FarAppearanceSample[size*size];spare=new FarAppearanceSample[size*size];
        }
        public static int ToroidalIndex(long x,long z,int size)
        {
            if(size<1)throw new ArgumentOutOfRangeException(nameof(size));
            int ix=(int)(x%size),iz=(int)(z%size);if(ix<0)ix+=size;if(iz<0)iz+=size;return ix+iz*size;
        }
        public void Request(long originX,long originZ,double spacing)
        {
            Numeric.Positive(spacing,nameof(spacing));
            if(Math.Abs((double)originX)>1e12||Math.Abs((double)originZ)>1e12||Math.Abs(originX*spacing)>1e12||Math.Abs(originZ*spacing)>1e12)
                throw new ArgumentOutOfRangeException(nameof(originX));
            if(desired.HasValue&&desired.Value.OriginX==originX&&desired.Value.OriginZ==originZ&&desired.Value.Spacing==spacing)return;
            if(desired.HasValue&&desired.Value.Spacing!=spacing)dirty.Clear();
            desired=new FarAppearanceWindow(originX,originZ,spacing,Size,++generation);candidate=null;
            // Only retained samples can carry a dirty obligation. Newly exposed
            // samples are always computed, including after a large teleport.
            if(!ActiveWindow.HasValue||ActiveWindow.Value.Spacing!=spacing)dirty.Clear();
            else
            {
                var retained=ActiveWindow.Value.Rect;
                for(int i=dirty.Count-1;i>=0;i--){var r=dirty[i].Intersection(retained);if(r.Count==0)dirty.RemoveAt(i);else dirty[i]=r;}
            }
        }
        static void Difference(ClipmapRect a,ClipmapRect b,List<ClipmapRect> result)
        {
            var c=a.Intersection(b);if(c.Count==0){if(a.Count>0)result.Add(a);return;}
            void Add(long x,long z,int w,int h){if(w>0&&h>0)result.Add(new ClipmapRect(x,z,w,h));}
            Add(a.X,a.Z,a.Width,(int)(c.Z-a.Z));Add(a.X,c.EndZ,a.Width,(int)(a.EndZ-c.EndZ));
            Add(a.X,c.Z,(int)(c.X-a.X),c.Height);Add(c.EndX,c.Z,(int)(a.EndX-c.EndX),c.Height);
        }
        List<ClipmapRect> Changes()
        {
            var result=new List<ClipmapRect>(8);if(!desired.HasValue)return result;
            var d=desired.Value;
            if(!ActiveWindow.HasValue||ActiveWindow.Value.Spacing!=d.Spacing){result.Add(d.Rect);return result;}
            Difference(d.Rect,ActiveWindow.Value.Rect,result);
            foreach(var r in dirty)
            {
                var remaining=new List<ClipmapRect>{r.Intersection(d.Rect)};
                foreach(var covered in result)
                {var next=new List<ClipmapRect>();foreach(var part in remaining)Difference(part,covered,next);remaining=next;}
                foreach(var part in remaining)if(part.Count>0)result.Add(part);
            }
            return result;
        }
        static ClipmapRect Union(ClipmapRect a,ClipmapRect b)
        {long x=Math.Min(a.X,b.X),z=Math.Min(a.Z,b.Z);return new ClipmapRect(x,z,(int)(Math.Max(a.EndX,b.EndX)-x),(int)(Math.Max(a.EndZ,b.EndZ)-z));}
        static void AddUncovered(List<ClipmapRect> target,ClipmapRect rect)
        {
            if(rect.Count==0)return;var remaining=new List<ClipmapRect>{rect};
            foreach(var covered in target)
            {var next=new List<ClipmapRect>();foreach(var part in remaining)Difference(part,covered,next);remaining=next;}
            target.AddRange(remaining);
        }
        void AddDirty(ClipmapRect rect)
        {
            if(rect.Count==0)return;
            for(int i=0;i<dirty.Count;i++)if(dirty[i].Intersects(rect)){dirty[i]=Union(dirty[i],rect);return;}
            dirty.Add(rect);
        }
        public void Invalidate(double x0,double z0,double x1,double z1)
        {
            Numeric.Finite(x0,nameof(x0));Numeric.Finite(z0,nameof(z0));Numeric.Finite(x1,nameof(x1));Numeric.Finite(z1,nameof(z1));
            if(x0>x1||z0>z1)throw new ArgumentException("Inverted appearance invalidation");
            if(!desired.HasValue)return;
            var d=desired.Value;
            // Current nested material taps reach16/7 footprints plus .42 for
            // supersampling. This also covers the fixed32m wind sample radius.
            double halo=32+3*Math.Min(d.Spacing,512);
            void Visit(ClipmapRect window)
            {
                double ax=Math.Max(x0-halo,window.X*d.Spacing),az=Math.Max(z0-halo,window.Z*d.Spacing);
                double bx=Math.Min(x1+halo,(window.EndX-1)*d.Spacing),bz=Math.Min(z1+halo,(window.EndZ-1)*d.Spacing);
                if(ax>bx||az>bz)return;
                long x=Math.Max(window.X,(long)Math.Floor(ax/d.Spacing)),z=Math.Max(window.Z,(long)Math.Floor(az/d.Spacing));
                long xx=Math.Min(window.EndX,(long)Math.Ceiling(bx/d.Spacing)+1),zz=Math.Min(window.EndZ,(long)Math.Ceiling(bz/d.Spacing)+1);
                var r=new ClipmapRect(x,z,(int)(xx-x),(int)(zz-z));AddDirty(r);
                RefreshCandidate(r);
            }
            Visit(d.Rect);
            if(ActiveWindow.HasValue&&ActiveWindow.Value.Spacing==d.Spacing)Visit(ActiveWindow.Value.Rect);
            if(dirty.Count>16)
            {dirty.Clear();dirty.Add(d.Rect);if(ActiveWindow.HasValue&&ActiveWindow.Value.Spacing==d.Spacing)dirty.Add(ActiveWindow.Value.Rect);RefreshCandidate(d.Rect);}
        }
        void RefreshCandidate(ClipmapRect dirtyRect)
        {
            if(candidate==null)return;var r=dirtyRect.Intersection(candidate.Window.Rect);if(r.Count==0)return;
            // Keep all unaffected completed samples and the one unaffected worker.
            // Only the publication token changes: old GPU uploads cannot commit.
            var w=candidate.Window;candidate.Window=new FarAppearanceWindow(w.OriginX,w.OriginZ,w.Spacing,w.Size,++generation);candidate.Publication=null;
            if(candidate.Running.HasValue&&candidate.Running.Value.Rect.Intersects(r))
            {AddUncovered(candidate.Pending,candidate.Running.Value.Rect);candidate.Running=null;}
            AddUncovered(candidate.Pending,r);AddUncovered(candidate.Changes,r);
            if(candidate.Pending.Count>128||candidate.Changes.Count>128)
            {
                // Pathological edit fragmentation remains bounded. Recomputing a
                // full window is conservative; no pending edit is discarded.
                candidate.Running=null;candidate.Pending.Clear();candidate.Pending.Add(w.Rect);
                candidate.Changes.Clear();candidate.Changes.Add(w.Rect);
            }
        }
        public void InvalidateAll()
        {
            candidate=null;generation++;dirty.Clear();
            if(desired.HasValue)dirty.Add(desired.Value.Rect);
            if(ActiveWindow.HasValue&&desired.HasValue&&ActiveWindow.Value.Spacing==desired.Value.Spacing)dirty.Add(ActiveWindow.Value.Rect);
        }
        void EnsureCandidate()
        {
            if(candidate!=null||!desired.HasValue)return;
            var changes=Changes();if(changes.Count==0)return;
            var d=desired.Value;bool copy=ActiveWindow.HasValue&&ActiveWindow.Value.Spacing==d.Spacing;
            if(copy)Array.Copy(active,spare,active.Length);
            candidate=new Candidate{Window=new FarAppearanceWindow(d.OriginX,d.OriginZ,d.Spacing,Size,++generation),Copy=copy};
            candidate.Changes.AddRange(changes);
            candidate.Pending.AddRange(changes);
        }
        public bool TryTakeJob(int maximumSamples,out FarAppearanceJob job)
        {
            if(maximumSamples<1)throw new ArgumentOutOfRangeException(nameof(maximumSamples));
            job=default;EnsureCandidate();if(candidate==null||candidate.Running.HasValue||candidate.Pending.Count==0)return false;
            var r=candidate.Pending[0];candidate.Pending.RemoveAt(0);
            int width=Math.Min(r.Width,maximumSamples),height=Math.Min(r.Height,maximumSamples/width);
            var part=new ClipmapRect(r.X,r.Z,width,height);
            if(width<r.Width)candidate.Pending.Insert(0,new ClipmapRect(r.X+width,r.Z,r.Width-width,height));
            if(height<r.Height)candidate.Pending.Add(new ClipmapRect(r.X,r.Z+height,r.Width,r.Height-height));
            job=new FarAppearanceJob(++nextJob,candidate.Window,part);candidate.Running=job;return true;
        }
        bool Current(FarAppearanceJob job)=>candidate!=null&&candidate.Running.HasValue&&candidate.Running.Value.Id==job.Id&&candidate.Running.Value.Generation==job.Generation;
        public bool Complete(FarAppearanceJob job,FarAppearanceSample[] samples)
        {
            if(samples==null||samples.Length!=job.SampleCount)throw new ArgumentException("Complete appearance job data required",nameof(samples));
            if(!Current(job))return false;
            int n=0;for(long z=job.Rect.Z;z<job.Rect.EndZ;z++)for(long x=job.Rect.X;x<job.Rect.EndX;x++)spare[ToroidalIndex(x,z,Size)]=samples[n++];
            candidate.Running=null;return true;
        }
        public bool Fail(FarAppearanceJob job)
        {if(!Current(job))return false;candidate.Running=null;candidate.Pending.Insert(0,job.Rect);return true;}
        public bool TryTakePublication(out FarAppearancePublication publication)
        {
            publication=null;EnsureCandidate();if(candidate==null||candidate.Running.HasValue||candidate.Pending.Count!=0)return false;
            if(candidate.Publication==null)candidate.Publication=new FarAppearancePublication(candidate.Window,spare,candidate.Changes.ToArray(),candidate.Copy);
            publication=candidate.Publication;return true;
        }
        public bool Publish(FarAppearancePublication publication)
        {
            if(candidate==null||publication==null||!ReferenceEquals(candidate.Publication,publication)||candidate.Running.HasValue||candidate.Pending.Count!=0)return false;
            var old=active;active=spare;spare=old;ActiveWindow=publication.Window;candidate=null;dirty.Clear();DataRevision++;return true;
        }
        public bool TryGetSample(long x,long z,out FarAppearanceSample sample)
        {sample=default;if(!ActiveWindow.HasValue||!ActiveWindow.Value.Rect.Contains(x,z))return false;sample=active[ToroidalIndex(x,z,Size)];return true;}
    }
    public static class TerrainFarAppearanceTarget
    {
        /// <summary>Rotation-independent conservative target. Excludes unused
        /// anchor rings outside the source domain and the view's enclosing sphere.</summary>
        public static bool TryChoose(ClipmapLevelInfo[] chain,int geometrySize,double observerX,double observerZ,
            double cornerRadius,double worldHalfX,double worldHalfZ,out ClipmapLevelInfo selected)
        {
            selected=default;if(chain==null||chain.Length==0)return false;
            Numeric.Positive(cornerRadius,nameof(cornerRadius));Numeric.Positive(worldHalfX,nameof(worldHalfX));Numeric.Positive(worldHalfZ,nameof(worldHalfZ));
            for(int i=chain.Length-1;i>=0;i--)
            {
                var level=chain[i];if(!level.Published)continue;
                var rectangles=TerrainClipmapMath.GetRingRects(level,i==0?(ClipmapLevelInfo?)null:chain[i-1],geometrySize);
                foreach(var r in rectangles)
                {
                    double x0=Math.Max(-worldHalfX,(level.OriginX+r.X)*level.Spacing),z0=Math.Max(-worldHalfZ,(level.OriginZ+r.Z)*level.Spacing);
                    double x1=Math.Min(worldHalfX,(level.OriginX+r.EndX)*level.Spacing),z1=Math.Min(worldHalfZ,(level.OriginZ+r.EndZ)*level.Spacing);
                    if(x0>=x1||z0>=z1)continue;
                    double dx=Math.Max(x0-observerX,Math.Max(0,observerX-x1)),dz=Math.Max(z0-observerZ,Math.Max(0,observerZ-z1));
                    if(dx*dx+dz*dz<=cornerRadius*cornerRadius){selected=level;return true;}
                }
            }
            return false;
        }
    }
}
