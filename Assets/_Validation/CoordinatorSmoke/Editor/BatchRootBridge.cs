using System;using System.IO;using AshenVoxel.Exploration;using UnityEditor;using UnityEngine;using UnityEngine.SceneManagement;
namespace AshenVoxel.Validation
{
 [InitializeOnLoad] public static class BatchRootBridge
 {
  static string Root=>Path.GetDirectoryName(Application.dataPath);static bool started;
  static BatchRootBridge(){EditorApplication.update+=Tick;EditorApplication.playModeStateChanged+=s=>{if(s==PlayModeStateChange.EnteredEditMode)started=false;};}
  static void Tick()
  {
   if(started||!EditorApplication.isPlaying||SceneManager.GetActiveScene().path!="Assets/_Validation/CoordinatorSmoke/ExplorationSmoke_20261027.unity")return;
   string request=Path.Combine(Root,".terrain-cache/content-batch-roots.request"),bee=Path.Combine(Root,".terrain-cache/content-batch-bees-2/report.json");
   if(!File.Exists(request)||!File.Exists(bee))return;
   var boot=UnityEngine.Object.FindFirstObjectByType<ExplorationBootstrap>();if(boot==null||!boot.Ready)return;
   string run=File.ReadAllText(request).Trim();File.Delete(request);started=true;
   try{var owner=boot.gameObject.AddComponent<RootNativeProbe>();owner.Sites=JsonUtility.FromJson<RootNativeProbe.SiteList>(File.ReadAllText(Path.Combine(Root,".terrain-cache/content-batch-root-sites.json"))).sites;owner.Configure(boot,run,Path.Combine(Root,".terrain-cache/shared-native-pass-2/roots"));}
   catch(Exception e){Directory.CreateDirectory(Path.Combine(Root,".terrain-cache/shared-native-pass-2/roots"));File.WriteAllText(Path.Combine(Root,".terrain-cache/shared-native-pass-2/roots/bridge-error.txt"),e.ToString());Debug.LogException(e);}
  }
 }
}
