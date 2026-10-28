using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.IO;
using AshenVoxel.Core;
using AshenVoxel.Core.Rocks;
using AshenVoxel.WorldScaleRuntime;
using AshenVoxel.WorldScaleRuntime.Editor;
using UnityEngine;
static class Program
{
 static int checks;
 static void Check(bool condition,string message){if(!condition)throw new Exception("FAIL "+message);checks++;Console.WriteLine("PASS "+message);}
 static object Call(object owner,string name,params object[] args)=>owner.GetType().GetMethod(name,BindingFlags.NonPublic|BindingFlags.Instance).Invoke(owner,args);
 static object Get(object owner,string name)=>owner.GetType().GetField(name,BindingFlags.NonPublic|BindingFlags.Instance).GetValue(owner);
 static int Grow(GroundCoverShowcase value){var run=(IEnumerator)Call(value,"Generate");int yields=0;while(run.MoveNext())yields++;return yields;}
 static string Digest(VoxelWorld world)
 {
  using var output=new MemoryStream();using var writer=new BinaryWriter(output);
  for(int z=0;z<world.SizeZ;z++)for(int y=0;y<world.SizeY;y++)for(int x=0;x<world.SizeX;x++){writer.Write((byte)world.Get(x,y,z));writer.Write(world.GetTint(x,y,z));}
  return Convert.ToHexString(SHA256.HashData(output.ToArray())).ToLowerInvariant();
 }
 static string ShapeDigest(VoxelWorld world,GroundCoverShowcase.SpecimenInfo info)
 {
  // Normalize translation and ignore all pigment/material color differences.
  using var output=new MemoryStream();using var writer=new BinaryWriter(output);
  for(int z=-14;z<14;z++)for(int y=0;y<world.SizeY-info.AnchorCell.y;y++)for(int x=-14;x<14;x++)
   writer.Write(world.Get(info.AnchorCell.x+x,info.AnchorCell.y+y,info.AnchorCell.z+z)!=MaterialId.Air);
  return Convert.ToHexString(SHA256.HashData(output.ToArray())).ToLowerInvariant();
 }
 static bool AllMatterCounted(GroundCoverShowcase sample)
 {
  int above=0,total=0;foreach(var info in sample.Specimens)total+=info.AboveGroundCells;
  for(int z=0;z<sample.World.SizeZ;z++)for(int y=5;y<sample.World.SizeY;y++)for(int x=0;x<sample.World.SizeX;x++)
   if(sample.World.Get(x,y,z)!=MaterialId.Air)above++;
  return above==total+1;
 }
 static void Main(string[] args)
 {
  var sample=new GroundCoverShowcase {gameObject=new GameObject("Workshop"),Seed=714103};
  Check(sample.Layout==GroundCoverWorkshopLayout.FullCollection&&sample.ShapeStudyMaterial==MaterialId.Granite&&sample.RockShapeOverride==RockShape.Auto,
   "existing scene defaults retain full collection, granite shape study and automatic rock selection");
  int yielded=Grow(sample);
  Check(sample.Specimens.Count==15&&yielded>=16&&!sample.Generating,"actual workshop iterator emits 10 plants and five rocks and yields between specimens");
  Check(sample.World.SizeX==208&&sample.World.SizeY==64&&sample.World.SizeZ==128&&Math.Abs(sample.World.VoxelSize-.1f)<1e-7,"bounded20.8x6.4x12.8m domain retains10cm matter");
  int all=0;bool unclipped=true,bounds=true,materials=true;
  var counts=new Dictionary<string,int>();
  foreach(var info in sample.Specimens)
  {
   Check(info.AboveGroundCells>0,info.Name+" has actual above-ground matter");counts[info.Name]=info.AboveGroundCells;all+=info.AboveGroundCells;
   unclipped&=!info.Truncated;
   bounds&=info.MinimumCell.x>=0&&info.MinimumCell.y>=5&&info.MinimumCell.z>=0&&info.MaximumCell.x<208&&info.MaximumCell.y<64&&info.MaximumCell.z<128;
   materials&=info.Materials.Length>0;
  }
  Check(unclipped&&bounds&&materials,"default specimens finish within their bounds and expose material names");
  int above=0;bool fungus=false,petal=false;
  for(int z=0;z<128;z++)for(int y=5;y<64;y++)for(int x=0;x<208;x++)
  {var m=sample.World.Get(x,y,z);if(m!=MaterialId.Air)above++;fungus|=m==MaterialId.Fungus;petal|=m==MaterialId.Petal;}
  Check(above==all+1,"displayed specimen cells cover all above-ground matter except the explicit one-cell leaf reference");
  Check(sample.World.Get(104,5,5)==MaterialId.Leaves&&sample.World.Get(104,4,5)==MaterialId.Stone,"one-cell reference is exactly a supported10cm leaf-material voxel");
  Check(fungus&&petal,"mushroom and flower samples use their distinct tissue material IDs");
  string first=Digest(sample.World);Grow(sample);
  Check(first==Digest(sample.World),"repeating seed reproduces every workshop material and tint");
  sample.SeedOffset=1;Grow(sample);
  Check(first!=Digest(sample.World),"next seed changes actual generated workshop matter or pigment");
  sample.Focus(-1);Check(sample.SelectedSpecimen==14,"previous focus wraps safely to the last specimen");
  sample.Focus(15);Check(sample.SelectedSpecimen==0,"next focus wraps safely to the first specimen");
  sample.FrameAll();var camera=(Camera)Get(sample,"view");
  Check(!float.IsNaN(camera.transform.position.x)&&camera.nearClipPlane==.02f&&camera.farClipPlane==100,"overview uses finite camera coordinates and close specimen clipping range");
  Call(sample,"OnGUI");Check(true,"workshop legend and projected labels execute with API substitutes");
  sample.RockShapeOverride=RockShape.Spire;Grow(sample);
  bool forced=true;for(int i=10;i<sample.Specimens.Count;i++)forced&=sample.Specimens[i].GeneratedRockShape==RockShape.Spire;
  Check(forced&&AllMatterCounted(sample),"full-collection override selects actual spire generation for all five rock materials within the displayed bounds");
  sample.Focus(14);
  var editor=new GroundCoverShowcaseEditor {target=sample};UnityEditor.EditorApplication.isPlaying=true;
  sample.OverviewOnStart=false;
  int starts=sample.CoroutineStarts;GUILayout.PressedButton="Compare six rock shapes";editor.OnInspectorGUI();
  Check(sample.Layout==GroundCoverWorkshopLayout.RockShapes&&sample.OverviewOnStart&&sample.CoroutineStarts==starts+1,
   "Inspector comparison button selects the six-shape layout, restores overview and requests actual regeneration");
  Grow(sample);
  Check(sample.Specimens.Count==6&&sample.SelectedSpecimen==5&&sample.World.SizeX==128&&sample.World.SizeZ==96,
   "switching15-to6 specimens clamps selection and uses the compact rock-study domain");
  Check(AllMatterCounted(sample),"six-shape label counts include every actual above-ground cell except the one-cell reference");
  var expected=new[]{RockShape.AngularBoulder,RockShape.Slab,RockShape.Wedge,RockShape.Spire,RockShape.SplitOutcrop,RockShape.Cluster};
  var geometry=new HashSet<string>();var shapeProfiles=new HashSet<string>();var shapeCounts=new Dictionary<string,int>();
  var shapeEvidence=new Dictionary<string,object>();
  bool actualProfiles=true,studyUnclipped=true;
  for(int i=0;i<expected.Length;i++)
  {
   var info=sample.Specimens[i];
   actualProfiles&=info.GeneratedRockShape==expected[i]&&info.RockMaterial==MaterialId.Granite&&info.Seed==unchecked(sample.Seed+sample.SeedOffset)
    &&info.GenerationProfileIdentity.Contains("/shape="+expected[i]+"/resolved="+expected[i]);
   studyUnclipped&=info.AboveGroundCells>0&&!info.Truncated&&info.MinimumCell.x>=0&&info.MaximumCell.x<sample.World.SizeX
    &&info.MinimumCell.z>=0&&info.MaximumCell.z<sample.World.SizeZ&&info.MaximumCell.y<sample.World.SizeY;
   string geometryHash=ShapeDigest(sample.World,info);
   geometry.Add(geometryHash);shapeProfiles.Add(info.GenerationProfileIdentity);shapeCounts[expected[i].ToString()]=info.AboveGroundCells;
   shapeEvidence[expected[i].ToString()]=new {profile_identity=info.GenerationProfileIdentity,seed=info.Seed,
    material=info.RockMaterial.ToString(),above_ground_cells=info.AboveGroundCells,color_independent_occupancy_sha256=geometryHash};
  }
  Check(actualProfiles&&shapeProfiles.Count==6,"six actual rock-generator results retain explicit distinct shapes with one material, one seed and matching profile identities");
  Check(geometry.Count==6,"all six shape specimens differ in voxel occupancy after translation normalization and removal of all color differences");
  Check(studyUnclipped,"all six shape specimens finish with nonempty bounded matter and no generation truncation");
  string shapeFirst=Digest(sample.World);Grow(sample);Check(shapeFirst==Digest(sample.World),"same-seed six-shape study reproduces all matter and tints");
  sample.SeedOffset=2;Grow(sample);Check(shapeFirst!=Digest(sample.World),"new seed changes the six-shape study");
  sample.Focus(-1);Check(sample.SelectedSpecimen==5,"previous focus wraps using actual six-specimen count");
  sample.Focus(6);Check(sample.SelectedSpecimen==0,"next focus wraps using actual six-specimen count");
  sample.ShapeStudyMaterial=MaterialId.Sandstone;Grow(sample);bool sandstone=true;
  foreach(var info in sample.Specimens)sandstone&=info.RockMaterial==MaterialId.Sandstone&&info.GeneratedRockShape.HasValue;
  Check(sandstone&&AllMatterCounted(sample),"shape-study material setting reaches all six real rock profiles and preserves counted bounds");
  sample.FrameAll();Call(sample,"OnGUI");Check(true,"compact six-shape framing and labels execute without fixed fifteen-specimen assumptions");
  var surface=(GameObject)Get(sample,"surfaceObject");var orbit=(WorkshopCamera)Get(sample,"orbit");Call(sample,"OnDestroy");
  Check(surface.WasDestroyed&&orbit.gameObject.WasDestroyed,"cleanup releases only the workshop's owned surface and camera objects");
  var report=new {status="PASS",checks,scope="Actual workshop C# generator iterator and camera/editor compilation with Unity API substitutes; not native Editor, mesh rendering or HLSL verification",counts,
   rock_generator_version=RockGenerator.Version,rock_shape_counts=shapeCounts,rock_shape_evidence=shapeEvidence,unique_color_independent_shape_hashes=geometry.Count};
  File.WriteAllText(args[0],JsonSerializer.Serialize(report,new JsonSerializerOptions {WriteIndented=true})+"\n");
  Console.WriteLine("All "+checks+" ground-cover workshop checks passed with Unity API substitutes.");
 }
}
