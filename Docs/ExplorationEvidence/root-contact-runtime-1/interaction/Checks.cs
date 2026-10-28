using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using AshenVoxel.Core;
using RootContactPrototype;
using RootContactInteraction;
using InteractionBuilder=RootContactInteraction.Builder;
using InteractionResult=RootContactInteraction.Result;
using InteractionPhase=RootContactInteraction.Phase;
using RootResult=RootContactPrototype.Result;

static class Checks
{
    static int assertions;
    static void Check(bool value,string name){assertions++;if(!value)throw new Exception("FAIL: "+name);}
    sealed class Identities:ICurrentIdentities
    {public string SourceIdentity{get;set;}public string ContextIdentity{get;set;}}
    sealed class ChangesAtPublication:ICurrentIdentities
    {
        readonly string source,context;int calls;
        public ChangesAtPublication(string s,string c){source=s;context=c;}
        public string SourceIdentity=>++calls==1?source:source+"-changed";
        public string ContextIdentity=>context;
    }
    sealed class Recorded:ITerrain
    {
        readonly long[] bare;readonly int side;
        public string SourceIdentity{get;}
        public long MinX{get;}public long MinZ{get;}public long MaxX=>MinX+side-1;public long MaxZ=>MinZ+side-1;
        public Recorded(string path)
        {
            SourceIdentity="recorded-bare:"+Policy.Hash(File.ReadAllBytes(path));using var r=new BinaryReader(File.OpenRead(path));MinX=r.ReadInt64();MinZ=r.ReadInt64();side=r.ReadInt32();
            if(side<1||side>1024)throw new Exception("Terrain fixture cap");bare=new long[side*side];
            for(int i=0;i<bare.Length;i++){r.ReadInt64();bare[i]=r.ReadInt64();r.ReadUInt32();r.ReadUInt32();}
        }
        public bool TryGetBareTop(long x,long z,out double top){top=0;if(x<MinX||x>MaxX||z<MinZ||z>MaxZ)return false;top=(bare[(z-MinZ)*side+x-MinX]+1)*.1;return true;}
    }
    static Template LoadTemplate(string path)
    {
        using var r=new BinaryReader(File.OpenRead(path));int count=r.ReadInt32();if(count<1||count>Policy.MaxColumns)throw new Exception("Template fixture cap");var cols=new Column[count];
        for(int i=0;i<count;i++)
        {
            int x=r.ReadInt16(),z=r.ReadInt16();r.ReadInt16();int n=r.ReadInt32();if(n<1||n>96)throw new Exception("Column fixture cap");var y=new short[n];var t=new uint[n];
            for(int j=0;j<n;j++){y[j]=r.ReadInt16();t[j]=r.ReadUInt32();}cols[i]=new Column(x,z,y,t);
        }
        return new Template(cols,29,"fixture-sha256:"+Policy.Hash(File.ReadAllBytes(path)));
    }
    static WoodCell[] LoadCells(string path,int minY,int maxY)
    {
        using var r=new BinaryReader(File.OpenRead(path));int count=r.ReadInt32();if(count<0||count>10000000||r.BaseStream.Length!=4L+10L*count)throw new Exception("Cell fixture bounds");
        var cells=new List<WoodCell>();for(int i=0;i<count;i++){var p=new Int3(r.ReadInt16(),r.ReadInt16(),r.ReadInt16());uint tint=r.ReadUInt32();if(p.y>=minY&&p.y<=maxY)cells.Add(new WoodCell(p,tint));}return cells.ToArray();
    }
    static RootResult SyntheticRoot(string path,string name)
    {
        WoodCell[] cells=LoadCells(path,-84,30);int x0=cells.Min(c=>c.Position.x),y0=cells.Min(c=>c.Position.y),z0=cells.Min(c=>c.Position.z);
        var origin=new Int3(x0,y0,z0);var world=new VoxelWorld(cells.Max(c=>c.Position.x)-x0+1,cells.Max(c=>c.Position.y)-y0+1,cells.Max(c=>c.Position.z)-z0+1,.1f,714103);var occupied=new List<Int3>();
        foreach(var c in cells){var p=c.Position;world.Set(p.x-x0,p.y-y0,p.z-z0,MaterialId.Wood,c.Color);occupied.Add(p);}
        string hash=Policy.Hash(File.ReadAllBytes(path));return new RootResult(world,occupied,origin,new Pose(0,0,0,0),"synthetic-policy-only:"+hash,0,"synthetic:"+name,hash,29);
    }
    static RootResult ActualRoot(int id,string baseFolder,string visualFolder)
    {
        using var sites=JsonDocument.Parse(File.ReadAllText(Path.Combine(visualFolder,"sites.json")));JsonElement site=sites.RootElement.EnumerateArray().Single(s=>s.GetProperty("id").GetInt32()==id);
        var pose=new Pose(site.GetProperty("x").GetDouble(),site.GetProperty("original_y").GetDouble(),site.GetProperty("z").GetDouble(),site.GetProperty("yaw").GetDouble());
        var b=new RootContactPrototype.Builder(LoadTemplate(Path.Combine(baseFolder,"v2-template-"+id+".bin")),new Recorded(Path.Combine(visualFolder,"terrain-"+id+".bin")),pose);
        while(!b.Terminal)b.Step(new Budget(31,307,1009));Check(b.State==RootContactPrototype.Phase.Complete,"actual bounded root rebuild");
        return b.Published;
    }
    static Identities Current(RootResult root,UpperContext context)=>new Identities{SourceIdentity=root.SourceIdentity,ContextIdentity=context.Identity};
    static (int steps,double cpuMs,double maxStep,long allocated) Finish(InteractionBuilder builder,int budget)
    {
        int steps=0;double maximum=0;long allocated=GC.GetAllocatedBytesForCurrentThread();var total=Stopwatch.StartNew();
        while(!builder.Terminal)
        {
            long before=builder.Candidates,start=Stopwatch.GetTimestamp();builder.Step(budget);maximum=Math.Max(maximum,(Stopwatch.GetTimestamp()-start)*1000d/Stopwatch.Frequency);
            if(builder.Candidates-before>budget)throw new Exception("Exceeded Step budget");if(!builder.Terminal&&builder.Published!=null)throw new Exception("Partial channels escaped");
            if(++steps>10000000)throw new Exception("Unfinished continuation");
        }
        total.Stop();return(steps,total.Elapsed.TotalMilliseconds,maximum,GC.GetAllocatedBytesForCurrentThread()-allocated);
    }
    static string Hash(InteractionResult result)
    {
        using var stream=new MemoryStream();using var w=new BinaryWriter(stream);
        foreach(int i in Enumerable.Range(0,result.RootCellCount).OrderBy(i=>result.Cell(i).x).ThenBy(i=>result.Cell(i).y).ThenBy(i=>result.Cell(i).z))
        {Int3 p=result.Cell(i);w.Write(p.x);w.Write(p.y);w.Write(p.z);w.Write(result.Color(i));w.Write((byte)result.At(i));}
        w.Flush();return Policy.Hash(stream.ToArray());
    }
    static void MatchOracle(InteractionResult result,string path)
    {
        using var r=new BinaryReader(File.OpenRead(path));int n=r.ReadInt32();Check(n==result.RootCellCount,"oracle root cardinality");
        var expected=new Dictionary<Int3,(uint color,Channel channel)>();for(int i=0;i<n;i++){var p=new Int3(r.ReadInt16(),r.ReadInt16(),r.ReadInt16());expected.Add(p,(r.ReadUInt32(),(Channel)r.ReadByte()));}
        int hard=0,stick=0,context=0;
        for(int i=0;i<result.RootCellCount;i++)
        {
            Int3 p=result.Cell(i);var e=expected[p];Channel ch=result.At(i);Check(ch==e.channel,"exact SciPy/leader channel");Check(result.Material(i)==MaterialId.Wood&&result.Color(i)==e.color,"material and RGB preserved");
            if(p.y>=30){context++;Check(ch==Channel.None,"context never emitted collision");}else{Check(ch==Channel.Hard||ch==Channel.Stick,"disjoint exhaustive Wood partition");hard+=ch==Channel.Hard?1:0;stick+=ch==Channel.Stick?1:0;}
        }
        Check(hard==result.HardCount&&stick==result.StickCount&&hard+stick+context==n,"channel count union");
    }
    static void Reject(InteractionBuilder b,string expected)
    {Finish(b,17);Check(b.State==InteractionPhase.Failed&&b.Failure==expected&&b.Published==null,"reject "+expected);}
    static void Throws(Action action,string name){bool thrown=false;try{action();}catch(ArgumentException){thrown=true;}Check(thrown,name);}
    static void Main(string[] args)
    {
        string here=args[0],baseFolder=args[1],visualFolder=args[2];using var manifest=JsonDocument.Parse(File.ReadAllText(Path.Combine(here,"manifest.json")));var results=new List<object>();
        RootResult example=null;UpperContext exampleContext=null;
        foreach(var entry in manifest.RootElement.GetProperty("cases").EnumerateArray())
        {
            string name=entry.GetProperty("name").GetString(),kind=entry.GetProperty("kind").GetString(),rootPath=entry.GetProperty("root_path").GetString(),contextPath=entry.GetProperty("context_path").GetString();
            RootResult root=kind=="actual-bounded-builder"?ActualRoot(entry.GetProperty("id").GetInt32(),baseFolder,visualFolder):SyntheticRoot(rootPath,name);
            var context=new UpperContext(LoadCells(contextPath,29,32),"canonical-file-sha256:"+Policy.Hash(File.ReadAllBytes(contextPath)));
            var b=new InteractionBuilder(root,context,Current(root,context));var cost=Finish(b,997);Check(b.State==InteractionPhase.Complete,"interaction complete "+name+":"+b.Failure);
            MatchOracle(b.Published,entry.GetProperty("oracle_path").GetString());Check(b.Published.SourceIdentity==root.SourceIdentity&&b.Published.ContextIdentity==context.Identity&&b.Published.RootProvenance==root.Provenance,"explicit identity/provenance");
            var large=new InteractionBuilder(root,context,Current(root,context));Finish(large,int.MaxValue);Check(large.State==InteractionPhase.Complete&&Hash(large.Published)==Hash(b.Published)&&large.Published.Provenance==b.Published.Provenance&&large.Candidates==b.Candidates,"stepped vs large identical");
            results.Add(new{name,kind,root_cells=root.OccupiedCount,canonical_context_cells=context.Count,hard=b.Published.HardCount,stick=b.Published.StickCount,core_cells=b.CoreCells,leader_rows=b.LeaderRowsKept,candidates=b.Candidates,morphology_queries=b.MorphologyQueries,leader_point_candidates=b.LeaderPointCandidates,steps=cost.steps,single_pass_cpu_ms=cost.cpuMs,max_step_ms=cost.maxStep,step_allocated_bytes=cost.allocated,source_identity=b.Published.SourceIdentity,context_identity=context.Identity,root_provenance=root.Provenance,provenance=b.Published.Provenance,channels_sha256=Hash(b.Published)});
            if(name=="thin-shoot"){example=root;exampleContext=context;Check(b.Published.HardCount==30&&b.Published.StickCount==9,"thin shoot is not allWood-hard");}
            if(name=="leader-jump4")Check(b.Published.HardCount==3,"existing near4 leader semantics retained");
        }
        foreach(InteractionPhase phase in new[]{InteractionPhase.ValidateRoot,InteractionPhase.ValidateContext,InteractionPhase.Erode,InteractionPhase.Dilate,InteractionPhase.LeaderClosest,InteractionPhase.LeaderLocal,InteractionPhase.LeaderKeep,InteractionPhase.Classify})
        {
            var b=new InteractionBuilder(example,exampleContext,Current(example,exampleContext));int attempts=0;while(b.State!=phase&&!b.Terminal&&++attempts<100000)b.Step(1);Check(b.State==phase,"reach phase "+phase);
            long before=b.Candidates;int pending=b.PendingPhaseItems;b.Step(0);Check(b.State==phase&&b.Candidates==before&&b.PendingPhaseItems==pending,"zero budget retains "+phase);
            b.Cancel();b.Step(int.MaxValue);Check(b.State==InteractionPhase.Cancelled&&b.Published==null,"cancel "+phase);
        }
        var tiny=new InteractionBuilder(example,exampleContext,Current(example,exampleContext));Finish(tiny,1);Check(tiny.State==InteractionPhase.Complete,"budget1 completion");
        foreach(bool changeContext in new[]{false,true})
        {
            var ids=Current(example,exampleContext);var b=new InteractionBuilder(example,exampleContext,ids);b.Step(10);if(changeContext)ids.ContextIdentity+="-changed";else ids.SourceIdentity+="-changed";Reject(b,"StaleIdentity");
        }
        var during=new InteractionBuilder(example,exampleContext,new ChangesAtPublication(example.SourceIdentity,exampleContext.Identity));during.Step(int.MaxValue);Check(during.State==InteractionPhase.Failed&&during.Failure=="StaleIdentity"&&during.Published==null,"final publication revalidates source");
        var altered=Enumerable.Range(0,exampleContext.Count).Select(exampleContext.Cell).ToArray();int pinned=Array.FindIndex(altered,c=>c.Position.y==29);altered[pinned]=new WoodCell(altered[pinned].Position,0xffabcdef);
        var mismatch=new UpperContext(altered,"changed-pigment");Reject(new InteractionBuilder(example,mismatch,Current(example,mismatch)),"PinnedContextMismatch");
        var extra=Enumerable.Range(0,exampleContext.Count).Select(exampleContext.Cell).Concat(new[]{new WoodCell(new Int3(99,29,99),0xff123456)}).ToArray();var extras=new UpperContext(extra,"extra-canonical-cell");Reject(new InteractionBuilder(example,extras,Current(example,extras)),"PinnedContextMismatch");
        var missing=new UpperContext(Enumerable.Range(0,exampleContext.Count).Select(exampleContext.Cell).Where(c=>c.Position.y!=30).ToArray(),"missing-canonical-row");Reject(new InteractionBuilder(example,missing,Current(example,missing)),"PinnedContextMismatch");
        Throws(()=>new UpperContext(new WoodCell[UpperContext.MaxCells+1],"overflow"),"context cell cap");
        Throws(()=>new UpperContext(new[]{new WoodCell(new Int3(0,33,0),0xff123456)},"bad-y"),"context finite row bounds");
        Throws(()=>new UpperContext(new[]{new WoodCell(new Int3(0,29,0),0xff123456),new WoodCell(new Int3(0,29,0),0xff123456)},"duplicate"),"context duplicate rejection");
        var array=new[]{new WoodCell(new Int3(0,29,0),0xff123456)};var immutable=new UpperContext(array,"immutable");array[0]=new WoodCell(new Int3(1,29,1),0xffabcdef);Check(immutable.Cell(0).Position.Equals(new Int3(0,29,0)),"immutable context input cloned");
        File.WriteAllText(Path.Combine(here,"checks.json"),JsonSerializer.Serialize(new{passed=true,assertions,cases=results,gates="exact independent SciPy+leader oracle; real frozen root rebuild; material/RGB preserved; disjoint exhaustive channels; canonical29/30 agreement; context-only excluded; row32 dependency; all-phase budgets/cancellation; source/context staleness including before publication; finite context storage; deterministic large/stepped budgets",timing_scope="Single .NET8 CPU pass includes JIT/GC; count budgets do not establish Unity/frame-time bounds."},new JsonSerializerOptions{WriteIndented=true}));
        Console.WriteLine("PASS "+assertions+" assertions");
    }
}
