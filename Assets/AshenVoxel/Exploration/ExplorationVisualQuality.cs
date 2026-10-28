#if UNITY_5_3_OR_NEWER
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
namespace AshenVoxel.Exploration
{
    /// <summary>Owned camera/post settings; source pipeline and shared profiles stay intact.</summary>
    public sealed class ExplorationVisualQuality:MonoBehaviour
    {
        VolumeProfile profile;
        Volume volume;
        UniversalAdditionalCameraData cameraData;
        Tonemapping tone;
        ColorAdjustments grade;
        WhiteBalance whiteBalance;
        Bloom bloom;
        Vignette vignette;
        bool previousPost;
        AntialiasingMode previousAA;
        AntialiasingQuality previousAAQuality;
        public void Initialize(Camera camera,RenderPipelineAsset pipeline)
        {
            if(profile!=null)return;
            var data=camera.GetUniversalAdditionalCameraData();cameraData=data;
            previousPost=data.renderPostProcessing;previousAA=data.antialiasing;previousAAQuality=data.antialiasingQuality;
            data.renderPostProcessing=true;
            data.antialiasing=AntialiasingMode.SubpixelMorphologicalAntiAliasing;data.antialiasingQuality=AntialiasingQuality.High;
            if(pipeline is UniversalRenderPipelineAsset urp){urp.msaaSampleCount=1;urp.renderScale=1;urp.shadowCascadeCount=4;urp.cascade4Split=new Vector3(.08f,.24f,.55f);urp.cascadeBorder=.15f;}
            profile=ScriptableObject.CreateInstance<VolumeProfile>();profile.name="Exploration visual quality (owned)";
            tone=profile.Add<Tonemapping>(true);grade=profile.Add<ColorAdjustments>(true);
            whiteBalance=profile.Add<WhiteBalance>(true);bloom=profile.Add<Bloom>(true);vignette=profile.Add<Vignette>(true);
            bloom.highQualityFiltering.value=true;
            volume=gameObject.AddComponent<Volume>();volume.isGlobal=true;volume.priority=20;volume.sharedProfile=profile;
            Apply(new ExplorationAtmosphere());
        }
        public void Apply(ExplorationAtmosphere settings)
        {
            if(profile==null||settings==null)return;
            // Keep the camera post stage alive for SMAA/DLAA. Weight disables only our grading.
            if(cameraData!=null)cameraData.renderPostProcessing=true;
            volume.weight=settings.PostProcessing?1:0;
            // Antialiasing belongs to ExplorationUpscaling after Initialize; never overwrite it here.
            tone.mode.value=settings.ToneMapping==ExplorationToneMapping.ACES?TonemappingMode.ACES:
                settings.ToneMapping==ExplorationToneMapping.Neutral?TonemappingMode.Neutral:TonemappingMode.None;
            grade.postExposure.value=settings.PostExposure;grade.contrast.value=settings.Contrast;
            grade.saturation.value=settings.Saturation;grade.colorFilter.value=settings.ColorFilter;
            whiteBalance.temperature.value=settings.Temperature;whiteBalance.tint.value=settings.Tint;
            bloom.intensity.value=settings.BloomIntensity;bloom.threshold.value=settings.BloomThreshold;bloom.scatter.value=settings.BloomScatter;
            vignette.intensity.value=settings.Vignette;vignette.smoothness.value=.55f;
        }
        void OnDestroy()
        {
            if(cameraData!=null){cameraData.renderPostProcessing=previousPost;cameraData.antialiasing=previousAA;cameraData.antialiasingQuality=previousAAQuality;}
            if(volume!=null){volume.sharedProfile=null;Release(volume);}
            if(profile==null)return;
            foreach(var component in profile.components)if(component!=null)Release(component);
            Release(profile);profile=null;
        }
        static void Release(Object value){if(Application.isPlaying)Destroy(value);else DestroyImmediate(value);}
    }
}
#endif
