using System;
using System.Collections.Generic;
using System.IO;
using AshenVoxel.Core;
using RootContactPrototype;
using RootContactInteraction;

namespace RootContactCache
{
    public sealed class Identity
    {
        public readonly string Hash,Template,Terrain,RootPolicy,Context,InteractionPolicy;
        public readonly Pose Pose;
        public Identity(string template,string terrain,string rootPolicy,string context,string interactionPolicy,Pose pose)
        {
            foreach(string value in new[]{template,terrain,rootPolicy,context,interactionPolicy})if(string.IsNullOrEmpty(value)||value.Length>4096)throw new ArgumentException("Bounded nonempty identity required");
            Template=template;Terrain=terrain;RootPolicy=rootPolicy;Context=context;InteractionPolicy=interactionPolicy;Pose=pose;
            using var stream=new MemoryStream();using var writer=new BinaryWriter(stream);
            writer.Write("root-contact-cache-key-v1");writer.Write(template);writer.Write(terrain);writer.Write(rootPolicy);writer.Write(context);writer.Write(interactionPolicy);
            writer.Write(pose.X);writer.Write(pose.Y);writer.Write(pose.Z);writer.Write(pose.YawDegrees);writer.Flush();Hash=Policy.Hash(stream.ToArray());
        }
        public static Identity For(Template template,ITerrain terrain,UpperContext context,Pose pose)=>new Identity(template.Identity,terrain.SourceIdentity,Policy.Identity+";seam="+template.DeformSeam,context.Identity,RootContactInteraction.Builder.InteractionPolicy,pose);
    }
    // Complete CPU cell payload only. No mesh, collider, native handle or production residency claim.
    // Arrays are exclusively transferred from the internal producer and never exposed.
    public sealed class OwnedArtifact
    {
        readonly Int3[] positions;readonly uint[] colors;readonly byte[] roles;
        public readonly Identity Identity;
        public readonly int RenderCellCount,ContextCellCount;
        public int CellCount=>positions.Length;
        public long PayloadBytes=>17L*CellCount;
        // Exact element payload + explicit conservative envelope reservation (not measured CLR heap).
        public long AccountedBytes=>PayloadBytes+512+2L*(Identity.Hash.Length+Identity.Template.Length+Identity.Terrain.Length+Identity.RootPolicy.Length+Identity.Context.Length+Identity.InteractionPolicy.Length);
        internal OwnedArtifact(Identity identity,Int3[] p,uint[] c,byte[] r,int render,int context)
        {Identity=identity;positions=p;colors=c;roles=r;RenderCellCount=render;ContextCellCount=context;}
        internal Int3 Position(int i)=>positions[i];internal uint Color(int i)=>colors[i];internal Channel Role(int i)=>(Channel)roles[i];
    }
    public enum JobState { Running,Complete,Cancelled,Failed }
    public interface IJob:IDisposable
    {
        Identity Identity{get;}long ReservedBytes{get;}JobState State{get;}string Phase{get;}string Failure{get;}OwnedArtifact Output{get;}
        int Step(int candidateBudget);void Cancel();
    }
    public sealed class Limits
    {
        public readonly int MaxPending,MaxReadyEntries,MaxLeases;
        public readonly long MaxReadyBytes,MaxWorkerReservationBytes,MaxResidentAccountedBytes;
        public Limits(int pending=4,int readyEntries=2,int leases=4,long readyBytes=2*1024*1024,long workerBytes=24*1024*1024,long residentBytes=26*1024*1024)
        {
            if(pending<1||pending>64||readyEntries<1||readyEntries>64||leases<1||leases>128||readyBytes<1||readyBytes>256L*1024*1024||workerBytes<1||workerBytes>256L*1024*1024||residentBytes<Math.Max(readyBytes,workerBytes)||residentBytes>512L*1024*1024)throw new ArgumentException("Invalid bounded controller limits");
            MaxPending=pending;MaxReadyEntries=readyEntries;MaxLeases=leases;MaxReadyBytes=readyBytes;MaxWorkerReservationBytes=workerBytes;MaxResidentAccountedBytes=residentBytes;
        }
    }
    public enum TicketState { Queued,Running,WaitingPublication,Published,Cancelled,Stale,Failed }
    public enum Admission { Queued,ExistingPending,Cached,QueueFull,ReservationTooLarge,Stale,Invalid }
    public sealed class Ticket
    {
        public readonly Identity Identity;public readonly long ReservationBytes;
        public TicketState State{get;internal set;}=TicketState.Queued;public string Reason{get;internal set;}="";public long JobCandidates{get;internal set;}
        internal Func<IJob> Factory;internal Func<bool> Current;
        internal Ticket(Identity identity,long bytes,Func<IJob> factory,Func<bool> current){Identity=identity;ReservationBytes=bytes;Factory=factory;Current=current;}
    }
    sealed class Entry
    {
        internal OwnedArtifact Artifact;internal Func<bool> Current;internal long LastUse;internal int Pins;internal bool Retired;
    }
    public sealed class Lease:IDisposable
    {
        Controller owner;Entry entry;
        internal Lease(Controller c,Entry e){owner=c;entry=e;}
        Entry Read()
        {
            if(entry==null)throw new ObjectDisposedException(nameof(Lease));
            if(entry.Retired||!Controller.IsCurrent(entry.Current))throw new InvalidOperationException("Stale cache lease");return entry;
        }
        public int CellCount=>Read().Artifact.CellCount;public int RenderCellCount=>Read().Artifact.RenderCellCount;
        public int ContextCellCount=>Read().Artifact.ContextCellCount;public string IdentityHash=>Read().Artifact.Identity.Hash;
        public Int3 Position(int i)=>Read().Artifact.Position(i);public uint Color(int i)=>Read().Artifact.Color(i);public Channel Role(int i)=>Read().Artifact.Role(i);
        public MaterialId Material(int i){Read().Artifact.Position(i);return MaterialId.Wood;}
        public void Dispose(){if(entry!=null){owner.Release(entry);entry=null;owner=null;}}
    }
    public readonly struct PumpResult
    {
        public readonly int WorkUnits,JobCandidates;public readonly string WaitingReason;
        internal PumpResult(int work,int candidates,string waiting){WorkUnits=work;JobCandidates=candidates;WaitingReason=waiting;}
    }
    // Single-owner cooperative controller; no threads, hidden work queue, cache singleton or timers.
    public sealed class Controller
    {
        readonly Limits limits;readonly LinkedList<Ticket> queue=new LinkedList<Ticket>();readonly Dictionary<string,Ticket> pending=new Dictionary<string,Ticket>();
        readonly Dictionary<string,Entry> ready=new Dictionary<string,Entry>();
        Ticket active;IJob job;long clock,readyBytes;int leases;bool pumping;
        public Controller(Limits configuration){limits=configuration??throw new ArgumentNullException(nameof(configuration));}
        public int PendingCount=>pending.Count;public int QueuedCount=>queue.Count;public int ReadyCount=>ready.Count;public int ActiveLeases=>leases;
        public long ReadyAccountedBytes=>readyBytes;public long WorkerReservedBytes=>active?.ReservationBytes??0;
        public long ResidentAccountedBytes=>readyBytes+WorkerReservedBytes;
        public string WorkerPhase=>active==null?"Idle":active.State==TicketState.WaitingPublication?"WaitingPublication":job?.Phase??"Starting";
        public long TotalJobCandidates{get;private set;}public long TotalWorkUnits{get;private set;}public long Evictions{get;private set;}
        public long PeakResidentAccountedBytes{get;private set;}
        public Identity QueuedIdentity(int index){if(index<0||index>=queue.Count)throw new ArgumentOutOfRangeException(nameof(index));var n=queue.First;while(index-->0)n=n.Next;return n.Value.Identity;}
        internal static bool IsCurrent(Func<bool> current){try{return current();}catch{return false;}}
        void ExternalCall(){if(pumping)throw new InvalidOperationException("Controller callbacks must not reenter the controller");}
        public Admission Request(Identity identity,long reservation,Func<IJob> factory,Func<bool> current,out Ticket ticket)
        {
            ExternalCall();
            ticket=null;if(identity==null||factory==null||current==null||reservation<1)return Admission.Invalid;
            if(reservation>limits.MaxWorkerReservationBytes||reservation>limits.MaxResidentAccountedBytes)return Admission.ReservationTooLarge;
            if(!IsCurrent(current))return Admission.Stale;
            if(ready.TryGetValue(identity.Hash,out Entry entry))
            {if(!entry.Retired&&IsCurrent(entry.Current))return Admission.Cached;Retire(entry);}
            if(pending.TryGetValue(identity.Hash,out ticket))return Admission.ExistingPending;
            if(pending.Count>=limits.MaxPending)return Admission.QueueFull;
            ticket=new Ticket(identity,reservation,factory,current);pending.Add(identity.Hash,ticket);queue.AddLast(ticket);return Admission.Queued;
        }
        public bool TryAcquire(Identity identity,out Lease lease)
        {
            ExternalCall();
            lease=null;if(identity==null||!ready.TryGetValue(identity.Hash,out Entry entry))return false;
            if(entry.Retired||!IsCurrent(entry.Current)){Retire(entry);return false;}
            if(leases==limits.MaxLeases)return false;
            entry.Pins++;leases++;entry.LastUse=++clock;lease=new Lease(this,entry);return true;
        }
        internal void Release(Entry entry){ExternalCall();entry.Pins--;leases--;if(entry.Retired&&entry.Pins==0)Remove(entry,false);}
        void Retire(Entry entry){entry.Retired=true;if(entry.Pins==0)Remove(entry,false);}
        void Remove(Entry entry,bool evicted)
        {if(ready.Remove(entry.Artifact.Identity.Hash)){readyBytes-=entry.Artifact.AccountedBytes;if(evicted)Evictions++;}}
        bool EvictOne()
        {
            Entry oldest=null;foreach(var e in ready.Values)if(e.Pins==0&&(oldest==null||e.LastUse<oldest.LastUse))oldest=e;
            if(oldest==null)return false;Remove(oldest,true);return true;
        }
        // Explicit invalidation never discards pending work silently: tickets become Stale.
        public void InvalidateStale()
        {
            ExternalCall();
            var stale=new List<Entry>();foreach(var e in ready.Values)if(!IsCurrent(e.Current))stale.Add(e);foreach(var e in stale)Retire(e);
            var node=queue.First;while(node!=null){var next=node.Next;if(!IsCurrent(node.Value.Current)){FinishQueued(node,TicketState.Stale,"StaleIdentity");}node=next;}
            if(active!=null&&!IsCurrent(active.Current))FinishActive(TicketState.Stale,"StaleIdentity");
        }
        void FinishQueued(LinkedListNode<Ticket> node,TicketState state,string reason)
        {Ticket t=node.Value;queue.Remove(node);pending.Remove(t.Identity.Hash);t.State=state;t.Reason=reason;t.Factory=null;t.Current=null;}
        public bool Cancel(Ticket ticket)
        {
            ExternalCall();
            if(ticket==null||!pending.TryGetValue(ticket.Identity.Hash,out Ticket actual)||!ReferenceEquals(ticket,actual))return false;
            if(ReferenceEquals(ticket,active)){FinishActive(TicketState.Cancelled,"Cancelled");return true;}
            var n=queue.First;while(n!=null){if(ReferenceEquals(n.Value,ticket)){FinishQueued(n,TicketState.Cancelled,"Cancelled");return true;}n=n.Next;}return false;
        }
        void FinishActive(TicketState state,string reason)
        {
            Ticket t=active;
            try{if(job!=null&&state!=TicketState.Published)job.Cancel();}catch{}
            try{job?.Dispose();}catch{}
            job=null;active=null;pending.Remove(t.Identity.Hash);t.State=state;t.Reason=reason;t.Factory=null;t.Current=null;
        }
        void Peak(){PeakResidentAccountedBytes=Math.Max(PeakResidentAccountedBytes,ResidentAccountedBytes);}
        public PumpResult Pump(int candidateBudget)
        {
            ExternalCall();pumping=true;
            try{return PumpCore(candidateBudget);}finally{pumping=false;}
        }
        PumpResult PumpCore(int candidateBudget)
        {
            if(candidateBudget<0)throw new ArgumentOutOfRangeException(nameof(candidateBudget));int work=0,spent=0;string waiting="";
            while(work<candidateBudget)
            {
                if(active==null)
                {
                    if(queue.Count==0)break;
                    work++;Ticket next=queue.First.Value;
                    if(!IsCurrent(next.Current)){FinishQueued(queue.First,TicketState.Stale,"StaleIdentity");continue;}
                    while(readyBytes+next.ReservationBytes>limits.MaxResidentAccountedBytes&&EvictOne()){}
                    if(readyBytes+next.ReservationBytes>limits.MaxResidentAccountedBytes){waiting="PinnedResidentCapacity";break;}
                    queue.RemoveFirst();active=next;next.State=TicketState.Running;Peak();
                    try
                    {
                        job=next.Factory();
                        if(job==null||job.Identity.Hash!=next.Identity.Hash||job.ReservedBytes>next.ReservationBytes||job.ReservedBytes<1){FinishActive(TicketState.Failed,"InvalidJobContract");continue;}
                    }
                    catch(Exception e){FinishActive(TicketState.Failed,"Factory:"+e.GetType().Name);continue;}
                    continue;
                }
                if(!IsCurrent(active.Current)){work++;FinishActive(TicketState.Stale,"StaleIdentity");continue;}
                if(job.State==JobState.Cancelled||job.State==JobState.Failed)
                {work++;FinishActive(TicketState.Failed,job.Failure);continue;}
                if(job.State==JobState.Complete)
                {
                    work++;OwnedArtifact output=job.Output;
                    if(output==null||output.Identity.Hash!=active.Identity.Hash||output.CellCount<1||output.CellCount>Policy.MaxOccupied||output.RenderCellCount+output.ContextCellCount!=output.CellCount||output.AccountedBytes>limits.MaxReadyBytes||output.AccountedBytes>active.ReservationBytes)
                    {FinishActive(TicketState.Failed,"InvalidOrOversizedArtifact");continue;}
                    // Transfer would replace the worker reservation, not double-charge the same artifact.
                    while((ready.Count>=limits.MaxReadyEntries||readyBytes+output.AccountedBytes>limits.MaxReadyBytes||readyBytes+output.AccountedBytes>limits.MaxResidentAccountedBytes)&&EvictOne()){}
                    if(ready.TryGetValue(output.Identity.Hash,out Entry sameIdentity)&&sameIdentity.Pins>0)
                    {active.State=TicketState.WaitingPublication;waiting="RetiredIdentityPinned";break;}
                    if(sameIdentity!=null)Remove(sameIdentity,false);
                    if(ready.Count>=limits.MaxReadyEntries||readyBytes+output.AccountedBytes>limits.MaxReadyBytes||readyBytes+output.AccountedBytes>limits.MaxResidentAccountedBytes)
                    {active.State=TicketState.WaitingPublication;waiting="PinnedPublicationCapacity";break;}
                    if(!IsCurrent(active.Current)){FinishActive(TicketState.Stale,"StaleBeforePublication");continue;}
                    var entry=new Entry{Artifact=output,Current=active.Current,LastUse=++clock};ready.Add(output.Identity.Hash,entry);readyBytes+=output.AccountedBytes;
                    // No callback/read can observe the entry until this single-owner method returns.
                    FinishActive(TicketState.Published,"");Peak();continue;
                }
                try
                {
                    int used=job.Step(candidateBudget-work);
                    if(used<0||used>candidateBudget-work){work=candidateBudget;FinishActive(TicketState.Failed,"InvalidWorkAccounting");break;}
                    work+=used;spent+=used;TotalJobCandidates+=used;active.JobCandidates+=used;
                    if(used==0&&job.State==JobState.Running){waiting="JobYieldedWithoutProgress";break;}
                }
                catch(Exception e){work++;FinishActive(TicketState.Failed,"Job:"+e.GetType().Name);}
            }
            TotalWorkUnits+=work;return new PumpResult(work,spent,waiting);
        }
    }
}
