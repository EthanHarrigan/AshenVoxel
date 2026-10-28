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
        static ExplorationNaturalTreeMatter captureTree;static bool captureTreeHud;
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
            captureTree=b.GetComponent<ExplorationNaturalTreeMatter>();captureTreeHud=captureTree!=null&&captureTree.ShowBetaHud;if(captureTree!=null)captureTree.ShowBetaHud=false;
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
            if(captureTree!=null)captureTree.ShowBetaHud=captureTreeHud;captureTree=null;
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
            TickFeatureSequence();TickCleanFrame();TickNaturalTree();
            const string workerRequest=".terrain-cache/release-idle-import-workers.request";
            if(!EditorApplication.isCompiling&&!EditorApplication.isUpdating&&File.Exists(workerRequest)&&(!Application.isPlaying||Current()?.Ready==true)){File.Delete(workerRequest);ReleaseIdleImportWorkers();}
            var b=Current();if(!Application.isPlaying){reviewing=false;return;}
            if(b==null||!b.Ready||EditorApplication.isCompiling)return;
            const string featureRequest=".terrain-cache/feature-capture.request";
            if(sequenceOutput==null&&frameOutput==null&&File.Exists(featureRequest))
            {string feature=File.ReadAllText(featureRequest).Trim();File.Delete(featureRequest);if(feature=="valley")ValleyFrame();else if(feature=="branch")RecordBranch();else if(feature=="bees")RecordBees();else if(feature=="shrub")RecordShrub();}
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
        [MenuItem("Ashen Voxel/Exploration/Beta/Release idle import workers (idle Editor)")]
        static void ReleaseIdleImportWorkers()
        {
            if(EditorApplication.isCompiling||EditorApplication.isUpdating||(Application.isPlaying&&Current()?.Ready!=true))return;
            int original=AssetDatabase.DesiredWorkerCount;string error="";
            try{AssetDatabase.DesiredWorkerCount=0;AssetDatabase.ForceToDesiredWorkerCount();}
            catch(Exception e){error=e.Message;}
            finally{AssetDatabase.DesiredWorkerCount=original;}
            Directory.CreateDirectory("Logs/FeatureCapture");
            File.WriteAllText("Logs/FeatureCapture/import-worker-release.json",JsonUtility.ToJson(new WorkerRecord{utc=DateTime.UtcNow.ToString("o"),originalDesired=original,restoredDesired=AssetDatabase.DesiredWorkerCount,error=error},true));
        }
        [Serializable]sealed class WorkerRecord{public string utc,error;public int originalDesired,restoredDesired;}
        [Serializable]sealed class SequenceSample
        {public string file;public double elapsedSeconds;public int gameFrame,beeActors,debris,shrubPrototype,sourceRemaining;public bool shrubActive;public long owned,cargo,aggregate;public bool conservation,cutAccepted,suction;public float maximumRotation;}
        [Serializable]sealed class SequenceRecord
        {public string utc,kind,source,mode="Actual native ScreenCapture frame sequence; real elapsed timing; no image generation or simulation time override";public bool newlyPlaced;public SequenceSample[] frames;}
        static string sequenceOutput,sequenceKind;static ExplorationBootstrap sequenceBoot;static double sequenceStart,nextSequenceFrame;static bool sequenceCut,sequencePlaced;
        static readonly System.Collections.Generic.List<SequenceSample> sequenceSamples=new System.Collections.Generic.List<SequenceSample>(80);
        static WorldPoint sequenceFeet;static bool sequenceFlying;static Quaternion sequenceRotation;static float sequenceFov;
        static void RecordBranch()=>Debug.LogWarning("Placed-branch demo recording retired. Natural tree interaction is the requested feature.");
        [MenuItem("Ashen Voxel/Exploration/Beta/Record existing bee patch (Play)")]
        static void RecordBees()=>BeginFeatureSequence("bees");
        [MenuItem("Ashen Voxel/Exploration/Beta/Record existing shrub breakup and collection (Play)")]
        static void RecordShrub()=>BeginFeatureSequence("existing-shrub");
        static bool FindPublishedShrub(ExplorationBootstrap b,out PopulationInstance found)
        {
            found=default;double best=48*48;int seen=0;bool have=false;
            var flags=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Public;
            var cells=typeof(VegetationStreaming).GetField("cells",flags).GetValue(b.Population) as System.Collections.IDictionary;
            if(cells==null)return false;
            foreach(System.Collections.DictionaryEntry entry in cells)
            {
                var type=entry.Value.GetType();if(!(bool)type.GetField("Published",flags).GetValue(entry.Value))continue;
                var data=type.GetField("Data",flags).GetValue(entry.Value) as PopulationCell;if(data==null)continue;
                foreach(var instance in data.Instances)
                {
                    if(++seen>4096)return have;if((instance.PrototypeId!=102&&instance.PrototypeId!=103)||Math.Abs(instance.Scale-1)>1e-6)continue;
                    double dx=instance.Position.X-b.Player.Feet.X,dz=instance.Position.Z-b.Player.Feet.Z,d=dx*dx+dz*dz;
                    if(d<best){best=d;found=instance;have=true;}
                }
            }
            return have;
        }
        static void BeginFeatureSequence(string kind)
        {
            var b=Current();if(!Application.isPlaying||b==null||!b.Ready||sequenceOutput!=null||frameOutput!=null)return;
            var matter=b.GetComponent<ExplorationBetaMatter>();sequencePlaced=false;
            captureBoot=b;captureHud=(bool)Hud.GetValue(b);capturePanel=(bool)Panel.GetValue(b);captureInput=b.Player.InputEnabled;
            sequenceFeet=b.Player.Feet;sequenceFlying=b.Player.Motor.Flying;sequenceRotation=b.Vista.MainCamera.transform.rotation;sequenceFov=b.ActiveSettings.FieldOfView;
            if(kind=="existing-shrub")
            {
                var shrub=b.GetComponent<ExplorationShrubMatter>();
                if(shrub==null||shrub.HasMatterSession||!FindPublishedShrub(b,out PopulationInstance instance))
                {Debug.LogWarning("Shrub recording unavailable: no unowned published shrub102/103 within48m");return;}
                double yaw=b.Player.Yaw*Math.PI/180;
                double x=instance.Position.X-Math.Sin(yaw)*1.35,z=instance.Position.Z-Math.Cos(yaw)*1.35;
                double ground=b.WorldField.Sample(x,z).HeightMeters;
                b.SetSettingsPanel(false,false);b.Player.ResetAt(new WorldPoint(x,ground+.05,z),true);
                var eye=b.Player.Eye;b.Vista.SetObserverPose(eye,Quaternion.LookRotation(new Vector3((float)(instance.Position.X-eye.X),(float)(instance.Position.Y+.35-eye.Y),(float)(instance.Position.Z-eye.Z)),Vector3.up));
                b.ActiveSettings.FieldOfView=50;b.Vista.MainCamera.fieldOfView=50;
            }
            else if(kind=="bees")
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
            captureTree=b.GetComponent<ExplorationNaturalTreeMatter>();captureTreeHud=captureTree!=null&&captureTree.ShowBetaHud;if(captureTree!=null)captureTree.ShowBetaHud=false;
            Hud.SetValue(b,false);b.SetSettingsPanel(false,false);if(matter!=null)matter.ShowBetaHud=false;if(captureShrub!=null)captureShrub.ShowBetaHud=false;
            sequenceBoot=b;sequenceKind=kind;sequenceOutput="Logs/FeatureCapture/"+kind+"-"+DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");Directory.CreateDirectory(sequenceOutput);
            sequenceStart=EditorApplication.timeSinceStartup;nextSequenceFrame=.25;sequenceCut=false;sequenceSamples.Clear();
        }
        static void TickFeatureSequence()
        {
            if(sequenceOutput==null)return;
            if(!Application.isPlaying||sequenceBoot==null){sequenceOutput=null;RestoreCapture();return;}
            double elapsed=EditorApplication.timeSinceStartup-sequenceStart;var matter=sequenceBoot.GetComponent<ExplorationBetaMatter>();
            var shrub=sequenceBoot.GetComponent<ExplorationShrubMatter>();
            if(sequenceKind=="existing-shrub"&&shrub!=null)
            {if(elapsed>=1&&!sequenceCut&&elapsed<3)sequenceCut=shrub.BreakNearby();}
            // Recording never enables suction. Collection requires an explicit player action.
            if(elapsed<8&&sequenceSamples.Count<80&&elapsed>=nextSequenceFrame)
            {
                nextSequenceFrame=elapsed+.1;string file="frame-"+sequenceSamples.Count.ToString("D3")+".png";
                ScreenCapture.CaptureScreenshot(Path.GetFullPath(sequenceOutput+"/"+file),1);
                sequenceSamples.Add(new SequenceSample{file=file,elapsedSeconds=elapsed,gameFrame=Time.frameCount,beeActors=sequenceBoot.BeePatch?.DrawnActors??0,
                    owned=matter?.OwnedCells??0,cargo=matter?.Matter?.CreditedCells??0,debris=matter?.Matter?.DebrisCount??0,aggregate=matter?.Actor?.Aggregate.Remaining??0,
                    conservation=sequenceKind=="existing-shrub"?shrub!=null&&(!shrub.HasMatterSession||shrub.OwnedCells==shrub.InitialCells):matter!=null&&matter.ConservationIntact,
                    cutAccepted=sequenceCut,suction=sequenceKind=="existing-shrub"?shrub!=null&&shrub.SuctionEnabled:matter!=null&&matter.SuctionEnabled,maximumRotation=matter?.MaximumRotationDegrees??0,
                    shrubPrototype=shrub?.SourcePrototypeId??0,sourceRemaining=shrub?.RemainingSourceCells??0,shrubActive=shrub!=null&&shrub.SessionActive});
                if(sequenceKind=="existing-shrub"&&shrub!=null)
                {var sample=sequenceSamples[sequenceSamples.Count-1];sample.owned=shrub.OwnedCells;sample.cargo=shrub.CargoCells;sample.debris=shrub.DebrisCount;sample.aggregate=0;}
            }
            if(elapsed<8.2)return;
            bool complete=true;foreach(var sample in sequenceSamples)if(!CompletePng(sequenceOutput+"/"+sample.file)){complete=false;break;}
            if(!complete&&elapsed<12)return;
            File.WriteAllText(sequenceOutput+"/sequence.json",JsonUtility.ToJson(new SequenceRecord{utc=DateTime.UtcNow.ToString("o"),kind=sequenceKind,source=sequenceBoot.WorldField.SourceIdentity,newlyPlaced=sequencePlaced,frames=sequenceSamples.ToArray()},true));
            Debug.Log("FEATURE_SEQUENCE "+sequenceOutput+" frames="+sequenceSamples.Count+" complete="+complete);
            sequenceBoot.ActiveSettings.FieldOfView=sequenceFov;sequenceBoot.Vista.MainCamera.fieldOfView=sequenceFov;
            sequenceBoot.Player.ResetAt(sequenceFeet,sequenceFlying);sequenceBoot.Vista.SetObserverPose(sequenceBoot.Player.Eye,sequenceRotation);
            if(sequenceKind=="existing-shrub"&&shrub!=null)shrub.Restore(); // Explicit private-session rollback after its recording.
            sequenceOutput=null;sequenceBoot=null;RestoreCapture();
        }


        static string treeOutput;static double treeNextStatus,treeRecordStart,treeNextFrame;static int treeFrames;
        static bool treeRecording,treeHudBefore;static readonly System.Collections.Generic.List<TreeSample> treeSamples=new System.Collections.Generic.List<TreeSample>(100);
        [Serializable]sealed class TreeSample
        {public string utc,status,file;public double elapsed,x,y,z,debt;public int initial,source,falling,individual,cargo,impacts,pairs,prototype,pendingSupport;public bool active,conserved,suction,cutAccepted;}
        static TreeSample TreeState(ExplorationBootstrap b,string file="",bool cut=false)
        {
            var t=b.GetComponent<ExplorationNaturalTreeMatter>();var eye=b.Player.Eye;
            return new TreeSample{utc=DateTime.UtcNow.ToString("o"),status=t?.Status,file=file,elapsed=treeRecording?EditorApplication.timeSinceStartup-treeRecordStart:0,x=eye.X,y=eye.Y,z=eye.Z,
                debt=t?.PhysicsDebtSeconds??0,initial=t?.InitialCells??0,source=t?.SourceCells??0,falling=t?.FallingCells??0,individual=t?.IndividualCells??0,cargo=t?.CargoCells??0,impacts=t?.GroundImpacts??0,pairs=t?.PairContacts??0,prototype=t?.SourcePrototypeId??-1,pendingSupport=t?.PendingSupport??0,active=t!=null&&t.SessionActive,conserved=t==null||t.ConservationIntact,suction=t!=null&&t.SuctionEnabled,cutAccepted=cut};
        }
        static bool FindPublishedTree(ExplorationBootstrap b,out PopulationInstance found)
        {
            found=default;double best=1500*1500;int seen=0;bool have=false;
            var flags=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.NonPublic;
            var cells=typeof(VegetationStreaming).GetField("cells",flags).GetValue(b.Population) as System.Collections.IDictionary;
            if(cells==null)return false;
            foreach(System.Collections.DictionaryEntry entry in cells)
            {
                var type=entry.Value.GetType();if(!(bool)type.GetField("Published",flags).GetValue(entry.Value))continue;
                var data=type.GetField("Data",flags).GetValue(entry.Value) as PopulationCell;if(data==null)continue;
                foreach(var instance in data.Instances)
                {
                    if(++seen>8192)return have;if((instance.PrototypeId!=0&&instance.PrototypeId!=1)||instance.Scale!=1)continue;
                    double dx=instance.Position.X-b.Player.Feet.X,dz=instance.Position.Z-b.Player.Feet.Z,d=dx*dx+dz*dz;
                    if(d>=best)continue;best=d;found=instance;have=true;
                }
            }
            return have;
        }
        [MenuItem("Ashen Voxel/Exploration/Beta/Review existing natural birch (Play)")]
        static void ReviewNaturalTree()
        {
            var b=Current();if(b==null||!b.Ready)return;
            treeOutput="Logs/FeatureCapture/natural-tree-"+DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");Directory.CreateDirectory(treeOutput);
            var t=b.GetComponent<ExplorationNaturalTreeMatter>();
            if(t==null||t.HasMatterSession||!FindPublishedTree(b,out PopulationInstance instance))
            {File.WriteAllText(treeOutput+"/acquisition.json","{\"status\":\"No unowned published young birch0/1 within1500m; no tree fabricated\"}");return;}
            double x=instance.Position.X+2.6,z=instance.Position.Z+.2;
            double ground=b.WorldField.Sample(x,z).HeightMeters;
            b.SetSettingsPanel(false,false);b.Player.ResetAt(new WorldPoint(x,ground+.05,z),true);
            var eye=b.Player.Eye;var direction=new Vector3((float)(instance.Position.X-eye.X),(float)(instance.Position.Y+.45-eye.Y),(float)(instance.Position.Z-eye.Z));
            b.ActiveSettings.FieldOfView=65;b.Vista.MainCamera.fieldOfView=65;b.Vista.SetObserverPose(eye,Quaternion.LookRotation(direction,Vector3.up));b.Vista.ResetObserverMotion();
            bool accepted=t.ActivateNearby();File.WriteAllText(treeOutput+"/acquisition.json",JsonUtility.ToJson(new TreeAcquisition{utc=DateTime.UtcNow.ToString("o"),accepted=accepted,prototype=instance.PrototypeId,x=instance.Position.X,y=instance.Position.Y,z=instance.Position.Z,yaw=instance.YawDegrees,source=b.WorldField.SourceIdentity},true));
        }
        [MenuItem("Ashen Voxel/Exploration/Beta/Review original existing forest (Play)")]
        static void ReviewExistingForest()
        {
            var b=Current();if(b==null||!b.Ready||b.Population.HasReservedShrubMatter)return;
            // Original world metadata clearing, inside the bundled 5m source. No
            // scene/spawn asset edit, population injection or fabricated specimen.
            const double x=-19065,z=-10200;double ground=b.WorldField.Sample(x,z).HeightMeters;
            b.SetSettingsPanel(false,false);b.Player.ResetAt(new WorldPoint(x,ground+2,z),true);
            b.ActiveSettings.FieldOfView=65;b.Vista.MainCamera.fieldOfView=65;
            b.Vista.SetObserverPose(b.Player.Eye,Quaternion.Euler(10,0,0));b.Vista.ResetObserverMotion();b.Vista.ForceReplan();
            Directory.CreateDirectory("Logs/FeatureCapture");File.WriteAllText("Logs/FeatureCapture/original-forest-view.json",JsonUtility.ToJson(new TreeAcquisition{utc=DateTime.UtcNow.ToString("o"),accepted=true,x=x,y=b.Player.Eye.Y,z=z,source=b.WorldField.SourceIdentity},true));
        }
        static void ReviewMappedBirchHabitat(bool high)
        {
            var b=Current();if(b==null||!b.Ready||b.Population.HasReservedShrubMatter)return;
            // Actual-source read-only population survey found young birch here.
            // Native acquisition still requires an existing published instance.
            const double x=-16575.262620561818,z=-8245.046197170825;double ground=b.WorldField.Sample(x,z).HeightMeters;
            b.SetSettingsPanel(false,false);b.Player.ResetAt(new WorldPoint(x,high?10000:ground+2,z),true);
            b.ActiveSettings.FieldOfView=65;b.Vista.MainCamera.fieldOfView=65;
            b.Vista.SetObserverPose(b.Player.Eye,Quaternion.Euler(high?75:10,0,0));b.Vista.ResetObserverMotion();b.Vista.ForceReplan();
            Directory.CreateDirectory("Logs/FeatureCapture");File.WriteAllText("Logs/FeatureCapture/mapped-birch-"+(high?"high":"land")+".json",JsonUtility.ToJson(new TreeAcquisition{utc=DateTime.UtcNow.ToString("o"),accepted=true,x=x,y=b.Player.Eye.Y,z=z,source=b.WorldField.SourceIdentity},true));
        }
        [Serializable]sealed class TreeAcquisition{public string utc,source;public bool accepted;public int prototype;public double x,y,z;public float yaw;}
        [Serializable]sealed class TreeSequence{public string mode="Actual Exploration game frames of an existing naturally generated tree. No forced motion or automatic suction.";public TreeSample[] frames;}
        [MenuItem("Ashen Voxel/Exploration/Beta/Record natural tree gameplay (Play)")]
        static void RecordNaturalTree()
        {
            var b=Current();var t=b?.GetComponent<ExplorationNaturalTreeMatter>();if(t==null||!t.SessionActive||treeRecording)return;
            treeOutput=treeOutput??"Logs/FeatureCapture/natural-tree-"+DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");Directory.CreateDirectory(treeOutput);
            treeRecording=true;treeRecordStart=EditorApplication.timeSinceStartup;treeNextFrame=0;treeFrames=0;treeSamples.Clear();treeHudBefore=t.ShowBetaHud;t.ShowBetaHud=false;Hud.SetValue(b,false);b.SetSettingsPanel(false,false);
        }
        static void TickNaturalTree()
        {
            var b=Current();if(!Application.isPlaying||b==null||!b.Ready){treeRecording=false;return;}
            const string request=".terrain-cache/natural-tree-review.request";
            if(File.Exists(request))
            {
                string action=File.ReadAllText(request).Trim();File.Delete(request);var t=b.GetComponent<ExplorationNaturalTreeMatter>();
                if(action=="birch-high")ReviewMappedBirchHabitat(true);else if(action=="birch-land")ReviewMappedBirchHabitat(false);else if(action=="forest")ReviewExistingForest();else if(action=="acquire")ReviewNaturalTree();else if(action=="record")RecordNaturalTree();
                else if(action=="cut"&&t!=null){bool cut=t.CutTrunk();if(treeOutput!=null)File.WriteAllText(treeOutput+"/cut-command.json",JsonUtility.ToJson(TreeState(b,"",cut),true));}
                else if(action=="kick"&&t!=null)t.Kick();else if(action=="vacuum"&&t!=null)t.SuctionEnabled=!t.SuctionEnabled;
                else if(action=="restore"&&t!=null)t.Restore();
            }
            if(treeOutput==null)return;
            if(EditorApplication.timeSinceStartup>=treeNextStatus){treeNextStatus=EditorApplication.timeSinceStartup+1;File.WriteAllText(treeOutput+"/latest.json",JsonUtility.ToJson(TreeState(b),true));}
            if(!treeRecording)return;double elapsed=EditorApplication.timeSinceStartup-treeRecordStart;
            if(elapsed<12&&treeFrames<96&&EditorApplication.timeSinceStartup>=treeNextFrame)
            {treeNextFrame=EditorApplication.timeSinceStartup+.125;string file="frame-"+(treeFrames++).ToString("D3")+".png";ScreenCapture.CaptureScreenshot(Path.GetFullPath(treeOutput+"/"+file),1);treeSamples.Add(TreeState(b,file));}
            if(elapsed>=12){treeRecording=false;var t=b.GetComponent<ExplorationNaturalTreeMatter>();if(t!=null)t.ShowBetaHud=treeHudBefore;File.WriteAllText(treeOutput+"/sequence.json",JsonUtility.ToJson(new TreeSequence{frames=treeSamples.ToArray()},true));}
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
