using System;
using System.Threading;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using AshenVoxel.Core;
using AshenVoxel.Exploration.Experimental;
using AshenVoxel.WorldScale;
using UnityEngine;
using UnityEngine.SceneManagement;
using NV=System.Numerics.Vector3;
using NQ=System.Numerics.Quaternion;

namespace AshenVoxel.Exploration
{
    // One native body per connected section, never per voxel. Matter ownership
    // stays in NaturalTreePhysics; PhysX alone owns coherent-section motion.
    public sealed class NaturalMatterRigidBodies:IDisposable
    {
        sealed class FloorData {public Vector3[] Vertices;public int[] Triangles;}
        readonly NaturalTreePhysics matter;readonly VoxelWorld source;
        GameObject stumpObject;Mesh stumpMesh;MeshCollider stumpCollider;
        readonly List<Vector3> stumpVertices=new List<Vector3>();readonly List<int> stumpIndices=new List<int>();
        int stumpRevision=-1,stumpCursor,stumpX,stumpY,stumpZ;bool stumpReady;
        const int StumpSide=33,StumpHeight=10,StumpFaces=32768;
        static readonly Int3[] Faces={new Int3(1,0,0),new Int3(-1,0,0),new Int3(0,1,0),new Int3(0,-1,0),new Int3(0,0,1),new Int3(0,0,-1)};
        static readonly Vector3[,] Corners={
            {new Vector3(1,0,0),new Vector3(1,1,0),new Vector3(1,1,1),new Vector3(1,0,1)},
            {new Vector3(0,0,1),new Vector3(0,1,1),new Vector3(0,1,0),new Vector3(0,0,0)},
            {new Vector3(0,1,1),new Vector3(1,1,1),new Vector3(1,1,0),new Vector3(0,1,0)},
            {new Vector3(0,0,0),new Vector3(1,0,0),new Vector3(1,0,1),new Vector3(0,0,1)},
            {new Vector3(1,0,1),new Vector3(1,1,1),new Vector3(0,1,1),new Vector3(0,0,1)},
            {new Vector3(0,0,0),new Vector3(0,1,0),new Vector3(1,1,0),new Vector3(1,0,0)}};
        readonly Transform parent;
        readonly Rigidbody[] bodies=new Rigidbody[8];
        readonly NaturalMatterGroundContact[] contacts=new NaturalMatterGroundContact[8];
        readonly Mesh[] hulls=new Mesh[8];
        readonly int[] generations=new int[8];
        readonly CancellationTokenSource cancellation=new CancellationTokenSource();
        readonly Task<FloorData> floorTask;
        GameObject floorObject;Mesh floorMesh;MeshCollider floorCollider;
        Vector3 lastOffset;bool positioned,disposed;
        public bool Ready=>floorCollider!=null;
        public string Error {get;private set;}
        static Vector3 Unity(NV v)=>new Vector3(v.X,v.Y,v.Z);
        static NV Numeric(Vector3 v)=>new NV(v.x,v.y,v.z);
        static Quaternion Unity(NQ q){float l=q.X*q.X+q.Y*q.Y+q.Z*q.Z+q.W*q.W;if(!(l>1e-8f)||float.IsInfinity(l))return Quaternion.identity;float k=1/Mathf.Sqrt(l);return new Quaternion(q.X*k,q.Y*k,q.Z*k,q.W*k);}
        GameObject CreateObject(string name)
        {
            var obj=new GameObject(name);SceneManager.MoveGameObjectToScene(obj,parent.gameObject.scene);
            obj.transform.SetParent(parent,false);return obj;
        }

        public NaturalMatterRigidBodies(Transform parent,NaturalTreePhysics matter,VoxelWorld world,
            WorldPoint origin,SurfaceMaterialSampler terrain)
        {
            this.parent=parent;this.matter=matter;this.source=world;matter.ExternalRigidBodies=true;
            var source=terrain.BaseField is IWorldFieldSnapshotProvider snapshots?snapshots.CaptureSnapshot():terrain.BaseField;
            var sampler=new SurfaceMaterialSampler(source,terrain.Profile);
            double radius=Math.Min(200,Math.Max(8,(Math.Max(world.SizeX,world.SizeZ)+world.SizeY)*.1+4));
            CancellationToken token=cancellation.Token;
            floorTask=Task.Run(()=>
            {
                int n=Math.Min(128,Math.Max(16,(int)Math.Ceiling(radius)));double step=radius*2/n;
                var vertices=new Vector3[(n+1)*(n+1)];var indices=new int[n*n*6];int at=0;
                for(int z=0;z<=n;z++)
                {
                    token.ThrowIfCancellationRequested();
                    for(int x=0;x<=n;x++)
                    {
                        double px=x*step-radius,pz=z*step-radius;
                        double y=VoxelTerrainPage.SurfaceTop(sampler.EvaluateColumn(origin.X+px,origin.Z+pz).SurfaceHeightMeters)-origin.Y;
                        vertices[z*(n+1)+x]=new Vector3((float)px,(float)y,(float)pz);
                        if(x==n||z==n)continue;int a=z*(n+1)+x,b=a+1,c=a+n+1,d=c+1;
                        indices[at++]=a;indices[at++]=c;indices[at++]=b;indices[at++]=b;indices[at++]=c;indices[at++]=d;
                    }
                }
                return new FloorData{Vertices=vertices,Triangles=indices};
            },token);
        }
        public void Tick(Vector3 offset,bool meshesReady)
        {
            if(disposed)return;
            if(!Ready&&Error==null&&floorTask.IsCompleted)
            {
                try
                {
                    var data=floorTask.GetAwaiter().GetResult();floorMesh=new Mesh{name="Matter local ground collision"};
                    floorMesh.vertices=data.Vertices;floorMesh.triangles=data.Triangles;floorMesh.RecalculateBounds();
                    floorObject=CreateObject("Matter ground collision");
                    floorCollider=floorObject.AddComponent<MeshCollider>();floorCollider.sharedMesh=floorMesh;
                }
                catch(Exception e){Error=e.GetBaseException().Message;return;}
            }
            if(!Ready)return;
            PumpStump(offset);
            Vector3 shift=positioned?offset-lastOffset:Vector3.zero;floorObject.transform.position=offset;
            for(int i=0;i<bodies.Length;i++)
            {
                if(!matter.RigidBodyActive(i)){Release(i);continue;}
                if(generations[i]!=matter.RigidBodyGeneration(i))Release(i);
                if(bodies[i]==null)
                {
                    if(!meshesReady||!stumpReady||!matter.RigidBodyShape(i,out var probes,out float mass))continue;
                    matter.BodyPose(i,out var position,out var rotation);
                    var points=new Vector3[probes.Length*8];var triangles=new int[(points.Length-2)*3];
                    for(int p=0;p<probes.Length;p++)for(int corner=0;corner<8;corner++)
                        points[p*8+corner]=Unity(probes[p])+new Vector3((corner&1)==0?-.05f:.05f,(corner&2)==0?-.05f:.05f,(corner&4)==0?-.05f:.05f);
                    for(int p=1;p<points.Length-1;p++){int a=(p-1)*3;triangles[a]=0;triangles[a+1]=p;triangles[a+2]=p+1;}
                    var mesh=new Mesh{name="Matter convex section hull"};mesh.vertices=points;mesh.triangles=triangles;mesh.RecalculateBounds();hulls[i]=mesh;
                    var obj=CreateObject("Matter rigid section "+i);
                    obj.transform.SetPositionAndRotation(offset+Unity(position),Unity(rotation));
                    var collider=obj.AddComponent<MeshCollider>();collider.convex=true;collider.sharedMesh=mesh;
                    var contact=obj.AddComponent<NaturalMatterGroundContact>();contact.Ground=floorCollider;contact.Stump=stumpCollider;contacts[i]=contact;
                    var body=obj.AddComponent<Rigidbody>();body.mass=Mathf.Clamp(mass,.01f,10000000);
                    body.centerOfMass=Vector3.zero;body.ResetInertiaTensor();body.linearDamping=.05f;body.angularDamping=.1f;
                    body.interpolation=RigidbodyInterpolation.Interpolate;body.collisionDetectionMode=CollisionDetectionMode.ContinuousSpeculative;
                    body.maxAngularVelocity=3;body.useGravity=true;bodies[i]=body;generations[i]=matter.RigidBodyGeneration(i);
                }
                else if(shift.sqrMagnitude>0)
                {bool asleep=bodies[i].IsSleeping();bodies[i].position+=shift;if(asleep)bodies[i].Sleep();}
                if(bodies[i]!=null)contacts[i].Stump=stumpCollider;
            }
            lastOffset=offset;positioned=true;
        }
        static bool StumpHard(MaterialId material)=>material!=MaterialId.Air&&material!=MaterialId.Leaves&&material!=MaterialId.Fruit&&MaterialCatalog.BlocksActorBody(material);
        void PumpStump(Vector3 offset)
        {
            if(stumpRevision!=matter.StructureRevision)
            {
                stumpRevision=matter.StructureRevision;stumpCursor=0;stumpReady=!matter.HasCut;
                stumpVertices.Clear();stumpIndices.Clear();
                if(stumpCollider!=null)stumpCollider.enabled=false; // Never collide against an older edit.
                foreach(var contact in contacts)if(contact!=null)contact.ResetStump();
                stumpX=matter.LastCutCell.x-16;stumpY=matter.LastCutCell.y-8;stumpZ=matter.LastCutCell.z-16;
            }
            if(stumpObject!=null)stumpObject.transform.position=offset;
            if(stumpReady||matter.PendingSupport>0&&!matter.WaitingForIslandSlot)return;
            long started=Stopwatch.GetTimestamp();var anchor=matter.SourceAnchor;
            while(stumpCursor<StumpSide*StumpSide*StumpHeight)
            {
                if((stumpCursor&63)==0&&(Stopwatch.GetTimestamp()-started)*1000.0/Stopwatch.Frequency>=.6)return;
                int at=stumpCursor++;var cell=new Int3(stumpX+at%StumpSide,stumpY+at/(StumpSide*StumpSide),stumpZ+at/StumpSide%StumpSide);
                if(!StumpHard(source.Get(cell)))continue;
                for(int face=0;face<6;face++)
                {
                    if(StumpHard(source.Get(cell+Faces[face])))continue;
                    if(stumpIndices.Count/6>=StumpFaces){Error="Stump contact surface exceeds bounded face capacity";return;}
                    int first=stumpVertices.Count;var origin=new Vector3((cell.x-anchor.x)*.1f,(cell.y-anchor.y)*.1f,(cell.z-anchor.z)*.1f);
                    for(int c=0;c<4;c++)stumpVertices.Add(origin+Corners[face,c]*.1f);
                    stumpIndices.Add(first);stumpIndices.Add(first+1);stumpIndices.Add(first+2);stumpIndices.Add(first);stumpIndices.Add(first+2);stumpIndices.Add(first+3);
                }
            }
            if(stumpObject==null){stumpObject=CreateObject("Matter remaining cut support");stumpCollider=stumpObject.AddComponent<MeshCollider>();}
            stumpObject.transform.SetPositionAndRotation(offset,Unity(matter.SourceRotation));stumpCollider.sharedMesh=null;
            if(stumpMesh!=null)UnityEngine.Object.Destroy(stumpMesh);
            stumpMesh=new Mesh{name="Exact bounded remaining cut support",indexFormat=UnityEngine.Rendering.IndexFormat.UInt32};
            stumpMesh.SetVertices(stumpVertices);stumpMesh.SetTriangles(stumpIndices,0);stumpMesh.RecalculateBounds();
            if(stumpIndices.Count>0){stumpCollider.sharedMesh=stumpMesh;stumpCollider.enabled=true;}stumpReady=true;
        }
        public void ReadBack(float elapsed)
        {
            if(!Ready||disposed)return;
            for(int i=0;i<bodies.Length;i++)if(bodies[i]!=null)
            {
                var body=bodies[i];var q=body.rotation;
                matter.ReadRigidBody(i,Numeric(body.position-lastOffset),new NQ(q.x,q.y,q.z,q.w),Numeric(body.linearVelocity),contacts[i].Touching,body.IsSleeping(),elapsed);
            }
        }
        void Release(int i)
        {
            if(bodies[i]!=null){bodies[i].detectCollisions=false;UnityEngine.Object.Destroy(bodies[i].gameObject);bodies[i]=null;contacts[i]=null;}
            if(hulls[i]!=null){UnityEngine.Object.Destroy(hulls[i]);hulls[i]=null;}
        }
        public void Dispose()
        {
            if(disposed)return;disposed=true;cancellation.Cancel();
            floorTask.ContinueWith(t=>{var observed=t.Exception;},TaskContinuationOptions.OnlyOnFaulted);
            for(int i=0;i<bodies.Length;i++)Release(i);
            if(floorObject!=null){floorObject.SetActive(false);UnityEngine.Object.Destroy(floorObject);}
            if(floorMesh!=null)UnityEngine.Object.Destroy(floorMesh);if(stumpObject!=null){stumpObject.SetActive(false);UnityEngine.Object.Destroy(stumpObject);}if(stumpMesh!=null)UnityEngine.Object.Destroy(stumpMesh);cancellation.Dispose();
        }
    }
    public sealed class NaturalMatterGroundContact:MonoBehaviour
    {
        public Collider Ground,Stump;bool groundTouch,stumpTouch;public bool Touching=>(groundTouch&&Ground!=null&&Ground.enabled&&Ground.gameObject.activeInHierarchy)||(stumpTouch&&Stump!=null&&Stump.enabled&&Stump.gameObject.activeInHierarchy);
        public void ResetStump(){stumpTouch=false;}
        void OnCollisionEnter(Collision other){Touch(other.collider,true);}
        void OnCollisionStay(Collision other){Touch(other.collider,true);}
        void OnCollisionExit(Collision other){Touch(other.collider,false);}
        void Touch(Collider collider,bool value){if(collider==Ground)groundTouch=value;if(collider==Stump)stumpTouch=value;}
    }
}

