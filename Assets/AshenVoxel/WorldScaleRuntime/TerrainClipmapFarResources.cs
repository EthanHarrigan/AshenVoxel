#if UNITY_5_3_OR_NEWER
using System;
using AshenVoxel.WorldScale;
using UnityEngine;
using UnityEngine.Rendering;

namespace AshenVoxel.WorldScaleRuntime
{
    public sealed partial class TerrainClipmapResources
    {
        RenderTexture farTint,farAppearance;
        FarAppearanceWindow farActive,farCandidate;
        int farSize,farKernel,farActiveBank,farCandidateBank,farExpected,farUploaded;
        bool farPublished,farUpdating;
        float farBlend;
        public bool FarAppearancePublished=>farPublished;
        public float FarAppearanceBlend=>farBlend;
        public bool CanPublishFarAppearance=>farUpdating&&farUploaded==farExpected&&farBlend<=0;
        // Two RGBA8 channels, two banks. Upload storage is the primary clipmap's
        // existing 4-MiB GPU buffer/managed array; there is no extra upload buffer.
        public long FarAppearanceGpuBytes=>(long)farSize*farSize*16;
        public static long EstimateFarAppearanceGpuBytes(int texels)=>checked((long)texels*texels*16);
        static int FarModulo(long value,int modulus){int n=(int)(value%modulus);return n<0?n+modulus:n;}
        static bool SameFarWindow(FarAppearanceWindow a,FarAppearanceWindow b)
            =>a.OriginX==b.OriginX&&a.OriginZ==b.OriginZ&&a.Size==b.Size&&a.Spacing==b.Spacing&&a.Generation==b.Generation;

        RenderTexture CreateFarArray(string name,int texels)
        {
            var texture=new RenderTexture(texels,texels,0,RenderTextureFormat.ARGB32,RenderTextureReadWrite.Linear)
            {
                name=name,dimension=TextureDimension.Tex2DArray,volumeDepth=2,enableRandomWrite=true,
                wrapMode=TextureWrapMode.Repeat,filterMode=FilterMode.Bilinear,useMipMap=false,
                autoGenerateMips=false,hideFlags=HideFlags.DontSave
            };
            if(!texture.Create()){Destroy(texture);throw new InvalidOperationException("Could not allocate "+name);}
            return texture;
        }
        void EnsureFarAppearance(int texels)
        {
            if(disposed)throw new ObjectDisposedException(nameof(TerrainClipmapResources));
            // Fast/High only. A 1536-square Ultra overlay would add 36 MiB and is
            // outside the explicitly bounded appearance improvement.
            if(texels!=384&&texels!=768)throw new ArgumentOutOfRangeException(nameof(texels));
            if(farSize!=0){if(farSize!=texels)throw new InvalidOperationException("Far appearance size is fixed for this renderer");return;}
            try
            {
                farTint=CreateFarArray("Far terrain tint (sRGB bytes)",texels);
                farAppearance=CreateFarArray("Far terrain appearance",texels);
                farKernel=scatter.FindKernel("ScatterAppearance");
                scatter.SetBuffer(farKernel,"_Updates",updateBuffer);
                scatter.SetTexture(farKernel,"_FarTint",farTint);scatter.SetTexture(farKernel,"_FarAppearance",farAppearance);
                farSize=texels;BindFarAppearanceTextures();
                if(Material.GetTexture("_CmFarTint")!=farTint||Material.GetTexture("_CmFarAppearance")!=farAppearance)
                    throw new InvalidOperationException("Far appearance texture bindings were not retained");
            }
            catch{DisposeFarAppearance();throw;}
        }
        void BindFarAppearanceTextures()
        {
            if(Material==null||farTint==null)return;
            Material.SetTexture("_CmFarTint",farTint);Material.SetTexture("_CmFarAppearance",farAppearance);
        }
        public void BeginFarAppearance(FarAppearanceWindow window,bool copyPublished,int expectedSamples)
        {
            EnsureFarAppearance(window.Size);
            if(farUpdating)throw new InvalidOperationException("A far appearance upload is already pending");
            if(double.IsNaN(window.Spacing)||double.IsInfinity(window.Spacing)||window.Spacing<=0||
                expectedSamples<0||expectedSamples>farSize*farSize)throw new ArgumentOutOfRangeException(nameof(window));
            if(copyPublished&&(!farPublished||farActive.Spacing!=window.Spacing||farActive.Size!=window.Size))
                throw new InvalidOperationException("Far appearance copy needs matching published spacing and size");
            if(!copyPublished&&expectedSamples!=farSize*farSize)
                throw new InvalidOperationException("A new far appearance window requires a full upload");
            farCandidate=window;farCandidateBank=1-farActiveBank;farExpected=expectedSamples;farUploaded=0;
            if(copyPublished)
            {
                Graphics.CopyTexture(farTint,farActiveBank,0,farTint,farCandidateBank,0);
                Graphics.CopyTexture(farAppearance,farActiveBank,0,farAppearance,farCandidateBank,0);
            }
            farUpdating=true;
        }
        public void UploadFarAppearance(FarAppearanceWindow window,ClipmapRect region,FarAppearanceSample[] samples)
        {
            if(!farUpdating||!SameFarWindow(window,farCandidate))throw new InvalidOperationException("Stale far appearance upload");
            if(samples==null||samples.Length!=farSize*farSize)throw new ArgumentException("Wrong far appearance mirror size",nameof(samples));
            long dx=region.X-window.OriginX,dz=region.Z-window.OriginZ;
            if(region.Width<=0||region.Height<=0||dx<0||dz<0||dx+region.Width>farSize||dz+region.Height>farSize)
                throw new ArgumentOutOfRangeException(nameof(region));
            long count=(long)region.Width*region.Height;
            if(farUploaded+count>farExpected)throw new InvalidOperationException("Far appearance upload exceeded its transaction");
            int n=0;
            for(int z=0;z<region.Height;z++)for(int x=0;x<region.Width;x++)
            {
                int u=FarModulo(region.X+x,farSize),v=FarModulo(region.Z+z,farSize);var value=samples[u+v*farSize];
                updates[n++]=new Update{Texel=(uint)(u|v<<16),Height=0,Tint=value.Tint,Appearance=value.Appearance};
                if(n==UploadCapacity){DispatchFarAppearance(n);n=0;}
            }
            if(n!=0)DispatchFarAppearance(n);farUploaded+=(int)count;
        }
        void DispatchFarAppearance(int count)
        {
            updateBuffer.SetData(updates,0,0,count);scatter.SetInt("_Count",count);scatter.SetInt("_Level",farCandidateBank);
            scatter.Dispatch(farKernel,(count+63)/64,1,1);
        }
        public void AdvanceFarAppearance(double seconds)
        {
            if(disposed)return;
            if(double.IsNaN(seconds)||double.IsInfinity(seconds)||seconds<0)throw new ArgumentOutOfRangeException(nameof(seconds));
            // Only one bank is sampled. Once the inactive bank is complete, fade
            // through the existing coarse appearance, then permit an atomic swap.
            bool fadeOut=farUpdating&&farUploaded==farExpected;
            double step=Math.Min(seconds,.1)/(fadeOut?.3:.4);
            farBlend=(float)(fadeOut?Math.Max(0,farBlend-step):farPublished?Math.Min(1,farBlend+step):0);
        }
        public void PublishFarAppearance(FarAppearanceWindow window)
        {
            if(!CanPublishFarAppearance||!SameFarWindow(window,farCandidate))
                throw new InvalidOperationException("Far appearance must be completely uploaded and faded out before publication");
            farActive=window;farActiveBank=farCandidateBank;farPublished=true;farUpdating=false;
            farExpected=farUploaded=0;farBlend=0;
        }
        public void CancelFarAppearance(){farUpdating=false;farExpected=farUploaded=0;}
        public void DisableFarAppearance(){CancelFarAppearance();DisposeFarAppearance();}
        void BindFarAppearanceView(WorldPoint renderOrigin)
        {
            if(!farPublished){Material.SetVector("_CmFarState",Vector4.zero);return;}
            Material.SetVector("_CmFarWindow",new Vector4((float)(farActive.OriginX*farActive.Spacing-renderOrigin.X),
                (float)(farActive.OriginZ*farActive.Spacing-renderOrigin.Z),(float)farActive.Spacing,farSize));
            Material.SetVector("_CmFarState",new Vector4(FarModulo(farActive.OriginX,farSize),FarModulo(farActive.OriginZ,farSize),farActiveBank,farBlend));
        }
        void DisposeFarAppearance()
        {
            if(Material!=null)Material.SetVector("_CmFarState",Vector4.zero);
            Release(farTint);Release(farAppearance);farTint=farAppearance=null;
            farPublished=farUpdating=false;farBlend=0;farSize=0;
        }
    }
}
#endif
