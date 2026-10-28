using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using AshenVoxel.Core;
using AshenVoxel.Core.Trees;
using AshenVoxel.Rendering;
using AshenVoxel.WorldScale;
using CompositeRootCandidate;
using UnityEngine;

static class CompositeChecks
{
    static long checks;static string stage="startup";static readonly Stopwatch clock=Stopwatch.StartNew();
    static readonly MethodInfo reducer=typeof(ExportPrototypes).GetMethod("Reduce",BindingFlags.NonPublic|BindingFlags.Static);
    static readonly Int3 anchor=new Int3(235,4,235);
    static void Check(bool value,string why){checks++;if(!value)throw new Exception(stage+": "+why);}
    static VoxelWorld Reduce(VoxelWorld w,int f)=>f==1?w:(VoxelWorld)reducer.Invoke(null,new object[]{w,f});
    static void Stage(string value){stage=value;Console.WriteLine(value+" t="+clock.Elapsed.TotalSeconds.ToString("F3")+" private="+Process.GetCurrentProcess().PrivateMemorySize64);}
    static uint Packed(Color32 c)=>(uint)c.r|((uint)c.g<<8)|((uint)c.b<<16)|((uint)c.a<<24);
    static Int3 Position(Int3 chunk,int index){var p=Chunk.Decode(index);return new Int3(chunk.x*16+p.x,chunk.y*16+p.y,chunk.z*16+p.z);}
    static RootCell[] ReadRoot(string path,out RootCell[] guard)
    {
        using var r=new BinaryReader(File.OpenRead(path));int n=r.ReadInt32();Check(n>0&&n<=65536,"fixture cap");var rows=new List<RootCell>();var g=new List<RootCell>();
        for(int i=0;i<n;i++){var c=new RootCell(new Int3(r.ReadInt16(),r.ReadInt16(),r.ReadInt16()),r.ReadUInt32());if(c.Position.y==30)g.Add(c);else rows.Add(c);}
        Check(r.BaseStream.Position==r.BaseStream.Length,"exact fixture length");guard=g.ToArray();return rows.ToArray();
    }
    static Template Capture(VoxelWorld original,CanonicalOrder order,int factor,int cut,int seed)
    {
        var best=new Dictionary<Int3,FixedSample>();var pinned=new List<RootCell>();int rank=0,nonwood=0;
        foreach(var pair in original.Chunks)
        {
            for(int i=0;i<Chunk.Volume;i++)
            {
                var m=pair.Value.Cells[i];if(m==MaterialId.Air)continue;var p=Position(pair.Key,i);if(!original.InBounds(p))continue;
                uint color=original.GetColor(p);int localY=p.y-4;
                if(localY==29&&m==MaterialId.Wood)pinned.Add(new RootCell(new Int3(p.x-235,29,p.z-235),color));
                var c=new Int3(p.x/factor,p.y/factor,p.z/factor);if(c.y>(cut+4)/factor||m==MaterialId.Wood&&localY<=28)continue;
                if(m!=MaterialId.Wood)nonwood++;
                if(!best.TryGetValue(c,out var old)||m==MaterialId.Wood&&old.Material!=MaterialId.Wood)best[c]=new FixedSample(c,m,color,rank,i);
            }
            rank++;
        }
        Check(rank==order.Count,"capture original visitation ranks");Console.WriteLine("capture F="+factor+" fixed="+best.Count+" nonwood fine candidates="+nonwood);
        return new Template(factor,cut,seed,"canonical-live-41556b:"+seed+":"+factor,order,best.Values.OrderBy(s=>s.Cell.z).ThenBy(s=>s.Cell.y).ThenBy(s=>s.Cell.x),pinned);
    }
    static void SaveTemplate(string path,Template template)
    {
        using var w=new BinaryWriter(File.Create(path));w.Write(0x314c4352);w.Write(template.Factor);w.Write(template.Cut);w.Write(template.Seed);w.Write(template.Count);
        for(int i=0;i<template.Count;i++){var s=template.At(i);w.Write((short)s.Cell.x);w.Write((short)s.Cell.y);w.Write((short)s.Cell.z);w.Write((byte)s.Material);w.Write(s.Color);w.Write(s.Rank);w.Write((ushort)s.Index);}
    }
    static VoxelWorld Reference(VoxelWorld canonical,CanonicalOrder order,RootCell[] root)
    {
        var changed=new Dictionary<Int3,uint>();foreach(var c in root)if(c.Position.y<=28)changed.Add(new Int3(c.Position.x+235,c.Position.y+4,c.Position.z+235),c.Color);
        var newChunks=new HashSet<Int3>();foreach(var p in changed.Keys){var c=new Int3(VoxelWorld.FloorDiv(p.x,16),VoxelWorld.FloorDiv(p.y,16),VoxelWorld.FloorDiv(p.z,16));if(order.Rank(c)<0)newChunks.Add(c);}
        var reference=new VoxelWorld(470,978+Job.Shift,470,.1f,canonical.Seed);
        void CopyChunk(Int3 chunk)
        {
            for(int i=0;i<4096;i++)
            {
                var p=Position(chunk,i);var m=canonical.Get(p);uint color=canonical.GetColor(p);
                if(m==MaterialId.Wood&&p.y-4<=28){m=MaterialId.Air;color=0;}
                if(changed.TryGetValue(p,out uint rootColor)){m=MaterialId.Wood;color=rootColor;}
                if(m!=MaterialId.Air)Check(reference.Set(p.x,p.y+Job.Shift,p.z,m,color),"reference Set accepts original/extended cell");
            }
        }
        foreach(var entry in order.Entries)CopyChunk(entry.Key);
        foreach(var chunk in newChunks.OrderBy(c=>c.z).ThenBy(c=>c.y).ThenBy(c=>c.x))CopyChunk(chunk);
        return reference;
    }
    static void VerifyPackChunk(Mesh mesh,PrototypeMeshData packed,ref int vertexAt,ref int indexAt)
    {
        for(int i=0;i<mesh.vertexCount;i++)
        {
            Check(vertexAt+i<packed.VertexCount,"pack has generated vertex");var v=mesh.vertices[i];var n=mesh.normals[i];
            for(int a=0;a<3;a++){float av=a==0?23.5f:a==1?.4f:23.5f;Check((short)Math.Round((v[a]-av)*10)==packed.Positions[(vertexAt+i)*3+a],"exact original packed position");}
            byte normal=(byte)(n.x>.5?0:n.x<-.5?1:n.y>.5?2:n.y<-.5?3:n.z>.5?4:5);
            Check(normal==packed.Normals[vertexAt+i]&&Packed(mesh.colors32[i])==packed.Colors[vertexAt+i],"exact packed normal/material/RGB/AO");
        }
        foreach(int index in mesh.triangles)Check(packed.Indices[indexAt++]==vertexAt+index,"exact packed triangle index/winding");vertexAt+=mesh.vertexCount;
    }
    static Dictionary<(int,int,int,int),(uint,uint,uint,uint)> Atoms(Mesh mesh,float voxelSize,int yShift,int firstUpper,bool upper)
    {
        var atoms=new Dictionary<(int,int,int,int),(uint,uint,uint,uint)>();
        for(int q=0;q<mesh.vertexCount;q+=4)
        {
            var normal=mesh.normals[q];int axis=normal.x!=0?0:normal.y!=0?1:2;bool positive=normal[axis]>0;int n=axis*2+(positive?0:1),u=(axis+1)%3,v=(axis+2)%3;
            int P(int i,int a)=>(int)Math.Round(mesh.vertices[i][a]/voxelSize)-(a==1?yShift:0);
            int plane=P(q,axis),u0=P(q,u),u1=P(q+1,u),v0=P(q,v),v1=P(q+3,v);
            var colors=(Packed(mesh.colors32[q]),Packed(mesh.colors32[q+1]),Packed(mesh.colors32[q+2]),Packed(mesh.colors32[q+3]));
            bool uniform=colors.Item1==colors.Item2&&colors.Item1==colors.Item3&&colors.Item1==colors.Item4;
            Check(uniform||u1-u0==1&&v1-v0==1,"nonuniform AO retains coarse unit corners");
            for(int y=v0;y<v1;y++)for(int x=u0;x<u1;x++)
            {
                int ownerY=axis==1?(positive?plane-1:plane):u==1?x:y;if((ownerY>=firstUpper)!=upper)continue;
                Check(atoms.TryAdd((n,plane,x,y),colors),"unit face has exactly one owner");
            }
        }
        return atoms;
    }
    static void EqualAtoms(Dictionary<(int,int,int,int),(uint,uint,uint,uint)> a,Dictionary<(int,int,int,int),(uint,uint,uint,uint)> b,string label)
    {Check(a.Count==b.Count,label+" count");foreach(var p in a)Check(b.TryGetValue(p.Key,out var value)&&value.Equals(p.Value),label+" exact oriented coverage/material/RGB/AO");}
    static void Finish(Job j,int records,int faces,int mesh)
    {int calls=0;while(!j.Terminal){j.Step(records,faces,mesh,"fixture-v2");Check(++calls<2000000,"bounded test continuation");}Check(j.State==Phase.Complete,"job complete: "+j.Failure);}
    static string SaveMeshes(string path,IReadOnlyList<(Int3 coordinate,Mesh mesh)> meshes,int factor,out int vertices,out int indices)
    {
        vertices=0;indices=0;using(var w=new BinaryWriter(File.Create(path)))
        {
            w.Write(0x314d4352);w.Write(meshes.Count);
            foreach(var part in meshes.OrderBy(m=>m.coordinate.z).ThenBy(m=>m.coordinate.y).ThenBy(m=>m.coordinate.x))
            {
                var m=part.mesh;w.Write(part.coordinate.x);w.Write(part.coordinate.y);w.Write(part.coordinate.z);w.Write(m.vertexCount);w.Write(m.triangles.Length);
                for(int i=0;i<m.vertexCount;i++)
                {
                    for(int a=0;a<3;a++){int c=(int)Math.Round(m.vertices[i][a]/(.1f*factor))*factor-(a==0?235:a==1?Job.Shift+4:235);w.Write(checked((short)c));}
                    var n=m.normals[i];w.Write((byte)(n.x>.5?0:n.x<-.5?1:n.y>.5?2:n.y<-.5?3:n.z>.5?4:5));w.Write(Packed(m.colors32[i]));
                }
                foreach(int index in m.triangles)w.Write(index);vertices+=m.vertexCount;indices+=m.triangles.Length;
            }
        }
        using var stream=File.OpenRead(path);using var hash=SHA256.Create();return Convert.ToHexString(hash.ComputeHash(stream)).ToLowerInvariant();
    }
    static object Lod(VoxelWorld canonical,VoxelWorld reference,ExplorationPrototype packed,Template template,RootCell[] root,string output)
    {
        int f=template.Factor,shift=Job.Shift/f,first=template.FirstUpper;Stage("LOD "+f+" reduce/reference");var timer=Stopwatch.StartNew();
        var original=Reduce(canonical,f);var full=Reduce(reference,f);
        var j=new Job(template,root,"fixture-v2");j.Step(0,0,0,"fixture-v2");Check(j.FixedCandidates==0&&j.RootCandidates==0&&j.PublishedWorld==null,"zero budgets retain all work");
        Finish(j,37,53,1);double steppedMs=timer.Elapsed.TotalMilliseconds;timer.Restart();
        int referenceCount=0;foreach(var pair in full.Chunks)for(int i=0;i<4096;i++)
        {var m=pair.Value.Cells[i];if(m==MaterialId.Air)continue;var p=Position(pair.Key,i);if(p.y>shift+first)continue;referenceCount++;Check(j.PublishedWorld.Get(p)==m&&j.PublishedWorld.GetColor(p)==full.GetColor(p),"candidate exact original-reducer representative");}
        Check(referenceCount==j.OccupiedCount,"no extra/missing coarse candidate cells including complete halo");
        var large=new Job(template,root.Reverse().ToArray(),"fixture-v2");Finish(large,int.MaxValue,int.MaxValue,1);
        string hash=SaveMeshes(Path.Combine(output,"lower-F"+f+".bin"),j.PublishedMeshes,f,out int vertices,out int indices);
        string reverseHash=SaveMeshes(Path.Combine(output,"reverse-F"+f+".bin"),large.PublishedMeshes,f,out int reverseVertices,out int reverseIndices);
        Check(hash==reverseHash&&vertices==reverseVertices&&indices==reverseIndices,"budget and root enumeration order do not change lower mesh bytes");
        var cancel=new Job(template,root,"fixture-v2");cancel.Step(1,0,0,"fixture-v2");cancel.Cancel();Check(cancel.State==Phase.Cancelled&&cancel.PublishedWorld==null&&cancel.PublishedMeshes==null,"cancel has no publication");
        var stale=new Job(template,root,"fixture-v2");stale.Step(1,0,0,"different");Check(stale.State==Phase.Rejected&&stale.Failure=="StaleSource"&&stale.PublishedWorld==null,"stale source fails closed");
        int lod=Array.IndexOf(new[]{1,2,4,16,64},f);var pack=packed.Mesh(lod);var mesher=new GreedyChunkMesher();var masked=new RootBaseChunkMesher();var aMesh=new Mesh();var bMesh=new Mesh();var lowerMesh=new Mesh();int vertexAt=0,indexAt=0;long upperAtoms=0,lowerAtoms=0;
        var keys=new HashSet<Int3>(original.Chunks.Keys);foreach(var key in full.Chunks.Keys)keys.Add(new Int3(key.x,key.y-shift/16,key.z));
        var lowerByChunk=j.PublishedMeshes.ToDictionary(p=>p.coordinate,p=>p.mesh);
        Stage("LOD "+f+" streaming packed/upper/lower comparison");
        foreach(var key in keys.OrderBy(p=>p.z).ThenBy(p=>p.y).ThenBy(p=>p.x))
        {
            mesher.Build(original,key,aMesh);if(original.Chunks.ContainsKey(key))VerifyPackChunk(aMesh,pack,ref vertexAt,ref indexAt);
            var shifted=new Int3(key.x,key.y+shift/16,key.z);mesher.Build(full,shifted,bMesh);
            var upperOriginal=Atoms(aMesh,original.VoxelSize,0,first,true);var upperReference=Atoms(bMesh,full.VoxelSize,shift,first,true);
            EqualAtoms(upperOriginal,upperReference,"shared upper");upperAtoms+=upperOriginal.Count;
            var lowerReference=Atoms(bMesh,full.VoxelSize,shift,first,false);
            if(lowerByChunk.TryGetValue(shifted,out var actual))
            {
                var lowerActual=Atoms(actual,j.PublishedWorld.VoxelSize,shift,first,false);EqualAtoms(lowerActual,lowerReference,"derived lower");lowerAtoms+=lowerActual.Count;
                Check(Atoms(actual,j.PublishedWorld.VoxelSize,shift,first,true).Count==0,"lower cache never owns shared upper faces");
                masked.Build(full,shifted,lowerMesh,shift+first);
                Check(actual.vertexCount==lowerMesh.vertexCount&&actual.triangles.SequenceEqual(lowerMesh.triangles),"exact reference masked topology/winding");
                for(int v=0;v<actual.vertexCount;v++)Check(actual.vertices[v].Equals(lowerMesh.vertices[v])&&actual.normals[v].Equals(lowerMesh.normals[v])&&Packed(actual.colors32[v])==Packed(lowerMesh.colors32[v]),"exact reference lower vertex attributes");
            }
            else Check(lowerReference.Count==0,"no omitted reference lower chunk");
        }
        Check(vertexAt==pack.VertexCount&&indexAt==pack.Indices.Length,"all original packed vertices/indices matched exactly");
        double comparisonMs=timer.Elapsed.TotalMilliseconds;
        return new{factor=f,cut_cells=template.Cut,shared_fixed_records=template.Count,shared_fixed_file_bytes=new FileInfo(Path.Combine(output,"fixed-F"+f+".bin")).Length,original_pack_vertices=vertexAt,original_pack_indices=indexAt,coarse_cells=j.OccupiedCount,coarse_chunks=j.ChunkCount,raw_lower_faces=j.RawFaces,lower_vertices=vertices,lower_indices=indices,lower_gpu_arithmetic_bytes=vertices*28L+indices*4L,lower_artifact_bytes=new FileInfo(Path.Combine(output,"lower-F"+f+".bin")).Length,lower_mesh_sha256=hash,unchanged_upper_face_atoms=upperAtoms,matching_lower_face_atoms=lowerAtoms,fixed_candidates=j.FixedCandidates,root_candidates=j.RootCandidates,face_candidates=j.FaceCandidates,mesh_chunk_calls=j.MeshChunkCalls,atomic_mesher_mask_candidates_per_chunk=26112,stepped_with_reference_reduction_ms=steppedMs,remaining_verification_ms=comparisonMs,private_bytes_checkpoint=Process.GetCurrentProcess().PrivateMemorySize64};
    }
    static void Main(string[] args)
    {
        string output=args[0],rootPath=args[1],packPath=args[2];int id=int.Parse(args[3]);var results=new List<object>();
        try
        {
            Check(id==10||id==11,"one authorized mature specimen");Stage("read bounded root");var root=ReadRoot(rootPath,out var guard);int seed=id==10?714103:271828;
            Stage("generate one original mature tree");var p=TreePresets.Get(TreeSpecies.Redwood).With(age:1);var world=new VoxelWorld(470,978,470,.1f,seed);var generate=Stopwatch.StartNew();
            var generated=TreeGenerator.Generate(world,p,23.5f,.4f,23.5f,seed,new TreeGenerationBudget(8192,160000000,16384));
            Check(!generated.Truncated,"original generation not truncated");Check(generated.WoodCells==(id==10?380993:411904)&&generated.LeafCells==(id==10?730110:747735),"historical full wood/foliage cell totals");double generationMs=generate.Elapsed.TotalMilliseconds;
            Stage("capture shared canonical summaries");var order=new CanonicalOrder(world.Chunks.Keys);var templates=new List<Template>();int[] factors={1,2,4,16,64},cuts={30,32,36,60,124};
            foreach(var c in root.Concat(guard).Where(c=>c.Position.y>=29))Check(world.Get(c.Position.x+235,c.Position.y+4,c.Position.z+235)==MaterialId.Wood&&world.GetColor(c.Position.x+235,c.Position.y+4,c.Position.z+235)==c.Color,"fixture pinned/guard rows exactly canonical");
            using(var w=new BinaryWriter(File.Create(Path.Combine(output,"canonical-order.bin")))){w.Write(order.Count);foreach(var e in order.Entries){w.Write((short)e.Key.x);w.Write((short)e.Key.y);w.Write((short)e.Key.z);w.Write(e.Value);}}
            for(int lod=0;lod<5;lod++){var t=Capture(world,order,factors[lod],cuts[lod],seed);templates.Add(t);SaveTemplate(Path.Combine(output,"fixed-F"+factors[lod]+".bin"),t);}
            Stage("construct one canonical-order composite reference");var reference=Reference(world,order,root);GC.Collect();GC.WaitForPendingFinalizers();
            ExplorationPrototype packed=null;using(var reader=new ExplorationPrototypeReader(File.ReadAllBytes(packPath)))while(reader.TryRead(out var record)){if(record.Id==id){packed=record;break;}record.ReleaseMeshData();GC.Collect();}
            Check(packed!=null&&packed.LodCount==5,"actual packed mature specimen loaded");
            foreach(var t in templates){results.Add(Lod(world,reference,packed,t,root,output));GC.Collect();GC.WaitForPendingFinalizers();}
            packed.ReleaseMeshData();Stage("complete");
            File.WriteAllText(Path.Combine(output,"result.json"),JsonSerializer.Serialize(new{status="PASS",id,checks,generation_ms=generationMs,total_seconds=clock.Elapsed.TotalSeconds,root_owned_cells=root.Length,root_authoritative_max_y=root.Max(c=>c.Position.y),canonical_chunk_ranks=order.Count,canonical_order_file_bytes=new FileInfo(Path.Combine(output,"canonical-order.bin")).Length,source_wood_cells=generated.WoodCells,source_leaf_cells=generated.LeafCells,lods=results,scope="One original full tree generated transiently for canonical capture/reference. Runtime candidate stores shared pre-reduced fixed representatives/order and bounded lower root/coarse caches. Exact actual packed original meshes and actual reducer/mesher comparison; no Unity/native or production integration."},new JsonSerializerOptions{WriteIndented=true}));
        }
        catch(Exception e){File.WriteAllText(Path.Combine(output,"failure.json"),JsonSerializer.Serialize(new{status="FAIL",stage,checks,total_seconds=clock.Elapsed.TotalSeconds,error=e.ToString(),completed_lods=results},new JsonSerializerOptions{WriteIndented=true}));Console.Error.WriteLine(e);Environment.ExitCode=1;}
    }
}
