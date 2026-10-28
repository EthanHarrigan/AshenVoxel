using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace AshenVoxel.Exploration
{
    /// <summary>Owned camera-enclosing cloud draw, composited after water against scene depth.</summary>
    public sealed class ExplorationCloudVolume : IDisposable
    {
        public const int Layer=29;
        readonly Camera camera;
        readonly UniversalAdditionalCameraData data;
        readonly CameraOverrideOption previousDepth;
        readonly GameObject root;
        readonly Mesh mesh;
        readonly MeshRenderer renderer;
        readonly Material material;
        Material sky;
        bool active,disposed;
        public bool Available=>material!=null;
        public ExplorationCloudVolume(Camera camera)
        {
            this.camera=camera;
            var shader=Resources.Load<Shader>("ExplorationCloudComposite");
            if(camera==null||shader==null)return;
            data=camera.GetUniversalAdditionalCameraData();previousDepth=data.requiresDepthOption;
            data.requiresDepthTexture=true;
            material=new Material(shader){name="Exploration cloud volume (owned)",hideFlags=HideFlags.HideAndDontSave};
            mesh=new Mesh{name="Cloud camera enclosure",hideFlags=HideFlags.HideAndDontSave};
            mesh.vertices=new[]{new Vector3(-1,-1,-1),new Vector3(1,-1,-1),new Vector3(1,1,-1),new Vector3(-1,1,-1),
                new Vector3(-1,-1,1),new Vector3(1,-1,1),new Vector3(1,1,1),new Vector3(-1,1,1)};
            mesh.triangles=new[]{0,2,1,0,3,2,4,5,6,4,6,7,0,1,5,0,5,4,3,7,6,3,6,2,0,4,7,0,7,3,1,2,6,1,6,5};
            mesh.RecalculateBounds();
            root=new GameObject("Exploration cloud volume"){layer=Layer,hideFlags=HideFlags.HideAndDontSave};
            root.AddComponent<MeshFilter>().sharedMesh=mesh;
            renderer=root.AddComponent<MeshRenderer>();renderer.sharedMaterial=material;renderer.enabled=false;
            renderer.shadowCastingMode=ShadowCastingMode.Off;renderer.receiveShadows=false;
            renderer.lightProbeUsage=LightProbeUsage.Off;renderer.reflectionProbeUsage=ReflectionProbeUsage.Off;
            renderer.motionVectorGenerationMode=MotionVectorGenerationMode.ForceNoMotion;
            renderer.allowOcclusionWhenDynamic=false;
            RenderPipelineManager.beginCameraRendering+=BeginCamera;
            RenderPipelineManager.endCameraRendering+=EndCamera;
        }
        public void Apply(Material source,bool enabled)
        {
            sky=source;active=enabled&&Available&&camera!=null&&!camera.orthographic&&
                (camera.cullingMask&(1<<Layer))!=0&&OpaqueDepthReady();
            if(source==null)return;
            source.SetFloat("_CloudVolumeExternal",active?1:0);
            source.SetFloat("_CloudDepthReady",active?1:0);
            if(!active)return;
            material.CopyPropertiesFromMaterial(source);
            // CopyProperties also copies the source queue; restore the required order.
            material.renderQueue=(int)RenderQueue.Transparent+100;
        }
        public void SetCameraData(Vector4 value){if(material!=null)material.SetVector("_CloudCameraData",value);}
        static bool OpaqueDepthReady()
        {
            // The ordinary Exploration renderer copies depth after opaques. A
            // camera depth request alone would also allow an after-transparents
            // copy, which is too late for the cloud draw. Fail to sky-only volume
            // if another pipeline/renderer cannot establish this ordering.
            var pipeline=GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            if(pipeline==null||pipeline.rendererDataList.Length==0)return false;
            foreach(var configured in pipeline.rendererDataList)
                if(!(configured is UniversalRendererData urp)||urp.copyDepthMode==CopyDepthMode.AfterTransparents)return false;
            return true;
        }
        void BeginCamera(ScriptableRenderContext context,Camera renderingCamera)
        {
            if(renderer==null)return;
            renderer.enabled=active&&renderingCamera==camera;
            if(sky!=null)sky.SetFloat("_CloudVolumeExternal",renderer.enabled?1:0);
            if(!renderer.enabled)return;
            root.transform.position=camera.transform.position;
            root.transform.rotation=Quaternion.identity;
            root.transform.localScale=Vector3.one*Mathf.Max(2,camera.nearClipPlane*4);
        }
        void EndCamera(ScriptableRenderContext context,Camera renderingCamera)
        {if(renderer!=null)renderer.enabled=false;if(sky!=null)sky.SetFloat("_CloudVolumeExternal",0);}
        public void Dispose()
        {
            if(disposed)return;disposed=true;
            RenderPipelineManager.beginCameraRendering-=BeginCamera;RenderPipelineManager.endCameraRendering-=EndCamera;
            if(sky!=null){sky.SetFloat("_CloudVolumeExternal",0);sky.SetFloat("_CloudDepthReady",0);}
            if(data!=null&&data.requiresDepthOption==CameraOverrideOption.On)data.requiresDepthOption=previousDepth;
            Release(root);Release(mesh);Release(material);
        }
        static void Release(UnityEngine.Object value)
        {if(value==null)return;if(Application.isPlaying)UnityEngine.Object.Destroy(value);else UnityEngine.Object.DestroyImmediate(value);}
    }
}
