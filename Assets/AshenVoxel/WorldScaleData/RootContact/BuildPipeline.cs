using System;
using System.IO;
using System.Threading;
using AshenVoxel.Core;
using RootContactPrototype;
using RootContactInteraction;
using RootContactCache;
using RootContactTemplateCache;
using RootContactManagedComposite;
using CompositeLimits=RootContactManagedComposite.Limits;
using CompositePhase=RootContactManagedComposite.Phase;

namespace RootContactBuildPipeline
{
    public sealed class BuildIdentity
    {
        public const string PolicyIdentity="root-complete-compute-v1;managed32d7948cb2232263d809a5c3d266b6ae264255e525009bb72450c4d358c514b5;array6424b3bd44e09caf291ee434bbd122ebf90286e8c5b6abaa2f1382ce9895ba27";
        public readonly string Hash,BundleFile,Pack,RootSource,CanonicalSource,ManagedTemplates,OwnerEdit;
        public readonly RootContactCache.Identity Root;
        public readonly long MinX,MaxX,MinZ,MaxZ,AccountedBytes;
        public readonly int ModelId,Seed;
        public BuildIdentity(TemplateBundle bundle,ManagedTemplateAssembly shared,ITerrain terrain,Pose pose,string ownerEdit)
        {
            if(bundle==null||shared==null||terrain==null||string.IsNullOrEmpty(ownerEdit)||ownerEdit.Length>4096||shared.Identity!=TemplateBundle.PolicyIdentity+"/"+bundle.FileIdentity)throw new ArgumentException("Complete matching inputs/owner identity required");
            ModelId=bundle.ModelId;Seed=bundle.Seed;BundleFile=bundle.FileIdentity;Pack=bundle.PackIdentity;RootSource=bundle.RootSourceIdentity;CanonicalSource=bundle.CanonicalSourceIdentity;ManagedTemplates=shared.Identity;OwnerEdit=ownerEdit;Root=RootContactCache.Identity.For(bundle.Root,terrain,bundle.Context,pose);
            MinX=terrain.MinX;MaxX=terrain.MaxX;MinZ=terrain.MinZ;MaxZ=terrain.MaxZ;
            using var stream=new MemoryStream();using var writer=new BinaryWriter(stream);writer.Write(PolicyIdentity);writer.Write(TemplateBundle.PolicyIdentity);writer.Write(ModelId);writer.Write(Seed);
            foreach(string value in new[]{BundleFile,Pack,RootSource,CanonicalSource,ManagedTemplates,OwnerEdit,Root.Hash})writer.Write(value);
            writer.Write(MinX);writer.Write(MaxX);writer.Write(MinZ);writer.Write(MaxZ);writer.Flush();Hash=Policy.Hash(stream.ToArray());
            AccountedBytes=512+2L*(Hash.Length+BundleFile.Length+Pack.Length+RootSource.Length+CanonicalSource.Length+ManagedTemplates.Length+OwnerEdit.Length+PolicyIdentity.Length+Root.Hash.Length+Root.Template.Length+Root.Terrain.Length+Root.RootPolicy.Length+Root.Context.Length+Root.InteractionPolicy.Length);
        }
    }
    public readonly struct CellData
    {
        public readonly Int3 Position;public readonly uint Color;public readonly Channel Role;public MaterialId Material=>MaterialId.Wood;
        internal CellData(Int3 p,uint color,Channel role){Position=p;Color=color;Role=role;}
    }
    public sealed class CompleteArtifact
    {
        readonly OwnedArtifact cells;public readonly BuildIdentity Identity;public readonly CompositeOutput Meshes;public readonly long AccountedBytes;
        public int CellCount=>cells.CellCount;public int RenderCellCount=>cells.RenderCellCount;public int ContextCellCount=>cells.ContextCellCount;
        public long CellPayloadBytes=>cells.PayloadBytes;
        public OwnedArtifact CellArtifact=>cells;
        public CellData CellAt(int i)=>new CellData(cells.Position(i),cells.Color(i),cells.Role(i));
        internal CompleteArtifact(BuildIdentity identity,OwnedArtifact owned,CompositeOutput meshes)
        {if(owned.Identity.Hash!=identity.Root.Hash||meshes.Identity!=identity.Hash||meshes.Count!=5)throw new ArgumentException("Incoherent complete artifact");Identity=identity;cells=owned;Meshes=meshes;AccountedBytes=owned.AccountedBytes+meshes.DeclaredOutputElementBytes+identity.AccountedBytes;}
    }
    public sealed class PipelineLimits
    {
        public readonly long MaxSharedDeclaredBytes,MaxOwnedCellBytes,MaxStagingBytes,MaxArtifactBytes,MaxTotalDeclaredBytes;
        public readonly CompositeLimits Composite;
        public PipelineLimits(CompositeLimits composite=null,long sharedBytes=8*1024*1024,long ownedCellBytes=2*1024*1024,long stagingBytes=65536*16,long artifactBytes=8*1024*1024,long totalBytes=64*1024*1024)
        {if(sharedBytes<0||ownedCellBytes<0||stagingBytes<0||artifactBytes<0||totalBytes<0||totalBytes>512L*1024*1024)throw new ArgumentException("Pipeline byte limits");Composite=composite??new CompositeLimits();MaxSharedDeclaredBytes=sharedBytes;MaxOwnedCellBytes=ownedCellBytes;MaxStagingBytes=stagingBytes;MaxArtifactBytes=artifactBytes;MaxTotalDeclaredBytes=totalBytes;}
    }
    public enum BuildState{Root,PackMeshInput,Composite,Complete,Cancelled,Stale,Rejected,Failed,Disposed}
    // Single compute owner. It can run on one Task with immutable captured inputs;
    // this is not a queue, scheduler, production cache or native publication owner.
    public sealed class BuildPipeline:IDisposable
    {
        TemplateBundle bundle;ManagedTemplateAssembly shared;ITerrain terrain;ICurrentIdentities identities;Func<bool> callerCurrent;
        readonly PipelineLimits limits;readonly CancellationToken token;
        FrozenJob rootJob;OwnedArtifact owned;RootCell[] staging;CompositeJob composite;int copyAt,renderAt;
        public readonly BuildIdentity Identity;public readonly long BundleElementBytes,SharedManagedElementBytes,TerrainReservationBytes;
        public BuildState State{get;private set;}=BuildState.Root;public string Failure{get;private set;}="";public CompleteArtifact Published{get;private set;}
        public bool Terminal=>State>=BuildState.Complete;public string RootPhase=>rootJob?.Phase??"Released";
        public int PackedMeshCells=>renderAt;public int RemainingPackingCells=>State==BuildState.PackMeshInput?owned.CellCount-copyAt:0;
        public int CompositeLods=>composite?.CompletedLods??(State==BuildState.Complete?5:0);
        public long RootCandidates{get;private set;}public long PackingCandidates{get;private set;}public long CompositeRecordCandidates{get;private set;}public long CompositeFaceCandidates{get;private set;}public int CompositeChunkCalls{get;private set;}
        public long PeakDeclaredReservationBytes{get;private set;}public long PeakCompositeObservedDeclaredBytes{get;private set;}
        public long CurrentDeclaredReservationBytes{get;private set;}public long FinalCellAccountedBytes{get;private set;}
        public BuildPipeline(TemplateBundle bundle,ManagedTemplateAssembly shared,ITerrain terrain,ICurrentIdentities identities,Pose pose,string ownerEditIdentity,long terrainReservationBytes,PipelineLimits limits,Func<bool> isCurrent,CancellationToken token=default)
        {
            if(bundle==null||shared==null||terrain==null||identities==null||limits==null||isCurrent==null||terrainReservationBytes<0)throw new ArgumentException("Pipeline inputs");
            this.bundle=bundle;this.shared=shared;this.terrain=terrain;this.identities=identities;this.limits=limits;callerCurrent=isCurrent;this.token=token;
            Identity=new BuildIdentity(bundle,shared,terrain,pose,ownerEditIdentity);TerrainReservationBytes=terrainReservationBytes;SharedManagedElementBytes=shared.DeclaredRetainedElementBytes;
            long fixedCount=0;for(int i=0;i<bundle.LodCount;i++)fixedCount+=bundle.Lod(i).Count;
            BundleElementBytes=6L*bundle.Root.SourceCells+8L*bundle.Root.Count+16L*bundle.Context.Count+12L*bundle.OrderCount+32L*fixedCount+40+2L*(bundle.FileIdentity.Length+bundle.PackIdentity.Length+bundle.RootSourceIdentity.Length+bundle.CanonicalSourceIdentity.Length+bundle.Root.Identity.Length+bundle.Root.SourceIdentity.Length+bundle.Context.Identity.Length+bundle.Context.CanonicalSourceIdentity.Length);
            if(BundleElementBytes+SharedManagedElementBytes+TerrainReservationBytes>limits.MaxSharedDeclaredBytes)throw new ArgumentException("SharedReservation");
            if(!Guard())return;if(!Reserve(ExternalShared+SharedManagedElementBytes+Identity.AccountedBytes+FrozenJob.WorkingReservation))return;
            rootJob=new FrozenJob(bundle.Root,terrain,bundle.Context,identities,pose);if(rootJob.Identity.Hash!=Identity.Root.Hash){Stop(BuildState.Failed,"RootIdentity");return;}Guard();
        }
        long ExternalShared=>BundleElementBytes+TerrainReservationBytes;
        bool ComputeCurrent()
        {
            if(Terminal||token.IsCancellationRequested)return false;
            // A caller may invalidate/cancel this producer from its predicate.
            // Keep captured references local and never dereference released fields.
            var t=terrain;var ids=identities;var current=callerCurrent;
            if(t==null||ids==null||current==null||!current()||Terminal||token.IsCancellationRequested)return false;
            return t.SourceIdentity==Identity.Root.Terrain&&t.MinX==Identity.MinX&&t.MaxX==Identity.MaxX&&t.MinZ==Identity.MinZ&&t.MaxZ==Identity.MaxZ&&ids.SourceIdentity==Identity.Root.Terrain&&ids.ContextIdentity==Identity.Root.Context;
        }
        bool Guard()
        {
            if(Terminal)return false;
            try
            {
                if(token.IsCancellationRequested){Stop(BuildState.Cancelled,"Cancelled");return false;}
                bool full=ComputeCurrent();if(Terminal)return false;if(token.IsCancellationRequested){Stop(BuildState.Cancelled,"Cancelled");return false;}
                if(!full){Stop(BuildState.Stale,"CurrentIdentity");return false;}return true;
            }
            catch(Exception){if(!Terminal)Stop(BuildState.Failed,"CurrentPredicate");return false;}
        }
        bool Reserve(long bytes)
        {PeakDeclaredReservationBytes=Math.Max(PeakDeclaredReservationBytes,bytes);if(bytes>limits.MaxTotalDeclaredBytes){Stop(BuildState.Rejected,"AggregateReservation");return false;}CurrentDeclaredReservationBytes=bytes;return true;}
        void ReleaseWorking()
        {
            rootJob?.Dispose();rootJob=null;composite?.Cancel();composite=null;owned=null;staging=null;bundle=null;shared=null;terrain=null;identities=null;callerCurrent=null;
        }
        void Stop(BuildState state,string why){State=state;Failure=why;Published=null;ReleaseWorking();CurrentDeclaredReservationBytes=0;}
        public void Cancel(){if(!Terminal)Stop(BuildState.Cancelled,"Cancelled");}
        // A caller that retained the immutable CompleteArtifact keeps ownership;
        // dropping this producer's reference never mutates or disposes that artifact.
        public void Dispose(){if(State==BuildState.Disposed)return;ReleaseWorking();Published=null;CurrentDeclaredReservationBytes=0;State=BuildState.Disposed;}
        public void Step(int rootBudget,int packingBudget,int recordBudget,int faceBudget,int chunkBudget)
        {
            if(rootBudget<0||packingBudget<0||recordBudget<0||faceBudget<0||chunkBudget<0)throw new ArgumentOutOfRangeException();if(!Guard())return;
            try
            {
                if(State==BuildState.Root)
                {
                    if(rootBudget==0)return;int used=rootJob.Step(rootBudget);RootCandidates+=used;if(used<0||used>rootBudget){Stop(BuildState.Failed,"FrozenWorkAccounting");return;}if(!Guard())return;
                    if(rootJob.State==JobState.Failed||rootJob.State==JobState.Cancelled){Stop(BuildState.Failed,"FrozenJob:"+rootJob.Failure);return;}if(rootJob.State!=JobState.Complete)return;
                    var complete=rootJob.Output;if(complete==null||complete.Identity.Hash!=Identity.Root.Hash||complete.CellCount<1||complete.CellCount>Policy.MaxOccupied||complete.RenderCellCount+complete.ContextCellCount!=complete.CellCount||complete.AccountedBytes>limits.MaxOwnedCellBytes){Stop(BuildState.Rejected,"OwnedCells");return;}
                    // Capture the immutable object BEFORE Dispose clears FrozenJob.Output.
                    owned=complete;FinalCellAccountedBytes=owned.AccountedBytes;rootJob.Dispose();rootJob=null;
                    long inputBytes=16L*owned.RenderCellCount;if(inputBytes>limits.MaxStagingBytes){Stop(BuildState.Rejected,"StagingCapacity");return;}
                    if(!Guard()||!Reserve(ExternalShared+SharedManagedElementBytes+Identity.AccountedBytes+owned.AccountedBytes+inputBytes))return;
                    staging=new RootCell[owned.RenderCellCount];State=BuildState.PackMeshInput;if(!Guard())return;
                }
                if(State==BuildState.PackMeshInput)
                {
                    while(copyAt<owned.CellCount&&packingBudget-->0)
                    {
                        if(!Guard())return;int i=copyAt++;PackingCandidates++;var p=owned.Position(i);var role=owned.Role(i);uint color=owned.Color(i);
                        if(color>>24!=255||p.y>30||p.y==30&&role!=Channel.None||p.y<30&&role!=Channel.Hard&&role!=Channel.Stick){Stop(BuildState.Failed,"OwnedRoleDomain");return;}
                        if(p.y<30){if(renderAt>=staging.Length){Stop(BuildState.Failed,"RenderCount");return;}staging[renderAt++]=new RootCell(p,color);}if(!Guard())return;
                    }
                    if(copyAt<owned.CellCount)return;if(renderAt!=staging.Length){Stop(BuildState.Failed,"RenderCount");return;}
                    if(!Guard()||!Reserve(ExternalShared+Identity.AccountedBytes+owned.AccountedBytes+16L*staging.Length+limits.Composite.MaxTotalBytes))return;
                    var acquired=new CompositeJob(shared.CopyTemplateReferences(),staging,Identity.Hash,limits.Composite,ComputeCurrent,token);
                    if(Terminal){acquired.Cancel();return;}composite=acquired;if(!Guard())return;staging=null;State=BuildState.Composite;
                    if(!Reserve(ExternalShared+Identity.AccountedBytes+owned.AccountedBytes+limits.Composite.MaxTotalBytes))return;
                }
                if(State==BuildState.Composite)
                {
                    if(!Guard())return;var running=composite;long records=running.FixedCandidates+running.RootCandidates,faces=running.FaceCandidates;int chunks=running.MeshChunkCalls;
                    running.Step(recordBudget,faceBudget,chunkBudget);
                    CompositeRecordCandidates+=running.FixedCandidates+running.RootCandidates-records;CompositeFaceCandidates+=running.FaceCandidates-faces;CompositeChunkCalls+=running.MeshChunkCalls-chunks;
                    if(Terminal)return;
                    PeakCompositeObservedDeclaredBytes=Math.Max(PeakCompositeObservedDeclaredBytes,ExternalShared+Identity.AccountedBytes+owned.AccountedBytes+running.PeakDeclaredTotalBytes);
                    if(!Guard())return;if(!running.Terminal)return;
                    if(running.State!=CompositePhase.Complete||running.Published==null){Stop(running.State==CompositePhase.Cancelled?BuildState.Cancelled:running.State==CompositePhase.Stale?BuildState.Stale:running.State==CompositePhase.Rejected?BuildState.Rejected:BuildState.Failed,"Composite:"+running.Failure);return;}
                    var meshes=running.Published;composite=null;running=null; // Drop the job, not just its publication counter.
                    if(!Guard())return;var result=new CompleteArtifact(Identity,owned,meshes);if(result.AccountedBytes>limits.MaxArtifactBytes){Stop(BuildState.Rejected,"ArtifactCapacity");return;}
                    if(!Reserve(ExternalShared+SharedManagedElementBytes+result.AccountedBytes)||!Guard())return;Published=result;if(!Guard())return;
                    State=BuildState.Complete;ReleaseWorking();CurrentDeclaredReservationBytes=result.AccountedBytes;
                }
            }
            catch(Exception e){if(!Terminal)Stop(BuildState.Failed,e.GetType().Name);else ReleaseWorking();}
        }
    }
}
