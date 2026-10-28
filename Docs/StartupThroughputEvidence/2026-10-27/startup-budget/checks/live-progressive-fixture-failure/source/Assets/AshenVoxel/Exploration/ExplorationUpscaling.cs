namespace AshenVoxel.Exploration
{
    public enum ExplorationUpscaleMode { NativeSmaa=0,NvidiaDlaa=1,NvidiaQuality=2 }
}
#if UNITY_5_3_OR_NEWER
namespace AshenVoxel.Exploration
{
    using System;
    using System.Reflection;
    using UnityEngine;
    using UnityEngine.Rendering;
    using UnityEngine.Rendering.Universal;
    using AshenVoxel.WorldScaleRuntime;
#if ENABLE_UPSCALER_FRAMEWORK && ENABLE_NVIDIA && ASHEN_NVIDIA_MODULE
    using UnityEngine.NVIDIA;
#endif
    [DefaultExecutionOrder(300)]
    public sealed class ExplorationUpscaling:MonoBehaviour
    {
        Camera cameraView;UniversalRenderPipelineAsset pipeline;UniversalAdditionalCameraData cameraData;
        WorldExplorer vista;ExplorationSettings settings;int applied=-1;Vector3 lastOrigin;bool haveOrigin;
        public string Status {get;private set;}="Native resolution + SMAA";
        public bool NvidiaAvailable {get;private set;}
        public int HistoryResets {get;private set;}
#if ENABLE_UPSCALER_FRAMEWORK && ENABLE_NVIDIA && ASHEN_NVIDIA_MODULE
        DLSSOptions options;
#endif
        public void Initialize(Camera camera,RenderPipelineAsset ownedPipeline,ExplorationSettings activeSettings,WorldExplorer world=null)
        {
            cameraView=camera;pipeline=ownedPipeline as UniversalRenderPipelineAsset;cameraData=camera.GetUniversalAdditionalCameraData();settings=activeSettings;vista=world;
#if ENABLE_UPSCALER_FRAMEWORK && ENABLE_NVIDIA && ASHEN_NVIDIA_MODULE
            try
            {
                NvidiaAvailable=SystemInfo.graphicsDeviceVendor.ToLowerInvariant().Contains("nvidia")&&
                    (NVUnityPlugin.IsLoaded()||NVUnityPlugin.Load())&&GraphicsDevice.CreateGraphicsDevice()!=null&&GraphicsDevice.device.IsFeatureAvailable(GraphicsDeviceFeature.DLSS);
                if(NvidiaAvailable&&pipeline!=null)
                {
                    options=ScriptableObject.CreateInstance<DLSSOptions>();options.name="Exploration owned DLSS options";
                    options.UpscalerName=DLSSIUpscaler.UPSCALER_NAME;options.FixedResolutionMode=true;
                    pipeline.iUpscalerOptions.RemoveAll(o=>o!=null&&o.UpscalerName==options.UpscalerName);pipeline.iUpscalerOptions.Add(options);
                    UpscalerRegistry.Register<DLSSIUpscaler,DLSSOptions>(options.UpscalerName);
                    var field=typeof(UniversalRenderPipelineAsset).GetField("m_IUpscalerName",BindingFlags.NonPublic|BindingFlags.Instance);
                    if(field==null)throw new NotSupportedException("Pinned URP upscaler selection field is unavailable.");
                    field.SetValue(pipeline,options.UpscalerName);
                }
            }
            catch(Exception e){NvidiaAvailable=false;Status="DLSS unavailable: "+e.Message;Debug.LogWarning(Status);}
#endif
            ApplyMode();
        }
        public void ApplyMode()
        {
            if(settings==null||pipeline==null)return;
            int mode=Mathf.Clamp((int)settings.Upscaling,0,2);if(applied==mode)return;applied=mode;
            pipeline.msaaSampleCount=1;pipeline.renderScale=1;cameraView.allowDynamicResolution=false;cameraData.renderPostProcessing=true;
            pipeline.upscalingFilter=UpscalingFilterSelection.Auto;
            cameraData.antialiasing=AntialiasingMode.SubpixelMorphologicalAntiAliasing;cameraData.antialiasingQuality=AntialiasingQuality.High;
            Status=mode==0?"Native resolution + SMAA":"NVIDIA upscaling unavailable; using native resolution + SMAA";
#if ENABLE_UPSCALER_FRAMEWORK && ENABLE_NVIDIA && ASHEN_NVIDIA_MODULE
            if(mode>0&&NvidiaAvailable&&options!=null)
            {
                options.DLSSQualityMode=mode==1?DLSSQuality.DLAA:DLSSQuality.MaximumQuality;
                pipeline.upscalingFilter=UpscalingFilterSelection.IUpscaler;
                cameraData.antialiasing=AntialiasingMode.TemporalAntiAliasing;
                Status=mode==1?"NVIDIA DLAA · native resolution":"NVIDIA DLSS · Quality";
            }
#endif
            cameraData.resetHistory=true;HistoryResets++;
        }
        void LateUpdate()
        {
            ApplyMode();if(vista==null||cameraData==null)return;
            var global=vista.GlobalCamera;var local=cameraView.transform.position;
            var origin=new Vector3((float)(global.X-local.x),(float)(global.Y-local.y),(float)(global.Z-local.z));
            if(haveOrigin&&(origin-lastOrigin).sqrMagnitude>.01f){cameraData.resetHistory=true;HistoryResets++;}
            haveOrigin=true;lastOrigin=origin;
        }
        void OnDestroy()
        {
#if ENABLE_UPSCALER_FRAMEWORK && ENABLE_NVIDIA && ASHEN_NVIDIA_MODULE
            if(options!=null)Destroy(options);
#endif
        }
    }
}
#endif
