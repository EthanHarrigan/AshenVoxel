using System;
using System.Collections.Generic;
using AshenVoxel.Core;
using UnityEngine;
using UnityEngine.Rendering;

namespace AshenVoxel.Core.Experimental
{
    /// <summary>Explicit finite-arena adapter: ONE Rigidbody and one owned mesh
    /// for an aggregate, four conservative slab colliders, no per-voxel objects.
    /// API-compiled only until a native scene verifies rotation/impact/clearance.
    /// Caller retains the core ledger when disabling/removing this visual actor.</summary>
    public sealed class FallingAggregateActor:MonoBehaviour
    {
        public Vector3 InitialAngularVelocity=new Vector3(0,0,1.5f);
        public float ImpactSpeed=1;
        public int FragmentCandidateBudget=32;
        public FallingAggregate Aggregate {get;private set;}
        public bool Armed {get;private set;}
        public int RenderedFaces {get;private set;}
        Rigidbody body;Mesh mesh;MeshFilter filter;MeshRenderer render;
        readonly BoxCollider[] proxies=new BoxCollider[4];
        readonly List<Vector3> vertices=new List<Vector3>();
        readonly List<Vector3> normals=new List<Vector3>();
        readonly List<Color32> colors=new List<Color32>();
        readonly List<int> indices=new List<int>();
        int meshVersion=-1;
        readonly Bounds[] sliceBounds=new Bounds[4];
        readonly bool[] slicePresent=new bool[4];
        bool[] hardBody;
        bool hasHardProxy;
        double originX,originY,originZ;
        public void Bind(FallingAggregate aggregate,Material borrowedMaterial,Func<AggregateVoxel,bool> authoritativeHardBody,
            double renderOriginX,double renderOriginY,double renderOriginZ)
        {
            if(Aggregate!=null||aggregate==null||borrowedMaterial==null||authoritativeHardBody==null)throw new InvalidOperationException("Bind one admitted aggregate once with its authoritative hard-body classifier.");
            Finite(renderOriginX);Finite(renderOriginY);Finite(renderOriginZ);
            var local=Local(aggregate.OriginX-renderOriginX,aggregate.OriginY-renderOriginY,aggregate.OriginZ-renderOriginZ);
            hardBody=new bool[aggregate.OriginalCount];
            for(int i=0;i<hardBody.Length;i++)hardBody[i]=authoritativeHardBody(aggregate.GetVoxel(i));
            originX=renderOriginX;originY=renderOriginY;originZ=renderOriginZ;
            Aggregate=aggregate;
            transform.SetPositionAndRotation(local,Quaternion.identity);
            filter=gameObject.AddComponent<MeshFilter>();render=gameObject.AddComponent<MeshRenderer>();render.sharedMaterial=borrowedMaterial;
            mesh=new Mesh{name="Bounded detached matter",indexFormat=IndexFormat.UInt32};mesh.MarkDynamic();filter.sharedMesh=mesh;
            int capacity=aggregate.OriginalCount*24;vertices.Capacity=normals.Capacity=colors.Capacity=capacity;indices.Capacity=aggregate.OriginalCount*36;
            body=gameObject.AddComponent<Rigidbody>();body.isKinematic=true;body.useGravity=true;
            body.mass=Mathf.Clamp((float)(aggregate.MassCue*1000),.01f,100000); // Approximate catalogue density cue, not engineering calibration.
            body.interpolation=RigidbodyInterpolation.Interpolate;body.collisionDetectionMode=CollisionDetectionMode.ContinuousDynamic;
            for(int i=0;i<proxies.Length;i++)proxies[i]=gameObject.AddComponent<BoxCollider>();
            RebuildVisualAndProxy();
        }
        // Host MUST acknowledge removal from static collision cache. Dirty render
        // meshes are not authority, and activating against old tree colliders
        // would eject the body. No automatic assumption based on elapsed frames.
        public bool ArmAfterStaticCollisionRevision(long publishedRevision)
        {
            if(Aggregate==null||publishedRevision<Aggregate.RequiredStaticRevision)return false;
            if(Armed||Aggregate.Remaining==0||Aggregate.ImpactReported||!isActiveAndEnabled||!hasHardProxy)return false;
            Armed=true;body.isKinematic=false;body.angularVelocity=InitialAngularVelocity;return true;
        }
        void CopyPose()
        {var p=transform.position;var q=transform.rotation;Aggregate.SetPose(p.x+originX,p.y+originY,p.z+originZ,q.x,q.y,q.z,q.w);}
        static void Finite(double value){if(double.IsNaN(value)||double.IsInfinity(value))throw new ArgumentOutOfRangeException(nameof(value));}
        static Vector3 Local(double x,double y,double z)
        {Finite(x);Finite(y);Finite(z);if(Math.Abs(x)>100000||Math.Abs(y)>100000||Math.Abs(z)>100000)throw new InvalidOperationException("Aggregate outside bounded render-origin window");return new Vector3((float)x,(float)y,(float)z);}
        // Host calls in the SAME rebase ordering as terrain/camera, before the
        // next physics step. Global matter pose does not change during a rebase.
        public void SetRenderOrigin(double x,double y,double z)
        {
            Finite(x);Finite(y);Finite(z);if(Aggregate==null)throw new InvalidOperationException("Bind first");
            var p=transform.position;Vector3 next=Local(p.x+originX-x,p.y+originY-y,p.z+originZ-z);
            originX=x;originY=y;originZ=z;body.position=next;transform.position=next;CopyPose();
        }
        public void RequestBreakup()
        {
            if(Aggregate==null||Aggregate.ImpactReported)return;CopyPose();Aggregate.ReportImpact();
            body.linearVelocity=Vector3.zero;body.angularVelocity=Vector3.zero;body.isKinematic=true;
            foreach(var proxy in proxies)proxy.enabled=false;
        }
        void OnCollisionEnter(Collision collision)
        {if(Armed&&collision.relativeVelocity.magnitude>=ImpactSpeed)RequestBreakup();}
        // The single scene owner schedules this, after its collider-cache work.
        public void TickOwnedPhysics()
        {
            if(Aggregate==null)return;CopyPose();
            if(Aggregate.ImpactReported)Aggregate.EmitFragments(Math.Max(0,FragmentCandidateBudget));
            if(meshVersion!=Aggregate.Version)RebuildVisualAndProxy();
            if(Aggregate.Remaining==0){body.isKinematic=true;body.detectCollisions=false;render.enabled=false;}
        }
        public int Suction(double x,double y,double z,double radius,int candidates)
        {
            if(Aggregate==null)return 0;CopyPose();int result=Aggregate.CollectNearby(x,y,z,radius,candidates);
            if(meshVersion!=Aggregate.Version)RebuildVisualAndProxy();return result;
        }
        // Conservative native-proxy occupancy for the host's composed shared
        // query (debris OR aggregate). Slab AABBs overblock concave/rotated empty
        // space; this must be measured visually before any forest adoption.
        public bool OccupiesCell(Int3 cell)
        {
            if(Aggregate==null||Aggregate.Remaining==0||!isActiveAndEnabled)return false;
            float s=Aggregate.CellSize;double x=(cell.x+.5)*(double)s-originX,y=(cell.y+.5)*(double)s-originY,z=(cell.z+.5)*(double)s-originZ;
            if(Math.Abs(x)>100000||Math.Abs(y)>100000||Math.Abs(z)>100000)return false;
            var target=Local(x,y,z);var a=transform.right;var b=transform.up;var c=transform.forward;
            for(int i=0;i<Aggregate.OriginalCount;i++)
            {
                if(!hardBody[i]||!Aggregate.IsRemaining(i))continue;
                var p=Aggregate.GetVoxel(i).Local;
                var center=transform.TransformPoint(new Vector3((p.x+.5f)*s,(p.y+.5f)*s,(p.z+.5f)*s));
                if(BoxesOverlap(center-target,s*.5f,a,b,c))return true;
            }
            return false;
        }
        // Exact cube/rotated-cube SAT. No world-AABB empty-space reservation.
        static bool BoxesOverlap(Vector3 delta,float half,Vector3 a,Vector3 b,Vector3 c)
        {
            bool Separate(Vector3 axis)
            {
                if(axis.sqrMagnitude<1e-10f)return false;
                float radius=half*(Mathf.Abs(axis.x)+Mathf.Abs(axis.y)+Mathf.Abs(axis.z)+Mathf.Abs(Vector3.Dot(axis,a))+Mathf.Abs(Vector3.Dot(axis,b))+Mathf.Abs(Vector3.Dot(axis,c)));
                return Mathf.Abs(Vector3.Dot(delta,axis))>radius+1e-6f;
            }
            if(Separate(Vector3.right)||Separate(Vector3.up)||Separate(Vector3.forward)||Separate(a)||Separate(b)||Separate(c))return false;
            for(int i=0;i<3;i++)for(int j=0;j<3;j++)
            {var u=i==0?a:i==1?b:c;var v=j==0?Vector3.right:j==1?Vector3.up:Vector3.forward;if(Separate(Vector3.Cross(u,v)))return false;}
            return true;
        }
        static Int3 Shift(Int3 p,int axis,int sign)=>axis==0?new Int3(p.x+sign,p.y,p.z):axis==1?new Int3(p.x,p.y+sign,p.z):new Int3(p.x,p.y,p.z+sign);
        void RebuildVisualAndProxy()
        {
            vertices.Clear();normals.Clear();colors.Clear();indices.Clear();RenderedFaces=0;
            int maxY=0;for(int i=0;i<Aggregate.OriginalCount;i++)maxY=Math.Max(maxY,Aggregate.GetVoxel(i).Local.y);
            var bounds=sliceBounds;var have=slicePresent;Array.Clear(have,0,have.Length);
            float cell=Aggregate.CellSize;
            for(int i=0;i<Aggregate.OriginalCount;i++)
            {
                if(!Aggregate.IsRemaining(i))continue;var voxel=Aggregate.GetVoxel(i);var p=voxel.Local;
                int slice=Math.Min(3,p.y*4/(maxY+1));var center=new Vector3((p.x+.5f)*cell,(p.y+.5f)*cell,(p.z+.5f)*cell);
                if(hardBody[i]){var cube=new Bounds(center,Vector3.one*cell);if(!have[slice]){bounds[slice]=cube;have[slice]=true;}else bounds[slice].Encapsulate(cube);}
                uint color=voxel.Matter.Tint==0?VoxelColor.Base(voxel.Matter.Material):voxel.Matter.Tint;
                var rgba=new Color32((byte)(color>>16),(byte)(color>>8),(byte)color,VertexAppearance.Pack(voxel.Matter.Material,3));
                for(int axis=0;axis<3;axis++)for(int sign=-1;sign<=1;sign+=2)
                {
                    if(Aggregate.HasRemaining(Shift(p,axis,sign),out CargoKey other)&&other.Material==voxel.Matter.Material)continue;
                    int start=vertices.Count,u=(axis+1)%3,v=(axis+2)%3;
                    for(int corner=0;corner<4;corner++)
                    {
                        var point=new Vector3(p.x,p.y,p.z);point[axis]+=sign>0?1:0;
                        point[u]+=(corner==1||corner==2)?1:0;point[v]+=corner>=2?1:0;
                        var normal=Vector3.zero;normal[axis]=sign;vertices.Add(point*cell);normals.Add(normal);colors.Add(rgba);
                    }
                    indices.Add(start);indices.Add(start+(sign>0?1:3));indices.Add(start+2);
                    indices.Add(start);indices.Add(start+2);indices.Add(start+(sign>0?3:1));RenderedFaces++;
                }
            }
            mesh.Clear();mesh.SetVertices(vertices);mesh.SetNormals(normals);mesh.SetColors(colors);mesh.SetTriangles(indices,0);mesh.RecalculateBounds();
            hasHardProxy=false;
            for(int i=0;i<proxies.Length;i++){proxies[i].enabled=have[i]&&!Aggregate.ImpactReported;if(have[i]){hasHardProxy=true;proxies[i].center=bounds[i].center;proxies[i].size=bounds[i].size;}}
            meshVersion=Aggregate.Version;
        }
        // Disabling is an explicit suspension boundary. Keep matter ownership,
        // but require a fresh host collision-revision acknowledgement to resume.
        // Never leave Armed=true on a kinematic body that cannot be rearmed.
        void OnDisable(){if(body!=null&&Aggregate!=null){CopyPose();body.isKinematic=true;Armed=false;foreach(var proxy in proxies)if(proxy!=null)proxy.enabled=false;}}
        void OnEnable(){if(Aggregate!=null&&mesh!=null)RebuildVisualAndProxy();}
        void OnDestroy(){if(mesh!=null)Destroy(mesh);}
    }
}
