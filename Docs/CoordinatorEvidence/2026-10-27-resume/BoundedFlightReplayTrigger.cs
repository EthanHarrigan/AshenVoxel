using System;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using AshenVoxel.Exploration;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace AshenVoxel.Validation
{
    // Passive arming adapter. Never starts Play, changes scenes or arms the
    // independent native memory guard. No request => no replay object.
    [InitializeOnLoad] public static class BoundedFlightReplayTrigger
    {
        const string ScenePath="Assets/_Validation/CoordinatorSmoke/ExplorationSmoke_20261027.unity";
        const string Request="C:/Users/ethan/Downloads/AshenVoxel/.terrain-cache/native-flight-replay.request";
        static double next;static bool attempted;
        static BoundedFlightReplayTrigger(){EditorApplication.update+=Poll;}
        static void Poll()
        {
            if(attempted||EditorApplication.timeSinceStartup<next)return;next=EditorApplication.timeSinceStartup+.2;
            if(!EditorApplication.isPlaying||EditorApplication.isCompiling||EditorApplication.isUpdating||
                SceneManager.GetActiveScene().path!=ScenePath||!File.Exists(Request))return;
            var boot=UnityEngine.Object.FindFirstObjectByType<ExplorationBootstrap>();
            if(boot==null||!boot.Ready)return;
            attempted=true;GameObject owned=null;
            try
            {
                if(!String.Equals(Path.GetFullPath(Application.dataPath).TrimEnd('/','\\'),"C:\\Users\\ethan\\Downloads\\AshenVoxel\\Assets",StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("Replay is scoped to existing main Editor only.");
                string consumed=Request+".consumed-"+DateTime.UtcNow.ToString("yyyyMMddTHHmmssfffZ");
                File.Move(Request,consumed);
                if(new FileInfo(consumed).Length>256)throw new InvalidDataException("Oversized replay marker.");
                string[] p=File.ReadAllText(consumed).TrimEnd('\r','\n').Split('\t');
                if(p.Length!=3||p[0]!="bounded-native-flight-v1"||!Regex.IsMatch(p[1],"^[A-Za-z0-9_-]{1,60}$")||
                    !DateTime.TryParseExact(p[2],"O",CultureInfo.InvariantCulture,DateTimeStyles.RoundtripKind,out DateTime time)||time.Kind!=DateTimeKind.Utc)
                    throw new InvalidDataException("Replay marker identity invalid.");
                double age=(DateTime.UtcNow-time).TotalSeconds;if(age<0||age>30)throw new InvalidDataException("Replay marker expired/future dated.");
                owned=new GameObject("Owned bounded native flight replay");owned.transform.SetParent(boot.transform,false);
                owned.AddComponent<BoundedFlightReplay>().Configure(boot,p[1]);
            }
            catch(Exception e){if(owned!=null)UnityEngine.Object.Destroy(owned);UnityEngine.Debug.LogException(e);}
        }
    }
}
