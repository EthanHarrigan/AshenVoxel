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
        static ExplorationBootstrap captureBoot;static ExplorationBetaMatter captureMatter;static ExplorationShrubMatter captureShrub;static bool captureShrubHud;
        static bool captureHud,captureInput,capturePanel,captureMatterEnabled;
        static string frameOutput;static int captureAfterFrame;static bool frameRequested;static double captureDeadline;
        static readonly System.Reflection.FieldInfo Hud=typeof(ExplorationBootstrap).GetField("showHud",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic);
        static readonly System.Reflection.FieldInfo Panel=typeof(ExplorationBootstrap).GetField("settingsOpen",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic);
        [MenuItem("Ashen Voxel/Exploration/Beta/Capture clean game frame (Play)")]
        static void CaptureFrame()=>BeginCleanFrame("mountain-game-"+DateTime.UtcNow.ToString("yyyyMMdd-HHmmss"));
        static void BeginCleanFrame(string label)
        {
            var b=Current();if(sequenceOutput!=null||frameOutput!=null||b==null||!b.Ready||Hud==null||Panel==null)return;
            if(label.Length<1||label.Length>80||System.Text.RegularExpressions.Regex.IsMatch(label,"[^a-zA-Z0-9_-]"))return;
            captureBoot=b;captureHud=(bool)Hud.GetValue(b);capturePanel=(bool)Panel.GetValue(b);captureInput=b.Player.InputEnabled;
            captureMatter=b.GetComponent<ExplorationBetaMatter>();captureMatterEnabled=captureMatter!=null&&captureMatter.ShowBetaHud;
            captureShrub=b.GetComponent<ExplorationShrubMatter>();captureShrubHud=captureShrub!=null&&captureShrub.ShowBetaHud;
            Hud.SetValue(b,false);b.SetSettingsPanel(false,false);if(captureMatter!=null)captureMatter.ShowBetaHud=false;if(captureShrub!=null)captureShrub.ShowBetaHud=false;
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
            if(captureMatter!=null)captureMatter.ShowBetaHud=captureMatterEnabled;
            if(captureShrub!=null)captureShrub.ShowBetaHud=captureShrubHud;captureShrub=null;
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
            TickFeatureSequence();TickCleanFrame();
            const string workerRequest=".terrain-cache/release-idle-import-workers.request";
            if(!Application.isPlaying&&!EditorApplication.isCompiling&&!EditorApplication.isUpdating&&File.Exists(workerRequest)){File.Delete(workerRequest);ReleaseIdleImportWorkers();}
            var b=Current();if(!Application.isPlaying){reviewing=false;return;}
            if(b==null||!b.Ready||EditorApplication.isCompiling)return;
            const string featureRequest=".terrain-cache/feature-capture.request";
            if(sequenceOutput==null&&frameOutput==null&&File.Exists(featureRequest))
            {string feature=File.ReadAllText(featureRequest).Trim();File.Delete(featureRequest);if(feature=="valley")ValleyFrame();else if(feature=="branch")RecordBranch();else if(feature=="bees")RecordBees();}
            const string frameRequest=".terrain-cache/mountain-clean-frame.request";
            if(frameOutput==null&&File.Exists(frameRequest)){string label=File.ReadAllText(frameRequest).Trim();File.Delete(frameRequest);BeginCleanFrame(label);}
            const string request=".terrain-cache/mountain-review.request";
            if(File.Exists(request)){File.Delete(request);Mountain();}
            if(!reviewing||EditorApplication.timeSinceStartup<nextSample||EditorApplication.timeSinceStartup-started>300)return;
            nextSample=EditorApplication.timeSinceStartup+10;
            try{ExplorationTerrainDiagnostic.Capture(b,output+"/sample-"+((int)(EditorApplication.timeSinceStartup-started)).ToString("D3"));}
            catch(Exception e){Debug.LogWarning("Mountain diagnostics: "+e.Message);}
        }

        [MenuItem("Ashen Voxel/Exploration/Beta/Frame mountain valley (Play)")]
        static void ValleyFrame()
        {
            var b=Current();if(!Application.isPlaying||b==null||!b.Ready)return;
            b.SetSettingsPanel(false,false);b.ActiveSettings.FieldOfView=70;b.Vista.MainCamera.fieldOfView=70;
            var eye=b.Player.Eye;var target=new WorldPoint(-69120,5300,-22400);
            var direction=new Vector3((float)(target.X-eye.X),(float)(target.Y-eye.Y),(float)(target.Z-eye.Z));
            b.Vista.SetObserverPose(eye,Quaternion.LookRotation(direction.normalized,Vector3.up));b.Vista.ResetObserverMotion();b.Vista.ForceReplan();
        }
        [MenuItem("Ashen Voxel/Exploration/Beta/Release idle import workers (Edit)")]
        static void ReleaseIdleImportWorkers()
        {
            if(Application.isPlaying||EditorApplication.isCompiling||EditorApplication.isUpdating)return;
            int original=AssetDatabase.DesiredWorkerCount;string error="";
            try{AssetDatabase.DesiredWorkerCount=0;AssetDatabase.ForceToDesiredWorkerCount();}
            catch(Exception e){error=e.Message;}
            finally{AssetDatabase.DesiredWorkerCount=original;}
            Directory.CreateDirectory("Logs/FeatureCapture");
            File.WriteAllText("Logs/FeatureCapture/import-worker-release.json",JsonUtility.ToJson(new WorkerRecord{utc=DateTime.UtcNow.ToString("o"),originalDesired=original,restoredDesired=AssetDatabase.DesiredWorkerCount,error=error},true));
        }
        [Serializable]sealed class WorkerRecord{public string utc,error;public int originalDesired,restoredDesired;}
        [Serializable]sealed class SequenceSample
        {public string file;public double elapsedSeconds;public int gameFrame,beeActors,debris;public long owned,cargo,aggregate;public bool conservation,cutAccepted,suction;public float maximumRotation;}
        [Serializable]sealed class SequenceRecord
        {public string utc,kind,source,mode="Actual native ScreenCapture frame sequence; real elapsed timing; no image generation or simulation time override";public bool newlyPlaced;public SequenceSample[] frames;}
        static string sequenceOutput,sequenceKind;static ExplorationBootstrap sequenceBoot;static double sequenceStart,nextSequenceFrame;static bool sequenceCut,sequencePlaced;
        static readonly System.Collections.Generic.List<SequenceSample> sequenceSamples=new System.Collections.Generic.List<SequenceSample>(80);
        static WorldPoint sequenceFeet;static bool sequenceFlying;static Quaternion sequenceRotation;static float sequenceFov;
        [MenuItem("Ashen Voxel/Exploration/Beta/Record placed branch fall and suction (Play)")]
        static void RecordBranch()=>BeginFeatureSequence("placed-branch");
        [MenuItem("Ashen Voxel/Exploration/Beta/Record existing bee patch (Play)")]
        static void RecordBees()=>BeginFeatureSequence("bees");
        static void BeginFeatureSequence(string kind)
        {
            var b=Current();if(!Application.isPlaying||b==null||!b.Ready||sequenceOutput!=null||frameOutput!=null)return;
            var matter=b.GetComponent<ExplorationBetaMatter>();sequencePlaced=false;
            captureBoot=b;captureHud=(bool)Hud.GetValue(b);capturePanel=(bool)Panel.GetValue(b);captureInput=b.Player.InputEnabled;
            sequenceFeet=b.Player.Feet;sequenceFlying=b.Player.Motor.Flying;sequenceRotation=b.Vista.MainCamera.transform.rotation;sequenceFov=b.ActiveSettings.FieldOfView;
            if(kind=="bees")
            {
                var bee=b.BeePatch;var flags=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic;
                if(bee==null||bee.GetType().GetField("owner",flags).GetValue(bee)==null){Debug.LogWarning("Bee recording unavailable: "+(bee==null?"not initialized":bee.Status));return;}
                double x=(double)bee.GetType().GetField("anchorX",flags).GetValue(bee),y=(double)bee.GetType().GetField("anchorY",flags).GetValue(bee),z=(double)bee.GetType().GetField("anchorZ",flags).GetValue(bee);
                b.SetSettingsPanel(false,false);b.Player.ResetAt(new WorldPoint(x+1.1,y+.45-b.Player.Motor.ViewHeight,z+1.1),true);
                var eye=b.Player.Eye;b.Vista.SetObserverPose(eye,Quaternion.LookRotation(new Vector3((float)(x-eye.X),(float)(y+.15-eye.Y),(float)(z-eye.Z)),Vector3.up));b.ActiveSettings.FieldOfView=45;b.Vista.MainCamera.fieldOfView=45;
            }
            else
            {
                if(matter==null)return;sequencePlaced=matter.Place();if(matter.World==null)return;
                b.SetSettingsPanel(false,false);b.ActiveSettings.FieldOfView=60;b.Vista.MainCamera.fieldOfView=60;
                b.Vista.SetObserverPose(b.Player.Eye,Quaternion.Euler(8,b.Player.Yaw,0));
            }
            captureMatter=matter;captureMatterEnabled=matter!=null&&matter.ShowBetaHud;
            captureShrub=b.GetComponent<ExplorationShrubMatter>();captureShrubHud=captureShrub!=null&&captureShrub.ShowBetaHud;
            Hud.SetValue(b,false);b.SetSettingsPanel(false,false);if(matter!=null)matter.ShowBetaHud=false;if(captureShrub!=null)captureShrub.ShowBetaHud=false;
            sequenceBoot=b;sequenceKind=kind;sequenceOutput="Logs/FeatureCapture/"+kind+"-"+DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");Directory.CreateDirectory(sequenceOutput);
            sequenceStart=EditorApplication.timeSinceStartup;nextSequenceFrame=.25;sequenceCut=false;sequenceSamples.Clear();
        }
        static void TickFeatureSequence()
        {
            if(sequenceOutput==null)return;
            if(!Application.isPlaying||sequenceBoot==null){sequenceOutput=null;RestoreCapture();return;}
            double elapsed=EditorApplication.timeSinceStartup-sequenceStart;var matter=sequenceBoot.GetComponent<ExplorationBetaMatter>();
            if(sequenceKind=="placed-branch"&&matter!=null)
            {if(elapsed>=.9&&!sequenceCut&&elapsed<2.5)sequenceCut=matter.CutSupport();if(elapsed>=3) matter.SuctionEnabled=true;}
            if(elapsed<8&&sequenceSamples.Count<80&&elapsed>=nextSequenceFrame)
            {
                nextSequenceFrame=elapsed+.1;string file="frame-"+sequenceSamples.Count.ToString("D3")+".png";
                ScreenCapture.CaptureScreenshot(Path.GetFullPath(sequenceOutput+"/"+file),1);
                sequenceSamples.Add(new SequenceSample{file=file,elapsedSeconds=elapsed,gameFrame=Time.frameCount,beeActors=sequenceBoot.BeePatch?.DrawnActors??0,
                    owned=matter?.OwnedCells??0,cargo=matter?.Matter?.CreditedCells??0,debris=matter?.Matter?.DebrisCount??0,aggregate=matter?.Actor?.Aggregate.Remaining??0,
                    conservation=matter!=null&&matter.ConservationIntact,cutAccepted=sequenceCut,suction=matter!=null&&matter.SuctionEnabled,maximumRotation=matter?.MaximumRotationDegrees??0});
            }
            if(elapsed<8.2)return;
            bool complete=true;foreach(var sample in sequenceSamples)if(!CompletePng(sequenceOutput+"/"+sample.file)){complete=false;break;}
            if(!complete&&elapsed<12)return;
            File.WriteAllText(sequenceOutput+"/sequence.json",JsonUtility.ToJson(new SequenceRecord{utc=DateTime.UtcNow.ToString("o"),kind=sequenceKind,source=sequenceBoot.WorldField.SourceIdentity,newlyPlaced=sequencePlaced,frames=sequenceSamples.ToArray()},true));
            Debug.Log("FEATURE_SEQUENCE "+sequenceOutput+" frames="+sequenceSamples.Count+" complete="+complete);
            sequenceBoot.ActiveSettings.FieldOfView=sequenceFov;sequenceBoot.Vista.MainCamera.fieldOfView=sequenceFov;
            sequenceBoot.Player.ResetAt(sequenceFeet,sequenceFlying);sequenceBoot.Vista.SetObserverPose(sequenceBoot.Player.Eye,sequenceRotation);
            sequenceOutput=null;sequenceBoot=null;RestoreCapture();
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
