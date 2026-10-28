using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using AshenVoxel.WorldScale;
using AshenVoxel.WorldScaleRuntime;
using UnityEngine;

namespace AshenVoxel.Exploration
{
    public readonly struct ExplorationSourceDemandCell
    {
        public readonly string Key;
        public readonly ExplorationRefinementBounds Core;
        public readonly int Priority;
        public readonly double Score;
        public readonly bool NearFront;
        public bool Current=>Priority==0;
        internal ExplorationSourceDemandCell(long x,long z,int priority,double score,bool nearFront=false)
        {Key=x.ToString(System.Globalization.CultureInfo.InvariantCulture)+"/"+z.ToString(System.Globalization.CultureInfo.InvariantCulture)+"/640";Core=new ExplorationRefinementBounds(x*640.0,z*640.0,(x+1)*640.0,(z+1)*640.0);Priority=priority;Score=score;NearFront=nearFront;}
    }
    /// <summary>Finite XZ view-corridor approximation, not an occlusion/visibility
    /// oracle. Fixed640m cells, at most290 scanned, radius5120m; no world bake.</summary>
    public static class ExplorationSourceDemandPlanner
    {
        public const double NearFrontReachMeters=1280,MaximumNearFrontHalfWidthMeters=1280;
        static bool NearFrontIntersects(ExplorationRefinementBounds core,WorldPoint observer,double hx,double hz,double halfAngle)
        {
            double width=Math.Min(MaximumNearFrontHalfWidthMeters,Math.Tan(halfAngle*Math.PI/180)*NearFrontReachMeters*.5);
            // Triangle origin at observer, capped half-FOV central view span at1280m.
            // Project in observer-relative coordinates to avoid large-world loss.
            double ax=hx*NearFrontReachMeters-hz*width,az=hz*NearFrontReachMeters+hx*width;
            double bx=hx*NearFrontReachMeters+hz*width,bz=hz*NearFrontReachMeters-hx*width;
            double x0=core.MinimumX-observer.X,x1=core.MaximumX-observer.X,z0=core.MinimumZ-observer.Z,z1=core.MaximumZ-observer.Z;
            bool Overlaps(double nx,double nz)
            {
                double ta=ax*nx+az*nz,tb=bx*nx+bz*nz,lo=Math.Min(0,Math.Min(ta,tb)),hi=Math.Max(0,Math.Max(ta,tb));
                double p0=x0*nx+z0*nz,p1=x0*nx+z1*nz,p2=x1*nx+z0*nz,p3=x1*nx+z1*nz;
                double boxLo=Math.Min(Math.Min(p0,p1),Math.Min(p2,p3)),boxHi=Math.Max(Math.Max(p0,p1),Math.Max(p2,p3));
                return boxHi>lo+1e-7&&hi>boxLo+1e-7;
            }
            return Overlaps(1,0)&&Overlaps(0,1)&&Overlaps(-az,ax)&&Overlaps(hx,hz)&&Overlaps(bz,-bx);
        }

        public const double CellMeters=640,MaximumRadiusMeters=5120,NearbyReachMeters=640;
        /// <summary>Predict from actual travel, independent of camera yaw. Ignore
        /// teleports/stale samples rather than scheduling a fictitious corridor.</summary>
        public static WorldPoint? PredictDestination(WorldPoint previous,WorldPoint observer,double elapsedSeconds,double lookAheadMeters)
        {
            Finite(elapsedSeconds,nameof(elapsedSeconds));Finite(lookAheadMeters,nameof(lookAheadMeters));
            if(lookAheadMeters<1024||lookAheadMeters>10000)throw new ArgumentOutOfRangeException(nameof(lookAheadMeters));
            if(elapsedSeconds<.1||elapsedSeconds>2)return null;
            double dx=observer.X-previous.X,dz=observer.Z-previous.Z,distance=Math.Sqrt(dx*dx+dz*dz),speed=distance/elapsedSeconds;
            if(speed<4||speed>2000)return null;
            double reach=Math.Min(Math.Min(lookAheadMeters,MaximumRadiusMeters-CellMeters),Math.Max(CellMeters,speed*6));
            return new WorldPoint(observer.X+dx/distance*reach,observer.Y,observer.Z+dz/distance*reach);
        }
        public const int MaximumScannedCells=290;
        static void Finite(double value,string name){if(double.IsNaN(value)||double.IsInfinity(value))throw new ArgumentOutOfRangeException(name);}
        static void Positive(double value,string name){Finite(value,name);if(value<=0)throw new ArgumentOutOfRangeException(name);}
        public static ExplorationSourceDemandCell[] Build(WorldPoint observer,double headingX,double headingZ,double lookAheadMeters,
            double worldWidth,double worldDepth,bool deferCurrent,bool wideNeighborhood,double halfAngleDegrees=55,WorldPoint? destination=null,ISet<string> retained=null)
        {
            Finite(observer.X,nameof(observer));Finite(observer.Z,nameof(observer));Finite(headingX,nameof(headingX));Finite(headingZ,nameof(headingZ));
            Positive(worldWidth,nameof(worldWidth));Positive(worldDepth,nameof(worldDepth));Finite(lookAheadMeters,nameof(lookAheadMeters));Finite(halfAngleDegrees,nameof(halfAngleDegrees));
            if(Math.Abs(headingX)>1e6||Math.Abs(headingZ)>1e6||Math.Abs(observer.X)>1e9||Math.Abs(observer.Z)>1e9||lookAheadMeters<1024||lookAheadMeters>10000||halfAngleDegrees<15||halfAngleDegrees>85)throw new ArgumentOutOfRangeException("Source demand view");
            double length=Math.Sqrt(headingX*headingX+headingZ*headingZ);if(length<1e-8){headingX=0;headingZ=1;}else{headingX/=length;headingZ/=length;}
            double reach=Math.Min(MaximumRadiusMeters,Math.Max(2560,lookAheadMeters+1920));
            double dx=destination.HasValue?destination.Value.X:observer.X+headingX*Math.Min(lookAheadMeters,MaximumRadiusMeters);
            double dz=destination.HasValue?destination.Value.Z:observer.Z+headingZ*Math.Min(lookAheadMeters,MaximumRadiusMeters);
            Finite(dx,nameof(destination));Finite(dz,nameof(destination));
            if(Math.Abs(dx)>1e9||Math.Abs(dz)>1e9)throw new ArgumentOutOfRangeException(nameof(destination));
            double travelX=dx-observer.X,travelZ=dz-observer.Z,targetDistance=Math.Sqrt(travelX*travelX+travelZ*travelZ);
            if(targetDistance>MaximumRadiusMeters-CellMeters){double scale=(MaximumRadiusMeters-CellMeters)/targetDistance;dx=observer.X+travelX*scale;dz=observer.Z+travelZ*scale;}
            long cx=(long)Math.Floor(observer.X/640),cz=(long)Math.Floor(observer.Z/640),tx=(long)Math.Floor(dx/640),tz=(long)Math.Floor(dz/640);
            var result=new List<ExplorationSourceDemandCell>(MaximumScannedCells);var seen=new HashSet<string>();
            void Add(long x,long z,bool explicitDestination=false)
            {
                double mx=(x+.5)*640,mz=(z+.5)*640,vx=mx-observer.X,vz=mz-observer.Z,distance=Math.Sqrt(vx*vx+vz*vz);
                bool current=x==cx&&z==cz, target=x==tx&&z==tz;
                var core=new ExplorationRefinementBounds(x*640.0,z*640.0,(x+1)*640.0,(z+1)*640.0);
                bool nearFront=NearFrontIntersects(core,observer,headingX,headingZ,halfAngleDegrees);
                bool nearby=core.DistanceSquared(observer.X,observer.Z)<NearbyReachMeters*NearbyReachMeters;
                if(current&&deferCurrent||distance>MaximumRadiusMeters+453||mx-320< -worldWidth*.5||mx+320>worldWidth*.5||mz-320< -worldDepth*.5||mz+320>worldDepth*.5)return;
                double ahead=vx*headingX+vz*headingZ,side=Math.Abs(vz*headingX-vx*headingZ);
                bool inView=ahead>=-453&&side<=Math.Max(0,ahead)*Math.Tan(halfAngleDegrees*Math.PI/180)+453;
                if(!current&&!target&&!nearFront&&!nearby&&(!(wideNeighborhood&&distance<=1280)&&!inView||distance>reach+453))return;
                // Far destinations use a bounded intermediate cell along the route,
                // never expand this into an unbounded destination neighborhood.
                if(explicitDestination&&distance>MaximumRadiusMeters+453)return;
                int priority=current?0:target?1:nearFront||nearby?2:distance<=1920&&inView?3:inView?4:5;
                var cell=new ExplorationSourceDemandCell(x,z,priority,distance+side*.35,nearFront);
                if(!seen.Add(cell.Key))return;
                double score=cell.Score-(retained!=null&&retained.Contains(cell.Key)?224:0);
                result.Add(new ExplorationSourceDemandCell(x,z,priority,score,nearFront));
            }
            for(int z=-8;z<=8;z++)for(int x=-8;x<=8;x++)Add(cx+x,cz+z);
            if(destination.HasValue&&Math.Abs(dx)<=1e9&&Math.Abs(dz)<=1e9)Add(tx,tz,true);
            result.Sort((a,b)=>{int p=a.Priority.CompareTo(b.Priority);if(p!=0)return p;int s=a.Score.CompareTo(b.Score);return s!=0?s:string.CompareOrdinal(a.Key,b.Key);});return result.ToArray();
        }
        public static long EstimatePayloadBytes(ExplorationRefinementBounds core,double blend)
        {
            Finite(blend,nameof(blend));if(blend<32||blend>256)throw new ArgumentOutOfRangeException(nameof(blend));
            double pad=blend+SurfaceProfile.SampleRadiusMax;
            long x0=(long)Math.Floor((core.MinimumX-pad)/5)-1,z0=(long)Math.Floor((core.MinimumZ-pad)/5)-1;
            long cols=(long)Math.Ceiling((core.MaximumX+pad)/5)+2-x0,rows=(long)Math.Ceiling((core.MaximumZ+pad)/5)+2-z0;
            if(cols<2||rows<2||cols>4096||rows>4096||cols*rows>262144)throw new ArgumentOutOfRangeException(nameof(core));
            return checked(cols*rows*24);
        }
        static bool Covers(ExplorationPublishedPatchInfo p,ExplorationSourceDemandCell cell)
            =>p.SpacingMeters<=5&&p.CoreBounds.MinimumX<=cell.Core.MinimumX&&p.CoreBounds.MinimumZ<=cell.Core.MinimumZ&&p.CoreBounds.MaximumX>=cell.Core.MaximumX&&p.CoreBounds.MaximumZ>=cell.Core.MaximumZ;
        /// <summary>Reserve mandatory resident footprints BEFORE buying demand.
        /// A selected core already covered by a reserved patch is charged once.</summary>
        public static ExplorationSourceDemandCell[] SelectForResidentCapacity(ExplorationSourceDemandCell[] eligible,
            ExplorationPublishedPatchInfo[] published,int maximumPatches,long maximumBytes,double blend,Func<string,bool> canEvict,
            out string[] mandatoryKeys,out long mandatoryBytes,out int deferred,out int priorityDeferred)
        {
            if(eligible==null||eligible.Length>MaximumScannedCells||published==null||published.Length>32||maximumPatches<1||maximumPatches>32||maximumBytes<1||canEvict==null)throw new ArgumentException("Bounded source selection inputs");
            var reserved=new HashSet<string>();var mandatory=new List<string>(32);long bytes=0;int slots=0;
            foreach(var patch in published)if(!canEvict(patch.Key)&&reserved.Add(patch.Key))
            {mandatory.Add(patch.Key);slots++;bytes+=patch.PayloadBytes;}
            mandatoryKeys=mandatory.ToArray();mandatoryBytes=bytes;deferred=priorityDeferred=0;
            var selected=new List<ExplorationSourceDemandCell>(32);var seen=new HashSet<string>();
            foreach(var cell in eligible)
            {
                if(!seen.Add(cell.Key))continue;
                if(selected.Count>=maximumPatches){deferred++;if(cell.Priority<=1)priorityDeferred++;continue;}
                string footprint=cell.Key;long cost=EstimatePayloadBytes(cell.Core,blend);bool covered=false;
                // Prefer already-reserved coverage, including a mandatory patch,
                // before charging an otherwise evictable enclosing published grid.
                foreach(var patch in published)if(Covers(patch,cell)&&reserved.Contains(patch.Key)){footprint=patch.Key;cost=patch.PayloadBytes;covered=true;break;}
                if(!covered)foreach(var patch in published)if(Covers(patch,cell)){footprint=patch.Key;cost=patch.PayloadBytes;break;}
                bool charged=reserved.Contains(footprint);
                if(!charged&&(slots>=maximumPatches||cost>maximumBytes-bytes))
                {deferred++;if(cell.Priority<=1)priorityDeferred++;continue;}
                // Mandatory content can already occupy the entire cap. Its own
                // ready core still belongs in demand; it must never be double charged.
                selected.Add(cell);if(!charged){reserved.Add(footprint);slots++;bytes+=cost;}
            }
            return selected.ToArray();
        }
        public static bool CanAdmitRetainingCoverage(ExplorationPublishedPatchInfo[] published,string incomingKey,long incomingBytes,
            int maximumPatches,long maximumBytes,ISet<string> retainedKeys,IReadOnlyList<ExplorationSourceDemandCell> desired,Func<string,bool> canEvict)
        {
            if(published==null||published.Length>32||desired==null||desired.Count>32||retainedKeys==null||canEvict==null||incomingBytes<1||maximumPatches<1||maximumPatches>32||maximumBytes<1)throw new ArgumentException("Bounded source admission inputs");
            var coverage=new List<ExplorationPublishedPatchInfo>(published);
            long bytes=incomingBytes;int count=1;
            for(int i=coverage.Count-1;i>=0;i--)
            {if(coverage[i].Key==incomingKey)coverage.RemoveAt(i);else{count++;bytes+=coverage[i].PayloadBytes;}}
            while(count>maximumPatches||bytes>maximumBytes)
            {
                int victim=-1;
                for(int i=0;i<coverage.Count;i++)if(canEvict(coverage[i].Key)&&(victim<0||coverage[i].LastUse<coverage[victim].LastUse))victim=i;
                if(victim<0)return false;
                var patch=coverage[victim];bool retained=retainedKeys.Contains(patch.Key);
                if(!retained)foreach(var cell in desired)if(patch.CoreBounds.MinimumX<=cell.Core.MinimumX&&patch.CoreBounds.MinimumZ<=cell.Core.MinimumZ&&patch.CoreBounds.MaximumX>=cell.Core.MaximumX&&patch.CoreBounds.MaximumZ>=cell.Core.MaximumZ){retained=true;break;}
                // Simulate the EXACT existing LRU victim order after touches. If it
                // would remove desired coverage, retain this completed request.
                if(retained)return false;
                count--;bytes-=patch.PayloadBytes;coverage.RemoveAt(victim);
            }
            return true;
        }
    }


    public readonly struct ExplorationSelectedSourceInfo
    {
        public readonly string Key;public readonly ExplorationRefinementBounds Core;public readonly int Priority;public readonly bool Ready,NearFront;
        internal ExplorationSelectedSourceInfo(ExplorationSourceDemandCell cell,bool ready){Key=cell.Key;Core=cell.Core;Priority=cell.Priority;Ready=ready;NearFront=cell.NearFront||cell.Current;}
    }
    public readonly struct ExplorationSourceRequestInfo
    {
        public readonly string Key,Reason;public readonly ExplorationRefinementBounds Core;public readonly int Priority;public readonly bool Selected,Current,Completed;
        internal ExplorationSourceRequestInfo(string key,ExplorationRefinementBounds core,int priority,bool selected,bool current,bool completed,string reason)
        {Key=key;Core=core;Priority=priority;Selected=selected;Current=current;Completed=completed;Reason=reason;}
    }

    /// <summary>Optional asynchronous decoded terrain extension. Every successful
    /// publication is an immutable grid with explicit player/query/prop protection.
    /// Unavailable or mismatched services leave the baked world playable.</summary>
    public sealed class ExplorationLiveRefinement:MonoBehaviour
    {
        public double TileMeters=640,BlendMeters=128,LookAheadMeters=1600;
        public int MaximumPatches=16;
        public long MaximumPayloadBytes=16L*1024*1024;
        public double ProtectedRadiusMeters=512,QueryAndPropGuardMeters=96;
        public ExplorationRefinementField WorldField {get;private set;}
        public bool Connected=>remote!=null&&remote.Connected&&accepted;
        public int PendingRequests=>work.Count;
        public string CurrentAreaStatus {get;private set;}="No current-area demand yet.";
        public bool CurrentAreaPending=>work.Exists(item=>item.Current);
        public int DiscardedStaleResults {get;private set;}
        public int DemandCandidateCells {get;private set;}
        public int NearFrontRequiredCells=>nearFrontCells.Length;
        public int NearFrontReadyCells {get;private set;}
        public int NearFrontGroundProtectedMissingCells {get;private set;}
        public bool NearFrontSourceReady=>NearFrontRequiredCells>0&&NearFrontReadyCells==NearFrontRequiredCells;
        // Bounded by the same290 candidate scan; contains required, not only selected cores.
        public ExplorationSelectedSourceInfo[] CaptureNearFrontSourceDemand()
        {var result=new ExplorationSelectedSourceInfo[nearFrontCells.Length];for(int i=0;i<result.Length;i++)result[i]=new ExplorationSelectedSourceInfo(nearFrontCells[i],IsReady(nearFrontCells[i].Core));return result;}
        public int DesiredSourceCells=>desired.Count;
        public int ReadyDesiredSourceCells {get;private set;}
        public int MissingDesiredSourceCells=>Math.Max(0,DesiredSourceCells-ReadyDesiredSourceCells);
        public int HeldCompletedRequests {get{int count=0;foreach(var item in work)if(item.Complete!=null&&item.Desired)count++;return count;}}
        public int BundledCandidateCells {get;private set;}
        public int CapacityDeferredCells {get;private set;}
        public int PriorityCapacityDeferredCells {get;private set;}
        public int MandatoryResidentPatches=>mandatorySourceKeys.Length;
        public long MandatoryResidentPayloadBytes {get;private set;}
        // Read from the main thread, like Tick. Copies are bounded32/32/2.
        public ExplorationSelectedSourceInfo[] CaptureSelectedSourceDemand()
        {var result=new ExplorationSelectedSourceInfo[desired.Count];for(int i=0;i<result.Length;i++)result[i]=new ExplorationSelectedSourceInfo(desired[i],IsReady(desired[i].Core));return result;}
        public string[] CaptureMandatorySourceKeys()=>(string[])mandatorySourceKeys.Clone();
        public ExplorationSourceRequestInfo[] CaptureSourceRequests()
        {
            var result=new ExplorationSourceRequestInfo[work.Count];
            for(int i=0;i<result.Length;i++)
            {
                var item=work[i];bool complete=item.Complete!=null;
                string reason=!item.Desired?"Stale request draining.":!complete?"Model request/copy in progress.":item.HoldReason??"Completed; awaiting publication check.";
                result[i]=new ExplorationSourceRequestInfo(item.Key,item.Core,item.Priority,item.Desired,item.Current,complete,reason);
            }
            return result;
        }
        public double DesiredCoreAreaSquareMeters=>DesiredSourceCells*640.0*640;
        public double ReadyCoreAreaSquareMeters=>ReadyDesiredSourceCells*640.0*640;
        public string SourceCoverageStatus=>"Selected640m source cells "+ReadyDesiredSourceCells+"/"+DesiredSourceCells+" at5m; "+MissingDesiredSourceCells+" missing, "+HeldCompletedRequests+" completed/held, "+CapacityDeferredCells+" selection/capacity deferred, "+PriorityCapacityDeferredCells+" current/destination deferred; "+MandatoryResidentPatches+" mandatory resident patches reserved. Near-front "+NearFrontReadyCells+"/"+NearFrontRequiredCells+" ready, "+NearFrontGroundProtectedMissingCells+" missing protected by ground guard. Far terrain outside these cells may use macro source.";
        public string Status {get;private set;}="Live decoding is off; bundled learned terrain is ready.";
        public event Action<ExplorationRefinementBounds> RegionChanged;
        sealed class Work
        {
            public string Key;
            public ExplorationRefinementBounds Core;
            public Task<ExplorationRefinementPatch> Task;
            public ExplorationRefinementPatch Complete;
            public bool Desired,Current;
            public int Priority;
            public float NextPublicationAttempt;
            public string HoldReason;
        }
        readonly List<Work> work=new List<Work>(2);
        readonly List<ExplorationSourceDemandCell> desired=new List<ExplorationSourceDemandCell>(16);
        readonly HashSet<string> desiredKeys=new HashSet<string>();
        string[] mandatorySourceKeys=Array.Empty<string>();
        ExplorationSourceDemandCell[] nearFrontCells=Array.Empty<ExplorationSourceDemandCell>();
        readonly Dictionary<string,float> retries=new Dictionary<string,float>();
        ExplorationAtlasField baked;
        WorldExplorer vista;
        RemoteTerrainField remote;
        Task connecting;
        string endpoint;
        bool requested,accepted,compatible,stopping;
        float nextConnect,nextRequests,nextStatus;
        Vector3 lastHeading=Vector3.forward;
        WorldPoint previousDemandObserver;
        float previousDemandTime;
        bool hasPreviousDemandObserver;
        public int RetiredDistantPatches {get;private set;}

        public ExplorationRefinementField Configure(ExplorationAtlasField source,WorldExplorer renderer,string endpoint="http://127.0.0.1:8787",bool enabled=true)
        {
            if(source==null)throw new ArgumentNullException(nameof(source));
            if(renderer==null)throw new ArgumentNullException(nameof(renderer));
            StopClient();baked=source;vista=renderer;this.endpoint=endpoint;
            hasPreviousDemandObserver=false;RetiredDistantPatches=0;
            nextRequests=nextStatus=0;retries.Clear();desired.Clear();desiredKeys.Clear();DemandCandidateCells=ReadyDesiredSourceCells=BundledCandidateCells=CapacityDeferredCells=PriorityCapacityDeferredCells=0;mandatorySourceKeys=Array.Empty<string>();MandatoryResidentPayloadBytes=0;nearFrontCells=Array.Empty<ExplorationSourceDemandCell>();NearFrontReadyCells=NearFrontGroundProtectedMissingCells=0;DiscardedStaleResults=0;CurrentAreaStatus="No current-area demand yet.";
            compatible=Math.Abs(source.WorldWidthMeters-source.OriginalWidthMeters)<.001&&Math.Abs(source.WorldDepthMeters-source.OriginalDepthMeters)<.001&&Math.Abs(source.VerticalScale-1)<1e-7;
            if(TileMeters!=ExplorationSourceDemandPlanner.CellMeters||double.IsNaN(BlendMeters)||BlendMeters<32||BlendMeters>256||double.IsNaN(LookAheadMeters)||LookAheadMeters<1024||LookAheadMeters>10000)
                throw new ArgumentOutOfRangeException("Live tile/blend/look-ahead controls");
            WorldField=new ExplorationRefinementField(source,source.DetailWorldId,5,MaximumPatches,MaximumPayloadBytes,ProtectedRadiusMeters,QueryAndPropGuardMeters);
            SetEnabled(enabled);return WorldField;
        }
        public void SetEnabled(bool enabled)
        {
            requested=enabled;
            if(!enabled){StopClient();Status="Live decoding is off; bundled and retained learned terrain remain playable.";return;}
            if(!compatible){StopClient();Status="Live decoding requires original map dimensions and vertical scale1; the resized baked world remains playable.";return;}
            nextConnect=0;Status="Looking for the matching local model service…";
        }
        public bool HasActiveDetail(double x,double z)=>WorldField!=null&&WorldField.HasActiveDetail(x,z);
        public double ResolutionMetersAt(double x,double z)=>WorldField==null?double.NaN:WorldField.ResolutionMetersAt(x,z);
        public void Tick(WorldPoint observer,Vector3 forward,ExplorationRefinementPublicationPolicy publicationPolicy=default,bool deferCurrentArea=false,WorldPoint? destination=null,double viewHorizontalHalfAngleDegrees=55)
        {
            DrainStopped();
            if(WorldField==null)return;WorldField.Touch(observer);
            if(!requested||!compatible)return;
            if(stopping){Status="Canceled model requests are draining before reconnection; bundled terrain continues.";return;}
            if(remote==null&&Time.unscaledTime>=nextConnect)StartConnection();
            if(connecting!=null&&connecting.IsCompleted)
            {
                Task completed=connecting;connecting=null;
                if(completed.IsFaulted||completed.IsCanceled)
                {string why=completed.IsFaulted?completed.Exception.GetBaseException().Message:"canceled";StopClient();nextConnect=Time.unscaledTime+30;Status="Local model unavailable; bundled learned terrain continues. "+why;return;}
                if(remote==null||remote.Status.world_id!=baked.DetailWorldId||Math.Abs(remote.Status.spacing_m-5)>1e-7)
                {StopClient();nextConnect=Time.unscaledTime+30;Status="Model world identity or5m coordinates differ; bundled learned terrain continues.";return;}
                accepted=true;Status="Matching InfiniteDiffusion service connected; current-area and forward demand enabled.";
            }
            if(remote!=null&&!string.IsNullOrEmpty(remote.TerminalFailure))
            {Status="Model stopped; bundled and retained learned terrain continue. "+remote.TerminalFailure;StopClient();nextConnect=Time.unscaledTime+30;return;}
            if(Time.unscaledTime>=nextRequests)
            {
                nextRequests=Time.unscaledTime+.5f;
                Vector3 horizontal=new Vector3(forward.x,0,forward.z);
                if(horizontal.x*horizontal.x+horizontal.z*horizontal.z>.0001f)lastHeading=horizontal.normalized;
                WorldPoint? predicted=hasPreviousDemandObserver?ExplorationSourceDemandPlanner.PredictDestination(previousDemandObserver,observer,Time.unscaledTime-previousDemandTime,LookAheadMeters):null;
                previousDemandObserver=observer;previousDemandTime=Time.unscaledTime;hasPreviousDemandObserver=true;
                QueueDemand(observer,lastHeading,deferCurrentArea,publicationPolicy,destination??predicted,viewHorizontalHalfAngleDegrees);
                RetireDistantSource(observer,publicationPolicy);
            }
            // Source extent/readiness planning is read-only while connecting or
            // unavailable, so bundled near detail never requires a service reply.
            if(!Connected)return;
            // Current-area completion is considered first. Stale in-flight tasks
            // keep their two-slot ownership until completion; travel cannot orphan them.
            work.Sort((a,b)=>a.Priority.CompareTo(b.Priority));
            for(int i=0;i<work.Count;)
            {
                Work item=work[i];
                if(item.Complete==null&&item.Task.IsCompleted)
                {
                    if(item.Task.IsFaulted||item.Task.IsCanceled)
                    {
                        if(item.Task.IsFaulted){var observed=item.Task.Exception;}
                        if(item.Desired)retries[item.Key]=Time.unscaledTime+30;
                        work.RemoveAt(i);continue;
                    }
                    item.Complete=item.Task.Result;
                }
                if(item.Complete!=null&&!item.Desired)
                {DiscardedStaleResults++;work.RemoveAt(i);continue;}
                if(item.Complete!=null&&IsReady(item.Core)){work.RemoveAt(i);continue;}
                if(item.Complete!=null&&Time.unscaledTime>=item.NextPublicationAttempt)
                {
                    item.NextPublicationAttempt=Time.unscaledTime+.5f;
                    if(!CanAdmitRetainingDemand(item,observer,publicationPolicy))item.HoldReason="Resident capacity held for selected source coverage.";
                    else if(WorldField.TryPublish(item.Complete,observer,out ExplorationRefinementBounds[] changes,publicationPolicy))
                    {
                        foreach(var bounds in changes)
                        {
                            vista.RefreshExternalRegion(bounds.MinimumX,bounds.MinimumZ,bounds.MaximumX,bounds.MaximumZ);
                            RegionChanged?.Invoke(bounds);
                        }
                        work.RemoveAt(i);continue;
                    }
                    else item.HoldReason=WorldField.LastPublishRefusal;
                }
                if(item.Current)CurrentAreaStatus=item.Complete==null?"Current area: requesting verified5m source.":"Current area: "+item.HoldReason;
                i++;
            }
            RefreshCoverageStats();
            double resolution=WorldField.ResolutionMetersAt(observer.X,observer.Z);
            if(resolution<=5.000001)CurrentAreaStatus="Current area:5m learned source active.";
            if(Time.unscaledTime>=nextStatus)
            {
                nextStatus=Time.unscaledTime+.5f;
                Status="Live model connected - "+WorldField.ResidentPatches+" retained decoded patches - "+work.Count+" pending (maximum2) - "+
                    "source here "+resolution.ToString("F1",System.Globalization.CultureInfo.InvariantCulture)+"m. "+CurrentAreaStatus+" "+SourceCoverageStatus;
                if(work.Exists(item=>!item.Desired))Status+=" Stale in-flight work is draining before replacement.";
            }
        }
        void RetireDistantSource(WorldPoint observer,ExplorationRefinementPublicationPolicy policy)
        {
            // One immutable publication per planning interval, at most32 examined.
            // Keep a full cell of hysteresis beyond the maximum request radius.
            double radius=ExplorationSourceDemandPlanner.MaximumRadiusMeters+TileMeters;
            foreach(var patch in WorldField.CapturePublishedCoverage())
            {
                if(desiredKeys.Contains(patch.Key)||patch.CoreBounds.Expanded(BlendMeters+QueryAndPropGuardMeters).DistanceSquared(observer.X,observer.Z)<=radius*radius)continue;
                if(!WorldField.TryRetire(patch.Key,observer,out var bounds,policy))continue;
                RetiredDistantPatches++;vista.RefreshExternalRegion(bounds.MinimumX,bounds.MinimumZ,bounds.MaximumX,bounds.MaximumZ);RegionChanged?.Invoke(bounds);break;
            }
        }
        void StartConnection()
        {
            try
            {
                remote=new RemoteTerrainField(baked,endpoint,false,4,maximumResidentBytes:8L*1024*1024);
                connecting=remote.InitializeAsync();accepted=false;nextConnect=Time.unscaledTime+30;
            }
            catch(Exception exception){StopClient();nextConnect=Time.unscaledTime+30;Status="Local model unavailable; bundled world continues. "+exception.Message;}
        }
        bool FullyBaked(ExplorationRefinementBounds core)
        {
            var influence=core.Expanded(BlendMeters);
            return baked.FineWeightAt(influence.MinimumX,influence.MinimumZ)>=.999999&&baked.FineWeightAt(influence.MaximumX,influence.MaximumZ)>=.999999&&
                baked.FineWeightAt(influence.MinimumX,influence.MaximumZ)>=.999999&&baked.FineWeightAt(influence.MaximumX,influence.MinimumZ)>=.999999;
        }
        bool RequiredBounds(ExplorationRefinementBounds core,out ExplorationRefinementBounds required)
        {
            required=core.Expanded(BlendMeters+SurfaceProfile.SampleRadiusMax+10);
            return required.MinimumX>=-baked.WorldWidthMeters*.5&&required.MaximumX<=baked.WorldWidthMeters*.5&&required.MinimumZ>=-baked.WorldDepthMeters*.5&&required.MaximumZ<=baked.WorldDepthMeters*.5;
        }
        bool IsReady(ExplorationRefinementBounds core)=>FullyBaked(core)||WorldField.HasPublishedCoreCoverage(core);
        void RefreshCoverageStats()
        {
            ReadyDesiredSourceCells=0;foreach(var cell in desired)if(IsReady(cell.Core))ReadyDesiredSourceCells++;
            NearFrontReadyCells=0;foreach(var cell in nearFrontCells)if(IsReady(cell.Core))NearFrontReadyCells++;
        }
        void TouchDesiredResidents()
        {
            // Existing LRU mechanism, view demand rather than fabricated player
            // safety evidence. Publication always receives the real observer.
            for(int i=desired.Count-1;i>=0;i--)
            {var c=desired[i].Core;if(WorldField.HasPublishedCoreCoverage(c))WorldField.Touch(new WorldPoint((c.MinimumX+c.MaximumX)*.5,0,(c.MinimumZ+c.MaximumZ)*.5));}
        }
        bool CanAdmitRetainingDemand(Work incoming,WorldPoint observer,ExplorationRefinementPublicationPolicy policy)
        {
            TouchDesiredResidents();
            return ExplorationSourceDemandPlanner.CanAdmitRetainingCoverage(WorldField.CapturePublishedCoverage(),incoming.Key,incoming.Complete.PayloadBytes,
                WorldField.MaximumResidentPatches,WorldField.MaximumResidentPayloadBytes,desiredKeys,desired,key=>WorldField.CanEvictPublishedPatch(key,observer,policy));
        }
        void QueueDemand(WorldPoint observer,Vector3 heading,bool deferCurrentArea,ExplorationRefinementPublicationPolicy policy,WorldPoint? destination,double halfAngle)
        {
            bool wider=false;
            try
            {
                double r=1280;var bounds=WorldField.QueryBounds(observer.X-r,observer.Z-r,observer.X+r,observer.Z+r);
                wider=policy.AllowsNeighborhoodDemand(observer,bounds.Maximum);
            }
            catch(Exception){ } // Missing bounds retain the narrower protected demand.
            var candidates=ExplorationSourceDemandPlanner.Build(observer,heading.x,heading.z,LookAheadMeters,baked.WorldWidthMeters,baked.WorldDepthMeters,deferCurrentArea,wider,halfAngle,destination,desiredKeys);
            DemandCandidateCells=candidates.Length;BundledCandidateCells=0;NearFrontGroundProtectedMissingCells=0;
            var requiredNear=new List<ExplorationSourceDemandCell>();
            foreach(var cell in candidates)if((cell.Current||cell.NearFront)&&RequiredBounds(cell.Core,out _))requiredNear.Add(cell);
            nearFrontCells=requiredNear.ToArray();
            var eligible=new List<ExplorationSourceDemandCell>(candidates.Length);
            foreach(var cell in candidates)
            {
                if(!RequiredBounds(cell.Core,out _))continue;
                if(FullyBaked(cell.Core)){BundledCandidateCells++;continue;}
                bool protectedHere=!wider&&cell.Core.Expanded(BlendMeters+QueryAndPropGuardMeters).DistanceSquared(observer.X,observer.Z)<=ProtectedRadiusMeters*ProtectedRadiusMeters;
                bool ready=IsReady(cell.Core);
                if(protectedHere&&!ready&&(cell.Current||cell.NearFront))NearFrontGroundProtectedMissingCells++;
                // Retain already-ready protected near cores. Missing guarded cores
                // need pre-spawn/verified flight; ranking never bypasses that safety.
                if(!cell.Current&&protectedHere&&!ready)continue;
                eligible.Add(cell);
            }
            var selected=ExplorationSourceDemandPlanner.SelectForResidentCapacity(eligible.ToArray(),WorldField.CapturePublishedCoverage(),
                WorldField.MaximumResidentPatches,WorldField.MaximumResidentPayloadBytes,BlendMeters,key=>WorldField.CanEvictPublishedPatch(key,observer,policy),
                out string[] mandatory,out long mandatoryBytes,out int deferred,out int priorityDeferred);
            mandatorySourceKeys=mandatory;MandatoryResidentPayloadBytes=mandatoryBytes;CapacityDeferredCells=deferred;PriorityCapacityDeferredCells=priorityDeferred;
            desired.Clear();desiredKeys.Clear();foreach(var cell in selected){desired.Add(cell);desiredKeys.Add(cell.Key);}
            if(priorityDeferred>0)CurrentAreaStatus="Current/destination source demand waits for mandatory resident capacity; protected detail is retained.";
            foreach(var item in work){item.Desired=false;item.Current=false;}
            foreach(var cell in desired)
            {
                Work existing=work.Find(item=>item.Key==cell.Key);
                if(existing!=null){existing.Desired=true;existing.Current=cell.Current;existing.Priority=cell.Priority;continue;}
                if(IsReady(cell.Core))continue;
                if(!Connected)continue; // Metadata planning never starts offline requests.
                if(retries.TryGetValue(cell.Key,out float until)&&Time.unscaledTime<until)
                {if(cell.Current)CurrentAreaStatus="Current area: model request failed; bounded30s retry pending.";continue;}
                if(work.Count>=2){if(cell.Current)CurrentAreaStatus="Current area: waiting for the existing two requests to drain.";continue;}
                RequiredBounds(cell.Core,out var required);
                var request=new Work{Key=cell.Key,Core=cell.Core,Desired=true,Current=cell.Current,Priority=cell.Priority};request.Task=FetchCopy(cell.Core,cell.Key,required);work.Add(request);
            }
            if(deferCurrentArea)CurrentAreaStatus="Current area: fast travel; destination demand resumes on slowing/stopping.";
            TouchDesiredResidents();RefreshCoverageStats();
            if(retries.Count>64)retries.Clear();
        }
        async Task<ExplorationRefinementPatch> FetchCopy(ExplorationRefinementBounds core,string key,ExplorationRefinementBounds required)
        {
            RemoteTerrainField client=remote;string id=baked.DetailWorldId;
            await client.PrefetchAsync(required.MinimumX,required.MinimumZ,required.MaximumX,required.MaximumZ);
            IWorldField snapshot=client.ReadySnapshot(required.MinimumX,required.MinimumZ,required.MaximumX,required.MaximumZ);
            double blend=BlendMeters;
            return await Task.Run(()=>ExplorationRefinementPatch.CopySnapshot(snapshot,key,id,core,blend,SurfaceProfile.SampleRadiusMax,5));
        }
        void StopClient()
        {
            // Dispose cancels transport, but retain all task ownership until terminal.
            // Repeated toggles/reconfiguration cannot create unbounded orphan work.
            if(remote!=null)remote.Dispose();remote=null;accepted=false;stopping=true;
            DrainStopped();
        }
        void DrainStopped()
        {
            if(!stopping)return;
            for(int i=work.Count-1;i>=0;i--)if(work[i].Task.IsCompleted)
            {if(work[i].Task.IsFaulted){var observed=work[i].Task.Exception;}work.RemoveAt(i);}
            if(connecting!=null&&connecting.IsCompleted)
            {if(connecting.IsFaulted){var observed=connecting.Exception;}connecting=null;}
            stopping=work.Count!=0||connecting!=null;
        }
        static void ObserveFailure(Task task)
        {if(task!=null)task.ContinueWith(completed=>{var observed=completed.Exception;},TaskContinuationOptions.OnlyOnFaulted);}
        void OnDestroy(){StopClient();foreach(var item in work)ObserveFailure(item.Task);ObserveFailure(connecting);}
    }
}
