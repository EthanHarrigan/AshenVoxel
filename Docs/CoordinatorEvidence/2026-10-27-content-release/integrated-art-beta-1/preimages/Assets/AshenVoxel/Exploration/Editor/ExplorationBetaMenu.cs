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
        static void ReviewTick()
        {
            var b=Current();if(!Application.isPlaying){reviewing=false;return;}
            if(b==null||!b.Ready||EditorApplication.isCompiling)return;
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
