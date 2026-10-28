using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using AshenVoxel.WorldScale;
using UnityEngine;

// CPU conversion/byte accounting only. No Mesh API, native upload or render
// performance is inferred from these numerical Unity substitutes.
static class Program
{
    [StructLayout(LayoutKind.Sequential,Pack=1)]
    struct PackedVertex {public Vector3 Position;public sbyte NX,NY,NZ,NW;public Color32 Color;}
    sealed class Ordinary {public Vector3[] Positions,Normals;public Color32[] Colors;}
    static Ordinary Expand(PrototypeMeshData source)
    {
        var data=new Ordinary{Positions=new Vector3[source.VertexCount],Normals=new Vector3[source.VertexCount],Colors=new Color32[source.VertexCount]};
        for(int i=0;i<source.VertexCount;i++)
        {
            data.Positions[i]=new Vector3(source.Positions[i*3]*.1f,source.Positions[i*3+1]*.1f,source.Positions[i*3+2]*.1f);
            byte n=source.Normals[i];data.Normals[i]=n==0?Vector3.right:n==1?Vector3.left:n==2?Vector3.up:n==3?Vector3.down:n==4?Vector3.forward:Vector3.back;
            uint c=source.Colors[i];data.Colors[i]=new Color32((byte)c,(byte)(c>>8),(byte)(c>>16),(byte)(c>>24));
        }
        return data;
    }
    static PackedVertex[] Pack(PrototypeMeshData source)
    {
        var data=new PackedVertex[source.VertexCount];
        for(int i=0;i<source.VertexCount;i++)
        {
            byte n=source.Normals[i];uint c=source.Colors[i];
            data[i]=new PackedVertex{Position=new Vector3(source.Positions[i*3]*.1f,source.Positions[i*3+1]*.1f,source.Positions[i*3+2]*.1f),
                NX=n==0?(sbyte)127:n==1?(sbyte)-127:(sbyte)0,NY=n==2?(sbyte)127:n==3?(sbyte)-127:(sbyte)0,NZ=n==4?(sbyte)127:n==5?(sbyte)-127:(sbyte)0,NW=0,
                Color=new Color32((byte)c,(byte)(c>>8),(byte)(c>>16),(byte)(c>>24))};
        }
        return data;
    }
    static void Main(string[] args)
    {
        if(Marshal.SizeOf<PackedVertex>()!=20)throw new Exception("Packed stride differs from20 bytes");
        byte[] pack=File.ReadAllBytes(args[1]);var rows=new List<object>();
        double[] ordinaryMs=new double[4],packedMs=new double[4];long[] ordinaryAlloc=new long[4],packedAlloc=new long[4];
        long vertices=0,ordinaryMeshBytes=0,packedMeshBytes=0;int models=0,lods=0,billboards=0;double maxOrdinary=0,maxPacked=0;
        using(var reader=new ExplorationPrototypeReader(pack))
        while(reader.TryRead(out var prototype))
        {
            models++;
            for(int lod=0;lod<prototype.LodCount;lod++)
            {
                if(prototype.Kind==0&&lod>=3){ordinaryMeshBytes+=136;packedMeshBytes+=136;billboards++;continue;}
                var source=prototype.Mesh(lod);vertices+=source.VertexCount;lods++;
                ordinaryMeshBytes+=source.VertexCount*28L+source.Indices.LongLength*4;packedMeshBytes+=source.VertexCount*20L+source.Indices.LongLength*4;
                var times=new List<object>();
                for(int pass=0;pass<4;pass++)
                {
                    Ordinary ordinary=null;PackedVertex[] packed=null;double oldMs=0,newMs=0;long oldAlloc=0,newAlloc=0;
                    for(int order=0;order<2;order++)
                    {
                        bool old=(pass+order)%2==0;long bytes=GC.GetAllocatedBytesForCurrentThread(),stamp=Stopwatch.GetTimestamp();
                        if(old)ordinary=Expand(source);else packed=Pack(source);
                        double ms=(Stopwatch.GetTimestamp()-stamp)*1000.0/Stopwatch.Frequency;long allocated=GC.GetAllocatedBytesForCurrentThread()-bytes;
                        if(old){oldMs=ms;oldAlloc=allocated;}else{newMs=ms;newAlloc=allocated;}
                    }
                    if(pass==0)for(int i=0;i<source.VertexCount;i++)
                    {
                        Vector3 p=ordinary.Positions[i],n=ordinary.Normals[i];var q=packed[i];Color32 c=ordinary.Colors[i],d=q.Color;
                        if(p.x!=q.Position.x||p.y!=q.Position.y||p.z!=q.Position.z||n.x!=q.NX/127f||n.y!=q.NY/127f||n.z!=q.NZ/127f||c.r!=d.r||c.g!=d.g||c.b!=d.b||c.a!=d.a)
                            throw new Exception("Packed vertex mismatch "+prototype.Id+"/"+lod+"/"+i);
                    }
                    ordinaryMs[pass]+=oldMs;packedMs[pass]+=newMs;ordinaryAlloc[pass]+=oldAlloc;packedAlloc[pass]+=newAlloc;
                    maxOrdinary=Math.Max(maxOrdinary,oldMs);maxPacked=Math.Max(maxPacked,newMs);
                    times.Add(new{pass,ordinaryMs=oldMs,packedMs=newMs,ordinaryAllocated=oldAlloc,packedAllocated=newAlloc});GC.KeepAlive(ordinary);GC.KeepAlive(packed);
                }
                rows.Add(new{id=prototype.Id,lod,vertices=source.VertexCount,times});
            }
            prototype.ReleaseMeshData();
        }
        File.WriteAllText(args[0],JsonSerializer.Serialize(new{status="PASS",models,lods,billboards,vertices,ordinaryMeshBytes,packedMeshBytes,ordinaryMs,packedMs,ordinaryAlloc,packedAlloc,maxOrdinary,maxPacked,rows,
            scope="Actual pack with installed billboard substitution. CPU-only conversion and exact vertex oracle;20-byte candidate format, no Mesh API/native upload/shader/wind/bounds test. GPU mesh bytes are arithmetic estimates, not resident VRAM. No production implementation or adoption."},new JsonSerializerOptions{WriteIndented=true}));
        Console.WriteLine("PASS exact "+vertices+" vertex positions/normals/colors/AO; ordinaryMs="+string.Join(",",ordinaryMs)+" packedMs="+string.Join(",",packedMs));
        Console.WriteLine("Estimated payload "+ordinaryMeshBytes+" -> "+packedMeshBytes+" bytes");
    }
}
