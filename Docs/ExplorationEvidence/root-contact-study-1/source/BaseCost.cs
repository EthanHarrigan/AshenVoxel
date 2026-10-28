using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using AshenVoxel.Core;
using AshenVoxel.Rendering;
using UnityEngine;

static class BaseCost
{
    sealed class Column{public short X,Z,Target;public short[] Y;public uint[] Tint;}
    static Column[] Read(string path)
    {
        using var r=new BinaryReader(File.OpenRead(path));var columns=new Column[r.ReadInt32()];
        for(int i=0;i<columns.Length;i++)
        {
            var c=new Column{X=r.ReadInt16(),Z=r.ReadInt16(),Target=r.ReadInt16()};int n=r.ReadInt32();c.Y=new short[n];c.Tint=new uint[n];
            for(int j=0;j<n;j++){c.Y[j]=r.ReadInt16();c.Tint[j]=r.ReadUInt32();}columns[i]=c;
        }
        if(r.BaseStream.Position!=r.BaseStream.Length)throw new Exception("Trailing bytes");return columns;
    }
    static VoxelWorld Build(Column[] columns,out Int3 origin)
    {
        int x0=columns.Min(c=>c.X),z0=columns.Min(c=>c.Z),y0=columns.Min(c=>(int)Math.Min(c.Target,c.Y[0]));
        origin=new Int3(x0,y0,z0);var world=new VoxelWorld(columns.Max(c=>c.X)-x0+1,31-y0,columns.Max(c=>c.Z)-z0+1,.1f,714103);
        foreach(var c in columns)
        {
            double ratio=(30d-c.Target)/(30-c.Y[0]);
            for(int j=0;j<c.Y.Length;j++)
            {
                int y=c.Y[j];int lo=y==30?30:(int)Math.Floor(c.Target+(y-c.Y[0])*ratio+1e-7);
                int hi=y==30?31:Math.Min(30,(int)Math.Ceiling(c.Target+(y+1-c.Y[0])*ratio-1e-7));
                for(int ny=lo;ny<hi;ny++)world.Set(c.X-x0,ny-y0,c.Z-z0,MaterialId.Wood,c.Tint[j]);
            }
        }
        return world;
    }
    static void Check(VoxelWorld world,Int3 origin,string expected)
    {
        using var r=new BinaryReader(File.OpenRead(expected));int n=r.ReadInt32();
        for(int i=0;i<n;i++)
        {
            int x=r.ReadInt16()-origin.x,y=r.ReadInt16()-origin.y,z=r.ReadInt16()-origin.z;uint tint=r.ReadUInt32();
            if(world.Get(x,y,z)!=MaterialId.Wood||world.GetColor(x,y,z)!=tint)throw new Exception("Mapped voxel/pigment mismatch");
        }
        if(world.Chunks.Sum(p=>p.Value.NonAirCount)!=n)throw new Exception("Mapped occupancy mismatch");
    }
    static void Main(string[] args)
    {
        var rows=new List<object>();
        foreach(int id in new[]{10,11})
        {
            var columns=Read(Path.Combine(args[0],$"cost-{id}.bin"));var buildMs=new List<double>();var buildBytes=new List<long>();var meshMs=new List<double>();var meshBytes=new List<long>();
            int faces=0,vertices=0,triangles=0,chunks=0,cells=0;
            for(int repeat=0;repeat<15;repeat++)
            {
                long a=GC.GetAllocatedBytesForCurrentThread();var sw=Stopwatch.StartNew();var world=Build(columns,out var origin);sw.Stop();
                if(repeat>=3){buildMs.Add(sw.Elapsed.TotalMilliseconds);buildBytes.Add(GC.GetAllocatedBytesForCurrentThread()-a);}
                if(repeat==0)Check(world,origin,Path.Combine(args[0],$"expected-{id}.bin"));
                a=GC.GetAllocatedBytesForCurrentThread();sw.Restart();var mesher=new GreedyChunkMesher();faces=vertices=triangles=0;
                foreach(var chunk in world.Chunks){var mesh=new Mesh();faces+=mesher.Build(world,chunk.Key,mesh);vertices+=mesh.vertexCount;triangles+=mesh.triangles.Length/3;}
                sw.Stop();if(repeat>=3){meshMs.Add(sw.Elapsed.TotalMilliseconds);meshBytes.Add(GC.GetAllocatedBytesForCurrentThread()-a);}
                chunks=world.Chunks.Count;cells=world.Chunks.Sum(p=>p.Value.NonAirCount);
            }
            buildMs.Sort();meshMs.Sort();
            rows.Add(new{id,columns=columns.Length,source_cells=columns.Sum(c=>c.Y.Length),cells,chunks,faces,vertices,triangles,
                template_payload_bytes=new FileInfo(Path.Combine(args[0],$"cost-{id}.bin")).Length,
                cumulative_material_build_allocated_bytes=buildBytes.Max(),cumulative_mesh_allocated_bytes=meshBytes.Max(),
                material_build_median_ms=buildMs[buildMs.Count/2],material_build_max_ms=buildMs.Last(),mesh_median_ms=meshMs[meshMs.Count/2],mesh_max_ms=meshMs.Last(),
                mesh_payload_bytes=(long)vertices*28+(long)triangles*3*4,
                actual_core_matter_and_rgb_match_python_geometry=true});
        }
        File.WriteAllText(Path.Combine(args[0],"cost.json"),JsonSerializer.Serialize(new{scope="Windows optimized .NET8 CPU. Actual VoxelWorld.Set and GreedyChunkMesher with existing non-Unity Mesh shim. Shared template load and terrain sampling excluded; full base with one canonical guard row, includes exposed top cap. No Unity upload/shader/collision/LOD/FPS or production cache cost claim. Fresh mesher/world each iteration, three warmups and twelve measured runs.",rows},new JsonSerializerOptions{WriteIndented=true}));
        Console.WriteLine("PASS two mapped bases exactly match all Python matter/pigment cells; measured actual Core/mesher CPU cost.");
    }
}
