using System;
using System.Globalization;
using System.Collections.Generic;
using System.Text;
using AshenVoxel.WorldScale;

namespace RootContactPrototype
{
    // Single-owner adapter. Provider snapshot/identity methods are trusted bounded callbacks.
    // This is not a live-current certificate for cache publication: check context and owner too.
    public sealed class RootTerrainSnapshot : ITerrain
    {
        public const int Version=1, MaxIdentityChars=4096, MaxUnwrap=8;
        public const int MaxSideCells=1024, MaxAreaCells=262144, MaxQueryAttempts=1048576;
        public const long MaxGlobalCell=10000001000L;
        public const string PolicyIdentity="bare-root-terrain-v1;cell=.1;center;SurfaceTop;no-snow;no-resampling";
        readonly IWorldField live,snapshot;
        readonly string capturedIdentity;
        readonly int queryLimit;
        public string SourceIdentity {get;}
        public long MinX {get;} public long MaxX {get;} public long MinZ {get;} public long MaxZ {get;}
        public int QueryAttempts {get;private set;}
        public int SampleCalls {get;private set;}
        public int RemainingQueries=>queryLimit-QueryAttempts;
        public string Failure {get;private set;}="";
        public bool Failed=>Failure.Length!=0;
        RootTerrainSnapshot(IWorldField live,IWorldField snapshot,string id,long minX,long maxX,long minZ,long maxZ,int queryLimit)
        {
            this.live=live;this.snapshot=snapshot;capturedIdentity=id;this.queryLimit=queryLimit;
            MinX=minX;MaxX=maxX;MinZ=minZ;MaxZ=maxZ;
            // Query budget is an operational limit, not a terrain-content identity.
            SourceIdentity=PolicyIdentity+"/"+Policy.Hash(Encoding.UTF8.GetBytes(id+"/rect="+
                minX.ToString(CultureInfo.InvariantCulture)+","+maxX.ToString(CultureInfo.InvariantCulture)+","+
                minZ.ToString(CultureInfo.InvariantCulture)+","+maxZ.ToString(CultureInfo.InvariantCulture)));
        }
        static string Identity(IWorldField field)
        {
            string id=field.SourceIdentity;
            if(string.IsNullOrEmpty(id)||id.Length>MaxIdentityChars)throw new ArgumentException("identity-limit");
            return id;
        }
        sealed class NestedSurfaceException:Exception {}
        static IWorldField Bare(IWorldField input)
        {
            IWorldField retained=input,cursor=input;int count=0;bool insideDetail=false;
            var seen=new HashSet<IWorldField>();
            while(cursor is SurfaceWorldField||cursor is TerrainDetailField)
            {
                // Both supported wrappers are sealed and retain reference equality.
                if(++count>MaxUnwrap||!seen.Add(cursor))throw new ArgumentException("wrapper-depth-or-cycle");
                if(cursor is SurfaceWorldField surface)
                {
                    if(insideDetail)throw new NestedSurfaceException();
                    retained=cursor=surface.BaseField;
                }
                else
                {
                    insideDetail=true;cursor=((TerrainDetailField)cursor).BaseField;
                }
            }
            if(cursor==null)throw new ArgumentException("null-field");
            return retained;
        }
        public static bool TryCapture(IWorldField source,long minX,long maxX,long minZ,long maxZ,int maxQueries,
            out RootTerrainSnapshot result,out string reason)
        {
            result=null;reason="";
            if(minX< -MaxGlobalCell||maxX>MaxGlobalCell||minZ< -MaxGlobalCell||maxZ>MaxGlobalCell||minX>maxX||minZ>maxZ)
            {reason="rectangle-range";return false;}
            long width=maxX-minX+1,depth=maxZ-minZ+1;
            if(width>MaxSideCells||depth>MaxSideCells||width*depth>MaxAreaCells||maxQueries<1||maxQueries>MaxQueryAttempts)
            {reason="work-limit";return false;}
            try
            {
                IWorldField bare=Bare(source);string identity=Identity(bare);
                if(!(bare is IWorldFieldSnapshotProvider provider)){reason="snapshot-provider-required";return false;}
                IWorldField captured=Bare(provider.CaptureSnapshot());
                // Preserve TerrainDetailField's own snapshot; never strip its procedural relief.
                if(identity!=Identity(bare)||identity!=Identity(captured)){reason="capture-stale";return false;}
                result=new RootTerrainSnapshot(bare,captured,identity,minX,maxX,minZ,maxZ,maxQueries);return true;
            }
            catch(NestedSurfaceException){reason="nested-surface-wrapper";return false;}
            catch(Exception){reason="capture-error-or-contract";return false;}
        }
        bool Reject(string reason){if(!Failed)Failure=reason;return false;}
        public void Cancel()=>Reject("cancelled");
        public bool IsCurrent()
        {
            if(Failed)return false;
            try{return capturedIdentity==Identity(live)&&capturedIdentity==Identity(snapshot)||Reject("stale-source");}
            catch(Exception){return Reject("identity-error");}
        }
        public bool TryGetBareTop(long globalX,long globalZ,out double meters)
        {
            meters=0;if(!IsCurrent())return false;
            if(QueryAttempts==queryLimit)return Reject("query-limit");
            QueryAttempts++;
            if(globalX<MinX||globalX>MaxX||globalZ<MinZ||globalZ>MaxZ)return Reject("outside-rectangle");
            try
            {
                WorldPoint center=new WorldCell(globalX,0,globalZ).Center;
                SampleCalls++;double raw=snapshot.Sample(center.X,center.Z).HeightMeters;
                double top=VoxelTerrainPage.SurfaceTop(raw);
                if(!IsCurrent())return false;
                meters=top;return true;
            }
            catch(Exception){return Reject("sample-error");}
        }
    }
}
