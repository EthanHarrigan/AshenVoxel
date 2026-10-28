using System;
using System.IO;
using System.Collections.Generic;
using System.Security.Cryptography;
using AshenVoxel.WorldScale;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace AshenVoxel.Exploration.Editor
{
    /// <summary>Explicit bounded native check using bundled terrain only. Never enters Play.</summary>
    public static class AtmosphereGraphicsCheck
    {
        static string folder;
        static string Folder=>folder??(folder=Path.Combine("Logs/AtmosphereGraphics",DateTime.UtcNow.ToString("yyyyMMdd-HHmmss")));
        [MenuItem("Ashen Voxel/Validation/Atmosphere shaders and preset captures")]
        public static void Run()
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Stop Play before running the bounded atmosphere fixture.");
            folder=Path.Combine("Logs/AtmosphereGraphics",DateTime.UtcNow.ToString("yyyyMMdd-HHmmss"));
            Directory.CreateDirectory(Folder);
            string[] shaders={"AshenVoxel/ExplorationSky","AshenVoxel/ExplorationCloudComposite","AshenVoxel/WorldSurface","AshenVoxel/GroundSurface",
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
            string[] paths={"Assets/AshenVoxel/Exploration/Resources/ExplorationSky.shader",
                "Assets/AshenVoxel/Exploration/Resources/ExplorationCloudComposite.shader",
                "Assets/AshenVoxel/Exploration/Resources/ExplorationCloudVolume.hlsl",
                "Assets/AshenVoxel/Exploration/Resources/ExplorationCloudNoise.bytes",
                "Assets/AshenVoxel/Exploration/ExplorationCloudNoise.cs","Assets/AshenVoxel/Exploration/ExplorationCloudVolume.cs",
                "Assets/AshenVoxel/Settings/ExplorationRenderer.asset",
                "Assets/AshenVoxel/Rendering/AshenAtmosphere.hlsl","Assets/AshenVoxel/Exploration/ExplorationAtmosphere.cs",
                "Assets/AshenVoxel/Exploration/ExplorationEnvironmentRuntime.cs","Assets/AshenVoxel/Exploration/ExplorationVisualQuality.cs",
                "Assets/AshenVoxel/Exploration/Editor/AtmosphereGraphicsCheck.cs","Assets/AshenVoxel/Settings/ExplorationURP.asset",
                "Assets/AshenVoxel/Settings/ExplorationSettings.asset","ProjectSettings/ProjectSettings.asset"};
            using(var hash=SHA256.Create())using(var writer=new StreamWriter(Folder+"/source.sha256"))
                foreach(string path in paths)writer.WriteLine(BitConverter.ToString(hash.ComputeHash(File.ReadAllBytes(path))).Replace("-","").ToLowerInvariant()+"  "+path);
            CheckColorUploads();CheckFogShader();CheckOptions();CapturePresets();
            File.WriteAllText(Folder+"/completed.txt","PASS native shader passes, GPU color/encoding, fog, opaque-depth and post-exposure probes, settings roundtrip, cloud resource reuse/disposal, global cleanup, 3 sky-only, 3 bounded source-terrain and 6 above-cloud captures. "+
                DateTime.UtcNow.ToString("o")+" / "+Application.unityVersion+" / "+SystemInfo.graphicsDeviceName+
                " / "+SystemInfo.graphicsDeviceType+" / "+QualitySettings.activeColorSpace+
                ". 1600x900; sky FOV55/pitch20 up/sunward, terrain FOV55/pitch7 up/saved yaw, above-cloud FOV65/pitch8 down/yaw90 from450m above layer, close-cloud FOV65/pitch4 down/yaw90 from150m above layer. Stationary clouds. Standard URP camera request updates grading; HDR camera target, post then sRGB encode; native SMAA. No Play. This does not establish full-scene visual acceptance or FPS.");
            Debug.Log("Atmosphere fixture complete: "+Folder);
        }
        public static void RunBatch()
        {
            EditorApplication.delayCall+=()=>
            {
                try
                {
                    Run();
                    Type.GetType("AshenVoxel.Rendering.Editor.MeshRegressionChecks, AshenVoxel.Rendering.Editor",true).GetMethod("Run").Invoke(null,null);
                    File.WriteAllText(Folder+"/mesh-completed.txt","PASS native mesh regression and independent mesh oracle. "+DateTime.UtcNow.ToString("o"));
                    EditorApplication.Exit(0);
                }
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
                var legacy=ExplorationEnvironmentOptions.Capture(source);legacy.version=1;
                legacy.atmosphere.CloudBaseHeight=0;legacy.atmosphere.CloudNoiseWorldSize=0;
                legacy.Apply(target);Require(target.Atmosphere.CloudBaseHeight==2200&&target.Atmosphere.CloudNoiseWorldSize==6400,"Version 1 volume defaults did not migrate.");
                var future=ExplorationEnvironmentOptions.Capture(source);future.version=3;
                target.SunAzimuth=123;future.Apply(target);Require(target.SunAzimuth==123,"Unknown option version was applied.");
                target.Atmosphere.SkyExposure=float.NaN;target.Atmosphere.FogHeightFalloff=float.PositiveInfinity;
                target.SunElevation=-100;ExplorationAtmosphere.SanitizeLighting(target);
                Require(target.Atmosphere.SkyExposure==1&&target.Atmosphere.FogHeightFalloff==.00035f&&target.SunElevation==-12,"Unsafe option values were not sanitized.");
            }
            finally{UnityEngine.Object.DestroyImmediate(source);UnityEngine.Object.DestroyImmediate(target);}
        }
        static void Require(bool ok,string error){if(!ok)throw new InvalidOperationException(error);}
        static void RenderNative(Camera camera)
        {
            // The standard request updates URP's Volume framework before drawing.
            // SingleCameraRequest bypasses that update, leaving stale grading in
            // repeated Edit-mode captures within one editor frame.
            var request=new RenderPipeline.StandardRequest{destination=camera.targetTexture};
            RenderPipeline.SubmitRenderRequest(camera,request);
        }
        static void CheckColorUploads()
        {
            // Read the GPU uniform, independently of post processing and PNG encoding.
            var shader=ShaderUtil.CreateShaderAsset(@"Shader ""Hidden/AshenAtmosphereColorProbe"" {
                Properties { _Plain(""Plain"",Color)=(1,1,1,1) [HDR] _Hdr(""HDR"",Color)=(1,1,1,1)
                    _Vector(""Vector"",Vector)=(1,1,1,1) _Mode(""Mode"",Float)=0 }
                SubShader { Pass { ZTest Always ZWrite Off Cull Off HLSLPROGRAM
                #pragma vertex vert_img
                #pragma fragment frag
                #include ""UnityCG.cginc""
                float4 _Plain,_Hdr,_Vector; float _Mode;
                float4 frag(v2f_img i):SV_Target {return _Mode<.5?_Plain:(_Mode<1.5?_Hdr:_Vector);}
                ENDHLSL } } }");
            var material=new Material(shader);
            var rt=new RenderTexture(1,1,0,RenderTextureFormat.ARGBFloat,RenderTextureReadWrite.Linear);
            var pixels=new Texture2D(1,1,TextureFormat.RGBAFloat,false,true);
            var display=new RenderTexture(1,1,0,RenderTextureFormat.ARGB32,RenderTextureReadWrite.sRGB);
            var displayPixels=new Texture2D(1,1,TextureFormat.RGB24,false);
            RenderTexture oldActive=RenderTexture.active;bool oldSrgb=GL.sRGBWrite;
            try
            {
                ShaderUtil.CompilePass(material,0,true);Require(!ShaderUtil.ShaderHasError(shader),"Color probe shader failed.");
                var authored=new Color(.42f,.53f,.65f,1);var linear=authored.linear;
                using(var writer=new StreamWriter(Folder+"/color-upload.csv"))
                {
                    writer.WriteLine("setter,property,cpuR,cpuG,cpuB,gpuR,gpuG,gpuB");
                    foreach(bool vector in new[]{false,true})
                    {
                        string[] names={"_Plain","_Hdr","_Vector"};
                        for(int i=0;i<names.Length;i++)
                        {
                            if(vector)material.SetVector(names[i],linear);else material.SetColor(names[i],authored);
                            material.SetFloat("_Mode",i);GL.sRGBWrite=false;
                            Graphics.Blit(Texture2D.whiteTexture,rt,material);RenderTexture.active=rt;
                            pixels.ReadPixels(new Rect(0,0,1,1),0,0);pixels.Apply();
                            var gpu=pixels.GetPixel(0,0);var cpu=material.GetVector(names[i]);
                            writer.WriteLine(FormattableString.Invariant($"{(vector?"SetVector-linear":"SetColor-sRGB")},{names[i]},{cpu.x:R},{cpu.y:R},{cpu.z:R},{gpu.r:R},{gpu.g:R},{gpu.b:R}"));
                            if((!vector&&i==0)||(vector&&i>0))
                            {
                                Color expected=QualitySettings.activeColorSpace==ColorSpace.Linear?linear:authored;
                                // The vector case explicitly supplied linear; use raw values in gamma projects.
                                if(vector)expected=linear;
                                Require(Mathf.Abs(gpu.r-expected.r)+Mathf.Abs(gpu.g-expected.g)+Mathf.Abs(gpu.b-expected.b)<1e-5f,"Unexpected native color upload conversion.");
                            }
                        }
                    }
                    GL.sRGBWrite=QualitySettings.activeColorSpace==ColorSpace.Linear;
                    Graphics.Blit(rt,display);RenderTexture.active=display;
                    displayPixels.ReadPixels(new Rect(0,0,1,1),0,0);displayPixels.Apply();
                    var encoded=displayPixels.GetPixel(0,0);
                    Color expectedDisplay=QualitySettings.activeColorSpace==ColorSpace.Linear?authored:linear;
                    Require(Mathf.Abs(encoded.r-expectedDisplay.r)+Mathf.Abs(encoded.g-expectedDisplay.g)+Mathf.Abs(encoded.b-expectedDisplay.b)<.015f,"HDR-to-PNG display encoding failed.");
                    writer.WriteLine(FormattableString.Invariant($"Display-encode,sRGB,{expectedDisplay.r:R},{expectedDisplay.g:R},{expectedDisplay.b:R},{encoded.r:R},{encoded.g:R},{encoded.b:R}"));
                }
            }
            finally
            {
                RenderTexture.active=oldActive;GL.sRGBWrite=oldSrgb;rt.Release();
                UnityEngine.Object.DestroyImmediate(pixels);UnityEngine.Object.DestroyImmediate(rt);
                display.Release();UnityEngine.Object.DestroyImmediate(display);UnityEngine.Object.DestroyImmediate(displayPixels);
                UnityEngine.Object.DestroyImmediate(material);UnityEngine.Object.DestroyImmediate(shader);
            }
        }
        static void CheckFogShader()
        {
            var shader=ShaderUtil.CreateShaderAsset(@"Shader ""Hidden/AshenAtmosphereFogProbe"" {
                Properties { _ProbeCamera(""Camera"",Vector)=(0,0,0,0) _ProbePoint(""Point"",Vector)=(0,0,1,0) }
                SubShader { Pass { ZTest Always ZWrite Off Cull Off HLSLPROGRAM
                #pragma target 3.5
                #pragma vertex Vert
                #pragma fragment Frag
                #include ""Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl""
                #include ""Assets/AshenVoxel/Rendering/AshenAtmosphere.hlsl""
                float4 _ProbeCamera,_ProbePoint;
                float4 Vert(float4 p:POSITION):SV_POSITION {return TransformObjectToHClip(p.xyz);}
                float4 Frag():SV_Target {float tau=AshenFogOpticalDepth(_ProbeCamera.xyz,_ProbePoint.xyz);return float4(exp(-tau),tau,0,1);}
                ENDHLSL } } }");
            var material=new Material(shader);
            var rt=new RenderTexture(1,1,0,RenderTextureFormat.ARGBFloat,RenderTextureReadWrite.Linear);
            var pixels=new Texture2D(1,1,TextureFormat.RGBAFloat,false,true);
            string[] names={"_AshenFogOriginY","_AshenFogDensity","_AshenFogBaseHeight","_AshenFogHeightFalloff","_AshenFogStart"};
            float[] saved=new float[names.Length];for(int i=0;i<names.Length;i++)saved[i]=Shader.GetGlobalFloat(names[i]);
            RenderTexture oldActive=RenderTexture.active;bool oldSrgb=GL.sRGBWrite;
            try
            {
                ShaderUtil.CompilePass(material,0,true);Require(!ShaderUtil.ShaderHasError(shader),"Fog probe shader failed.");
                var random=new System.Random(6127);double worst=0;
                using(var writer=new StreamWriter(Folder+"/fog-gpu.csv"))
                {
                    writer.WriteLine("ray,originY,expectedTransmission,gpuTransmission,absoluteError");
                    for(int test=0;test<48;test++)
                    {
                        float cy=random.Next(-600,4000),py=test%8==0?cy:random.Next(-600,6000),z=random.Next(1,40000);
                        float density=test%9==0?0:.00004f*(1+test%5),falloff=test%7==0?0:.00035f*(1+test%3),start=test%11==0?50000:80;
                        double distance=Math.Sqrt((py-cy)*(double)(py-cy)+z*(double)z),sum=0;
                        if(distance>start)
                        {
                            const int steps=2048;
                            for(int j=0;j<=steps;j++)
                            {
                                double fraction=start/distance+(1-start/distance)*j/steps;
                                sum+=(j==0||j==steps?1:j%2==0?2:4)*Math.Exp(-falloff*(cy+(py-cy)*fraction));
                            }
                            sum*=density*(distance-start)/(3*steps);
                        }
                        double expected=Math.Exp(-Math.Min(80,sum));
                        foreach(float origin in new[]{0f,256f,-1024f,8192f})
                        {
                            Shader.SetGlobalFloat(names[0],origin);Shader.SetGlobalFloat(names[1],density);Shader.SetGlobalFloat(names[2],0);
                            Shader.SetGlobalFloat(names[3],falloff);Shader.SetGlobalFloat(names[4],start);
                            material.SetVector("_ProbeCamera",new Vector4(0,cy-origin,0,0));material.SetVector("_ProbePoint",new Vector4(0,py-origin,z,0));
                            GL.sRGBWrite=false;Graphics.Blit(Texture2D.whiteTexture,rt,material);RenderTexture.active=rt;
                            pixels.ReadPixels(new Rect(0,0,1,1),0,0);pixels.Apply();
                            float actual=pixels.GetPixel(0,0).r;double error=Math.Abs(expected-actual);worst=Math.Max(worst,error);
                            Require(!float.IsNaN(actual)&&error<2e-5,"GPU fog transmission disagrees with independent quadrature.");
                            writer.WriteLine(FormattableString.Invariant($"{test},{origin:R},{expected:R},{actual:R},{error:R}"));
                        }
                    }
                    writer.WriteLine(FormattableString.Invariant($"PASS 192 GPU rays; worst absolute transmission error {worst:R}"));
                }
            }
            finally
            {
                for(int i=0;i<names.Length;i++)Shader.SetGlobalFloat(names[i],saved[i]);
                RenderTexture.active=oldActive;GL.sRGBWrite=oldSrgb;rt.Release();
                UnityEngine.Object.DestroyImmediate(pixels);UnityEngine.Object.DestroyImmediate(rt);
                UnityEngine.Object.DestroyImmediate(material);UnityEngine.Object.DestroyImmediate(shader);
            }
        }
        static void CapturePresets()
        {
            var oldDefault=GraphicsSettings.defaultRenderPipeline;var oldQuality=QualitySettings.renderPipeline;
            int oldAntialiasing=QualitySettings.antiAliasing;
            Material oldSky=RenderSettings.skybox;RenderTexture oldActive=RenderTexture.active;
            Light oldSun=RenderSettings.sun;Color oldFog=RenderSettings.fogColor;
            float oldFogEnabled=Shader.GetGlobalFloat("_AshenAtmosphereEnabled");
            var source=AssetDatabase.LoadAssetAtPath<RenderPipelineAsset>("Assets/AshenVoxel/Settings/ExplorationURP.asset");
            Require(source!=null,"Exploration URP asset missing.");
            var pipeline=UnityEngine.Object.Instantiate(source);
            var settings=ScriptableObject.CreateInstance<ExplorationSettings>();
            var root=new GameObject("Atmosphere fixture"){hideFlags=HideFlags.HideAndDontSave};
            var sky=new Material(Shader.Find("AshenVoxel/ExplorationSky")){hideFlags=HideFlags.HideAndDontSave};
            const int width=1600,height=900;
            // Keep HDR through the camera/post pipeline, then encode display values.
            // A UNorm camera target clips the sun before tone mapping; a half-float
            // target copied directly into PNG omits linear-to-sRGB display encoding.
            var rt=new RenderTexture(width,height,24,RenderTextureFormat.ARGBHalf,RenderTextureReadWrite.Linear);
            var display=new RenderTexture(width,height,0,RenderTextureFormat.ARGB32,RenderTextureReadWrite.sRGB);
            var pixels=new Texture2D(width,height,TextureFormat.RGB24,false);
            var environment=new ExplorationEnvironmentRuntime();
            Texture3D ownedNoise=null;
            TerrainContext context=null;
            try
            {
                GraphicsSettings.defaultRenderPipeline=pipeline;QualitySettings.renderPipeline=pipeline;
                var cameraObject=new GameObject("Fixture camera");cameraObject.transform.SetParent(root.transform,false);
                var camera=cameraObject.AddComponent<Camera>();camera.enabled=false;camera.cullingMask=1<<ExplorationCloudVolume.Layer;
                camera.clearFlags=CameraClearFlags.Skybox;camera.allowHDR=true;camera.fieldOfView=55;camera.targetTexture=rt;
                var sunObject=new GameObject("Fixture sun");sunObject.transform.SetParent(root.transform,false);
                var sun=sunObject.AddComponent<Light>();sun.type=LightType.Directional;
                RenderSettings.sun=sun;RenderSettings.skybox=sky;
                var quality=root.AddComponent<ExplorationVisualQuality>();quality.Initialize(camera,pipeline);
                environment.BindCamera(camera);
                context=new TerrainContext(root.transform);
                File.WriteAllText(Folder+"/terrain-context.txt",context.Description);
                foreach(ExplorationAtmospherePreset preset in Enum.GetValues(typeof(ExplorationAtmospherePreset)))
                {
                    ExplorationAtmosphere.ApplyPreset(settings,preset);settings.Atmosphere.CloudSpeed=0;
                    environment.Apply(settings,sky,sun);environment.SetOriginY(settings.Atmosphere.CloudBaseHeight-1200);
                    var noise=sky.GetTexture("_CloudNoiseTex") as Texture3D;
                    Require(noise!=null&&noise.width==64&&noise.height==64&&noise.depth==64&&noise.mipmapCount==7&&!noise.isReadable,"Cloud noise GPU resource contract failed.");
                    Require(ownedNoise==null||ownedNoise==noise,"Changing preset recreated the cloud noise.");ownedNoise=noise;
                    Require(sky.GetFloat("_CloudVolumeExternal")==1&&sky.GetFloat("_CloudDepthReady")==1,"Cloud composite did not acquire early opaque depth.");
                    environment.SetObserver(0,settings.Atmosphere.CloudBaseHeight-1200,0);
                    quality.Apply(settings.Atmosphere);camera.transform.rotation=Quaternion.Euler(-20,settings.SunAzimuth+180,0);
                    Color authoredHorizon=sky.GetColor("_HorizonColor");
                    var horizon=(QualitySettings.activeColorSpace==ColorSpace.Linear?authoredHorizon.linear:authoredHorizon)*settings.Atmosphere.SkyExposure;
                    var fog=Shader.GetGlobalVector("_AshenFogColor");
                    Require((new Vector3(horizon.r-fog.x,horizon.g-fog.y,horizon.b-fog.z)).sqrMagnitude<1e-10f,"Fog and sky horizon uploads disagree.");
                    RenderNative(camera);
                    bool oldSrgbWrite=GL.sRGBWrite;
                    try{GL.sRGBWrite=QualitySettings.activeColorSpace==ColorSpace.Linear;Graphics.Blit(rt,display);}
                    finally{GL.sRGBWrite=oldSrgbWrite;}
                    RenderTexture.active=display;pixels.ReadPixels(new Rect(0,0,width,height),0,0);pixels.Apply();
                    File.WriteAllBytes(Folder+"/"+preset+".png",pixels.EncodeToPNG());
                    context.Root.SetActive(true);camera.cullingMask=(1<<30)|(1<<ExplorationCloudVolume.Layer);
                    camera.transform.position=context.CameraPosition;camera.transform.rotation=context.CameraRotation;
                    camera.farClipPlane=24000;environment.SetOriginY(context.OriginY);
                    environment.SetObserver(context.X,camera.transform.position.y+context.OriginY,context.Z);
                    sun.shadows=LightShadows.Soft;
                    RenderNative(camera);oldSrgbWrite=GL.sRGBWrite;
                    try{GL.sRGBWrite=QualitySettings.activeColorSpace==ColorSpace.Linear;Graphics.Blit(rt,display);}
                    finally{GL.sRGBWrite=oldSrgbWrite;}
                    RenderTexture.active=display;pixels.ReadPixels(new Rect(0,0,width,height),0,0);pixels.Apply();
                    File.WriteAllBytes(Folder+"/"+preset+"-terrain.png",pixels.EncodeToPNG());
                    camera.transform.position=new Vector3(0,settings.Atmosphere.CloudBaseHeight+settings.Atmosphere.CloudThickness+450-context.OriginY,0);
                    camera.transform.rotation=Quaternion.Euler(8,90,0);camera.fieldOfView=65;
                    environment.SetObserver(context.X,camera.transform.position.y+context.OriginY,context.Z);
                    RenderNative(camera);oldSrgbWrite=GL.sRGBWrite;
                    try{GL.sRGBWrite=QualitySettings.activeColorSpace==ColorSpace.Linear;Graphics.Blit(rt,display);}
                    finally{GL.sRGBWrite=oldSrgbWrite;}
                    RenderTexture.active=display;pixels.ReadPixels(new Rect(0,0,width,height),0,0);pixels.Apply();
                    File.WriteAllBytes(Folder+"/"+preset+"-above-clouds.png",pixels.EncodeToPNG());
                    camera.transform.position-=Vector3.up*300;camera.transform.rotation=Quaternion.Euler(4,90,0);
                    environment.SetObserver(context.X,camera.transform.position.y+context.OriginY,context.Z);
                    RenderNative(camera);oldSrgbWrite=GL.sRGBWrite;
                    try{GL.sRGBWrite=QualitySettings.activeColorSpace==ColorSpace.Linear;Graphics.Blit(rt,display);}
                    finally{GL.sRGBWrite=oldSrgbWrite;}
                    RenderTexture.active=display;pixels.ReadPixels(new Rect(0,0,width,height),0,0);pixels.Apply();
                    File.WriteAllBytes(Folder+"/"+preset+"-close-clouds.png",pixels.EncodeToPNG());
                    context.Root.SetActive(false);camera.cullingMask=1<<ExplorationCloudVolume.Layer;camera.transform.position=Vector3.zero;camera.fieldOfView=55;sun.shadows=LightShadows.None;
                }
                CheckCloudOcclusion(camera,rt,environment,settings,sky,sun,quality);
                quality.Apply(new ExplorationAtmosphere{PostProcessing=false});
                Require(camera.GetComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>().renderPostProcessing,"Disabling grading disabled the AA camera stage.");
            }
            finally
            {
                context?.Dispose();
                environment.Dispose();RenderSettings.skybox=oldSky;RenderTexture.active=oldActive;
                Require(ownedNoise==null,"Owned cloud noise survived environment disposal.");
                GraphicsSettings.defaultRenderPipeline=oldDefault;QualitySettings.renderPipeline=oldQuality;
                UnityEngine.Object.DestroyImmediate(root);UnityEngine.Object.DestroyImmediate(sky);UnityEngine.Object.DestroyImmediate(settings);
                rt.Release();display.Release();UnityEngine.Object.DestroyImmediate(rt);UnityEngine.Object.DestroyImmediate(display);
                UnityEngine.Object.DestroyImmediate(pixels);UnityEngine.Object.DestroyImmediate(pipeline);
                QualitySettings.antiAliasing=oldAntialiasing;
            }
            Require(RenderSettings.sun==oldSun&&RenderSettings.fogColor==oldFog&&Shader.GetGlobalFloat("_AshenAtmosphereEnabled")==oldFogEnabled,"Environment state was not restored.");
        }
        static void CheckCloudOcclusion(Camera camera,RenderTexture rt,ExplorationEnvironmentRuntime environment,
            ExplorationSettings settings,Material sky,Light sun,ExplorationVisualQuality quality)
        {
            // Alternating near/far geometry catches stale or late depth textures.
            // A near opaque plane must remove all cloud light; a far plane must
            // remain behind the layer. This executes the actual composite shader.
            var shader=ShaderUtil.CreateShaderAsset(@"Shader ""Hidden/AshenCloudDepthProbe"" {
                SubShader { Tags { ""RenderPipeline""=""UniversalPipeline"" ""Queue""=""Geometry"" }
                Pass { Tags { ""LightMode""=""UniversalForward"" } Cull Off ZWrite On
                HLSLPROGRAM
                #pragma vertex vert
                #pragma fragment frag
                #include ""Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl""
                float4 vert(float4 p:POSITION):SV_POSITION {return TransformObjectToHClip(p.xyz);}
                float4 frag():SV_Target {return float4(.04,.03,.02,1);}
                ENDHLSL }
                Pass { Tags { ""LightMode""=""DepthOnly"" } Cull Off ZWrite On ColorMask R
                HLSLPROGRAM
                #pragma vertex vert
                #pragma fragment frag
                #include ""Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl""
                float4 vert(float4 p:POSITION):SV_POSITION {return TransformObjectToHClip(p.xyz);}
                float frag():SV_Target {return 0;}
                ENDHLSL }
                Pass { Tags { ""LightMode""=""DepthNormals"" } Cull Off ZWrite On
                HLSLPROGRAM
                #pragma vertex vert
                #pragma fragment frag
                #include ""Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl""
                float4 vert(float4 p:POSITION):SV_POSITION {return TransformObjectToHClip(p.xyz);}
                float4 frag():SV_Target {return float4(0,1,0,0);}
                ENDHLSL } } }");
            var material=new Material(shader);
            var mesh=new Mesh();mesh.vertices=new[]{new Vector3(-1,-1,0),new Vector3(1,-1,0),new Vector3(1,1,0),new Vector3(-1,1,0)};
            mesh.triangles=new[]{0,2,1,0,3,2};mesh.RecalculateBounds();
            var plane=new GameObject("Cloud depth probe"){layer=30,hideFlags=HideFlags.HideAndDontSave};
            plane.transform.SetParent(camera.transform,false);
            plane.AddComponent<MeshFilter>().sharedMesh=mesh;plane.AddComponent<MeshRenderer>().sharedMaterial=material;
            var readback=new Texture2D(1,1,TextureFormat.RGBAFloat,false,true);
            try
            {
                for(int pass=0;pass<material.passCount;pass++)ShaderUtil.CompilePass(material,pass,true);
                Require(!ShaderUtil.ShaderHasError(shader),"Cloud occlusion fixture shader failed compilation.");
                ExplorationAtmosphere.ApplyPreset(settings,ExplorationAtmospherePreset.ClearDay);
                settings.Atmosphere.PostProcessing=false;settings.Atmosphere.CloudSpeed=0;
                settings.Atmosphere.CloudCoverage=1;settings.Atmosphere.CloudDensity=2;
                quality.Apply(settings.Atmosphere);camera.transform.position=Vector3.zero;
                camera.transform.rotation=Quaternion.Euler(90,0,0);camera.farClipPlane=10000;
                camera.cullingMask=(1<<30)|(1<<ExplorationCloudVolume.Layer);
                Color Sample(float distance,int cloudQuality)
                {
                    plane.transform.localPosition=new Vector3(0,0,distance);plane.transform.localScale=Vector3.one*distance*2;
                    settings.Atmosphere.CloudQuality=cloudQuality;environment.Apply(settings,sky,sun);
                    environment.SetObserver(0,settings.Atmosphere.CloudBaseHeight+settings.Atmosphere.CloudThickness+450,0);
                    RenderNative(camera);RenderTexture.active=rt;
                    readback.ReadPixels(new Rect(rt.width/2,rt.height/2,1,1),0,0);readback.Apply();return readback.GetPixel(0,0);
                }
                Color farOff=Sample(3000,0),nearOn=Sample(20,2),nearOff=Sample(20,0),farOn=Sample(3000,2);
                float nearError=Mathf.Max(Mathf.Abs(nearOn.r-nearOff.r),Mathf.Abs(nearOn.g-nearOff.g),Mathf.Abs(nearOn.b-nearOff.b));
                float farGain=farOn.grayscale-farOff.grayscale;
                File.WriteAllText(Folder+"/cloud-depth.txt",FormattableString.Invariant($"Actual GPU composite, alternating far/near opaque plane with forward, depth and depth-normal passes. Near on/off max channel error={nearError:R}; far cloud luminance gain={farGain:R}. Near off={nearOff}; far off={farOff}; near on={nearOn}; far on={farOn}. After-opaques depth copy; no Play."));
                Require(nearError<.0005f,"Clouds leaked through the foreground opaque plane.");
                Require(farGain>.08f,"Clouds failed to obscure the far opaque plane (possibly stale depth).");
                settings.Atmosphere.PostProcessing=true;settings.Atmosphere.PostExposure=0;quality.Apply(settings.Atmosphere);
                Color gradeZero=Sample(20,0);
                settings.Atmosphere.PostExposure=1;quality.Apply(settings.Atmosphere);
                Color gradeOne=Sample(20,0);
                File.WriteAllText(Folder+"/post-exposure.txt",FormattableString.Invariant($"Native URP render request, ACES exposure0 luminance={gradeZero.grayscale:R}; exposure+1 luminance={gradeOne.grayscale:R}. No Play."));
                Require(gradeOne.grayscale>gradeZero.grayscale*1.3f,"Post exposure did not visibly affect the native camera output.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(plane);UnityEngine.Object.DestroyImmediate(mesh);
                UnityEngine.Object.DestroyImmediate(material);UnityEngine.Object.DestroyImmediate(shader);UnityEngine.Object.DestroyImmediate(readback);
            }
        }
        sealed class TerrainContext : IDisposable
        {
            public readonly GameObject Root;
            public readonly float OriginY;
            public readonly double X,Z;
            public readonly Vector3 CameraPosition;
            public readonly Quaternion CameraRotation;
            public readonly string Description;
            readonly List<Mesh> meshes=new List<Mesh>();
            readonly Material material;
            static HeightAtlas Height(TextAsset asset){using(var s=new MemoryStream(asset.bytes,false))return AtlasIO.Read(s);}
            static ClimateAtlas Climate(TextAsset asset){using(var s=new MemoryStream(asset.bytes,false))return ClimateArchiveIO.Read(s);}
            public TerrainContext(Transform parent)
            {
                var authored=AssetDatabase.LoadAssetAtPath<ExplorationSettings>("Assets/AshenVoxel/Settings/ExplorationSettings.asset");
                var macro=Height(authored.MacroWorld);var fine=Height(authored.FineWorld);
                ExplorationBiomeAtlas biomes;
                using(var s=new MemoryStream(authored.SourceBiomes.bytes,false))biomes=ExplorationBiomeAtlasIO.Read(s);
                IWorldField field=new ExplorationAtlasField(macro,Climate(authored.MacroClimate),fine,Climate(authored.FineClimate),
                    authored.FineBlendMeters,macro.ExtentX,macro.ExtentZ,authored.VerticalScale,biomes,macro.ExtentX,macro.ExtentZ);
                if(authored.EnableTerrainReshape&&authored.TerrainReshape!=null)
                    using(var s=new MemoryStream(authored.TerrainReshape.bytes,false))field=new TerrainReshapeField(field,TerrainReshapeIO.Read(s));
                var surface=new SurfaceMaterialSampler(field,authored.Surface());
                double cx=authored.SpawnX,cz=authored.SpawnZ;X=cx;Z=cz;
                double ground=surface.EvaluateColumn(cx,cz).SurfaceHeightMeters;
                OriginY=(float)(Math.Floor(ground/256)*256);
                CameraPosition=new Vector3(0,(float)(ground-OriginY+90),0);
                CameraRotation=Quaternion.Euler(-7,authored.SpawnYaw,0);
                Root=new GameObject("Bounded source terrain context"){hideFlags=HideFlags.HideAndDontSave};Root.transform.SetParent(parent,false);
                material=new Material(Shader.Find("AshenVoxel/GroundSurface")){hideFlags=HideFlags.HideAndDontSave};
                material.SetVector("_SurfaceOriginPhase",new Vector4((float)(cx%4096),OriginY%4096,(float)(cz%4096),0));
                Build(surface,cx,cz,32,false);Build(surface,cx,cz,256,true);
                Root.SetActive(false);
                Description=FormattableString.Invariant($"Bounded Edit-mode lighting context; no Play, streaming, services, props or gameplay. Two source-sampled meshes, each at most 16641 vertices/32768 triangles. Near grid 32m across4096m; far ring256m across32768m. These are diagnostic render samples, not 10cm matter or added source detail. Camera global ({cx:R},{ground+90:R},{cz:R}), yaw {authored.SpawnYaw:R}, pitch7 degrees up; render originY {OriginY:R}. Source: {surface.SourceIdentity}");
            }
            void Build(SurfaceMaterialSampler surface,double cx,double cz,int spacing,bool ring)
            {
                const int cells=128,side=129;var vertices=new Vector3[side*side];var colors=new Color32[vertices.Length];
                var triangles=new List<int>(cells*cells*6);
                for(int z=0;z<side;z++)for(int x=0;x<side;x++)
                {
                    int i=z*side+x;float px=(x-64)*spacing,pz=(z-64)*spacing;
                    var column=surface.EvaluateColumn(cx+px,cz+pz,spacing);
                    vertices[i]=new Vector3(px,(float)column.SurfaceHeightMeters-OriginY,pz);
                    uint tint=column.SurfaceTint;colors[i]=new Color32((byte)(tint>>16),(byte)(tint>>8),(byte)tint,(byte)(((byte)column.SurfaceMaterial<<2)|3));
                    if(x==cells||z==cells)continue;
                    float mx=px+spacing*.5f,mz=pz+spacing*.5f;
                    if(ring&&Mathf.Abs(mx)<2048&&Mathf.Abs(mz)<2048)continue;
                    triangles.Add(i);triangles.Add(i+side);triangles.Add(i+1);
                    triangles.Add(i+1);triangles.Add(i+side);triangles.Add(i+side+1);
                }
                // Match the far ring's linear edge at the shared boundary.
                if(!ring)
                    for(int edge=0;edge<4;edge++)for(int j=0;j<128;j++)
                    {
                        int low=j/8*8,high=low+8;
                        int At(int k)=>edge==0?k:edge==1?128*side+k:edge==2?k*side:k*side+128;
                        int at=At(j);var p=vertices[at];p.y=Mathf.Lerp(vertices[At(low)].y,vertices[At(high)].y,(j-low)/8f);vertices[at]=p;
                    }
                var mesh=new Mesh{name=ring?"Bounded far source ring":"Bounded near source"};meshes.Add(mesh);
                mesh.vertices=vertices;mesh.colors32=colors;mesh.SetTriangles(triangles,0);mesh.RecalculateNormals();mesh.RecalculateBounds();
                var tile=new GameObject(mesh.name){layer=30,hideFlags=HideFlags.HideAndDontSave};tile.transform.SetParent(Root.transform,false);
                tile.AddComponent<MeshFilter>().sharedMesh=mesh;tile.AddComponent<MeshRenderer>().sharedMaterial=material;
            }
            public void Dispose()
            {
                UnityEngine.Object.DestroyImmediate(Root);foreach(var mesh in meshes)UnityEngine.Object.DestroyImmediate(mesh);
                UnityEngine.Object.DestroyImmediate(material);
            }
        }
    }
}
