using System;
using System.IO;
using AshenVoxel.Authoring;
using AshenVoxel.Core.Trees;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace AshenVoxel.Editor
{
    [CustomEditor(typeof(TreeProfile))]
    public sealed class TreeProfileEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            DrawDefaultInspector();
            serializedObject.ApplyModifiedProperties();
            var profile = (TreeProfile)target;
            EditorGUILayout.Space();
            EditorGUILayout.HelpBox("Changing species keeps your edits. Apply species defaults to replace shape and palette deliberately. Height is mature height; Age changes the grown size.", MessageType.Info);
            if (GUILayout.Button("Apply species defaults"))
            {
                Undo.RecordObject(profile, "Apply tree species defaults"); profile.ApplySpeciesDefaults();
                EditorUtility.SetDirty(profile);
            }
            if (GUILayout.Button("New seed"))
            {
                Undo.RecordObject(profile, "New tree seed");
                profile.Seed = Guid.NewGuid().GetHashCode(); EditorUtility.SetDirty(profile);
            }
            using (new EditorGUI.DisabledScope(!EditorApplication.isPlaying))
            {
                if (GUILayout.Button("Regenerate active workshop"))
                    foreach (var showcase in UnityEngine.Object.FindObjectsByType<TreeShowcase>(FindObjectsSortMode.None)) showcase.Regenerate();
            }
            if (!EditorApplication.isPlaying) EditorGUILayout.LabelField("Press Play in TreeWorkshop to preview changes.", EditorStyles.wordWrappedLabel);
        }
    }

    [CustomEditor(typeof(TreeShowcase))]
    public sealed class TreeShowcaseEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            EditorGUILayout.Space();
            EditorGUILayout.HelpBox("Assign a Tree Profile to inspect one specimen, or use Four Species to compare the preset grove. Profile edits are saved assets; generated previews exist only during Play mode.", MessageType.Info);
            using (new EditorGUI.DisabledScope(!EditorApplication.isPlaying))
                if (GUILayout.Button("Regenerate trees")) ((TreeShowcase)target).Regenerate();
        }
    }

    public static class TreeAuthoringMenu
    {
        const string PresetFolder = "Assets/AshenVoxel/Settings/Trees";
        public const string WorkshopScenePath = "Assets/Scenes/TreeWorkshop.unity";
        [MenuItem("Ashen Voxel/Trees/Create species profiles")]
        public static void CreateProfiles()
        {
            Directory.CreateDirectory(PresetFolder); AssetDatabase.Refresh();
            foreach (TreeSpecies species in Enum.GetValues(typeof(TreeSpecies)))
            {
                string path = PresetFolder + "/" + species + ".asset";
                if (AssetDatabase.LoadAssetAtPath<TreeProfile>(path) != null) continue;
                var profile = ScriptableObject.CreateInstance<TreeProfile>();
                profile.Species = species; profile.Seed = 714103 + (int)species * 977;
                profile.ApplySpeciesDefaults(); AssetDatabase.CreateAsset(profile, path);
            }
            AssetDatabase.SaveAssets();
        }
        [MenuItem("Ashen Voxel/Trees/Open tree workshop")]
        public static void OpenWorkshop()
        {
            if (EditorApplication.isPlaying)
            { Debug.Log("Leave Play mode before opening the Tree Workshop scene."); return; }
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            CreateProfiles();
            if (File.Exists(WorkshopScenePath))
            { EditorSceneManager.OpenScene(WorkshopScenePath); return; }
            ProjectSetup.EnsureDemo();
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var showcase = new GameObject("Tree Workshop").AddComponent<TreeShowcase>();
            showcase.Layout = TreeWorkshopLayout.FourSpecies;
            showcase.SpeciesProfiles = new TreeProfile[4];
            for (int i = 0; i < 4; i++)
                showcase.SpeciesProfiles[i] = AssetDatabase.LoadAssetAtPath<TreeProfile>(PresetFolder + "/" + (TreeSpecies)i + ".asset");
            showcase.SelectedProfile = showcase.SpeciesProfiles[1];
            var sun = new GameObject("Workshop afternoon light").AddComponent<Light>();
            sun.type = LightType.Directional; sun.intensity = 2.2f;
            sun.color = new Color(1, .85f, .69f); sun.shadows = LightShadows.Soft;
            sun.shadowBias = .025f; sun.transform.rotation = Quaternion.Euler(34, -38, 0);
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(.35f, .4f, .46f);
            RenderSettings.ambientEquatorColor = new Color(.2f, .23f, .24f);
            RenderSettings.ambientGroundColor = new Color(.1f, .11f, .1f);
            RenderSettings.fog = false; RenderSettings.skybox = null;
            Directory.CreateDirectory("Assets/Scenes");
            EditorSceneManager.SaveScene(scene, WorkshopScenePath);
            Selection.activeGameObject = showcase.gameObject;
            Debug.Log("Tree Workshop created. Press Play to grow the grove.");
        }
    }
}
