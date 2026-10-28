using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using AshenVoxel.Core;
using RootContactPrototype;
using RootContactInteraction;
using RootContactCache;

static class Checks
{
    static int assertions;
    static void Check(bool condition,string name){assertions++;if(!condition)throw new Exception("FAIL: "+name);}
    static Identity Key(string name)=>new Identity("template:"+name,"terrain-v1","root-policy-v1","context-v1","interaction-v1",new Pose(0,0,0,37));
    sealed class FakeJob:IJob
    {
        public Identity Identity{get;}public long ReservedBytes{get;}public JobState State{get;private set;}=JobState.Running;
        public string Phase=>"FakeContinuation";public string Failure{get;private set;}="";public OwnedArtifact Output{get;private set;}
        public int CancelCalls,DisposeCalls;public int ExtraReportedWork{get;set;}public Action StepHook{get;set;}int remaining;readonly bool fail,zero;readonly int cells;
        public FakeJob(Identity key,int steps=3,int cellCount=2,bool failure=false,bool yield=false,long reservation=4096)
        {Identity=key;remaining=steps;cells=cellCount;fail=failure;zero=yield;ReservedBytes=reservation;}
        public int Step(int budget)
        {
            StepHook?.Invoke();if(zero||State!=JobState.Running)return 0;int used=Math.Min(budget,remaining);remaining-=used;
            if(remaining==0)
            {
                if(fail){State=JobState.Failed;Failure="InjectedFailure";return used;}
                var p=new Int3[cells];var c=new uint[cells];var r=new byte[cells];
                for(int i=0;i<cells;i++){p[i]=new Int3(i,0,0);c[i]=0xff123456;r[i]=(byte)Channel.Hard;}
                Output=new OwnedArtifact(Identity,p,c,r,cells,0);State=JobState.Complete;
            }
            return used+ExtraReportedWork;
        }
        public void Cancel(){CancelCalls++;State=JobState.Cancelled;Output=null;}
        public void Dispose(){DisposeCalls++;Output=null;}
    }
    sealed class Recorded:ITerrain
    {
        readonly long[] bare;readonly int side;
        public string SourceIdentity{get;set;}public long MinX{get;}public long MinZ{get;}public long MaxX=>MinX+side-1;public long MaxZ=>MinZ+side-1;
        public Recorded(string path)
        {
            SourceIdentity="recorded-bare:"+Policy.Hash(File.ReadAllBytes(path));using var r=new BinaryReader(File.OpenRead(path));MinX=r.ReadInt64();MinZ=r.ReadInt64();side=r.ReadInt32();bare=new long[side*side];
            for(int i=0;i<bare.Length;i++){r.ReadInt64();bare[i]=r.ReadInt64();r.ReadUInt32();r.ReadUInt32();}
        }
        public bool TryGetBareTop(long x,long z,out double top){top=0;if(x<MinX||x>MaxX||z<MinZ||z>MaxZ)return false;top=(bare[(z-MinZ)*side+x-MinX]+1)*.1;return true;}
    }
    sealed class Current:ICurrentIdentities
    {public Recorded Terrain;public UpperContext Context;public string ContextRevision{get;set;}public string SourceIdentity=>Terrain.SourceIdentity;public string ContextIdentity=>ContextRevision??Context.Identity;}
    static Template Template(string path)
    {
        using var r=new BinaryReader(File.OpenRead(path));int n=r.ReadInt32();var columns=new Column[n];
        for(int i=0;i<n;i++){int x=r.ReadInt16(),z=r.ReadInt16();r.ReadInt16();int count=r.ReadInt32();var y=new short[count];var t=new uint[count];for(int j=0;j<count;j++){y[j]=r.ReadInt16();t[j]=r.ReadUInt32();}columns[i]=new Column(x,z,y,t);}
        return new Template(columns,29,"fixture-sha256:"+Policy.Hash(File.ReadAllBytes(path)));
    }
    static UpperContext Context(string path)
    {
        using var r=new BinaryReader(File.OpenRead(path));int n=r.ReadInt32();var cells=new List<WoodCell>();
        for(int i=0;i<n;i++){var p=new Int3(r.ReadInt16(),r.ReadInt16(),r.ReadInt16());uint color=r.ReadUInt32();if(p.y>=29&&p.y<=32)cells.Add(new WoodCell(p,color));}
        return new UpperContext(cells.ToArray(),"canonical-file-sha256:"+Policy.Hash(File.ReadAllBytes(path)));
    }
    static Ticket Request(Controller c,Identity key,Func<IJob> factory,Func<bool> current=null,long reservation=4096)
    {Check(c.Request(key,reservation,factory,current??(()=>true),out Ticket t)==Admission.Queued,"request queued");return t;}
    static PumpResult Pump(Controller c,int budget)
    {
        PumpResult r=c.Pump(budget);Check(r.WorkUnits<=budget&&r.JobCandidates<=r.WorkUnits,"bounded Pump accounting");return r;
    }
    static void Complete(Controller c,Ticket ticket,int budget=11)
    {
        int turns=0;while(ticket.State==TicketState.Queued||ticket.State==TicketState.Running||ticket.State==TicketState.WaitingPublication)
        {Pump(c,budget);if(++turns>1000000)throw new Exception("Unfinished ticket");}
    }
    static void AssertOracle(Lease lease,string path)
    {
        using var r=new BinaryReader(File.OpenRead(path));int n=r.ReadInt32();var expected=new Dictionary<Int3,(uint color,Channel role)>();
        for(int i=0;i<n;i++){var p=new Int3(r.ReadInt16(),r.ReadInt16(),r.ReadInt16());expected.Add(p,(r.ReadUInt32(),(Channel)r.ReadByte()));}
        Check(n==lease.CellCount,"complete cell array count");int render=0,guard=0;
        for(int i=0;i<n;i++){Int3 p=lease.Position(i);var e=expected[p];Check(lease.Color(i)==e.color&&lease.Role(i)==e.role&&lease.Material(i)==MaterialId.Wood,"atomic geometry RGB material role match");if(p.y<30){render++;Check(lease.Role(i)!=Channel.None,"render cell has interaction role");}else{guard++;Check(lease.Role(i)==Channel.None,"context never emitted collision");}}
        Check(render==lease.RenderCellCount&&guard==lease.ContextCellCount,"emission/context counts");
    }
    static void Throws(Action action,string label){bool threw=false;try{action();}catch(InvalidOperationException){threw=true;}Check(threw,label);}
    static void Main(string[] args)
    {
        string here=args[0],geometry=args[1],interaction=args[2],visual=args[3];var actualRows=new List<object>();
        var a=Key("a");var b=Key("b");var d=Key("d");var e=Key("e");
        Check(Key("a").Hash==a.Hash,"identity deterministic");
        var variation=new[]{new Identity(a.Template,"terrain-v2",a.RootPolicy,a.Context,a.InteractionPolicy,a.Pose),new Identity(a.Template,a.Terrain,a.RootPolicy,"context-v2",a.InteractionPolicy,a.Pose),new Identity(a.Template,a.Terrain,"root-policy-v2",a.Context,a.InteractionPolicy,a.Pose),new Identity(a.Template,a.Terrain,a.RootPolicy,a.Context,"interaction-v2",a.Pose),new Identity(a.Template,a.Terrain,a.RootPolicy,a.Context,a.InteractionPolicy,new Pose(.1,0,0,37))};
        Check(variation.All(v=>v.Hash!=a.Hash)&&variation.Select(v=>v.Hash).Distinct().Count()==5,"all provenance inputs separate identity");
        var queue=new Controller(new Limits(pending:2,readyBytes:8192,workerBytes:4096,residentBytes:12288));int factories=0;FakeJob active=null;
        Ticket qa=Request(queue,a,()=>{factories++;return active=new FakeJob(a);});Ticket qb=Request(queue,b,()=>new FakeJob(b));
        Check(queue.Request(d,4096,()=>new FakeJob(d),()=>true,out _)==Admission.QueueFull&&queue.PendingCount==2,"explicit queue-full preserves work");
        Check(queue.Request(a,4096,()=>throw new Exception(),()=>true,out Ticket duplicate)==Admission.ExistingPending&&ReferenceEquals(qa,duplicate),"deduplicate pending identity");
        Pump(queue,0);Check(factories==0&&queue.QueuedCount==2,"zero budget creates no worker");
        Pump(queue,1);Check(factories==1&&queue.WorkerPhase=="FakeContinuation"&&!queue.TryAcquire(a,out _),"factory charged; no partial publication");
        Pump(queue,1);Check(!queue.TryAcquire(a,out _),"partial worker not cached");
        Check(queue.Cancel(qb)&&qb.State==TicketState.Cancelled&&queue.PendingCount==1,"queued cancel explicit terminal");
        Ticket qd=Request(queue,d,()=>new FakeJob(d));Check(queue.QueuedIdentity(0).Hash==d.Hash,"retry admission retains FIFO");
        Check(queue.Cancel(qa)&&qa.State==TicketState.Cancelled&&active.CancelCalls==1&&active.DisposeCalls==1,"active cancellation forwarded once");
        Complete(queue,qd);Check(qd.State==TicketState.Published&&queue.ReadyCount==1,"remaining queued work completes");
        var lru=new Controller(new Limits(readyEntries:2,readyBytes:8192,workerBytes:4096,residentBytes:12288));
        Complete(lru,Request(lru,a,()=>new FakeJob(a)));Complete(lru,Request(lru,b,()=>new FakeJob(b)));Check(lru.TryAcquire(a,out Lease touch),"touch A");touch.Dispose();
        Complete(lru,Request(lru,d,()=>new FakeJob(d)));bool acquiredA=lru.TryAcquire(a,out Lease survived);Check(!lru.TryAcquire(b,out _)&&acquiredA&&lru.Evictions==1,"deterministic LRU evicts B");survived.Dispose();
        Check(lru.Request(a,4096,()=>throw new Exception(),()=>true,out _)==Admission.Cached,"cache hit avoids factory");
        Complete(lru,Request(lru,b,()=>new FakeJob(b)));Check(lru.TryAcquire(b,out Lease retry),"evicted identity can rebuild");retry.Dispose();
        var pin=new Controller(new Limits(pending:3,readyEntries:1,leases:1,readyBytes:4096,workerBytes:4096,residentBytes:8192));
        Complete(pin,Request(pin,a,()=>new FakeJob(a)));Check(pin.TryAcquire(a,out Lease held)&&!pin.TryAcquire(a,out _),"lease limit and pin");
        Ticket pb=Request(pin,b,()=>new FakeJob(b));PumpResult blocked=Pump(pin,100);Check(pb.State==TicketState.WaitingPublication&&blocked.WaitingReason=="PinnedPublicationCapacity"&&!pin.TryAcquire(b,out _),"completed job retained while cache pinned");
        Ticket pd=Request(pin,d,()=>new FakeJob(d));Check(pin.PendingCount==2&&pin.QueuedCount==1,"blocked publication retains queued work");long before=pin.TotalWorkUnits;Pump(pin,0);Check(pin.TotalWorkUnits==before&&pb.State==TicketState.WaitingPublication,"zero budget retains completed worker");
        held.Dispose();Throws(()=>{int ignored=held.CellCount;},"released lease cannot retain payload access");Pump(pin,1);Check(pb.State==TicketState.Published&&pd.State==TicketState.Queued,"release unblocks atomic publication only");Complete(pin,pd);
        var resident=new Controller(new Limits(readyEntries:1,readyBytes:4096,workerBytes:4096,residentBytes:4096));Complete(resident,Request(resident,a,()=>new FakeJob(a)));resident.TryAcquire(a,out Lease pinned);
        Ticket rb=Request(resident,b,()=>new FakeJob(b));Check(Pump(resident,1).WaitingReason=="PinnedResidentCapacity"&&rb.State==TicketState.Queued,"worker admission respects resident accounting");pinned.Dispose();Complete(resident,rb);
        var failing=new Controller(new Limits(readyBytes:4096,workerBytes:4096,residentBytes:8192));Ticket fail=Request(failing,a,()=>new FakeJob(a,failure:true));Complete(failing,fail);Check(fail.State==TicketState.Failed&&fail.Reason=="InjectedFailure"&&!failing.TryAcquire(a,out _),"failed job publishes nothing");
        Complete(failing,Request(failing,a,()=>new FakeJob(a)));Ticket factoryFail=Request(failing,b,()=>throw new IOException());Complete(failing,factoryFail);Check(factoryFail.State==TicketState.Failed&&factoryFail.Reason=="Factory:IOException","factory failure is observable");
        Ticket oversized=Request(failing,d,()=>new FakeJob(d,cellCount:300));Complete(failing,oversized);Check(oversized.State==TicketState.Failed&&oversized.Reason=="InvalidOrOversizedArtifact","artifact byte cap");
        Check(failing.Request(e,4097,()=>new FakeJob(e),()=>true,out _)==Admission.ReservationTooLarge,"worker reservation cap");
        FakeJob yielding=null;Ticket yieldTicket=Request(failing,e,()=>yielding=new FakeJob(e,yield:true));Check(Pump(failing,10).WaitingReason=="JobYieldedWithoutProgress"&&yieldTicket.State==TicketState.Running,"zero-progress job stays observable");failing.Cancel(yieldTicket);Check(yielding.CancelCalls==1,"yielding job cancel");
        bool valid=true;var stale=new Controller(new Limits(readyBytes:4096,workerBytes:4096,residentBytes:8192));Complete(stale,Request(stale,a,()=>new FakeJob(a),()=>valid));stale.TryAcquire(a,out Lease old);
        Ticket pendingStale=Request(stale,b,()=>new FakeJob(b),()=>valid);valid=false;stale.InvalidateStale();Check(pendingStale.State==TicketState.Stale&&stale.ReadyCount==1&&!stale.TryAcquire(a,out _),"stale queued and pinned ready handled");Throws(()=>{int ignored=old.CellCount;},"old lease rejects changed source");old.Dispose();Check(stale.ReadyCount==0&&stale.ReadyAccountedBytes==0,"retired pinned bytes released");
        var waitCancel=new Controller(new Limits(readyEntries:1,readyBytes:4096,workerBytes:4096,residentBytes:8192));Complete(waitCancel,Request(waitCancel,a,()=>new FakeJob(a)));waitCancel.TryAcquire(a,out Lease anchor);FakeJob completeJob=null;
        Ticket waiting=Request(waitCancel,b,()=>completeJob=new FakeJob(b));Pump(waitCancel,100);Check(waiting.State==TicketState.WaitingPublication&&waitCancel.Cancel(waiting)&&completeJob.CancelCalls==1&&!waitCancel.TryAcquire(b,out _),"cancel completed unpublished result");anchor.Dispose();
        var publicationRace=new Controller(new Limits(readyBytes:4096,workerBytes:4096,residentBytes:8192));FakeJob raceJob=null;int finalChecks=0;
        Ticket race=Request(publicationRace,a,()=>raceJob=new FakeJob(a),()=>raceJob==null||raceJob.State!=JobState.Complete||++finalChecks<2);Complete(publicationRace,race,100);
        Check(race.State==TicketState.Stale&&race.Reason=="StaleBeforePublication"&&!publicationRace.TryAcquire(a,out _),"source revalidated immediately before atomic publication");
        var reentrant=new Controller(new Limits(readyBytes:4096,workerBytes:4096,residentBytes:8192));Ticket reentrantTicket=null;
        reentrantTicket=Request(reentrant,a,()=>new FakeJob(a){StepHook=()=>reentrant.Cancel(reentrantTicket)});Complete(reentrant,reentrantTicket);
        Check(reentrantTicket.State==TicketState.Failed&&reentrantTicket.Reason=="Job:InvalidOperationException"&&reentrant.PendingCount==0,"reentrant producer cannot corrupt controller state");
        var contracts=new Controller(new Limits(readyBytes:4096,workerBytes:4096,residentBytes:8192));Ticket wrongIdentity=Request(contracts,a,()=>new FakeJob(b));Complete(contracts,wrongIdentity);Check(wrongIdentity.State==TicketState.Failed&&wrongIdentity.Reason=="InvalidJobContract","factory identity mismatch rejected");
        Ticket wrongWork=Request(contracts,a,()=>new FakeJob(a){ExtraReportedWork=1});Pump(contracts,1);Pump(contracts,1);Check(wrongWork.State==TicketState.Failed&&wrongWork.Reason=="InvalidWorkAccounting"&&!contracts.TryAcquire(a,out _),"over-budget callback rejected");
        bool boundsRejected=false;try{new Limits(readyBytes:long.MaxValue,workerBytes:long.MaxValue,residentBytes:long.MaxValue);}catch(ArgumentException){boundsRejected=true;}Check(boundsRejected,"byte configuration bounded against overflow");
        using var sites=JsonDocument.Parse(File.ReadAllText(Path.Combine(visual,"sites.json")));var actual=new Controller(new Limits());
        foreach(var site in sites.RootElement.EnumerateArray())
        {
            int id=site.GetProperty("id").GetInt32();Template template=Template(Path.Combine(geometry,"v2-template-"+id+".bin"));var terrain=new Recorded(Path.Combine(visual,"terrain-"+id+".bin"));UpperContext context=Context(Path.Combine(visual,"wood-"+id+"-baseline.bin"));
            var current=new Current{Terrain=terrain,Context=context};var pose=new Pose(site.GetProperty("x").GetDouble(),site.GetProperty("original_y").GetDouble(),site.GetProperty("z").GetDouble(),site.GetProperty("yaw").GetDouble());Identity key=Identity.For(template,terrain,context,pose);FrozenJob job=null;
            Func<bool> isCurrent=()=>terrain.SourceIdentity==key.Terrain&&current.ContextIdentity==key.Context;
            Ticket ticket=Request(actual,key,()=>job=new FrozenJob(template,terrain,context,current,pose),isCurrent,FrozenJob.WorkingReservation);
            int turns=0;while(actual.WorkerPhase!="PackingCellsAndRoles"&&ticket.State!=TicketState.Published){Pump(actual,4096);if(++turns>10000)throw new Exception("No packing phase");}
            Check(job.CopiedCells>0&&job.CopiedCells<Policy.MaxOccupied&&!actual.TryAcquire(key,out _),"actual partial cell-role arrays never visible");Complete(actual,ticket,4096);Check(ticket.State==TicketState.Published,"actual job published");
            Check(actual.TryAcquire(key,out Lease lease),"actual atomic acquire");AssertOracle(lease,Path.Combine(interaction,"actual-"+id+"-oracle.bin"));
            actualRows.Add(new{id,key=key.Hash,cells=lease.CellCount,render_cells=lease.RenderCellCount,context_cells=lease.ContextCellCount,job_candidates=ticket.JobCandidates,cache_ready_accounted_bytes=actual.ReadyAccountedBytes});lease.Dispose();
            if(id==10)
            {
                // Exercise actual frozen component cancellation and its independent source check.
                var cancellation=new Controller(new Limits());FrozenJob cancelledJob=null;Ticket cancelled=Request(cancellation,key,()=>cancelledJob=new FrozenJob(template,terrain,context,current,pose),isCurrent,FrozenJob.WorkingReservation);Pump(cancellation,20);Check(cancellation.Cancel(cancelled)&&cancelledJob.State==JobState.Cancelled&&!cancellation.TryAcquire(key,out _),"actual builder cancellation");
                var revisionTerrain=new Recorded(Path.Combine(visual,"terrain-10.bin"));var revisionCurrent=new Current{Terrain=revisionTerrain,Context=context};
                var sourceGate=new Controller(new Limits());FrozenJob changedJob=null;Ticket changed=Request(sourceGate,key,()=>changedJob=new FrozenJob(template,revisionTerrain,context,revisionCurrent,pose),()=>true,FrozenJob.WorkingReservation);Pump(sourceGate,20);revisionTerrain.SourceIdentity+=";revision2";Complete(sourceGate,changed,4096);Check(changed.State==TicketState.Failed&&changed.Reason=="Root:StaleSource"&&!sourceGate.TryAcquire(key,out _),"actual root rejects source change even if coordinator predicate misses it");
                Identity revised=Identity.For(template,revisionTerrain,context,pose);Check(revised.Hash!=key.Hash,"actual revision changes key");
                Ticket renewed=Request(sourceGate,revised,()=>new FrozenJob(template,revisionTerrain,context,revisionCurrent,pose),()=>revisionTerrain.SourceIdentity==revised.Terrain,FrozenJob.WorkingReservation);Complete(sourceGate,renewed,4096);bool acquiredRevision=sourceGate.TryAcquire(revised,out Lease revisedLease);Check(renewed.State==TicketState.Published&&acquiredRevision,"actual revised source retry succeeds");AssertOracle(revisedLease,Path.Combine(interaction,"actual-10-oracle.bin"));revisedLease.Dispose();
            }
        }
        Check(actual.ReadyCount==2&&actual.PeakResidentAccountedBytes>FrozenJob.WorkingReservation&&actual.PeakResidentAccountedBytes<=26L*1024*1024&&actual.ReadyAccountedBytes<=2L*1024*1024,"two actual cached entries coexist within resident accounting");
        File.WriteAllText(Path.Combine(here,"checks.json"),JsonSerializer.Serialize(new{passed=true,assertions,actual_cases=actualRows,actual_peak_resident_accounted_bytes=actual.PeakResidentAccountedBytes,actual_total_job_candidates=actual.TotalJobCandidates,gates="identity determinism and revisions; FIFO/dedup/queue-full retry; zero-budget; partial real packaging; bounded work; LRU eviction/retry; leases and publication pinning; resident admission; cancellation queued/running/waiting; actual builder cancellation; failure/factory/oversize; yielding work retained; stale queue/cache/lease; independent actual source gate; actual revised rebuild; exact cell/RGB/roles vs prior frozen oracle"},new JsonSerializerOptions{WriteIndented=true}));
        Console.WriteLine("PASS "+assertions+" assertions");
    }
}
