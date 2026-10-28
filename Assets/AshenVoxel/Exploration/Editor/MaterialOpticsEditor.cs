using AshenVoxel.Core;
using AshenVoxel.Rendering;
using UnityEditor;
using UnityEngine;
namespace AshenVoxel.Exploration.Editor
{
    [CustomEditor(typeof(MaterialOpticsProfile))]
    public sealed class MaterialOpticsEditor : UnityEditor.Editor
    {
        [MenuItem("Ashen Voxel/Appearance/Material transparency and transmission")]
        static void SelectProfile(){Selection.activeObject=Resources.Load<MaterialOpticsProfile>("MaterialOptics");}
        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            EditorGUILayout.HelpBox("Transparency changes surface coverage; light transmission adds soft backlighting independently. Default leaves are opaque beyond 0.8m and fade by at most 6% close to the eye. Ice and petals use only a subtle transparency accent. Manual transparency still applies at every distance. These controls do not change collision. Ice currently has no refraction.",MessageType.Info);
            var entries=serializedObject.FindProperty("Materials");
            for(int i=0;i<entries.arraySize;i++)
            {
                var entry=entries.GetArrayElementAtIndex(i);var material=entry.FindPropertyRelative("Material");var id=(MaterialId)material.intValue;
                if(!MaterialCatalog.IsValid(id)||id==MaterialId.Air)continue;
                EditorGUILayout.LabelField(id+" — "+MaterialCatalog.Get(id).Name,EditorStyles.boldLabel);
                var opacity=entry.FindPropertyRelative("Opacity");opacity.floatValue=1-EditorGUILayout.Slider("Transparency",1-opacity.floatValue,0,1);
                EditorGUILayout.Slider(entry.FindPropertyRelative("Transmission"),0,1,new GUIContent("Light transmission"));EditorGUILayout.Space(4);
            }
            if(serializedObject.ApplyModifiedProperties())MaterialOpticsProfile.Apply((MaterialOpticsProfile)target);
        }
    }
}
