using System;using System.Collections;using System.Collections.Generic;using System.Diagnostics;using System.IO;using System.Reflection;using System.Text.RegularExpressions;
using AshenVoxel.Exploration;using AshenVoxel.WorldScale;using RootContactBuildPipeline;using UnityEngine;using UnityEngine.SceneManagement;
namespace AshenVoxel.Validation
{
 [DefaultExecutionOrder(1000)] public sealed class RootNativeProbe:MonoBehaviour
 {
  [Serializable] sealed class Arm{public string marker="",run="";public int pid=0;public long startTicks=0;public bool began=false,finished=false;}
  [Serializable] public sealed class Observation{public int site,phase,committed,bodyProbes,bodyHits;public double seconds,originX,originZ;public long nativeReservation,workerReservation;public bool targetBound;public string status,screenshot;}
  [Serializable] sealed class Report{public string status,scope="Installed gameplay owner in guarded copied profile; two natural sites. Screenshots require visual inspection. Frame/resource counters are observations, not measured GPU allocation or full-profile FPS.";public Observation[] observations;public string cleanupError="";}
  readonly List<Observation> observations=new List<Observation>();
  ExplorationBootstrap boot;string folder;WorldPoint spawn;bool configured,complete,oldEnabled,oldInput,oldCapture,oldFlying,oldNoClip;int site,phase;double began,phaseAt;double x,z,y;float yaw;
  public bool Complete=>complete;
  static object Get(object owner,string field)=>owner.GetType().GetField(field,BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public)?.GetValue(owner);
  public void Configure(ExplorationBootstrap target,string run,string outputFolder)
  {
   if(configured||target==null||!target.Ready||!Application.isPlaying||target.LoadSavedOptions||SceneManager.GetActiveScene().path!="Assets/_Validation/CoordinatorSmoke/ExplorationSmoke_20261027.unity"||!Regex.IsMatch(run??"","^[A-Za-z0-9_-]{1,60}$"))throw new InvalidOperationException("Ready guarded copied profile required");
   string project=Path.GetDirectoryName(Application.dataPath),armPath=Path.Combine(project,".terrain-cache/coordinator-smoke/arm.json");var arm=JsonUtility.FromJson<Arm>(File.ReadAllText(armPath));
   using(var p=Process.GetCurrentProcess())if(arm.marker!="coordinator-smoke-v1"||arm.run!=run||arm.pid!=p.Id||arm.startTicks!=p.StartTime.ToUniversalTime().Ticks||!arm.began||arm.finished)throw new InvalidOperationException("Native guard mismatch");
   folder=Path.GetFullPath(outputFolder);if(!folder.StartsWith(Path.GetFullPath(Path.Combine(project,".terrain-cache"))+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("Owned output folder required");Directory.CreateDirectory(folder);
   boot=target;var player=boot.Player;spawn=player.Feet;oldEnabled=player.enabled;oldInput=player.InputEnabled;oldCapture=player.CapturePointerOnEnable;oldFlying=player.Motor.Flying;oldNoClip=player.Motor.NoClip;
   configured=true;began=Time.realtimeSinceStartupAsDouble;player.CapturePointerOnEnable=false;player.SetInputEnabled(false);player.enabled=false;StartSite();
  }
  // Coordinator supplies exact frozen site JSON before Configure; no terrain fixture replaces the installed field.
  [Serializable] public sealed class Site{public int id;public double x,z,original_y;public float yaw;}
  [Serializable] public sealed class SiteList{public Site[] sites=Array.Empty<Site>();}
  public Site[] Sites;
  void StartSite(){if(Sites==null||Sites.Length!=2)throw new InvalidOperationException("Exact two-site data required");var s=Sites[site];if(s.id!=site+10)throw new InvalidOperationException("Site identity");x=s.x;z=s.z;y=s.original_y;yaw=s.yaw;phase=0;Move(24,0);phaseAt=Time.realtimeSinceStartupAsDouble;}
  void Move(double dx,double dz)
  {
   var p=boot.Player;double ground=boot.WorldField.Sample(x+dx,z+dz).HeightMeters;
   p.ResetAt(new WorldPoint(x+dx,ground+.25,z+dz),true);p.Motor.NoClip=true;
   WorldPoint eye=p.Eye;Vector3 direction=new Vector3((float)(x-eye.X),(float)(y+.65-eye.Y),(float)(z-eye.Z));boot.Vista.SetObserverPose(eye,Quaternion.LookRotation(direction));
  }
  CompleteArtifact Target()
  {
   if(!(Get(boot.Population,"rootCommitted") is IDictionary map))return null;
   foreach(DictionaryEntry e in map){var cpu=Get(e.Value,"Cpu");var a=Get(cpu,"Artifact") as CompleteArtifact;if(a!=null&&a.Identity.ModelId==site+10&&Math.Abs(a.Identity.Root.Pose.X-x)<.02&&Math.Abs(a.Identity.Root.Pose.Z-z)<.02)return a;}return null;
  }
  void LateUpdate()
  {
   if(!configured||complete)return;
   try
   {
    if(boot==null||!Application.isPlaying){Finish("Owner unavailable");return;}double now=Time.realtimeSinceStartupAsDouble,elapsed=now-phaseAt;var target=Target();
    if(phase==0&&(target!=null||elapsed>6)){Record(target,null);phase=1;Move(5,0);phaseAt=now;}
    else if(phase==1&&elapsed>1.5){Capture(target,"east");phase=2;Move(-5,0);phaseAt=now;}
    else if(phase==2&&elapsed>1.5){Capture(target,"west");phase=3;Move(4000,0);phaseAt=now;}
    else if(phase==3&&elapsed>1.5){Record(target,null);phase=4;Move(24,0);phaseAt=now;}
    else if(phase==4&&(target!=null||elapsed>6))
    {Record(target,null);site++;if(site==2){Finish("RECORDED_REQUIRES_VISUAL_REVIEW");return;}StartSite();}
    if(now-began>38)Finish("TIME_BOUND_REACHED");
   }
   catch(Exception e){Finish("FAIL: "+e.GetType().Name+": "+e.Message);UnityEngine.Debug.LogException(e);}
  }
  void Capture(CompleteArtifact target,string label){string file=Path.Combine(folder,"site"+(site+10)+"-"+label+".png");ScreenCapture.CaptureScreenshot(file);Record(target,file);}
  void Record(CompleteArtifact a,string screenshot)
  {
   var origin=boot.Vista.RenderOrigin;var p=boot.Population;var row=new Observation{site=site+10,phase=phase,seconds=Time.realtimeSinceStartupAsDouble-began,committed=p.GroundedRootCount,nativeReservation=p.GroundedRootNativeReservation,workerReservation=p.GroundedRootWorkerReservation,targetBound=a!=null,status=p.GroundedRootStatus,screenshot=screenshot,originX=origin.X,originZ=origin.Z};
   if(a!=null){double radians=yaw*Math.PI/180,co=Math.Cos(radians),si=Math.Sin(radians);for(int i=0;i<a.CellCount&&row.bodyProbes<16;i++){var c=a.CellAt(i);if((byte)c.Role!=1||c.Position.y>=29)continue;double lx=(c.Position.x+.5)*.1,lz=(c.Position.z+.5)*.1,wx=x+co*lx+si*lz,wz=z-si*lx+co*lz,wy=y+(c.Position.y+.5)*.1;row.bodyProbes++;if(p.BlocksBody(wx-.002,wy-.002,wz-.002,wx+.002,wy+.002,wz+.002))row.bodyHits++;}}
   observations.Add(row);File.WriteAllText(Path.Combine(folder,"root-progress.json"),JsonUtility.ToJson(row,true));
  }
  void Finish(string status)
  {
   if(complete)return;complete=true;var report=new Report{status=status,observations=observations.ToArray()};
   try{if(boot!=null&&boot.Player!=null){var p=boot.Player;p.CapturePointerOnEnable=false;p.ResetAt(spawn,oldFlying);p.Motor.NoClip=oldNoClip;p.enabled=oldEnabled;p.SetInputEnabled(oldInput);p.CapturePointerOnEnable=oldCapture;}}
   catch(Exception e){report.cleanupError=e.GetType().Name+": "+e.Message;}
   File.WriteAllText(Path.Combine(folder,"root-report.json"),JsonUtility.ToJson(report,true));
  }
  void OnDisable(){if(configured&&!complete)Finish("DISABLED");}
 }
}
