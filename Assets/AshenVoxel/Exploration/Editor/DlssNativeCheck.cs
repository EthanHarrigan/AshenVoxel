#if ENABLE_UPSCALER_FRAMEWORK && ENABLE_NVIDIA && ASHEN_NVIDIA_MODULE
using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.NVIDIA;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
namespace AshenVoxel.Exploration.Editor
{
    public static class DlssNativeCheck
    {
        static Camera camera;static RenderTexture target;static ExplorationUpscaling controller;static ExplorationSettings settings;
        static int frame,mode;static double started;const string Output="Logs/DlssNative";
        [Serializable] sealed class Result {public string mode,gpu,unity,status;public int frames,valid_features;public uint input_width,input_height,output_width,output_height;public long pixel_energy;}
        public static void Run(){EditorApplication.delayCall+=StartFixture;}
        static void StartFixture()
        {
            Directory.CreateDirectory(Output);
            try
            {
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene);
                var original=GraphicsSettings.currentRenderPipeline;var owned=UnityEngine.Object.Instantiate(original);owned.name="DLSS native review pipeline";
                var root=new GameObject("Bounded DLSS camera");camera=root.AddComponent<Camera>();camera.enabled=false;camera.allowHDR=true;camera.nearClipPlane=.1f;camera.farClipPlane=300;
                camera.transform.position=new Vector3(0,3,-9);camera.transform.LookAt(new Vector3(0,1,0));camera.backgroundColor=new Color(.2f,.35f,.55f);camera.clearFlags=CameraClearFlags.SolidColor;
                settings=ScriptableObject.CreateInstance<ExplorationSettings>();settings.Upscaling=ExplorationUpscaleMode.NativeSmaa;
                root.AddComponent<ExplorationVisualQuality>().Initialize(camera,owned);
                controller=root.AddComponent<ExplorationUpscaling>();controller.Initialize(camera,owned,settings);
                if(!controller.NvidiaAvailable)throw new Exception(controller.Status);
                GraphicsSettings.defaultRenderPipeline=owned;QualitySettings.renderPipeline=owned;
                var sun=new GameObject("DLSS check sun").AddComponent<Light>();sun.type=LightType.Directional;sun.intensity=2;sun.transform.rotation=Quaternion.Euler(45,-20,0);sun.shadows=LightShadows.Soft;
                RenderSettings.ambientMode=AmbientMode.Trilight;RenderSettings.ambientSkyColor=new Color(.4f,.5f,.6f);RenderSettings.ambientEquatorColor=new Color(.25f,.3f,.35f);RenderSettings.ambientGroundColor=new Color(.1f,.1f,.1f);
                var material=new Material(Shader.Find("Universal Render Pipeline/Lit"));material.color=new Color(.35f,.65f,.25f);
                for(int i=0;i<16;i++){var cube=GameObject.CreatePrimitive(PrimitiveType.Cube);cube.transform.position=new Vector3(i%4-1.5f,(i%3)*.3f,i/4);cube.transform.localScale=new Vector3(.7f,1+(i%3)*.6f,.7f);cube.GetComponent<Renderer>().sharedMaterial=material;}
                var plane=GameObject.CreatePrimitive(PrimitiveType.Plane);plane.GetComponent<Renderer>().sharedMaterial=material;
                target=new RenderTexture(1280,720,24,RenderTextureFormat.ARGBHalf);target.Create();camera.targetTexture=target;
                started=EditorApplication.timeSinceStartup;EditorApplication.update+=Tick;
            }
            catch(Exception e){Fail(e);}
        }
        static void Tick()
        {
            try
            {
                if(EditorApplication.timeSinceStartup-started>120)throw new Exception("DLSS native fixture timed out.");
                camera.transform.position+=new Vector3(.003f,0,0);camera.Render();
                if(++frame<16)return;
                var result=new Result{mode=settings.Upscaling.ToString(),gpu=SystemInfo.graphicsDeviceName,unity=Application.unityVersion,status=controller.Status,frames=frame};
                var device=GraphicsDevice.device;var debug=device.CreateDebugView();device.UpdateDebugView(debug);
                foreach(var feature in debug.dlssFeatureInfos)if(feature.validFeature){result.valid_features++;result.input_width=feature.initData.inputRTWidth;result.input_height=feature.initData.inputRTHeight;result.output_width=feature.initData.outputRTWidth;result.output_height=feature.initData.outputRTHeight;}
                device.DeleteDebugView(debug);
                var prior=RenderTexture.active;RenderTexture.active=target;var image=new Texture2D(1280,720,TextureFormat.RGB24,false);image.ReadPixels(new Rect(0,0,1280,720),0,0);image.Apply();RenderTexture.active=prior;
                foreach(var p in image.GetPixels32())result.pixel_energy+=p.r+p.g+p.b;
                File.WriteAllBytes(Output+"/"+result.mode+".png",image.EncodeToPNG());UnityEngine.Object.DestroyImmediate(image);
                if(result.pixel_energy<1000000)throw new Exception("Upscaling output is unexpectedly blank.");
                if(mode>0&&(result.valid_features<1||result.output_width!=1280||result.output_height!=720))throw new Exception("No executed DLSS context at the requested output resolution.");
                if(mode==1&&result.input_width!=1280)throw new Exception("DLAA did not retain native input resolution.");
                if(mode==2&&(result.input_width>=1280||result.input_width==0))throw new Exception("DLSS Quality did not select an upscaled input resolution.");
                File.WriteAllText(Output+"/"+result.mode+".json",JsonUtility.ToJson(result,true));
                if(++mode>2){File.WriteAllText(Output+"/completed.txt","PASS actual GPU DLAA and DLSS Quality execution through URP RenderGraph, camera motion, native fallback, valid NVIDIA feature and pixel readback.");EditorApplication.update-=Tick;EditorApplication.Exit(0);return;}
                settings.Upscaling=(ExplorationUpscaleMode)mode;controller.ApplyMode();frame=0;
            }
            catch(Exception e){Fail(e);}
        }
        static void Fail(Exception e){Directory.CreateDirectory(Output);File.WriteAllText(Output+"/failure.txt",e.ToString());EditorApplication.update-=Tick;Debug.LogException(e);EditorApplication.Exit(1);}
    }
}
#endif
