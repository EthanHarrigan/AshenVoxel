using UnityEditor;
namespace AshenVoxel.Exploration.Editor
{
    /// <summary>Project-file patches can precede the Package Manager's graph
    /// refresh. Stay compilable with SMAA and resolve the enabled built-in module.</summary>
    [InitializeOnLoad]
    static class ExplorationPackageRefresh
    {
        static ExplorationPackageRefresh()
        {
#if ENABLE_UPSCALER_FRAMEWORK && ENABLE_NVIDIA && !ASHEN_NVIDIA_MODULE
            EditorApplication.delayCall+=ResolveOnce;
#endif
        }
        static void ResolveOnce()
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode||EditorApplication.isCompiling||EditorApplication.isUpdating){EditorApplication.delayCall+=ResolveOnce;return;}
            const string key="AshenVoxel.NvidiaModuleResolve.v1";
            if(SessionState.GetBool(key,false))return;
            SessionState.SetBool(key,true);
            UnityEditor.PackageManager.Client.Resolve();
            UnityEngine.Debug.Log("Refreshing enabled Unity built-in NVIDIA module. Native SMAA remains available until package compilation finishes.");
        }
    }
}
