using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace AshenVoxel.WorldScaleRuntime.Editor
{
    [CustomEditor(typeof(GroundCoverShowcase))]
    public sealed class GroundCoverShowcaseEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            EditorGUILayout.HelpBox("Press Play, then Tab to inspect each 10 cm specimen. Compare six rock shapes uses one material, one seed and the same size settings so silhouette differences stay clear. Shape Study Material chooses that material; Rock Shape Override affects the five rocks in Full Collection. Plants are static mockups without wind or growth simulation.", MessageType.Info);
            using (new EditorGUI.DisabledScope(!EditorApplication.isPlaying))
            {
                if (GUILayout.Button("Compare six rock shapes"))
                {
                    var showcase = (GroundCoverShowcase)target;
                    showcase.Layout = GroundCoverWorkshopLayout.RockShapes;
                    showcase.OverviewOnStart = true;
                    showcase.Regenerate();
                }
                if (GUILayout.Button("Regenerate current seed")) ((GroundCoverShowcase)target).Regenerate();
                if (GUILayout.Button("Frame all specimens")) ((GroundCoverShowcase)target).FrameAll();
            }
        }
    }

    public static class GroundCoverWorkshopSetup
    {
        public const string ScenePath = "Assets/Scenes/GroundCoverWorkshop.unity";

        [MenuItem("Ashen Voxel/World/Open ground-cover workshop")]
        public static void Open()
        {
            if (EditorApplication.isPlaying) { Debug.Log("Leave Play mode before opening the ground-cover workshop."); return; }
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            if (File.Exists(ScenePath)) { EditorSceneManager.OpenScene(ScenePath); return; }
            Create();
        }

        [MenuItem("Ashen Voxel/World/Create or repair ground-cover workshop")]
        public static void Repair()
        {
            if (EditorApplication.isPlaying) { Debug.Log("Leave Play mode before repairing the ground-cover workshop."); return; }
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            Create();
        }

        static void Create()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var root = new GameObject("Ground-cover Workshop").AddComponent<GroundCoverShowcase>();
            var sun = new GameObject("Workshop daylight").AddComponent<Light>();
            sun.type = LightType.Directional; sun.intensity = 2.1f; sun.shadows = LightShadows.Soft;
            sun.color = new Color(1, .91f, .78f); sun.transform.rotation = Quaternion.Euler(42, -32, 0);
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(.36f, .42f, .48f);
            RenderSettings.ambientEquatorColor = new Color(.24f, .28f, .29f);
            RenderSettings.ambientGroundColor = new Color(.13f, .14f, .12f);
            RenderSettings.fog = false; RenderSettings.skybox = null;
            Directory.CreateDirectory("Assets/Scenes");
            EditorSceneManager.SaveScene(scene, ScenePath);
            Selection.activeGameObject = root.gameObject;
        }
    }
}
