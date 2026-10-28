using System;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace AshenVoxel.Exploration
{
    /// <summary>One owned, immutable 64-cubed linear noise texture. No per-frame generation.</summary>
    public sealed class ExplorationCloudNoise : IDisposable
    {
        public const int Size=64,ByteCount=Size*Size*Size*4;
        Texture3D texture;
        bool attempted;
        public Texture3D Texture
        {
            get
            {
                if(attempted)return texture;
                attempted=true;
                if(!SystemInfo.supports3DTextures)return null;
                var asset=Resources.Load<TextAsset>("ExplorationCloudNoise");
                if(asset==null){Debug.LogWarning("Cloud noise is unavailable; using layered clouds.");return null;}
                try
                {
                    byte[] data=asset.bytes;
                    if(data.Length!=ByteCount)throw new InvalidOperationException("Expected 64 cubed RGBA8 cloud noise.");
                    texture=new Texture3D(Size,Size,Size,GraphicsFormat.R8G8B8A8_UNorm,TextureCreationFlags.MipChain)
                    {
                        name="Exploration cloud noise (owned)",hideFlags=HideFlags.HideAndDontSave,
                        wrapMode=TextureWrapMode.Repeat,filterMode=FilterMode.Trilinear,anisoLevel=1
                    };
                    texture.SetPixelData(data,0);
                    texture.Apply(true,true);
                }
                catch(Exception e)
                {
                    ReleaseTexture();Debug.LogWarning("Cloud noise failed; using layered clouds. "+e.Message);
                }
                finally{Resources.UnloadAsset(asset);}
                return texture;
            }
        }
        void ReleaseTexture()
        {
            if(texture==null)return;
            if(Application.isPlaying)UnityEngine.Object.Destroy(texture);else UnityEngine.Object.DestroyImmediate(texture);
            texture=null;
        }
        public void Dispose(){ReleaseTexture();attempted=true;}
    }
}
