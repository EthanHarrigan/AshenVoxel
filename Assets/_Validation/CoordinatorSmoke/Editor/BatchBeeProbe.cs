using System;
using System.IO;
using System.Reflection;
using AshenVoxel.Exploration;
using AshenVoxel.WildlifeDraft;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
namespace AshenVoxel.Validation
{
    // Inert unless an explicit bounded marker exists in the owned copied scene.
    // Does not start Play, change settings, or save scenes.
    [InitializeOnLoad] public static class BatchBeeProbe
    {
        static double next,phaseAt;
        static int phase;
        static bool running,oldEnabled,oldCapture;
        static Vector3 oldPosition;static Quaternion oldRotation;
        static ExplorationBootstrap boot;static ExplorationBeePatch bee;
        static readonly string Root=Path.GetDirectoryName(Application.dataPath);
        static readonly string Request=Path.Combine(Root,".terrain-cache/content-batch-bees.request");
        static readonly string Output=Path.Combine(Root,".terrain-cache/content-batch-bees-2");
        [Serializable] sealed class Report{public string status,error;public int actors,drawCalls;public bool disableStops,enableRestores;public double firstCaptureAt,secondCaptureAt;}
        static Report report;
        static BatchBeeProbe(){EditorApplication.update+=Tick;EditorApplication.playModeStateChanged+=s=>{if(s==PlayModeStateChange.ExitingPlayMode&&running)Finish("PLAY_EXIT");};}
        static double Number(string name)=>(double)typeof(ExplorationBeePatch).GetField(name,BindingFlags.Instance|BindingFlags.NonPublic).GetValue(bee);
        static void Tick()
        {
            if(EditorApplication.timeSinceStartup<next)return;next=EditorApplication.timeSinceStartup+.05;
            try
            {
                if(!running)
                {
                    if(!EditorApplication.isPlaying||SceneManager.GetActiveScene().path!="Assets/_Validation/CoordinatorSmoke/ExplorationSmoke_20261027.unity"||!File.Exists(Request))return;
                    string token=File.ReadAllText(Request);File.Delete(Request);if(token.Trim()!="content-batch-bees-1")return;
                    Directory.CreateDirectory(Output);report=new Report{status="WAITING"};running=true;phase=0;phaseAt=EditorApplication.timeSinceStartup;
                }
                boot=UnityEngine.Object.FindFirstObjectByType<ExplorationBootstrap>();
                if(boot==null||!boot.Ready){if(EditorApplication.timeSinceStartup-phaseAt>30)Finish("BOOT_TIMEOUT");return;}
                bee=UnityEngine.Object.FindFirstObjectByType<ExplorationBeePatch>();
                if(phase==0)
                {
                    if(bee==null||bee.DrawnActors!=6){if(EditorApplication.timeSinceStartup-phaseAt>15)Finish("BEE_NOT_VISIBLE: "+bee?.Status);return;}
                    report.actors=bee.DrawnActors;report.drawCalls=bee.DrawCalls;
                    oldEnabled=boot.Player.enabled;oldCapture=boot.Player.CapturePointerOnEnable;
                    boot.Player.CapturePointerOnEnable=false;boot.Player.SetInputEnabled(false);boot.Player.enabled=false;
                    var camera=boot.Vista.MainCamera;oldPosition=camera.transform.position;oldRotation=camera.transform.rotation;
                    var origin=boot.Vista.RenderOrigin;
                    var anchor=new Vector3((float)(Number("anchorX")-origin.X),(float)(Number("anchorY")-origin.Y),(float)(Number("anchorZ")-origin.Z));
                    camera.transform.position=anchor+new Vector3(2.7f,1.5f,2.7f);camera.transform.LookAt(anchor+Vector3.up);
                    phase=1;phaseAt=EditorApplication.timeSinceStartup;
                }
                else if(phase==1&&EditorApplication.timeSinceStartup-phaseAt>.15)
                {Capture("bees-0.png");report.firstCaptureAt=Time.timeAsDouble;phase=2;phaseAt=EditorApplication.timeSinceStartup;}
                else if(phase==2&&EditorApplication.timeSinceStartup-phaseAt>.27)
                {Capture("bees-1.png");report.secondCaptureAt=Time.timeAsDouble;bee.enabled=false;phase=3;phaseAt=EditorApplication.timeSinceStartup;}
                else if(phase==3&&EditorApplication.timeSinceStartup-phaseAt>.1)
                {report.disableStops=bee.DrawnActors==0;bee.enabled=true;phase=4;phaseAt=EditorApplication.timeSinceStartup;}
                else if(phase==4&&EditorApplication.timeSinceStartup-phaseAt>.15)
                {report.enableRestores=bee.DrawnActors==6;Finish(report.disableStops&&report.enableRestores?"CAPTURE_AND_ENABLE_LIFECYCLE_PASS":"LIFECYCLE_FAILED");}
            }
            catch(Exception e){if(report==null)report=new Report();report.error=e.ToString();Finish("EXCEPTION");}
        }
        static void Capture(string name)
        {
            var camera=boot.Vista.MainCamera;var old=camera.targetTexture;var active=RenderTexture.active;
            var origin=boot.Vista.RenderOrigin;
            var anchor=new Vector3((float)(Number("anchorX")-origin.X),(float)(Number("anchorY")-origin.Y),(float)(Number("anchorZ")-origin.Z));
            camera.transform.position=anchor+new Vector3(2.7f,1.5f,2.7f);camera.transform.LookAt(anchor+Vector3.up);
            RenderTexture rt=null;Texture2D pixels=null;
            try
            {
                rt=new RenderTexture(640,480,24);rt.Create();camera.targetTexture=rt;
                camera.Render();camera.Render();camera.Render();RenderTexture.active=rt;
                pixels=new Texture2D(640,480,TextureFormat.RGB24,false);pixels.ReadPixels(new Rect(0,0,640,480),0,0);pixels.Apply();
                RootNativeProbe.WriteFrame(pixels,Path.Combine(Output,Path.ChangeExtension(name,".ppm")));
            }
            finally{camera.targetTexture=old;RenderTexture.active=active;if(pixels!=null)UnityEngine.Object.DestroyImmediate(pixels);if(rt!=null){rt.Release();UnityEngine.Object.DestroyImmediate(rt);}}
        }
        static void Finish(string status)
        {
            try
            {
                if(phase>0&&boot!=null&&boot.Player!=null)
                {
                    var camera=boot.Vista.MainCamera;if(camera!=null)camera.transform.SetPositionAndRotation(oldPosition,oldRotation);
                    boot.Player.enabled=oldEnabled;boot.Player.CapturePointerOnEnable=false;boot.Player.SetInputEnabled(oldEnabled);boot.Player.CapturePointerOnEnable=oldCapture;
                    if(bee!=null)bee.enabled=true;
                }
            }
            catch(Exception e){report.error+=" RESTORE: "+e;}
            finally{running=false;report.status=status;Directory.CreateDirectory(Output);File.WriteAllText(Path.Combine(Output,"report.json"),JsonUtility.ToJson(report,true));phase=0;}
        }
    }
}
