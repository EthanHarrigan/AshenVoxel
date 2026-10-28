using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using AshenVoxel.Core;
using AshenVoxel.WorldScale;
using RootContactPrototype;
using RootContactInteraction;
using RootContactTemplateCache;
using RootContactBuildPipeline;
using RootContactManagedComposite;
using RootContactUploadPrototype;
using RootQueries=RootContactCollision.RootCollision;
using UnityEngine;

namespace AshenVoxel.Exploration
{
    public sealed partial class VegetationStreaming
    {
        // Additional root-cache budgets; terrain/vegetation coverage limits are unchanged.
        const int RootBindingLimit=4;
        const long RootWorkerBytes=64L*1024*1024,RootCpuBytes=16L*1024*1024,RootNativeBytes=32L*1024*1024;
        const double RootPrepareRange=64,RootRetainRange=96,RootPlayerClearance=8;
        public bool EnableGroundedRoots=false;
        public bool GroundedRootsInitialized=>!rootClosed;
        public int GroundedRootCount=>rootCommitted.Count;
        public long GroundedRootNativeReservation=>rootNativeBytes;
        public long GroundedRootWorkerReservation=>rootTask!=null||rootWaiting!=null||rootClosingTask!=null&&!rootClosingTask.IsCompleted?RootWorkerBytes:0;
        public string GroundedRootStatus{get;private set;}="Not initialized";
        readonly struct RootKey:IEquatable<RootKey>
        {
            public readonly PopulationKey Cell;public readonly ulong Stable;
            public RootKey(PopulationKey cell,ulong stable){Cell=cell;Stable=stable;}
            public bool Equals(RootKey other)=>Cell.Equals(other.Cell)&&Stable==other.Stable;
            public override bool Equals(object value)=>value is RootKey key&&Equals(key);
            public override int GetHashCode()=>Cell.GetHashCode()*397^Stable.GetHashCode();
        }
        sealed class RootShared{public TemplateBundle Bundle;public ManagedTemplateAssembly Templates;}
        sealed class RootRequest
        {
            public RootKey Key;public CachedCell Owner;public PopulationInstance Instance;public RootShared Shared;
            public ExplorationPrototype Canonical;public RootTerrainSnapshot Terrain;public string LiveSource,OwnerIdentity;public CancellationToken Token;
        }
        sealed class RootCurrent:ICurrentIdentities
        {
            readonly RootTerrainSnapshot terrain;readonly string context;
            public RootCurrent(RootTerrainSnapshot t,string c){terrain=t;context=c;}
            public string SourceIdentity=>terrain.SourceIdentity;public string ContextIdentity=>context;
        }
        sealed class RootCpu
        {
            public RootRequest Request;public CompleteArtifact Artifact;public RootQueries Collision;public PreparedMesh[] Prepared;
            public string Failure="";public long Bytes;public double ComputeMilliseconds;
        }
        sealed class RootBinding
        {
            public int Id;public RootCpu Cpu;public Mesh[] Meshes=new Mesh[5];public int[] Triangles=new int[5];public long[] Reservations=new long[5];
            public int Uploaded;public bool Retiring;public long NativePayload;
        }
        sealed class RootRetirement{public Mesh Mesh;public MeshRetirement Failed;public long Reservation,Payload;public bool Requested;}
        readonly Dictionary<int,RootShared> rootShared=new Dictionary<int,RootShared>();
        readonly Dictionary<RootKey,RootBinding> rootBindings=new Dictionary<RootKey,RootBinding>();
        Dictionary<RootKey,RootBinding> rootCommitted=new Dictionary<RootKey,RootBinding>(),rootNext;
        readonly Dictionary<RootKey,string> rootRejected=new Dictionary<RootKey,string>();
        readonly List<RootRetirement> rootRetirements=new List<RootRetirement>();
        readonly List<RootKey> rootRemove=new List<RootKey>();
        IWorldField rootField;Task<RootCpu> rootTask,rootClosingTask;RootRequest rootRequest;CancellationTokenSource rootCancellation;
        RootBinding rootUploading;RootCpu rootWaiting;UnityMeshUpload rootUploader;long rootNativeBytes,rootPayloadBytes;int rootNextId;bool rootClosed=true;
        long rootLastScan;

        static string RootPackIdentity=>RootQueries.PackIdentity;
        void InitializeRoots(IWorldField bare,byte[] pack)
        {
            rootClosed=false;rootField=bare;rootUploader=new UnityMeshUpload(Thread.CurrentThread.ManagedThreadId);rootNextId=0;rootLastScan=Now-1000000;
            PrototypePackIdentity=ExplorationPrototypeReader.CanonicalIdentity(pack);
            if(!EnableGroundedRoots){GroundedRootStatus="Disabled";return;}
            if(PrototypePackIdentity!=RootPackIdentity){GroundedRootStatus="Canonical pack differs; authored roots retained";return;}
            foreach(int id in new[]{10,11})
            {
                var asset=Resources.Load<TextAsset>("AshenVoxelRoot"+id);if(asset==null)continue;
                string pin=id==10?RootQueries.Model10Companion:RootQueries.Model11Companion;
                using var stream=new MemoryStream(asset.bytes,false);
                if(!TemplateBundle.TryRead(stream,pin,RootPackIdentity,id,out var bundle,out string why)){GroundedRootStatus="Companion rejected: "+why;continue;}
                var templates=ManagedTemplateAssembly.Create(bundle,new RootContactManagedComposite.Limits(),2*1024*1024,()=>true);
                rootShared.Add(id,new RootShared{Bundle=bundle,Templates=templates});
            }
            GroundedRootStatus=rootShared.Count==0?"Companions unavailable; authored roots retained":"Grounded roots ready";
        }
        bool RootPartition(int id,int lod)=>EnableGroundedRoots&&rootShared.ContainsKey(id)&&lod>=0&&lod<5;
        bool RootSupported(Model model)
        {for(int lod=0;lod<model.Meshes.Length;lod++)if(!(impostorMaterial!=null&&lod>=3)&&!model.Split[lod])return false;return true;}
        bool RootOwnerAlive(RootRequest q)=>cells.TryGetValue(q.Key.Cell,out var owner)&&ReferenceEquals(owner,q.Owner);
        bool RootSourceCurrent(RootRequest q)=>!rootClosed&&RootOwnerAlive(q)&&rootField.SourceIdentity==q.LiveSource;
        double RootDistance(RootRequest q)
        {double x=observer.X-q.Instance.Position.X,z=observer.Z-q.Instance.Position.Z;return Math.Sqrt(x*x+z*z);}
        static double RootRadius(RootRequest q)
        {
            var t=q.Shared.Bundle.Root;
            return Math.Max(Math.Max(Math.Abs(t.MinX),Math.Abs(t.MaxX+1)),Math.Max(Math.Abs(t.MinZ),Math.Abs(t.MaxZ+1)))*.1*Math.Sqrt(2);
        }
        bool RootSafe(RootRequest q)=>RootDistance(q)>RootRadius(q)+RootPlayerClearance;
        static RootCpu ComputeRoot(RootRequest q)
        {
            var result=new RootCpu{Request=q};var started=System.Diagnostics.Stopwatch.StartNew();
            try
            {
                Func<bool> current=()=>!q.Token.IsCancellationRequested&&q.Terrain.IsCurrent();
                var pose=new RootContactPrototype.Pose(q.Instance.Position.X,q.Instance.Position.Y,q.Instance.Position.Z,q.Instance.YawDegrees);
                using(var pipeline=new BuildPipeline(q.Shared.Bundle,q.Shared.Templates,q.Terrain,new RootCurrent(q.Terrain,q.Shared.Bundle.Context.Identity),pose,q.OwnerIdentity,65536,
                    new PipelineLimits(totalBytes:RootWorkerBytes),current,q.Token))
                {
                    while(!pipeline.Terminal){q.Token.ThrowIfCancellationRequested();pipeline.Step(8192,8192,8192,8192,1);}
                    if(pipeline.State!=BuildState.Complete||pipeline.Published==null)throw new InvalidOperationException(pipeline.Failure);
                    result.Artifact=pipeline.Published;
                }
                result.Collision=RootQueries.Create(result.Artifact.CellArtifact,q.Canonical,q.Shared.Bundle,RootPackIdentity,new RootContactCollision.BuildLimits(),current,q.Token);
                result.Prepared=new PreparedMesh[5];result.Bytes=result.Artifact.AccountedBytes+65536+result.Collision.SpanElementBytes;
                for(int lod=0;lod<5;lod++)
                {
                    q.Token.ThrowIfCancellationRequested();if(!current())throw new InvalidOperationException("Stale after compute");
                    var output=result.Artifact.Meshes.At(lod);var parts=new RootContactArrayMesher.MeshData[output.Count];
                    for(int i=0;i<parts.Length;i++)parts[i]=output.At(i).Data;
                    result.Prepared[lod]=UploadPreparation.PrepareLower(parts,lod,result.Artifact.Identity.Hash,new UploadLimits(262144,393216,8*1024*1024));
                    result.Bytes+=result.Prepared[lod].GpuPayloadBytes;
                    if(result.Bytes>RootCpuBytes)throw new InvalidOperationException("Root CPU result capacity");
                }
                if(!current())throw new InvalidOperationException("Stale before worker publication");
            }
            catch(Exception e){result.Failure=e.GetType().Name+": "+e.Message;result.Artifact=null;result.Collision=null;result.Prepared=null;result.Bytes=0;}
            result.ComputeMilliseconds=started.Elapsed.TotalMilliseconds;return result;
        }
        bool TryRootRequest(PopulationKey key,CachedCell cell,PopulationInstance instance,out RootRequest request)
        {
            request=null;if(instance.Scale!=1||!rootShared.TryGetValue(instance.PrototypeId,out var shared)||!models.TryGetValue(instance.PrototypeId,out var canonical)||!RootSupported(canonical))return false;
            var t=shared.Bundle.Root;double angle=instance.YawDegrees*Math.PI/180,co=Math.Cos(angle),si=Math.Sin(angle);
            double lowX=double.PositiveInfinity,lowZ=lowX,highX=double.NegativeInfinity,highZ=highX;
            foreach(int x in new[]{t.MinX,t.MaxX+1})foreach(int z in new[]{t.MinZ,t.MaxZ+1})
            {double gx=instance.Position.X*10+co*x+si*z,gz=instance.Position.Z*10-si*x+co*z;lowX=Math.Min(lowX,gx);highX=Math.Max(highX,gx);lowZ=Math.Min(lowZ,gz);highZ=Math.Max(highZ,gz);}
            string live=rootField.SourceIdentity;
            if(!RootTerrainSnapshot.TryCapture(rootField,(long)Math.Floor(lowX)-1,(long)Math.Ceiling(highX)+1,(long)Math.Floor(lowZ)-1,(long)Math.Ceiling(highZ)+1,100000,out var snapshot,out string why))
            {GroundedRootStatus="Terrain capture deferred: "+why;return false;}
            if(rootField.SourceIdentity!=live)return false;
            request=new RootRequest{Key=new RootKey(key,instance.StableKey),Owner=cell,Instance=instance,Shared=shared,Canonical=canonical.Data,Terrain=snapshot,LiveSource=live,
                OwnerIdentity="root-instance-v1/"+key.X+","+key.Z+","+key.Layer+"/"+instance.StableKey+"/epoch="+cell.Epoch};return true;
        }
        long RootCpuResident()
        {long bytes=rootUploading?.Cpu.Bytes??0;foreach(var b in rootBindings.Values)bytes+=b.Cpu.Bytes;return bytes;}
        void TickRoots()
        {
            PumpRootRetirements();if(rootClosed||!EnableGroundedRoots||rootShared.Count==0||!Ready)return;
            if(rootClosingTask!=null)
            {if(!rootClosingTask.IsCompleted){GroundedRootStatus="Waiting for prior cancelled worker";return;}if(rootClosingTask.IsFaulted){_ = rootClosingTask.Exception;}rootClosingTask=null;}
            if(rootTask!=null)
            {
                if(!RootSourceCurrent(rootRequest))rootCancellation.Cancel();
                if(rootTask.IsCompleted)
                {
                    RootCpu result=null;try{result=rootTask.GetAwaiter().GetResult();}catch(Exception e){GroundedRootStatus=e.GetType().Name;}
                    rootTask=null;rootCancellation.Dispose();rootCancellation=null;
                    if(result!=null&&result.Failure.Length==0&&RootSourceCurrent(result.Request))
                    {
                        rootWaiting=result;
                    }
                    else if(result!=null){GroundedRootStatus=result.Failure;rootRejected[result.Request.Key]=result.Request.LiveSource;}
                    rootRequest=null;
                }
            }
            if(rootWaiting!=null)
            {
                if(!RootSourceCurrent(rootWaiting.Request)){rootWaiting=null;GroundedRootStatus="Stale root result retired";}
                else if(RootCpuResident()+rootWaiting.Bytes<=RootCpuBytes){rootUploading=new RootBinding{Id=++rootNextId,Cpu=rootWaiting};rootWaiting=null;}
                else{GroundedRootStatus="Completed root waiting for CPU capacity";return;}
            }
            if(rootUploading!=null)
            {
                var b=rootUploading;
                if(!RootSourceCurrent(b.Cpu.Request)){RetireRoot(b);rootUploading=null;return;}
                var prepared=b.Cpu.Prepared[b.Uploaded];long reservation=prepared.GpuPayloadBytes*2+65536;
                if(rootNativeBytes+reservation>RootNativeBytes){GroundedRootStatus="Root upload waiting for native capacity";return;}
                rootNativeBytes+=reservation;
                bool ok=rootUploader.TryCreate(prepared,"Grounded root "+b.Id+" LOD"+b.Uploaded,ExplorationWind.MaximumDisplacementMeters,()=>RootSourceCurrent(b.Cpu.Request),
                    new UploadReservation(prepared.Identity,reservation),out var mesh,out var retirement,out string failure);
                if(!ok)
                {
                    if(retirement!=null)rootRetirements.Add(new RootRetirement{Failed=retirement,Reservation=reservation});else rootNativeBytes-=reservation;
                    GroundedRootStatus="Root upload failed: "+failure;rootRejected[b.Cpu.Request.Key]=b.Cpu.Request.LiveSource;RetireRoot(b);rootUploading=null;return;
                }
                int lod=b.Uploaded++;b.Meshes[lod]=mesh;b.Reservations[lod]=reservation;b.Triangles[lod]=prepared.Triangles(0);b.NativePayload+=prepared.GpuPayloadBytes;rootPayloadBytes+=prepared.GpuPayloadBytes;
                if(b.Uploaded==5)
                {
                    b.Cpu.Prepared=null;b.Cpu.Bytes=b.Cpu.Artifact.AccountedBytes+65536+b.Cpu.Collision.SpanElementBytes;
                    rootBindings.Add(b.Cpu.Request.Key,b);rootUploading=null;matricesDirty=true;
                    GroundedRootStatus="Root ready; "+b.Cpu.ComputeMilliseconds.ToString("F1")+"ms worker; "+b.NativePayload+" mesh bytes";
                }
                return; // One native upload per Tick, never a millisecond guarantee.
            }
            RetireUnusedRoots();
            if(rootTask!=null||rootBindings.Count>=RootBindingLimit||Ms(Now-rootLastScan)<500)return;
            rootLastScan=Now;PopulationInstance best=default;CachedCell bestCell=null;PopulationKey bestKey=default;double bestDistance=RootPrepareRange*RootPrepareRange;
            long cx=(long)Math.Floor(observer.X/64),cz=(long)Math.Floor(observer.Z/64);
            for(long z=cz-2;z<=cz+2;z++)for(long x=cx-2;x<=cx+2;x++)
            {
                var key=new PopulationKey(x,z,0);if(!cells.TryGetValue(key,out var cell))continue;
                foreach(var instance in cell.Data.Instances)
                {
                    if(instance.Scale!=1||!rootShared.TryGetValue(instance.PrototypeId,out var shared)||!models.TryGetValue(instance.PrototypeId,out var model)||!RootSupported(model))continue;
                    var identity=new RootKey(key,instance.StableKey);if(rootBindings.ContainsKey(identity)||rootRejected.TryGetValue(identity,out string source)&&source==rootField.SourceIdentity)continue;
                    double dx=observer.X-instance.Position.X,dz=observer.Z-instance.Position.Z,distance=dx*dx+dz*dz;
                    var template=shared.Bundle.Root;double radius=Math.Max(Math.Max(Math.Abs(template.MinX),Math.Abs(template.MaxX+1)),Math.Max(Math.Abs(template.MinZ),Math.Abs(template.MaxZ+1)))*.1*Math.Sqrt(2)+RootPlayerClearance;
                    if(distance>=bestDistance||distance<=radius*radius)continue;best=instance;bestCell=cell;bestKey=key;bestDistance=distance;
                }
            }
            if(bestCell==null)return;
            if(TryRootRequest(bestKey,bestCell,best,out var request))
            {
                if(rootRejected.Count>64)rootRejected.Clear();rootCancellation=new CancellationTokenSource();request.Token=rootCancellation.Token;rootRequest=request;
                rootTask=Task.Run(()=>ComputeRoot(request));GroundedRootStatus="Preparing root on one worker";
            }
        }
        void CaptureRootSelection()
        {
            rootNext=new Dictionary<RootKey,RootBinding>();
            foreach(var pair in rootCommitted)
                if(RootOwnerAlive(pair.Value.Cpu.Request)&&RootDistance(pair.Value.Cpu.Request)<=RootRetainRange||!RootSafe(pair.Value.Cpu.Request))rootNext.Add(pair.Key,pair.Value);
            foreach(var pair in rootBindings)
                if(!rootNext.ContainsKey(pair.Key)&&RootSourceCurrent(pair.Value.Cpu.Request)&&RootSafe(pair.Value.Cpu.Request)&&RootDistance(pair.Value.Cpu.Request)<=RootRetainRange)rootNext.Add(pair.Key,pair.Value);
            // Preserve the exact original root recipe through hide, async copy and restore.
            if(shrubLease!=null&&!shrubLease.Closed&&shrubLease.OwnerCell is CachedCell owner)
            {
                var key=new RootKey(owner.Data.Key,shrubLease.Instance.StableKey);
                if(shrubLease.NaturalRoot is RootBinding pinned)rootNext[key]=pinned;else rootNext.Remove(key);
            }
        }
        bool CommitRootSelection()
        {
            if(rootNext==null)return true;
            // A lease can arrive after this matrix build captured its root selection.
            // Reject that older build rather than publishing a different original recipe.
            if(shrubLease!=null&&!shrubLease.Closed&&shrubLease.OwnerCell is CachedCell leasedOwner)
            {
                rootNext.TryGetValue(new RootKey(leasedOwner.Data.Key,shrubLease.Instance.StableKey),out var selected);
                if(!ReferenceEquals(selected,shrubLease.NaturalRoot))return false;
            }
            foreach(var p in rootNext)if(!rootCommitted.TryGetValue(p.Key,out var old)||!ReferenceEquals(old,p.Value))
                if(!RootSafe(p.Value.Cpu.Request)||!RootSourceCurrent(p.Value.Cpu.Request))return false;
            foreach(var p in rootCommitted)if(!rootNext.TryGetValue(p.Key,out var next)||!ReferenceEquals(next,p.Value))if(!RootSafe(p.Value.Cpu.Request))return false;
            rootCommitted=rootNext;rootNext=null;return true;
        }
        RootBinding SelectedRoot(PopulationCell cell,PopulationInstance instance,bool next)
        {
            var selection=next?rootNext:rootCommitted;if(selection==null||!selection.TryGetValue(new RootKey(cell.Key,instance.StableKey),out var b))return null;
            return ReferenceEquals(b.Cpu.Request.Owner.Data,cell)?b:null;
        }
        bool RootInSelection(RootBinding b,Dictionary<RootKey,RootBinding> selection)=>selection!=null&&selection.TryGetValue(b.Cpu.Request.Key,out var other)&&ReferenceEquals(b,other);
        int RootTriangleCost(Model model,int lod,RootBinding root)
        {return model.Split[lod]&&root!=null?model.UpperTriangles[lod]+root.Triangles[lod]:model.Triangles[lod];}
        void AddRootAwarePages(PopulationInstance instance,Model model,int lod,double pageSize,RootBinding root)
        {
            if(!model.Split[lod]){AddRootPage(instance,model.Meshes[lod],lod,pageSize,0,0,0,model.Triangles[lod],true);return;}
            AddRootPage(instance,model.Meshes[lod],lod,pageSize,0,0,0,model.UpperTriangles[lod],true);
            if(root==null)AddRootPage(instance,model.Meshes[lod],lod,pageSize,1,0,1,model.BaseTriangles[lod],false);
            else AddRootPage(instance,root.Meshes[lod],lod,pageSize,2,root.Id,0,root.Triangles[lod],false);
        }
        void AddRootPage(PopulationInstance instance,Mesh mesh,int lod,double pageSize,int part,int variant,int submesh,int triangles,bool countsInstance)
        {
            if(triangles==0)return;
            var key=new PageKey(instance.PrototypeId,lod,(long)Math.Floor(instance.Position.X/pageSize),(long)Math.Floor(instance.Position.Z/pageSize),part,variant);
            if(!buildingPages.TryGetValue(key,out var page))
            {
                page=pagePool.Count>0?pagePool.Pop():new Page();page.Key=key;page.HasBounds=false;page.Matrices.Clear();page.Mesh=mesh;page.Submesh=submesh;page.Triangles=triangles;page.CountsInstance=countsInstance;buildingPages.Add(key,page);
            }
            Vector3 position=new Vector3((float)(instance.Position.X-origin.X),(float)(instance.Position.Y-origin.Y),(float)(instance.Position.Z-origin.Z));
            page.Matrices.Add(Matrix4x4.TRS(position,Quaternion.Euler(0,instance.YawDegrees,0),Vector3.one));
            Bounds actual=mesh.bounds;float radius=Math.Max(Math.Abs(actual.min.x),Math.Max(Math.Abs(actual.max.x),Math.Max(Math.Abs(actual.min.z),Math.Abs(actual.max.z))))*1.415f+.1f;
            var bounds=new Bounds(position+Vector3.up*actual.center.y,new Vector3(radius*2,actual.size.y+.2f,radius*2));
            if(!page.HasBounds){page.Bounds=bounds;page.HasBounds=true;}else page.Bounds.Encapsulate(bounds);
        }
        void RetireUnusedRoots()
        {
            rootRemove.Clear();foreach(var p in rootBindings)if(!RootInSelection(p.Value,rootCommitted)&&!RootInSelection(p.Value,rootNext)&&
                (!RootOwnerAlive(p.Value.Cpu.Request)||RootDistance(p.Value.Cpu.Request)>RootRetainRange))rootRemove.Add(p.Key);
            foreach(var key in rootRemove){RetireRoot(rootBindings[key]);rootBindings.Remove(key);}
        }
        void RetireRoot(RootBinding b)
        {
            if(b.Retiring)return;b.Retiring=true;
            for(int lod=0;lod<b.Uploaded;lod++)rootRetirements.Add(new RootRetirement{Mesh=b.Meshes[lod],Reservation=b.Reservations[lod],Payload=b.Cpu.Artifact.Meshes.At(lod).RetainedMeshBytes});
        }
        void PumpRootRetirements()
        {
            for(int i=rootRetirements.Count-1;i>=0;i--)
            {
                var r=rootRetirements[i];bool retired=false;
                if(r.Failed!=null){if(!r.Requested)r.Requested=r.Failed.TryRequestDestroy(out _);r.Failed.TryConfirmRetired(out retired,out _);}
                else{if(!r.Requested){if(r.Mesh!=null)Destroy(r.Mesh);r.Requested=true;}retired=r.Mesh==null;}
                if(retired){rootNativeBytes-=r.Reservation;rootPayloadBytes-=r.Payload;rootRetirements.RemoveAt(i);}
            }
        }
        void DisposeRoots()
        {
            rootClosed=true;rootCancellation?.Cancel();
            if(rootTask!=null){rootClosingTask=rootTask;rootTask.ContinueWith(static task=>{if(task.IsFaulted){_ = task.Exception;}},TaskScheduler.Default);}
            rootTask=null;rootRequest=null;rootWaiting=null;rootCancellation?.Dispose();rootCancellation=null;
            if(rootUploading!=null)RetireRoot(rootUploading);rootUploading=null;
            foreach(var b in rootBindings.Values)RetireRoot(b);rootBindings.Clear();rootCommitted.Clear();rootNext=null;rootRejected.Clear();rootShared.Clear();PumpRootRetirements();
            rootField=null;GroundedRootStatus="Closed; native retirement requested";
        }
    }
}
