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
        public void Initialize(Camera camera,RenderPipelineAsset pipeline)
        {
            var data=camera.GetUniversalAdditionalCameraData();data.renderPostProcessing=true;
            data.antialiasing=AntialiasingMode.SubpixelMorphologicalAntiAliasing;data.antialiasingQuality=AntialiasingQuality.High;
            if(pipeline is UniversalRenderPipelineAsset urp){urp.msaaSampleCount=1;urp.renderScale=1;urp.shadowCascadeCount=4;urp.cascade4Split=new Vector3(.08f,.24f,.55f);urp.cascadeBorder=.15f;}
            profile=ScriptableObject.CreateInstance<VolumeProfile>();profile.name="Exploration visual quality (owned)";
            profile.Add<Tonemapping>(true).mode.value=TonemappingMode.ACES;
            var grade=profile.Add<ColorAdjustments>(true);grade.postExposure.value=.2f;grade.contrast.value=6;grade.saturation.value=3;
            var bloom=profile.Add<Bloom>(true);bloom.threshold.value=1.2f;bloom.intensity.value=.08f;bloom.scatter.value=.55f;
            var volume=gameObject.AddComponent<Volume>();volume.isGlobal=true;volume.priority=20;volume.sharedProfile=profile;
        }
        void OnDestroy(){if(profile!=null){if(Application.isPlaying)Destroy(profile);else DestroyImmediate(profile);}}
    }
}
#endif
