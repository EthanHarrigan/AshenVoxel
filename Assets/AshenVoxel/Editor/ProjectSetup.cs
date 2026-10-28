using System;
using System.IO;
using AshenVoxel.Runtime;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace AshenVoxel.Editor
{
    [InitializeOnLoad]
    public static class ProjectSetup
    {
        public const string ScenePath = "Assets/Scenes/ForestArena.unity";
        const string SettingsFolder = "Assets/AshenVoxel/Settings";
        static ProjectSetup() { EditorApplication.delayCall += FirstImport; }
        static void FirstImport()
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            { EditorApplication.delayCall += FirstImport; return; }
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            // The playable exploration entry owns first-import pipeline/scene setup.
            // Explicit legacy repair/build menus remain available below.
            if (File.Exists("Assets/Scenes/Exploration.unity")) return;
            // Only initialize a missing project asset. Existing user changes are preserved.
            if (AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(SettingsFolder+"/AshenURP.asset") == null) ConfigurePipeline();
            if (!File.Exists(ScenePath)) CreateScene();
        }
        [MenuItem("Ashen Voxel/Create or repair demo scene")]
        public static void EnsureDemo()
        {
            ConfigurePipeline();
            if (!File.Exists(ScenePath)) CreateScene();
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath,true) };
            AssetDatabase.SaveAssets();
            Debug.Log("AshenVoxel ready: open " + ScenePath + " and press Play.");
        }
        static void ConfigurePipeline()
        {
            Directory.CreateDirectory(SettingsFolder); AssetDatabase.Refresh();
            string dataPath = SettingsFolder+"/AshenRenderer.asset", pipelinePath=SettingsFolder+"/AshenURP.asset";
            var data=AssetDatabase.LoadAssetAtPath<UniversalRendererData>(dataPath);
            if(data==null)
            {
                data=ScriptableObject.CreateInstance<UniversalRendererData>();
                data.name="Ashen forward renderer";
                // Match URP's own factory: OnEnable reloads renderer resources; this
                // package asset supplies the postprocess shader/texture references.
                data.postProcessData=AssetDatabase.LoadAssetAtPath<PostProcessData>(
                    "Packages/com.unity.render-pipelines.universal/Runtime/Data/PostProcessData.asset");
                if(data.postProcessData==null)
                    throw new InvalidOperationException("URP postprocess resource asset is missing. Allow Package Manager to finish importing URP 17.3 before setup.");
                AssetDatabase.CreateAsset(data,dataPath);
                EditorUtility.SetDirty(data);
            }
            if(data.postProcessData==null)
            {
                data.postProcessData=AssetDatabase.LoadAssetAtPath<PostProcessData>(
                    "Packages/com.unity.render-pipelines.universal/Runtime/Data/PostProcessData.asset");
                if(data.postProcessData==null) throw new InvalidOperationException("URP postprocess resource asset is missing.");
                EditorUtility.SetDirty(data);
            }
            var pipeline=AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(pipelinePath);
            if(pipeline==null)
            {
                pipeline=UniversalRenderPipelineAsset.Create(data);
                pipeline.name="Ashen URP";
                pipeline.msaaSampleCount=4;
                pipeline.renderScale=1;
                pipeline.shadowDistance=65;
                pipeline.mainLightShadowmapResolution=4096;
                pipeline.supportsHDR=true;
                // These URP 17 properties expose internal setters. Use serialized
                // asset fields, as the package's own Inspector does.
                var pipelineSettings=new SerializedObject(pipeline);
                SetPipelineInt(pipelineSettings,"m_MainLightRenderingMode",(int)LightRenderingMode.PerPixel);
                SetPipelineInt(pipelineSettings,"m_AdditionalLightsRenderingMode",(int)LightRenderingMode.PerPixel);
                SetPipelineBool(pipelineSettings,"m_AnyShadowsSupported",true);
                SetPipelineBool(pipelineSettings,"m_MainLightShadowsSupported",true);
                SetPipelineBool(pipelineSettings,"m_AdditionalLightShadowsSupported",false);
                SetPipelineBool(pipelineSettings,"m_SoftShadowsSupported",true);
                pipelineSettings.ApplyModifiedPropertiesWithoutUndo();
                AssetDatabase.CreateAsset(pipeline,pipelinePath);
            }
            // Repair only a missing renderer reference; preserve existing renderer choices.
            var rendererSettings=new SerializedObject(pipeline);
            var renderers=RequiredPipelineProperty(rendererSettings,"m_RendererDataList");
            if(renderers.arraySize==0) renderers.InsertArrayElementAtIndex(0);
            if(renderers.GetArrayElementAtIndex(0).objectReferenceValue==null)
            {
                renderers.GetArrayElementAtIndex(0).objectReferenceValue=data;
                rendererSettings.ApplyModifiedPropertiesWithoutUndo();
            }
            GraphicsSettings.defaultRenderPipeline=pipeline;
            QualitySettings.renderPipeline=pipeline;
            PlayerSettings.colorSpace=ColorSpace.Linear;
            PlayerSettings.companyName="Ashen Voxel";
            PlayerSettings.productName="Ashen Voxel Foundation";
            PlayerSettings.runInBackground=true;
            // Shader.Find-created actor materials require explicit build retention.
            var graphics=AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/GraphicsSettings.asset");
            if(graphics.Length>0)
            {
                var serialized=new SerializedObject(graphics[0]);
                var shaders=serialized.FindProperty("m_AlwaysIncludedShaders");
                if(shaders!=null)
                {
                    foreach(string shaderName in new[]{"Universal Render Pipeline/Lit","Universal Render Pipeline/Unlit","AshenVoxel/WorldSurface"})
                    {
                        var shader=Shader.Find(shaderName); if(shader==null) continue;
                        bool present=false;
                        for(int i=0;i<shaders.arraySize;i++) if(shaders.GetArrayElementAtIndex(i).objectReferenceValue==shader) present=true;
                        if(!present){int i=shaders.arraySize;shaders.InsertArrayElementAtIndex(i);shaders.GetArrayElementAtIndex(i).objectReferenceValue=shader;}
                    }
                    serialized.ApplyModifiedPropertiesWithoutUndo();
                }
            }
            EditorUtility.SetDirty(pipeline); AssetDatabase.SaveAssets();
        }
        static SerializedProperty RequiredPipelineProperty(SerializedObject settings,string name)
        {
            var property=settings.FindProperty(name);
            if(property==null) throw new InvalidOperationException("URP 17 serialized setting is missing: "+name);
            return property;
        }
        static void SetPipelineInt(SerializedObject settings,string name,int value)
        { RequiredPipelineProperty(settings,name).intValue=value; }
        static void SetPipelineBool(SerializedObject settings,string name,bool value)
        { RequiredPipelineProperty(settings,name).boolValue=value; }

        static void CreateScene()
        {
            Directory.CreateDirectory("Assets/Scenes");
            var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            var bootstrap=new GameObject("Ashen Voxel Demo").AddComponent<DemoBootstrap>();
            EditorSceneManager.SaveScene(scene,ScenePath);
            EditorBuildSettings.scenes=new[]{new EditorBuildSettingsScene(ScenePath,true)};
        }
        [MenuItem("Ashen Voxel/Build/Linux x64")]
        public static void BuildLinux() { Build(BuildTarget.StandaloneLinux64,"Builds/Linux/AshenVoxel"); }
        [MenuItem("Ashen Voxel/Build/Windows x64")]
        public static void BuildWindows() { Build(BuildTarget.StandaloneWindows64,"Builds/Windows/AshenVoxel.exe"); }
        static void Build(BuildTarget target,string path)
        {
            EnsureDemo();
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            BuildReport report=BuildPipeline.BuildPlayer(new BuildPlayerOptions {
                scenes=new[]{ScenePath},locationPathName=path,target=target,options=BuildOptions.Development
            });
            if(report.summary.result!=BuildResult.Succeeded) throw new Exception("Build failed: "+report.summary.result);
        }
    }
}
