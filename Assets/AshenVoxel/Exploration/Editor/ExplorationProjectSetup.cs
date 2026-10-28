using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Globalization;
using System.Text;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace AshenVoxel.Exploration.Editor
{
    /// <summary>One-time project entry and explicit repair/build commands. It
    /// never overrides a user's later scene choice or saves a dirty scene.</summary>
    [InitializeOnLoad]
    public static class ExplorationProjectSetup
    {
        public const string ScenePath="Assets/Scenes/Exploration.unity";
        public const string SettingsPath="Assets/AshenVoxel/Settings/ExplorationSettings.asset";
        public const string PipelinePath="Assets/AshenVoxel/Settings/ExplorationURP.asset";
        public const string RendererPath="Assets/AshenVoxel/Settings/ExplorationRenderer.asset";
        const string SampleFolder="Assets/AshenVoxel/Exploration/Samples/";
        const string PrototypePath="Assets/AshenVoxel/Exploration/Prototypes/ExplorationPrototypes.bytes";
        static string FirstOpenKey=>"AshenVoxel.Exploration.FirstOpen.v1."+Hash128.Compute(Application.dataPath);

        static ExplorationProjectSetup(){EditorApplication.delayCall+=FirstOpen;}
        static void FirstOpen()
        {
            if(EditorApplication.isCompiling||EditorApplication.isUpdating)
            {EditorApplication.delayCall+=FirstOpen;return;}
            if(EditorApplication.isPlayingOrWillChangePlaymode||EditorPrefs.GetBool(FirstOpenKey,false))return;
            try
            {
                Configure(false);
                // Mark once even when a user has already edited another scene.
                // The menu remains available; no prompt or destructive replacement.
                EditorPrefs.SetBool(FirstOpenKey,true);
                if(AnySceneDirty())
                {Debug.Log("Exploration is ready. Your modified scene was preserved; use Ashen Voxel > Exploration > Open playable world.");return;}
                EditorSceneManager.OpenScene(ScenePath);
                Debug.Log("Exploration ready: press Play to walk or fly through the bundled learned world. F1 opens settings.");
            }
            catch(Exception exception)
            {Debug.LogError("Exploration setup could not complete: "+exception.Message+". Let packages finish importing, then use Ashen Voxel > Exploration > Repair project setup.");}
        }
        static bool AnySceneDirty()
        {
            for(int i=0;i<UnityEngine.SceneManagement.SceneManager.sceneCount;i++)
                if(UnityEngine.SceneManagement.SceneManager.GetSceneAt(i).isDirty)return true;
            return false;
        }

        [MenuItem("Ashen Voxel/Exploration/Open playable world")]
        public static void Open()
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode){Debug.Log("Leave Play mode before changing scenes.");return;}
            if(!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())return;
            Configure(false);EditorSceneManager.OpenScene(ScenePath);
        }
        [MenuItem("Ashen Voxel/Exploration/Select world and movement settings")]
        public static void SelectSettings()
        {Selection.activeObject=AssetDatabase.LoadAssetAtPath<ExplorationSettings>(SettingsPath);EditorGUIUtility.PingObject(Selection.activeObject);}
        [MenuItem("Ashen Voxel/Exploration/Repair project setup")]
        public static void Repair()
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode){Debug.Log("Leave Play mode before repairing project assets.");return;}
            Configure(true);Debug.Log("Exploration settings, renderer, shaders and build entry are ready. Open the playable world and press Play.");
        }
        [MenuItem("Ashen Voxel/Exploration/Prepare another Azgaar map")]
        public static void PrepareAzgaar()
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode){Debug.Log("Leave Play mode before preparing a different world.");return;}
            string project=Path.GetDirectoryName(Application.dataPath);
            if(Application.platform!=RuntimePlatform.WindowsEditor)
            {EditorUtility.OpenWithDefaultApp(Path.Combine(project,"Tools/ExplorationBake/README.md"));return;}
            string source=EditorUtility.OpenFilePanel("Select an Azgaar Full JSON export","","json");
            if(string.IsNullOrEmpty(source))return;
            var settings=AssetDatabase.LoadAssetAtPath<ExplorationSettings>(SettingsPath);
            var start=new System.Diagnostics.ProcessStartInfo("powershell.exe")
            // Shell launch gives the console application its own visible window
            // instead of inheriting the Editor's hidden console.
            {UseShellExecute=true,WindowStyle=System.Diagnostics.ProcessWindowStyle.Normal,WorkingDirectory=project};
            string[] arguments={"-NoProfile","-ExecutionPolicy","Bypass","-NoExit","-File",
                Path.Combine(project,"Tools/ExplorationBake/Start-InfiniteDiffusion.ps1"),"-AzgaarJson",source,
                "-WorldWidthMeters",(settings!=null?settings.WorldWidthMeters:299520).ToString("R",CultureInfo.InvariantCulture),
                "-WorldDepthMeters",(settings!=null?settings.WorldDepthMeters:149760).ToString("R",CultureInfo.InvariantCulture),
                "-ModelMetersPerSample","5","-Threads","4","-FineSide","1024","-Port","8787"};
            // Quote native arguments explicitly for compatibility across Editor
            // runtimes; no command shell evaluates the selected map path.
            start.Arguments=string.Join(" ",Array.ConvertAll(arguments,QuoteWindowsArgument));
            try
            {
                System.Diagnostics.Process.Start(start);
                Debug.Log("World preparation launch requested in a separate PowerShell window. Check that window for progress or errors; launch alone does not confirm generation. Existing bundled data is backed up before baking. After successful completion refresh Assets and press Play again.");
            }
            catch(Exception exception){Debug.LogError("Could not open world preparation: "+exception.Message+". See Tools/ExplorationBake/README.md.");}
        }
        internal static string QuoteWindowsArgument(string value)
        {
            if(value==null)throw new ArgumentNullException(nameof(value));
            var quoted=new StringBuilder("\"");int slashes=0;
            foreach(char character in value)
            {
                if(character=='\\'){slashes++;continue;}
                // A quote consumes one slash; preceding slashes must be doubled.
                quoted.Append('\\',character=='\"'?slashes*2+1:slashes);
                quoted.Append(character);slashes=0;
            }
            // Slashes immediately before the closing quote must also be doubled.
            quoted.Append('\\',slashes*2);quoted.Append('\"');return quoted.ToString();
        }

        /// <summary>New assets get the baseline. Existing artist choices are kept
        /// unless the explicit repair command requests baseline pipeline settings.</summary>
        public static void Configure(bool resetPipeline)
        {
            if(!File.Exists(ScenePath))throw new FileNotFoundException("The bundled exploration scene is missing.",ScenePath);
            ExplorationSettings settings=AssetDatabase.LoadAssetAtPath<ExplorationSettings>(SettingsPath);
            if(settings==null)throw new InvalidOperationException("The bundled ExplorationSettings asset is missing or its script has not imported.");
            WireMissingSamples(settings);
            UniversalRendererData renderer=AssetDatabase.LoadAssetAtPath<UniversalRendererData>(RendererPath);
            bool newRenderer=renderer==null;
            if(newRenderer)
            {
                renderer=ScriptableObject.CreateInstance<UniversalRendererData>();renderer.name="Exploration forward renderer";
                renderer.postProcessData=AssetDatabase.LoadAssetAtPath<PostProcessData>("Packages/com.unity.render-pipelines.universal/Runtime/Data/PostProcessData.asset");
                AssetDatabase.CreateAsset(renderer,RendererPath);
            }
            if(renderer.postProcessData==null)
                renderer.postProcessData=AssetDatabase.LoadAssetAtPath<PostProcessData>("Packages/com.unity.render-pipelines.universal/Runtime/Data/PostProcessData.asset");
            if(renderer.postProcessData==null)
                Debug.LogWarning("URP post-process resources are not yet imported. Ordinary world rendering remains available; repair setup after package import to enable optional effects.");
            if(newRenderer||resetPipeline)
            {
                var serialized=new SerializedObject(renderer);
                SetInt(serialized,"m_RenderingMode",0); // URP Forward, not Deferred/Forward+.
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }
            ConfigureAmbientOcclusion(renderer,settings.EnableAmbientOcclusion,newRenderer||resetPipeline);
            UniversalRenderPipelineAsset pipeline=AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(PipelinePath);
            bool newPipeline=pipeline==null;
            if(newPipeline)
            {pipeline=UniversalRenderPipelineAsset.Create(renderer);pipeline.name="Exploration URP";AssetDatabase.CreateAsset(pipeline,PipelinePath);}
            if(newPipeline||resetPipeline)
            {
                pipeline.msaaSampleCount=2;pipeline.renderScale=1;pipeline.supportsHDR=true;
                pipeline.supportsCameraDepthTexture=true;pipeline.supportsCameraOpaqueTexture=false;
                pipeline.shadowDistance=settings.ShadowDistanceMeters;pipeline.mainLightShadowmapResolution=4096;
                pipeline.shadowCascadeCount=4;pipeline.cascade4Split=new Vector3(.08f,.23f,.55f);pipeline.cascadeBorder=.12f;
                var serialized=new SerializedObject(pipeline);
                SetInt(serialized,"m_MainLightRenderingMode",(int)LightRenderingMode.PerPixel);
                SetInt(serialized,"m_AdditionalLightsRenderingMode",(int)LightRenderingMode.PerPixel);
                SetBool(serialized,"m_AnyShadowsSupported",true);SetBool(serialized,"m_MainLightShadowsSupported",true);
                SetBool(serialized,"m_AdditionalLightShadowsSupported",false);SetBool(serialized,"m_SoftShadowsSupported",true);
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }
            var rendererSettings=new SerializedObject(pipeline);
            SerializedProperty renderers=rendererSettings.FindProperty("m_RendererDataList");
            if(renderers==null)throw new InvalidOperationException("URP17 renderer list is unavailable.");
            if(renderers.arraySize==0)renderers.InsertArrayElementAtIndex(0);
            if(renderers.GetArrayElementAtIndex(0).objectReferenceValue==null||resetPipeline)
            {renderers.GetArrayElementAtIndex(0).objectReferenceValue=renderer;rendererSettings.ApplyModifiedPropertiesWithoutUndo();}
            GraphicsSettings.defaultRenderPipeline=pipeline;QualitySettings.renderPipeline=pipeline;
            PlayerSettings.colorSpace=ColorSpace.Linear;PlayerSettings.runInBackground=true;
            PlayerSettings.companyName="Ashen Voxel";PlayerSettings.productName="Ashen Voxel Exploration";
            EnsureGlobalSettings();RetainShaders();MakeFirstBuildEntry();
            EditorUtility.SetDirty(renderer);EditorUtility.SetDirty(pipeline);AssetDatabase.SaveAssets();
        }
        static void WireMissingSamples(ExplorationSettings settings)
        {
            // Never replace an assigned imported world or artist prototype pack.
            var serialized=new SerializedObject(settings);
            Wire(serialized,"MacroWorld",SampleFolder+"ExplorationMacro.bytes");
            Wire(serialized,"MacroClimate",SampleFolder+"ExplorationMacro.climate.bytes");
            Wire(serialized,"FineWorld",SampleFolder+"ExplorationFine.bytes");
            Wire(serialized,"FineClimate",SampleFolder+"ExplorationFine.climate.bytes");
            Wire(serialized,"SourceBiomes",SampleFolder+"ExplorationBiomes.bytes");
            Wire(serialized,"WorldMetadata",SampleFolder+"exploration-world.json");
            Wire(serialized,"PrototypePack",PrototypePath);
            serialized.ApplyModifiedPropertiesWithoutUndo();EditorUtility.SetDirty(settings);
        }
        static void Wire(SerializedObject serialized,string name,string path)
        {
            SerializedProperty property=serialized.FindProperty(name);
            if(property!=null&&property.objectReferenceValue==null)
                property.objectReferenceValue=AssetDatabase.LoadAssetAtPath<TextAsset>(path);
        }
        static void ConfigureAmbientOcclusion(UniversalRendererData renderer,bool enabled,bool reset)
        {
            const string featureName="UnityEngine.Rendering.Universal.ScreenSpaceAmbientOcclusion";
            ScriptableRendererFeature feature=null;
            foreach(ScriptableRendererFeature item in renderer.rendererFeatures)
                if(item!=null&&item.GetType().FullName==featureName){feature=item;break;}
            if(feature==null)
            {
                Type type=typeof(UniversalRendererData).Assembly.GetType(featureName,false);
                if(type==null||!typeof(ScriptableRendererFeature).IsAssignableFrom(type))
                {Debug.LogWarning("Optional SSAO is unavailable in this URP package; the world can still render without it.");return;}
                feature=ScriptableObject.CreateInstance(type) as ScriptableRendererFeature;
                if(feature==null){Debug.LogWarning("Optional SSAO feature could not be created.");return;}
                feature.name="Exploration contact occlusion";
                AssetDatabase.AddObjectToAsset(feature,renderer);renderer.rendererFeatures.Add(feature);reset=true;
            }
            if(reset)
            {
                var serialized=new SerializedObject(feature);
                if(serialized.FindProperty("m_Settings.Source")==null)
                {feature.SetActive(false);Debug.LogWarning("SSAO serialized settings differ from URP17.3; optional AO was disabled.");return;}
                SetInt(serialized,"m_Settings.AOMethod",1); // Interleaved gradient avoids extra blue-noise texture resources.
                SetInt(serialized,"m_Settings.Source",1); // Actual mesh DepthNormals, including ground.
                SetBool(serialized,"m_Settings.Downsample",true);SetBool(serialized,"m_Settings.AfterOpaque",false);
                SetFloat(serialized,"m_Settings.Intensity",.7f);SetFloat(serialized,"m_Settings.Radius",.35f);
                SetFloat(serialized,"m_Settings.DirectLightingStrength",.12f);SetFloat(serialized,"m_Settings.Falloff",100);
                SetInt(serialized,"m_Settings.Samples",1);SetInt(serialized,"m_Settings.BlurQuality",1);
                serialized.ApplyModifiedPropertiesWithoutUndo();EditorUtility.SetDirty(feature);
            }
            feature.SetActive(enabled);EditorUtility.SetDirty(feature);
            renderer.SetDirty();
        }
        static void EnsureGlobalSettings()
        {
            // Unity's own Ensure populates renderer resources and default volumes.
            // Use reflection because this package factory is internal in17.3.
            Type type=typeof(UniversalRenderPipelineAsset).Assembly.GetType("UnityEngine.Rendering.Universal.UniversalRenderPipelineGlobalSettings",false);
            MethodInfo ensure=type?.GetMethod("Ensure",BindingFlags.Static|BindingFlags.NonPublic,null,new[]{typeof(bool)},null);
            if(ensure==null){Debug.LogWarning("URP global-settings factory is unavailable; verify optional SSAO resources in Graphics Settings.");return;}
            try{ensure.Invoke(null,new object[]{true});}
            catch(Exception exception){Debug.LogWarning("Optional URP resource setup requires review: "+exception.GetBaseException().Message);}
        }
        static void RetainShaders()
        {
            UnityEngine.Object[] graphics=AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/GraphicsSettings.asset");
            if(graphics.Length==0)return;
            var serialized=new SerializedObject(graphics[0]);SerializedProperty list=serialized.FindProperty("m_AlwaysIncludedShaders");
            if(list==null)return;
            foreach(string name in new[]{"AshenVoxel/WorldSurface","AshenVoxel/GroundSurface","AshenVoxel/VisualWater","Skybox/Procedural",
                "Universal Render Pipeline/Lit","Universal Render Pipeline/Unlit","Hidden/Universal Render Pipeline/ScreenSpaceAmbientOcclusion"})
            {
                Shader shader=Shader.Find(name);if(shader==null)continue;
                bool present=false;for(int i=0;i<list.arraySize;i++)if(list.GetArrayElementAtIndex(i).objectReferenceValue==shader){present=true;break;}
                if(!present){int index=list.arraySize;list.InsertArrayElementAtIndex(index);list.GetArrayElementAtIndex(index).objectReferenceValue=shader;}
            }
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }
        static void MakeFirstBuildEntry()
        {
            var scenes=new List<EditorBuildSettingsScene>{new EditorBuildSettingsScene(ScenePath,true)};
            foreach(EditorBuildSettingsScene scene in EditorBuildSettings.scenes)if(scene.path!=ScenePath)scenes.Add(scene);
            EditorBuildSettings.scenes=scenes.ToArray();
        }
        static void SetInt(SerializedObject obj,string name,int value)
        {SerializedProperty p=obj.FindProperty(name);if(p!=null)p.intValue=value;}
        static void SetBool(SerializedObject obj,string name,bool value)
        {SerializedProperty p=obj.FindProperty(name);if(p!=null)p.boolValue=value;}
        static void SetFloat(SerializedObject obj,string name,float value)
        {SerializedProperty p=obj.FindProperty(name);if(p!=null)p.floatValue=value;}

        [MenuItem("Ashen Voxel/Exploration/Build Windows x64")]
        public static void BuildWindows()=>Build(BuildTarget.StandaloneWindows64,"Builds/Exploration-Windows/AshenVoxel.exe");
        [MenuItem("Ashen Voxel/Exploration/Build Linux x64")]
        public static void BuildLinux()=>Build(BuildTarget.StandaloneLinux64,"Builds/Exploration-Linux/AshenVoxel");
        static void Build(BuildTarget target,string output)
        {
            Configure(false);Directory.CreateDirectory(Path.GetDirectoryName(output));
            BuildReport report=BuildPipeline.BuildPlayer(new BuildPlayerOptions{scenes=new[]{ScenePath},locationPathName=output,target=target,options=BuildOptions.Development});
            if(report.summary.result!=BuildResult.Succeeded)throw new InvalidOperationException("Exploration build failed: "+report.summary.result);
        }
    }

    [CustomEditor(typeof(ExplorationSettings))]
    public sealed class ExplorationSettingsEditor:UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            EditorGUILayout.HelpBox("Press Play in Exploration for movement, flight and the learned world. Fog and movement can also be adjusted with F1. Use the button below after changing the renderer's ambient-occlusion option.",MessageType.Info);
            using(new EditorGUI.DisabledScope(EditorApplication.isPlayingOrWillChangePlaymode))
                if(GUILayout.Button("Apply renderer options"))ExplorationProjectSetup.Configure(false);
        }
    }
}
