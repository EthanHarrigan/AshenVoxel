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
  ExplorationBootstrap boot;string folder;WorldPoint spawn;bool moveNextFrame;double pendingX,pendingZ;bool configured,complete,oldEnabled,oldInput,oldCapture,oldFlying,oldNoClip;int site,phase;double began,phaseAt;double x,z,y,upX=1,upZ,prepareX=24,prepareZ;float yaw;
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
  void PrepareOffset()
  {
   if(!(Get(boot.Population,"rootShared") is IDictionary shared)||!shared.Contains(site+10))throw new InvalidOperationException("Required root companion missing");
   var template=Get(Get(shared[site+10],"Bundle"),"Root");
   double loX=Convert.ToDouble(Get(template,"MinX")),hiX=Convert.ToDouble(Get(template,"MaxX")),loZ=Convert.ToDouble(Get(template,"MinZ")),hiZ=Convert.ToDouble(Get(template,"MaxZ"));
   double distance=Math.Max(Math.Max(Math.Abs(loX),Math.Abs(hiX+1)),Math.Max(Math.Abs(loZ),Math.Abs(hiZ+1)))*.1*Math.Sqrt(2)+8.25;
   var other=Sites[1-site];double dx=x-other.x,dz=z-other.z,length=Math.Sqrt(dx*dx+dz*dz);prepareX=distance*(length>1e-6?dx/length:1);prepareZ=distance*(length>1e-6?dz/length:0);
  }
  void StartSite(){if(Sites==null||Sites.Length!=2)throw new InvalidOperationException("Exact two-site data required");var s=Sites[site];if(s.id!=site+10)throw new InvalidOperationException("Site identity");x=s.x;z=s.z;y=s.original_y;yaw=s.yaw;double gx=boot.WorldField.Sample(x+.5,z).HeightMeters-boot.WorldField.Sample(x-.5,z).HeightMeters,gz=boot.WorldField.Sample(x,z+.5).HeightMeters-boot.WorldField.Sample(x,z-.5).HeightMeters,len=Math.Sqrt(gx*gx+gz*gz);upX=len>1e-6?gx/len:1;upZ=len>1e-6?gz/len:0;phase=0;PrepareOffset();Move(prepareX,prepareZ);phaseAt=Time.realtimeSinceStartupAsDouble;}
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
   if(!configured||complete)return;if(moveNextFrame){moveNextFrame=false;Move(pendingX,pendingZ);phaseAt=Time.realtimeSinceStartupAsDouble;return;}
   try
   {
    if(boot==null||!Application.isPlaying){Finish("Owner unavailable");return;}double now=Time.realtimeSinceStartupAsDouble,elapsed=now-phaseAt;var target=Target();
    if(phase==0&&(target!=null||elapsed>6)){Record(target,null);phase=1;Move(5*upX,5*upZ);phaseAt=now;}
    else if(phase==1&&elapsed>1.5){Capture(target,"uphill");phase=2;moveNextFrame=true;pendingX=-5*upX;pendingZ=-5*upZ;phaseAt=now;}
    else if(phase==2&&elapsed>1.5){Capture(target,"downhill");phase=3;moveNextFrame=true;pendingX=4000;pendingZ=0;phaseAt=now;}
    else if(phase==3&&elapsed>1.5){Record(target,null);phase=4;Move(prepareX,prepareZ);phaseAt=now;}
    else if(phase==4&&(target!=null||elapsed>6))
    {Record(target,null);site++;if(site==2){Finish("RECORDED_REQUIRES_VISUAL_REVIEW");return;}StartSite();}
    if(now-began>38)Finish("TIME_BOUND_REACHED");
   }
   catch(Exception e){Finish("FAIL: "+e.GetType().Name+": "+e.Message);UnityEngine.Debug.LogException(e);}
  }
  void Capture(CompleteArtifact target,string label)
  {
   string file=Path.Combine(folder,"site"+(site+10)+"-"+label+".ppm");
   var camera=boot.Vista.MainCamera;var old=camera.targetTexture;var active=RenderTexture.active;
   RenderTexture rt=null;Texture2D pixels=null;
   try
   {
    rt=new RenderTexture(640,480,24);rt.Create();camera.targetTexture=rt;
    camera.Render();camera.Render();camera.Render();RenderTexture.active=rt;
    pixels=new Texture2D(640,480,TextureFormat.RGB24,false);pixels.ReadPixels(new Rect(0,0,640,480),0,0);pixels.Apply();WriteFrame(pixels,file);
   }
   finally{camera.targetTexture=old;RenderTexture.active=active;if(pixels!=null)Destroy(pixels);if(rt!=null){rt.Release();Destroy(rt);}}
   Record(target,file);
  }
  public static void WriteFrame(Texture2D pixels,string file)
  {
   using(var stream=File.Create(file))
   {
    byte[] header=System.Text.Encoding.ASCII.GetBytes("P6\n"+pixels.width+" "+pixels.height+"\n255\n");stream.Write(header,0,header.Length);
    var colors=pixels.GetPixels32();var row=new byte[pixels.width*3];
    for(int y=pixels.height-1;y>=0;y--){for(int x=0;x<pixels.width;x++){var c=colors[y*pixels.width+x];row[x*3]=c.r;row[x*3+1]=c.g;row[x*3+2]=c.b;}stream.Write(row,0,row.Length);}
   }
  }
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
