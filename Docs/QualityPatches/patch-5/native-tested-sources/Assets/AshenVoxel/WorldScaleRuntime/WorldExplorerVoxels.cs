using System;
using System.Collections.Generic;
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
        [Range(.5f,8)] public float VoxelTargetPixels=1.5f;
        [Range(16,160)] public float VoxelFullDetailRadius=100;
        [Range(128,2048)] public int VoxelContactBudgetMB=1024;
        public int ContactTiles=>contactSurfaces.Count;
        public int ContactPendingTiles=>contactPending.Count+contactDeferred.Count+(currentBuild!=null&&currentBuild.Work.Contact?1:0);
        public long ContactMeshBytes {get;private set;}
        public long ContactTriangles {get;private set;}
        readonly Dictionary<TileKey,Surface> contactSurfaces=new Dictionary<TileKey,Surface>();
        readonly HashSet<TileKey> contactDesired=new HashSet<TileKey>(),contactDeferred=new HashSet<TileKey>();
        readonly List<Work> contactPending=new List<Work>();
        readonly Vector4[] contactMask=new Vector4[64];
        long contactCenterX=long.MinValue,contactCenterZ=long.MinValue;
        int contactRevision=-1;float contactRadius=-1;
        Material contactMaterial;
        void RefreshContactPlan()
        {
            if(!UseVoxelLodTerrain||coarseStartup||field==null)return;
            double size=VoxelLodSurface.BaseTileMeters;
            long cx=(long)Math.Floor(cameraX/size),cz=(long)Math.Floor(cameraZ/size);
            float radius=Mathf.Clamp(VoxelFullDetailRadius,16,160);
            if(cx==contactCenterX&&cz==contactCenterZ&&revision==contactRevision&&radius==contactRadius)return;
            bool sourceChanged=contactRevision!=revision;
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
                if(sourceChanged||!contactSurfaces.ContainsKey(key))
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
                else {CancelBuild();if(!w.Contact&&w.Revision==revision&&desired.Contains(w.Tile.Key))pending.Enqueue(w);}
            }
            var retired=new List<TileKey>();foreach(var key in contactSurfaces.Keys)if(!contactDesired.Contains(key))retired.Add(key);
            foreach(var key in retired)ReleaseContact(key);
            UpdateContactMask();
        }
        Work NextContactWork()
        {
            int best=0;double distance=double.MaxValue;
            for(int i=0;i<contactPending.Count;i++){double d=DistanceSquared(contactPending[i].Tile);if(d<distance){distance=d;best=i;}}
            Work result=contactPending[best];contactPending.RemoveAt(best);return result;
        }
        static long SurfaceBytes(Surface s)=>(long)s.TriangleCount*60; // Four24-byte vertices + six4-byte indices per quad.
        void ReleaseContact(TileKey key)
        {
            if(!contactSurfaces.TryGetValue(key,out Surface s))return;
            ContactMeshBytes-=SurfaceBytes(s);ContactTriangles-=s.TriangleCount;
            Destroy(s.Object);Destroy(s.Mesh);contactSurfaces.Remove(key);
        }
        void PublishContact(Surface built,Work work)
        {
            long bytes=SurfaceBytes(built),limit=(long)Mathf.Clamp(VoxelContactBudgetMB,128,2048)*1024*1024;
            long replaced=contactSurfaces.TryGetValue(work.Tile.Key,out Surface old)?SurfaceBytes(old):0;
            if(ContactMeshBytes-replaced+bytes>limit)
            {
                // Never silently lower the10cm contact resolution to meet admission.
                // Retain the old terrain and observable request until the next move/replan.
                Destroy(built.Object);Destroy(built.Mesh);contactDeferred.Add(work.Tile.Key);return;
            }
            ReleaseContact(work.Tile.Key);contactSurfaces.Add(work.Tile.Key,built);
            ContactMeshBytes+=bytes;ContactTriangles+=built.TriangleCount;
            built.Object.SetActive(!inspection);UpdateContactMask();
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
            ContactMeshBytes=ContactTriangles=0;contactCenterX=contactCenterZ=long.MinValue;contactRevision=-1;
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
        Task<VoxelLodSurface> voxelBuildJob;
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
            Vector3 forward=view==null?Vector3.forward:view.transform.forward;
            double vertical=(view==null?75:view.fieldOfView)*Math.PI/360;
            double horizontal=Math.Atan(Math.Tan(vertical)*Math.Max(1,view==null?Screen.width/(double)Math.Max(1,Screen.height):view.aspect));
            return VoxelLodQuality.InView(tile,GlobalCamera,forward.x,forward.z,Math.Min(Math.PI, horizontal+20*Math.PI/180));
        }
        int VoxelQuadsFor(LodTile tile)
        {
            int wanted=VoxelLodQuality.Select(tile.SizeMeters,Math.Sqrt(DistanceSquared(tile)),FocalPixels,
                Mathf.Clamp(VoxelTargetPixels,.5f,8),32,InVoxelView(tile),coarseStartup);
            return voxelCapacityQuads.TryGetValue(tile.Key,out int cap)?Math.Min(wanted,cap):wanted;
        }
        void DeferFailedVoxelBuild(Work work,Exception error)
        {
            int n=VoxelQuadsFor(work.Tile);
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
        void RecordVoxelStatistics()
        {
            VoxelLodTiles=CameraPriorityTiles=PixelTargetLimitedTiles=0;TerrainTriangles=0;
            foreach(TileKey key in active)if(surfaces.TryGetValue(key,out Surface s))
            {
                TerrainTriangles+=s.TriangleCount;if(!s.VoxelColumns)continue;VoxelLodTiles++;
                if(InVoxelView(s.Tile))
                {
                    CameraPriorityTiles++;
                    double distance=Math.Max(.1,Math.Sqrt(DistanceSquared(s.Tile)));
                    if(s.Tile.SizeMeters/s.Quads*FocalPixels/distance>VoxelTargetPixels&&s.Tile.SizeMeters/s.Quads>.10001)PixelTargetLimitedTiles++;
                }
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
            voxelBuildJob=Task.Run(()=>
            {
                var clock=System.Diagnostics.Stopwatch.StartNew();
                // Query the complete neighboring footprint on the immutable
                // worker snapshot. An apron encloses different LOD boundaries.
                double floor=samples is IWorldFieldBounds bounded?bounded.QueryBounds(tile.OriginXMeters-tile.SizeMeters*2,tile.OriginZMeters-tile.SizeMeters*2,
                    tile.OriginXMeters+tile.SizeMeters*3,tile.OriginZMeters+tile.SizeMeters*3).Minimum-1:RemoteTerrainField.MinimumSupportedHeight-1;
                VoxelLodSurface data=VoxelLodSurface.Build(sampler,tile,n,floor,cancel);workerMilliseconds=clock.Elapsed.TotalMilliseconds;return data;
            },cancel);
            try
            {
                while(!voxelBuildJob.IsCompleted){voxelBuildWaiting=true;yield return null;}
                voxelBuildWaiting=false;
                if(voxelBuildJob.IsFaulted)throw voxelBuildJob.Exception.GetBaseException();
                if(voxelBuildJob.IsCanceled)yield break;
                VoxelLodSurface data=voxelBuildJob.Result;voxelBuildJob=null;LastVoxelWorkerMilliseconds=workerMilliseconds;
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
                var mesh=new Mesh{name="Voxel LOD "+tile.Key,indexFormat=IndexFormat.UInt32};
                mesh.SetVertexBufferParams(vertexCount,VoxelVertexLayout);
                mesh.SetVertexBufferData(voxelVertices,0,0,vertexCount);
                mesh.SetIndexBufferParams(indexCount,IndexFormat.UInt32);mesh.SetIndexBufferData(voxelIndices,0,0,indexCount);
                var bounds=new Bounds((lo+hi)*.5f,hi-lo);
                mesh.SetSubMesh(0,new SubMeshDescriptor(0,indexCount){bounds=bounds,vertexCount=vertexCount},MeshUpdateFlags.DontRecalculateBounds);
                mesh.bounds=bounds;mesh.UploadMeshData(true);
                var go=new GameObject(mesh.name);go.transform.SetParent(transform,false);go.AddComponent<MeshFilter>().sharedMesh=mesh;
                var renderer=go.AddComponent<MeshRenderer>();
                if(contact&&contactMaterial==null)
                {contactMaterial=new Material(EffectiveMaterial);contactMaterial.SetVector("_VoxelTerrainGrid",new Vector4(0,0,0,0));}
                renderer.sharedMaterial=contact?contactMaterial:EffectiveMaterial;
                var result=new Surface{Object=go,Mesh=mesh,Renderer=renderer,Tile=tile,AnchorY=data.AnchorY,NeuralReady=neuralReady,Quads=n,
                    LocalHeights=data.Heights,VoxelColumns=true,TriangleCount=indexCount/3};
                PositionSurface(result);go.SetActive(false);yield return result;
            }
            finally{voxelBuildCancellation?.Cancel();voxelBuildWaiting=false;}
        }
        static Vector3 VoxelAxis(int axis)=>axis==0?new Vector3(1,0,0):axis==1?new Vector3(0,1,0):new Vector3(0,0,1);
        void DisposeVoxelWorker()
        {
            voxelBuildCancellation?.Cancel();voxelBuildCancellation?.Dispose();voxelBuildCancellation=null;
            if(voxelBuildJob!=null)voxelBuildJob.ContinueWith(task=>{var observed=task.Exception;},TaskContinuationOptions.OnlyOnFaulted);
            voxelBuildJob=null;voxelVertices=null;voxelIndices=null;voxelCapacityQuads.Clear();
        }
    }
}
