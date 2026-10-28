using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.Json;
using AshenVoxel.Core;
using AshenVoxel.WorldScale;
using AshenVoxel.Exploration;
using AshenVoxel.WorldScaleRuntime;
using UnityEngine;
using UnityEngine.InputSystem;
static class Program
{
 static int checks;
 static void Check(bool value,string name){if(!value)throw new Exception("FAIL "+name);checks++;Console.WriteLine("PASS "+name);}
 static void Reject(Action a,string name){bool failed=false;try{a();}catch(ArgumentException){failed=true;}Check(failed,name);}
 sealed class Field:IWorldField
 {
  public readonly Func<double,double,double> Height;
  public int Queries;
  public Field(Func<double,double,double> h=null){Height=h??((x,z)=>0);}
  public string SourceIdentity=>"movement-controlled-height";
  public SurfaceSample Sample(double x,double z){Queries++;return new SurfaceSample(Height(x,z),6,6,true);}
 }
 readonly struct Box
 {
  public readonly double X0,Y0,Z0,X1,Y1,Z1;public readonly MaterialId Material;
  public Box(double x0,double y0,double z0,double x1,double y1,double z1,MaterialId m=MaterialId.Wood){X0=x0;Y0=y0;Z0=z0;X1=x1;Y1=y1;Z1=z1;Material=m;}
 }
 sealed class Props:IExplorationObstacles,IExplorationResistance
 {
  public double SoftVoxels;public int SoftQueries;
  public double WeightedSoftVoxels(WorldPoint feet,double radius,double height){SoftQueries++;return SoftVoxels;}
  public readonly List<Box> Boxes=new List<Box>();public bool Ready=true;public double ReadyMaxX=double.PositiveInfinity;
  public bool IsReady(double x,double z,double radius)=>Ready&&x+radius<=ReadyMaxX;
  public bool BlocksBody(double x0,double y0,double z0,double x1,double y1,double z1)
  {foreach(var b in Boxes)if(MaterialCatalog.BlocksActorBody(b.Material)&&x0<b.X1-1e-9&&x1>b.X0+1e-9&&y0<b.Y1-1e-9&&y1>b.Y0+1e-9&&z0<b.Z1-1e-9&&z1>b.Z0+1e-9)return true;return false;}
  public bool TryGetSupportHeight(double x,double z,double radius,double minimum,double maximum,out double height)
  {
   bool found=false;height=minimum;
   foreach(var b in Boxes)
   {
    if(!MaterialCatalog.BlocksActorBody(b.Material)||b.Y1<minimum-1e-8||b.Y1>maximum+1e-8)continue;
    double dx=Math.Max(b.X0-x,Math.Max(0,x-b.X1)),dz=Math.Max(b.Z0-z,Math.Max(0,z-b.Z1));
    if(dx*dx+dz*dz>radius*radius)continue;
    height=Math.Max(height,b.Y1);found=true;
   }
   return found;
  }
 }
 static ExplorationMotor Motor(Field field=null,Props props=null,WorldPoint? start=null,ExplorationMovementSettings settings=null)
 =>new ExplorationMotor(new FieldExplorationCollision(field??new Field(),props),start??new WorldPoint(0,.015,0),settings);
 static void Run(ExplorationMotor m,int fps,double seconds,ExplorationMoveInput input)
 {int count=(int)Math.Round(fps*seconds);for(int i=0;i<count;i++)m.Tick(input,1d/fps);}
 static double HorizontalDistance(WorldPoint p)=>Math.Sqrt(p.X*p.X+p.Z*p.Z);
 static void Main(string[] args)
 {
  var rates=new List<object>();
  foreach(int fps in new[]{30,60,144})
  {
   var foliage=new Props{SoftVoxels=100};var drag=Motor(props:foliage);Run(drag,fps,1,new ExplorationMoveInput(1,0));
   Check(Math.Abs(drag.Feet.X-4/1.08)<1e-8&&foliage.SoftQueries==fps,"100 overlapping leaf-equivalent cells slow walking equally with one query per tick at"+fps+"Hz");
   foliage.SoftVoxels=400;var dense=Motor(props:foliage);Run(dense,fps,1,new ExplorationMoveInput(1,0));
   Check(dense.Feet.X<drag.Feet.X&&Math.Abs(dense.Feet.X-4/1.32)<1e-8,"greater stick/leaf volume increases resistance at"+fps+"Hz");
   foliage.SoftVoxels=1000000;var capped=Motor(props:foliage);Run(capped,fps,1,new ExplorationMoveInput(1,0));
   Check(Math.Abs(capped.Feet.X-2.6)<1e-8,"extreme foliage remains passable at65percent walking speed at"+fps+"Hz");
   foliage.SoftVoxels=0;capped.Tick(new ExplorationMoveInput(1,0),1d/fps);Check(capped.VegetationSpeedFactor==1,"leaving foliage restores normal speed at"+fps+"Hz");
   var flying=Motor(props:new Props{SoftVoxels=1000000});flying.SetFlying(true);Run(flying,fps,1,new ExplorationMoveInput(1,0));Check(Math.Abs(flying.Feet.X-40)<1e-8,"foliage resistance does not change flight at"+fps+"Hz");
   var walk=Motor();Run(walk,fps,2,new ExplorationMoveInput(1,0));
   Check(Math.Abs(walk.Feet.X-8)<1e-8&&Math.Abs(walk.Feet.Y-.015)<1e-10&&walk.Grounded,"flat walking is smooth and metric at"+fps+"Hz");
   var diagonal=Motor();Run(diagonal,fps,2,new ExplorationMoveInput(1,1));
   Check(Math.Abs(HorizontalDistance(diagonal.Feet)-8)<1e-8,"diagonal motion is normalized at"+fps+"Hz");
   var sprint=Motor();Run(sprint,fps,2,new ExplorationMoveInput(1,0,sprint:true));Check(Math.Abs(sprint.Feet.X-14)<1e-8,"sprint speed is consistent at"+fps+"Hz");
   var slope=Motor(new Field((x,z)=>x*.35));double maximumYStep=0,lastY=slope.Feet.Y;
   for(int i=0;i<fps*2;i++){slope.Tick(new ExplorationMoveInput(1,0),1d/fps);maximumYStep=Math.Max(maximumYStep,Math.Abs(slope.Feet.Y-lastY));lastY=slope.Feet.Y;}
   Check(Math.Abs(slope.Feet.X-8)<1e-8&&Math.Abs(slope.Feet.Y-2.815)<1e-8&&maximumYStep<.05,"continuous hillside avoids quantized10cm stair jitter at"+fps+"Hz");
   var jumping=Motor();jumping.Tick(new ExplorationMoveInput(0,0,jump:true),1d/fps);double apex=jumping.Feet.Y,landTime=0;
   for(int i=1;i<fps*2;i++){jumping.Tick(new ExplorationMoveInput(0,0),1d/fps);apex=Math.Max(apex,jumping.Feet.Y);if(landTime==0&&jumping.Grounded)landTime=(i+1d)/fps;}
   Check(Math.Abs(apex-.915)<.002&&jumping.Grounded&&Math.Abs(jumping.Feet.Y-.015)<1e-9,"jump and gravity return to the same floor at"+fps+"Hz");
   Check(Math.Abs(landTime-.6)<=1d/fps+.001,"falling jump does not snap prematurely through the final24cm at"+fps+"Hz");
   rates.Add(new{fps,walk_x=walk.Feet.X,slope_y=slope.Feet.Y,maximum_slope_y_step=maximumYStep,jump_apex=apex,jump_airtime=landTime});
  }
  var wall=new Props();wall.Boxes.Add(new Box(1,0,-100,1.1,10,100));
  var blocked=Motor(props:wall);Run(blocked,30,3,new ExplorationMoveInput(1,0));
  Check(blocked.Feet.X<=.720001&&blocked.Feet.X>.66&&!blocked.Collision.OverlapsBody(blocked.Feet,.28,1.8),"one-voxel wood wall blocks walking without penetration");
  var slide=Motor(props:wall);Run(slide,60,2,new ExplorationMoveInput(1,1));
  Check(slide.Feet.X<=.720001&&slide.Feet.Z>4,"diagonal movement slides along wood instead of sticking on both axes");
  var corner=new Props();corner.Boxes.Add(new Box(1,0,-10,1.1,10,10));corner.Boxes.Add(new Box(-10,0,1,10,10,1.1));var stopped=Motor(props:corner);
  Run(stopped,60,2,new ExplorationMoveInput(1,1));WorldPoint stop=stopped.Feet;Run(stopped,60,2,new ExplorationMoveInput(1,1));
  Check(stopped.Feet.X<=.72+1e-6&&stopped.Feet.Z<=.72+1e-6&&Math.Abs(stopped.Feet.X-stop.X)+Math.Abs(stopped.Feet.Z-stop.Z)<1e-10,"closed corner remains stable without oscillation or tunneling");
  foreach(var tissue in new[]{MaterialId.Leaves,MaterialId.Fungus,MaterialId.Petal})
  {
   var soft=new Props();soft.Boxes.Add(new Box(1,0,-2,2,4,2,tissue));var through=Motor(props:soft);Run(through,60,1,new ExplorationMoveInput(1,0));
   Check(Math.Abs(through.Feet.X-4)<1e-8&&through.Grounded,tissue+" does not obstruct the actor body");
  }
  var stairs=new Props();stairs.Boxes.Add(new Box(1,0,-1,3,.2,1,MaterialId.Granite));var stepped=Motor(props:stairs);Run(stepped,60,.5,new ExplorationMoveInput(1,0));
  Check(stepped.Feet.X>1.9&&Math.Abs(stepped.Feet.Y-.215)<1e-8&&stepped.Grounded,"20cm stone step is traversable without a repeated voxel collision bump");
  var high=new Props();high.Boxes.Add(new Box(1,0,-1,3,.6,1,MaterialId.Granite));var highStep=Motor(props:high);Run(highStep,60,1,new ExplorationMoveInput(1,0));
  Check(highStep.Feet.X<.73&&Math.Abs(highStep.Feet.Y-.015)<1e-9,"high rock ledge is not silently teleported onto");
  var ceiling=new Props();ceiling.Boxes.Add(new Box(-2,2,-2,2,2.1,2,MaterialId.Wood));var head=Motor(props:ceiling);head.Tick(new ExplorationMoveInput(0,0,jump:true),1d/60);double maximumHead=head.Feet.Y+1.8;
  for(int i=0;i<120;i++){head.Tick(new ExplorationMoveInput(0,0),1d/60);maximumHead=Math.Max(maximumHead,head.Feet.Y+1.8);}
  Check(maximumHead<=2.00001&&head.Grounded,"wood ceiling stops the head and the player lands cleanly");
  var boulder=new Props();boulder.Boxes.Add(new Box(-1,0,-1,1,1,1,MaterialId.Granite));var falling=Motor(props:boulder,start:new WorldPoint(0,4,0));Run(falling,144,2,new ExplorationMoveInput(0,0));
  Check(Math.Abs(falling.Feet.Y-1.015)<1e-8&&falling.Grounded,"falling body lands on hard-prop support rather than the terrain below");
  var steep=Motor(new Field((x,z)=>2*x));Run(steep,60,.5,new ExplorationMoveInput(1,0));
  Check(steep.Feet.X<0,"over-limit incline rejects ascent and gravity slides down the slope");
  var unavailable=new Props{Ready=false};var waiting=Motor(props:unavailable);Run(waiting,60,5,new ExplorationMoveInput(1,0));
  Check(waiting.WaitingForWorld&&waiting.Feet.X==0&&waiting.Feet.Y==.015&&waiting.DeferredSeconds==0,"unready collision holds the player without storing delayed movement");
  unavailable.Ready=true;waiting.Tick(new ExplorationMoveInput(1,0),1d/60);
  Check(waiting.Feet.X<.07&&!waiting.WaitingForWorld,"stream readiness resumes only current input without a catch-up launch");
  var border=new Props{ReadyMaxX=2};var edge=Motor(props:border);Run(edge,30,2,new ExplorationMoveInput(1,0));
  Check(edge.Feet.X+edge.Settings.Radius<=2&&edge.WaitingForWorld,"movement stops before the unloaded capsule boundary");
  foreach(int fps in new[]{30,60,144})
  {
   var flight=Motor(props:wall,start:new WorldPoint(0,2,0));flight.SetFlying(true);Run(flight,fps,1,new ExplorationMoveInput(1,0,sprint:true));
   Check(flight.Feet.X<=.720001&&!flight.Collision.OverlapsBody(flight.Feet,.28,1.8),"fast flight cannot tunnel through a10cm wood wall at"+fps+"Hz");
  }
  var fly=Motor(props:wall,start:new WorldPoint(0,2,0));Check(fly.SetFlying(true),"flight starts without changing global position");fly.NoClip=true;Run(fly,60,1,new ExplorationMoveInput(1,0));
  Check(fly.Feet.X>39&&!fly.Grounded,"explicit noclip flight bypasses hard props");fly.ResetAt(new WorldPoint(1.05,2,0),true);fly.NoClip=true;
  Check(!fly.SetFlying(false)&&fly.Flying,"leaving noclip inside wood is refused rather than trapping the walker");
  fly.ResetAt(new WorldPoint(4,2,0),true);fly.NoClip=true;Check(fly.SetFlying(false)&&!fly.NoClip,"returning to walking in clear space restores collision");
  var groundFlight=Motor(start:new WorldPoint(0,1,0));groundFlight.SetFlying(true);Run(groundFlight,60,1,new ExplorationMoveInput(0,0,-1));
  Check(Math.Abs(groundFlight.Feet.Y-.015)<1e-8,"collision-enabled flight stops at continuous terrain height");
  var budget=Motor(settings:new ExplorationMovementSettings(flightSpeed:1000,flightBoost:10,maxSubsteps:32));budget.SetFlying(true);budget.NoClip=true;budget.Tick(new ExplorationMoveInput(1,0,sprint:true),1);
  Check(budget.LastSubsteps==32&&budget.DeferredSeconds>.99&&Math.Abs(budget.SimulatedSeconds+budget.DeferredSeconds-1)<1e-10,"extreme movement preserves bounded work and observable deferred time");
  var giant=Motor(new Field((x,z)=>1000),start:new WorldPoint(1e9,1000.015,-1e9));Run(giant,60,2,new ExplorationMoveInput(1,0));
  Check(Math.Abs(giant.Feet.X-(1e9+8))<.001&&Math.Abs(giant.Feet.Z+1e9)<1e-9,"globaldouble movement remains precise at billion-meter render origins");
  var tree=new Props();tree.Boxes.Add(new Box(-.4,0,-.4,.4,8,.4,MaterialId.Wood));var spawn=Motor(props:tree);
  Check(spawn.TryFindClearSpawn(new WorldPoint(0,0,0))&&!spawn.Collision.OverlapsBody(spawn.Feet,.28,1.8)&&spawn.Grounded,"bounded spawn search finds clear ground beside a trunk");
  tree.Ready=false;WorldPoint before=spawn.Feet;Check(!spawn.TryFindClearSpawn(new WorldPoint(10,0,10))&&spawn.Feet.Equals(before),"unknown spawn terrain is not treated as empty");
  var steepSpawn=Motor(new Field((x,z)=>2*x));WorldPoint beforeSteepSpawn=steepSpawn.Feet;
  Check(!steepSpawn.TryFindClearSpawn(new WorldPoint(0,0,0))&&steepSpawn.Feet.Equals(beforeSteepSpawn),"steep-only spawn region is refused without grounding on an unwalkable slope");
  var flatNearby=Motor(new Field((x,z)=>x<1?2*x:2));
  Check(flatNearby.TryFindClearSpawn(new WorldPoint(0,0,0))&&Math.Abs(flatNearby.Feet.X-2)<1e-8&&Math.Abs(flatNearby.Feet.Y-2.015)<1e-8&&flatNearby.Grounded,
   "spawn search skips a steep center and selects the nearby clear walkable plateau");
  var stable=Motor();Run(stable,60,1,new ExplorationMoveInput(0,0));long begin=GC.GetAllocatedBytesForCurrentThread();Run(stable,60,20,new ExplorationMoveInput(0,0));long allocated=GC.GetAllocatedBytesForCurrentThread()-begin;
  Check(allocated==0,"warmed stationary motor ticks allocate zero managed bytes");
  Reject(()=>new ExplorationMovementSettings(walkSpeed:double.NaN),"nonfinite movement speed is rejected");Reject(()=>new ExplorationMovementSettings(eyeHeight:3),"eye cannot exceed body height");
  Reject(()=>new ExplorationMovementSettings(sweepStep:.1),"collision sweep cannot exceed the10cm matter safety spacing");Reject(()=>new ExplorationMovementSettings(maxSubsteps:0),"zero substep work budget is rejected");
  Reject(()=>stable.Tick(new ExplorationMoveInput(0,0),double.PositiveInfinity),"nonfinite timestep is rejected");
  VoxelSlopeChecks();
  ControllerChecks();
  File.WriteAllText(args[0],JsonSerializer.Serialize(new{status="PASS",checks,scope="Actual engine-free movement plus actual input/controller code with Unity substitutes; not native Unity controller/play/render testing",rates,warmed_motor_allocated_bytes=allocated},new JsonSerializerOptions{WriteIndented=true})+"\n");
  Console.WriteLine("All "+checks+" exploration movement checks passed.");
 }
 // Terrain contact remains cubic; slope policy must not mistake a quantized
 // riser for an over-limit continuous incline. These execute the actual motor
 // and VoxelTerrainCollision, without weakening the configured 50degree limit.
 static void VoxelSlopeChecks()
 {
  foreach(int fps in new[]{15,30,60,144,240})foreach(double speed in new[]{.5,4.0})foreach(int direction in new[]{-1,1})
  {
   var field=new Field((x,z)=>1.18*x); // 49.7201degrees, below the default limit.
   var collision=new VoxelTerrainCollision(field,new FieldExplorationCollision(field));
   collision.TryGetGroundHeight(0,0,out double top);
   var motor=new ExplorationMotor(collision,new WorldPoint(0,top+.015,0),new ExplorationMovementSettings(walkSpeed:speed));
   bool exactContact=true;
   for(int frame=0;frame<fps*3;frame++)
   {
    motor.Tick(new ExplorationMoveInput(direction,0),1d/fps);
    collision.TryGetGroundHeight(motor.Feet.X,motor.Feet.Z,out top);
    exactContact&=motor.Grounded&&Math.Abs(motor.Feet.Y-top-motor.Settings.Skin)<1e-8;
   }
   Check(Math.Abs(motor.Feet.X-direction*speed*3)<1e-7,
    "below-limit voxel hillside preserves "+speed+"m/s in direction"+direction+" at"+fps+"Hz without false blocking or downhill acceleration");
   Check(exactContact,"continuous slope policy retains exact10cm contact every frame at"+fps+"Hz, speed"+speed+", direction"+direction);
  }
  foreach(double origin in new[]{-10000.03,10000.03})
  {
   var field=new Field((x,z)=>1.18*(x-origin));var collision=new VoxelTerrainCollision(field,new FieldExplorationCollision(field));
   collision.TryGetGroundHeight(origin,0,out double top);var motor=new ExplorationMotor(collision,new WorldPoint(origin,top+.015,0));
   Run(motor,60,3,new ExplorationMoveInput(1,0));
   Check(Math.Abs(motor.Feet.X-origin-12)<1e-7,"fractional global cell phase preserves below-limit traversal at"+origin+"m");
  }
  foreach(int fps in new[]{30,60,144})
  {
   var field=new Field((x,z)=>1.2*x); // 50.1944degrees, genuinely above the limit.
   var collision=new VoxelTerrainCollision(field,new FieldExplorationCollision(field));collision.TryGetGroundHeight(0,0,out double top);
   var motor=new ExplorationMotor(collision,new WorldPoint(0,top+.015,0));Run(motor,fps,3,new ExplorationMoveInput(1,0));
   // Existing freedom within the initial horizontal tread is intentional here:
   // this regression requires no continued ascent, not zero subcell movement.
   Check(motor.Feet.X<.11,"above-limit voxel hillside cannot advance beyond its initial10cm tread at"+fps+"Hz");
   field=new Field((x,z)=>1.18*(x+z)/Math.Sqrt(2));collision=new VoxelTerrainCollision(field,new FieldExplorationCollision(field));
   collision.TryGetGroundHeight(0,0,out top);motor=new ExplorationMotor(collision,new WorldPoint(0,top+.015,0));Run(motor,fps,3,new ExplorationMoveInput(1,1));
   Check(Math.Abs(HorizontalDistance(motor.Feet)-12)<1e-7,"near-limit voxel diagonal remains normalized and traversable at"+fps+"Hz");
  }
  var failed=new FailedSlopeCollision(new FieldExplorationCollision(new Field((x,z)=>x*.2)));
  var held=new ExplorationMotor(failed,new WorldPoint(0,.015,0));Run(held,60,1,new ExplorationMoveInput(1,0));
  Check(Math.Abs(held.Feet.X)<1e-9,"failed optional slope query refuses ascent rather than silently treating unknown slope as flat");
  // SurfaceSample already rejects NaN/Infinity. Finite opposite extremes test
  // the separate guard against arithmetic overflow while deriving the slope.
  var extremes=new Field((x,z)=>x>.29?double.MaxValue:-double.MaxValue);
  var overflow=new VoxelTerrainCollision(extremes,new FieldExplorationCollision(extremes));
  Check(!overflow.TryGetGroundGradient(0,0,out _,out _),"finite source values whose derived gradient overflows return explicit slope failure");
 }
 sealed class FailedSlopeCollision:IExplorationCollision,IExplorationGroundSlope
 {
  readonly IExplorationCollision basis;
  public FailedSlopeCollision(IExplorationCollision basis){this.basis=basis;}
  public bool TryGetGroundGradient(double x,double z,out double gx,out double gz){gx=gz=0;return false;}
  public bool IsReady(double x,double z,double radius)=>basis.IsReady(x,z,radius);
  public bool TryGetGroundHeight(double x,double z,out double height)=>basis.TryGetGroundHeight(x,z,out height);
  public bool OverlapsBody(WorldPoint feet,double radius,double height)=>basis.OverlapsBody(feet,radius,height);
  public bool TryGetSupportHeight(double x,double z,double radius,double minimum,double maximum,out double height)=>basis.TryGetSupportHeight(x,z,radius,minimum,maximum,out height);
 }
 static void ControllerChecks()
 {
  var vista=new WorldExplorer();var props=new Props();props.Boxes.Add(new Box(1,0,-1,3,.2,1,MaterialId.Granite));
  var c=new ExplorationController();c.Configure(vista,new FieldExplorationCollision(new Field(),props),new WorldPoint(0,.015,0),capturePointer:false);
  Check(c.IsConfigured&&Math.Abs(c.Eye.Y-1.665)<1e-8,"actual controller configures a physical eye without moving the terrain origin");
  double last=c.Eye.Y,maxDelta=0;for(int i=0;i<30;i++){c.Advance(new ExplorationMoveInput(1,0),1d/60);maxDelta=Math.Max(maxDelta,Math.Abs(c.Eye.Y-last));last=c.Eye.Y;}
  Check(c.Feet.X>1.9&&maxDelta<.1&&c.Eye.Y<=c.Feet.Y+c.Motor.Settings.Height-.024,"step camera damps vertical voxel changes and remains inside the tested body");
  for(int i=0;i<30;i++)c.Advance(new ExplorationMoveInput(1,0),1d/60);
  Check(c.Eye.Y<=c.Feet.Y+c.Motor.Settings.Height-.024,"descending camera cannot rise through an untested ceiling volume");
  c.ResetAt(new WorldPoint(1e9,2000,-1e9),true);c.Advance(new ExplorationMoveInput(0,1),1d/60);
  Check(vista.LastEye.X==c.Feet.X&&vista.LastEye.Z==c.Feet.Z&&Math.Abs(vista.LastEye.Y-(c.Feet.Y+1.65))<1e-8,"view receives globaldouble eye coordinates before renderer rebasing");
  c.SetInputEnabled(false);WorldPoint stopped=c.Feet;c.Advance(new ExplorationMoveInput(1,0),1);
  Check(c.Feet.Equals(stopped)&&c.DeferredMovementSeconds==0,"settings/loading pause disables motion and clears old input remainder");
  c.SetInputEnabled(true);Keyboard.current=new Keyboard();Mouse.current=new Mouse();c.CapturePointer();
  Keyboard.current.fKey.wasPressedThisFrame=true;Invoke(c,"Update");Keyboard.current.fKey.wasPressedThisFrame=false;
  Check(!c.Motor.Flying,"F changes from clear-space flight to walking through the actual input layer");
  Keyboard.current.escapeKey.wasPressedThisFrame=true;Invoke(c,"Update");Keyboard.current.escapeKey.wasPressedThisFrame=false;
  Check(Cursor.lockState==CursorLockMode.None&&Cursor.visible,"Escape releases the pointer without any terrain edit path");
  Mouse.current.leftButton.wasPressedThisFrame=true;Invoke(c,"Update");Mouse.current.leftButton.wasPressedThisFrame=false;
  Check(Cursor.lockState==CursorLockMode.Locked&&!Cursor.visible,"left click only recaptures the pointer");
  c.Motor.SetFlying(true);Keyboard.current.nKey.wasPressedThisFrame=true;Invoke(c,"Update");Keyboard.current.nKey.wasPressedThisFrame=false;
  Check(c.Motor.NoClip,"N explicitly enables noclip only while flying");
  c.CapturePointer();Invoke(c,"OnDisable");Check(Cursor.lockState==CursorLockMode.None,"disabled player releases its captured pointer");
  Keyboard.current=null;Mouse.current=null;
 }
 static void Invoke(object target,string method)=>target.GetType().GetMethod(method,BindingFlags.Instance|BindingFlags.NonPublic).Invoke(target,null);
}
