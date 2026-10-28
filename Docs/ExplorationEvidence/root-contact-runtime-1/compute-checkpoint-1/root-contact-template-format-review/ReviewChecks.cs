using System;
using System.IO;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text.Json;
using RootContactTemplateCache;
static class ReviewChecks
{
    sealed class Input:Stream
    {
        readonly byte[] data;readonly int piece;readonly int failAfter;readonly bool throwCanRead;
        int at;public int Reads,Bytes;public bool Closed;
        public Input(byte[] data,int piece=7,int failAfter=int.MaxValue,bool throwCanRead=false){this.data=data;this.piece=piece;this.failAfter=failAfter;this.throwCanRead=throwCanRead;}
        public override bool CanRead=>throwCanRead?throw new IOException("CanRead failure"):true;
        public override bool CanSeek=>false;public override bool CanWrite=>false;
        public override long Length=>throw new NotSupportedException();public override long Position{get=>throw new NotSupportedException();set=>throw new NotSupportedException();}
        public override int Read(byte[] buffer,int offset,int count)
        {Reads++;if(at>=failAfter)throw new IOException("Read failure");int n=Math.Min(Math.Min(piece,count),Math.Min(data.Length-at,failAfter-at));Array.Copy(data,at,buffer,offset,n);at+=n;Bytes+=n;return n;}
        protected override void Dispose(bool disposing){Closed=true;base.Dispose(disposing);}
        public override void Flush()=>throw new NotSupportedException();public override long Seek(long offset,SeekOrigin origin)=>throw new NotSupportedException();public override void SetLength(long length)=>throw new NotSupportedException();public override void Write(byte[] b,int o,int c)=>throw new NotSupportedException();
    }
    static string Hash(byte[] b){using var h=SHA256.Create();return Convert.ToHexString(h.ComputeHash(b)).ToLowerInvariant();}
    static void Main(string[] args)
    {
        string folder=args[0],pack=args[1],output=args[2];var results=new List<object>();
        foreach(string name in new[]{"valid","oversized","missing-context31","mismatched-fixed-rank"})
        {
            byte[] bytes=File.ReadAllBytes(Path.Combine(folder,name+".bin"));using var input=new Input(bytes);
            bool ok=TemplateBundle.TryRead(input,Hash(bytes),pack,10,out var bundle,out var why);
            results.Add(new{name,accepted=ok,failure=why,reads=input.Reads,bytes_read=input.Bytes,caller_stream_closed=input.Closed,output_null=bundle==null});
        }
        byte[] valid=File.ReadAllBytes(Path.Combine(folder,"valid.bin"));
        foreach(int failAfter in new[]{0,8,16,47,100,valid.Length})
        {
            using var input=new Input(valid,17,failAfter);bool ok=TemplateBundle.TryRead(input,Hash(valid),pack,10,out var bundle,out var why);
            if(ok||bundle!=null||input.Closed)throw new Exception("Read failure not contained");
            results.Add(new{name="read-error",failAfter,accepted=ok,failure=why,output_null=bundle==null,caller_stream_closed=input.Closed});
        }
        foreach(string name in new[]{"missing-context31","mismatched-fixed-rank"})
        {
            byte[] bytes=File.ReadAllBytes(Path.Combine(folder,name+".bin"));using var input=new Input(bytes);
            bool ok=TemplateBundle.TryRead(input,Hash(valid),pack,10,out var bundle,out var why);
            if(ok||bundle!=null||why!="FileIdentity")throw new Exception("Trusted pin bypass");
            results.Add(new{name=name+"-original-trusted-pin",accepted=ok,failure=why});
        }
        bool escaped=false;string exception="";
        try{using var input=new Input(valid,throwCanRead:true);TemplateBundle.TryRead(input,Hash(valid),pack,10,out _,out _);}
        catch(Exception e){escaped=true;exception=e.GetType().Name;}
        results.Add(new{name="CanRead-throws",exception_escaped=escaped,exception});
        File.WriteAllText(output,JsonSerializer.Serialize(new{scope="Independent adverse tests against frozen first-PASS library; accepted resigned semantic mutations are not trusted-file-pin bypasses.",results},new JsonSerializerOptions{WriteIndented=true}));
        Console.WriteLine("Review cases="+results.Count+" CanRead exception escaped="+escaped);
    }
}
