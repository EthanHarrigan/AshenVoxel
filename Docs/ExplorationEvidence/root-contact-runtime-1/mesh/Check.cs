using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using AshenVoxel.Core;
using AshenVoxel.Rendering;
using UnityEngine;

static class Check
{
    static int assertions;
    static void Assert(bool good,string reason){if(!good)throw new Exception(reason);assertions++;}
    static Dictionary<string,string> Faces(Mesh mesh,int maximumY)
    {
        var result=new Dictionary<string,string>();
        for(int q=0;q<mesh.vertexCount;q+=4)
        {
            var n=mesh.normals[q];int axis=Math.Abs(n.x)>.5?0:Math.Abs(n.y)>.5?1:2;bool positive=n[axis]>0;
            int[] lo={int.MaxValue,int.MaxValue,int.MaxValue},hi={int.MinValue,int.MinValue,int.MinValue};
            for(int i=0;i<4;i++)for(int d=0;d<3;d++){int cell=(int)Math.Round(mesh.vertices[q+i][d]*10);lo[d]=Math.Min(lo[d],cell);hi[d]=Math.Max(hi[d],cell);}
            if(positive)lo[axis]--;hi[axis]=lo[axis]+1;
            string attributes=string.Join("/",Enumerable.Range(q,4).Select(i=>$"{mesh.colors32[i].r},{mesh.colors32[i].g},{mesh.colors32[i].b},{mesh.colors32[i].a}"));
            for(int z=lo[2];z<hi[2];z++)for(int y=lo[1];y<hi[1];y++)for(int x=lo[0];x<hi[0];x++)
            {
                if(y>=maximumY)continue;
                string key=$"{x},{y},{z},{axis},{positive}";if(!result.TryAdd(key,attributes))throw new Exception("Duplicate face:"+key);
            }
        }
        return result;
    }
    static object Compare(VoxelWorld world,int cut,string label)
    {
        var baseline=new GreedyChunkMesher();var candidate=new RootBaseChunkMesher();int before=0,after=0,guardFaces=0;
        foreach(var chunk in world.Chunks)
        {
            var stock=new Mesh();var patch=new Mesh();var all=new Mesh();baseline.Build(world,chunk.Key,stock);candidate.Build(world,chunk.Key,patch,cut);candidate.Build(world,chunk.Key,all);
            Assert(Faces(stock,int.MaxValue).OrderBy(p=>p.Key).SequenceEqual(Faces(all,int.MaxValue).OrderBy(p=>p.Key)),label+" unlimited unchanged RGB/material/AO/normals");
            var expected=Faces(stock,cut);var actual=Faces(patch,int.MaxValue);
            Assert(expected.OrderBy(p=>p.Key).SequenceEqual(actual.OrderBy(p=>p.Key)),label+" emission domain matches authoritative faces exactly");
            Assert(patch.vertices.All(v=>v.y<=cut*.1f+.0001f),label+" no guard-row geometry above seam");
            before+=stock.triangles.Length/6;after+=patch.triangles.Length/6;guardFaces+=Faces(stock,int.MaxValue).Count-expected.Count;
        }
        return new{label,chunks=world.Chunks.Count,unmasked_quads=before,masked_quads=after,excluded_guard_cell_faces=guardFaces};
    }
    static void Main(string[] args)
    {
        var rows=new List<object>();var solid=new VoxelWorld(35,36,35,.1f,714103);
        for(int z=1;z<34;z++)for(int y=0;y<33;y++)for(int x=1;x<34;x++)
            if((x-17)*(x-17)+(z-17)*(z-17)<=100&&!(x<14&&z<14&&y<10))solid.Set(x,y,z,MaterialId.Wood,(uint)(0xff805a30u+(uint)((x+y+z)%3)));
        foreach(int cut in new[]{1,15,16,17,29,30,31,32})rows.Add(Compare(solid,cut,"synthetic cut"+cut));
        foreach(int id in new[]{10,11})
        {
            var cells=new List<(short x,short y,short z,uint color)>();using(var r=new BinaryReader(File.OpenRead(Path.Combine(args[0],$"expected-{id}.bin"))))
            {int n=r.ReadInt32();for(int i=0;i<n;i++)cells.Add((r.ReadInt16(),r.ReadInt16(),r.ReadInt16(),r.ReadUInt32()));}
            int x0=cells.Min(c=>c.x),y0=cells.Min(c=>c.y),z0=cells.Min(c=>c.z);
            var world=new VoxelWorld(cells.Max(c=>c.x)-x0+1,cells.Max(c=>c.y)-y0+1,cells.Max(c=>c.z)-z0+1,.1f,714103);
            foreach(var c in cells)world.Set(c.x-x0,c.y-y0,c.z-z0,MaterialId.Wood,c.color);
            rows.Add(Compare(world,30-y0,"actual mapped root"+id));
        }
        File.WriteAllText(args[1],JsonSerializer.Serialize(new{status="PASS",assertions,scope="Isolated copy of actual mesher with ownership-domain filtering only. CPU Mesh shim; exact expanded face geometry/RGB/material/AO versus stock mesher restricted to emitted cells. Guard stays in source for neighbor/AO reads. Does not prove old shared upper mesh AO agrees with changed lower row, nor native stitching/wind/LOD.",rows},new JsonSerializerOptions{WriteIndented=true}));
        Console.WriteLine("PASS "+assertions+" masked-mesher exact-face assertions across10 worlds/cuts.");
    }
}
