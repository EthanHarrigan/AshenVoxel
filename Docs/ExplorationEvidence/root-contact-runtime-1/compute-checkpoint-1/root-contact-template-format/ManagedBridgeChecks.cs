using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using AshenVoxel.Core;
using RootContactArrayMesher;
using RootContactManagedComposite;
using RootContactTemplateCache;

static class ManagedBridgeChecks
{
    static int checks;
    static void Require(bool good,string why){checks++;if(!good)throw new Exception(why);}
    static int Local(float v,int factor,int anchor)=>(int)Math.Round(v/(double)(.1f*factor))*factor-anchor;
    static byte Normal(Float3 n)=>n.x==1?(byte)0:n.x==-1?(byte)1:n.y==1?(byte)2:n.y==-1?(byte)3:n.z==1?(byte)4:(byte)5;
    static uint Color(Rgba32 c)=>(uint)(c.r|c.g<<8|c.b<<16|c.a<<24);
    static void Main(string[] args)
    {
        string here=args[0],cache=args[1];var rows=new List<object>();
        using var manifest=JsonDocument.Parse(File.ReadAllText(Path.Combine(here,"export-manifest.json")));
        string pack=manifest.RootElement.GetProperty("pack_sha256").GetString();
        foreach(var m in manifest.RootElement.GetProperty("models").EnumerateArray())
        {
            int id=m.GetProperty("model").GetInt32();using var input=File.OpenRead(Path.Combine(here,m.GetProperty("file").GetString()));
            Require(TemplateBundle.TryRead(input,m.GetProperty("file_sha256").GetString(),pack,id,out var bundle,out string reason),reason);
            var limits=new Limits();var shared=ManagedTemplateAssembly.Create(bundle,limits,2*1024*1024,()=>true);
            Require(shared.DeclaredRetainedElementBytes<=limits.MaxTemplateBytes,"Template budget");
            var root=new List<RootCell>();
            using(var r=new BinaryReader(File.OpenRead(Path.Combine(cache,$"root-contact-runtime-prototype/v2-expected-{id}.bin"))))
            {int n=r.ReadInt32();Require(n>0&&n<=65536,"Root fixture count");for(int i=0;i<n;i++){var p=new Int3(r.ReadInt16(),r.ReadInt16(),r.ReadInt16());uint c=r.ReadUInt32();if(p.y<=29)root.Add(new RootCell(p,c));}Require(r.BaseStream.Position==r.BaseStream.Length,"Fixture length");}
            var job=new CompositeJob(shared.CopyTemplateReferences(),root.ToArray(),"bridge/"+bundle.FileIdentity,limits,()=>true);
            int steps=0;while(!job.Terminal){job.Step(701,997,1);Require(++steps<10000,"Continuation limit");Require(job.ActiveCoarseWorlds<=1,"One coarse world");}
            Require(job.State==Phase.Complete&&job.Published!=null,"Composite failure "+job.Failure);var result=job.Published;Require(result.Count==5,"All five LODs");
            for(int lod=0;lod<5;lod++)
            {
                var output=result.At(lod);string fixture=Path.Combine(cache,"root-contact-composite-lods/captured-replay-20261027T131848203063Z",$"specimen-{id}",$"lower-F{output.Factor}.bin");
                using var r=new BinaryReader(File.OpenRead(fixture));Require(r.ReadInt32()==0x314d4352,"Mesh magic");int count=r.ReadInt32();Require(count==output.Count,"Chunk count");
                // Recorded output is sorted by coordinate; assembly is free to discover
                // chunks in a different order while preserving exact per-chunk geometry.
                var actual=new Dictionary<Int3,MeshData>();for(int k=0;k<output.Count;k++)actual.Add(output.At(k).Coordinate,output.At(k).Data);
                for(int k=0;k<count;k++)
                {
                    var coordinate=new Int3(r.ReadInt32(),r.ReadInt32(),r.ReadInt32());int nv=r.ReadInt32(),ni=r.ReadInt32();
                    Require(actual.TryGetValue(coordinate,out var mesh)&&mesh.VertexCount==nv&&mesh.IndexCount==ni,"Exact chunk counts");
                    for(int i=0;i<nv;i++)
                    {
                        int x=r.ReadInt16(),y=r.ReadInt16(),z=r.ReadInt16();byte n=r.ReadByte();uint color=r.ReadUInt32();var p=mesh.Vertices[i];
                        Require(Local(p.x,output.Factor,235)==x&&Local(p.y,output.Factor,1028)==y&&Local(p.z,output.Factor,235)==z,"Exact local vertex");
                        Require(Normal(mesh.Normals[i])==n&&Color(mesh.Colors[i])==color,"Exact normal RGB material AO");
                    }
                    for(int i=0;i<ni;i++)Require(r.ReadInt32()==mesh.Indices[i],"Exact topology");
                    Require(mesh.PayloadBytes==mesh.RetainedBufferBytes,"Compact immutable mesh retention");
                }
                Require(r.BaseStream.Position==r.BaseStream.Length,"Mesh EOF");
            }
            bool rejected=false;try{ManagedTemplateAssembly.Create(bundle,limits,0,()=>true);}catch(ArgumentException){rejected=true;}Require(rejected,"Conversion capacity refusal");
            rejected=false;try{ManagedTemplateAssembly.Create(bundle,limits,2*1024*1024,()=>false);}catch(InvalidOperationException){rejected=true;}Require(rejected,"Stale assembly");
            using var cancel=new CancellationTokenSource();cancel.Cancel();rejected=false;try{ManagedTemplateAssembly.Create(bundle,limits,2*1024*1024,()=>true,cancel.Token);}catch(OperationCanceledException){rejected=true;}Require(rejected,"Cancelled assembly");
            var exported=shared.CopyTemplateReferences();exported[0]=null;Require(shared.At(0)!=null,"Reference array ownership");
            rows.Add(new{model=id,steps,retained_mesh_bytes=result.RetainedMeshBytes,shared_template_elements=shared.DeclaredRetainedElementBytes,temporary_conversion_elements=shared.MaximumConversionElementBytes,peak_declared_composite_elements=job.PeakDeclaredTotalBytes});
        }
        File.WriteAllText(args[2],JsonSerializer.Serialize(new{status="PASS",assertions=checks,models=rows,scope="Trusted companion files through immutable managed template assembly and composite reducer/mesher, all10 exact recorded lower meshes; compact root fixture input, no fulltree generation, Unity, native or production adoption."},new JsonSerializerOptions{WriteIndented=true}));
        Console.WriteLine("PASS managed companion bridge "+checks);
    }
}
