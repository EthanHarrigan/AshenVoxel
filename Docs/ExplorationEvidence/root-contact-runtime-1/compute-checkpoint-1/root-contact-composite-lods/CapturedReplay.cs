using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using AshenVoxel.Core;
using CompositeRootCandidate;

static class CapturedReplay
{
    static void Require(bool b,string why){if(!b)throw new Exception(why);}
    static FixedSample[] Samples(string path,out int factor,out int cut,out int seed)
    {
        using var r=new BinaryReader(File.OpenRead(path));Require(r.ReadInt32()==0x314c4352,"sample magic");factor=r.ReadInt32();cut=r.ReadInt32();seed=r.ReadInt32();int count=r.ReadInt32();Require(count>=0&&count<=65536,"sample count");var a=new FixedSample[count];
        for(int i=0;i<count;i++)a[i]=new FixedSample(new Int3(r.ReadInt16(),r.ReadInt16(),r.ReadInt16()),(MaterialId)r.ReadByte(),r.ReadUInt32(),r.ReadInt32(),r.ReadUInt16());Require(r.BaseStream.Position==r.BaseStream.Length,"sample length");return a;
    }
    static void Main(string[] args)
    {
        string input=args[0],fixture=args[1],output=args[2];var chunks=new List<Int3>();
        using(var r=new BinaryReader(File.OpenRead(Path.Combine(input,"canonical-order.bin")))){int n=r.ReadInt32();Require(n>=0&&n<=8192,"order count");for(int i=0;i<n;i++){chunks.Add(new Int3(r.ReadInt16(),r.ReadInt16(),r.ReadInt16()));Require(r.ReadInt32()==i,"order rank");}Require(r.BaseStream.Position==r.BaseStream.Length,"order length");}
        var order=new CanonicalOrder(chunks);var roots=new List<RootCell>();using(var r=new BinaryReader(File.OpenRead(fixture))){int n=r.ReadInt32();Require(n>0&&n<=65536,"root count");for(int i=0;i<n;i++){var c=new RootCell(new Int3(r.ReadInt16(),r.ReadInt16(),r.ReadInt16()),r.ReadUInt32());Require(c.Position.y<=30,"root maximum");if(c.Position.y<=29)roots.Add(c);}Require(r.BaseStream.Position==r.BaseStream.Length,"root length");}
        var pinned=Samples(Path.Combine(input,"fixed-F1.bin"),out _,out _,out _).Where(s=>s.Cell.y==33&&s.Material==MaterialId.Wood).Select(s=>new RootCell(new Int3(s.Cell.x-235,29,s.Cell.z-235),s.Color)).ToArray();var rows=new List<object>();
        using var report=JsonDocument.Parse(File.ReadAllText(Path.Combine(input,"result.json")));
        foreach(int f in new[]{1,2,4,16,64})
        {
            var samples=Samples(Path.Combine(input,"fixed-F"+f+".bin"),out int factor,out int cut,out int seed);Require(factor==f,"factor");var t=new Template(f,cut,seed,"frozen-captured-replay",order,samples,pinned);var j=new Job(t,roots.ToArray(),"replay");int steps=0;
            while(!j.Terminal){j.Step(1000,1000,1,"replay");Require(++steps<10000,"replay steps");}Require(j.State==Phase.Complete,"replay failed "+j.Failure);
            string expected=report.RootElement.GetProperty("lods").EnumerateArray().Single(e=>e.GetProperty("factor").GetInt32()==f).GetProperty("lower_mesh_sha256").GetString();
            var method=typeof(CompositeChecks).GetMethod("SaveMeshes",BindingFlags.NonPublic|BindingFlags.Static);object[] call={Path.Combine(output,"lower-F"+f+".bin"),j.PublishedMeshes,f,0,0};string actual=(string)method.Invoke(null,call);Require(actual==expected,"captured replay lower mesh differs from frozen full-baker proof");
            string path=Path.Combine(output,"cells-F"+f+".bin");var world=j.PublishedWorld;using(var w=new BinaryWriter(File.Create(path)))
            {
                w.Write(0x31434352);w.Write(f);w.Write(cut);w.Write(world.SizeX);w.Write(world.SizeY);w.Write(world.SizeZ);w.Write(j.OccupiedCount);
                foreach(var pair in world.Chunks.OrderBy(p=>p.Key.z).ThenBy(p=>p.Key.y).ThenBy(p=>p.Key.x))for(int i=0;i<4096;i++)
                {var m=pair.Value.Cells[i];if(m==MaterialId.Air)continue;var p=Chunk.Decode(i);int x=pair.Key.x*16+p.x,y=pair.Key.y*16+p.y,z=pair.Key.z*16+p.z;w.Write((short)x);w.Write((short)y);w.Write((short)z);w.Write((byte)m);w.Write(world.GetColor(x,y,z));}
            }
            rows.Add(new{factor=f,voxel_size_m=world.VoxelSize,local_cut_decimeters=cut,exclusive_owner_cut_y=Job.Shift/f+(cut+4)/f,grid_dimensions=new[]{world.SizeX,world.SizeY,world.SizeZ},occupied_cells=j.OccupiedCount,cell_file=Path.GetFileName(path),cell_file_bytes=new FileInfo(path).Length,lower_mesh_sha256=actual,conversion="local decimeter vertex = round(world-grid vertex) * factor - (235,1028,235); normal codes +X,-X,+Y,-Y,+Z,-Z; packed color bytes R,G,B,material/AO"});
        }
        File.WriteAllText(Path.Combine(output,"replay.json"),JsonSerializer.Serialize(new{status="PASS",scope="Captured summary/order/root replay only; no generator, whole tree, pack decode, native or shader execution. All replayed lower mesh hashes match frozen full-baker proof.",lods=rows},new JsonSerializerOptions{WriteIndented=true}));Console.WriteLine("PASS captured replay "+input);
    }
}
