using System;
using System.Collections.Generic;
using AshenVoxel.Exploration.Experimental;
using UnityEngine;

namespace AshenVoxel.Exploration
{
    public sealed partial class VegetationStreaming
    {
        // Borrow the actual published mesh/LOD/root parts. The matter renderer
        // never owns or destroys these shared caches, and the lease pins roots.
        public bool TryGetNaturalMatterRenderSource(ShrubLease lease,out NaturalMatterRenderSource source)
        {
            source=null;if(!ShrubCurrent(lease)||lease.Active)return false;
            var position=lease.Instance.Position;
            float x=(float)(position.X-origin.X),y=(float)(position.Y-origin.Y),z=(float)(position.Z-origin.Z);
            var parts=new List<NaturalMatterRenderSource.Part>(2);
            foreach(var batch in batches)
            {
                if(batch.ModelId!=lease.Instance.PrototypeId)continue;
                // Billboard parts have different shading/geometry contracts and
                // cannot serve as a nearby, volumetric matter surface.
                if(batch.ModelId<100&&batch.Lod>=3&&impostorMaterial!=null)continue;
                for(int i=0;i<batch.Count;i++)
                {
                    var matrix=batch.Matrices[i];
                    if(Math.Abs(matrix.m03-x)>.001f||Math.Abs(matrix.m13-y)>.001f||Math.Abs(matrix.m23-z)>.001f)continue;
                    parts.Add(new NaturalMatterRenderSource.Part(batch.Mesh,batch.Submesh,batch.Shadows));break;
                }
            }
            if(parts.Count==0)return false;
            source=new NaturalMatterRenderSource(parts.ToArray(),material);return true;
        }
    }
}
