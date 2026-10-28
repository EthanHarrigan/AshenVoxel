using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using AshenVoxel.Core;
using AshenVoxel.WorldScale;
static class SplitChecks
{
    static int checks;
    static void Check(bool v,string name){if(!v)throw new Exception(name);checks++;}
    static Dictionary<string,long> Areas(int[] indices,Func<int,int,short> pos,Func<int,byte> normal,Func<int,uint> color)
    {
        var sum=new Dictionary<string,long>();
        for(int i=0;i<indices.Length;i+=3)
        {
            int a=indices[i],b=indices[i+1],c=indices[i+2],n=normal(a),axis=n/2;
            long ax=pos(b,0)-pos(a,0),ay=pos(b,1)-pos(a,1),az=pos(b,2)-pos(a,2),bx=pos(c,0)-pos(a,0),by=pos(c,1)-pos(a,1),bz=pos(c,2)-pos(a,2);
            long area=axis==0?ay*bz-az*by:axis==1?az*bx-ax*bz:ax*by-ay*bx;
            if((n%2==0?area:-area)<=0)throw new Exception("Degenerate/wrong triangle winding");
            string key=$"{n}/{pos(a,axis)}/{color(a)}";sum.TryGetValue(key,out long old);sum[key]=old+Math.Abs(area);
        }
        return sum;
    }
    static object Verify(PrototypeMeshData mesh,string name)
    {
        long start=GC.GetAllocatedBytesForCurrentThread();var watch=Stopwatch.StartNew();var split=SharedUpperSplit.Build(mesh);watch.Stop();long allocation=GC.GetAllocatedBytesForCurrentThread()-start;
        Check(ReferenceEquals(mesh,split.Original),name+" retains original vertex arrays");
        Check(split.ExtraColors.Length==2*split.CrossingQuads,name+" only two additional vertices per crossing");
        Check(split.UpperIndices.Length+split.BaseIndices.Length==mesh.Indices.Length+split.CrossingQuads*6,name+" exact index overhead");
        var before=Areas(mesh.Indices,(v,a)=>mesh.Positions[v*3+a],v=>mesh.Normals[v],v=>mesh.Colors[v]);
        var after=Areas(split.UpperIndices,split.Position,split.Normal,split.Color);var lower=Areas(split.BaseIndices,split.Position,split.Normal,split.Color);
        foreach(var p in lower){after.TryGetValue(p.Key,out long old);after[p.Key]=old+p.Value;}
        Check(before.OrderBy(p=>p.Key).SequenceEqual(after.OrderBy(p=>p.Key)),name+" exact material/pigment/AO plane areas");
        Check(split.UpperIndices.All(v=>split.Position(v,1)>=30),name+" upper lies above seam");
        Check(split.BaseIndices.All(v=>split.Position(v,1)<=30),name+" authored base below seam");
        for(int i=0;i<split.UpperIndices.Length;i+=3)
        {int v=split.UpperIndices[i];if(split.Normal(v)==2)Check(split.Position(v,1)>30,name+" positive horizontal seam face owned by base");}
        for(int i=0;i<split.BaseIndices.Length;i+=3)
        {int v=split.BaseIndices[i];if(split.Normal(v)==3)Check(split.Position(v,1)<30,name+" negative horizontal seam face owned by upper");}
        return new{name,vertices=mesh.VertexCount,original_indices=mesh.Indices.Length,crossing_quads=split.CrossingQuads,appended_vertices=split.ExtraColors.Length,
            upper_triangles=split.UpperIndices.Length/3,authored_base_triangles=split.BaseIndices.Length/3,
            additional_gpu_arithmetic_bytes_at_28_bytes_per_vertex=(long)split.ExtraColors.Length*28+(long)split.CrossingQuads*6*4,
            temporary_additional_index_and_attribute_bytes=(long)(split.UpperIndices.Length+split.BaseIndices.Length)*4+split.ExtraColors.Length*11L,
            cpu_single_run_ms=watch.Elapsed.TotalMilliseconds,cumulative_allocated_bytes=allocation};
    }
    static PrototypeMeshData Quad(byte normal,int low,int high,bool varying=false)
    {
        short x0=-2,x1=2,z0=-2,z1=2;short[] positions;
        if(normal/2==0)positions=new short[]{0,(short)low,z0,0,(short)high,z0,0,(short)high,z1,0,(short)low,z1};
        else if(normal/2==1)positions=new short[]{x0,(short)low,z0,x0,(short)low,z1,x1,(short)low,z1,x1,(short)low,z0};
        else positions=new short[]{x0,(short)low,0,x1,(short)low,0,x1,(short)high,0,x0,(short)high,0};
        var colors=Enumerable.Repeat(0x0f885522u,4).ToArray();if(varying)colors[1]^=1u<<24;
        return new PrototypeMeshData(positions,Enumerable.Repeat(normal,4).ToArray(),colors,normal%2==0?new[]{0,1,2,0,2,3}:new[]{0,2,1,0,3,2});
    }
    static void Main(string[] args)
    {
        var rows=new List<object>();foreach(byte normal in new byte[]{0,1,2,3,4,5})
        {
            rows.Add(Verify(Quad(normal,20,40),"synthetic normal"+normal));rows.Add(Verify(Quad(normal,30,40),"synthetic exact-seam normal"+normal));
        }
        bool rejected=false;try{SharedUpperSplit.Build(Quad(0,20,40,true));}catch(InvalidOperationException){rejected=true;}Check(rejected,"nonuniform crossing shading rejected");
        using var reader=new ExplorationPrototypeReader(File.ReadAllBytes(args[0]));while(reader.TryRead(out var p))
        {
            if(p.Id==10||p.Id==11)rows.Add(Verify(p.Mesh(0),"actual live pack "+p.Name+" LOD0"));p.ReleaseMeshData();
        }
        File.WriteAllText(args[1],JsonSerializer.Serialize(new{passed=true,checks,scope="CPU split of actual live packed LOD0. Original vertex arrays shared; upper/authored-base index partitions retain exact surface area, normals, material/RGB/AO with two additional vertices per uniform crossing quad. Does not create native Mesh/submeshes or change live drawing/collision/LODs. Single-run CPU includes JIT, not FPS. Coarse LOD seam alignment still requires separate treatment.",rows},new JsonSerializerOptions{WriteIndented=true}));
        Console.WriteLine("PASS "+checks+" shared-upper partition assertions.");
    }
}
