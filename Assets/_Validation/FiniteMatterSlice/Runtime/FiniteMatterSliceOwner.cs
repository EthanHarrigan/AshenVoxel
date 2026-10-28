using System.Collections.Generic;
using AshenVoxel.Core;
using AshenVoxel.Rendering;
using UnityEngine;
using UnityEngine.Rendering;

namespace AshenVoxel.Core.Experimental
{
    /// <summary>One controlled 3.2m arena, one rotating body, one external matter
    /// ledger. Explicit opt-in scene only. No Exploration or persistence owner.</summary>
    public sealed class FiniteMatterSliceOwner : MonoBehaviour
    {
        public VoxelWorld World {get;private set;}
        public LocalizedMatter Matter {get;private set;}
        public FallingAggregateActor Actor {get;private set;}
        public ChunkWorldRenderer Terrain {get;private set;}
        public bool SuctionEnabled;
        public bool AutoCut;
        public bool AutoSuctionAfterImpact;
        public long PublishedStaticRevision {get;private set;}
        public float MaximumRotationDegrees {get;private set;}
        public bool ConservationIntact => initialized&&OwnedCells==InitialMatterCells;
        public long OwnedCells => WorldBranchCells()+Matter.DebrisCount+(Actor==null?0:Actor.Aggregate.Remaining)+Matter.CreditedCells;
        public const int InitialMatterCells=72;
        readonly FallingAggregateSlot slot=new FallingAggregateSlot();
        readonly List<Int3> branch=new List<Int3>(64);
        readonly Int3 collector=new Int3(26,4,15);
        Mesh debrisMesh; MeshFilter debrisFilter;
        readonly List<Vector3> vertices=new List<Vector3>(384);
        readonly List<Vector3> normals=new List<Vector3>(384);
        readonly List<Color32> colors=new List<Color32>(384);
        readonly List<int> triangles=new List<int>(576);
        string state="Building finite arena";bool cutRequested,capturePending,initialized;
        float debrisClock;
        // This arena has a fixed explicit origin. No floating-origin integration
        // is claimed; actor and terrain use the same global-meter coordinates.
        const double RenderOriginX=0,RenderOriginY=0,RenderOriginZ=0;
        GameObject Child(string label){var go=new GameObject(label);go.transform.SetParent(transform,false);return go;}
        void Start()
        {
            if(transform.position!=Vector3.zero||transform.rotation!=Quaternion.identity||transform.lossyScale!=Vector3.one)
            {enabled=false;Debug.LogError("FiniteMatterSliceOwner requires an identity scene root.");return;}
            World=new VoxelWorld(32,32,32,.1f,714103);
            for(int z=0;z<32;z++)for(int x=0;x<32;x++)for(int y=0;y<2;y++)World.Set(x,y,z,MaterialId.Stone);
            for(int y=2;y<20;y++)for(int z=14;z<16;z++)for(int x=14;x<16;x++)
            {var p=new Int3(x,y,z);World.Set(p,MaterialId.Wood,0xFF916039);if(y>=4)branch.Add(p);}
            Matter=new LocalizedMatter(World,new Int3(0,0,0),new Int3(31,31,31),64,16,8);
            Matter.AdditionalOccupancy=(p,excluded)=>Actor!=null&&!ReferenceEquals(excluded,Actor.Aggregate)&&Actor.OccupiesCell(p);
            Terrain=Child("Authoritative-world chunk cache").AddComponent<ChunkWorldRenderer>();
            Terrain.GenerateColliders=true;Terrain.ColliderBuildsPerFrame=2;Terrain.Initialize(World);
            var debris=Child("Bounded debris cache (one mesh)");debrisFilter=debris.AddComponent<MeshFilter>();
            debris.AddComponent<MeshRenderer>().sharedMaterial=Terrain.SurfaceMaterial;
            debrisMesh=new Mesh{name="Finite debris cache",indexFormat=IndexFormat.UInt32};debrisMesh.MarkDynamic();debrisFilter.sharedMesh=debrisMesh;
            var camera=Child("Finite slice camera").AddComponent<Camera>();camera.tag="MainCamera";
            camera.transform.position=new Vector3(4.5f,2.7f,-2.2f);camera.transform.LookAt(new Vector3(1.6f,.9f,1.6f));
            camera.nearClipPlane=.02f;camera.farClipPlane=30;camera.backgroundColor=new Color(.12f,.17f,.20f);camera.clearFlags=CameraClearFlags.SolidColor;
            var light=Child("Finite slice light").AddComponent<Light>();light.type=LightType.Directional;light.intensity=2;light.transform.rotation=Quaternion.Euler(45,-35,0);
            initialized=true;state="Ready: cut the eight-cell support";
        }
        public bool CutSupport()
        {
            if(!initialized||cutRequested||Terrain.PendingChunks!=0||Terrain.PendingColliders!=0)return false;
            // Exactly eight support cell centers lie within this radius. Their
            // matter enters debris before branch capture; nothing is deleted.
            if(!Matter.TryBeginRemoval(1.5,.3,1.5,.09,10))return false;
            cutRequested=true;capturePending=true;state="Cut queued; bounded support withdrawal";return true;
        }
        void Update()
        {
            if(!initialized)return;
            Terrain.RebuildBudget(2);
            if(Terrain.PendingChunks==0&&Terrain.PendingColliders==0)
            {
                PublishedStaticRevision=World.Revision;
                if(Actor!=null&&!Actor.Armed&&!Actor.Aggregate.ImpactReported)
                {Physics.SyncTransforms();if(Actor.ArmAfterStaticCollisionRevision(PublishedStaticRevision))state="Rotating fall armed after static collider publication";}
                if(AutoCut&&!cutRequested)CutSupport();
            }
        }
        void FixedUpdate()
        {
            if(!initialized)return;
            Matter.Step(64,16);
            if(capturePending&&!Matter.Pending)
            {
                // A cut chip still occupies matter space until evacuated. Pull
                // the eight chips aside before declaring the branch unsupported.
                Matter.StepDebris(16,true,collector);
                bool blocked=false;
                for(int z=14;z<16;z++)for(int x=14;x<16;x++)if(Matter.IsOccupied(new Int3(x,3,z)))blocked=true;
                if(!blocked&&slot.TryCapture(World,Matter,branch,1024,64,out string reason))
                {
                    capturePending=false;
                    Actor=Child("Single detached branch body").AddComponent<FallingAggregateActor>();
                    Actor.InitialAngularVelocity=new Vector3(0,0,1.5f);Actor.FragmentCandidateBudget=16;
                    Actor.Bind(slot.Current,Terrain.SurfaceMaterial,v=>MaterialCatalog.BlocksActorBody(v.Matter.Material,false),RenderOriginX,RenderOriginY,RenderOriginZ);
                    state="Branch captured; waiting for static collider cache";
                }
                else state="Evacuating support chips; branch remains in world";
            }
            if(Actor!=null)
            {
                Actor.TickOwnedPhysics();
                MaximumRotationDegrees=Mathf.Max(MaximumRotationDegrees,Quaternion.Angle(Quaternion.identity,Actor.transform.rotation));
                if(Actor.Aggregate.ImpactReported)state=Actor.Aggregate.Status;
                if(Actor.Aggregate.ImpactReported&&AutoSuctionAfterImpact)SuctionEnabled=true;
                if(SuctionEnabled&&Actor.Aggregate.ImpactReported)
                    Actor.Suction((collector.x+.5)*.1,(collector.y+.5)*.1,(collector.z+.5)*.1,3,16);
            }
            debrisClock+=Time.fixedDeltaTime;
            if(debrisClock>=.05f){debrisClock=0;if(!capturePending)Matter.StepDebris(16,SuctionEnabled,collector);RebuildDebris();}
            if(OwnedCells!=InitialMatterCells){state="CONSERVATION FAILURE";enabled=false;Debug.LogError(state);}
        }
        int WorldBranchCells()
        {
            if(World==null)return 0;int count=0;
            for(int y=2;y<20;y++)for(int z=14;z<16;z++)for(int x=14;x<16;x++)if(World.Get(x,y,z)==MaterialId.Wood)count++;
            return count;
        }
        void RebuildDebris()
        {
            vertices.Clear();normals.Clear();colors.Clear();triangles.Clear();
            foreach(var voxel in Matter.Debris)
            {
                uint color=voxel.Matter.Tint==0?VoxelColor.Base(voxel.Matter.Material):voxel.Matter.Tint;
                var rgba=new Color32((byte)(color>>16),(byte)(color>>8),(byte)color,VertexAppearance.Pack(voxel.Matter.Material,3));
                for(int axis=0;axis<3;axis++)for(int sign=-1;sign<=1;sign+=2)
                {
                    int start=vertices.Count,u=(axis+1)%3,v=(axis+2)%3;
                    for(int corner=0;corner<4;corner++)
                    {
                        var point=new Vector3(voxel.Cell.x,voxel.Cell.y,voxel.Cell.z);point[axis]+=sign>0?1:0;
                        point[u]+=(corner==1||corner==2)?1:0;point[v]+=corner>=2?1:0;
                        var normal=Vector3.zero;normal[axis]=sign;vertices.Add(point*.1f);normals.Add(normal);colors.Add(rgba);
                    }
                    triangles.Add(start);triangles.Add(start+(sign>0?1:3));triangles.Add(start+2);
                    triangles.Add(start);triangles.Add(start+2);triangles.Add(start+(sign>0?3:1));
                }
            }
            debrisMesh.Clear();debrisMesh.SetVertices(vertices);debrisMesh.SetNormals(normals);debrisMesh.SetColors(colors);debrisMesh.SetTriangles(triangles,0);debrisMesh.RecalculateBounds();
        }
        void OnGUI()
        {
            if(!initialized)return;
            GUI.Box(new Rect(12,12,560,180),"Finite matter slice — WIP small branch only");
            GUI.Label(new Rect(24,40,540,24),state);
            GUI.Label(new Rect(24,64,540,24),"World + aggregate + debris + cargo = "+OwnedCells+" / 72 cells");
            GUI.Label(new Rect(24,88,540,24),"Cargo "+Matter.CreditedCells+" | debris "+Matter.DebrisCount+" / 16 | retained "+(Actor==null?0:Actor.Aggregate.Remaining));
            GUI.enabled=!cutRequested&&Terrain.PendingChunks==0&&Terrain.PendingColliders==0;
            if(GUI.Button(new Rect(24,120,190,30),"1. Cut support"))CutSupport();GUI.enabled=true;
            if(GUI.Button(new Rect(224,120,190,30),SuctionEnabled?"2. Suction ON":"2. Suction OFF"))SuctionEnabled=!SuctionEnabled;
            GUI.Label(new Rect(24,156,540,24),"Static queues "+Terrain.PendingChunks+" / "+Terrain.PendingColliders+"; cargo has no unload/save contract.");
        }
        void OnDisable(){if(Actor!=null)Actor.enabled=false;}
        void OnEnable(){if(Actor!=null)Actor.enabled=true;}
        void OnDestroy(){Matter?.Dispose();if(debrisMesh!=null)Destroy(debrisMesh);}
    }
}
