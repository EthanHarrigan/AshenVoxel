using System;
using System.Collections.Generic;
using AshenVoxel.WorldScale;
using UnityEngine;

namespace AshenVoxel.Exploration
{
    public static class ForestBillboard
    {
        public static float Span(ExplorationPrototype data)=>Math.Max(data.MaxY-data.MinY,data.Radius*2)*1.08f;
        public static Mesh Create(ExplorationPrototype data)
        {
            float center=(data.MinY+data.MaxY)*.5f,half=Span(data)*.5f;
            var mesh=new Mesh{name=data.Name+" silhouette impostor"};
            mesh.vertices=new[]{new Vector3(-half,center-half,0),new Vector3(half,center-half,0),new Vector3(half,center+half,0),new Vector3(-half,center+half,0)};
            mesh.normals=new[]{Vector3.forward,Vector3.forward,Vector3.forward,Vector3.forward};
            mesh.SetUVs(0,new List<Vector4>{new Vector4(0,0,data.Id,center),new Vector4(1,0,data.Id,center),new Vector4(1,1,data.Id,center),new Vector4(0,1,data.Id,center)});
            mesh.SetTriangles(new[]{0,1,2,0,2,3},0,true);
            // Shader rotates the square about its center, including overhead views.
            mesh.bounds=new Bounds(new Vector3(0,center,0),Vector3.one*(Span(data)*1.415f));mesh.UploadMeshData(true);return mesh;
        }
    }
}
