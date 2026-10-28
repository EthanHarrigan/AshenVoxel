using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using AshenVoxel.Core;
using RootContactManagedComposite;
using RootContactArrayMesher;

static class Checks
{
    static long assertions;static string stage="start";
    static void Check(bool b,string why){assertions++;if(!b)throw new Exception(stage+": "+why);}
    static readonly int[] factors={1,2,4,16,64},cuts={30,32,36,60,124};
    static GridTransform Transform(int seed)=>new GridTransform(new Int3(235,4,235),1024,470,470,seed);
    static uint Pack(Rgba32 c)=>(uint)c.r|((uint)c.g<<8)|((uint)c.b<<16)|((uint)c.a<<24;
    static FixedSample[] ReadSamples(string path,out int factor,out int cut,out int seed)
    {using var r=new BinaryReader(File.OpenRead(path));Check(r.ReadInt32()==0x314c4352,"fixed magic");factor=r.ReadInt32();cut=r.ReadInt32();seed=r.ReadInt32();int n=r.ReadInt32();Check(n>=0&&n<=65536,"fixed cap");var a=new FixedSample[n];for(int i=0;i<n;i++)a[i]=new FixedSample(new Int3(r.ReadInt16(),r.ReadInt16(),r.ReadInt16()),(MaterialId)r.ReadByte(),r.ReadUInt32(),r.ReadInt32(),r.ReadUInt16());Check(r.BaseStream.Position==r.BaseStream.Length,"fixed exact length");return a;}
    static LodTemplate[] Load(string dir,Limits limits)
    {
        Int3[] ordered;using(var r=new BinaryReader(File.OpenRead(Path.Combine(dir,"canonical-order.bin")))){int n=r.ReadInt32();Check(n>=0&&n<=8192,"rank cap");ordered=new Int3[n];for(int i=0;i<n;i++){ordered[i]=new Int3(r.ReadInt16(),r.ReadInt16(),r.ReadInt16());Check(r.ReadInt32()==i,"rank visitation order");}Check(r.BaseStream.Position==r.BaseStream.Length,"rank exact length");}
        var samples=new FixedSample[5][];int seed=0;for(int i=0;i<5;i++){samples[i]=ReadSamples(Path.Combine(dir,"fixed-F"+factors[i]+".bin"),out int f,out int cut,out seed);Check(f==factors[i]&&cut==cuts[i],"ordered LOD cuts");}
        var pins=samples[0].Where(s=>s.Cell.y==33&&s.Material==MaterialId.Wood).Select(s=>new RootCell(new Int3(s.Cell.x-235,29,s.Cell.z-235),s.Color)).ToArray();
        var source=new CanonicalSource(ordered,pins,Transform(seed),"sha-bound-canonical-fixture:"+seed,limits,()=>true);var result=new LodTemplate[5];for(int i=0;i<5;i++)result[i]=new LodTemplate(source,factors[i],cuts[i],samples[i],"sha-bound-fixed-fixture:"+seed+":"+factors[i],limits,()=>true);
        var before=result[0].At(0);samples[0][0]=default;ordered[0]=new Int3(-999,-999,-999);if(pins.Length>0)pins[0]=default;Check(result[0].At(0).Color==before.Color&&result[0].At(0).Cell.Equals(before.Cell),"source arrays privately owned");return result;
    }
    static RootCell[] ReadRoot(string path)
    {using var r=new BinaryReader(File.OpenRead(path));int n=r.ReadInt32();Check(n>0&&n<=65536,"root fixture cap");var a=new List<RootCell>();for(int i=0;i<n;i++){var c=new RootCell(new Int3(r.ReadInt16(),r.ReadInt16(),r.ReadInt16()),r.ReadUInt32());Check(c.Position.y<=30,"fixture guard bound");if(c.Position.y<=29)a.Add(c);}Check(r.BaseStream.Position==r.BaseStream.Length,"root exact length");return a.ToArray();}
    static void Finish(CompositeJob j,int records,int faces,int chunks)
    {int iterations=0;while(!j.Terminal){Check(j.ActiveCoarseWorlds<=1,"only one active coarse world");j.Step(records,faces,chunks);Check(++iterations<1000000,"bounded harness continuation");}}
    static string MeshFile(string path,LodOutput output)
    {
        using(var w=new BinaryWriter(File.Create(path)))
        {
            w.Write(0x314d4352);w.Write(output.Count);
            var parts=Enumerable.Range(0,output.Count).Select(output.At).OrderBy(p=>p.Coordinate.z).ThenBy(p=>p.Coordinate.y).ThenBy(p=>p.Coordinate.x);
            foreach(var part in parts)
            {
                var m=part.Data;w.Write(part.Coordinate.x);w.Write(part.Coordinate.y);w.Write(part.Coordinate.z);w.Write(m.VertexCount);w.Write(m.IndexCount);
                Check(m.PayloadBytes==m.RetainedBufferBytes,"exact-length compact arrays retained");
                for(int i=0;i<m.VertexCount;i++)
                {
                    for(int a=0;a<3;a++){int grid=(int)Math.Round(m.Vertices[i][a]/(.1f*output.Factor));int anchor=a==0?output.Transform.Anchor.x:a==1?output.Transform.Anchor.y+output.Transform.FineYShift:output.Transform.Anchor.z;w.Write(checked((short)(grid*output.Factor-anchor)));}
                    var n=m.Normals[i];w.Write((byte)(n.x>.5?0:n.x<-.5?1:n.y>.5?2:n.y<-.5?3:n.z>.5?4:5));w.Write(Pack(m.Colors[i]));
                }
                foreach(int index in m.Indices)w.Write(index);
            }
        }
        using var stream=File.OpenRead(path);using var h=SHA256.Create();return Convert.ToHexString(h.ComputeHash(stream)).ToLowerInvariant();
    }
    static object Actual(int id,string dir,string fixture,string output)
    {
        stage="actual "+id;var limits=new Limits();var templates=Load(dir,limits);var root=ReadRoot(fixture);var sourceCopy=(RootCell[])root.Clone();var templateCopy=(LodTemplate[])templates.Clone();var clock=Stopwatch.StartNew();var job=new CompositeJob(templateCopy,sourceCopy,"complete-fixture-identity:"+id,limits,()=>true);
        sourceCopy[0]=default;templateCopy[0]=templates[1];job.Step(0,0,0);Check(job.FixedCandidates==0&&job.RootCandidates==0&&job.Published==null,"zero budgets preserve unpublished work");Finish(job,37,53,1);Check(job.State==Phase.Complete&&job.Published!=null&&job.ActiveCoarseWorlds==0,"complete all five: "+job.Failure);
        var first=job.Published;using var prior=JsonDocument.Parse(File.ReadAllText(Path.Combine(dir,"result.json")));var rows=new List<object>();
        for(int i=0;i<5;i++){var lod=first.At(i);string actual=MeshFile(Path.Combine(output,$"site-{id}-F{lod.Factor}.bin"),lod);string expected=prior.RootElement.GetProperty("lods")[i].GetProperty("lower_mesh_sha256").GetString();Check(actual==expected,"all exact recorded lower geometry/attributes/indices");rows.Add(new{factor=lod.Factor,cut=lod.Cut,chunks=lod.Count,retained_mesh_bytes=lod.RetainedMeshBytes,sha256=actual});}
        var second=new CompositeJob(templates,root.Reverse().ToArray(),"complete-fixture-identity:"+id,limits,()=>true);Finish(second,int.MaxValue,int.MaxValue,1);Check(second.State==Phase.Complete,"large-budget reverse root completes");for(int i=0;i<5;i++)Check(MeshFile(Path.Combine(output,$"site-{id}-reverse-F{factors[i]}.bin"),second.Published.At(i))==MeshFile(Path.Combine(output,$"site-{id}-ownership-F{factors[i]}.bin"),first.At(i)),"budget/order and prior output ownership exact");
        var changed=(RootCell[])root.Clone();int pin=Array.FindIndex(changed,r=>r.Position.y==29);Check(pin>=0,"actual pin present");changed[pin]=new RootCell(changed[pin].Position,changed[pin].Color^1);var bad=new CompositeJob(templates,changed,"bad-pin",limits,()=>true);Finish(bad,int.MaxValue,int.MaxValue,1);Check(bad.State==Phase.Rejected&&bad.Failure=="ChangedPinnedRow"&&bad.Published==null,"changed canonical pin rejected");
        var missing=new CompositeJob(templates,root.Where((c,i)=>i!=pin).ToArray(),"missing-pin",limits,()=>true);Finish(missing,int.MaxValue,int.MaxValue,1);Check(missing.State==Phase.Rejected&&missing.Failure=="MissingPinnedRow","incomplete pinned row rejected");
        return new{id,seconds=clock.Elapsed.TotalSeconds,retained_mesh_bytes=first.RetainedMeshBytes,declared_output_element_bytes=first.DeclaredOutputElementBytes,template_element_bytes=job.TemplateElementBytes,root_element_bytes=job.RootElementBytes,peak_declared_scratch_bytes=job.PeakDeclaredScratchBytes,peak_declared_total_bytes=job.PeakDeclaredTotalBytes,fixed_candidates=job.FixedCandidates,root_candidates=job.RootCandidates,face_candidates=job.FaceCandidates,halo_cells_read=job.HaloCellsRead,mesh_chunk_calls=job.MeshChunkCalls,lods=rows};
    }
    static LodTemplate[] Tiny(Limits limits=null)
    {
        limits=limits??new Limits();var source=new CanonicalSource(new[]{new Int3(14,2,14),new Int3(16,2,14)},Array.Empty<RootCell>(),Transform(11),"tiny-source",limits,()=>true);var a=new LodTemplate[5];
        for(int i=0;i<5;i++){int f=factors[i];a[i]=new LodTemplate(source,f,cuts[i],new[]{new FixedSample(new Int3(225/f,32/f,225/f),MaterialId.Stone,0xff778899,0,257),new FixedSample(new Int3(257/f,32/f,225/f),MaterialId.Wood,0xff556677,1,257)},"tiny-"+f,limits,()=>true);}return a;
    }
    static void Synthetic()
    {
        stage="synthetic";var limits=new Limits();var templates=Tiny();var root=Array.Empty<RootCell>();
        var small=new CompositeJob(templates,root,"small",limits,()=>true);small.Step(1,0,0);Check(small.RemainingFixed==1&&small.Published==null,"fixed queue retained");small.Step(1,1,0);Check(small.RemainingFaces==11,"face queue retained");Finish(small,1,1,1);Check(small.State==Phase.Complete,"tiny single budgets finish");
        foreach(bool cancel in new[]{false,true})foreach(int threshold in new[]{1,4,6,20,108,109,110,111,112,113,114})
        {
            CompositeJob job=null;using var cts=new CancellationTokenSource();int calls=0;Func<bool> predicate=()=>{if(job!=null&&job.State==Phase.Mesh&&++calls>=threshold){if(cancel){cts.Cancel();return true;}return false;}return true;};
            job=new CompositeJob(templates,root,"checkpoint",limits,predicate,cts.Token);Finish(job,1000,1000,1);Check(job.State==(cancel?Phase.Cancelled:Phase.Stale)&&job.Published==null&&job.RetainedMeshBytes==0&&job.ActiveCoarseWorlds==0,"stale/cancel across halo/mesh/compaction/handoff clears partial state");
        }
        foreach(int point in new[]{0,1,2})
        {
            CompositeJob j=null;Func<bool> current=()=>{if(j==null)return true;if(point==0)return j.HaloCellsRead==0;if(point==1)return j.CompletedLods<1;return j.Published==null;};
            j=new CompositeJob(templates,root,"publication",limits,current);Finish(j,1000,1000,1);Check(j.State==Phase.Stale&&j.Published==null&&j.RetainedMeshBytes==0&&j.ActiveCoarseWorlds==0,"acquisition/LOD/final publication stale guard");
        }
        bool explode=false;var exceptionJob=new CompositeJob(templates,root,"throwing",limits,()=>explode?throw new Exception():true);explode=true;exceptionJob.Step(1,1,1);Check(exceptionJob.State==Phase.Failed&&exceptionJob.Failure=="CurrentPredicate"&&exceptionJob.Published==null,"predicate exception fails closed");
        var cancelled=new CompositeJob(templates,root,"cancel",limits,()=>true);cancelled.Step(1000,1000,1);cancelled.Cancel();Check(cancelled.State==Phase.Cancelled&&cancelled.Published==null&&cancelled.ActiveCoarseWorlds==0,"explicit cancel releases active cache");
        var noOutput=new CompositeJob(templates,root,"output-cap",new Limits(maxOutputBytes:0),()=>true);Finish(noOutput,1000,1000,1);Check(noOutput.State==Phase.Rejected&&noOutput.Failure=="ByteCapacity"&&noOutput.Published==null,"aggregate output cap");
        var noChunk=new CompositeJob(templates,root,"scratch-cap",new Limits(maxScratchBytes:6000),()=>true);Finish(noChunk,1000,1000,1);Check(noChunk.State==Phase.Rejected&&noChunk.Failure=="ByteCapacity","scratch reservation rejected before coarse chunk allocation");
        bool failed=false;try{new CompositeJob(templates,new RootCell[65537],"count",limits,()=>true);}catch(ArgumentException){failed=true;}Check(failed,"root count before clone");
        foreach(var cap in new[]{new Limits(maxRootBytes:0),new Limits(maxTemplateBytes:0),new Limits(maxTotalBytes:0)}){failed=false;try{new CompositeJob(templates,root,"limits",cap,()=>true);}catch(ArgumentException){failed=true;}Check(failed,"constructor byte admission");}
        var changed=(LodTemplate[])templates.Clone();changed[0]=templates[1];failed=false;try{new CompositeJob(changed,root,"template-change",limits,()=>true);}catch(ArgumentException){failed=true;}Check(failed,"changed/incorrect LOD template set rejected");
        failed=false;try{new LodTemplate(templates[0].Source,1,30,new FixedSample[65537],"large",limits,()=>true);}catch(ArgumentException){failed=true;}Check(failed,"fixed array length before copy");
        foreach(var bad in new[]{new[]{new RootCell(new Int3(0,30,0),0xff123456)},new[]{new RootCell(new Int3(0,0,0),0xff123456),new RootCell(new Int3(0,0,0),0xff123456)}}){var j=new CompositeJob(templates,bad,"bad-root",limits,()=>true);Finish(j,1000,1000,1);Check(j.State==Phase.Rejected&&j.Published==null,"bad root domain/duplicate");}
        Check(!typeof(CompositeJob).GetProperties().Any(p=>p.PropertyType==typeof(VoxelWorld)),"no public mutable voxel cache");
        Check(!typeof(CompositeOutput).GetFields().Any(f=>f.FieldType.IsArray)&&!typeof(LodOutput).GetFields().Any(f=>f.FieldType.IsArray),"no public output arrays");
        var assembly=typeof(CompositeJob).Assembly;Check(assembly.GetReferencedAssemblies().All(a=>!a.Name.StartsWith("Unity")),"standalone library has no Unity assembly reference");Check(assembly.GetTypes().All(t=>t.Namespace==null||!t.Namespace.StartsWith("Unity")),"no shim namespace in standalone library");
    }
    static void Main(string[] args)
    {
        var rows=new List<object>();var timer=Stopwatch.StartNew();try{Synthetic();for(int i=0;i<2;i++){int id=10+i;rows.Add(Actual(id,args[1+i*2],args[2+i*2],args[0]));GC.Collect();}File.WriteAllText(Path.Combine(args[0],"report.json"),JsonSerializer.Serialize(new{status="PASS",assertions,seconds=timer.Elapsed.TotalSeconds,actual=rows,scope="Pure managed captured-summary/root compute adapter. No Unity/shim references, tree generation, pack decoding, native or production worker/cache integration. Byte accounting is declared bounded array payload, not measured CLR reservation."},new JsonSerializerOptions{WriteIndented=true}));Console.WriteLine("PASS "+assertions+" managed composite assertions");}
        catch(Exception e){File.WriteAllText(Path.Combine(args[0],"failure.json"),JsonSerializer.Serialize(new{status="FAIL",stage,assertions,error=e.ToString(),seconds=timer.Elapsed.TotalSeconds,completed=rows},new JsonSerializerOptions{WriteIndented=true}));Console.Error.WriteLine(e);Environment.ExitCode=1;}
    }
}
