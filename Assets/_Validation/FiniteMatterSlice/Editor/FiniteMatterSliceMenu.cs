using AshenVoxel.Core.Experimental;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace AshenVoxel.FiniteMatter.Editor
{
    public static class FiniteMatterSliceMenu
    {
        [MenuItem("Ashen Voxel/Validation/Open finite matter slice (WIP)")]
        public static void Open()
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode)return;
            if(!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())return;
            const string path="Assets/Scenes/FiniteMatterSlice.unity";
            if(System.IO.File.Exists(path)){EditorSceneManager.OpenScene(path);return;}
            var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            new GameObject("Finite matter slice owner").AddComponent<FiniteMatterSliceOwner>();
            EditorSceneManager.SaveScene(scene,path);
            Selection.activeGameObject=GameObject.Find("Finite matter slice owner");
        }
    }
}
