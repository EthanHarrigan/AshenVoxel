using System;
using System.IO;
using System.IO.Compression;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Diagnostics;
using AshenVoxel.Core;
using AshenVoxel.Core.Trees;
using AshenVoxel.Core.Details;
using AshenVoxel.Core.Rocks;
using AshenVoxel.WorldScale;
using AshenVoxel.Rendering;
using UnityEngine;
static class ExportPrototypes
{
 static readonly int[] Seeds={714103,271828};
 static readonly MaterialId[] Rocks={MaterialId.Stone,MaterialId.Granite,MaterialId.Limestone,MaterialId.Sandstone,MaterialId.Shale};
 sealed class MeshPart{public readonly List<Vector3> Vertices=new List<Vector3>(),Normals=new List<Vector3>();public readonly List<Color32> Colors=new List<Color32>();public readonly List<int> Indices=new List<int>();}
 static void Main(string[] args)
 {
  Directory.CreateDirectory(args[0]);string mode=args.Length>1?args[1]:"all";var reports=new List<object>();
  if(mode=="all"||mode=="trees")foreach(TreeSpecies species in Enum.GetValues(typeof(TreeSpecies)))for(int stage=0;stage<2;stage++)for(int variant=0;variant<2;variant++)
  {
   int id=(int)species*4+stage*2+variant;int seed=Seeds[variant];var p=TreePresets.Get(species).With(age:stage==0?(species==TreeSpecies.Redwood?.32f:.45f):1f);
   double reach=p.CrownRadius*Math.Pow(p.Age,.85)*1.5+p.LeafClusterRadius*Math.Pow(p.Age,.65)*3+p.TrunkRadius*Math.Pow(p.Age,1.15)*(1.7+p.RootFlare*1.1)+3;
   int side=(int)Math.Ceiling(reach*2/.1);if(side%2!=0)side++;int height=(int)Math.Ceiling((p.Height*p.Age*1.25+4)/.1);
   var world=new VoxelWorld(side,height,side,.1f,seed);var anchor=new Int3(side/2,4,side/2);var watch=Stopwatch.StartNew();
   TreeResult result=TreeGenerator.Generate(world,p,anchor.x*.1f,anchor.y*.1f,anchor.z*.1f,seed,new TreeGenerationBudget(8192,160000000,16384));
   if(result.Truncated)throw new Exception("Truncated tree "+species+" "+stage+" "+variant+": "+result.TruncationReason);
   Console.WriteLine("Generated "+species+" "+stage+" "+variant+" cells="+(result.WoodCells+result.LeafCells)+" in "+watch.ElapsedMilliseconds+"ms");
   reports.Add(Export(args[0],id,0,species+" "+(stage==0?"young":"mature")+" "+variant,world,anchor,watch,new{species,stage,variant,seed,profile=p,wood_cells=result.WoodCells,leaf_cells=result.LeafCells,branches=result.Branches.Count,voxel_candidates=result.VoxelCandidates,truncated=result.Truncated,actual_height=result.ActualHeight}));
   world=null;GC.Collect();
  }
  if(mode=="all"||mode=="plants")foreach(PlantKind kind in Enum.GetValues(typeof(PlantKind)))for(int variant=0;variant<2;variant++)
  {
   var world=new VoxelWorld(80,64,80,.1f,Seeds[variant]);var anchor=new Int3(40,4,40);PlantProfile p=PlantPresets.For(kind);if(kind==PlantKind.Shrub)p=p.With(fruitProbability:.35);
   var watch=Stopwatch.StartNew();var result=PlantGenerator.Generate(world,p,anchor,Seeds[variant]);if(result.Truncated)throw new Exception("Truncated plant "+kind);
   reports.Add(Export(args[0],100+(int)kind*2+variant,1,kind+" "+variant,world,anchor,watch,new{profile=p,result}));
  }
  if(mode=="all"||mode=="plants")for(int micro=0;micro<3;micro++)
  {
   PlantKind kind=micro==0?PlantKind.Mushroom:micro==1?PlantKind.BroadleafHerb:PlantKind.Flower;var world=new VoxelWorld(16,16,16,.1f,Seeds[0]);var anchor=new Int3(8,4,8);var p=PlantPresets.For(kind).With(heightMeters:.1,radiusMeters:.05,stems:1);var watch=Stopwatch.StartNew();var r=PlantGenerator.Generate(world,p,anchor,Seeds[0]);reports.Add(Export(args[0],120+micro,1,"Micro "+kind,world,anchor,watch,r));
  }
  if(mode=="all"||mode=="rocks")for(int material=0;material<Rocks.Length;material++)for(int shape=1;shape<=6;shape++)for(int size=0;size<2;size++)
  {
   var world=new VoxelWorld(80,64,80,.1f,Seeds[0]);var anchor=new Int3(40,4,40);var p=RockPresets.For(Rocks[material],Seeds[0]).With(shape:(RockShape)shape,widthMeters:size==0?.8:2,heightMeters:(shape==2?.45:shape==4?2.7:1.25)*(size==0?.4:1),depthMeters:(shape==2?1.25:1.8)*(size==0?.4:1));
   var watch=Stopwatch.StartNew();var result=RockGenerator.Generate(world,anchor,p);if(result.Truncated||result.ClippedCells>0)throw new Exception("Truncated rock "+material+" "+shape);
   reports.Add(Export(args[0],200+material*12+(shape-1)*2+size,2,Rocks[material]+" "+(RockShape)shape+" "+(size==0?"small":"large"),world,anchor,watch,new{profile=p,result}));
  }
  File.WriteAllText(Path.Combine(args[0],"report-"+mode+".json"),JsonSerializer.Serialize(new{status="PASS",mode,records=reports},new JsonSerializerOptions{WriteIndented=true,IncludeFields=true}));
 }
 static object Export(string output,int id,byte kind,string name,VoxelWorld world,Int3 anchor,Stopwatch watch,object generation)
 {
  var meshes=new List<MeshPart>();var lodCounts=new List<object>();float[] bounds={float.MaxValue,float.MaxValue,float.MaxValue,float.MinValue,float.MinValue,float.MinValue};
  int occupied=0;var columns=new Dictionary<(short x,short z),List<short>>();
  foreach(var pair in world.Chunks)for(int i=0;i<Chunk.Volume;i++)
  {
   MaterialId m=pair.Value.Cells[i];if(m==MaterialId.Air)continue;Int3 local=Chunk.Decode(i);int x=pair.Key.x*16+local.x,y=pair.Key.y*16+local.y,z=pair.Key.z*16+local.z;if(!world.InBounds(x,y,z))continue;occupied++;
   bounds[0]=Math.Min(bounds[0],(x-anchor.x)*.1f);bounds[1]=Math.Min(bounds[1],(y-anchor.y)*.1f);bounds[2]=Math.Min(bounds[2],(z-anchor.z)*.1f);
   bounds[3]=Math.Max(bounds[3],(x-anchor.x+1)*.1f);bounds[4]=Math.Max(bounds[4],(y-anchor.y+1)*.1f);bounds[5]=Math.Max(bounds[5],(z-anchor.z+1)*.1f);
   if(MaterialCatalog.BlocksActorBody(m))
   {var key=((short)(x-anchor.x),(short)(z-anchor.z));if(!columns.TryGetValue(key,out var list)){list=new List<short>();columns.Add(key,list);}list.Add((short)(y-anchor.y));}
  }
  int[] multipliers=kind==0?new[]{1,2,4,16,64}:new[]{1,2,4,8};
  foreach(int multiplier in multipliers)
  {
   VoxelWorld lod=multiplier==1?world:Reduce(world,multiplier);MeshPart mesh=Extract(lod,new Vector3(anchor.x*.1f,anchor.y*.1f,anchor.z*.1f));
   meshes.Add(mesh);lodCounts.Add(new{cell_m=multiplier*.1,vertices=mesh.Vertices.Count,triangles=mesh.Indices.Count/3});Console.WriteLine("  "+name+" LOD"+multiplier+" verts="+mesh.Vertices.Count+" tris="+mesh.Indices.Count/3);
  }
  byte[] payload;
  using(var stream=new MemoryStream())using(var w=new BinaryWriter(stream,Encoding.UTF8,true))
  {
   w.Write(id);w.Write(kind);byte[] text=Encoding.UTF8.GetBytes(name);w.Write((ushort)text.Length);w.Write(text);foreach(float b in bounds)w.Write(b);w.Write((byte)meshes.Count);
   foreach(MeshPart mesh in meshes)
   {
    w.Write(mesh.Vertices.Count);w.Write(mesh.Indices.Count);
    for(int i=0;i<mesh.Vertices.Count;i++){Vector3 v=mesh.Vertices[i],n=mesh.Normals[i];w.Write(checked((short)Math.Round(v.x*10)));w.Write(checked((short)Math.Round(v.y*10)));w.Write(checked((short)Math.Round(v.z*10)));w.Write((byte)(n.x>.5?0:n.x<-.5?1:n.y>.5?2:n.y<-.5?3:n.z>.5?4:5));Color32 c=mesh.Colors[i];w.Write((uint)c.r|((uint)c.g<<8)|((uint)c.b<<16)|((uint)c.a<<24));}
    foreach(int t in mesh.Indices)w.Write(t);
   }
   w.Write(columns.Count);
   foreach(var pair in columns.OrderBy(x=>x.Key.x).ThenBy(x=>x.Key.z))
   {
    w.Write(pair.Key.x);w.Write(pair.Key.z);pair.Value.Sort();var spans=new List<short>();short start=pair.Value[0],end=(short)(start+1);
    for(int i=1;i<pair.Value.Count;i++){short y=pair.Value[i];if(y==end)end++;else{spans.Add(start);spans.Add(end);start=y;end=(short)(y+1);}}spans.Add(start);spans.Add(end);
    w.Write((ushort)(spans.Count/2));foreach(short value in spans)w.Write(value);
   }
   w.Flush();payload=stream.ToArray();
  }
  byte[] compressed;using(var stream=new MemoryStream()){using(var gzip=new GZipStream(stream,CompressionLevel.Optimal,true))gzip.Write(payload,0,payload.Length);compressed=stream.ToArray();}
  using(var w=new BinaryWriter(File.Create(Path.Combine(output,id.ToString("D3")+".avpr")))){w.Write(compressed.Length);w.Write(payload.Length);w.Write(ExplorationPrototypeReader.Crc(payload));w.Write(compressed);}
  var report=new{id,name,kind,occupied_cells=occupied,bounds,lods=lodCounts,collision_columns=columns.Count,expanded_bytes=payload.Length,compressed_bytes=compressed.Length,elapsed_ms=watch.ElapsedMilliseconds,generation};
  File.WriteAllText(Path.Combine(output,id.ToString("D3")+".json"),JsonSerializer.Serialize(report,new JsonSerializerOptions{WriteIndented=true,IncludeFields=true}));return report;
 }
 static VoxelWorld Reduce(VoxelWorld source,int factor)
 {
  var result=new VoxelWorld((source.SizeX+factor-1)/factor,(source.SizeY+factor-1)/factor,(source.SizeZ+factor-1)/factor,source.VoxelSize*factor,source.Seed);
  // Keep a deterministic representative and material/color palette per occupied visual block.
  // This is a visual cache only: full-resolution collision columns are retained separately.
  foreach(var pair in source.Chunks)for(int i=0;i<Chunk.Volume;i++)
  {
   MaterialId m=pair.Value.Cells[i];if(m==MaterialId.Air)continue;Int3 local=Chunk.Decode(i);int x=pair.Key.x*16+local.x,y=pair.Key.y*16+local.y,z=pair.Key.z*16+local.z;if(!source.InBounds(x,y,z))continue;
   int xx=x/factor,yy=y/factor,zz=z/factor;MaterialId old=result.Get(xx,yy,zz);
   if(old==MaterialId.Air||m==MaterialId.Wood&&old!=MaterialId.Wood)result.Set(xx,yy,zz,m,source.GetColor(x,y,z));
  }
  return result;
 }
 static MeshPart Extract(VoxelWorld world,Vector3 origin)
 {
  var result=new MeshPart();var mesher=new GreedyChunkMesher();var mesh=new Mesh();
  foreach(var pair in world.Chunks.OrderBy(p=>p.Key.z).ThenBy(p=>p.Key.y).ThenBy(p=>p.Key.x))
  {
   mesher.Build(world,pair.Key,mesh);int offset=result.Vertices.Count;
   foreach(Vector3 v in mesh.vertices)result.Vertices.Add(v-origin);result.Normals.AddRange(mesh.normals);result.Colors.AddRange(mesh.colors32);foreach(int index in mesh.triangles)result.Indices.Add(index+offset);
  }
  if(result.Vertices.Count==0)throw new Exception("Empty prototype mesh");return result;
 }
}
