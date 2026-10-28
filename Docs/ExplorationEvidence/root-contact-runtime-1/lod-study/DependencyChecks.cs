using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using AshenVoxel.Core;
using AshenVoxel.Rendering;
using UnityEngine;

static class DependencyChecks
{
    static int checks;
    static void Check(bool value,string message){if(!value)throw new Exception(message);checks++;}
    static readonly MethodInfo ReduceMethod=typeof(ExportPrototypes).GetMethod("Reduce",BindingFlags.Static|BindingFlags.NonPublic);
    static VoxelWorld Reduce(VoxelWorld source,int factor)=>factor==1?source:(VoxelWorld)ReduceMethod.Invoke(null,new object[]{source,factor});
    static VoxelWorld World()=>new VoxelWorld(256,320,256,.1f,714103);
    static Dictionary<Int3,(MaterialId,uint)> Cells(VoxelWorld world,int minimumY=0)
    {
        var cells=new Dictionary<Int3,(MaterialId,uint)>();
        foreach(var pair in world.Chunks)for(int i=0;i<Chunk.Volume;i++)
        {if(pair.Value.Cells[i]==MaterialId.Air)continue;var local=Chunk.Decode(i);var p=new Int3(pair.Key.x*16+local.x,pair.Key.y*16+local.y,pair.Key.z*16+local.z);if(p.y>=minimumY)cells.Add(p,(world.Get(p),world.GetColor(p)));}
        return cells;
    }
    static bool Same<TKey,TValue>(Dictionary<TKey,TValue> a,Dictionary<TKey,TValue> b)=>a.Count==b.Count&&a.All(p=>b.TryGetValue(p.Key,out var value)&&EqualityComparer<TValue>.Default.Equals(p.Value,value));
    static uint Packed(Color32 c)=>(uint)c.r|((uint)c.g<<8)|((uint)c.b<<16)|((uint)c.a<<24);
    // Expand each ordinary greedy quad to its coarse unit-face atoms. Uniform-AO
    // merged faces are constant; nonuniform faces must remain one cell wide/high.
    // This compares visible coverage/shading, not greedy partition array identity.
    static Dictionary<string,string> Surface(VoxelWorld world,int firstUpperCell)
    {
        var result=new Dictionary<string,string>();var mesher=new GreedyChunkMesher();var mesh=new Mesh();
        foreach(var chunk in world.Chunks.OrderBy(p=>p.Key.z).ThenBy(p=>p.Key.y).ThenBy(p=>p.Key.x))
        {
            mesher.Build(world,chunk.Key,mesh);
            for(int q=0;q<mesh.vertexCount;q+=4)
            {
                var normal=mesh.normals[q];int axis=normal.x!=0?0:normal.y!=0?1:2;bool positive=normal[axis]>0;int n=axis*2+(positive?0:1),u=(axis+1)%3,v=(axis+2)%3;
                int P(int vertex,int a)=>(int)Math.Round(mesh.vertices[vertex][a]/world.VoxelSize);
                int plane=P(q,axis),u0=P(q,u),u1=P(q+1,u),v0=P(q,v),v1=P(q+3,v);
                uint c0=Packed(mesh.colors32[q]),c1=Packed(mesh.colors32[q+1]),c2=Packed(mesh.colors32[q+2]),c3=Packed(mesh.colors32[q+3]);
                bool uniform=c0==c1&&c0==c2&&c0==c3;
                Check(uniform||(u1-u0==1&&v1-v0==1),"actual mesher preserves nonuniform AO unit faces");
                for(int yy=v0;yy<v1;yy++)for(int xx=u0;xx<u1;xx++)
                {
                    int ownerY=axis==1?(positive?plane-1:plane):u==1?xx:yy;if(ownerY<firstUpperCell)continue;
                    string key=n+"/"+plane+"/"+xx+"/"+yy;string values=c0+","+c1+","+c2+","+c3;
                    Check(!result.ContainsKey(key),"each oriented coarse face atom has one owner");result.Add(key,values);
                }
            }
        }
        return result;
    }
    static VoxelWorld Pattern(int factor,int pattern)
    {
        var w=World();int lastChanged=32/factor;
        for(int z=0;z<4;z++)for(int y=Math.Max(0,lastChanged-2);y<=lastChanged+3;y++)for(int x=0;x<4;x++)
        {
            if((x*17+y*31+z*7+pattern)%5==0)continue;
            var material=(x+y+z+pattern)%3==0?MaterialId.Leaves:MaterialId.Wood;
            w.Set(x*factor,y*factor,z*factor,material,0xff000000u|(uint)((x*73+y*39+z*89+pattern)*17431&0xffffff));
        }
        return w;
    }
    static void Main(string[] args)
    {
        int[] factors={1,2,4,16,64},cuts={30,32,36,60,124};var rows=new List<object>();var counterexamples=new List<object>();
        for(int lod=0;lod<5;lod++)
        {
            int factor=factors[lod],lastChanged=32/factor,firstUpper=(cuts[lod]+4)/factor;
            Check((cuts[lod]+4)%factor==0&&firstUpper==lastChanged+2,"cut is lattice-aligned with one full unchanged coarse-cell halo");
            for(int pattern=0;pattern<8;pattern++)
            {
                var a=Pattern(factor,pattern);var b=Pattern(factor,pattern);
                foreach(var p in Cells(b).Keys.ToArray())if(p.y<=32)b.Set(p,MaterialId.Air);
                for(int z=0;z<4;z++)for(int x=0;x<4;x++)if((x+z+pattern)%2==0)b.Set(x*factor,32,z*factor,MaterialId.Stone,0xff445566);
                Check(Same(Cells(a,33),Cells(b,33)),"all authored fine cells/tints above changedY remain identical");
                var aa=Reduce(a,factor);var bb=Reduce(b,factor);
                Check(Same(Cells(aa,lastChanged+1),Cells(bb,lastChanged+1)),"complete buffer and upper coarse representatives remain identical under lower-only edits");
                var before=Surface(aa,firstUpper);var after=Surface(bb,firstUpper);Check(before.Count>0&&Same(before,after),"buffered upper geometry/material/RGB/AO face atoms unchanged");
                rows.Add(new{lod,factor,cut_cells=cuts[lod],pattern,changed_fine_max=28,source_anchor_y=4,last_changed_coarse_cell=lastChanged,buffer_coarse_cell=lastChanged+1,first_shared_upper_coarse_cell=firstUpper,buffer_fine_y_min=(lastChanged+1)*factor-4,buffer_fine_y_max=(lastChanged+2)*factor-5,upper_face_atoms=before.Count,exact=true});
            }
            // Removing the full unchanged coarse layer permits a lower edit to
            // alter the first upper face's corner AO even with unchanged material.
            var c=World();var d=World();foreach(var w in new[]{c,d}){w.Set(factor,(lastChanged+1)*factor,factor,MaterialId.Wood,0xff887766);w.Set(factor,(lastChanged+2)*factor,factor,MaterialId.Wood,0xff887766);}
            c.Set(factor*2,32,factor,MaterialId.Wood,0xff887766);
            var cc=Reduce(c,factor);var dd=Reduce(d,factor);var unsafeBefore=Surface(cc,lastChanged+1);var unsafeAfter=Surface(dd,lastChanged+1);
            Check(!Same(unsafeBefore,unsafeAfter),"no-buffer cut allows lower edits to change upper AO");
            var safeBefore=Surface(cc,firstUpper);var safeAfter=Surface(dd,firstUpper);Check(Same(safeBefore,safeAfter),"same AO counterexample is isolated by the proposed full coarse buffer");
            counterexamples.Add(new{kind="one-layer-too-low AO",lod,factor,unsafe_cut_cells=(lastChanged+1)*factor-4,proposed_cut_cells=cuts[lod],changed_upper_atoms=unsafeBefore.Keys.Union(unsafeAfter.Keys).Count(k=>!unsafeBefore.TryGetValue(k,out var av)||!unsafeAfter.TryGetValue(k,out var bv)||av!=bv)});
        }
        foreach(bool sameMaterial in new[]{true,false})
        {
            var a=World();var b=World();var left=new Int3(1,128,1);var right=new Int3(17,128,1);MaterialId m1=sameMaterial?MaterialId.Wood:MaterialId.Leaves,m2=sameMaterial?MaterialId.Wood:MaterialId.Fruit;
            a.Set(left,m1,0xffff0000);a.Set(right,m2,0xff0000ff);b.Set(right,m2,0xff0000ff);b.Set(left,m1,0xffff0000);
            Check(Same(Cells(a),Cells(b)),"reconstruction counterexample has exactly identical fine material/tint data");
            var aa=Reduce(a,64);var bb=Reduce(b,64);var cell=new Int3(0,2,0);
            Check(aa.Get(cell)!=bb.Get(cell)||aa.GetColor(cell)!=bb.GetColor(cell),"actual baker representative depends on cross-chunk enumeration order");
            Check(!Same(Surface(aa,2),Surface(bb,2)),"same fine field, reordered chunks alter shared upper surface attributes at cut124");
            counterexamples.Add(new{kind=sameMaterial?"same Wood, different upper RGB":"non-Wood representative material changes",lod=4,factor=64,cut_cells=124,first_material=aa.Get(cell).ToString(),reordered_material=bb.Get(cell).ToString(),first_color=aa.GetColor(cell).ToString("x8"),reordered_color=bb.GetColor(cell).ToString("x8"),fine_fields_identical=true});
        }
        File.WriteAllText(args[0],JsonSerializer.Serialize(new{status="PASS",checks,scope="Actual source-matched ExportPrototypes.Reduce called directly by reflection and actual GreedyChunkMesher with original numerical Unity substitutes. Synthetic fine fields test locality and meaningful counterexamples; no tree rebake, native render, composite root generator or production writes.",cases=rows,counterexamples},new JsonSerializerOptions{WriteIndented=true}));Console.WriteLine("PASS "+checks+" dependency/actual-baker assertions;40 lower-change cases and7 counterexamples.");
    }
}
