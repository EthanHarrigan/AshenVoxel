using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using AshenVoxel.Core;
using AshenVoxel.Rendering;
using RootContactArrayMesher;
using UnityEngine;

static class Checks
{
    static int assertions,chunks,triangles;static readonly List<object> rows=new List<object>();
    static void Check(bool value,string message){assertions++;if(!value)throw new Exception(message);}
    static ChunkInput Input(VoxelWorld world,Int3 coordinate,int cut,string identity="fixture",Action<MaterialId[],uint[]> afterCopy=null)
    {
        var m=new MaterialId[ChunkInput.HaloCells];var c=new uint[m.Length];
        for(int z=-1;z<=16;z++)for(int y=-1;y<=16;y++)for(int x=-1;x<=16;x++)
        {int i=x+1+18*(y+1+18*(z+1)),wx=coordinate.x*16+x,wy=coordinate.y*16+y,wz=coordinate.z*16+z;m[i]=world.Get(wx,wy,wz);c[i]=m[i]==MaterialId.Air?0:world.GetColor(wx,wy,wz);}
        var snapshot=new ChunkInput(coordinate,world.VoxelSize,cut,identity,m,c);afterCopy?.Invoke(m,c);return snapshot;
    }
    static uint Packed(Rgba32 c)=>(uint)(c.r|(c.g<<8)|(c.b<<16)|(c.a<<24));
    static uint Packed(Color32 c)=>(uint)(c.r|(c.g<<8)|(c.b<<16)|(c.a<<24));
    static void Compare(MeshData data,Mesh reference,string label)
    {
        Check(data.VertexCount==reference.vertexCount&&data.IndexCount==reference.triangles.Length,label+" counts");
        Check(data.PayloadBytes==28L*data.VertexCount+4L*data.IndexCount&&data.RetainedBufferBytes==data.PayloadBytes,label+" exact retained output after compaction");
        for(int i=0;i<data.VertexCount;i++)
        {
            for(int d=0;d<3;d++)
            {Check(data.Vertices[i][d]==reference.vertices[i][d]&&data.Normals[i][d]==reference.normals[i][d],label+" exact position/normal");Check(data.Vertices[i][d]>=data.Bounds.Minimum[d]&&data.Vertices[i][d]<=data.Bounds.Maximum[d],label+" bounds");}
            Check(Packed(data.Colors[i])==Packed(reference.colors32[i]),label+" exact RGB/material/AO");
        }
        for(int i=0;i<data.IndexCount;i++)Check(data.Indices[i]==reference.triangles[i],label+" exact AO diagonal/index");
        for(int i=0;i<data.IndexCount;i+=3)
        {
            int a=data.Indices[i],b=data.Indices[i+1],c=data.Indices[i+2];Check(a>=0&&b>=0&&c>=0&&a<data.VertexCount&&b<data.VertexCount&&c<data.VertexCount,"valid triangle indices");
            Float3 av=data.Vertices[a],bv=data.Vertices[b],cv=data.Vertices[c],n=data.Normals[a];
            double x1=bv.x-av.x,y1=bv.y-av.y,z1=bv.z-av.z,x2=cv.x-av.x,y2=cv.y-av.y,z2=cv.z-av.z;
            Check((y1*z2-z1*y2)*n.x+(z1*x2-x1*z2)*n.y+(x1*y2-y1*x2)*n.z>0,label+" nondegenerate outward winding");triangles++;
        }
        Check(data.Bounds.HasVertices==(data.VertexCount>0),label+" empty bound");chunks++;
    }
    static MeshData BuildCompare(VoxelWorld world,Int3 chunk,int cut,string label)
    {
        var baseline=new Mesh();new RootBaseChunkMesher().Build(world,chunk,baseline,cut);
        var capacity=new MeshCapacity();var result=ArrayRootMesher.Build(Input(world,chunk,cut,label),capacity,()=>true);
        Check(result.Status==BuildStatus.Complete&&result.Output!=null,label+" complete");Check(result.CompletedMaskPlanes==102,label+"102 atomic mask planes");
        Compare(result.Output,baseline,label);return result.Output;
    }
    static void Reject(Action action,string label){bool rejected=false;try{action();}catch(ArgumentException){rejected=true;}Check(rejected,label);}
    static void Synthetic()
    {
        var world=new VoxelWorld(33,36,33,.1f,714103);
        for(int z=12;z<21;z++)for(int y=13;y<33;y++)for(int x=12;x<21;x++)if((x+y+z)%7!=0)
            world.Set(x,y,z,(x+y+z)%3==0?MaterialId.Stone:MaterialId.Wood,0xff805a30u+(uint)((x+2*y+z)%5));
        foreach(int cut in new[]{0,15,16,17,29,30,31,32,int.MaxValue})foreach(var chunk in world.Chunks.Keys)BuildCompare(world,chunk,cut,"mixed AO boundaries cut"+cut);
        var one=new VoxelWorld(16,16,16,.1f,1);one.Set(3,4,5,MaterialId.Wood,0xff123456);var key=new Int3(0,0,0);
        var input=Input(one,key,30,"immutable-copy",(m,c)=>{Array.Clear(m,0,m.Length);Array.Clear(c,0,c.Length);});
        var exact=ArrayRootMesher.Build(input,new MeshCapacity(6,816),()=>true);Check(exact.Status==BuildStatus.Complete&&exact.Output.VertexCount==24,"exact capacity and copied immutable halo");
        Check(exact.Output.Identity=="immutable-copy"&&exact.Output.PayloadBytes==816&&exact.Output.RetainedBufferBytes==816,"identity and exact accounted buffers");
        var cap=ArrayRootMesher.Build(input,new MeshCapacity(5,680),()=>true);Check(cap.Status==BuildStatus.CapacityRejected&&cap.Output==null,"cap rejects entire chunk no partial output");
        var zero=ArrayRootMesher.Build(input,new MeshCapacity(0,0),()=>true);Check(zero.Status==BuildStatus.CapacityRejected&&zero.Output==null,"zero-cap nonempty rejection");
        var empty=ArrayRootMesher.Build(Input(new VoxelWorld(1,1,1,.1f,1),key,30),new MeshCapacity(0,0),()=>true);Check(empty.Status==BuildStatus.Complete&&empty.Output.VertexCount==0&&!empty.Output.Bounds.HasVertices,"empty zero-cap success");
        var stale=ArrayRootMesher.Build(input,new MeshCapacity(),()=>false);Check(stale.Status==BuildStatus.Stale&&stale.Output==null&&stale.CompletedMaskPlanes==0,"stale before allocation");
        int calls=0;var late=ArrayRootMesher.Build(input,new MeshCapacity(),()=>++calls<105);Check(late.Status==BuildStatus.Stale&&late.Output==null&&late.CompletedMaskPlanes==102,"stale immediately before publishing fully constructed arrays");
        using(var cts=new CancellationTokenSource())
        {cts.Cancel();var cancelled=ArrayRootMesher.Build(input,new MeshCapacity(),()=>true,cts.Token);Check(cancelled.Status==BuildStatus.Cancelled&&cancelled.Output==null&&cancelled.CompletedMaskPlanes==0,"pre-cancel before allocation");}
        using(var cts=new CancellationTokenSource())
        {calls=0;var cancelled=ArrayRootMesher.Build(input,new MeshCapacity(),()=>{if(++calls==5)cts.Cancel();return true;},cts.Token);Check(cancelled.Status==BuildStatus.Cancelled&&cancelled.Output==null&&cancelled.CompletedMaskPlanes<=4,"mid-chunk cancellation discards all arrays");}
        using(var cts=new CancellationTokenSource())
        {calls=0;var cancelled=ArrayRootMesher.Build(input,new MeshCapacity(),()=>{if(++calls==105)cts.Cancel();return true;},cts.Token);Check(cancelled.Status==BuildStatus.Cancelled&&cancelled.Output==null&&cancelled.CompletedMaskPlanes==102,"cancellation after compaction discards completed output");}
        var failed=ArrayRootMesher.Build(input,new MeshCapacity(),()=>throw new IOException());Check(failed.Status==BuildStatus.Failed&&failed.Output==null,"freshness exception fails closed");
        var retained=exact.Output;uint old=Packed(retained.Colors[0]);BuildCompare(world,world.Chunks.Keys.First(),30,"later independent worker");Check(Packed(retained.Colors[0])==old&&retained.VertexCount==24,"prior immutable output survives later build");
        var compact=ArrayRootMesher.Build(input,new MeshCapacity(),()=>true);Check(compact.Output.RetainedBufferBytes==816&&new MeshCapacity().MaximumLiveElementBytes==1114112,"oversized scratch compacts with explicit worst-case copy allowance");
        var copy=retained.Vertices[0];copy.x+=100;Check(retained.Vertices[0].x!=copy.x,"value access cannot mutate stored vertices");
        Reject(()=>new MeshCapacity(7,816),"byte reservation validation");Reject(()=>new MeshCapacity(MeshCapacity.MaximumQuads+1,long.MaxValue),"absolute quad ceiling");
        var mh=new MaterialId[ChunkInput.HaloCells];var ch=new uint[mh.Length];int center=4+18*(5+18*6);mh[center]=MaterialId.Wood;ch[center]=0xff123456;
        Reject(()=>new ChunkInput(key,float.NaN,30,"x",mh,ch),"nonfinite voxel size");Reject(()=>new ChunkInput(new Int3(int.MaxValue,0,0),.1f,30,"x",mh,ch),"chunk coordinate overflow");
        Reject(()=>new ChunkInput(key,.1f,30,"x",new MaterialId[1],ch),"incomplete halo");ch[center]=0x00123456;Reject(()=>new ChunkInput(key,.1f,30,"x",mh,ch),"nonopaque pigment rejected");ch[center]=0xff123456;
        var negative=new ChunkInput(new Int3(-1,-1,-1),.1f,30,"negative",mh,ch);var n=ArrayRootMesher.Build(negative,new MeshCapacity(),()=>true);Check(n.Status==BuildStatus.Complete&&n.Output.Bounds.Maximum.x<0&&n.Output.Bounds.Maximum.y<0&&n.Output.Bounds.Maximum.z<0,"negative lattice chunks supported");
    }
    static void Actual(string directory,int id)
    {
        foreach(int factor in new[]{1,2,4,16,64})
        {
            string cellPath=Path.Combine(directory,"specimen-"+id,"cells-F"+factor+".bin");using var cells=new BinaryReader(File.OpenRead(cellPath));
            Check(cells.ReadInt32()==0x31434352&&cells.ReadInt32()==factor,"captured cell header");int cut=cells.ReadInt32();int sx=cells.ReadInt32(),sy=cells.ReadInt32(),sz=cells.ReadInt32(),count=cells.ReadInt32();
            var world=new VoxelWorld(sx,sy,sz,.1f*factor,714103);for(int i=0;i<count;i++)world.Set(cells.ReadInt16(),cells.ReadInt16(),cells.ReadInt16(),(MaterialId)cells.ReadByte(),cells.ReadUInt32());Check(cells.BaseStream.Position==cells.BaseStream.Length,"cell EOF");
            using var expected=new BinaryReader(File.OpenRead(Path.Combine(directory,"specimen-"+id,"lower-F"+factor+".bin")));Check(expected.ReadInt32()==0x314d4352,"frozen mesh header");int parts=expected.ReadInt32();long payload=0,retained=0;int vertexTotal=0,indexTotal=0,maxChunkQuads=0;
            for(int part=0;part<parts;part++)
            {
                var chunk=new Int3(expected.ReadInt32(),expected.ReadInt32(),expected.ReadInt32());int vc=expected.ReadInt32(),ic=expected.ReadInt32();
                var output=BuildCompare(world,chunk,1024/factor+(cut+4)/factor,"actual"+id+"F"+factor+"chunk"+part);
                Check(vc==output.VertexCount&&ic==output.IndexCount,"frozen actual mesh counts");
                for(int i=0;i<vc;i++)
                {
                    for(int d=0;d<3;d++)Check(expected.ReadInt16()==(int)Math.Round(output.Vertices[i][d]/(.1f*factor))*factor-(d==1?1028:235),"frozen actual oriented vertex");
                    var n=output.Normals[i];int normal=n.x>.5?0:n.x<-.5?1:n.y>.5?2:n.y<-.5?3:n.z>.5?4:5;
                    Check(expected.ReadByte()==normal&&expected.ReadUInt32()==Packed(output.Colors[i]),"frozen actual normal RGB material AO");
                }
                for(int i=0;i<ic;i++)Check(expected.ReadInt32()==output.Indices[i],"frozen actual topology");
                payload+=output.PayloadBytes;retained+=output.RetainedBufferBytes;vertexTotal+=vc;indexTotal+=ic;maxChunkQuads=Math.Max(maxChunkQuads,vc/4);
            }
            Check(expected.BaseStream.Position==expected.BaseStream.Length,"mesh EOF");
            rows.Add(new{id,factor,occupied=count,chunks=parts,vertices=vertexTotal,indices=indexTotal,payload_bytes=payload,retained_capacity_bytes=retained,max_chunk_quads=maxChunkQuads});
        }
    }
    static void Main(string[] args)
    {
        Synthetic();Actual(args[0],10);Actual(args[0],11);
        File.WriteAllText(args[1],JsonSerializer.Serialize(new{status="PASS",assertions,chunks,triangles,actual=rows,scope="CPU only: managed-array candidate equals frozen mesher via test-only Mesh shim, and all ten captured composite lower-F binary meshes exactly; no generation, native, GPU or production wiring."},new JsonSerializerOptions{WriteIndented=true}));
        Console.WriteLine("PASS "+assertions+" assertions across "+chunks+" chunk comparisons; actual10LODs +synthetic capacity/stale/cancel/ownership.");
    }
}
