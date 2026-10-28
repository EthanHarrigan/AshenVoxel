using System;
using System.Threading;
using UnityEngine;
using UnityEngine.Rendering;

namespace RootContactUploadPrototype
{
    // Opaque caller-owned admission identity. Bytes are a declared reservation,
    // not a native allocation measurement; the coordinator owns the actual ledger.
    public sealed class UploadReservation
    {
        public readonly string Identity;public readonly long Bytes;
        public UploadReservation(string identity,long bytes)
        {if(string.IsNullOrEmpty(identity)||identity.Length>4096||bytes<1)throw new ArgumentException("reservation");Identity=identity;Bytes=bytes;}
    }

    // Returned only when failed upload allocated a native Mesh. Caller MUST retain
    // this token in its runtime-owned retirement queue and keep the reservation charged.
    // Neither returning the token nor requesting Destroy certifies native release.
    public sealed class MeshRetirement
    {
        readonly int mainThreadId;Mesh mesh;
        public readonly string PreparedIdentity;public readonly UploadReservation Reservation;
        public readonly long ElementPayloadBytes;
        public string UploadFailure{get;private set;}="";
        public string LastRetirementFailure{get;private set;}="";
        public bool DestroyRequested{get;private set;}
        public bool Retired{get;private set;}
        // Reference access is managed only. Any Unity operation on it is main-thread-only.
        public Mesh Resource=>mesh;
        internal MeshRetirement(int thread,PreparedMesh prepared,UploadReservation reservation)
        {mainThreadId=thread;PreparedIdentity=prepared.Identity;ElementPayloadBytes=prepared.GpuPayloadBytes;Reservation=reservation;}
        internal void Attach(Mesh resource){mesh=resource;}
        internal void Failed(string failure){UploadFailure=failure;}
        internal void Transfer(){mesh=null;}
        bool ThreadAllowed(out string failure)
        {failure="";if(Thread.CurrentThread.ManagedThreadId==mainThreadId)return true;failure="wrong-thread";return false;}
        public bool TryRequestDestroy(out string failure)
        {
            if(!ThreadAllowed(out failure))return false;
            if(Retired||DestroyRequested)return true;
            try
            {
                UnityEngine.Object.Destroy(mesh);DestroyRequested=true;LastRetirementFailure="";return true;
            }
            catch(Exception e){failure=e.GetType().Name+":"+e.Message;LastRetirementFailure=failure;return false;}
        }
        public bool TryConfirmRetired(out bool retired,out string failure)
        {
            retired=false;if(!ThreadAllowed(out failure))return false;
            try
            {
                // Unity's destroyed-object comparison must run on the actual owner thread.
                // Only this observed postcondition permits the caller to release its ledger entry.
                if(!Retired&&mesh==null){Retired=true;mesh=null;}
                retired=Retired;return true;
            }
            catch(Exception e){failure=e.GetType().Name+":"+e.Message;LastRetirementFailure=failure;return false;}
        }
    }

    public sealed class UnityMeshUpload
    {
        readonly int mainThreadId;
        // Capture this ID in the actual runtime coordinator Awake/Start; do not invent
        // it on a worker or carry an ID across a domain/session lifecycle.
        public UnityMeshUpload(int coordinatorMainThreadId)
        {if(coordinatorMainThreadId<=0)throw new ArgumentException("main-thread-id");mainThreadId=coordinatorMainThreadId;}
        public bool TryCreate(PreparedMesh prepared,string name,float windPaddingMeters,Func<bool> isCurrent,
            UploadReservation reservation,out Mesh complete,out MeshRetirement retirement,out string failure)
        {
            complete=null;retirement=null;failure="";
            if(Thread.CurrentThread.ManagedThreadId!=mainThreadId){failure="wrong-thread";return false;}
            Mesh mesh=null;MeshRetirement ownership=null;
            try
            {
                if(prepared==null||isCurrent==null||reservation==null||reservation.Bytes<prepared.GpuPayloadBytes||string.IsNullOrEmpty(name)||name.Length>128||float.IsNaN(windPaddingMeters)||float.IsInfinity(windPaddingMeters)||windPaddingMeters<0||windPaddingMeters>16)
                    throw new ArgumentException("upload-input-or-reservation");
                if(!isCurrent()){failure="stale-before-preparation";return false;}
                var vertices=new Vector3[prepared.VertexCount];var normals=new Vector3[vertices.Length];var colors=new Color32[vertices.Length];var indices=new int[prepared.PartCount][];
                for(int i=0;i<vertices.Length;i++){var p=prepared.Positions[i];var n=prepared.Normals[i];var c=prepared.Colors[i];vertices[i]=new Vector3(p.x,p.y,p.z);normals[i]=new Vector3(n.x,n.y,n.z);colors[i]=new Color32(c.r,c.g,c.b,c.a);}
                for(int part=0;part<indices.Length;part++)indices[part]=prepared.Indices(part).ToArray();
                var min=prepared.Bounds.Minimum;var max=prepared.Bounds.Maximum;
                var bounds=new Bounds(new Vector3((min.x+max.x)*.5f,(min.y+max.y)*.5f,(min.z+max.z)*.5f),new Vector3(max.x-min.x,max.y-min.y,max.z-min.z));bounds.Expand(windPaddingMeters*2);
                // Preallocate ownership metadata before native construction, so failure
                // cleanup never needs to allocate a token after native Mesh creation.
                ownership=new MeshRetirement(mainThreadId,prepared,reservation);
                if(!isCurrent()){failure="stale-before-native";return false;}
                mesh=new Mesh();ownership.Attach(mesh);
                mesh.name=name;mesh.indexFormat=IndexFormat.UInt32;
                mesh.vertices=vertices;mesh.normals=normals;mesh.colors32=colors;mesh.subMeshCount=indices.Length;
                for(int part=0;part<indices.Length;part++)mesh.SetTriangles(indices[part],part,false);
                mesh.bounds=bounds;mesh.UploadMeshData(true);
                if(!isCurrent()){failure="stale-after-upload";return false;}
                complete=mesh;mesh=null;ownership.Transfer();return true;
            }
            catch(Exception e){failure=e.GetType().Name+":"+e.Message;return false;}
            finally
            {
                // No Unity call or potentially failing Destroy here. A failed allocated
                // resource remains observable and charged until explicit retirement.
                if(!ReferenceEquals(mesh,null)){ownership.Failed(failure);retirement=ownership;}
            }
        }
    }
}
