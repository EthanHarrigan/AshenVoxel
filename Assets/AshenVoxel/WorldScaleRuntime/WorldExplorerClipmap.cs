using System;
using AshenVoxel.WorldScale;
using UnityEngine;

namespace AshenVoxel.WorldScaleRuntime
{
    public sealed partial class WorldExplorer
    {
        public bool DeferLegacyTerrain{get;set;}
        public TerrainClipmap ClipmapRenderer{get;private set;}
        public bool UsingClipmap=>ClipmapRenderer!=null&&!ClipmapRenderer.Failed;
        internal SurfaceMaterialSampler CaptureClipmapSampler()
            =>new SurfaceMaterialSampler(CaptureField(BareField(field)),CurrentSurfaceProfile);
        internal void AttachClipmap(TerrainClipmap renderer)
        {
            if(renderer==null||field==null)throw new InvalidOperationException("Configure the terrain source before its clipmap");
            renderer.CopySurfaceStyle(ownedMaterial);
            renderer.Material.SetFloat("_VoxelSilhouettes",0);
            renderer.Material.SetVector("_VoxelTerrainGrid",Vector4.zero);
            // Publish the replacement only after its resource bindings validate.
            // A failed attach must leave Legacy available to the startup fallback.
            DeferLegacyTerrain=false;ClipmapRenderer=renderer;revision++;planJob=null;ClearTerrain();coarseStartup=false;
        }
        internal void DetachClipmap(TerrainClipmap renderer)
        {if(ClipmapRenderer!=renderer)return;ClipmapRenderer=null;coarseStartup=true;ForceReplan();RequestPlan();}
    }
}
