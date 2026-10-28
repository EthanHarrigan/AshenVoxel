using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using AshenVoxel.Core;
using AshenVoxel.WorldScale;
static class VerifyPackedBase
{
    static void Main(string[] args)
    {
        var rows=new List<object>();using var reader=new ExplorationPrototypeReader(File.ReadAllBytes(args[0]));
        while(reader.TryRead(out var p))
        {
            if(p.Id!=10&&p.Id!=11){p.ReleaseMeshData();continue;}
            var wood=new Dictionary<(short,short,short),uint>();
            using(var r=new BinaryReader(File.OpenRead(Path.Combine(args[1],$"wood-{p.Id}-baseline.bin"))))
            {int count=r.ReadInt32();for(int i=0;i<count;i++){short x=r.ReadInt16(),y=r.ReadInt16(),z=r.ReadInt16();uint color=r.ReadUInt32();if(y<80)wood.Add((x,y,z),color);}}
            int faces=0,cells=0;var mesh=p.Mesh(0);
            for(int q=0;q<mesh.VertexCount;q+=4)
            {
                if(VertexAppearance.Material((byte)(mesh.Colors[q]>>24))!=(byte)MaterialId.Wood)continue;
                int axis=mesh.Normals[q]/2;bool positive=mesh.Normals[q]%2==0;var low=new int[]{32767,32767,32767};var high=new int[]{-32768,-32768,-32768};
                for(int corner=0;corner<4;corner++)for(int d=0;d<3;d++){int v=mesh.Positions[(q+corner)*3+d];low[d]=Math.Min(low[d],v);high[d]=Math.Max(high[d],v);}
                if(positive)low[axis]--;high[axis]=low[axis]+1;
                if(low[1]>=80)continue;high[1]=Math.Min(high[1],80);faces++;
                uint packed=mesh.Colors[q];uint tint=0xff000000u|((packed&255)<<16)|(packed&0xff00)|((packed>>16)&255);
                for(int z=low[2];z<high[2];z++)for(int y=low[1];y<high[1];y++)for(int x=low[0];x<high[0];x++)
                {if(!wood.TryGetValue(((short)x,(short)y,(short)z),out uint expected)||expected!=tint)throw new Exception($"Live packed wood/pigment mismatch id={p.Id} at{x},{y},{z}");cells++;}
            }
            rows.Add(new{id=p.Id,checked_lod0_wood_quads_below_8m=faces,checked_face_cells=cells,status="PASS",scope="Every actual packed exposed wood face below8m has identical occupied cell and intrinsic RGB in the offline canonical data; interior cells/foliage outside this assertion."});p.ReleaseMeshData();
        }
        if(rows.Count!=2)throw new Exception("Missing redwoods");File.WriteAllText(Path.Combine(args[1],"packed-base-verification.json"),JsonSerializer.Serialize(rows,new JsonSerializerOptions{WriteIndented=true}));Console.WriteLine("PASS both live packed mature-redwood bases match offline actual wood occupancy and pigment below8m.");
    }
}
