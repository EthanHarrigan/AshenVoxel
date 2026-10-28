using System;
using System.IO;
using System.Linq;
using System.Diagnostics;
using System.Collections.Generic;
using System.Numerics;
using System.Text.Json;
using AshenVoxel.Core;
using AshenVoxel.Exploration.Experimental;
static class Program
{
 static int assertions;static readonly List<object> records=new List<object>();
 static void Check(bool ok,string message){assertions++;if(!ok)throw new Exception(message);}
 static List<Int3> CutLayer(VoxelWorld world,int y)
 {
  var result=new List<Int3>();foreach(var pair in world.Chunks)for(int i=0;i<Chunk.Volume;i++){var local=Chunk.Decode(i);var p=new Int3(pair.Key.x*16+local.x,pair.Key.y*16+local.y,pair.Key.z*16+local.z);if(p.y==y&&MaterialCatalog.BlocksActorBody(world.Get(p)))result.Add(p);}return result;
 }
 static void Tick(NaturalTreePhysics p,float dt,int support=256,int bodies=8192){p.Step(dt,support,bodies);Check(p.Conserved,"conservation");}
 static NaturalTreePhysics Birch(bool slope)
 {
  var w=NaturalTreeTemplate.Replay(0,out var anchor,out _);var cut=CutLayer(w,anchor.y+8);var p=new NaturalTreePhysics(w,anchor,.3f){TerrainHeight=(x,z)=>slope?.1f*x:0,SuctionEnabled=false};
  Check(p.IntactAnchoredCells()==p.Count,"birch initial anchored");Check(cut.Count>0&&cut.Count<=32,"birch cut layer bounded");foreach(var cell in cut)Check(p.Cut(cell),"birch queued cut");return p;
 }
 static void RealBirch(bool slope)
 {
  var p=Birch(slope);bool detached=false,loose=false;int islandMax=0;float age=0,firstLoose=0,minimumUp=1;
  for(int frame=0;frame<1500;frame++)
  {
   Tick(p,1f/60,64);if(p.IslandCells>0&&!detached){detached=true;age=0;}if(detached)age+=1f/60;
   islandMax=Math.Max(islandMax,p.IslandCells);
#if !BASELINE
   for(int body=0;body<8;body++)if(p.BodyPose(body,out _,out var rotation))minimumUp=Math.Min(minimumUp,Vector3.Transform(Vector3.UnitY,rotation).Y);
#endif
   if(detached&&p.IslandCells<islandMax){loose=true;firstLoose=age;break;}
  }
  Check(detached,"real birch detached section");Check(loose,"real birch settles and releases");Check(firstLoose>=.59f,"section retained at least .6 seconds");
#if !BASELINE
  Check(minimumUp<.8f,"actual tall birch visibly topples before release");
#endif
  for(int i=0;i<600;i++)Tick(p,1f/60);
  #if BASELINE
  for(int i=0;i<p.Count;i++){var c=p.Read(i);if(c.Owner==NaturalTreePhysics.Ownership.Individual)Check(c.Position.Y-.05f>=(slope?.1f*c.Position.X:0)-1e-4,"debris above floor");}
#else
  int logical=0;for(int i=0;i<p.PendingDebris;i++){int index=p.LooseIndex(i);var c=p.Read(index);logical+=p.LooseWeight(index);Check(c.Position.Y-.05f>=(slope?.1f*c.Position.X:0)-1e-4,"debris above floor");}Check(logical==p.IndividualCells,"parcel weight exact");
#endif
  int remaining=p.SourceCells;p.SuctionEnabled=true;p.Hand=new Vector3(0,1,0);
#if !BASELINE
  p.CollectionRadius=100;
#endif
  for(int i=0;i<1800&&p.IndividualCells+p.IslandCells>0;i++)Tick(p,1f/60);
  Check(p.CargoCells==p.Count-remaining,"automatic pickup exact count");Check(p.Cargo.Values.Sum()==p.CargoCells,"cargo material sum");
  records.Add(new{test=slope?"real birch slope":"real birch flat",count=p.Count,island_max=islandMax,first_release_age=firstLoose,minimum_up=minimumUp,cargo=p.CargoCells,source=p.SourceCells});
 }
 static NaturalTreePhysics Block(int count)
 {
  var world=new VoxelWorld(32,40,32,.1f,1);world.Set(10,0,10,MaterialId.Wood);world.Set(10,1,10,MaterialId.Wood);
  int added=0;for(int y=2;y<40&&added<count;y++)for(int z=0;z<20&&added<count;z++)for(int x=0;x<20&&added<count;x++){world.Set(x,y,z,MaterialId.Wood,0xff804020);added++;}
  var p=new NaturalTreePhysics(world,new Int3(10,0,10),0){TerrainHeight=(x,z)=>0,SuctionEnabled=false};Check(p.Cut(new Int3(10,1,10)),"block cut");return p;
 }
 static void LowBudget()
 {
  var p=Block(200);int frames=0;while(frames++<20000&&(p.PendingSupport>0||p.IslandCells>0||p.SourceCells>1))Tick(p,1f/60,1,1);
  Check(p.SourceCells==1&&p.IslandCells==0&&p.IndividualCells==201,"budget1 eventually finishes retained support and release");records.Add(new{test="budget1",frames});
 }
 static void QueuedCuts()
 {
#if !BASELINE
  var world=new VoxelWorld(5,8,5,.1f,1);for(int x=0;x<3;x++)for(int y=0;y<4;y++)world.Set(x,y,0,MaterialId.Wood);
  var p=new NaturalTreePhysics(world,new Int3(0,0,0),0){TerrainHeight=(x,z)=>0,SuctionEnabled=false};
  for(int x=0;x<3;x++)Check(p.Cut(new Int3(x,1,0)),"queued repeated cut admitted");Check(p.PendingCuts==2,"queued cut retained");
  int frames=0;while(frames++<3000&&(p.PendingCuts>0||p.PendingSupport>0||p.SourceCells>3)){Tick(p,1f/60,1,1);Check(p.LastSupportCandidates<=1,"support candidate allowance");}
  Check(p.PendingCuts==0&&p.PendingSupport==0&&p.SourceCells==3&&p.IndividualCells+p.IslandCells==9,"all queued cuts applied once cuts="+p.PendingCuts+" support="+p.PendingSupport+" source="+p.SourceCells+" individual="+p.IndividualCells+" island="+p.IslandCells);records.Add(new{test="queued lowbudget cuts",frames,source=p.SourceCells,individual=p.IndividualCells});
#endif
 }
 static void IslandSlotLiveness()
 {
#if !BASELINE
  var world=new VoxelWorld(32,16,32,.1f,1);world.Set(0,0,0,MaterialId.Wood);world.Set(0,1,0,MaterialId.Wood);
  for(int i=0;i<12;i++)world.Set(4+(i%4)*3,8,4+(i/4)*3,MaterialId.Wood);
  var p=new NaturalTreePhysics(world,new Int3(0,0,0),0){TerrainHeight=(x,z)=>0,SuctionEnabled=false};Check(p.Cut(new Int3(0,1,0)),"slot fixture cut");
  bool waited=false,movedWhileWaiting=false;int published=-1,pending=-1,delay=0,frames=0;
  while(frames++<4000&&(p.PendingSupport>0||p.IslandCells>0||p.SourceCells>1))
  {
   if(pending!=p.StructureRevision){pending=p.StructureRevision;delay=3;}
   if(delay--<=0)published=pending;
   p.MovementEnabled=!p.StructureBusy&&published==p.StructureRevision;
   waited|=p.WaitingForIslandSlot;movedWhileWaiting|=p.WaitingForIslandSlot&&p.MovementEnabled;
   Tick(p,1f/60,4,4);
  }
  Check(waited,"more than eight components uses waiting slot");Check(movedWhileWaiting,"waiting slot permits movement after fake mesh publication");
  Check(p.PendingSupport==0&&p.IslandCells==0&&p.SourceCells==1&&p.IndividualCells==13,"all twelve sections drain without slot/mesh deadlock");
  records.Add(new{test="twelve disconnected sections delayed mesh gate",frames,waited,movedWhileWaiting,source=p.SourceCells,individual=p.IndividualCells});
#endif
 }
 static void AdaptiveHundredThousand()
 {
#if !BASELINE
  var world=new VoxelWorld(100,12,100,.1f,1);world.Set(0,0,0,MaterialId.Wood);world.Set(0,1,0,MaterialId.Wood);
  for(int y=2;y<12;y++)for(int z=0;z<100;z++)for(int x=0;x<100;x++)world.Set(x,y,z,MaterialId.Wood,0xff735231);
  var p=new NaturalTreePhysics(world,new Int3(0,0,0),0){TerrainHeight=(x,z)=>0,SuctionEnabled=false};
  Check(p.Count==100002&&p.DebrisWeight==25,"adaptive hundred-thousand weight25");Check(p.IntactAnchoredCells()==p.Count,"hundred-thousand connected through stem");
  Check(p.Cut(new Int3(0,1,0)),"hundred-thousand stem cut");bool partialRetained=false,detached=false;int frames=0,peakRepresentatives=0;
  var timer=Stopwatch.StartNew();
  while(frames++<30000&&(p.PendingSupport>0||p.IslandCells>0||p.SourceCells>1))
  {
   Tick(p,1f/60,4096,16);detached|=p.IslandCells>0;peakRepresentatives=Math.Max(peakRepresentatives,p.PendingDebris);
   if(p.IndividualCells>1&&p.PendingDebris==1)partialRetained=true;
  }
  Check(detached,"hundred-thousand detached as connected island");Check(partialRetained,"partial weighted groups retained across release frames");
  Check(p.SourceCells==1&&p.IslandCells==0&&p.IndividualCells==100001,"hundred-thousand release exact logical counts");
  Check(p.PendingDebris==4001,"hundred-thousand adaptive exact4001 representatives");long logical=0;
  for(int i=0;i<p.PendingDebris;i++){int index=p.LooseIndex(i);int weight=p.LooseWeight(index);Check(weight==1||weight==25,"hundred-thousand exact full/tail weight");logical+=weight;}
  Check(logical==100001,"hundred-thousand representative weights sum");
  for(int i=0;i<300;i++)Tick(p,1f/60,4096,8192);
  Check(p.AwakeDebris==0&&p.DormantDebris==4001&&p.LastBodyCandidates==0,"all4001 representatives dormant with zero scheduled visits");
  for(int i=0;i<p.PendingDebris;i++){var c=p.Read(p.LooseIndex(i));Check(c.Position.Y-.05f>=-1e-4,"hundred-thousand representatives above floor");}
  p.SuctionEnabled=true;p.CollectionRadius=1000;p.Hand=new Vector3(0,1,0);
  for(int i=0;i<1800&&p.IndividualCells>0;i++)Tick(p,1f/60,4096,8192);
  Check(p.CargoCells==100001&&p.SourceCells==1&&p.PendingDebris==0,"hundred-thousand eventual weighted cargo exact");
  Check(p.Cargo.Where(x=>x.Key.Tint==0xff735231).Sum(x=>x.Value)==100000,"hundred-thousand exact tint cargo");
  int resolvedCargo=0,resolvedSource=0;for(int i=0;i<p.Count;i++){var local=p.ReadLocal(i);var pose=p.Read(i);Check(local.Owner==pose.Owner,"follower local/world owner agrees");if(pose.Owner==NaturalTreePhysics.Ownership.Cargo)resolvedCargo++;else if(pose.Owner==NaturalTreePhysics.Ownership.Source)resolvedSource++;else Check(false,"follower resolves final cargo/source owner");}
  Check(resolvedCargo==100001&&resolvedSource==1,"all hundred-thousand followers resolve cargo ownership");
  records.Add(new{test="100000 connected upper cells adaptive parcels",count=p.Count,weight=p.DebrisWeight,representatives=4001,dormant_idle_candidates=0,peakRepresentatives,logical=100001,partialRetained,frames,source=p.SourceCells,cargo=p.CargoCells,resolvedCargo,resolvedSource,total_cpu_ms=timer.Elapsed.TotalMilliseconds});
#endif
 }
 static void Parcels()
 {
#if !BASELINE
  foreach(int weight in new[]{1,2,4,7})
  {
   var p=Block(27);p.DebrisWeight=weight;for(int i=0;i<1000;i++)Tick(p,1f/60);
   int logical=0;for(int i=0;i<p.PendingDebris;i++){int index=p.LooseIndex(i);int w=p.LooseWeight(index);Check(w>=1&&w<=weight,"parcel range");logical+=w;}
   Check(logical==28&&p.IndividualCells==28,"nonmultiple tail weight conserved");int representatives=p.PendingDebris;
   p.SuctionEnabled=true;p.CollectionRadius=100;p.Hand=new Vector3(0,1,0);for(int i=0;i<600&&p.IndividualCells>0;i++)Tick(p,1f/60);
   Check(p.CargoCells==28&&p.Cargo.Values.Sum()==28,"weighted pickup exact");Check(p.Cargo.Where(x=>x.Key.Tint==0xff804020).Sum(x=>x.Value)==27,"weighted tint exact");records.Add(new{test="weighted parcel",weight,representatives,logical,cargo=p.CargoCells});
  }
#endif
 }
 static void Rocks()
 {
  foreach(int id in Enumerable.Range(0,16).Concat(Enumerable.Range(200,60))){var w=NaturalTreeTemplate.Replay(id,out var anchor,out _);var p=new NaturalTreePhysics(w,anchor,0);Check(p.IntactAnchoredCells()==p.Count,"prototype anchored "+id);}records.Add(new{test="all 76 tree and rock initial support"});
 }
 static void Punches()
 {
#if !BASELINE
  foreach(var material in new[]{MaterialId.Wood,MaterialId.Stone})foreach(float yaw in new[]{0f,.7f,1.5707963f})
  foreach(var test in new[]{(-1f,.05f,true),(-2f,.05f,true),(-2.5f,.05f,true),(.05f,.05f,true),(-2f,.16f,true),(-2f,.25f,false),(-2.51f,.05f,false)})
  {
   var w=new VoxelWorld(16,16,16,.1f,1);w.Set(0,0,0,material,0xff704020);var p=new NaturalTreePhysics(w,new Int3(0,0,0),yaw);
   var rotation=Quaternion.CreateFromAxisAngle(Vector3.UnitY,yaw);
   bool hit=p.CutPunch(Vector3.Transform(new Vector3(test.Item1,.05f,test.Item2),rotation),Vector3.Transform(Vector3.UnitX,rotation));
   Check(hit==test.Item3,"punch range/assist "+material+" yaw="+yaw+" "+test);Check(p.Conserved&&p.IndividualCells==(hit?1:0),"punch conservation");
   Check(w.Get(0,0,0)==(hit?MaterialId.Air:material),"punch uses authoritative Set");
  }
  var world=new VoxelWorld(32,16,16,.1f,1);world.Set(0,0,1,MaterialId.Wood);world.Set(10,0,0,MaterialId.Stone);
  var solver=new NaturalTreePhysics(world,new Int3(0,0,0),0);
  Check(solver.CutPunch(new Vector3(-1,.05f,.05f),Vector3.UnitX),"center precedence punch");
  Check(world.Get(10,0,0)==MaterialId.Air&&world.Get(0,0,1)==MaterialId.Wood,"exact center wins over closer assisted cell");
  Check(!solver.CutPunch(Vector3.Zero,new Vector3(float.NaN,0,0))&&!solver.CutPunch(Vector3.Zero,Vector3.Zero),"invalid punch rejected");
  Check(!solver.CutPunch(new Vector3(float.PositiveInfinity,0,0),Vector3.UnitX),"infinite origin rejected");
  records.Add(new{test="2.5m punch; 1m/2m/edge/inside/assist/miss; yaw and center priority",cases=42});
#endif
 }
 static void SphereBrush()
 {
#if !BASELINE
  var world=new VoxelWorld(16,16,16,.1f,1);
  for(int z=0;z<7;z++)for(int y=0;y<7;y++)for(int x=0;x<7;x++)world.Set(x,y,z,MaterialId.Leaves,0xff517832);
  var p=new NaturalTreePhysics(world,new Int3(0,0,0),0){SuctionEnabled=false};
  Check(p.CutSphere(new Int3(3,3,3))==81,"25cm spherical brush removes81 cells, not full bounding cube");
  Check(world.Get(5,3,3)==MaterialId.Air&&world.Get(5,5,3)==MaterialId.Leaves&&world.Get(0,0,0)==MaterialId.Leaves,"sphere axis and diagonal boundaries");
  Check(p.IndividualCells==81&&p.SourceCells==262&&p.Conserved,"soft sphere exact ownership");
  Check(p.CutSphere(new Int3(5,3,3))>0&&p.PendingCuts==1,"brush queued as one operation while support runs");
  for(int i=0;i<100&&p.PendingSupport+p.PendingCuts>0;i++)p.Step(.02f,256);
  Check(p.PendingCuts==0&&p.PendingSupport==0&&p.Conserved,"queued sphere drains without lost work");
  foreach(var material in new[]{MaterialId.Leaves,MaterialId.Fruit,MaterialId.Grass})
  {
   var micro=new VoxelWorld(16,16,16,.1f,1);micro.Set(0,0,0,material,0xff517832);
   var q=new NaturalTreePhysics(micro,new Int3(0,0,0),0);
   Check(q.CutPunch(new Vector3(-1,.05f,.05f),Vector3.UnitX)&&q.IndividualCells==1&&q.Conserved,"single-cell plant punch "+material);
  }
  var tower=new VoxelWorld(16,32,16,.1f,1);for(int y=0;y<20;y++)tower.Set(0,y,0,MaterialId.Wood);
  var rigid=new NaturalTreePhysics(tower,new Int3(0,0,0),0){ExternalRigidBodies=true,SuctionEnabled=false};rigid.Cut(new Int3(0,1,0));rigid.Step(.02f);
  Check(rigid.RigidBodyActive(0)&&rigid.RigidBodyShape(0,out var probes,out float mass)&&probes.Length==14&&mass>0,"bounded native section shape");
  rigid.ReadRigidBody(0,new Vector3(3,4,5),Quaternion.Identity,Vector3.UnitY,false,false,.02f);rigid.Step(.02f);
  rigid.BodyPose(0,out var pose,out _);Check(pose==new Vector3(3,4,5),"native pose has no competing analytic gravity or torque");
  for(int i=0;i<200;i++)rigid.ReadRigidBody(0,pose,Quaternion.Identity,Vector3.Zero,false,true,.02f);
  Check(rigid.RigidBodyActive(0),"airborne sleeping report cannot dismantle a section");
  for(int i=0;i<31;i++)rigid.ReadRigidBody(0,pose,Quaternion.Identity,Vector3.Zero,true,true,.02f);
  Check(!rigid.RigidBodyActive(0),"grounded settled native body begins bounded debris release");
  rigid.Step(.02f);Check(rigid.Conserved&&rigid.IslandCells==0,"native release conserves matter");
  records.Add(new{test="sphere brush; soft microplants; external native pose and contact gate",radius=.25});
#endif
 }
 static void Perf(bool large,bool airborne=false,bool collecting=false,bool fullParticles=false)
 {
  var p=large?Block(12000):Birch(false);
#if !BASELINE
  if(fullParticles)p.DebrisWeight=1;
#endif
  for(int i=0;i<600;i++)p.Step(1f/60,8192,8192);
  #if !BASELINE
  if(airborne)
  {
   p.SuctionEnabled=true;p.CollectionRadius=100000;p.Hand=new Vector3(0,1000,0);
   for(int n=0;n<10000;n++){p.Step(1f/60,8192,100000);if(p.AwakeDebris==p.PendingDebris&&p.PendingWakeCandidates==0)break;}
   Check(p.AwakeDebris==p.PendingDebris,"airborne benchmark actually wakes every representative");
   p.SuctionEnabled=false;p.TerrainHeight=(x,z)=>-100000;
  }
  if(collecting){p.SuctionEnabled=true;p.CollectionRadius=100000;p.Hand=new Vector3(0,1,0);}
  int representatives=p.PendingDebris;
#else
  int representatives=p.IndividualCells;
#endif
  string weighting="weight1";
#if !BASELINE
  weighting=fullParticles?"weight1":"weight4";
#endif
  int samples=collecting?120:240;var times=new double[samples];long allocated=GC.GetAllocatedBytesForCurrentThread();double supplied=0;
  for(int i=0;i<samples;i++){long start=Stopwatch.GetTimestamp();p.Step(1f/60,8192,8192);times[i]=(Stopwatch.GetTimestamp()-start)*1000.0/Stopwatch.Frequency;supplied+=1.0/60;Check(p.Conserved,"perf conservation");}
  allocated=GC.GetAllocatedBytesForCurrentThread()-allocated;Array.Sort(times);records.Add(new{test=(large?"12000 debris perf":"3395 birch perf")+(airborne?" active air":collecting?" suction first120":" settled")+" "+weighting,count=p.Count,representatives_at_start=representatives,body_budget=8192,representative_visit_bound_frames=(representatives+8191)/8192,individual=p.IndividualCells,island=p.IslandCells,physics_debt=p.PhysicsDebtSeconds,supplied_seconds=supplied,cargo=p.CargoCells,p50_ms=times[samples/2],p95_ms=times[(int)(samples*.95)],max_ms=times[samples-1],allocated_bytes=allocated});
#if !BASELINE
  Check(p.PhysicsDebtSeconds==0,"cheap debris no global debt");
#endif
 }
 static void LargeAttachedLocalCut()
 {
#if !BASELINE
  var world=NaturalTreeTemplate.Replay(11,out var anchor,out _);var build=Stopwatch.StartNew();var p=new NaturalTreePhysics(world,anchor,0){SuctionEnabled=false};double construction=build.Elapsed.TotalMilliseconds;
  Int3 bark=new Int3(-1,-1,-1);foreach(var pair in world.Chunks)for(int i=0;i<Chunk.Volume;i++)if(pair.Value.Cells[i]==MaterialId.Wood)
  {var l=Chunk.Decode(i);var c=new Int3(pair.Key.x*16+l.x,pair.Key.y*16+l.y,pair.Key.z*16+l.z);if(c.y==anchor.y+10&&c.x>bark.x)bark=c;}
  Check(bark.x>=0,"large redwood bark fixture");int removed=p.CutSphere(bark);Check(removed>0,"large redwood bark sphere removed");
  int iterations=0,work=0;var timer=Stopwatch.StartNew();while(p.PendingSupport>0&&iterations++<10000){p.StepSupport(32768,1.25);work+=p.LastSupportCandidates;Check(p.Conserved,"localized large cut conserved");}
  Check(p.PendingSupport==0&&p.IslandCells==0&&p.IndividualCells==removed,"side bark edit retains still-attached tree");
  Check(work<100000,"near-root bark edit avoids whole million-cell flood");
  records.Add(new{test="mature redwood11 attached near-root sphere localized support",cells=p.Count,removed,work,iterations,construction_cpu_ms=construction,support_cpu_ms=timer.Elapsed.TotalMilliseconds});
#endif
 }
 #if !BASELINE
 static NaturalTreePhysics DormantFixture(int count)
 {
  var world=new VoxelWorld(count*4,1,1,.1f,1);
  for(int n=0;n<count;n++)world.Set(n*4,0,0,n%2==0?MaterialId.Wood:MaterialId.Stone,0xff886633);
  var p=new NaturalTreePhysics(world,new Int3(0,0,0),0){SuctionEnabled=false,TerrainHeight=(x,z)=>0};
  for(int n=0;n<count;n++){Check(p.Cut(new Int3(n*4,0,0)),"fixture cut");while(p.PendingSupport>0)p.StepSupport(8192);}
  p.Step(.02f);Check(p.DormantDebris==count&&p.AwakeDebris==0&&p.PendingDebris==count,"all leaders dormant and retained for render");return p;
 }
 static void ScheduledTick(NaturalTreePhysics p,int budget=1)
 {
  p.Step(.02f,8192,budget);Check(p.Conserved,"scheduled conservation");Check(p.LastBodyCandidates<=budget,"combined wake/body budget");
  Check(p.PendingDebris==p.AwakeDebris+p.DormantDebris,"awake dormant partition");
 }
 static void AwakeDormant()
 {
  var p=DormantFixture(500);int groundCalls=0;p.TerrainHeight=(x,z)=>{groundCalls++;return 0;};
  int revision=p.PoseRevision;var poses=Enumerable.Range(0,p.PendingDebris).Select(k=>p.Read(p.LooseIndex(k)).Position).ToArray();
  for(int n=0;n<600;n++){ScheduledTick(p);Check(p.LastBodyCandidates==0&&p.LastWakeCandidates==0,"idle dormant has zero scheduled candidates");}
  Check(groundCalls==0&&p.PoseRevision==revision,"idle has no sampling or pose invalidation");
  Check(poses.SequenceEqual(Enumerable.Range(0,p.PendingDebris).Select(k=>p.Read(p.LooseIndex(k)).Position)),"idle visible positions retained");
  p.Hand=new Vector3(-100,1,0);p.SuctionEnabled=true;ScheduledTick(p);Check(p.PendingWakeCandidates==0,"distant pickup rejected by dormant bounds");
  int tail=p.LooseIndex(499);var tailPosition=p.Read(tail).Position;p.CollectionRadius=.15f;p.Hand=tailPosition;
  for(int n=0;n<2000&&p.CargoCells==0;n++)ScheduledTick(p);
  Check(p.CargoCells==1&&p.Read(tail).Owner==NaturalTreePhysics.Ownership.Cargo,"budget1 reaches tail pickup and resolves ownership");
  for(int n=0;n<1500&&p.PendingWakeCandidates>0;n++)ScheduledTick(p);
  Check(p.PendingWakeCandidates==0&&p.DormantDebris==499,"swap removal scan drains without lost candidates");
  p.SuctionEnabled=false;ScheduledTick(p);p.Hand=p.Read(p.LooseIndex(0)).Position;p.SuctionEnabled=true;
  for(int n=0;n<2000&&p.CargoCells<2;n++)ScheduledTick(p);
  Check(p.CargoCells==2,"enable pickup wakes existing sleepers");
  p.CollectionRadius=1000;p.Hand=new Vector3(100,1,0);int steps=0;
  while(p.IndividualCells>0&&steps++<20000){p.Hand.X=100+(steps%2)*.001f;ScheduledTick(p);}
  Check(p.CargoCells==500&&p.PendingDebris==0&&p.AwakeDebris==0&&p.DormantDebris==0&&p.PendingWakeCandidates==0,"moving pickup/radius growth/scan mutation make fair budget1 progress");
  Check(p.Cargo.Values.Sum()==500&&p.Collected(MaterialId.Wood)==250&&p.Collected(MaterialId.Stone)==250,"material/tint accounting exact after wake");

  p=DormantFixture(1);int leader=p.LooseIndex(0);var start=p.Read(leader).Position;
  for(int n=0;n<1000;n++)p.Step(.25f);
  p.Hand=start+Vector3.UnitX;p.CollectionRadius=2;p.SuctionEnabled=true;ScheduledTick(p,8);
  Check(p.Read(leader).Position==start&&p.CargoCells==0,"wake excludes250 seconds of dormant time");ScheduledTick(p,8);
  Check(Vector3.Distance(start,p.Read(leader).Position)<=.20001f&&p.CargoCells==0,"first pickup motion uses fresh elapsed time");

  p=DormantFixture(1);leader=p.LooseIndex(0);
  for(int cycle=0;cycle<20;cycle++)
  {
   var position=p.Read(leader).Position;Check(p.Kick(position-Vector3.UnitY*.1f,Vector3.UnitY)==1,"kick hits dormant leader");
   Check(p.AwakeDebris==1&&p.DormantDebris==0,"kick wakes immediately");ScheduledTick(p);
   Check(p.Read(leader).Position.Y>position.Y,"kick produces motion");
   for(int n=0;n<120&&p.AwakeDebris>0;n++)ScheduledTick(p);
   Check(p.DormantDebris==1&&Math.Abs(p.Read(leader).Position.Y-.05f)<1e-6,"repeated kicks do not accumulate phantom pile occupancy");
  }
  Check(p.Kick(Vector3.Zero,new Vector3(float.NaN,0,0))==0,"invalid kick rejected");

  p=DormantFixture(8);leader=p.LooseIndex(7);float terrain=0;groundCalls=0;
  p.TerrainHeight=(x,z)=>{groundCalls++;return x>2.6f?terrain:0;};
  p.NotifyTerrainChanged(100,100,101,101);Check(p.PendingWakeCandidates==0,"distant terrain rejected by bounds");
  terrain=1;p.NotifyTerrainChanged(2.7f,-.1f,2.9f,.2f);ScheduledTick(p);
  p.NotifyTerrainChanged(2.65f,-.2f,2.95f,.3f); // Coalesce while a budget1 scan is in progress.
  for(int n=0;n<100&&(p.PendingWakeCandidates>0||p.AwakeDebris>0);n++)ScheduledTick(p);
  Check(p.PendingWakeCandidates==0&&p.DormantDebris==8&&Math.Abs(p.Read(leader).Position.Y-1.05f)<1e-5,"raised terrain wakes tail through coalesced bounded events");
  Check(groundCalls<=2,"terrain wake samples affected sleeper only");
  terrain=-2;p.NotifyTerrainChanged(2.7f,-.1f,2.9f,.2f);
  for(int n=0;n<200&&(p.PendingWakeCandidates>0||p.AwakeDebris>0);n++)ScheduledTick(p);
  Check(Math.Abs(p.Read(leader).Position.Y+1.95f)<1e-5&&p.DormantDebris==8,"removed support makes sleeper fall onto new terrain");
  for(int k=0;k<7;k++)Check(Math.Abs(p.Read(p.LooseIndex(k)).Position.Y-.05f)<1e-6,"unaffected sleepers retain poses");
  terrain=float.NaN;p.NotifyTerrainChanged(2.7f,-.1f,2.9f,.2f);
  for(int n=0;n<50;n++)ScheduledTick(p);
  Check(p.AwakeDebris==1&&p.DormantDebris==7,"missing ground retains awake retry");terrain=-3;
  for(int n=0;n<100&&p.AwakeDebris>0;n++)ScheduledTick(p);
  Check(p.DormantDebris==8&&Math.Abs(p.Read(leader).Position.Y+2.95f)<1e-5,"ground readiness completes retained work");
  records.Add(new{test="awake dormant scheduling",idle_parcels=500,idle_steps=600,idle_candidates=0,moving_pickup_budget=1,moving_pickup_steps=steps,kick_cycles=20,terrain="raise/lower/missing/recovery/coalesced",partition="all parcels remain renderable until exact cargo credit"});
 }
 #endif
 static void Main(string[] args)
 {
  string mode=args[1];
#if !BASELINE
  if(mode=="awake"||mode=="current")AwakeDormant();
#endif
  if(mode=="awake"){}else if(mode=="baseline")Perf(false);else if(mode=="perf"){Rocks();Perf(false);Perf(true);Perf(true,true);Perf(true,false,true);Perf(true,true,false,true);Perf(false,false,false,true);}else{LargeAttachedLocalCut();Punches();SphereBrush();RealBirch(false);RealBirch(true);LowBudget();QueuedCuts();IslandSlotLiveness();AdaptiveHundredThousand();Parcels();Rocks();Perf(false);Perf(true);Perf(true,true);Perf(true,false,true);Perf(true,true,false,true);Perf(false,false,false,true);}
  File.WriteAllText(args[0],JsonSerializer.Serialize(new{status="PASS",scope=".NET CPU approximate debris; not native Unity/render/FPS",assertions,records},new JsonSerializerOptions{WriteIndented=true}));Console.WriteLine("PASS "+assertions);
 }
}

