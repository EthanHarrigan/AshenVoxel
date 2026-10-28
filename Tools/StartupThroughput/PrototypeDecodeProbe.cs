using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using AshenVoxel.WorldScale;

public static class DecodeMetrics
{
    public static double Inflate,Crc,Columns;
    public static readonly List<double> Lods=new List<double>();
    public static long Start=>Stopwatch.GetTimestamp();
    public static double Elapsed(long start)=>(Stopwatch.GetTimestamp()-start)*1000.0/Stopwatch.Frequency;
    public static void Reset(){Inflate=Crc=Columns=0;Lods.Clear();}
}
static class Program
{
    static readonly BindingFlags Flags=BindingFlags.Instance|BindingFlags.NonPublic;
    static void Bytes(IncrementalHash hash,Array array)
    {
        var buffer=new byte[Math.Min(65536,Math.Max(1,Buffer.ByteLength(array)))];
        for(int offset=0;offset<Buffer.ByteLength(array);offset+=buffer.Length)
        {int count=Math.Min(buffer.Length,Buffer.ByteLength(array)-offset);Buffer.BlockCopy(array,offset,buffer,0,count);hash.AppendData(buffer,0,count);}
    }
    static string Digest(ExplorationPrototype p)
    {
        using var hash=IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        using var bytes=new MemoryStream();using var writer=new BinaryWriter(bytes);
        writer.Write(p.Id);writer.Write(p.Kind);writer.Write(p.Name);
        foreach(float f in new[]{p.MinX,p.MinY,p.MinZ,p.MaxX,p.MaxY,p.MaxZ})writer.Write(f);
        writer.Write(p.LodCount);writer.Flush();hash.AppendData(bytes.ToArray());
        for(int l=0;l<p.LodCount;l++){var m=p.Mesh(l);Bytes(hash,m.Positions);Bytes(hash,m.Normals);Bytes(hash,m.Colors);Bytes(hash,m.Indices);}
        foreach(string name in new[]{"collision","leaves","sticks"})
        {
            var columns=(Dictionary<int,PrototypeCollisionColumn>)typeof(ExplorationPrototype).GetField(name,Flags).GetValue(p);
            foreach(var pair in columns.OrderBy(x=>x.Key))
            {bytes.SetLength(0);writer.Write(pair.Key);foreach(short span in pair.Value.Spans)writer.Write(span);writer.Flush();hash.AppendData(bytes.ToArray());}
        }
        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }
    static void Main(string[] args)
    {
        var readClock=Stopwatch.StartNew();byte[] pack=File.ReadAllBytes(args[0]);double fileReadMs=readClock.Elapsed.TotalMilliseconds;
        var passes=new List<object>();var audit=new List<object>();
        for(int pass=0;pass<5;pass++)
        {
            GC.Collect();GC.WaitForPendingFinalizers();GC.Collect();
            var records=new List<object>();double total=0,inflate=0,crc=0,columns=0,mesh=0;long allocations=0;
            using var reader=new ExplorationPrototypeReader(pack);
            while(reader.ReadCount<reader.Count)
            {
                DecodeMetrics.Reset();long before=GC.GetAllocatedBytesForCurrentThread();long stamp=Stopwatch.GetTimestamp();
                if(!reader.TryRead(out ExplorationPrototype p))throw new Exception("Early record end");
                double ms=DecodeMetrics.Elapsed(stamp);long allocated=GC.GetAllocatedBytesForCurrentThread()-before;
                var lods=Enumerable.Range(0,p.LodCount).Select(l=>new{lod=l,vertices=p.Mesh(l).VertexCount,indices=p.Mesh(l).Indices.Length,
                    bytes=p.Mesh(l).Positions.LongLength*2+p.Mesh(l).Normals.LongLength+p.Mesh(l).Colors.LongLength*4+p.Mesh(l).Indices.LongLength*4,
                    decodeMs=DecodeMetrics.Lods[l]}).ToArray();
                records.Add(new{id=p.Id,p.Name,p.Kind,ms,allocated,inflateMs=DecodeMetrics.Inflate,crcMs=DecodeMetrics.Crc,columnsMs=DecodeMetrics.Columns,lods});
                total+=ms;inflate+=DecodeMetrics.Inflate;crc+=DecodeMetrics.Crc;columns+=DecodeMetrics.Columns;mesh+=DecodeMetrics.Lods.Sum();allocations+=allocated;
                // Untimed separate audit pass avoids changing the next timed record's allocation/JIT state.
                p.ReleaseMeshData();
            }
            if(reader.TryRead(out _))throw new Exception("Record count mismatch");
            passes.Add(new{pass,kind=pass==0?"first-process decode/JIT":"warm-process decode",totalMs=total,allocatedBytes=allocations,inflateMs=inflate,crcMs=crc,columnsMs=columns,meshMs=mesh,records});
            Console.WriteLine("PASS "+pass+": "+total.ToString("F2")+"ms, "+(allocations/1048576.0).ToString("F2")+"MiB allocated; mesh "+mesh.ToString("F2")+"ms");
        }
        using(var reader=new ExplorationPrototypeReader(pack))
            while(reader.TryRead(out ExplorationPrototype p)){audit.Add(new{id=p.Id,sha256=Digest(p)});p.ReleaseMeshData();}
        File.WriteAllText(args[1],JsonSerializer.Serialize(new{status="PASS",packBytes=pack.Length,packSha256=Convert.ToHexString(SHA256.HashData(pack)).ToLowerInvariant(),fileReadMs,passes,audit,
            scope="Actual AVPM4 pack, frozen engine-free reader, one process first decode plus four warm decodes. Phase instrumentation in private source only. Allocated bytes are cumulative .NET allocations, not resident memory. No Unity upload, GPU, Play or OS disk-cache flush."},new JsonSerializerOptions{WriteIndented=true}));
    }
}
