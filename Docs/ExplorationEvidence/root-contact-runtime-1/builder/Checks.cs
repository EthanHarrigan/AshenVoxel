using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using AshenVoxel.Core;
using RootContactPrototype;

static class Checks
{
    static int assertions;
    static readonly List<object> failures=new List<object>();
    static void Check(bool good,string label){assertions++;if(!good)throw new Exception("FAIL: "+label);}
    sealed class Field:ITerrain
    {
        public string SourceIdentity {get;set;}="synthetic-v1";
        public long MinX {get;set;}=-10000;public long MaxX {get;set;}=10000;public long MinZ {get;set;}=-10000;public long MaxZ {get;set;}=10000;
        public Func<long,long,double> Height=(x,z)=>0;
        public readonly HashSet<(long,long)> Queried=new HashSet<(long,long)>();
        public bool TryGetBareTop(long x,long z,out double top){Queried.Add((x,z));top=Height(x,z);return true;}
    }
    sealed class Recorded:ITerrain
    {
        readonly long[] bare;readonly int side;
        public string SourceIdentity {get;}
        public long MinX {get;} public long MinZ {get;} public long MaxX=>MinX+side-1;public long MaxZ=>MinZ+side-1;
        public Recorded(string path)
        {
            SourceIdentity="recorded-bare:"+Policy.Hash(File.ReadAllBytes(path));using var r=new BinaryReader(File.OpenRead(path));
            MinX=r.ReadInt64();MinZ=r.ReadInt64();side=r.ReadInt32();if(side<1||side>1024)throw new Exception("Fixture bounds");bare=new long[side*side];
            for(int i=0;i<bare.Length;i++){r.ReadInt64();bare[i]=r.ReadInt64();r.ReadUInt32();r.ReadUInt32();}
            if(r.BaseStream.Position!=r.BaseStream.Length)throw new Exception("Trailing terrain bytes");
        }
        public bool TryGetBareTop(long x,long z,out double top)
        {top=0;if(x<MinX||x>MaxX||z<MinZ||z>MaxZ)return false;top=(bare[(z-MinZ)*side+x-MinX]+1)*.1;return true;}
    }
    static Template LoadTemplate(string path,int seam)
    {
        using var r=new BinaryReader(File.OpenRead(path));int count=r.ReadInt32();Check(count>0&&count<=Policy.MaxColumns,"fixture column cap");var cols=new Column[count];
        for(int i=0;i<count;i++){int x=r.ReadInt16(),z=r.ReadInt16();r.ReadInt16();int n=r.ReadInt32();Check(n>0&&n<=96,"fixture column height");var y=new short[n];var tint=new uint[n];for(int j=0;j<n;j++){y[j]=r.ReadInt16();tint[j]=r.ReadUInt32();}cols[i]=new Column(x,z,y,tint);}
        Check(r.BaseStream.Position==r.BaseStream.Length,"no trailing fixture");return new Template(cols,seam,"fixture-sha256:"+Policy.Hash(File.ReadAllBytes(path)));
    }
    static Dictionary<Int3,uint> LoadCells(string path,int minY=int.MinValue)
    {
        using var r=new BinaryReader(File.OpenRead(path));int n=r.ReadInt32();var result=new Dictionary<Int3,uint>();
        for(int i=0;i<n;i++){var p=new Int3(r.ReadInt16(),r.ReadInt16(),r.ReadInt16());uint tint=r.ReadUInt32();if(p.y>=minY&&p.y<=30)result.Add(p,tint);}
        Check(r.BaseStream.Position==r.BaseStream.Length,"no trailing cells");return result;
    }
    static string GeometryHash(Result result)
    {
        using var stream=new MemoryStream();using var writer=new BinaryWriter(stream);
        var cells=Enumerable.Range(0,result.OccupiedCount).Select(result.Cell).OrderBy(p=>p.x).ThenBy(p=>p.y).ThenBy(p=>p.z);
        foreach(var p in cells){writer.Write(p.x);writer.Write(p.y);writer.Write(p.z);writer.Write(result.Color(p));}
        writer.Flush();return Policy.Hash(stream.ToArray());
    }
    static (int steps,double ms,double maxStep,long allocation) Finish(Builder b,Budget budget)
    {
        int count=0;double max=0;long alloc=GC.GetAllocatedBytesForCurrentThread();var total=Stopwatch.StartNew();
        while(!b.Terminal)
        {
            long q=b.QueryCandidates,c=b.CellCandidates,f=b.FaceCandidates+b.ConnectivityCandidates;long start=Stopwatch.GetTimestamp();b.Step(budget);
            max=Math.Max(max,(Stopwatch.GetTimestamp()-start)*1000d/Stopwatch.Frequency);
            if(b.QueryCandidates-q>budget.Queries||b.CellCandidates-c>budget.Cells||b.FaceCandidates+b.ConnectivityCandidates-f>budget.Faces)throw new Exception("Step exceeded candidate budget");
            if(!b.Terminal&&b.Published!=null)throw new Exception("Partial result escaped");
            if(++count>2000000)throw new Exception("Lost work / no progress");
        }
        total.Stop();return(count,total.Elapsed.TotalMilliseconds,max,GC.GetAllocatedBytesForCurrentThread()-alloc);
    }
    static Column Col(int x,int z,int low=0,int high=30)
    {var y=new short[high-low+1];var t=new uint[y.Length];for(int i=0;i<y.Length;i++){y[i]=(short)(low+i);t[i]=0xff604020u+(uint)i;}return new Column(x,z,y,t);}
    static Template Small(int bottom=0)=>new Template(new[]{Col(0,0,bottom)},29,"synthetic-canonical-v1");
    static void Rejected(Builder b,string reason,Budget budget)
    {
        Finish(b,budget);Check(b.State==Phase.Failed&&b.Failure==reason&&b.Published==null,"reject "+reason+" without publication");
        Check(b.PeakAllocatedChunks<=Policy.MaxChunks&&b.PeakOccupiedCells<=Policy.MaxOccupied,"failure respects allocation caps");
        failures.Add(new{reason=b.Failure,peak_chunks=b.PeakAllocatedChunks,peak_occupied=b.PeakOccupiedCells,query_candidates=b.QueryCandidates,cell_candidates=b.CellCandidates,connectivity_candidates=b.ConnectivityCandidates,connected_cells=b.ConnectedCells,face_candidates=b.FaceCandidates,raw_faces=b.RawFaces});
    }
    static void Throws(Action f,string label){bool thrown=false;try{f();}catch(ArgumentException){thrown=true;}Check(thrown,label);}
    static List<(double x,double z)> Clip(List<(double x,double z)> polygon,Func<(double x,double z),double> distance)
    {
        var next=new List<(double,double)>();for(int i=0;i<polygon.Count;i++)
        {var a=polygon[i];var b=polygon[(i+1)%polygon.Count];double da=distance(a),db=distance(b);if(da>=0)next.Add(a);if((da<0)!=(db<0)){double t=da/(da-db);next.Add((a.x+(b.x-a.x)*t,a.z+(b.z-a.z)*t));}}
        return next;
    }
    // Independent polygon-clipping oracle, not another SAT implementation.
    static HashSet<(long,long)> OverlapOracle(Pose pose,int x,int z)
    {
        double co=Math.Cos(pose.YawDegrees*Math.PI/180),si=Math.Sin(pose.YawDegrees*Math.PI/180);
        var polygon=new List<(double x,double z)>();foreach(var v in new[]{(0,0),(1,0),(1,1),(0,1)})polygon.Add((pose.X*10+co*(x+v.Item1)+si*(z+v.Item2),pose.Z*10-si*(x+v.Item1)+co*(z+v.Item2)));
        var result=new HashSet<(long,long)>();
        for(long gz=(long)Math.Floor(polygon.Min(p=>p.z))-1;gz<=Math.Ceiling(polygon.Max(p=>p.z));gz++)
        for(long gx=(long)Math.Floor(polygon.Min(p=>p.x))-1;gx<=Math.Ceiling(polygon.Max(p=>p.x));gx++)
        {
            var p=Clip(polygon,v=>v.x-gx);p=Clip(p,v=>gx+1-v.x);p=Clip(p,v=>v.z-gz);p=Clip(p,v=>gz+1-v.z);double area=0;
            for(int i=0;i<p.Count;i++){var a=p[i];var b=p[(i+1)%p.Count];area+=a.x*b.z-b.x*a.z;}
            if(Math.Abs(area)*.5>1e-9)result.Add((gx,gz));
        }
        return result;
    }
    static void Main(string[] args)
    {
        string here=args[0],evidence=args[1];var rows=new List<object>();var large=new Budget(1000000,1000000,1000000);var small=new Budget(7,97,101);
        using var sites=JsonDocument.Parse(File.ReadAllText(Path.Combine(evidence,"sites.json")));
        foreach(var site in sites.RootElement.EnumerateArray())foreach(int seam in new[]{30,29})
        {
            int id=site.GetProperty("id").GetInt32();string prefix=seam==30?Path.Combine(evidence,"continuous-shaft"):here;
            string templatePath=Path.Combine(prefix,(seam==30?"cost-":"v2-template-")+id+".bin");string expectedPath=Path.Combine(prefix,(seam==30?"expected-":"v2-expected-")+id+".bin");
            var template=LoadTemplate(templatePath,seam);var terrain=new Recorded(Path.Combine(evidence,"terrain-"+id+".bin"));
            var pose=new Pose(site.GetProperty("x").GetDouble(),site.GetProperty("original_y").GetDouble(),site.GetProperty("z").GetDouble(),site.GetProperty("yaw").GetDouble());
            var b=new Builder(template,terrain,pose);var cost=Finish(b,small);Check(b.State==Phase.Complete,"actual completion "+id+" seam"+seam+":"+b.Failure);
            var expected=LoadCells(expectedPath);var result=b.Published;Check(result.OccupiedCount==expected.Count,"actual exact occupied count");
            Check(result.SourceIdentity==terrain.SourceIdentity&&result.TemplateIdentity==template.Identity&&result.PolicyIdentity==Policy.Identity+";seam="+seam,"explicit source/template/policy provenance");
            foreach(var p in expected){Check(result.NeighborMaterial(p.Key)==MaterialId.Wood&&result.Color(p.Key)==p.Value,"actual exact geometry/RGB");Check(result.OwnsGameplayCell(p.Key)==(p.Key.y<30),"guard excluded gameplay domain");}
            var canonical=LoadCells(Path.Combine(evidence,"wood-"+id+"-baseline.bin"),seam);
            Check(canonical.Count==expected.Keys.Count(p=>p.y>=seam),"pinned canonical cardinality");
            foreach(var p in canonical)Check(result.Color(p.Key)==p.Value,"pinned canonical pigment/cell");
            var one=new Builder(template,terrain,pose);Finish(one,large);Check(one.State==Phase.Complete&&GeometryHash(one.Published)==GeometryHash(result),"step partition deterministic");
            Check(one.Published.Provenance==result.Provenance&&one.SourceQueries==b.SourceQueries&&one.CellCandidates==b.CellCandidates&&one.RawFaces==b.RawFaces,"provenance and counters independent of budgets");
            rows.Add(new{id,policy=seam==29?"runtime-v2-two-row-seam":"visual-v1-compatibility-only",template_identity=template.Identity,source_identity=terrain.SourceIdentity,result_provenance=result.Provenance,geometry_sha256=GeometryHash(result),occupied=result.OccupiedCount,allocated_chunks=result.AllocatedChunks,guard_cells=expected.Keys.Count(p=>p.y==30),raw_unmerged_faces=b.RawFaces,query_candidates=b.QueryCandidates,source_queries=b.SourceQueries,cell_candidates=b.CellCandidates,connectivity_candidates=b.ConnectivityCandidates,connected_cells=b.ConnectedCells,face_candidates=b.FaceCandidates,stepped_calls=cost.steps,measured_cpu_ms=cost.ms,max_step_ms=cost.maxStep,step_allocated_bytes=cost.allocation});
        }
        var flat=Small();var f=new Field();var flatBuilder=new Builder(flat,f,new Pose(0,0,0,0));Finish(flatBuilder,small);
        Check(flatBuilder.State==Phase.Complete&&flatBuilder.Published.OccupiedCount==31,"flat unchanged geometry");
        for(int y=0;y<=30;y++)Check(flatBuilder.Published.Color(new Int3(0,y,0))==flat.GetColumn(0).Tint(y),"flat exact pigment");
        Check(flatBuilder.SourceQueries==1&&flatBuilder.Published.RawFaces==121,"touching edges excluded; guard suppresses top face");
        foreach(double yaw in new[]{0d,37,45,90,180,270,-37})foreach(double offset in new[]{0d,-1.025})
        {
            var pose=new Pose(offset,0,offset-.075,yaw);var field=new Field();var b=new Builder(flat,field,pose);Finish(b,small);
            Check(b.State==Phase.Complete&&field.Queried.SetEquals(OverlapOracle(pose,0,0)),"positive area overlap vs clipping yaw/negative coordinates");
        }
        var cols=new List<Column>();for(int x=-2;x<=2;x++)for(int z=-2;z<=2;z++)cols.Add(Col(x,z));var slopeTemplate=new Template(cols.ToArray(),29,"synthetic-slope-v1");
        var slope=new Field{Height=(x,z)=>Math.Floor(.4*x+.2*z)*.1};var slopePose=new Pose(-.025,0,-.075,37);var sb=new Builder(slopeTemplate,slope,slopePose);Finish(sb,new Budget(1,1,1));
        var sb2=new Builder(slopeTemplate,slope,slopePose);Finish(sb2,large);Check(sb.State==Phase.Complete&&GeometryHash(sb.Published)==GeometryHash(sb2.Published),"diagonal quantized slope; all budgets1 deterministic");
        foreach(var c in cols){double low=OverlapOracle(slopePose,c.X,c.Z).Min(p=>slope.Height(p.Item1,p.Item2));int bottom=Enumerable.Range(0,sb.Published.OccupiedCount).Select(sb.Published.Cell).Where(p=>p.x==c.X&&p.z==c.Z).Min(p=>p.y);Check(bottom*.1<=low+1e-8&&low-bottom*.1<.100001,"full positive-overlap basal gap/quantization");}
        Rejected(new Builder(flat,new Field{Height=(x,z)=>-2.1},new Pose(0,0,0,0)),"DeformationCap",small);
        Rejected(new Builder(flat,new Field{Height=(x,z)=>2.1},new Pose(0,0,0,0)),"DeformationCap",small);
        Rejected(new Builder(flat,new Field{Height=(x,z)=>x==1?3:0},new Pose(0,0,0,45)),"DeformationCap",small);
        var capEdge=new Builder(flat,new Field{Height=(x,z)=>-2},new Pose(0,0,0,0));Finish(capEdge,small);Check(capEdge.State==Phase.Complete,"exact20 downward boundary accepted");
        Rejected(new Builder(Small(10),new Field{Height=(x,z)=>2.6},new Pose(0,0,0,0)),"SeamClearance",small);
        Rejected(new Builder(flat,new Field{Height=(x,z)=>double.NaN},new Pose(0,0,0,0)),"InvalidSourceHeight",small);
        Rejected(new Builder(flat,new Field{Height=(x,z)=>throw new IOException("fixture")},new Pose(0,0,0,0)),"ProviderOrBuildException:IOException",small);
        Rejected(new Builder(flat,new Field{MinX=5},new Pose(0,0,0,0)),"SourceBounds",small);
        foreach(Phase phase in new[]{Phase.Queries,Phase.Cells,Phase.Connectivity,Phase.Faces})
        {
            var cancel=new Builder(flat,new Field(),new Pose(0,0,0,0));while(cancel.State!=phase)cancel.Step(new Budget(1,1,1));cancel.Cancel();cancel.Step(large);
            Check(cancel.State==Phase.Cancelled&&cancel.Published==null,"cancel "+phase+" no partial publication");
        }
        var detached=new Template(new[]{Col(0,0),Col(1,0,0,0)},29,"thin-lobe-adversary");
        Rejected(new Builder(detached,new Field{Height=(x,z)=>x==1?-2:0},new Pose(0,0,0,0)),"DisconnectedBase",small);
        Rejected(new Builder(new Template(new[]{Col(0,0,0,3)},29,"no-anchor"),new Field(),new Pose(0,0,0,0)),"NoSeamAnchor",small);
        var connectivityPause=new Builder(flat,new Field(),new Pose(0,0,0,0));while(connectivityPause.State!=Phase.Connectivity)connectivityPause.Step(new Budget(1,1,0));
        long beforeConnectivity=connectivityPause.ConnectivityCandidates;connectivityPause.Step(new Budget(50,50,0));Check(connectivityPause.State==Phase.Connectivity&&connectivityPause.ConnectivityCandidates==beforeConnectivity,"zero connectivity budget retains pending traversal");
        Finish(connectivityPause,new Budget(1,1,1));Check(connectivityPause.State==Phase.Complete&&connectivityPause.ConnectedCells==connectivityPause.Published.OccupiedCount,"connectivity resumes with budget1");
        var staleField=new Field();var stale=new Builder(flat,staleField,new Pose(0,0,0,0));stale.Step(new Budget(1,3,0));staleField.SourceIdentity="synthetic-v2";Rejected(stale,"StaleSource",small);
        var midField=new Field();var mid=new Builder(flat,midField,new Pose(0,0,0,0));midField.Height=(x,z)=>{midField.SourceIdentity="changed-in-query";return 0;};Rejected(mid,"StaleSource",large);
        var paused=new Builder(flat,new Field(),new Pose(0,0,0,0));paused.Step(new Budget(0,0,0));Check(paused.QueryCandidates==0&&paused.CellCandidates==0&&paused.FaceCandidates==0&&!paused.Terminal,"zero budgets retain all work");Finish(paused,small);Check(paused.State==Phase.Complete,"resume zero budgets");
        var dense=new List<Column>();for(int x=0;x<40;x++)for(int z=0;z<40;z++)dense.Add(Col(x,z));
        Rejected(new Builder(new Template(dense.ToArray(),29,"occupied-overflow"),new Field{Height=(x,z)=>-2},new Pose(0,0,0,0)),"OccupiedCap",small);
        var sparse=new List<Column>();for(int x=-100;x<100;x+=2)for(int z=-100;z<100;z+=2)if(sparse.Count<600)sparse.Add(Col(x,z));
        Rejected(new Builder(new Template(sparse.ToArray(),29,"face-overflow"),new Field(),new Pose(0,0,0,0)),"RawFaceCap",small);
        var scattered=new List<Column>();for(int x=-128;x<128;x+=16)for(int z=-128;z<128;z+=16)scattered.Add(Col(x,z));
        Rejected(new Builder(new Template(scattered.ToArray(),29,"chunk-overflow"),new Field(),new Pose(0,0,0,0)),"ChunkCap",small);
        Throws(()=>new Column(128,0,new short[]{0},new uint[]{0xff112233}),"finite template bounds");
        Throws(()=>new Template(new[]{Col(0,0),Col(0,0)},29,"duplicate"),"duplicate columns rejected");
        Throws(()=>new Template(new[]{Col(0,0,30,30)},29,"missing29"),"v2 guard requires canonical29");
        Throws(()=>new Pose(double.NaN,0,0,0),"nonfinite pose rejected");
        var inputY=new short[]{0,29,30};var inputTint=new uint[]{0xff112233,0xff223344,0xff334455};var column=new Column(0,0,inputY,inputTint);inputY[0]=25;inputTint[0]=0;Check(column.Y(0)==0&&column.Tint(0)==0xff112233,"immutable template arrays cloned");
        var changedPose=new Builder(flat,new Field(),new Pose(.01,0,0,0));Finish(changedPose,large);Check(changedPose.Published.Provenance!=flatBuilder.Published.Provenance,"pose provenance explicit");
        var changedSource=new Builder(flat,new Field{SourceIdentity="another-content"},new Pose(0,0,0,0));Finish(changedSource,large);Check(changedSource.Published.Provenance!=flatBuilder.Published.Provenance,"source provenance explicit");
        var longSource=new Builder(flat,new Field{SourceIdentity=new string('x',780)},new Pose(0,0,0,0));Finish(longSource,small);Check(longSource.State==Phase.Complete,"actual-size world identity accepted");
        Throws(()=>new Builder(flat,new Field{SourceIdentity=new string('x',4097)},new Pose(0,0,0,0)),"identity input bound");
        File.WriteAllText(Path.Combine(here,"checks.json"),JsonSerializer.Serialize(new{assertions,passed=true,actual_cases=rows,rejection_cases=failures,synthetic_gates="flat, exact pigment, SAT vs polygon clipping at7 yaws and negative coordinates, diagonal quantized slope,20cell displacement, mixed-height cliff, seam clearance, detached thin lobe, missing seam anchor, NaN, provider exception, bounds, each-phase cancel including connectivity, source mutation before/duringStep, zero budgets including connectivity, occupied overflow, raw-face overflow, sparse-chunk overflow, finite/duplicate/missing-row metadata, immutability, long identity, provenance",timing_scope="Single .NET8 CPU pass including JIT/GC; budget checks in every step; constructor and shared template validation excluded. Not Unity timings."},new JsonSerializerOptions{WriteIndented=true}));
        Console.WriteLine("PASS "+assertions+" assertions");
    }
}
