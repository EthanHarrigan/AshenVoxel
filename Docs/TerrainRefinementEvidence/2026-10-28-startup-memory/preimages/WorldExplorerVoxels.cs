using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using AshenVoxel.WorldScale;
using UnityEngine;
using UnityEngine.Rendering;

namespace AshenVoxel.WorldScaleRuntime
{
    public sealed partial class WorldExplorer
    {
        [Header("World voxel detail")]
        [Tooltip("Next regeneration: exposed voxel surfaces across every terrain LOD; same10cm implicit material authority.")]
        public bool UseVoxelLodTerrain;
        [Tooltip("Optional rendered-cache representation. Distant indexed height grids; near/contact matter and cubes remain unchanged.")]
        public bool EnableDistantHeightProxies;
        HeightProxyDiskCache heightProxyDiskCache;
        bool heightProxyCacheConfigured;
        long heightProxyCacheHits,heightProxyCacheMisses,heightProxyCacheWrites,heightProxyCacheWriteFailures,heightProxyCacheOwnedBytes;
        int heightProxyCacheRecords;
        string heightProxyCacheStatus="Off";
        public static string DefaultHeightProxyDiskCacheDirectory=>Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"AshenVoxel","HeightProxyCacheV1");
        public bool HeightProxyDiskCacheEnabled=>heightProxyDiskCache!=null;
        public long HeightProxyDiskCacheHits=>Interlocked.Read(ref heightProxyCacheHits);
        public long HeightProxyDiskCacheMisses=>Interlocked.Read(ref heightProxyCacheMisses);
        public long HeightProxyDiskCacheWrites=>Interlocked.Read(ref heightProxyCacheWrites);
        public long HeightProxyDiskCacheWriteFailures=>Interlocked.Read(ref heightProxyCacheWriteFailures);
        public long HeightProxyDiskCacheOwnedBytes=>Interlocked.Read(ref heightProxyCacheOwnedBytes);
        public int HeightProxyDiskCacheRecords=>Volatile.Read(ref heightProxyCacheRecords);
        public string HeightProxyDiskCacheStatus=>Volatile.Read(ref heightProxyCacheStatus);
        /// <summary>Optional next-Play cache; call once before terrain initialization.
        /// Construction does no file IO. All cache operations use the existing worker.</summary>
        public bool ConfigureHeightProxyDiskCache(bool enabled,string absoluteDirectory,long budgetBytes=HeightProxyDiskCache.DefaultBudgetBytes)
        {
            if(heightProxyCacheConfigured||field!=null||voxelBuildJob!=null)
                throw new InvalidOperationException("Configure disk cache once before terrain initialization; restart Play to change it.");
            heightProxyCacheConfigured=true;if(!enabled)return true;
            try
            {
                if(!Path.IsPathFullyQualified(absoluteDirectory)||new Uri(absoluteDirectory).IsUnc)throw new ArgumentException("Dedicated absolute local cache directory required.");
                heightProxyDiskCache=new HeightProxyDiskCache(absoluteDirectory,true,budgetBytes);
                SetHeightProxyCacheStatus("Enabled; awaiting proxy work");return true;
            }
            catch(Exception e){SetHeightProxyCacheStatus("Unavailable: "+e.GetType().Name);return false;}
        }
        void SetHeightProxyCacheStatus(string status)=>Volatile.Write(ref heightProxyCacheStatus,status.Length>128?status.Substring(0,128):status);
        void RecordHeightProxyCache(HeightProxyDiskCache cache)
        {Interlocked.Exchange(ref heightProxyCacheOwnedBytes,cache.OwnedDiskBytes);Volatile.Write(ref heightProxyCacheRecords,cache.RecordCount);}
        HeightProxySurface BuildCachedHeightProxy(HeightProxyDiskCache cache,SurfaceMaterialSampler sampler,LodTile tile,int n,double skirt,CancellationToken cancel)
        {
            HeightProxyCacheKey key=null;string miss="";
            if(cache!=null)
            {
                try
                {
                    cancel.ThrowIfCancellationRequested();key=HeightProxyCacheKey.Create(sampler,tile,n,skirt);
                    bool hit=cache.TryLoad(key,out HeightProxySurface loaded,out miss,cancel);RecordHeightProxyCache(cache);cancel.ThrowIfCancellationRequested();
                    if(hit){Interlocked.Increment(ref heightProxyCacheHits);SetHeightProxyCacheStatus("Hit");return loaded;}
                }
                catch(OperationCanceledException){throw;}
                catch(Exception e){key=null;miss="key/load: "+e.GetType().Name;}
                Interlocked.Increment(ref heightProxyCacheMisses);SetHeightProxyCacheStatus("Miss: "+miss);
            }
            cancel.ThrowIfCancellationRequested();var built=HeightProxySurface.Build(sampler,tile,n,skirt,cancel,parallelSampling:true);
            if(cache!=null&&key!=null)
            {
                try
                {
                    bool stored=cache.Store(key,built,out string reason,cancel);RecordHeightProxyCache(cache);cancel.ThrowIfCancellationRequested();
                    if(stored){if(reason=="stored")Interlocked.Increment(ref heightProxyCacheWrites);}
                    else Interlocked.Increment(ref heightProxyCacheWriteFailures);
                    SetHeightProxyCacheStatus("Miss ("+miss+"); "+reason);
                }
                catch(OperationCanceledException){throw;}
                catch(Exception e){Interlocked.Increment(ref heightProxyCacheWriteFailures);SetHeightProxyCacheStatus("Write skipped: "+e.GetType().Name);}
            }
            cancel.ThrowIfCancellationRequested();return built;
        }
        [Range(512,4096)] public float DistantHeightProxyMinimumMeters=768;
        public string TerrainRepresentationMode=>!UseVoxelLodTerrain?"Legacy height proxies":EnableDistantHeightProxies?"Distant height proxies + near cubes":"Exposed voxels";
        bool WantsHeightProxy(LodTile tile)
        {
            if(!UseVoxelLodTerrain||!EnableDistantHeightProxies)return false;
            double dx=Math.Max(tile.OriginXMeters-cameraX,Math.Max(0,cameraX-tile.OriginXMeters-tile.SizeMeters));
            double dz=Math.Max(tile.OriginZMeters-cameraZ,Math.Max(0,cameraZ-tile.OriginZMeters-tile.SizeMeters));
            double distance=Mathf.Clamp(DistantHeightProxyMinimumMeters,512,4096);
            return dx*dx+dz*dz>=distance*distance;
        }
        bool NeedsTerrainQuality(LodTile tile,Surface s)=>s.Quads<VoxelQuadsFor(tile)||s.HeightProxy!=WantsHeightProxy(tile);
        [Range(.5f,8)] public float VoxelTargetPixels=1.5f;
        [Range(1,LodSettings.MaximumSplitFactor)] public float VoxelPlanSplitFactor=2.2f;
        public bool VoxelGreedyVisibleDetail;
        [Range(16,160)] public float VoxelFullDetailRadius=100;
        [Range(128,2048)] public int VoxelContactBudgetMB=1024;
        public int ContactTiles=>contactSurfaces.Count;
        public int ContactPendingTiles=>contactPending.Count+contactDeferred.Count+(currentBuild!=null&&currentBuild.Work.Contact?1:0);
        public long ContactMeshBytes {get;private set;}
        public long ContactTriangles {get;private set;}
        readonly Dictionary<TileKey,Surface> contactSurfaces=new Dictionary<TileKey,Surface>();
        readonly HashSet<TileKey> contactDesired=new HashSet<TileKey>();
        readonly Dictionary<TileKey,long> contactDeferred=new Dictionary<TileKey,long>();
        public int ContactDeferredTiles=>contactDeferred.Count;
        long contactObservedBudgetBytes=-1;
        long ContactBudgetBytes=>(long)Mathf.Clamp(VoxelContactBudgetMB,128,2048)*1024*1024;
        public long ContactMeshLimitBytes=>ContactBudgetBytes;
        readonly List<Work> contactPending=new List<Work>();
        readonly Vector4[] contactMask=new Vector4[64];
        long contactCenterX=long.MinValue,contactCenterZ=long.MinValue;
        int contactRevision=-1;float contactRadius=-1;
        Material contactMaterial;
        bool contactAltitudeDeferring,contactAltitudeBoundsCached,contactAltitudeBoundsKnown;
        long contactAltitudeX,contactAltitudeZ;int contactAltitudeRevision;float contactAltitudeRadius;
        double contactAltitudeCeiling;
        public bool ContactAltitudeDeferred=>contactAltitudeDeferring;
        public int ContactAltitudeHeldTiles=>contactAltitudeDeferring?contactPending.Count:0;
        public bool ContactAltitudeBoundsKnown=>contactAltitudeBoundsKnown;
        public double ContactAltitudeCeilingMeters=>contactAltitudeCeiling;
        public int ContactAltitudeRetiredTiles {get;private set;}
        void UpdateContactAltitudePriority(long cx,long cz,float radius)
        {
            bool hold=false;
            // Unknown motion and fast descent preserve immediate arrival prefetch.
            if(voxelContactMotionKnown&&voxelMotionY>=-12)
            {
                if(!contactAltitudeBoundsCached||contactAltitudeX!=cx||contactAltitudeZ!=cz||contactAltitudeRevision!=revision||contactAltitudeRadius!=radius)
                {
                    contactAltitudeX=cx;contactAltitudeZ=cz;contactAltitudeRevision=revision;contactAltitudeRadius=radius;
                    contactAltitudeBoundsCached=true;contactAltitudeBoundsKnown=false;
                    if(BareField(field) is IWorldFieldBounds bounded)
                    {
                        // One conservative local-footprint query per cell/radius/
                        // source revision, not repeated height sampling per queued tile.
                        int reach=(int)Math.Ceiling(radius/VoxelLodSurface.BaseTileMeters)+1;
                        double size=VoxelLodSurface.BaseTileMeters;
                        try
                        {
                            HeightBounds b=bounded.QueryBounds((cx-reach)*size-SurfaceHalo,(cz-reach)*size-SurfaceHalo,
                                (cx+reach+1)*size+SurfaceHalo,(cz+reach+1)*size+SurfaceHalo);
                            contactAltitudeCeiling=b.Maximum+CurrentSurfaceProfile.MaxSnowDepthMeters+.1;
                            contactAltitudeBoundsKnown=!double.IsNaN(contactAltitudeCeiling)&&!double.IsInfinity(contactAltitudeCeiling);
                        }
                        catch(Exception){contactAltitudeBoundsKnown=false;}
                    }
                }
                hold=contactAltitudeBoundsKnown&&cameraY>contactAltitudeCeiling+256;
            }
            if(contactAltitudeDeferring!=hold){contactAltitudeDeferring=hold;voxelRefinementScanPending=true;}
            if(hold&&currentBuild!=null&&currentBuild.Work.Contact)
            {
                Work work=currentBuild.Work;CancelBuild();
                if(work.Revision==revision&&contactDesired.Contains(work.Tile.Key)&&!contactPending.Exists(p=>p.Tile.Key.Equals(work.Tile.Key)))contactPending.Add(work);
            }
        }
        bool HasRunnableContactWork()=>!contactAltitudeDeferring&&contactPending.Count>0&&(!voxelTravelDeferring||HasUrgentContactWork());
        bool HasContactFallback(TileKey contact)
        {
            for(int level=0;level<=14;level++)
            {
                long divisor=1L<<level;
                var key=new TileKey((long)Math.Floor((double)contact.X/divisor),(long)Math.Floor((double)contact.Z/divisor),level);
                if(active.Contains(key)&&!externallyDirty.Contains(key)&&surfaces.TryGetValue(key,out Surface s)&&s.Object!=null&&s.Mesh!=null)return true;
            }
            return false;
        }
        void RetireAltitudeContacts()
        {
            if(!contactAltitudeDeferring||!contactAltitudeBoundsKnown)return;
            // A bounded four retirements/frame; retain every request and require
            // complete aligned active far coverage before clearing its contact mask.
            remove.Clear();foreach(TileKey key in contactSurfaces.Keys)
                if(contactDesired.Contains(key)&&HasContactFallback(key)){remove.Add(key);if(remove.Count==4)break;}
            if(remove.Count==0)return;
            foreach(TileKey key in remove)
            {
                ReleaseContact(key);ContactAltitudeRetiredTiles++;
                if(!contactDeferred.ContainsKey(key)&&!contactPending.Exists(p=>p.Tile.Key.Equals(key)))
                {double size=VoxelLodSurface.BaseTileMeters;contactPending.Add(new Work(new LodTile(key,key.X*size,key.Z*size,size,RemoteTerrainField.MinimumSupportedHeight,RemoteTerrainField.MaximumSupportedHeight),revision,true,true));}
            }
            WakeAdmissibleContactRequests(ContactBudgetBytes);UpdateContactMask();
        }
        void RefreshContactPlan()
        {
            if(!UseVoxelLodTerrain||coarseStartup||field==null)return;
            long budget=ContactBudgetBytes;
            if(contactObservedBudgetBytes>=0&&budget>contactObservedBudgetBytes)WakeAdmissibleContactRequests(budget);
            contactObservedBudgetBytes=budget;
            double size=VoxelLodSurface.BaseTileMeters;
            long cx=(long)Math.Floor(cameraX/size),cz=(long)Math.Floor(cameraZ/size);
            float radius=Mathf.Clamp(VoxelFullDetailRadius,16,160);
            UpdateContactAltitudePriority(cx,cz,radius);
            if(cx==contactCenterX&&cz==contactCenterZ&&revision==contactRevision&&radius==contactRadius)
            {
                RetireAltitudeContacts();
                // Vertical descent can make the existing queue urgent without
                // changing XZ cells, source revision or the contact radius.
                if(currentBuild!=null&&!currentBuild.Work.Contact&&HasUrgentContactWork())
                {
                    Work work=currentBuild.Work;CancelBuild();
                    if(work.Revision==revision&&desired.Contains(work.Tile.Key))pending.Enqueue(work);
                }
                return;
            }
            contactCenterX=cx;contactCenterZ=cz;contactRadius=radius;contactRevision=revision;
            contactDesired.Clear();contactPending.Clear();contactDeferred.Clear();
            int reach=(int)Math.Ceiling(radius/size)+1;
            for(long z=cz-reach;z<=cz+reach;z++)for(long x=cx-reach;x<=cx+reach;x++)
            {
                double dx=Math.Max(x*size-cameraX,Math.Max(0,cameraX-(x+1)*size));
                double dz=Math.Max(z*size-cameraZ,Math.Max(0,cameraZ-(z+1)*size));
                // Add one tile of travel reserve. The 16x16 mask covers the complete set.
                if(dx*dx+dz*dz>(radius+size)*(radius+size)||x<cx-8||x>=cx+8||z<cz-8||z>=cz+8)continue;
                var key=new TileKey(x,z,0);contactDesired.Add(key);
                // A deferred replacement must survive overlap/radius replans.
                // Keeping a fallback mesh does not make its source current.
                if(!contactSurfaces.TryGetValue(key,out Surface existing)||existing.SourceRevision!=revision)
                {
                    // Height queries and bounds remain on the geometry worker.
                    var tile=new LodTile(key,x*size,z*size,size,RemoteTerrainField.MinimumSupportedHeight,RemoteTerrainField.MaximumSupportedHeight);
                    contactPending.Add(new Work(tile,revision,true,true));
                }
            }
            if(currentBuild!=null)
            {
                Work w=currentBuild.Work;
                if(w.Contact&&w.Revision==revision&&contactDesired.Contains(w.Tile.Key))contactPending.RemoveAll(p=>p.Tile.Key.Equals(w.Tile.Key));
                // Crossing a contact cell must not repeatedly discard useful
                // distant work. Ground/descent urgency still preempts it.
                else if(w.Contact||w.Revision!=revision||!desired.Contains(w.Tile.Key)||HasUrgentContactWork())
                {CancelBuild();if(!w.Contact&&w.Revision==revision&&desired.Contains(w.Tile.Key))pending.Enqueue(w);}
            }
            var retired=new List<TileKey>();foreach(var key in contactSurfaces.Keys)if(!contactDesired.Contains(key))retired.Add(key);
            foreach(var key in retired)ReleaseContact(key);
            UpdateContactMask();
            RetireAltitudeContacts();
        }
        Work NextContactWork()
        {
            int best=-1;double distance=double.MaxValue;
            for(int i=0;i<contactPending.Count;i++){if(contactAltitudeDeferring||voxelTravelDeferring&&!ContactWorkIsUrgent(contactPending[i].Tile))continue;double d=DistanceSquared(contactPending[i].Tile);if(d<distance){distance=d;best=i;}}
            if(best<0)throw new InvalidOperationException("No runnable contact work.");
            Work result=contactPending[best];contactPending.RemoveAt(best);return result;
        }
        static long SurfaceBytes(Surface s)=>s.MeshBytes>0?s.MeshBytes:(long)s.TriangleCount*60;
        void ReleaseContact(TileKey key)
        {
            if(!contactSurfaces.TryGetValue(key,out Surface s))return;
            ContactMeshBytes-=SurfaceBytes(s);ContactTriangles-=s.TriangleCount;
            Destroy(s.Object);Destroy(s.Mesh);contactSurfaces.Remove(key);
        }
        bool AdmitContactMesh(TileKey key,long bytes)
        {
            long replaced=contactSurfaces.TryGetValue(key,out Surface old)?SurfaceBytes(old):0;
            if(ContactMeshBytes-replaced+bytes<=ContactBudgetBytes)return true;
            contactDeferred[key]=bytes;return false;
        }
        void PublishContact(Surface built,Work work)
        {
            long bytes=SurfaceBytes(built),limit=ContactBudgetBytes,previousBytes=ContactMeshBytes;
            if(!AdmitContactMesh(work.Tile.Key,bytes))
            {
                // Never silently lower the10cm contact resolution to meet admission.
                // Retain the old terrain and observable request until the next move/replan.
                Destroy(built.Object);Destroy(built.Mesh);contactDeferred[work.Tile.Key]=bytes;return;
            }
            built.SourceRevision=work.Revision;
            ReleaseContact(work.Tile.Key);contactSurfaces.Add(work.Tile.Key,built);
            ContactMeshBytes+=bytes;ContactTriangles+=built.TriangleCount;
            contactDeferred.Remove(work.Tile.Key);
            built.Object.SetActive(!inspection);UpdateContactMask();
            if(ContactMeshBytes<previousBytes)WakeAdmissibleContactRequests(limit);
        }
        void WakeAdmissibleContactRequests(long limit)
        {
            remove.Clear();
            foreach(var request in contactDeferred)
            {
                if(!contactDesired.Contains(request.Key))continue;
                long previous=contactSurfaces.TryGetValue(request.Key,out Surface old)?SurfaceBytes(old):0;
                if(ContactMeshBytes-previous+request.Value>limit)continue;
                double size=VoxelLodSurface.BaseTileMeters;TileKey key=request.Key;
                var tile=new LodTile(key,key.X*size,key.Z*size,size,RemoteTerrainField.MinimumSupportedHeight,RemoteTerrainField.MaximumSupportedHeight);
                contactPending.Add(new Work(tile,revision,true,true));remove.Add(key);
            }
            foreach(TileKey key in remove)contactDeferred.Remove(key);
        }
        void UpdateContactMask()
        {
            if(EffectiveMaterial==null||contactCenterX==long.MinValue)return;
            Array.Clear(contactMask,0,contactMask.Length);
            long x0=contactCenterX-8,z0=contactCenterZ-8;
            foreach(var key in contactSurfaces.Keys)
            {
                long x=key.X-x0,z=key.Z-z0;if(x<0||x>=16||z<0||z>=16)continue;
                int index=(int)(x+z*16),slot=index/4,channel=index%4;
                Vector4 v=contactMask[slot];if(channel==0)v.x=1;else if(channel==1)v.y=1;else if(channel==2)v.z=1;else v.w=1;contactMask[slot]=v;
            }
            EffectiveMaterial.SetVectorArray("_VoxelTerrainMask",contactMask);
            EffectiveMaterial.SetVector("_VoxelTerrainGrid",new Vector4((float)(x0*VoxelLodSurface.BaseTileMeters-originX),(float)(z0*VoxelLodSurface.BaseTileMeters-originZ),(float)VoxelLodSurface.BaseTileMeters,16));
        }
        void ClearContactTerrain()
        {
            foreach(var s in contactSurfaces.Values){Destroy(s.Object);Destroy(s.Mesh);}
            contactSurfaces.Clear();contactDesired.Clear();contactPending.Clear();contactDeferred.Clear();
            ContactMeshBytes=ContactTriangles=0;contactCenterX=contactCenterZ=long.MinValue;contactRevision=-1;contactObservedBudgetBytes=-1;
            contactAltitudeDeferring=contactAltitudeBoundsCached=contactAltitudeBoundsKnown=false;
            if(contactMaterial!=null)Destroy(contactMaterial);contactMaterial=null;
            if(EffectiveMaterial!=null)EffectiveMaterial.SetVector("_VoxelTerrainGrid",new Vector4(0,0,0,0));
        }
        //24 bytes instead of44 per vertex: exact axis normals and RGBA/AO
        // are byte-exact, surface properties are normalized8-bit. Geometry stays FP32.
        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential,Pack=1)]
        struct PackedVoxelVertex
        {public Vector3 Position;public sbyte NX,NY,NZ,NW;public Color32 Color,Surface;}
        static readonly VertexAttributeDescriptor[] VoxelVertexLayout={
            new VertexAttributeDescriptor(VertexAttribute.Position,VertexAttributeFormat.Float32,3),
            new VertexAttributeDescriptor(VertexAttribute.Normal,VertexAttributeFormat.SNorm8,4),
            new VertexAttributeDescriptor(VertexAttribute.Color,VertexAttributeFormat.UNorm8,4),
            new VertexAttributeDescriptor(VertexAttribute.TexCoord0,VertexAttributeFormat.UNorm8,4)};
        PackedVoxelVertex[] voxelVertices;
        int[] voxelIndices;
        void EnsureVoxelBuffers(int vertices,int indices)
        {
            if(voxelVertices==null||voxelVertices.Length<vertices)voxelVertices=new PackedVoxelVertex[Capacity(vertices)];
            if(voxelIndices==null||voxelIndices.Length<indices)voxelIndices=new int[Capacity(indices)];
        }
        static int Capacity(int n){int size=1024;while(size<n)size*=2;return size;}
        public long VoxelScratchBytes=>(voxelVertices==null?0:voxelVertices.LongLength*24)+(voxelIndices==null?0:voxelIndices.LongLength*4);
        sealed class TerrainBuildResult
        {
            public readonly VoxelLodSurface Voxels;public readonly HeightProxySurface Proxy;
            public TerrainBuildResult(VoxelLodSurface data){Voxels=data;}
            public TerrainBuildResult(HeightProxySurface data){Proxy=data;}
        }
        Task<TerrainBuildResult> voxelBuildJob;
        CancellationTokenSource voxelBuildCancellation;
        bool voxelBuildWaiting;
        Vector3 plannedForward;
        public int VoxelLodTiles {get;private set;}
        public long TerrainTriangles {get;private set;}
        public int CameraPriorityTiles {get;private set;}
        public int PixelTargetLimitedTiles {get;private set;}
        public double LastVoxelWorkerMilliseconds {get;private set;}
        public int VoxelBuildFailures {get;private set;}
        public string LastVoxelBuildError {get;private set;}
        float voxelRetryAt;
        readonly Dictionary<TileKey,int> voxelCapacityQuads=new Dictionary<TileKey,int>();
        double workerMilliseconds;
        double FocalPixels=>Math.Max(720,view==null?Screen.height:view.pixelHeight)*.5/Math.Tan(Math.Max(30,view==null?75:view.fieldOfView)*Math.PI/360);
        bool InVoxelView(LodTile tile)
        {
            Quaternion rotation=view==null?Quaternion.Euler(0,0,0):view.transform.rotation;
            Vector3 forward=rotation*Vector3.forward,right=rotation*Vector3.right,up=rotation*Vector3.up;
            double length=Math.Sqrt((double)forward.x*forward.x+(double)forward.z*forward.z);
            if(length<.2)return true;
            double vertical=Math.Tan((view==null?75:view.fieldOfView)*Math.PI/360);
            double horizontal=vertical*Math.Max(1,view==null?Screen.width/(double)Math.Max(1,Screen.height):view.aspect);
            // Project the complete frustum ray rectangle onto horizontal heading
            // axes. Pitch/roll can widen it far beyond the level-camera FOV.
            double along=length-(Math.Abs(right.x*forward.x+right.z*forward.z)*horizontal+
                Math.Abs(up.x*forward.x+up.z*forward.z)*vertical)/length;
            double across=(Math.Abs(right.x*forward.z-right.z*forward.x)*horizontal+
                Math.Abs(up.x*forward.z-up.z*forward.x)*vertical)/length;
            // If the rectangle crosses the eye's heading plane, retaining every
            // heading is conservative. Otherwise bound its largest azimuth.
            double angle=along<=0?Math.PI:Math.Atan2(across,along);
            return VoxelLodQuality.InView(tile,GlobalCamera,forward.x,forward.z,Math.Min(Math.PI,angle+20*Math.PI/180));
        }
        // An8-column tile has at most64 tops,112 internal exposed sides and32
        // boundary sides. Each side has at most38 layer/pigment segments, plus
        // one boundary apron. Reserve this proven worst case even when merging
        // makes the actual coarse mesh much smaller. This guarantees room for
        // both bounded coverage plans without retaining a second mesh per tile.
        const long VoxelCoverageTileReserveBytes=(64+112*38+32*39)*120L;
        public static long CoverageTileMeshReserveBytes=>VoxelCoverageTileReserveBytes;
        long voxelInitialDetailBudgetBytes=1536L*1024*1024;
        long VoxelDetailBudgetBytes=>voxelInitialDetailBudgetBytes;
        public long TerrainDetailLimitBytes=>VoxelDetailBudgetBytes;
        public long TerrainDetailMeshBytes=>voxelDetailMeshBytes;
        /// <summary>Configure cache quotas before terrain initialization. Settings
        /// changes afterward require a new Play session; no live far-quota eviction.</summary>
        public void ConfigureInitialVoxelMeshBudgets(int detailMiB,int contactMiB)
        {
            if(field!=null||currentBuild!=null||surfaces.Count!=0||contactSurfaces.Count!=0)
                throw new InvalidOperationException("Terrain mesh budgets must be configured before initialization; restart Play to change them.");
            voxelInitialDetailBudgetBytes=(long)Mathf.Clamp(detailMiB,64,8192)*1024*1024;
            VoxelContactBudgetMB=Mathf.Clamp(contactMiB,128,2048);
        }
        long voxelDetailMeshBytes;
        bool voxelRefinementScanPending;
        int consecutiveContactBuilds;
        readonly struct DeferredVoxelRefinement
        {
            public readonly int Quads;
            public readonly long RequiredDetailBytes;
            public DeferredVoxelRefinement(int quads,long bytes){Quads=quads;RequiredDetailBytes=bytes;}
        }
        readonly Dictionary<TileKey,DeferredVoxelRefinement> voxelDeferredQuads=new Dictionary<TileKey,DeferredVoxelRefinement>();
        public int VoxelFaceCapacityLimitedTiles=>voxelCapacityQuads.Count;
        public long TerrainResidentMeshBytes {get;private set;}
        public long HeightProxyMeshBytes {get;private set;}
        public long ExposedVoxelMeshBytes=>TerrainResidentMeshBytes-HeightProxyMeshBytes;
        public int DistantHeightProxyTiles {get;private set;}
        public long LastTerrainUploadBytes {get;private set;}
        public int LastTerrainUploadVertices {get;private set;}
        public int LastTerrainUploadIndices {get;private set;}
        public string LastTerrainUploadRepresentation {get;private set;}
        void RecordTerrainUpload(string kind,int vertices,int indices,long bytes)
        {LastTerrainUploadRepresentation=kind;LastTerrainUploadVertices=vertices;LastTerrainUploadIndices=indices;LastTerrainUploadBytes=bytes;}
        // Retain the older active plan's reserve when a smaller replacement is
        // requested. Both cached plans remain covered until their atomic swap.
        public int TerrainCoverageReserveTiles=>Math.Max(Mathf.Clamp(MaximumTiles,64,LodSettings.MaximumSupportedTiles),Math.Max(active.Count,desired.Count));
        public long TerrainCoveragePlanReserveBytes=>(long)TerrainCoverageReserveTiles*VoxelCoverageTileReserveBytes;
        public long TerrainSteadyMeshLimitBytes=>VoxelDetailBudgetBytes+TerrainCoveragePlanReserveBytes;
        public long TerrainTransitionMeshLimitBytes=>VoxelDetailBudgetBytes+2*TerrainCoveragePlanReserveBytes;
        public int TerrainPlannedTiles=>desired.Count;
        public int TerrainPlanPendingSplits=>limitedSplits;
        public string TerrainQualityLimitReason=>!UseVoxelLodTerrain?"Voxel terrain disabled":
            VoxelMemoryPressureBlocked?"Commit headroom waiting":
            VoxelBuildFailures>0&&voxelRetryAt>Time.unscaledTime?"Build retry":
            coarseStartup||planActivationPending?"Initial coverage":
            voxelTravelDeferring?"Fast-flight refinement held":
            VoxelRefinementDeferredTiles>0?"Far mesh budget":
            VoxelFaceCapacityLimitedTiles>0?"Per-tile face limit":
            VoxelRefinementPendingTiles>0?"Refining visible terrain":
            limitedSplits>0?"Plan tile limit":PixelTargetLimitedTiles>0?"Tile sampling limit":"Current request reached";
        public int VoxelRefinementPendingTiles {get;private set;}
        public int VoxelRefinementDeferredTiles=>voxelDeferredQuads.Count;
        public int VoxelRefinementBuilds {get;private set;}
        int UnqueuedVoxelRefinementTiles
        {
            get
            {
                // Quality demand is per tile; do not count its queued/running
                // request again in the public total.
                int count=VoxelRefinementPendingTiles;
                if(count==0)return 0;
                bool NeedsQuality(Work work)=>!work.Contact&&surfaces.TryGetValue(work.Tile.Key,out Surface s)&&NeedsTerrainQuality(work.Tile,s);
                foreach(Work work in pending)if(NeedsQuality(work))count--;
                if(currentBuild!=null&&NeedsQuality(currentBuild.Work))count--;
                return Math.Max(0,count);
            }
        }
        static long DetailBytes(long bytes)=>Math.Max(0,bytes-VoxelCoverageTileReserveBytes);
        int VoxelQuadsFor(LodTile tile)
        {
            double distance=Math.Sqrt(DistanceSquared(tile));bool visible=InVoxelView(tile);
            double target=Mathf.Clamp(VoxelTargetPixels,.5f,8);
            int wanted=VoxelLodQuality.Select(tile.SizeMeters,distance,FocalPixels,target,32,visible,coarseStartup);
            // After coverage, visible tiles may earn the next rung beyond the
            // old128-column ceiling. Source resolution and10cm matter stay fixed.
            if(!coarseStartup&&visible)
            {
                if(VoxelGreedyVisibleDetail)wanted=VoxelLodSurface.MaximumQuads;
                while(wanted<256&&tile.SizeMeters/wanted*FocalPixels/Math.Max(.1,distance)>target)wanted*=2;
            }
            return !WantsHeightProxy(tile)&&voxelCapacityQuads.TryGetValue(tile.Key,out int cap)?Math.Min(wanted,cap):wanted;
        }
        void ResetVoxelRefinementAdmission()
        {voxelDeferredQuads.Clear();voxelRefinementScanPending=true;}
        void QueueVoxelCoverage(LodTile tile,Surface existing)
        {
            if(existing==null){pending.Enqueue(new Work(tile,revision,quads:8));return;}
            if(externallyDirty.Contains(tile.Key))
                pending.Enqueue(new Work(tile,revision,true,quads:existing.Quads));
            else if(existing.HeightProxy!=WantsHeightProxy(tile))
                pending.Enqueue(new Work(tile,revision,true,quads:Math.Min(existing.Quads,VoxelQuadsFor(tile))));
            else if(!coarseStartup&&existing.Quads>VoxelQuadsFor(tile))
                // Release surplus cached detail after travel/turns while retaining
                // the current pixel target, including for still-visible terrain.
                pending.Enqueue(new Work(tile,revision,true,quads:VoxelQuadsFor(tile)));
        }
        void QueueNextVoxelRefinement()
        {
            if(!UseVoxelLodTerrain||coarseStartup||planActivationPending||!voxelRefinementScanPending||
                currentBuild!=null||HasRunnableVoxelWork())return;
            if(HasRunnableContactWork()&&(voxelTravelDeferring?HasUrgentContactWork():consecutiveContactBuilds<2||HasUrgentContactWork()))return;
            Surface best=null;double priority=double.MaxValue;int next=0;
            foreach(var tile in desiredTiles.Values)
            {
                if(!active.Contains(tile.Key)||!surfaces.TryGetValue(tile.Key,out Surface s))continue;
                // While the outer contact ring fills, spend fair distant slots on
                // visible coverage rather than terrain already masked by10cm contact.
                if(contactPending.Count>0&&tile.Key.Level==0&&contactSurfaces.ContainsKey(tile.Key))continue;
                int target=VoxelQuadsFor(tile);if(voxelTravelDeferring)target=Math.Min(target,32);
                bool switchRepresentation=s.HeightProxy!=WantsHeightProxy(tile);
                // Reclaim cached fine meshes even when the plan key is unchanged.
                // A twofold quality gap avoids turn/distance boundary churn; fast
                // travel immediately earns the existing32-column coarse ceiling.
                bool downshift=s.Quads>target&&(voxelTravelDeferring||s.Quads>=target*2);
                if(s.Quads>=target&&!switchRepresentation&&!downshift||!downshift&&voxelDeferredQuads.ContainsKey(tile.Key))continue;
                // Favor near/in-view terrain; at similar distance, improve the
                // roughest tiles first. Scan the bounded plan, reuse its one queue.
                double score=(WorkPriority(tile)+tile.SizeMeters*tile.SizeMeters*.01)*s.Quads*s.Quads;
                // Greedy detail still publishes progressive rungs. Highest
                // projected error wins before spending on tiny far improvements.
                if(VoxelGreedyVisibleDetail)score/=tile.SizeMeters*tile.SizeMeters;
                if(downshift)score=-Math.Max(1,s.MeshBytes);
                if(score<priority){priority=score;best=s;next=downshift?target:switchRepresentation?Math.Min(target,s.Quads):Math.Min(target,Math.Max(32,s.Quads*2));}
            }
            if(best==null){voxelRefinementScanPending=false;RecordVoxelStatistics();return;}
            pending.Enqueue(new Work(best.Tile,revision,true,quads:next,progressive:true));
            VoxelRefinementBuilds++;
        }
        WorldPoint voxelMotionPosition;
        double voxelMotionTime=-1,voxelMotionX,voxelMotionY,voxelMotionZ,voxelFastSince=-1,voxelSlowSince=-1,voxelRecoveryUntil;
        bool voxelContactMotionKnown;
        bool voxelTravelDeferring;
        public bool VoxelTravelDetailDeferred=>voxelTravelDeferring;
        void SetVoxelTravelDeferral(bool value)
        {
            if(voxelTravelDeferring==value)return;
            voxelSlowSince=-1;
            voxelTravelDeferring=value;voxelRefinementScanPending=true;
            if(!value){voxelFastSince=-1;ForceReplan();}
        }
        /// <summary>Explicit teleport/respawn notification; normal pose updates
        /// retain global velocity history across render-origin rebases.</summary>
        public void ResetObserverMotion()
        {
            SetVoxelTravelDeferral(false);ResetVoxelTravelMotion();
            voxelRefinementScanPending=true;ForceReplan();
        }
        void ResetVoxelTravelMotion()
        {
            voxelMotionTime=voxelFastSince=voxelSlowSince=-1;voxelMotionX=voxelMotionY=voxelMotionZ=voxelRecoveryUntil=0;voxelContactMotionKnown=false;
            voxelTravelDeferring=false;
        }
        void UpdateVoxelTravelMotion()
        {
            if(!UseVoxelLodTerrain||!ExternalNavigation){ResetVoxelTravelMotion();return;}
            double now=Time.unscaledTime,dt=now-voxelMotionTime;WorldPoint eye=GlobalCamera;
            if(voxelMotionTime<0||dt>1)
            {SetVoxelTravelDeferral(false);voxelMotionTime=now;voxelMotionPosition=eye;voxelFastSince=-1;voxelContactMotionKnown=false;return;}
            if(dt<=0)return;
            double vx=(eye.X-voxelMotionPosition.X)/dt,vy=(eye.Y-voxelMotionPosition.Y)/dt,vz=(eye.Z-voxelMotionPosition.Z)/dt;
            double speed2=vx*vx+vz*vz,previous2=voxelMotionX*voxelMotionX+voxelMotionZ*voxelMotionZ;
            bool reverse=speed2>96*96&&previous2>96*96&&vx*voxelMotionX+vz*voxelMotionZ<0;
            voxelMotionTime=now;voxelMotionPosition=eye;voxelMotionX=vx;voxelMotionY=vy;voxelMotionZ=vz;voxelContactMotionKnown=true;
            var key=new TileKey((long)Math.Floor(eye.X/25.6),(long)Math.Floor(eye.Z/25.6),0);
            // Unknown/stale bounds and descent preserve arrival work. Global
            // coordinates prevent floating-origin shifts from masquerading as speed.
            bool safe=!coarseStartup&&vy>=-12&&TryContactEnclosure(key,out LodTile bounds)&&eye.Y>bounds.MaximumHeight+32.1;
            if(!safe||reverse||double.IsNaN(speed2)||double.IsInfinity(speed2))
            {
                voxelFastSince=voxelSlowSince=-1;SetVoxelTravelDeferral(false);
                if(reverse)voxelRecoveryUntil=now+.5;
                return;
            }
            if(voxelTravelDeferring)
            {
                if(speed2<1)SetVoxelTravelDeferral(false);
                else if(speed2<96*96)
                {if(voxelSlowSince<0)voxelSlowSince=now;if(now-voxelSlowSince>=.25)SetVoxelTravelDeferral(false);}
                else voxelSlowSince=-1;
            }
            else if(speed2>=128*128&&now>=voxelRecoveryUntil)
            {if(voxelFastSince<0)voxelFastSince=now;if(now-voxelFastSince>=.25)SetVoxelTravelDeferral(true);}
            else voxelFastSince=-1;
        }
        bool HasRunnableVoxelWork()
        {
            foreach(Work work in pending)if(!HoldVoxelDetailWork(work))return true;
            return false;
        }
        bool HoldVoxelDetailWork(Work work)=>voxelTravelDeferring&&work.Progressive&&work.Quads>32&&
            !externallyDirty.Contains(work.Tile.Key)&&surfaces.TryGetValue(work.Tile.Key,out Surface s)&&work.Quads>s.Quads;
        int voxelPlanBoundsRevision=-1;
        bool HasUrgentContactWork()
        {
            foreach(Work work in contactPending)if(ContactWorkIsUrgent(work.Tile))return true;
            return false;
        }
        bool ContactWorkIsUrgent(LodTile tile)
        {
            if(contactAltitudeDeferring)return false;
            double distance=DistanceSquared(tile);
            if(distance>32*32)return false;
            // Reuse immutable planner enclosures, never query the field here.
            // Unknown/stale bounds retain the original arrival-safe priority.
            if(!TryContactEnclosure(tile.Key,out LodTile enclosure))return true;
            double dy=Math.Max(enclosure.MinimumHeight-.1-cameraY,Math.Max(0,cameraY-enclosure.MaximumHeight-.1));
            return distance+dy*dy<=32*32;
        }
        bool TryContactEnclosure(TileKey contactKey,out LodTile enclosure)
        {
            enclosure=default;
            if(voxelPlanBoundsRevision!=revision||!(BareField(field) is IWorldFieldBounds))return false;
            for(int level=0;level<=14;level++)
            {
                long divisor=1L<<level;
                var key=new TileKey((long)Math.Floor((double)contactKey.X/divisor),(long)Math.Floor((double)contactKey.Z/divisor),level);
                if(desiredTiles.TryGetValue(key,out enclosure))return true;
            }
            return false;
        }
        void WakeAdmissibleVoxelRefinements(long previousDetailBytes)
        {
            // Run after complete replacement/retirement, never during the temporary
            // Release-before-add gap. Only measured requests which now fit wake.
            if(voxelDetailMeshBytes>=previousDetailBytes||voxelDeferredQuads.Count==0)return;
            remove.Clear();
            foreach(var request in voxelDeferredQuads)
            {
                if(!desired.Contains(request.Key))continue;
                long previous=surfaces.TryGetValue(request.Key,out Surface old)?DetailBytes(SurfaceBytes(old)):0;
                if(voxelDetailMeshBytes-previous+request.Value.RequiredDetailBytes>VoxelDetailBudgetBytes)continue;
                // Requeue the measured request itself: source refreshes can need
                // replacement even when the old mesh already has the target quads.
                pending.Enqueue(new Work(desiredTiles[request.Key],revision,true,quads:request.Value.Quads,progressive:true));
                VoxelRefinementBuilds++;remove.Add(request.Key);
            }
            foreach(TileKey key in remove)voxelDeferredQuads.Remove(key);
            if(remove.Count>0)voxelRefinementScanPending=true;
        }
        bool AdmitVoxelMesh(LodTile tile,int n,long bytes)
        {
            if(n==8&&bytes>VoxelCoverageTileReserveBytes)
                throw new InvalidOperationException("Voxel coverage exceeded its proven face reserve.");
            long previous=surfaces.TryGetValue(tile.Key,out Surface old)?DetailBytes(SurfaceBytes(old)):0;
            if(voxelDetailMeshBytes-previous+DetailBytes(bytes)<=VoxelDetailBudgetBytes)
            {
                if(voxelDeferredQuads.TryGetValue(tile.Key,out DeferredVoxelRefinement request)&&n>=request.Quads)voxelDeferredQuads.Remove(tile.Key);
                return true;
            }
            // Reject before vertex expansion/GPU upload, remember the request and
            // back off until a plan/source change can free/reprioritize capacity.
            voxelDeferredQuads[tile.Key]=new DeferredVoxelRefinement(n,DetailBytes(bytes));voxelRefinementScanPending=true;
            if(externallyDirty.Contains(tile.Key))pending.Enqueue(new Work(tile,revision,true,quads:8));
            return false;
        }
        void DeferFailedVoxelBuild(Work work,Exception error)
        {
            int n=work.Quads>0?work.Quads:VoxelQuadsFor(work.Tile);
            if(voxelCapacityQuads.TryGetValue(work.Tile.Key,out int cap))n=Math.Min(n,cap);
            if(!work.Contact&&error is VoxelLodCapacityException&&n>8)voxelCapacityQuads[work.Tile.Key]=n/2;
            VoxelBuildFailures++;LastVoxelBuildError=error.Message;voxelRetryAt=Time.unscaledTime+1;
            if(work.Contact)contactPending.Add(work);else pending.Enqueue(work);Status="Voxel tile queued for retry: "+error.Message;
        }
        double WorkPriority(LodTile tile)=>DistanceSquared(tile)*(UseVoxelLodTerrain&&!InVoxelView(tile)?16:1);
        bool ViewTurned()
        {
            if(!UseVoxelLodTerrain||view==null)return false;
            Vector3 f=view.transform.forward;
            return f.x*plannedForward.x+f.y*plannedForward.y+f.z*plannedForward.z<.90;
        }
        public int VisibleTerrainSpacingTiles {get;private set;}
        public int VisibleTerrainAtRequestedSpacingTiles {get;private set;}
        public double VisibleTerrainMeanCurrentSpacingMeters {get;private set;}
        public double VisibleTerrainMeanRequestedSpacingMeters {get;private set;}
        void RecordVoxelStatistics()
        {
            VoxelLodTiles=DistantHeightProxyTiles=CameraPriorityTiles=PixelTargetLimitedTiles=VoxelRefinementPendingTiles=0;TerrainTriangles=0;
            VisibleTerrainSpacingTiles=VisibleTerrainAtRequestedSpacingTiles=0;
            VisibleTerrainMeanCurrentSpacingMeters=VisibleTerrainMeanRequestedSpacingMeters=0;
            if(UseVoxelLodTerrain&&!coarseStartup)
                foreach(var tile in desiredTiles.Values)
                    if(surfaces.TryGetValue(tile.Key,out Surface existing)&&NeedsTerrainQuality(tile,existing))VoxelRefinementPendingTiles++;
            foreach(TileKey key in active)if(surfaces.TryGetValue(key,out Surface s))
            {
                TerrainTriangles+=s.TriangleCount;if(!s.VoxelColumns&&!s.HeightProxy)continue;if(s.HeightProxy)DistantHeightProxyTiles++;else VoxelLodTiles++;
                if(InVoxelView(s.Tile))
                {
                    CameraPriorityTiles++;
                    if(desiredTiles.TryGetValue(key,out LodTile request))
                    {
                        int target=VoxelQuadsFor(request);VisibleTerrainSpacingTiles++;
                        VisibleTerrainMeanCurrentSpacingMeters+=s.Tile.SizeMeters/s.Quads;
                        VisibleTerrainMeanRequestedSpacingMeters+=request.SizeMeters/target;
                        if(s.Quads>=target)VisibleTerrainAtRequestedSpacingTiles++;
                    }
                    double distance=Math.Max(.1,Math.Sqrt(DistanceSquared(s.Tile)));
                    if(s.Tile.SizeMeters/s.Quads*FocalPixels/distance>VoxelTargetPixels&&s.Tile.SizeMeters/s.Quads>.10001)PixelTargetLimitedTiles++;
                }
            }
            if(VisibleTerrainSpacingTiles>0)
            {
                VisibleTerrainMeanCurrentSpacingMeters/=VisibleTerrainSpacingTiles;
                VisibleTerrainMeanRequestedSpacingMeters/=VisibleTerrainSpacingTiles;
            }
        }
        IEnumerator<Surface> BuildVoxelSurfaceSteps(LodTile tile,int n,bool contact=false)
        {
            // A canceled iterator never abandons a running worker and starts a
            // second one. Wait for cooperative cancellation before reusing slot.
            while(voxelBuildJob!=null&&!voxelBuildJob.IsCompleted){voxelBuildWaiting=true;yield return null;}
            if(voxelBuildJob!=null){if(voxelBuildJob.IsFaulted){var observed=voxelBuildJob.Exception;}voxelBuildJob=null;}
            voxelBuildCancellation?.Dispose();voxelBuildCancellation=new CancellationTokenSource();
            CancellationToken cancel=voxelBuildCancellation.Token;
            bool neuralReady=remote!=null&&TileReady(tile);
            IWorldField samples=remote==null?CaptureField(BareField(field)):!neuralReady?remote.RoughField:remote.ReadySnapshot(
                tile.OriginXMeters-SurfaceHalo,tile.OriginZMeters-SurfaceHalo,tile.OriginXMeters+tile.SizeMeters+SurfaceHalo,tile.OriginZMeters+tile.SizeMeters+SurfaceHalo);
            var sampler=new SurfaceMaterialSampler(samples,CurrentSurfaceProfile);
            bool proxy=!contact&&WantsHeightProxy(tile);
            HeightProxyDiskCache cache=heightProxyDiskCache;
            voxelBuildJob=Task.Run(()=>
            {
                var clock=System.Diagnostics.Stopwatch.StartNew();
                if(proxy)
                {
                    var surface=new SurfaceWorldField(samples,sampler.Profile,new HeightBounds(RemoteTerrainField.MinimumSupportedHeight,RemoteTerrainField.MaximumSupportedHeight));
                    double skirt=SkirtDepth(surface,tile,tile.SizeMeters/n);
                    var proxyData=BuildCachedHeightProxy(cache,sampler,tile,n,skirt,cancel);
                    workerMilliseconds=clock.Elapsed.TotalMilliseconds;return new TerrainBuildResult(proxyData);
                }
                // Query the complete neighboring footprint on the immutable
                // worker snapshot. An apron encloses different LOD boundaries.
                double floor=samples is IWorldFieldBounds bounded?bounded.QueryBounds(tile.OriginXMeters-tile.SizeMeters*2,tile.OriginZMeters-tile.SizeMeters*2,
                    tile.OriginXMeters+tile.SizeMeters*3,tile.OriginZMeters+tile.SizeMeters*3).Minimum-1:RemoteTerrainField.MinimumSupportedHeight-1;
                VoxelLodSurface data=VoxelLodSurface.Build(sampler,tile,n,floor,cancel,parallelSampling:true);workerMilliseconds=clock.Elapsed.TotalMilliseconds;return new TerrainBuildResult(data);
            },cancel);
            try
            {
                while(!voxelBuildJob.IsCompleted){voxelBuildWaiting=true;yield return null;}
                voxelBuildWaiting=false;
                if(voxelBuildJob.IsFaulted)throw voxelBuildJob.Exception.GetBaseException();
                if(voxelBuildJob.IsCanceled)yield break;
                TerrainBuildResult completed=voxelBuildJob.Result;voxelBuildJob=null;LastVoxelWorkerMilliseconds=workerMilliseconds;
                if(!contact&&proxy!=WantsHeightProxy(tile))
                {if(desired.Contains(tile.Key))pending.Enqueue(new Work(tile,revision,true,quads:n));voxelRefinementScanPending=true;yield break;}
                if(proxy)
                {
                    using(var upload=UploadHeightProxySteps(tile,completed.Proxy,neuralReady,cancel))
                        while(upload.MoveNext())yield return upload.Current;
                    yield break;
                }
                VoxelLodSurface data=completed.Voxels;
                long payloadBytes=(long)data.Faces.Count*120;
                if(contact?!AdmitContactMesh(tile.Key,payloadBytes):!AdmitVoxelMesh(tile,n,payloadBytes))yield break;
                while(!HasVoxelUploadHeadroom(payloadBytes)){voxelBuildWaiting=true;cancel.ThrowIfCancellationRequested();yield return null;}
                voxelBuildWaiting=false;
                int vertexCount=data.Faces.Count*4,indexCount=data.Faces.Count*6;
                EnsureVoxelBuffers(vertexCount,indexCount);
                int processed=0,vertex=0,index=0;Vector3 lo=new Vector3(float.MaxValue,float.MaxValue,float.MaxValue),hi=new Vector3(float.MinValue,float.MinValue,float.MinValue);
                foreach(VoxelLodFace face in data.Faces)
                {
                    int start=vertex;Vector3 p=new Vector3(face.X,face.Y,face.Z);
                    Vector3 u=VoxelAxis((face.Axis+1)%3)*face.U,v=VoxelAxis((face.Axis+2)%3)*face.V;
                    sbyte sign=(sbyte)(face.Positive?127:-127);
                    byte r=(byte)(face.Tint>>16),g=(byte)(face.Tint>>8),b=(byte)face.Tint;
                    byte rough=(byte)Math.Round(SurfaceRoughness(face.Material,face.Wetness/255.0)*255);
                    var surface=new Color32(rough,face.Wetness,face.Material==Core.MaterialId.Snow?(byte)255:(byte)0,(byte)(128+(int)face.Material));
                    void Vertex(Vector3 position,byte ao)
                    {
                        voxelVertices[vertex++]=new PackedVoxelVertex{Position=position,NX=face.Axis==0?sign:(sbyte)0,NY=face.Axis==1?sign:(sbyte)0,NZ=face.Axis==2?sign:(sbyte)0,
                            Color=new Color32(r,g,b,ao),Surface=surface};
                        lo=new Vector3(Math.Min(lo.x,position.x),Math.Min(lo.y,position.y),Math.Min(lo.z,position.z));
                        hi=new Vector3(Math.Max(hi.x,position.x),Math.Max(hi.y,position.y),Math.Max(hi.z,position.z));
                    }
                    Vertex(p,face.A0);Vertex(p+u,face.A1);Vertex(p+u+v,face.A2);Vertex(p+v,face.A3);
                    void Tri(int a,int bb,int c){voxelIndices[index++]=start+a;voxelIndices[index++]=start+(face.Positive?bb:c);voxelIndices[index++]=start+(face.Positive?c:bb);}
                    if(face.A0+face.A2>face.A1+face.A3){Tri(0,1,3);Tri(1,2,3);}else{Tri(0,1,2);Tri(0,2,3);}
                    if(++processed%128==0)yield return null;
                }
                // The legacy debug contact allowance may change during expansion.
                if(contact&&!AdmitContactMesh(tile.Key,payloadBytes))yield break;
                var mesh=new Mesh{name="Voxel LOD "+tile.Key,indexFormat=IndexFormat.UInt32};
                mesh.SetVertexBufferParams(vertexCount,VoxelVertexLayout);
                mesh.SetVertexBufferData(voxelVertices,0,0,vertexCount);
                mesh.SetIndexBufferParams(indexCount,IndexFormat.UInt32);mesh.SetIndexBufferData(voxelIndices,0,0,indexCount);
                var bounds=new Bounds((lo+hi)*.5f,hi-lo);
                mesh.SetSubMesh(0,new SubMeshDescriptor(0,indexCount){bounds=bounds,vertexCount=vertexCount},MeshUpdateFlags.DontRecalculateBounds);
                mesh.bounds=bounds;mesh.UploadMeshData(true);
                RecordTerrainUpload(contact?"Contact cubes":"Exposed voxels",vertexCount,indexCount,payloadBytes);
                var go=new GameObject(mesh.name);go.transform.SetParent(transform,false);go.AddComponent<MeshFilter>().sharedMesh=mesh;
                var renderer=go.AddComponent<MeshRenderer>();
                if(contact&&contactMaterial==null)
                {contactMaterial=new Material(EffectiveMaterial);contactMaterial.SetVector("_VoxelTerrainGrid",new Vector4(0,0,0,0));}
                renderer.sharedMaterial=contact?contactMaterial:EffectiveMaterial;
                var result=new Surface{Object=go,Mesh=mesh,Renderer=renderer,Tile=tile,AnchorY=data.AnchorY,NeuralReady=neuralReady,Quads=n,
                    LocalHeights=data.Heights,VoxelColumns=true,TriangleCount=indexCount/3,MeshBytes=payloadBytes};
                PositionSurface(result);go.SetActive(false);yield return result;
            }
            finally{voxelBuildCancellation?.Cancel();voxelBuildWaiting=false;}
        }
        static Vector3 VoxelAxis(int axis)=>axis==0?new Vector3(1,0,0):axis==1?new Vector3(0,1,0):new Vector3(0,0,1);
        IEnumerator<Surface> UploadHeightProxySteps(LodTile tile,HeightProxySurface data,bool neuralReady,CancellationToken cancel)
        {
            long bytes=data.MeshPayloadBytes;
            if(!AdmitVoxelMesh(tile,data.Quads,bytes))yield break;
            while(!HasVoxelUploadHeadroom(bytes)){voxelBuildWaiting=true;cancel.ThrowIfCancellationRequested();yield return null;}
            voxelBuildWaiting=false;
            Mesh mesh=null;GameObject go=null;bool transferred=false;
            try
            {
                cancel.ThrowIfCancellationRequested();mesh=new Mesh{name="Distant height proxy "+tile.Key,indexFormat=IndexFormat.UInt32};
                mesh.SetVertexBufferParams(data.Vertices.Length,VoxelVertexLayout);mesh.SetVertexBufferData(data.Vertices,0,0,data.Vertices.Length);
                mesh.SetIndexBufferParams(data.Indices.Length,IndexFormat.UInt32);mesh.SetIndexBufferData(data.Indices,0,0,data.Indices.Length);
                var bounds=new Bounds(new Vector3((float)(tile.SizeMeters*.5),(data.MinimumLocalY+data.MaximumLocalY)*.5f,(float)(tile.SizeMeters*.5)),
                    new Vector3((float)tile.SizeMeters,data.MaximumLocalY-data.MinimumLocalY,(float)tile.SizeMeters));
                mesh.SetSubMesh(0,new SubMeshDescriptor(0,data.Indices.Length){bounds=bounds,vertexCount=data.Vertices.Length},MeshUpdateFlags.DontRecalculateBounds);
                mesh.bounds=bounds;mesh.UploadMeshData(true);RecordTerrainUpload("Distant height proxy",data.Vertices.Length,data.Indices.Length,bytes);
                go=new GameObject(mesh.name);go.transform.SetParent(transform,false);go.AddComponent<MeshFilter>().sharedMesh=mesh;
                var renderer=go.AddComponent<MeshRenderer>();renderer.sharedMaterial=EffectiveMaterial;
                var result=new Surface{Object=go,Mesh=mesh,Renderer=renderer,Tile=tile,AnchorY=data.AnchorY,NeuralReady=neuralReady,Quads=data.Quads,
                    LocalHeights=data.Heights,HeightProxy=true,MeshBytes=bytes,TriangleCount=data.Indices.Length/3};
                PositionSurface(result);go.SetActive(false);transferred=true;yield return result;
            }
            finally{if(!transferred){if(go!=null)Destroy(go);if(mesh!=null)Destroy(mesh);}}
        }
        void DisposeVoxelWorker()
        {
            voxelBuildCancellation?.Cancel();voxelBuildCancellation?.Dispose();voxelBuildCancellation=null;
            // Do not block Unity or close a file owner while its worker is in IO.
            // The terminal continuation reuses that worker; no new queue/thread.
            HeightProxyDiskCache cache=heightProxyDiskCache;heightProxyDiskCache=null;
            if(voxelBuildJob!=null&&!voxelBuildJob.IsCompleted)
            {
                SetHeightProxyCacheStatus(cache==null?"Off":"Closing after terrain worker");
                voxelBuildJob.ContinueWith(task=>{var observed=task.Exception;cache?.Dispose();},TaskContinuationOptions.ExecuteSynchronously);
            }
            else
            {
                if(voxelBuildJob!=null){var observed=voxelBuildJob.Exception;}
                cache?.Dispose();SetHeightProxyCacheStatus(cache==null?"Off":"Closed");
            }
            voxelBuildJob=null;voxelVertices=null;voxelIndices=null;voxelCapacityQuads.Clear();
        }
    }
}
