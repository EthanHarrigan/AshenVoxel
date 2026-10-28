using System;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Reflection;
using System.Collections.Generic;
using AshenVoxel.WorldScale;
using AshenVoxel.WorldScaleRuntime;
using UnityEngine;

static class ExportWorldViews
{
    const BindingFlags Flags=BindingFlags.NonPublic|BindingFlags.Instance;
    static void Set(object o,string name,object value)=>o.GetType().GetField(name,Flags).SetValue(o,value);
    static object Get(object o,string name)=>o.GetType().GetField(name,Flags|BindingFlags.Public).GetValue(o);
    static double[] P(double x,double y,double z)=>new[]{x,y,z};
    static void Main(string[] args)
    {
        string root=args[0],output=args[1];Directory.CreateDirectory(output);
        IWorldField analytic=new ProceduralMountainField(714103,6500,100000,true);
        double[] eye=Peak(analytic);eye[1]+=6;
        Export(output,"analytic-observer",analytic,"Analytic fallback / NOT diffusion",eye,false,false);
        Export(output,"analytic-high",analytic,"Analytic fallback / NOT diffusion",P(eye[0],eye[1]+5994,eye[2]),false,false);
        using(var input=File.OpenRead(Path.Combine(root,"Assets/AshenVoxel/WorldScaleRuntime/Samples/Ceteviles.bytes")))
        {
            var atlas=AtlasIO.Read(input);IWorldField map=new AtlasWorldField(atlas);
            double best=double.MinValue,bx=0,bz=0;
            for(int z=1;z<atlas.Rows-1;z++)for(int x=1;x<atlas.Columns-1;x++)
            {double h=atlas.HeightAt(x,z);if(h>best){best=h;bx=(x/(double)(atlas.Columns-1)-.5)*atlas.ExtentX;bz=(z/(double)(atlas.Rows-1)-.5)*atlas.ExtentZ;}}
            Export(output,"ceteviles-observer",map,"Imported authored Ceteviles control field / NOT neural refinement",P(bx,best+240,bz),false,false);
        }
        using(var input=File.OpenRead(Path.Combine(root,"Assets/AshenVoxel/WorldScaleRuntime/Samples/CetevilesNeural.bytes")))
        {
            var atlas=AtlasIO.Read(input);IWorldField field=new AtlasWorldField(atlas);
            double top=((IWorldFieldBounds)field).QueryBounds(-480,-480,480,480).Maximum;
            Export(output,"neural-study",field,"Actual Ceteviles-conditioned InfiniteDiffusion output",P(1100,top+700,-1400),true,true);
        }
    }
    static double[] Spawn(IWorldField field,double width)
    {
        double best=double.MinValue,bx=0,bz=0;
        // Exact observation search used by WorldExplorer.Regenerate.
        for(int z=-4;z<=4;z++)for(int x=-4;x<=4;x++)
        {double px=x*width/12,pz=z*width/12,h=field.Sample(px,pz).HeightMeters;if(h>best){best=h;bx=px;bz=pz;}}
        return P(bx,best+240,bz);
    }
    static double[] Peak(IWorldField field)
    {
        double best=double.MinValue,bx=0,bz=0;
        for(int z=0;z<=320;z++)for(int x=0;x<=320;x++)
        {double px=-15000+x*30000.0/320,pz=-15000+z*30000.0/320,h=field.Sample(px,pz).HeightMeters;if(h>best){best=h;bx=px;bz=pz;}}
        double cx=bx,cz=bz;
        for(int z=-10;z<=10;z++)for(int x=-10;x<=10;x++)
        {double px=cx+x*10,pz=cz+z*10,h=field.Sample(px,pz).HeightMeters;if(h>best){best=h;bx=px;bz=pz;}}
        return P(bx,best,bz);
    }
    static double[] OpenView(IWorldField field,double[] camera)
    {
        double best=double.MinValue,angle=0;
        for(int i=0;i<32;i++)
        {
            double a=i*Math.PI*2/32,score=0;
            for(int j=1;j<=7;j++)
            {double d=j*4000,h=field.Sample(camera[0]+Math.Cos(a)*d,camera[2]+Math.Sin(a)*d).HeightMeters;score+=(camera[1]-h)/(j*.35+1);}
            if(score>best){best=score;angle=a;}
        }
        return P(camera[0]+Math.Cos(angle)*15000,camera[1]-1900,camera[2]+Math.Sin(angle)*15000);
    }
    static void Export(string output,string name,IWorldField field,string kind,double[] camera,bool study,bool omitSkirts)
    {
        double ox=Math.Floor(camera[0]/100)*100,oy=Math.Floor(camera[1]/100)*100,oz=Math.Floor(camera[2]/100)*100;
        var explorer=new WorldExplorer{TileQuads=32,TerrainMaterial=new Material(null)};
        Set(explorer,"field",field);Set(explorer,"originX",ox);Set(explorer,"originY",oy);Set(explorer,"originZ",oz);
        var settings=new LodSettings(64,12,32000,2.2,384,-12000,48000);
        LodPlan plan=study?null:LodPlanner.Build(field,new WorldPoint(camera[0],camera[1],camera[2]),settings);
        IReadOnlyList<LodTile> tiles;
        if(study){var range=((IWorldFieldBounds)field).QueryBounds(-480,-480,480,480);tiles=new[]{new LodTile(new TileKey(0,0,0),-480,-480,960,range.Minimum,range.Maximum)};}
        else tiles=plan.Tiles;
        var positions=new List<Vector3>();var normals=new List<Vector3>();var colors=new List<Color32>();var indices=new List<uint>();var levels=new List<byte>();
        var tileRecords=new List<object>();double minY=double.MaxValue,maxY=double.MinValue;
        var low=P(double.MaxValue,double.MaxValue,double.MaxValue);var high=P(double.MinValue,double.MinValue,double.MinValue);
        foreach(var tile in tiles)
        {
            object surface=typeof(WorldExplorer).GetMethod("BuildSurface",Flags).Invoke(explorer,new object[]{tile});
            var mesh=(Mesh)Get(surface,"Mesh");var go=(GameObject)Get(surface,"Object");var translation=go.transform.position;
            int count=omitSkirts?33*33:mesh.Vertices.Count;int triCount=omitSkirts?32*32*6:mesh.Triangles.Count;uint offset=(uint)positions.Count;
            for(int i=0;i<count;i++)
            {
                Vector3 p=mesh.Vertices[i]+translation;positions.Add(p);normals.Add(mesh.Normals[i]);colors.Add(mesh.Colors[i]);levels.Add((byte)tile.Key.Level);
                double[] q=P(p.x,p.y,p.z);for(int a=0;a<3;a++){low[a]=Math.Min(low[a],q[a]);high[a]=Math.Max(high[a],q[a]);}
                if(i<33*33){minY=Math.Min(minY,p.y+oy);maxY=Math.Max(maxY,p.y+oy);}
            }
            for(int i=0;i<triCount;i++)indices.Add(offset+(uint)mesh.Triangles[i]);
            tileRecords.Add(new{level=tile.Key.Level,x=tile.Key.X,z=tile.Key.Z,size=tile.SizeMeters,ox=tile.OriginXMeters,oz=tile.OriginZMeters,minY=tile.MinimumHeight,maxY=tile.MaximumHeight,sample_spacing_m=tile.SizeMeters/32});
        }
        string bin=Path.Combine(output,name+".bin");
        using(var w=new BinaryWriter(File.Create(bin)))
        {
            w.Write(Encoding.ASCII.GetBytes("AVWM0001"));w.Write(positions.Count);w.Write(indices.Count/3);
            foreach(Vector3 p in positions){w.Write(p.x);w.Write(p.y);w.Write(p.z);}
            foreach(Vector3 p in normals){w.Write(p.x);w.Write(p.y);w.Write(p.z);}
            foreach(Color32 c in colors){w.Write(c.r);w.Write(c.g);w.Write(c.b);w.Write(c.a);}
            foreach(uint i in indices)w.Write(i);foreach(byte l in levels)w.Write(l);
        }
        double[] target=study?P(0,(minY+maxY)*.5,0):OpenView(field,camera);
        var metadata=new{name,source_kind=kind,field_identity=field.SourceIdentity,seed=714103,format="AVWM0001: positions f32xyz; normals f32xyz; colors u8rgba; indices u32x3; vertex LOD u8",camera_origin=P(camera[0]-ox,camera[1]-oy,camera[2]-oz),camera_target=P(target[0]-ox,target[1]-oy,target[2]-oz),camera_origin_m=P(camera[0]-ox,camera[1]-oy,camera[2]-oz),camera_target_m=P(target[0]-ox,target[1]-oy,target[2]-oz),global_camera=camera,global_origin=P(ox,oy,oz),world_origin_m=P(ox,oy,oz),camera_height_above_ground_m=camera[1]-field.Sample(camera[0],camera[2]).HeightMeters,far_distance_m=study?10000:57600,planned_view_distance_m=study?10000:32000,fov_degrees=study?42:62,vertex_count=positions.Count,triangle_count=indices.Count/3,minimum_surface_height_m=minY,maximum_surface_height_m=maxY,bounds_min=low,bounds_max=high,tiles=tileRecords,tile_cap=study?1:384,pending_splits=plan?.PendingSplits??0,skirt_policy=omitSkirts?"Only actual mesh surface exported for finite neural diagnostic; skirts omitted":"Exact runtime bounds-aware skirts retained",note="Actual C# fields, LodPlanner and WorldExplorer.BuildSurface under test Unity data substitutes; NOT native Unity output."};
        File.WriteAllText(Path.Combine(output,name+".json"),JsonSerializer.Serialize(metadata,new JsonSerializerOptions{WriteIndented=true}));
        Console.WriteLine(name+": "+tiles.Count+" tiles, "+positions.Count+" vertices, "+(indices.Count/3)+" triangles; elevations "+minY.ToString("F1")+".."+maxY.ToString("F1")+"m");
    }
}
