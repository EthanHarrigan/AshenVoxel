using System;
using System.Collections;
using System.IO;
using System.Reflection;
using System.Threading;
using AshenVoxel.WorldScale;
using AshenVoxel.WorldScaleRuntime;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace AshenVoxel.Exploration.Editor
{
    /// <summary>Small GPU fixture, never starts the full-world Play workload.</summary>
    public static class QualityGraphicsCheck
    {
        const BindingFlags Flags=BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public;
        static object Get(object o,string n)=>o.GetType().GetField(n,Flags).GetValue(o);
        static void Set(object o,string n,object v)=>o.GetType().GetField(n,Flags).SetValue(o,v);
        public static void Run(){EditorApplication.delayCall+=StartFixture;}
        public static void RunWindOptics(){EditorApplication.delayCall+=RunWindOpticsFixture;}
        static void RunWindOpticsFixture()
        {
            const string folder="Logs/WindOptics";Directory.CreateDirectory(folder);
            try
            {
                var pipeline=AssetDatabase.LoadAssetAtPath<RenderPipelineAsset>(ExplorationProjectSetup.PipelinePath);
                if(pipeline==null)throw new Exception("Review pipeline is missing.");
                GraphicsSettings.defaultRenderPipeline=pipeline;QualitySettings.renderPipeline=pipeline;
                MaterialOpticsNativeCheck.Test();WindNativeCheck.Test();ForestShadeNativeCheck.Test();
                foreach(string name in new[]{"AshenVoxel/GroundSurface","AshenVoxel/ForestImpostor","AshenVoxel/ForestHorizonGpu"})
                {
                    var shader=Shader.Find(name);if(shader==null)throw new Exception("Missing shader: "+name);
                    var material=new Material(shader);
                    try{for(int pass=0;pass<material.passCount;pass++)ShaderUtil.CompilePass(material,pass,true);}
                    finally{UnityEngine.Object.DestroyImmediate(material);}
                    if(ShaderUtil.ShaderHasError(shader))throw new Exception("Shader error: "+name);
                }
                File.WriteAllText(folder+"/completed.txt","PASS native wind/opacity/forest shading and all changed shader passes. "+DateTime.UtcNow.ToString("o"));
                EditorApplication.Exit(0);
            }
            catch(Exception e){File.WriteAllText(folder+"/failure.txt",e.ToString());Debug.LogException(e);EditorApplication.Exit(1);}
        }
        static void StartFixture()
        {
            string folder="Logs/QualityGraphics";Directory.CreateDirectory(folder);
            try
            {
                var pipeline=AssetDatabase.LoadAssetAtPath<RenderPipelineAsset>(ExplorationProjectSetup.PipelinePath);
                if(pipeline==null)throw new Exception("Review pipeline is missing.");
                GraphicsSettings.defaultRenderPipeline=pipeline;QualitySettings.renderPipeline=pipeline;
                ForestAtlasBaker.Bake();
                MaterialOpticsNativeCheck.Test();
                WindNativeCheck.Test();
                ForestShadeNativeCheck.Test();
                var settings=AssetDatabase.LoadAssetAtPath<ExplorationSettings>(ExplorationProjectSetup.SettingsPath);
                HeightAtlas Height(TextAsset a){using(var stream=new MemoryStream(a.bytes))return AtlasIO.Read(stream);}
                ClimateAtlas Climate(TextAsset a){using(var stream=new MemoryStream(a.bytes))return ClimateArchiveIO.Read(stream);}
                var macro=Height(settings.MacroWorld);var fine=Height(settings.FineWorld);
                var field=new TerrainDetailField(new ExplorationAtlasField(macro,Climate(settings.MacroClimate),fine,Climate(settings.FineClimate),settings.FineBlendMeters,macro.ExtentX,macro.ExtentZ,settings.VerticalScale),settings.Seed);
                var go=new GameObject("Bounded native terrain review");var vista=go.AddComponent<WorldExplorer>();vista.enabled=false;vista.ShowVisualWater=false;
                Set(vista,"field",new SurfaceWorldField(field,settings.Surface(),new HeightBounds(-12000,12000)));Set(vista,"externalProfile",settings.Surface());
                Set(vista,"originX",89881.6);Set(vista,"originY",400.0);Set(vista,"originZ",12467.2);
                typeof(WorldExplorer).GetMethod("EnsureCameraAndMaterial",Flags).Invoke(vista,null);
                var camera=vista.MainCamera;camera.enabled=false;camera.tag="Untagged";
                var lightObject=new GameObject("Review sunlight");var light=lightObject.AddComponent<Light>();light.type=LightType.Directional;light.color=settings.SunColor;light.intensity=settings.SunIntensity;light.shadows=LightShadows.Soft;light.transform.rotation=Quaternion.Euler(36,325,0);
                AshenVoxel.Rendering.MaterialOpticsProfile.SetAmbient(settings.AmbientSky,settings.AmbientEquator,settings.AmbientGround,settings.AmbientFillStrength);
                RenderSettings.ambientMode=AmbientMode.Trilight;RenderSettings.ambientSkyColor=settings.AmbientSky;RenderSettings.ambientEquatorColor=settings.AmbientEquator;RenderSettings.ambientGroundColor=settings.AmbientGround;RenderSettings.fog=false;
                var sky=new Material(Resources.Load<Shader>("ExplorationSky"));RenderSettings.skybox=sky;sky.SetVector("_SunDirection",-light.transform.forward);camera.clearFlags=CameraClearFlags.Skybox;
                var meshMethod=typeof(WorldExplorer).GetMethod("BuildVoxelSurfaceSteps",Flags);
                long bytes=0;int tiles=0;
                for(int z=0;z<2;z++)for(int x=0;x<2;x++)
                {
                    var tile=new LodTile(new TileKey(3511+x,487+z,0),(3511+x)*25.6,(487+z)*25.6,25.6,-12000,12000);
                    var steps=(IEnumerator)meshMethod.Invoke(vista,new object[]{tile,256,false});object surface=null;int iterations=0;
                    while(steps.MoveNext()){if(steps.Current!=null)surface=steps.Current;else if(++iterations%100==0)Thread.Sleep(1);}
                    (steps as IDisposable)?.Dispose();if(surface==null)throw new Exception("Native surface conversion did not complete.");
                    var mesh=(Mesh)Get(surface,"Mesh");if(mesh.GetVertexBufferStride(0)!=24)throw new Exception("Packed terrain vertex layout changed.");
                    bytes+=(long)mesh.vertexCount*24+(long)mesh.GetIndexCount(0)*4;((GameObject)Get(surface,"Object")).SetActive(true);tiles++;
                }
                double eyeX=89902,eyeZ=12475;double height=field.Sample(eyeX,eyeZ).HeightMeters;
                camera.transform.position=new Vector3((float)(eyeX-89881.6),(float)(height+3-400),(float)(eyeZ-12467.2));camera.transform.rotation=Quaternion.Euler(24,5,0);camera.fieldOfView=65;
                var rt=new RenderTexture(1280,720,24);camera.targetTexture=rt;var previous=RenderTexture.active;
                camera.Render();RenderTexture.active=rt;var image=new Texture2D(1280,720,TextureFormat.RGB24,false);image.ReadPixels(new Rect(0,0,1280,720),0,0);image.Apply();File.WriteAllBytes(folder+"/redwood-ground.png",image.EncodeToPNG());RenderTexture.active=previous;camera.targetTexture=null;rt.Release();
                foreach(string name in new[]{"AshenVoxel/GroundSurface","AshenVoxel/ForestImpostor","AshenVoxel/ForestHorizonGpu","AshenVoxel/ExplorationSky"})if(ShaderUtil.ShaderHasError(Shader.Find(name)))throw new Exception("Shader error: "+name);
                File.WriteAllText(folder+"/terrain.json","{\"status\":\"PASS\",\"tiles\":"+tiles+",\"vertex_index_bytes\":"+bytes+",\"scope\":\"Actual source at redwoods, actual runtime worker and packed native mesh upload, offscreen DX12 camera. No full-scene FPS claim.\"}");
                // This batch process exits after bounded fixtures, releasing all native resources.
                ForestGpuChecks.Run();
                Type.GetType("AshenVoxel.Rendering.Editor.MeshRegressionChecks, AshenVoxel.Rendering.Editor",true).GetMethod("Run").Invoke(null,null);
                File.WriteAllText(folder+"/completed.txt","PASS native terrain upload/render, new atlas bake, indirect forest checks, native mesh regressions. "+Application.unityVersion+" / "+SystemInfo.graphicsDeviceName);
                EditorApplication.Exit(0);
            }
            catch(Exception e){File.WriteAllText(folder+"/failure.txt",e.ToString());Debug.LogException(e);EditorApplication.Exit(1);}
        }
    }
}
