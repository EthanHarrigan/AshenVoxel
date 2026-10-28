using UnityEngine;
namespace AshenVoxel.Exploration
{
    public sealed partial class VegetationStreaming
    {
        /// <summary>Borrow one already-loaded silhouette-preserving rock LOD. The caller may draw it,
        /// but must never mutate/destroy it or retain it across population teardown.</summary>
        public bool TryBorrowCoarseRock(int prototypeId,out Mesh mesh,out Material sharedMaterial,out Bounds geometryBounds,out int triangles)
        {
            mesh=null;sharedMaterial=null;geometryBounds=default;triangles=0;
            if(!Ready||!models.TryGetValue(prototypeId,out var model)||model.Data.Kind!=2||model.Meshes.Length==0)return false;
            int lod=System.Math.Min(1,model.Meshes.Length-1);mesh=model.Meshes[lod];if(mesh==null||material==null)return false;
            geometryBounds=mesh.bounds;
#if UNITY_5_3_OR_NEWER
            // Ordinary Load pads all rock draw bounds for wind too. Recover the
            // actual geometry bounds; padded bounds cannot seat a rock.
            geometryBounds.Expand(-ExplorationWind.MaximumDisplacementMeters*2);
#endif
            if(geometryBounds.size.x<=0||geometryBounds.size.y<=0||geometryBounds.size.z<=0){mesh=null;return false;}
            sharedMaterial=material;triangles=model.Triangles[lod];return true;
        }
    }
}
