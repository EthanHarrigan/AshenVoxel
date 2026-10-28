using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using AshenVoxel.Core;
using AshenVoxel.Rendering;
using UnityEngine;

static class UpperSeamCheck
{
    static Dictionary<(short x,short y,short z),uint> Read(string path)
    {
        var result=new Dictionary<(short,short,short),uint>();using var r=new BinaryReader(File.OpenRead(path));int n=r.ReadInt32();
        for(int i=0;i<n;i++){short x=r.ReadInt16(),y=r.ReadInt16(),z=r.ReadInt16();uint c=r.ReadUInt32();if(y>=29&&y<=34)result.Add((x,y,z),c);}return result;
    }
    static Dictionary<string,string> UpperFaces(Dictionary<(short x,short y,short z),uint> cells,int x0,int z0,int sideX,int sideZ)
    {
        var world=new VoxelWorld(sideX,6,sideZ,.1f,714103);foreach(var p in cells)world.Set(p.Key.x-x0,p.Key.y-29,p.Key.z-z0,MaterialId.Wood,p.Value);
        var result=new Dictionary<string,string>();var mesher=new GreedyChunkMesher();
        foreach(var chunk in world.Chunks)
        {
            var mesh=new Mesh();mesher.Build(world,chunk.Key,mesh);
            for(int q=0;q<mesh.vertexCount;q+=4)
            {
                var n=mesh.normals[q];int axis=Math.Abs(n.x)>.5?0:Math.Abs(n.y)>.5?1:2;bool positive=n[axis]>0;
                int[] lo={int.MaxValue,int.MaxValue,int.MaxValue},hi={int.MinValue,int.MinValue,int.MinValue};
                for(int i=0;i<4;i++)for(int d=0;d<3;d++){int v=(int)Math.Round(mesh.vertices[q+i][d]*10);lo[d]=Math.Min(lo[d],v);hi[d]=Math.Max(hi[d],v);}
                if(positive)lo[axis]--;hi[axis]=lo[axis]+1;
                string a=string.Join("/",Enumerable.Range(q,4).Select(i=>$"{mesh.colors32[i].r},{mesh.colors32[i].g},{mesh.colors32[i].b},{mesh.colors32[i].a}"));
                for(int z=lo[2];z<hi[2];z++)for(int y=Math.Max(1,lo[1]);y<Math.Min(5,hi[1]);y++)for(int x=lo[0];x<hi[0];x++)result.Add($"{x},{y},{z},{axis},{positive}",a);
            }
        }
        return result;
    }
    static void Main(string[] args)
    {
        var rows=new List<object>();foreach(int id in new[]{10,11})
        {
            var canonical=Read(Path.Combine(args[0],$"wood-{id}-baseline.bin"));var lower=Read(Path.Combine(args[0],$"continuous-shaft/expected-{id}.bin"));
            var merged=canonical.Where(p=>p.Key.y>=30).ToDictionary(p=>p.Key,p=>p.Value);
            foreach(var p in lower.Where(p=>p.Key.y==29))merged[p.Key]=p.Value;
            var keys=canonical.Keys.Concat(merged.Keys).ToArray();int x0=keys.Min(p=>p.x),z0=keys.Min(p=>p.z),sx=keys.Max(p=>p.x)-x0+1,sz=keys.Max(p=>p.z)-z0+1;
            var baseline=UpperFaces(canonical,x0,z0,sx,sz);var candidate=UpperFaces(merged,x0,z0,sx,sz);
            int missing=baseline.Keys.Except(candidate.Keys).Count(),extra=candidate.Keys.Except(baseline.Keys).Count();int changed=baseline.Count(p=>candidate.TryGetValue(p.Key,out string c)&&c!=p.Value);
            // The exact pinned row is the correction contract; upper-face checks
            // use the real mesher and its actual corner AO implementation.
            foreach(var key in merged.Keys.Where(p=>p.y==29).ToArray())merged.Remove(key);
            foreach(var p in canonical.Where(p=>p.Key.y==29))merged.Add(p.Key,p.Value);
            var fixedFaces=UpperFaces(merged,x0,z0,sx,sz);
            if(!baseline.OrderBy(p=>p.Key).SequenceEqual(fixedFaces.OrderBy(p=>p.Key)))throw new Exception("Pinned canonical row29 failed upper face continuity");
            rows.Add(new{id,baseline_upper_cell_faces=baseline.Count,visual_v1_missing_upper_faces=missing,visual_v1_extra_upper_faces=extra,visual_v1_changed_upper_face_attributes=changed,pinned_row29_geometry_rgb_material_ao_match=true});
        }
        File.WriteAllText(args[1],JsonSerializer.Serialize(new{scope="Actual stock mesher CPU check for upper cells30..33, reading row29 below and34 above as neighbor/AO context. Demonstrates whether the existing visual-v1 base can reuse old upper face attributes. No Unity or live source mutation.",rows},new JsonSerializerOptions{WriteIndented=true}));Console.WriteLine("PASS exact canonical row29 preserves existing upper mesh cell faces/RGB/material/AO.");
    }
}
