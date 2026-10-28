using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace AshenVoxel.WorldScaleRuntime.Editor
{
    [CustomEditor(typeof(WorldExplorer))]
    public sealed class WorldExplorerEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            EditorGUILayout.HelpBox("This scene tests large terrain and distance LOD. P switches to a separate finite10cm inspection region; its temporary edits are discarded on exit. Change settings, then Rebuild. Imported atlas files are prepared by Tools/WorldScaleImport.", MessageType.Info);
            using (new EditorGUI.DisabledScope(!EditorApplication.isPlaying))
                if (GUILayout.Button("Rebuild world explorer")) ((WorldExplorer)target).Regenerate();
        }
    }
    public static class WorldExplorerSetup
    {
        public const string ScenePath = "Assets/Scenes/WorldExplorer.unity";
        [MenuItem("Ashen Voxel/World/Open baked neural sample")]
        public static void OpenNeuralSample() => OpenAtlas("Assets/AshenVoxel/WorldScaleRuntime/Samples/NeuralTerrain.bytes", 4);
        [MenuItem("Ashen Voxel/World/Open imported Ceteviles map")]
        public static void OpenCeteviles() => OpenAtlas("Assets/AshenVoxel/WorldScaleRuntime/Samples/Ceteviles.bytes", 32);
        [MenuItem("Ashen Voxel/World/Open baked Ceteviles neural sample")]
        public static void OpenCetevilesNeural() => OpenAtlas("Assets/AshenVoxel/WorldScaleRuntime/Samples/CetevilesNeural.bytes", 4);
        [MenuItem("Ashen Voxel/World/Build explorer Linux")]
        public static void BuildLinux() => Build(BuildTarget.StandaloneLinux64, "Builds/WorldExplorer/Linux/AshenVoxelWorld.x86_64");
        [MenuItem("Ashen Voxel/World/Build explorer Windows")]
        public static void BuildWindows() => Build(BuildTarget.StandaloneWindows64, "Builds/WorldExplorer/Windows/AshenVoxelWorld.exe");
        static void Build(BuildTarget target, string output)
        {
            if (EditorApplication.isPlaying) { Debug.Log("Leave Play mode before building."); return; }
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            if (!File.Exists(ScenePath) && !TryOpen()) return;
            Directory.CreateDirectory(Path.GetDirectoryName(output));
            var report = BuildPipeline.BuildPlayer(new[] { ScenePath }, output, target, BuildOptions.Development);
            if (report.summary.result != UnityEditor.Build.Reporting.BuildResult.Succeeded)
                throw new System.InvalidOperationException("World Explorer build did not succeed: " + report.summary.result);
        }
        static void OpenAtlas(string path, float distance)
        {
            if (EditorApplication.isPlaying) { Debug.Log("Leave Play mode before opening a world source."); return; }
            TextAsset atlas = AssetDatabase.LoadAssetAtPath<TextAsset>(path);
            if (atlas == null) { Debug.LogError("World atlas asset is missing: " + path); return; }
            if (!TryOpen()) return;
            WorldExplorer[] explorers = Object.FindObjectsByType<WorldExplorer>(FindObjectsSortMode.None);
            foreach (WorldExplorer explorer in explorers)
            {
                Undo.RecordObject(explorer, "Choose world source");
                explorer.BakedWorldAtlas = atlas; explorer.UseImportedDimensions = true;
                // Climate archives are strictly paired by the original height
                // bytes. Clearing when absent prevents a previous sample's
                // climate from being silently retained after a menu switch.
                explorer.BakedClimateAtlas = AssetDatabase.LoadAssetAtPath<TextAsset>(path.Replace(".bytes", ".climate.bytes"));
                explorer.UseRemoteDiffusion = false; explorer.ViewDistanceKm = distance;
                EditorUtility.SetDirty(explorer); Selection.activeGameObject = explorer.gameObject;
            }
        }
        [MenuItem("Ashen Voxel/World/Open world explorer")]
        public static void Open()
        { TryOpen(); }
        static bool TryOpen()
        {
            if (EditorApplication.isPlaying) { Debug.Log("Leave Play mode before opening the world explorer."); return false; }
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return false;
            if (File.Exists(ScenePath)) { EditorSceneManager.OpenScene(ScenePath); return true; }
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            new GameObject("World Explorer").AddComponent<WorldExplorer>();
            var sun = new GameObject("Highland daylight").AddComponent<Light>();
            sun.type = LightType.Directional; sun.intensity = 2.1f; sun.shadows = LightShadows.Soft;
            sun.color = new Color(1, .9f, .78f); sun.transform.rotation = Quaternion.Euler(33, -28, 0);
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(.36f, .41f, .47f);
            RenderSettings.ambientEquatorColor = new Color(.21f, .25f, .28f);
            RenderSettings.ambientGroundColor = new Color(.11f, .13f, .14f);
            RenderSettings.fog = false; RenderSettings.skybox = null;
            Directory.CreateDirectory("Assets/Scenes"); EditorSceneManager.SaveScene(scene, ScenePath);
            return true;
        }
    }
}
