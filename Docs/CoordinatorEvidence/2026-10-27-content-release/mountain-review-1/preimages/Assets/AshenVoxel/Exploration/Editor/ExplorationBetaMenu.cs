using UnityEditor;
using UnityEngine;
namespace AshenVoxel.Exploration.Editor
{
    // Useful when Game view input is captured or an accessibility tool cannot send function keys.
    public static class ExplorationBetaMenu
    {
        static ExplorationBootstrap Current()=>Object.FindFirstObjectByType<ExplorationBootstrap>();
        [MenuItem("Ashen Voxel/Exploration/Beta/Open in-game settings (Play)")]
        static void Settings(){var b=Current();if(Application.isPlaying&&b!=null)b.SetSettingsPanel(true);}
        [MenuItem("Ashen Voxel/Exploration/Beta/Release pointer for branch controls (Play)")]
        static void Pointer(){var b=Current();if(Application.isPlaying&&b!=null)b.SetSettingsPanel(false,false);}
        [MenuItem("Ashen Voxel/Exploration/Beta/Resume gameplay input (Play)")]
        static void Resume(){var b=Current();if(Application.isPlaying&&b!=null)b.SetSettingsPanel(false,true);}
    }
}
