using System;
using System.IO;
using System.Security.Cryptography;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace AshenVoxel.Exploration.Editor
{
    /// <summary>Explicit, bounded native check. Never enters Play or loads world data.</summary>
    public static class AtmosphereGraphicsCheck
    {
        const string Folder="Logs/AtmosphereGraphics";
        [MenuItem("Ashen Voxel/Validation/Atmosphere shaders and preset captures")]
        public static void Run()
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Stop Play before running the bounded atmosphere fixture.");
            Directory.CreateDirectory(Folder);
            string[] shaders={"AshenVoxel/ExplorationSky","AshenVoxel/WorldSurface","AshenVoxel/GroundSurface",
                "AshenVoxel/ForestImpostor","AshenVoxel/ForestHorizonGpu","AshenVoxel/VisualWater"};
            foreach(string name in shaders)
            {
                Shader shader=Shader.Find(name);
                if(shader==null)throw new InvalidOperationException("Missing shader: "+name);
                var material=new Material(shader);
                try{for(int pass=0;pass<material.passCount;pass++)ShaderUtil.CompilePass(material,pass,true);}
                finally{UnityEngine.Object.DestroyImmediate(material);}
                if(ShaderUtil.ShaderHasError(shader))throw new InvalidOperationException("Shader error: "+name);
            }
            CheckOptions();CapturePresets();
            string[] paths={"Assets/AshenVoxel/Exploration/Resources/ExplorationSky.shader",
                "Assets/AshenVoxel/Rendering/AshenAtmosphere.hlsl","Assets/AshenVoxel/Exploration/ExplorationAtmosphere.cs",
                "Assets/AshenVoxel/Exploration/ExplorationEnvironmentRuntime.cs","Assets/AshenVoxel/Exploration/ExplorationVisualQuality.cs"};
            using(var hash=SHA256.Create())using(var writer=new StreamWriter(Folder+"/source.sha256"))
                foreach(string path in paths)writer.WriteLine(BitConverter.ToString(hash.ComputeHash(File.ReadAllBytes(path))).Replace("-","").ToLowerInvariant()+"  "+path);
            File.WriteAllText(Folder+"/completed.txt","PASS native shader passes, settings roundtrip, global cleanup and 3 sky-only captures. "+
                DateTime.UtcNow.ToString("o")+" / "+Application.unityVersion+" / "+SystemInfo.graphicsDeviceName+
                ". This does not establish full-scene visual acceptance, fog continuity or FPS.");
            Debug.Log("Atmosphere fixture complete: "+Folder);
        }
        public static void RunBatch()
        {
            EditorApplication.delayCall+=()=>
            {
                try{Run();EditorApplication.Exit(0);}
                catch(Exception e){Directory.CreateDirectory(Folder);File.WriteAllText(Folder+"/failure.txt",e.ToString());Debug.LogException(e);EditorApplication.Exit(1);}
            };
        }
        static void CheckOptions()
        {
            var source=ScriptableObject.CreateInstance<ExplorationSettings>();
            var target=ScriptableObject.CreateInstance<ExplorationSettings>();
            try
            {
                foreach(ExplorationAtmospherePreset preset in Enum.GetValues(typeof(ExplorationAtmospherePreset)))
                {
                    ExplorationAtmosphere.ApplyPreset(source,preset);
                    var snapshot=ExplorationEnvironmentOptions.Capture(source);
                    JsonUtility.FromJson<ExplorationEnvironmentOptions>(JsonUtility.ToJson(snapshot)).Apply(target);
                    Require(target.SunElevation==source.SunElevation&&target.Atmosphere.CloudCoverage==source.Atmosphere.CloudCoverage,"Preset options did not roundtrip.");
                    target.Atmosphere.CloudCoverage=0;
                    Require(source.Atmosphere.CloudCoverage>0&&snapshot.atmosphere.CloudCoverage>0,"Settings clone shares mutable state.");
                }
                var future=ExplorationEnvironmentOptions.Capture(source);future.version=2;
                target.SunAzimuth=123;future.Apply(target);Require(target.SunAzimuth==123,"Unknown option version was applied.");
                target.Atmosphere.SkyExposure=float.NaN;target.Atmosphere.FogHeightFalloff=float.PositiveInfinity;
                target.SunElevation=-100;ExplorationAtmosphere.SanitizeLighting(target);
                Require(target.Atmosphere.SkyExposure==1&&target.Atmosphere.FogHeightFalloff==.00035f&&target.SunElevation==-12,"Unsafe option values were not sanitized.");
            }
            finally{UnityEngine.Object.DestroyImmediate(source);UnityEngine.Object.DestroyImmediate(target);}
        }
        static void Require(bool ok,string error){if(!ok)throw new InvalidOperationException(error);}
        static void CapturePresets()
        {
            var oldDefault=GraphicsSettings.defaultRenderPipeline;var oldQuality=QualitySettings.renderPipeline;
            Material oldSky=RenderSettings.skybox;RenderTexture oldActive=RenderTexture.active;
            Light oldSun=RenderSettings.sun;Color oldFog=RenderSettings.fogColor;
            float oldFogEnabled=Shader.GetGlobalFloat("_AshenAtmosphereEnabled");
            var source=AssetDatabase.LoadAssetAtPath<RenderPipelineAsset>("Assets/AshenVoxel/Settings/ExplorationURP.asset");
            Require(source!=null,"Exploration URP asset missing.");
            var pipeline=UnityEngine.Object.Instantiate(source);
            var settings=ScriptableObject.CreateInstance<ExplorationSettings>();
            var root=new GameObject("Atmosphere fixture"){hideFlags=HideFlags.HideAndDontSave};
            var sky=new Material(Shader.Find("AshenVoxel/ExplorationSky")){hideFlags=HideFlags.HideAndDontSave};
            var rt=new RenderTexture(960,540,24,RenderTextureFormat.ARGBHalf);
            var pixels=new Texture2D(960,540,TextureFormat.RGB24,false);
            var environment=new ExplorationEnvironmentRuntime();
            try
            {
                GraphicsSettings.defaultRenderPipeline=pipeline;QualitySettings.renderPipeline=pipeline;
                var camera=root.AddComponent<Camera>();camera.enabled=false;camera.cullingMask=0;
                camera.clearFlags=CameraClearFlags.Skybox;camera.allowHDR=true;camera.fieldOfView=75;camera.targetTexture=rt;
                var sunObject=new GameObject("Fixture sun");sunObject.transform.SetParent(root.transform,false);
                var sun=sunObject.AddComponent<Light>();sun.type=LightType.Directional;
                RenderSettings.sun=sun;RenderSettings.skybox=sky;
                var quality=root.AddComponent<ExplorationVisualQuality>();quality.Initialize(camera,pipeline);
                foreach(ExplorationAtmospherePreset preset in Enum.GetValues(typeof(ExplorationAtmospherePreset)))
                {
                    ExplorationAtmosphere.ApplyPreset(settings,preset);settings.Atmosphere.CloudSpeed=0;
                    environment.Apply(settings,sky,sun);environment.SetOriginY(0);
                    quality.Apply(settings.Atmosphere);camera.transform.rotation=Quaternion.Euler(-10,settings.SunAzimuth+180,0);
                    var horizon=sky.GetVector("_HorizonColor")*settings.Atmosphere.SkyExposure;
                    var fog=Shader.GetGlobalVector("_AshenFogColor");
                    Require((new Vector3(horizon.x-fog.x,horizon.y-fog.y,horizon.z-fog.z)).sqrMagnitude<1e-10f,"Fog and sky horizon uploads disagree.");
                    camera.Render();RenderTexture.active=rt;pixels.ReadPixels(new Rect(0,0,960,540),0,0);pixels.Apply();
                    File.WriteAllBytes(Folder+"/"+preset+".png",pixels.EncodeToPNG());
                }
                quality.Apply(new ExplorationAtmosphere{PostProcessing=false});
                Require(camera.GetComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>().renderPostProcessing,"Disabling grading disabled the AA camera stage.");
            }
            finally
            {
                environment.Dispose();RenderSettings.skybox=oldSky;RenderTexture.active=oldActive;
                GraphicsSettings.defaultRenderPipeline=oldDefault;QualitySettings.renderPipeline=oldQuality;
                UnityEngine.Object.DestroyImmediate(root);UnityEngine.Object.DestroyImmediate(sky);UnityEngine.Object.DestroyImmediate(settings);
                rt.Release();UnityEngine.Object.DestroyImmediate(rt);UnityEngine.Object.DestroyImmediate(pixels);UnityEngine.Object.DestroyImmediate(pipeline);
            }
            Require(RenderSettings.sun==oldSun&&RenderSettings.fogColor==oldFog&&Shader.GetGlobalFloat("_AshenAtmosphereEnabled")==oldFogEnabled,"Environment state was not restored.");
        }
    }
}
