using System;
using System.Threading;
using UnityEngine;
using UnityEngine.Rendering;

namespace RootContactUploadPrototype
{
    public sealed class UnityMeshUpload
    {
        readonly int mainThreadId;
        // Capture Thread.CurrentThread.ManagedThreadId in the actual coordinator Awake/Start.
        // An arbitrary worker-created value is NOT proof that it is Unity's main thread.
        public UnityMeshUpload(int coordinatorMainThreadId)
        {if(coordinatorMainThreadId<=0)throw new ArgumentException("main-thread-id");mainThreadId=coordinatorMainThreadId;}
        public bool TryCreate(PreparedMesh prepared,string name,float windPaddingMeters,Func<bool> isCurrent,out Mesh complete,out string failure)
        {
            complete=null;failure="";
            if(Thread.CurrentThread.ManagedThreadId!=mainThreadId){failure="wrong-thread";return false;}
            Mesh mesh=null;
            try
            {
                if(prepared==null||isCurrent==null||string.IsNullOrEmpty(name)||name.Length>128||float.IsNaN(windPaddingMeters)||float.IsInfinity(windPaddingMeters)||windPaddingMeters<0||windPaddingMeters>16)throw new ArgumentException("upload-input");
                if(!isCurrent()){failure="stale-before-preparation";return false;}
                // All Unity-compatible arrays exist before any native Mesh is constructed/mutated.
                var vertices=new Vector3[prepared.VertexCount];var normals=new Vector3[vertices.Length];var colors=new Color32[vertices.Length];
                var indices=new int[prepared.PartCount][];
                for(int i=0;i<vertices.Length;i++){var p=prepared.Positions[i];var n=prepared.Normals[i];var c=prepared.Colors[i];vertices[i]=new Vector3(p.x,p.y,p.z);normals[i]=new Vector3(n.x,n.y,n.z);colors[i]=new Color32(c.r,c.g,c.b,c.a);}
                for(int part=0;part<indices.Length;part++)indices[part]=prepared.Indices(part).ToArray();
                var min=prepared.Bounds.Minimum;var max=prepared.Bounds.Maximum;
                var bounds=new Bounds(new Vector3((min.x+max.x)*.5f,(min.y+max.y)*.5f,(min.z+max.z)*.5f),new Vector3(max.x-min.x,max.y-min.y,max.z-min.z));
                bounds.Expand(windPaddingMeters*2);
                if(!isCurrent()){failure="stale-before-native";return false;}
                mesh=new Mesh{name=name,indexFormat=IndexFormat.UInt32};
                mesh.vertices=vertices;mesh.normals=normals;mesh.colors32=colors;mesh.subMeshCount=indices.Length;
                for(int part=0;part<indices.Length;part++)mesh.SetTriangles(indices[part],part,false);
                mesh.bounds=bounds;mesh.UploadMeshData(true);
                if(!isCurrent()){failure="stale-after-upload";return false;}
                complete=mesh;mesh=null;return true; // Caller owns this unpublished object and its byte reservation.
            }
            catch(Exception e){failure=e.GetType().Name;return false;}
            finally
            {
                // Runtime Destroy is deferred. Keep the failed native reservation charged until retirement.
                // No existing/shared Mesh is ever passed in or changed, and neither submesh is disabled.
                if(mesh!=null)UnityEngine.Object.Destroy(mesh);
            }
        }
    }
}
