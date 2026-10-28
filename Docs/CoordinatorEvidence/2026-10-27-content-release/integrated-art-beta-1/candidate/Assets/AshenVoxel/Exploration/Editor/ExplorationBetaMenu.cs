using System;
using System.IO;
using AshenVoxel.WorldScale;
using UnityEditor;
using UnityEngine;
namespace AshenVoxel.Exploration.Editor
{
    // Useful when Game view input is captured or an accessibility tool cannot send function keys.
    [InitializeOnLoad]
    public static class ExplorationBetaMenu
    {
        static WorldPoint previousFeet;static Quaternion previousRotation;
        static bool reviewing,previousFlying,previousInput;static float previousFov;
        static double started,nextSample;static string output;
        static ExplorationBetaMenu(){EditorApplication.update+=ReviewTick;}
        // Session-only camera bookmark. This never changes the authored/saved spawn or options.
        [MenuItem("Ashen Voxel/Exploration/Beta/Review largest high mountain range (Play)")]
        static void Mountain()
        {
            var b=Current();if(!Application.isPlaying||b==null||!b.Ready||reviewing)return;
            previousFeet=b.Player.Feet;previousRotation=b.Vista.MainCamera.transform.rotation;
            previousFlying=b.Player.Motor.Flying;previousInput=b.Player.InputEnabled;previousFov=b.ActiveSettings.FieldOfView;
            const double x=-63200,z=-18800;
            double y=Math.Max(6000,b.WorldField.Sample(x,z).HeightMeters+200);
            b.SetSettingsPanel(false,false);b.Player.ResetAt(new WorldPoint(x,y-b.Player.Motor.ViewHeight,z),true);
            b.ActiveSettings.FieldOfView=60;b.Vista.MainCamera.fieldOfView=60;
            var eye=b.Player.Eye;
            var direction=new Vector3((float)(-69120-eye.X),(float)(7776.5063-eye.Y),(float)(-22400-eye.Z));
            b.Vista.SetObserverPose(eye,Quaternion.LookRotation(direction.normalized,Vector3.up));b.Vista.ResetObserverMotion();b.Vista.ForceReplan();
            output="Logs/MountainReview/"+DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");Directory.CreateDirectory(output);
            started=EditorApplication.timeSinceStartup;nextSample=0;reviewing=true;
            File.WriteAllText(output+"/viewpoint.json",JsonUtility.ToJson(new Viewpoint{utc=DateTime.UtcNow.ToString("o"),x=eye.X,y=eye.Y,z=eye.Z,source=b.WorldField.SourceIdentity},true));
            Debug.Log("MOUNTAIN_REVIEW_STARTED "+output);
        }
        [Serializable]sealed class Viewpoint{public string utc,source;public double x,y,z;}
        [MenuItem("Ashen Voxel/Exploration/Beta/Return from mountain review (Play)")]
        static void Return()
        {
            var b=Current();if(!reviewing||b==null||!b.Ready)return;
            b.Player.ResetAt(previousFeet,previousFlying);b.ActiveSettings.FieldOfView=previousFov;
            b.Vista.SetObserverPose(b.Player.Eye,previousRotation);b.SetSettingsPanel(false,previousInput);reviewing=false;
        }
        static ExplorationBootstrap captureBoot;static Behaviour captureMatter;
        static bool captureHud,captureInput,capturePanel,captureMatterEnabled;
        static string frameOutput;static int captureAfterFrame;static bool frameRequested;static double captureDeadline;
        static readonly System.Reflection.FieldInfo Hud=typeof(ExplorationBootstrap).GetField("showHud",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic);
        static readonly System.Reflection.FieldInfo Panel=typeof(ExplorationBootstrap).GetField("settingsOpen",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic);
        [MenuItem("Ashen Voxel/Exploration/Beta/Capture clean game frame (Play)")]
        static void CaptureFrame()=>BeginCleanFrame("mountain-game-"+DateTime.UtcNow.ToString("yyyyMMdd-HHmmss"));
        static void BeginCleanFrame(string label)
        {
            var b=Current();if(frameOutput!=null||b==null||!b.Ready||Hud==null||Panel==null)return;
            if(label.Length<1||label.Length>80||System.Text.RegularExpressions.Regex.IsMatch(label,"[^a-zA-Z0-9_-]"))return;
            captureBoot=b;captureHud=(bool)Hud.GetValue(b);capturePanel=(bool)Panel.GetValue(b);captureInput=b.Player.InputEnabled;
            captureMatter=b.GetComponent<ExplorationBetaMatter>();captureMatterEnabled=captureMatter!=null&&captureMatter.enabled;
            Hud.SetValue(b,false);b.SetSettingsPanel(false,false);if(captureMatter!=null)captureMatter.enabled=false;
            frameOutput="Logs/MountainReviewFrames/"+label;Directory.CreateDirectory(frameOutput);
            captureAfterFrame=Time.frameCount+8;captureDeadline=EditorApplication.timeSinceStartup+15;frameRequested=false;
        }
        [Serializable]sealed class FrameRecord{public string utc,mode;public int completedFrame;public float fov;public string appliedPreset;}
        static bool CompletePng(string path)
        {
            if(!File.Exists(path))return false;
            try{using(var f=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite))
            {if(f.Length<64)return false;f.Seek(-8,SeekOrigin.End);byte[] end=new byte[8];return f.Read(end,0,8)==8&&end[0]==73&&end[1]==69&&end[2]==78&&end[3]==68&&end[4]==174&&end[5]==66&&end[6]==96&&end[7]==130;}}
            catch(IOException){return false;}
        }
        static void RestoreCapture()
        {
            if(captureBoot!=null){Hud.SetValue(captureBoot,captureHud);captureBoot.SetSettingsPanel(capturePanel,captureInput);}
            if(captureMatter!=null)captureMatter.enabled=captureMatterEnabled;
            frameOutput=null;captureBoot=null;captureMatter=null;frameRequested=false;
        }
        static void TickCleanFrame()
        {
            if(frameOutput==null)return;
            if(!Application.isPlaying||captureBoot==null||EditorApplication.timeSinceStartup>captureDeadline){RestoreCapture();return;}
            if(Time.frameCount<captureAfterFrame){EditorApplication.QueuePlayerLoopUpdate();return;}
            try
            {
                if(!frameRequested){ScreenCapture.CaptureScreenshot(Path.GetFullPath(frameOutput+"/view.png"),1);frameRequested=true;EditorApplication.QueuePlayerLoopUpdate();return;}
                if(!CompletePng(frameOutput+"/view.png")){EditorApplication.QueuePlayerLoopUpdate();return;}
                File.WriteAllText(frameOutput+"/frame.json",JsonUtility.ToJson(new FrameRecord{utc=DateTime.UtcNow.ToString("o"),mode="Actual game-frame ScreenCapture after8 UI-free frames; native Game-view resolution/history; no Camera.Render or image editing",completedFrame=Time.frameCount,fov=captureBoot.Vista.MainCamera.fieldOfView,appliedPreset=captureBoot.AppliedTerrainQuality.Label},true));
                ExplorationTerrainDiagnostic.Capture(captureBoot,frameOutput);Debug.Log("CLEAN_GAME_FRAME "+frameOutput);RestoreCapture();
            }
            catch(Exception e){Debug.LogWarning("Game frame capture failed: "+e.Message);RestoreCapture();}
        }
        static void ReviewTick()
        {
            TickCleanFrame();
            var b=Current();if(!Application.isPlaying){reviewing=false;return;}
            if(b==null||!b.Ready||EditorApplication.isCompiling)return;
            const string frameRequest=".terrain-cache/mountain-clean-frame.request";
            if(frameOutput==null&&File.Exists(frameRequest)){string label=File.ReadAllText(frameRequest).Trim();File.Delete(frameRequest);BeginCleanFrame(label);}
            const string request=".terrain-cache/mountain-review.request";
            if(File.Exists(request)){File.Delete(request);Mountain();}
            if(!reviewing||EditorApplication.timeSinceStartup<nextSample||EditorApplication.timeSinceStartup-started>300)return;
            nextSample=EditorApplication.timeSinceStartup+10;
            try{ExplorationTerrainDiagnostic.Capture(b,output+"/sample-"+((int)(EditorApplication.timeSinceStartup-started)).ToString("D3"));}
            catch(Exception e){Debug.LogWarning("Mountain diagnostics: "+e.Message);}
        }
        static ExplorationBootstrap Current()=>UnityEngine.Object.FindFirstObjectByType<ExplorationBootstrap>();
        [MenuItem("Ashen Voxel/Exploration/Beta/Open in-game settings (Play)")]
        static void Settings(){var b=Current();if(Application.isPlaying&&b!=null)b.SetSettingsPanel(true);}
        [MenuItem("Ashen Voxel/Exploration/Beta/Release pointer for branch controls (Play)")]
        static void Pointer(){var b=Current();if(Application.isPlaying&&b!=null)b.SetSettingsPanel(false,false);}
        [MenuItem("Ashen Voxel/Exploration/Beta/Resume gameplay input (Play)")]
        static void Resume(){var b=Current();if(Application.isPlaying&&b!=null)b.SetSettingsPanel(false,true);}
    }
}
