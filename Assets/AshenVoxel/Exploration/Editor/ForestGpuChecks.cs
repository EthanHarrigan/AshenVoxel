using System;
using System.IO;
using AshenVoxel.WorldScale;
using UnityEditor;
using UnityEngine;

namespace AshenVoxel.Exploration.Editor
{
    public static class ForestGpuChecks
    {
        [Serializable] sealed class Result
        {
            public string unity,gpu,api,scope="Actual compute culling, GPU append count, indirect rendering and pixel readback; not FPS profiling";
            public int checks,tree_pixels;public long lit_energy,reverse_energy;
            public uint visible_30km,visible_100km,visible_after_turn,visible_after_rebase;
        }
        static void Check(bool value,string message){if(!value)throw new InvalidOperationException(message);Debug.Log("FOREST_GPU_PASS "+message);}
        public static void Run()
        {
            UnityEditor.SceneManagement.EditorSceneManager.OpenScene(ExplorationProjectSetup.ScenePath);
            var lightingObject=new GameObject("Impostor lighting check sun");var lighting=lightingObject.AddComponent<Light>();lighting.type=LightType.Directional;lighting.intensity=2;lighting.transform.rotation=Quaternion.Euler(45,0,0);
            RenderSettings.ambientMode=UnityEngine.Rendering.AmbientMode.Custom;var ambient=new UnityEngine.Rendering.SphericalHarmonicsL2();ambient.AddAmbientLight(new Color(.16f,.2f,.24f));RenderSettings.ambientProbe=ambient;
            var template=Resources.Load<Material>("ForestImpostor");
            using(var renderer=ForestGpuRenderer.TryCreate(template))
            {
                Check(renderer!=null,"Compute and indirect support available; native GPU path created");
                var root=new GameObject("Forest GPU test camera");var camera=root.AddComponent<Camera>();camera.cullingMask=1<<29;
                camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=Color.black;camera.fieldOfView=60;camera.aspect=16f/9;
                camera.nearClipPlane=.1f;camera.farClipPlane=125000;camera.transform.position=new Vector3(0,10000,0);camera.transform.rotation=Quaternion.identity;
                var target=new RenderTexture(1280,720,24);camera.targetTexture=target;
                ForestGpuRenderer.Instance At(float x,float z,float span)=>new ForestGpuRenderer.Instance{PositionSpan=new Vector4(x,10000,z,span),Style=new Vector4(1,4,0,0)};
                var data=new[]{At(0,100,20),At(-500,50000,3000),At(1000,90000,5000),At(0,-1000,20),At(10000,100,20),At(0,110000,20),At(0,10,20)};
                var eye=new WorldPoint(0,10000,0);var origin=new WorldPoint(0,0,0);var result=new Result{unity=Application.unityVersion,gpu=SystemInfo.graphicsDeviceName,api=SystemInfo.graphicsDeviceType.ToString()};
                try
                {
                    renderer.Publish(data,origin);
                    renderer.Draw(camera,eye,origin,50,30000,29);camera.Render();result.visible_30km=renderer.ReadVisibleCountForValidation();
                    Check(result.visible_30km==1,"30km compute range/frustum excludes near, behind, offscreen and far stands");
                    renderer.Draw(camera,eye,origin,50,100000,29);camera.Render();result.visible_100km=renderer.ReadVisibleCountForValidation();
                    Check(result.visible_100km==3,"100km visibility retains actual 50km and90km instances");
                    var previous=RenderTexture.active;RenderTexture.active=target;var pixels=new Texture2D(1280,720,TextureFormat.RGB24,false);
                    pixels.ReadPixels(new Rect(0,0,1280,720),0,0);pixels.Apply();RenderTexture.active=previous;
                    foreach(var c in pixels.GetPixels32()){if(c.r>10||c.g>10||c.b>10)result.tree_pixels++;result.lit_energy+=c.r+c.g+c.b;}
                    Check(result.tree_pixels>100,"Indirect GPU primitives render non-background tree pixels");
                    Directory.CreateDirectory("Logs/ForestGpuChecks");File.WriteAllBytes("Logs/ForestGpuChecks/indirect-100km.png",pixels.EncodeToPNG());lighting.transform.rotation=Quaternion.Euler(45,180,0);renderer.Draw(camera,eye,origin,50,100000,29);camera.Render();RenderTexture.active=target;pixels.ReadPixels(new Rect(0,0,1280,720),0,0);pixels.Apply();RenderTexture.active=previous;
                    foreach(var c in pixels.GetPixels32())result.reverse_energy+=c.r+c.g+c.b;
                    Check(Math.Abs(result.lit_energy-result.reverse_energy)>result.lit_energy*.02,"Distant atlas normals respond to the live sun direction instead of baked light");
                    UnityEngine.Object.DestroyImmediate(pixels);
                    camera.transform.rotation=Quaternion.Euler(0,180,0);renderer.Draw(camera,eye,origin,50,100000,29);camera.Render();result.visible_after_turn=renderer.ReadVisibleCountForValidation();
                    Check(result.visible_after_turn==1,"Camera turn updates GPU frustum culling and resets append counter");
                    camera.transform.rotation=Quaternion.identity;camera.transform.position=new Vector3(-8192,10000,0);
                    renderer.Draw(camera,eye,new WorldPoint(8192,0,0),50,100000,29);camera.Render();result.visible_after_rebase=renderer.ReadVisibleCountForValidation();
                    Check(result.visible_after_rebase==3,"Origin rebase keeps GPU source positions coherent without matrix rewrites");
                    renderer.Publish(Array.Empty<ForestGpuRenderer.Instance>(),origin);Check(renderer.Draw(camera,eye,origin,50,100000,29)==0&&renderer.ReadVisibleCountForValidation()==0,"Empty publication removes old indirect instances");
                    Check(!ShaderUtil.ShaderHasError(Resources.Load<Shader>("ForestHorizonGpu")),"Actual indirect forward/depth/normal shader imports without errors");
                    var settings=ScriptableObject.CreateInstance<ExplorationSettings>();settings.ViewDistanceKm=30;settings.FarHorizonKm=100;settings.ForestHorizonKm=2;
                    Check(settings.EffectiveForestDistanceKm==100,"Default forest distance follows full100km terrain horizon rather than legacy forest slider");
                    settings.ForestFollowsTerrainView=false;Check(settings.EffectiveForestDistanceKm==2,"Explicit manual forest distance remains available");UnityEngine.Object.DestroyImmediate(settings);
                    result.checks=11;File.WriteAllText("Logs/ForestGpuChecks/validation.json",JsonUtility.ToJson(result,true));
                }
                finally{camera.targetTexture=null;target.Release();UnityEngine.Object.DestroyImmediate(target);UnityEngine.Object.DestroyImmediate(root);UnityEngine.Object.DestroyImmediate(lightingObject);}
            }
        }
    }
}
