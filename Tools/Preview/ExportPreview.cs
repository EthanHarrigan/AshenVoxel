using System;
using System.IO;
using System.Runtime.InteropServices;
using AshenVoxel.Core;
public static class ExportPreview
{
    public static void Main(string[] args)
    {
        var w = new VoxelWorld(256,128,256,.1f,714103);
        Generation.Generate(w);
        File.WriteAllText(Path.Combine(args[0],"generator-version.txt"),Generation.Version.ToString());
        Export(w,Path.Combine(args[0],"forest"));
        WorldEdits.CarveSphere(w,12.8f,3.1f,12.8f,2.0f,10);
        using(var s=new Simulation(w))
        {
            WorldEdits.PlaceSphere(w,14.5f,6.5f,12.8f,.6f,MaterialId.Sand);
            for(int i=0;i<16;i++)s.Step(5000);
        }
        Export(w,Path.Combine(args[0],"edited"));
    }
    static void Export(VoxelWorld w,string path)
    {
        var material=new byte[w.SizeX*w.SizeY*w.SizeZ];
        var color=new uint[material.Length];
        foreach(var chunk in w.Chunks.Values)
        {
            ReadOnlySpan<MaterialId> ids=chunk.Cells;
            ReadOnlySpan<uint> tints=chunk.Tints;
            for(int i=0;i<Chunk.Volume;i++)
            {
                MaterialId id=ids[i];if(id==MaterialId.Air)continue;
                Int3 local=Chunk.Decode(i);
                int x=chunk.Coordinate.x*16+local.x,y=chunk.Coordinate.y*16+local.y,z=chunk.Coordinate.z*16+local.z;
                if(!w.InBounds(x,y,z))continue;
                int index=x+w.SizeX*(y+w.SizeY*z);material[index]=(byte)id;
                uint tint=tints.Length==0?0:tints[i];color[index]=tint==0?VoxelColor.Base(id):tint;
            }
        }
        File.WriteAllBytes(path+".raw",material);
        using(var file=File.Create(path+".rgb.raw"))file.Write(MemoryMarshal.AsBytes(color.AsSpan()));
    }
}
