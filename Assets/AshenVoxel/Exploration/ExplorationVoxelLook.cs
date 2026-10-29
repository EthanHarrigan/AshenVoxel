using UnityEngine;
namespace AshenVoxel.Exploration
{
    /// <summary>Publishes the GroundSurface "voxel look at every distance" globals: block size per
    /// pixel, strength on distant mountains, stepped silhouettes and world-grid pigment.
    /// Called with the other live terrain shading options (twice a second while playing).</summary>
    public static class ExplorationVoxelLook
    {
        public const float LargestBlockMeters=12.8f,LargestSilhouetteStepMeters=3.2f,SilhouetteGrazingBand=.35f;
        public static void Apply(ExplorationSettings s,bool virtualStepsOn,Camera camera)
        {
            bool look=virtualStepsOn&&s!=null&&s.VoxelLookAllDistances;
            float pixels=s!=null?Mathf.Clamp(s.VoxelBlockPixels,1,4):2,far=s!=null?Mathf.Clamp01(s.VoxelLookFarStrength):.45f;
            Shader.SetGlobalVector("_AshenVoxelLook",new Vector4(look?1:0,pixels,far,LargestBlockMeters));
            Shader.SetGlobalVector("_AshenVoxelLook2",new Vector4(look&&s.VoxelSilhouettes?1:0,LargestSilhouetteStepMeters,SilhouetteGrazingBand,s!=null?Mathf.Clamp01(s.VoxelWorldGridPigment):1));
            float height=camera!=null?Mathf.Max(1,camera.pixelHeight):1080,fov=camera!=null?camera.fieldOfView:75;
            Shader.SetGlobalVector("_AshenVoxelPixel",new Vector4(2*Mathf.Tan(fov*Mathf.Deg2Rad*.5f)/height,0,0,0));
        }
    }
}
