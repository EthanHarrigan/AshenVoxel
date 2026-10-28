using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using AshenVoxel.Core;
using AshenVoxel.Exploration.Experimental;
using AshenVoxel.WorldScale;
static class Program
{
 sealed class Owner:AshenVoxel.Exploration.IShrubMatterOwner{}
 static int PunchRegistry()
 {
  int checks=0;
  void Check(bool ok,string why){checks++;if(!ok)throw new Exception(why);}
  var hard=new Dictionary<int,PrototypeCollisionColumn>{{0,new PrototypeCollisionColumn(0,0,new short[]{0,1})}};
  var prototype=new ExplorationPrototype(0,0,"single punch cell",new float[]{0,0,0,.1f,.1f,.1f},Array.Empty<PrototypeMeshData>(),hard,new Dictionary<int,PrototypeCollisionColumn>());
  var owner=new Owner();
  foreach(float yaw in new[]{0f,.7f,1.5707963f})foreach(var test in new[]{(-1f,.05f,true),(-2f,.05f,true),(-2.5f,.05f,true),(.05f,.05f,true),(-2f,.16f,true),(-2f,.25f,false),(-2.51f,.05f,false)})
  {
   var rotation=System.Numerics.Quaternion.CreateFromAxisAngle(System.Numerics.Vector3.UnitY,yaw);
   var eye=System.Numerics.Vector3.Transform(new System.Numerics.Vector3(test.Item1,.05f,test.Item2),rotation);var dir=System.Numerics.Vector3.Transform(System.Numerics.Vector3.UnitX,rotation);
   var registry=AshenVoxel.Exploration.VegetationStreaming.Fixture(prototype,new PopulationInstance{PrototypeId=0,Scale=1,YawDegrees=yaw*180/(float)Math.PI,Position=new WorldPoint(0,0,0),StableKey=7});
   bool hit=registry.TryReserveNaturalMatterPunch(new WorldPoint(eye.X,eye.Y,eye.Z),new WorldPoint(dir.X,dir.Y,dir.Z),100,owner,out var lease,out _);
   Check(hit==test.Item3,"registry punch range/assist yaw="+yaw+" "+test);Check((lease!=null)==hit,"failed selection leaves no lease");
   if(hit)Check(lease.Instance.StableKey==7,"correct aimed specimen");
  }
  var near=new PopulationInstance{PrototypeId=0,Scale=1,Position=new WorldPoint(0,0,.1),StableKey=1};
  var center=new PopulationInstance{PrototypeId=0,Scale=1,Position=new WorldPoint(1,0,0),StableKey=2};
  var selection=AshenVoxel.Exploration.VegetationStreaming.Fixture(prototype,near,center);
  Check(selection.TryReserveNaturalMatterPunch(new WorldPoint(-1,.05,.05),new WorldPoint(1,0,0),100,owner,out var chosen,out _)&&chosen.Instance.StableKey==2,"center specimen wins over closer assisted specimen");
  selection=AshenVoxel.Exploration.VegetationStreaming.Fixture(prototype,near,center);
  Check(!selection.TryReserveNaturalMatterPunch(new WorldPoint(-1,.05,.05),new WorldPoint(1,0,0),1,owner,out chosen,out _)&&chosen==null,"partial registry scan never reserves");
  Check(!selection.TryReserveNaturalMatterPunch(new WorldPoint(-1,.05,.05),new WorldPoint(double.NaN,0,0),100,owner,out chosen,out _),"invalid punch direction");
  Check(!selection.TryReserveNaturalMatterRay(new WorldPoint(-1,.05,.05),new WorldPoint(1,0,0),double.NaN,100,owner,out chosen,out _),"invalid ray range");
  selection=AshenVoxel.Exploration.VegetationStreaming.Fixture(prototype,center);
  Check(selection.TryReserveNaturalMatterRay(new WorldPoint(-10,.05,.05),new WorldPoint(1,0,0),12,100,owner,out var warm,out _),"approach reserves original hard matter before punch reach");
  Check(selection.NaturalMatterLeaseAimed(warm,new WorldPoint(-10,.05,.05),new WorldPoint(1,0,0),12),"approach retains exact aimed lease");
  Check(!selection.NaturalMatterLeaseAimed(warm,new WorldPoint(-10,.05,.05),new WorldPoint(1,0,0),2.5),"preparation does not extend punch reach");
  Check(!selection.NaturalMatterLeaseAimed(warm,new WorldPoint(-10,.05,.05),new WorldPoint(-1,0,0),12),"look away releases approach demand");
  Check(!selection.NaturalMatterLeaseAimed(warm,new WorldPoint(-13,.05,.05),new WorldPoint(1,0,0),12),"approach retains bounded range");
  Check(!selection.NaturalMatterLeaseAimed(warm,new WorldPoint(-10,.05,.05),new WorldPoint(double.NaN,0,0),12),"invalid approach retention direction");
  Console.WriteLine("PASS "+checks+" punch registry checks");return checks;
 }
 static int RootAndSoftChecks()
 {
  int checks=0;void Check(bool ok,string why){checks++;if(!ok)throw new Exception(why);}
  var softColumns=new Dictionary<int,PrototypeCollisionColumn>{{0,new PrototypeCollisionColumn(0,0,new short[]{0,1})}};
  var plant=new ExplorationPrototype(120,1,"micro soft fixture",new float[]{0,0,0,.1f,.1f,.1f},Array.Empty<PrototypeMeshData>(),new Dictionary<int,PrototypeCollisionColumn>(),softColumns);
  var registry=AshenVoxel.Exploration.VegetationStreaming.Fixture(plant,new PopulationInstance{PrototypeId=120,Scale=1,Position=new WorldPoint(0,0,0),StableKey=9});
  var owner=new Owner();Check(registry.TryReserveNaturalMatterPunch(new WorldPoint(-1,.05,.05),new WorldPoint(1,0,0),100,owner,out var softLease,out _),"soft micro punch admitted");
  Check(registry.TryPrepareLeasedMatterAsync(softLease,out var softTask,out _),"soft leased prepare");var soft=softTask.GetAwaiter().GetResult();Check(soft.World.Chunks.Values.Sum(c=>c.NonAirCount)==1,"exact soft micro copied");
  registry=AshenVoxel.Exploration.VegetationStreaming.Fixture(plant,new PopulationInstance{PrototypeId=120,Scale=1,Position=new WorldPoint(0,0,0),StableKey=9});
  Check(!registry.TryReserveNaturalMatterRay(new WorldPoint(-1,.05,.05),new WorldPoint(1,0,0),2.5,100,owner,out _,out _),"legacy hard ray still ignores soft foliage");
  var columns=new Dictionary<int,PrototypeCollisionColumn>{{0,new PrototypeCollisionColumn(0,0,new short[]{-8,-7})}};
  var prototype=new ExplorationPrototype(10,0,"adapted lower fixture",new float[]{0,-1,0,.1f,4,.1f},Array.Empty<PrototypeMeshData>(),columns);
  var artifact=new RootContactBuildPipeline.CompleteArtifact(new[]{new RootContactBuildPipeline.CellData(new Int3(0,-8,0),0xff123456),new RootContactBuildPipeline.CellData(new Int3(0,30,0),0xffabcdef)},1);
  registry=AshenVoxel.Exploration.VegetationStreaming.Fixture(prototype,new PopulationInstance{PrototypeId=10,Scale=1,Position=new WorldPoint(0,0,0),StableKey=10});registry.FixtureAdaptedRoot(artifact,prototype);
  Check(registry.TryReserveNaturalMatterPunch(new WorldPoint(-1,-.75,.05),new WorldPoint(1,0,0),100,owner,out var lease,out _),"actual adapted lower root hit admitted");
  Check(registry.OriginalHardCell(lease,0,-8,0),"original hard collision uses adapted root");
  Check(registry.TryPrepareLeasedMatterAsync(lease,out var task,out _),"adapted leased worker admitted");var prepared=task.GetAwaiter().GetResult();
  Check(prepared.Anchor.y==8,"negative root cells shift copy and anchor together");
  Check(prepared.World.Get(prepared.Anchor.x,0,prepared.Anchor.z)==MaterialId.Wood&&prepared.World.GetTint(prepared.Anchor.x,0,prepared.Anchor.z)==0xff123456,"exact lower root position/color inserted");
  Check(prepared.World.Get(prepared.Anchor.x,prepared.Anchor.y,prepared.Anchor.z)==MaterialId.Air,"canonical lower base removed");
  var original=NaturalTreeTemplate.Replay(10,out var originalAnchor,out _);
  Check(prepared.World.GetTint(prepared.Anchor.x,prepared.Anchor.y+30,prepared.Anchor.z)==original.GetTint(originalAnchor.x,originalAnchor.y+30,originalAnchor.z),"context row not inserted over canonical matter");
  var originalUpper=original.Chunks.SelectMany(pair=>Enumerable.Range(0,Chunk.Volume).Where(i=>pair.Value.Cells[i]!=MaterialId.Air).Select(i=>{var l=Chunk.Decode(i);return new Int3(pair.Key.x*16+l.x,pair.Key.y*16+l.y,pair.Key.z*16+l.z);})).First(p=>p.y>=originalAnchor.y+40);
  Check(prepared.World.Get(originalUpper.x,originalUpper.y+4,originalUpper.z)==original.Get(originalUpper)&&prepared.World.GetTint(originalUpper.x,originalUpper.y+4,originalUpper.z)==original.GetTint(originalUpper),"upper canonical material/tint retained at identical relative pose");
  Check(prepared.MatterIdentity.EndsWith("/root=root-fixture"),"exact root identity retained");registry.FixtureRootCurrent=false;
  Check(!registry.TryPrepareLeasedMatterAsync(lease,out _,out _),"stale root source refused before preparing");
  Console.WriteLine("PASS "+checks+" root/soft lease checks");return checks;
 }
 static void Main(string[] args)
 {
  var models=new Dictionary<int,ExplorationPrototype>();using(var r=new ExplorationPrototypeReader(File.ReadAllBytes(args[0])))while(r.TryRead(out var p))models.Add(p.Id,p);
  var records=new List<object>();int assertions=PunchRegistry();
  foreach(int id in Enumerable.Range(0,16).Concat(Enumerable.Range(100,23)).Concat(Enumerable.Range(200,60)))
  {
   var watch=Stopwatch.StartNew();var prepared=NaturalTreeTemplate.Prepare(id);var world=prepared.CreateWorld(out var anchor);var model=models[id];
   var hard=new HashSet<Int3>();var soft=new HashSet<Int3>();int count=0;
   using(var hash=IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
   {
    foreach(var pair in world.Chunks.OrderBy(p=>p.Key.z).ThenBy(p=>p.Key.y).ThenBy(p=>p.Key.x))for(int i=0;i<Chunk.Volume;i++)
    {
     var material=pair.Value.Cells[i];if(material==MaterialId.Air)continue;var local=Chunk.Decode(i);var p=new Int3(pair.Key.x*16+local.x,pair.Key.y*16+local.y,pair.Key.z*16+local.z);
     count++;using(var stream=new MemoryStream())using(var writer=new BinaryWriter(stream)){writer.Write(p.x);writer.Write(p.y);writer.Write(p.z);writer.Write((byte)material);writer.Write(world.GetTint(p));hash.AppendData(stream.ToArray());}
     if(MaterialCatalog.BlocksActorBody(material))hard.Add(new Int3(p.x-anchor.x,p.y-anchor.y,p.z-anchor.z));else soft.Add(new Int3(p.x-anchor.x,p.y-anchor.y,p.z-anchor.z));
    }
    var field=typeof(ExplorationPrototype).GetField("collision",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic);
    var columns=(Dictionary<int,PrototypeCollisionColumn>)field.GetValue(model);int packedHard=0;
    var sticks=(Dictionary<int,PrototypeCollisionColumn>)typeof(ExplorationPrototype).GetField("sticks",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).GetValue(model);
    foreach(var column in columns.Values.Concat(sticks.Values)){var spans=column.Spans;for(int i=0;i<spans.Length;i+=2)for(int y=spans[i];y<spans[i+1];y++){if(!hard.Contains(new Int3(column.X,y,column.Z)))throw new Exception("Hard span mismatch "+id);packedHard++;assertions++;}}
    int packedSoft=0;var leaves=(Dictionary<int,PrototypeCollisionColumn>)typeof(ExplorationPrototype).GetField("leaves",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).GetValue(model);
    foreach(var column in leaves.Values){var spans=column.Spans;for(int i=0;i<spans.Length;i+=2)for(int y=spans[i];y<spans[i+1];y++){if(!soft.Contains(new Int3(column.X,y,column.Z)))throw new Exception("Soft span mismatch "+id);packedSoft++;assertions++;}}
    if(count!=prepared.CellCount||hard.Count!=packedHard||soft.Count!=packedSoft)throw new Exception("Count mismatch "+id+" count="+count+" expected="+prepared.CellCount+" hard="+hard.Count+" packed="+packedHard);assertions+=2;
    records.Add(new{id,cells=count,hard_cells=packedHard,cell_sha256=Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant(),elapsed_ms=watch.ElapsedMilliseconds});
   }
   Console.WriteLine(id+" PASS "+count+" cells "+watch.ElapsedMilliseconds+"ms");
  }
  // Independent copies are mutable, retained template never shares world buffers.
  var t=NaturalTreeTemplate.Prepare(0);var a=t.CreateWorld(out var an);var b=t.CreateWorld(out _);var c=a.Chunks.First();var lp=Chunk.Decode(Enumerable.Range(0,Chunk.Volume).First(i=>c.Value.Cells[i]!=MaterialId.Air));var pos=new Int3(c.Key.x*16+lp.x,c.Key.y*16+lp.y,c.Key.z*16+lp.z);a.Set(pos.x,pos.y,pos.z,MaterialId.Air);if(b.Get(pos)==MaterialId.Air)throw new Exception("Shared mutable template");assertions++;
  if(!NaturalTreeTemplate.TryRequest(0,out var task))throw new Exception("Async admission");var cached=task.GetAwaiter().GetResult();
  if(!NaturalTreeTemplate.TryRequest(0,out var repeated)||!object.ReferenceEquals(task,repeated))throw new Exception("Cache reuse");
  var asyncWorld=cached.CreateWorldAsync().GetAwaiter().GetResult();if(asyncWorld.Chunks.Values.Sum(c=>c.NonAirCount)!=3395)throw new Exception("Async copy");assertions+=3;
  var ray=typeof(AshenVoxel.Exploration.VegetationStreaming).GetMethod("HardRay",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Static);
  var hardColumns=new Dictionary<int,PrototypeCollisionColumn>{{0,new PrototypeCollisionColumn(0,0,new short[]{0,1})}};
  var softColumns=new Dictionary<int,PrototypeCollisionColumn>{{2,new PrototypeCollisionColumn(2,0,new short[]{0,1})}};
  var fixture=new ExplorationPrototype(0,0,"ray fixture",new float[]{0,0,0,.3f,.1f,.1f},Array.Empty<PrototypeMeshData>(),hardColumns,softColumns);
  double hit=(double)ray.Invoke(null,new object[]{fixture,-1d,.05d,.05d,1d,0d,0d,6d});if(Math.Abs(hit-1)>1e-8)throw new Exception("Exact hard ray");
  hit=(double)ray.Invoke(null,new object[]{fixture,.15d,.05d,.05d,1d,0d,0d,6d});if(!double.IsPositiveInfinity(hit))throw new Exception("Foliage must not steal a solid-matter cut");
  hit=(double)ray.Invoke(null,new object[]{fixture,-1d,.15d,.05d,1d,0d,0d,6d});if(!double.IsPositiveInfinity(hit))throw new Exception("Ray must miss empty voxel");assertions+=3;
  var raySticks=new Dictionary<int,PrototypeCollisionColumn>{{3,new PrototypeCollisionColumn(3,0,new short[]{0,1})}};
  fixture=new ExplorationPrototype(0,0,"ray through leaves",new float[]{0,0,0,.4f,.1f,.1f},Array.Empty<PrototypeMeshData>(),hardColumns,softColumns,raySticks);
  hit=(double)ray.Invoke(null,new object[]{fixture,.15d,.05d,.05d,1d,0d,0d,6d});if(Math.Abs(hit-.15)>1e-8)throw new Exception("Woody stick remains targetable through leaves");
  hit=(double)ray.Invoke(null,new object[]{fixture,.05d,.05d,.05d,1d,0d,0d,6d});if(hit!=0)throw new Exception("Close/inside ray must not have a blind zone");assertions+=2;
  assertions+=RootAndSoftChecks();
  File.WriteAllText(args[1],JsonSerializer.Serialize(new{status="PASS",scope=".NET CPU full-authority recipe replay and exact AVPM4 hard plus stick span equality; no Unity/render/FPS claim",assertions,records},new JsonSerializerOptions{WriteIndented=true}));
 }
}

