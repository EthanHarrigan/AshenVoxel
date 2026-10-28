using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Diagnostics;
using AshenVoxel.WorldScale;
using AshenVoxel.Exploration;
using UnityEngine;
static class Program
{
 const BindingFlags Flags=BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public;static int checks;
 static object Get(object x,string name)=>x.GetType().GetField(name,Flags).GetValue(x);
 static void Set(object x,string name,object value)=>x.GetType().GetField(name,Flags).SetValue(x,value);
 static object Call(object x,string name,params object[] args)=>x.GetType().GetMethod(name,Flags).Invoke(x,args);
 static void Check(bool yes,string name){if(!yes)throw new Exception(name);checks++;Console.WriteLine("PASS "+name);}
 sealed class Field:IWorldField,IClimateWorldField
 {
  public string SourceIdentity=>"streaming-controlled-forest";public SurfaceSample Sample(double x,double z)=>new SurfaceSample(120,6,6,true);
  public bool TrySampleClimate(double x,double z,out SurfaceClimate c){c=new SurfaceClimate(15,3,1300,30,-.0065);return true;}
 }
 static void Step(VegetationStreaming stream,Camera camera,double x=31,double z=31,double y=121.65)
 {stream.SetObserver(new WorldPoint(x,y,z),new WorldPoint(0,0,0));stream.Tick(camera);}
 static void Main(string[] args)
 {
  var v=new VegetationStreaming{TreeDistance=128,RockDistance=96,GroundCoverDistance=32,MaxResidentCells=128,MaxInstances=1000,CellsPerFrame=8,MatricesPerFrame=256,WorkMilliseconds=1000};
  var template=new Material();var field=new Field();var camera=new Camera();v.Initialize(field,new SurfaceProfile(enableSnow:false),new TextAsset{bytes=File.ReadAllBytes(args[1])},template,714103);
  Check(!v.Ready&&!v.IsReady(31,31,.32),"movement readiness is false while shared prototypes load");int loading=0;
  while(!v.Ready&&loading++<1000)Step(v,camera);
  Check(v.Ready&&v.LoadedModels==99&&loading>=52&&loading<=413,"all99 prototypes upload in bounded batches across the existing412LOD yields");
  Check(v.MeshBytes>250000000&&!template.enableInstancing,"large genuine tree meshes have observable bytes and template material stays immutable");
  int warm=0;while(!v.IsReady(31,31,16)&&warm++<1000)Step(v,camera);
  Check(v.IsReady(31,31,16)&&warm<1000,"minimum supported residency/instance budgets eventually publish all startup collision cells");
  Check(v.ResidentCells<=128&&v.ResidentInstances<=v.EffectiveInstanceLimit&&v.EffectiveInstanceLimit==4096,"population remains inside effective cell and instance caps");
  var cells=(IDictionary)Get(v,"cells");object near=cells[new PopulationKey(0,0,0)];
  Check(near!=null&&(bool)Get(near,"Published"),"near cell explicitly records visual publication independently of pending far work");
  Type cellType=near.GetType();var far=Activator.CreateInstance(cellType,true);Set(far,"Data",new PopulationCell(new PopulationKey(2,0,0),Array.Empty<PopulationInstance>(),0));Set(far,"Published",false);cells[new PopulationKey(2,0,0)]=far;
  Check(v.IsReady(31,31,.32),"new unpublished cell97m away does not freeze an already-ready player");
  // An asymmetric rotated rock catches a sign error that symmetric trunks
  // cannot expose. Rendering uses Unity's positiveY rotation: x'=cx+sz,z'=-sx+cz.
  var models=(IDictionary)Get(v,"models");object rockModel=models[217];var rockData=(ExplorationPrototype)Get(rockModel,"Data");
  var columns=(IDictionary)Get(rockData,"collision");double localX=0,localZ=0,localY=.15;bool asymmetric=false;double yaw=53*Math.PI/180,co=Math.Cos(yaw),si=Math.Sin(yaw);
  foreach(DictionaryEntry entry in columns)
  {
   object column=entry.Value;short[] spans=(short[])Get(column,"spans");if(spans[spans.Length-1]<3)continue;
   double lx=((short)Get(column,"X")+.5)*.1,lz=((short)Get(column,"Z")+.5)*.1;
   double wx=lx*co+lz*si,wz=-lx*si+lz*co,wrongX=wx*co+wz*si,wrongZ=-wx*si+wz*co;
   if(!rockData.BlocksLocal(wrongX-.01,localY-.01,wrongZ-.01,wrongX+.01,localY+.01,wrongZ+.01)){localX=lx;localZ=lz;asymmetric=true;break;}
  }
  Check(asymmetric,"rotated collision fixture includes an off-center asymmetric solid voxel");
  var saved=new List<DictionaryEntry>();foreach(DictionaryEntry entry in cells)saved.Add(entry);cells.Clear();
  var isolated=Activator.CreateInstance(cellType,true);var hardInstance=new PopulationInstance(217,new WorldPoint(40,120,40),53,1,99);Set(isolated,"Data",new PopulationCell(new PopulationKey(0,0,1),new[]{hardInstance},1));Set(isolated,"Published",true);cells[new PopulationKey(0,0,1)]=isolated;
  double worldX=40+localX*co+localZ*si,worldZ=40-localX*si+localZ*co;
  Check(v.BlocksBody(worldX-.004,120+localY-.004,worldZ-.004,worldX+.004,120+localY+.004,worldZ+.004),"hard-prop collision rotates back through the exact inverse of the rendered yaw");
  Check(v.TryGetSupportHeight(worldX,worldZ,.005,119,124,out double support)&&support>120,"rotated rock support uses the same local columns as its visible mesh");
  cells.Clear();foreach(DictionaryEntry entry in saved)cells.Add(entry.Key,entry.Value);
  var batches=(IList)Get(v,"batches");Check(batches.Count>0&&v.DrawCalls>0&&v.VisibleInstances>0,"actual cached pages dispatch shared instanced meshes");
  object batch=batches[0];var matrices=(Matrix4x4[])Get(batch,"Matrices");float before=matrices[0].m03;float boundBefore=((Bounds)Get(batch,"Bounds")).center.x;
  v.SetObserver(new WorldPoint(31,121.65,31),new WorldPoint(1024,0,0));
  Check(Math.Abs(matrices[0].m03-(before-1024))<.001&&Math.Abs(((Bounds)Get(batch,"Bounds")).center.x-(boundBefore-1024))<.001,"same-call origin rebase shifts visible matrices and page bounds together");
  v.SetObserver(new WorldPoint(31,121.65,31),new WorldPoint(0,0,0));
  Call(v,"BeginMatrices");v.MatricesPerFrame=100000;Call(v,"BuildMatrices",Stopwatch.GetTimestamp(),(double)v.WorkMilliseconds);
  var pending=(IList)Get(v,"nextBatches");Check(pending.Count<=32,"matrix-page finalization copies at most32 draw batches per call");
  if(pending.Count>0){var nextMatrix=(Matrix4x4[])Get(pending[0],"Matrices");float old=nextMatrix[0].m03;v.SetObserver(new WorldPoint(31,121.65,31),new WorldPoint(1024,0,0));Check(Math.Abs(nextMatrix[0].m03-(old-1024))<.001,"in-progress finalized pages rebase before atomic publication too");v.SetObserver(new WorldPoint(31,121.65,31),new WorldPoint(0,0,0));}
  var distant=Activator.CreateInstance(cellType,true);var distantKey=new PopulationKey(10,0,0);Set(distant,"Data",new PopulationCell(distantKey,Array.Empty<PopulationInstance>(),0));Set(distant,"Published",true);cells[distantKey]=distant;
  v.InvalidateRegion(640,0,704,64);Check(!cells.Contains(distantKey)&&object.ReferenceEquals(cells[new PopulationKey(0,0,0)],near)&&v.IsReady(31,31,.32),"far learned-source activation invalidates only far cells and preserves near movement readiness");
  v.InvalidateRegion(0,0,64,64);Check(object.ReferenceEquals(cells[new PopulationKey(0,0,0)],near),"512m source-change safety guard protects current hard-prop cells");
  v.SetObserver(new WorldPoint(5000,2000,5000),new WorldPoint(0,0,0));Check(v.IsReady(5000,5000,.32),"high flight does not wait for unreachable ground props");
  v.SetObserver(new WorldPoint(5000,121.65,5000),new WorldPoint(0,0,0));Check(!v.IsReady(5000,5000,.32),"descending into unloaded forest waits before reaching tree height");
  v.SetObserver(new WorldPoint(31,121.65,31),new WorldPoint(0,0,0));v.MatricesPerFrame=5000;
  for(int i=0;i<30;i++)Step(v,camera);GeometryUtility.CullAll=true;Step(v,camera);Check(v.VisibleInstances==0&&v.ShadowOnlyDrawCalls>0,"off-screen nearby shadow casters remain submitted without visible model draws");GeometryUtility.CullAll=false;
  // Continuous walking through cell boundaries after a64m prefetch: newly arriving
  // far cells may still queue, but local published collision must not flicker.
  v.TreeDistance=512;v.MaxResidentCells=512;v.MaxInstances=12000;v.CellsPerFrame=8;v.MatricesPerFrame=5000;v.WorkMilliseconds=1000;v.RefreshPopulation();
  for(int i=0;i<500&&!v.IsReady(31,31,.32);i++)Step(v,camera);int blocked=0;double x=31;
  for(int frame=0;frame<900;frame++){x+=4.0/60;Step(v,camera,x);if(!v.IsReady(x,31,.32))blocked++;}
  Check(blocked==0,"continuous4m/s walking across a64m cell boundary has no readiness flicker while far cells stream");
  Check(v.ResidentCells<=512&&v.ResidentInstances<=v.EffectiveInstanceLimit,"moving streamer respects residency bounds");
  Check(v.IsReady(x,31,.32),"walk endpoint is locally ready before a density-quality edit");
  var beforeDensity=(IDictionary)Get(v,"cells");var beforeNear=new Dictionary<PopulationKey,object>();foreach(DictionaryEntry entry in beforeDensity){PopulationKey key=(PopulationKey)entry.Key;double size=key.SizeMeters,dx=Math.Max(key.X*size-x,Math.Max(0,x-(key.X+1)*size)),dz=Math.Max(key.Z*size-31,Math.Max(0,31-(key.Z+1)*size));if(dx*dx+dz*dz<=(key.Layer==2?16*16:64*64))beforeNear.Add(key,entry.Value);}
  v.TreeDensity=.6f;Step(v,camera,x);
  Check(v.IsReady(x,31,.32)&&beforeNear.All(pair=>object.ReferenceEquals(beforeDensity[pair.Key],pair.Value)),"density changes preserve published near props and cannot spawn wood inside the player");
  var body=new WorldPoint(x,120.1,31);for(int i=0;i<100;i++)v.WeightedSoftVoxels(body,.28,1.8);
  var queryClock=new Stopwatch();long allocatedBefore=GC.GetAllocatedBytesForCurrentThread();queryClock.Start();
  for(int i=0;i<10000;i++)v.WeightedSoftVoxels(body,.28,1.8);
  queryClock.Stop();long allocatedQuery=GC.GetAllocatedBytesForCurrentThread()-allocatedBefore;
  Check(allocatedQuery==0,"10000 warmed actual soft-span body queries allocate no per-query managed storage");
  double queryMicroseconds=queryClock.Elapsed.TotalMilliseconds*1000/10000;
  long originalMeshBytes=v.MeshBytes;var owned=(Material)Get(v,"material");v.DisposeRuntime();Check(owned.Destroyed&&!template.Destroyed&&v.ResidentCells==0&&v.MeshBytes==0&&!v.Ready,"cleanup releases owned resources and leaves shared assets intact");
  Resources.UseImpostors=true;
  var sprites=new VegetationStreaming{TreeDistance=128,RockDistance=96,GroundCoverDistance=32,MaxResidentCells=128,CellsPerFrame=8,WorkMilliseconds=1000};
  sprites.Initialize(field,new SurfaceProfile(enableSnow:false),new TextAsset{bytes=File.ReadAllBytes(args[1])},template,714103);
  for(int i=0;i<1000&&!sprites.Ready;i++)Step(sprites,camera);
  Check(sprites.Ready&&sprites.Horizon!=null,"baked-atlas marker activates the actual impostor and stand-horizon path");
  Check(sprites.MeshBytes<originalMeshBytes,"impostor levels reduce shared mesh residency");
  for(int i=0;i<10000&&sprites.Horizon.ResidentStands==0&&sprites.Horizon.Error==null;i++)
  {sprites.Horizon.Tick(camera,new WorldPoint(31,550,31),new WorldPoint(0,0,0),1400,30000,1);System.Threading.Thread.Sleep(1);}
  Check(sprites.Horizon.Error==null&&sprites.Horizon.ResidentStands>0&&sprites.Horizon.ResidentStands<=16384,"asynchronous 30km forest HLOD completes within the256tile/16384sample cap");
  int stands=sprites.Horizon.ResidentStands;sprites.Horizon.Tick(camera,new WorldPoint(31,550,31),new WorldPoint(1024,0,0),1400,30000,1);
  Check(sprites.Horizon.ResidentStands==stands&&sprites.Horizon.DrawCalls>0,"origin rebase keeps distant stands published and drawable");
  for(int i=0;i<10000&&sprites.Horizon.PublishedFarDistance!=100000&&sprites.Horizon.Error==null;i++)
  {sprites.Horizon.Tick(camera,new WorldPoint(31,550,31),new WorldPoint(1024,0,0),1400,100000,1);System.Threading.Thread.Sleep(1);}
  Check(sprites.Horizon.Error==null&&sprites.Horizon.PublishedFarDistance==100000&&sprites.Horizon.FarthestStandDistance>80000,"100km forest planning actually retains stands beyond80km rather than only raising a draw limit");
  Check(sprites.Horizon.ResidentStands<=16384,"100km coverage lowers spatial detail without increasing the stand count cap");
  sprites.Horizon.Invalidate();sprites.DisposeRuntime();Resources.UseImpostors=false;
  CoverageCheck.Run(args[1]);
  File.WriteAllText(args[0],JsonSerializer.Serialize(new{status="PASS",checks,scope="Actual VegetationStreaming executed with numerical Unity substitutes and real99-model pack; no native Unity/GPU/FPS claim",startup_lod_frames=loading,near_warmup_frames=warm,walk_blocked_frames=blocked,soft_query_microseconds=queryMicroseconds,soft_query_allocated_bytes=allocatedQuery,soft_query_iterations=10000},new JsonSerializerOptions{WriteIndented=true}));Console.WriteLine("All "+checks+" exploration streaming checks passed.");
 }
}
