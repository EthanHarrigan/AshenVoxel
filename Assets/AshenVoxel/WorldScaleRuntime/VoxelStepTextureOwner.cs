using UnityEngine;

namespace AshenVoxel.WorldScaleRuntime
{
    /// <summary>Owns the per-tile colour/height textures of one merged voxel-step terrain tile.
    /// Every terrain release path destroys the tile GameObject, so destroying the textures
    /// here keeps them in lockstep with the mesh without touching each release site.</summary>
    [DisallowMultipleComponent]
    public sealed class VoxelStepTextureOwner : MonoBehaviour
    {
        public Texture2D Colors;
        public Texture2D Heights;
        public long TextureBytes;
        void OnDestroy()
        {
            if(Colors!=null)Destroy(Colors);
            if(Heights!=null)Destroy(Heights);
            Colors=null;Heights=null;
        }
    }
}
