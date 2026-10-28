using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using AshenVoxel.Core;
using RootContactPrototype;
using RootContactInteraction;
using RootContactTemplateCache;

static class Checks
{
    static int checks;
    static void Check(bool pass,string reason){checks++;if(!pass)throw new Exception(reason);}
    static string Hash(byte[] bytes){using var h=SHA256.Create();return Convert.ToHexString(h.ComputeHash(bytes)).ToLowerInvariant();}
    static byte[] Edit(byte[] bytes,int offset,int value)
    {var data=(byte[])bytes.Clone();Array.Copy(BitConverter.GetBytes(value),0,data,offset,4);return data;}
    static byte[] Seal(byte[] data)
    {
        Array.Copy(BitConverter.GetBytes(data.Length-48),0,data,12,4);
        using var h=SHA256.Create();var digest=h.ComputeHash(data,48,data.Length-48);Array.Copy(digest,0,data,16,32);return data;
    }
    static TemplateBundle Read(byte[] bytes,int model,string pack,string expected=null)
    {
        using var stream=new MemoryStream(bytes,false);
        bool ok=TemplateBundle.TryRead(stream,expected??Hash(bytes),pack,model,out var data,out string reason);
        Check(ok&&data!=null,"Read failed: "+reason);Check(stream.CanRead,"Reader must leave caller stream open");return data;
    }
    static void Reject(byte[] bytes,int model,string pack,string expected=null,string exact=null)
    {
        using var stream=new MemoryStream(bytes,false);
        bool ok=TemplateBundle.TryRead(stream,expected??Hash(bytes),pack,model,out var data,out string reason);
        Check(!ok&&data==null&&!string.IsNullOrEmpty(reason),"Invalid data accepted");
        if(exact!=null)Check(reason==exact,"Expected "+exact+", got "+reason);
        Check(stream.CanRead,"Failure closed caller stream");
    }
    static Template RootOracle(string path)
    {
        var raw=File.ReadAllBytes(path);using var r=new BinaryReader(new MemoryStream(raw));int n=r.ReadInt32();var columns=new Column[n];
        for(int i=0;i<n;i++)
        {int x=r.ReadInt16(),z=r.ReadInt16();r.ReadInt16();int c=r.ReadInt32();var ys=new short[c];var colors=new uint[c];for(int j=0;j<c;j++){ys[j]=r.ReadInt16();colors[j]=r.ReadUInt32();}columns[i]=new Column(x,z,ys,colors);}
        return new Template(columns,29,"fixture-sha256:"+Hash(raw));
    }
    static UpperContext ContextOracle(string path)
    {
        var raw=File.ReadAllBytes(path);using var r=new BinaryReader(new MemoryStream(raw));int n=r.ReadInt32();var values=new List<WoodCell>();
        for(int i=0;i<n;i++){var p=new Int3(r.ReadInt16(),r.ReadInt16(),r.ReadInt16());uint c=r.ReadUInt32();if(p.y>=29&&p.y<=32)values.Add(new WoodCell(p,c));}
        return new UpperContext(values.ToArray(),"canonical-file-sha256:"+Hash(raw));
    }
    static void Main(string[] args)
    {
        string here=args[0],cache=args[1],outPath=args[2];var rows=new List<object>();
        using var manifest=JsonDocument.Parse(File.ReadAllText(Path.Combine(here,"export-manifest.json")));
        string pack=manifest.RootElement.GetProperty("pack_sha256").GetString();
        foreach(var record in manifest.RootElement.GetProperty("models").EnumerateArray())
        {
            int model=record.GetProperty("model").GetInt32();string expected=record.GetProperty("file_sha256").GetString();
            byte[] bytes=File.ReadAllBytes(Path.Combine(here,record.GetProperty("file").GetString()));Check(Hash(bytes)==expected,"Pinned fixture hash");
            TemplateBundle data=Read(bytes,model,pack,expected);var root=RootOracle(Path.Combine(cache,$"root-contact-runtime-prototype/v2-template-{model}.bin"));
            var context=ContextOracle(Path.Combine(cache,$"grounding-root-visual/wood-{model}-baseline.bin"));
            Check(data.ModelId==model&&data.Seed==record.GetProperty("seed").GetInt32()&&data.SerializedBytes==bytes.Length,"Model metadata");
            Check(data.Root.Identity==root.Identity&&data.Context.Identity==context.Identity,"Exact root and canonical-context provenance");
            Check(data.Root.Count==root.Count&&data.Root.SourceCells==root.SourceCells&&data.Context.Count==context.Count,"Exact source counts");
            for(int i=0;i<root.Count;i++)
            {var a=root.GetColumn(i);var b=data.Root.GetColumn(i);Check(a.X==b.X&&a.Z==b.Z&&a.Count==b.Count,"Column");for(int j=0;j<a.Count;j++)Check(a.Y(j)==b.Y(j)&&a.Tint(j)==b.Tint(j),"Source cell");}
            for(int i=0;i<context.Count;i++){var a=context.Cell(i);var b=data.Context.Cell(i);Check(a.Position.Equals(b.Position)&&a.Color==b.Color,"Context cell");}
            string run=model==10?"specimen-10-20261027T130937232091Z":"specimen-11-20261027T131159753179Z";
            string folder=Path.Combine(cache,"root-contact-composite-lods",run);
            using(var r=new BinaryReader(File.OpenRead(Path.Combine(folder,"canonical-order.bin"))))
            {int n=r.ReadInt32();Check(n==data.OrderCount,"Rank count");for(int i=0;i<n;i++){var p=new Int3(r.ReadInt16(),r.ReadInt16(),r.ReadInt16());Check(r.ReadInt32()==i&&p.Equals(data.OrderedChunk(i)),"Exact canonical chunk order");}}
            for(int k=0;k<data.LodCount;k++)
            {
                var lod=data.Lod(k);using var r=new BinaryReader(File.OpenRead(Path.Combine(folder,"fixed-F"+lod.Factor+".bin")));
                Check(r.ReadInt32()==0x314c4352&&r.ReadInt32()==lod.Factor&&r.ReadInt32()==lod.Cut&&r.ReadInt32()==data.Seed&&r.ReadInt32()==lod.Count,"Fixed header");
                for(int i=0;i<lod.Count;i++)
                {var a=lod.At(i);var p=new Int3(r.ReadInt16(),r.ReadInt16(),r.ReadInt16());byte m=r.ReadByte();uint color=r.ReadUInt32();int rank=r.ReadInt32(),index=r.ReadUInt16();Check(a.Cell.Equals(p)&&(byte)a.Material==m&&a.Color==color&&a.Rank==rank&&a.Index==index,"Exact fixed representative");}
            }
            // Read again and mutate caller bytes: all published data stays immutable.
            var clone=(byte[])bytes.Clone();var immutable=Read(clone,model,pack);Array.Clear(clone,0,clone.Length);
            Check(immutable.Root.Identity==root.Identity&&immutable.Context.Identity==context.Identity&&immutable.Lod(0).At(0).Color==data.Lod(0).At(0).Color,"Input-buffer ownership");
            foreach(int n in Enumerable.Range(0,49).Concat(new[]{64,128,184,bytes.Length/2,bytes.Length-17,bytes.Length-1}).Distinct())Reject(bytes.Take(n).ToArray(),model,pack,expected);
            Reject(bytes.Concat(new byte[]{0}).ToArray(),model,pack,expected, "TrailingBytes");
            Reject(Edit(bytes,8,2),model,pack,null,"Version");Reject(Edit(bytes,12,-1),model,pack,null,"PayloadLimit");Reject(Edit(bytes,12,TemplateBundle.MaximumPayloadBytes+1),model,pack,null,"PayloadLimit");
            Reject(bytes,model,pack,new string('0',64),"FileIdentity");Reject(bytes,model,new string('0',64),null,"PackIdentity");
            Reject(bytes,model==10?11:10,pack,null,"ModelSeed");Reject(bytes,0,pack,null,"InvalidExpectedIdentity");Reject(bytes,model,pack,expected.ToUpperInvariant(),"InvalidExpectedIdentity");
            foreach(int p in new[]{16,47,48,192,bytes.Length/3,bytes.Length-1})
            {var bad=(byte[])bytes.Clone();bad[p]^=0x40;Reject(bad,model,pack,null,"PayloadChecksum");}
            // Re-sign malformed records to exercise parser defenses independently of hash rejection.
            Reject(Seal(Edit(bytes,48,12)),model,pack,null,"ModelSeed");Reject(Seal(Edit(bytes,56,471)),model,pack,null,"Dimensions");
            Reject(Seal(Edit(bytes,68,234)),model,pack,null,"Anchor");Reject(Seal(Edit(bytes,176,3)),model,pack,null,"RootPolicy");
            Reject(Seal(Edit(bytes,184,-1)),model,pack,null,"ColumnsCount");Reject(Seal(Edit(bytes,184,8193)),model,pack,null,"ColumnsCount");
            Reject(Seal(Edit(bytes,192,97)),model,pack,null,"ColumnCellsCount");
            int contextOffset,rankOffset,lodOffset,fixedOffset;
            using(var scan=new BinaryReader(new MemoryStream(bytes,false)))
            {
                scan.BaseStream.Position=184;int n=scan.ReadInt32();for(int i=0;i<n;i++){scan.BaseStream.Position+=4;int c=scan.ReadInt32();scan.BaseStream.Position+=6*c;}
                contextOffset=(int)scan.BaseStream.Position;n=scan.ReadInt32();scan.BaseStream.Position+=10*n;
                rankOffset=(int)scan.BaseStream.Position;n=scan.ReadInt32();scan.BaseStream.Position+=6*n;
                lodOffset=(int)scan.BaseStream.Position;fixedOffset=lodOffset+12;
            }
            Reject(Seal(Edit(bytes,contextOffset,16385)),model,pack,null,"ContextCount");
            Reject(Seal(Edit(bytes,rankOffset,8193)),model,pack,null,"RanksCount");Reject(Seal(Edit(bytes,lodOffset,6)),model,pack,null,"LodCount");
            Reject(Seal(Edit(bytes,lodOffset+4,3)),model,pack,null,"LodLattice");Reject(Seal(Edit(bytes,fixedOffset,65537)),model,pack,null,"FixedSamplesCount");
            rows.Add(new{model,bytes=bytes.Length,root_columns=root.Count,root_cells=root.SourceCells,context_cells=context.Count,canonical_ranks=data.OrderCount,fixed_samples=Enumerable.Range(0,5).Sum(k=>data.Lod(k).Count)});
        }
        File.WriteAllText(outPath,JsonSerializer.Serialize(new{status="PASS",assertions=checks,models=rows,scope="Bounded companion data decoder only; corruption/truncation/version/identity/size and exact frozen template equality. Not production loading or native execution."},new JsonSerializerOptions{WriteIndented=true}));
        Console.WriteLine("PASS companion reader "+checks+" assertions");
    }
}
