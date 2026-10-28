using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using AshenVoxel.WorldScale;
static class PackedInteractionSeam
{
    static void Main(string[] args)
    {
        using var audit=JsonDocument.Parse(File.ReadAllText(args[2]));var expected=new Dictionary<int,Dictionary<int,int>>();
        foreach(var row in audit.RootElement.GetProperty("results").EnumerateArray())
        {
            var counts=new Dictionary<int,int>();foreach(var y in row.GetProperty("upper_rows").EnumerateArray())
            {
                int n=y.GetProperty("cells").GetInt32();if(y.GetProperty("original_hard").GetInt32()!=n||y.GetProperty("composite_hard").GetInt32()!=n||y.GetProperty("changed_roles").GetInt32()!=0)throw new Exception("Audit is not all-hard/unchanged");counts.Add(y.GetProperty("y").GetInt32(),n);
            }
            expected.Add(row.GetProperty("id").GetInt32(),counts);
        }
        var rows=new List<object>();using var reader=new ExplorationPrototypeReader(File.ReadAllBytes(args[0]));
        while(reader.TryRead(out var prototype))
        {
            if(!expected.TryGetValue(prototype.Id,out var counts)){prototype.ReleaseMeshData();continue;}
            var checkedCounts=new Dictionary<int,int>();int bodyChecks=0,resistanceChecks=0;
            using var data=new BinaryReader(File.OpenRead(Path.Combine(args[1],$"wood-{prototype.Id}-baseline.bin")));int cells=data.ReadInt32();
            for(int i=0;i<cells;i++)
            {
                short x=data.ReadInt16(),y=data.ReadInt16(),z=data.ReadInt16();data.ReadUInt32();if(y<30||y>32)continue;
                checkedCounts.TryGetValue(y,out int n);checkedCounts[y]=n+1;
                double x0=x*.1+.00001,y0=y*.1+.00001,z0=z*.1+.00001,x1=(x+1)*.1-.00001,y1=(y+1)*.1-.00001,z1=(z+1)*.1-.00001;
                if(!prototype.BlocksLocal(x0,y0,z0,x1,y1,z1))throw new Exception("Packed hard-body seam mismatch");bodyChecks++;
                if(prototype.ResistanceLocal(x0,y0,z0,x1,y1,z1)!=0)throw new Exception("Packed seam unexpectedly has stick/leaf resistance");resistanceChecks++;
            }
            if(!counts.OrderBy(p=>p.Key).SequenceEqual(checkedCounts.OrderBy(p=>p.Key)))throw new Exception("Seam corpus mismatch");
            rows.Add(new{id=prototype.Id,body_checks=bodyChecks,resistance_checks=resistanceChecks,rows=checkedCounts,all_existing_packed_upper_roles_match_composite_oracle=true});prototype.ReleaseMeshData();
        }
        if(rows.Count!=2)throw new Exception("Missing prototype");File.WriteAllText(args[3],JsonSerializer.Serialize(new{passed=true,scope="Direct current live AVPM public BlocksLocal/ResistanceLocal checks for all canonical wood cells in rows30..32 against independent original/composite policy audit. Interior subcell boxes avoid neighboring-cell float boundary inclusion. No native/scene wiring tested.",results=rows},new JsonSerializerOptions{WriteIndented=true}));
        Console.WriteLine("PASS live packed upper body/resistance spans agree with both composite seam fixtures.");
    }
}
