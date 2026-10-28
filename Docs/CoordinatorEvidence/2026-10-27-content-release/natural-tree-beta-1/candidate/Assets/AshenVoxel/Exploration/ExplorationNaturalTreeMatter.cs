using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using AshenVoxel.Core;
using AshenVoxel.Exploration.Experimental;
using AshenVoxel.WorldScale;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using NV=System.Numerics.Vector3;
using NQ=System.Numerics.Quaternion;

namespace AshenVoxel.Exploration
{
    [DefaultExecutionOrder(270)]
    public sealed class ExplorationNaturalTreeMatter:MonoBehaviour,IShrubMatterOwner
    {
        const int GroundSide=128;
        ExplorationBootstrap boot;VegetationStreaming registry;VegetationStreaming.ShrubLease lease;
        NaturalTreePhysics physics;VoxelWorld world;Int3 anchor;WorldPoint instance;
        GameObject visual;Mesh mesh;Material material;
        readonly List<Vector3> vertices=new List<Vector3>(4096*24),normals=new List<Vector3>(4096*24);
        readonly List<Color32> colors=new List<Color32>(4096*24);readonly List<int> indices=new List<int>(4096*36);
        readonly float[] ground=new float[GroundSide*GroundSide];long groundX,groundZ;int groundCursor;
        bool requested,active,restoring,packChecked,packValid;
        NV previousPlayer;float nextCut;
        string status="T: enable existing young birch | LMB: cut trunk | I: vacuum OFF | G: kick | O: restore";
        public bool ShowBetaHud=true;
        public string Status=>status;
        public bool SessionActive=>active;
        public bool HasMatterSession=>lease!=null;
        public int SourcePrototypeId=>lease==null?-1:lease.Instance.PrototypeId;
        public int SourceCells=>physics?.SourceCells??0;
        public int FallingCells=>physics?.IslandCells??0;
        public int IndividualCells=>physics?.IndividualCells??0;
        public int CargoCells=>physics?.CargoCells??0;
        public int InitialCells=>physics?.Count??0;
        public int GroundImpacts=>physics?.GroundImpacts??0;
        public int PairContacts=>physics?.PairContacts??0;
        public double PhysicsDebtSeconds=>physics?.PhysicsDebtSeconds??0;
        public int PendingSupport=>physics?.PendingSupport??0;
        public int ExplicitKickCommands {get;private set;}
        public bool ConservationIntact=>physics==null||physics.Conserved;
        public bool SuctionEnabled {get=>physics!=null&&physics.SuctionEnabled;set{if(physics!=null)physics.SuctionEnabled=value;}}
        public static ExplorationNaturalTreeMatter Attach(ExplorationBootstrap boot)
        {
            if(boot==null)throw new ArgumentNullException(nameof(boot));var result=boot.GetComponent<ExplorationNaturalTreeMatter>();
            if(result==null){result=boot.gameObject.AddComponent<ExplorationNaturalTreeMatter>();result.boot=boot;}return result;
        }
        bool PackMatches()
        {
            if(packChecked)return packValid;packChecked=true;
            if(boot.ActiveSettings?.PrototypePack==null)return false;
            using(var sha=SHA256.Create())packValid=BitConverter.ToString(sha.ComputeHash(boot.ActiveSettings.PrototypePack.bytes)).Replace("-","").ToLowerInvariant()==NaturalTreeTemplate.PackIdentity;
            return packValid;
        }
        public bool ActivateNearby()
        {
            if(boot==null||!boot.Ready||boot.Player==null||lease!=null||!PackMatches())return false;
            registry=boot.Population;
            if(registry==null||!registry.TryReserveNaturalTree(boot.Player.Feet,512,this,out lease,out status))return false;
            try
            {
                instance=lease.Instance.Position;world=NaturalTreeTemplate.Replay(lease.Instance.PrototypeId,out anchor,out _);
                physics=new NaturalTreePhysics(world,anchor,lease.Instance.YawDegrees*Mathf.Deg2Rad);
                if(physics.IntactAnchoredCells()!=physics.Count)throw new InvalidOperationException("Authored specimen has unsupported initial cells; transfer refused intact");
                physics.TerrainHeight=GroundHeight;
                // Collection is deliberately OFF on every new real-tree session.
                physics.SuctionEnabled=false;groundCursor=0;groundX=(long)Math.Floor((instance.X-6.4)*10);groundZ=(long)Math.Floor((instance.Z-6.4)*10);
                previousPlayer=Relative(boot.Player.Feet);
                if(!OriginalClearOfPlayer())throw new InvalidOperationException("Step clear of hard tree matter before activation");
                visual=new GameObject("Natural birch matter "+lease.Instance.StableKey);visual.transform.SetParent(boot.transform,false);visual.SetActive(false);
                mesh=new Mesh{name="One shared natural-tree cube buffer",indexFormat=IndexFormat.UInt32};mesh.MarkDynamic();visual.AddComponent<MeshFilter>().sharedMesh=mesh;
                var template=Resources.Load<Material>("VoxelSurface");if(template==null)throw new InvalidOperationException("Voxel surface material unavailable");
                material=new Material(template);visual.AddComponent<MeshRenderer>().sharedMaterial=material;
                BuildFixedMesh();SyncOrigin();status="Complete natural tree prepared; capturing actual10cm terrain contact page";return true;
            }
            catch(Exception error){status="Tree transfer rejected intact: "+error.Message;registry.RestoreShrub(lease);return false;}
        }
        NV Relative(WorldPoint p)=>new NV((float)(p.X-instance.X),(float)(p.Y-instance.Y),(float)(p.Z-instance.Z));
        static NV Numeric(Vector3 p)=>new NV(p.x,p.y,p.z);
        static Vector3 Unity(NV p)=>new Vector3(p.X,p.Y,p.Z);
        float GroundHeight(float x,float z)
        {
            long gx=(long)Math.Floor((instance.X+x)*10)-groundX,gz=(long)Math.Floor((instance.Z+z)*10)-groundZ;
            return gx<0||gz<0||gx>=GroundSide||gz>=GroundSide?float.NaN:ground[(int)gx+(int)gz*GroundSide];
        }
        void CaptureGround()
        {
            var sampler=boot.Vista.SurfaceSampler;if(sampler==null)throw new InvalidOperationException("Actual current surface sampler required");
            for(int budget=0;groundCursor<ground.Length&&budget<256;budget++,groundCursor++)
            {
                int x=groundCursor%GroundSide,z=groundCursor/GroundSide;
                var column=sampler.EvaluateColumn((groundX+x+.5)*.1,(groundZ+z+.5)*.1);
                ground[groundCursor]=(float)(VoxelTerrainPage.SurfaceTop(column.SurfaceHeightMeters)-instance.Y);
            }
        }
        void Update()
        {
            if(boot==null||!boot.Ready)return;var keys=Keyboard.current;
            if(boot.Player!=null&&boot.Player.InputEnabled&&keys!=null)
            {
                if(keys.tKey.wasPressedThisFrame)ActivateNearby();
                if(keys.iKey.wasPressedThisFrame&&physics!=null)physics.SuctionEnabled=!physics.SuctionEnabled;
                if(keys.oKey.wasPressedThisFrame)Restore();
                if(active&&!restoring)
                {
                    if(Mouse.current!=null&&Mouse.current.leftButton.isPressed&&Time.unscaledTime>=nextCut){CutTrunk();nextCut=Time.unscaledTime+.15f;}
                    if(keys.gKey.wasPressedThisFrame)Kick();
                }
            }
            if(physics!=null&&!requested&&!restoring)
            {
                try{CaptureGround();if(groundCursor==ground.Length){requested=true;if(!registry.RequestShrubOwnership(lease))Restore();}}
                catch(Exception error){status="Contact capture rejected intact: "+error.Message;Restore();}
            }
        }
        public bool CutTrunk()
        {
            if(!active||restoring)return false;
            bool cut=physics.CutRay(Relative(boot.Player.Eye),Numeric(boot.Vista.MainCamera.transform.forward));
            status=cut?"Actual wood cell removed; bounded six-connected support discovery":"Aim at actual trunk wood within5m (collision backlog rejects new cuts)";return cut;
        }
        public int Kick()
        {
            if(!active||restoring)return 0;
            int count=physics.Kick(Relative(boot.Player.Eye),Numeric(boot.Vista.MainCamera.transform.forward));
            if(count>0)ExplicitKickCommands++;status="Explicit player kick: "+count+" independent cubes / falling bodies";return count;
        }
        public void PublishShrubOwnership(bool value)
        {
            if(value){active=true;visual.SetActive(true);SyncOrigin();status="Natural tree active: cut its trunk; vacuum remains OFF";}
            else
            {
                active=false;physics=null;world=null;if(visual!=null)visual.SetActive(false);
                if(mesh!=null)Destroy(mesh);mesh=null;if(material!=null)Destroy(material);material=null;if(visual!=null)Destroy(visual);visual=null;
                lease=null;requested=false;restoring=false;groundCursor=0;
            }
        }
        public bool CanPublishShrubOwnership(bool value)=>value?!restoring&&visual!=null&&groundCursor==ground.Length&&OriginalClearOfPlayer():OriginalClearOfPlayer();
        bool OriginalClearOfPlayer()
        {
            if(physics==null||boot.Player==null)return true;var feet=Relative(boot.Player.Feet);
            float yaw=lease.Instance.YawDegrees*Mathf.Deg2Rad;var q=NQ.CreateFromAxisAngle(NV.UnitY,yaw);
            for(int i=0;i<physics.Count;i++)
            {
                var c=physics.Read(i);if(!MaterialCatalog.BlocksActorBody(c.Matter.Material))continue;
                var p=NV.Transform(new NV((c.Cell.x-anchor.x+.5f)*.1f,(c.Cell.y-anchor.y+.5f)*.1f,(c.Cell.z-anchor.z+.5f)*.1f),q);
                if(CubeBody(p,q,feet,.4,1.9))return false;
            }
            return true;
        }
        public void Restore(){if(lease==null)return;restoring=true;status="Restore pending: step outside original trunk; private cargo is discarded on publication";registry.RestoreShrub(lease);}
        void FixedUpdate()
        {
            if(!active||restoring||physics==null)return;
            var feet=Relative(boot.Player.Feet);physics.PlayerFeet=feet;physics.PlayerVelocity=(feet-previousPlayer)/Math.Max(Time.fixedDeltaTime,.001f);if(physics.PlayerVelocity.LengthSquared()>400)physics.PlayerVelocity=NV.Zero;physics.PlayerPresent=true;previousPlayer=feet;
            var eye=Relative(boot.Player.Eye);physics.Hand=eye+Numeric(boot.Vista.MainCamera.transform.forward)*.55f;
            physics.Step(Time.fixedDeltaTime);
            status="Source "+physics.SourceCells+" | falling "+physics.IslandCells+" | independent "+physics.IndividualCells+" | session cargo "+physics.CargoCells+" / "+physics.Count+" | vacuum "+(physics.SuctionEnabled?"ON":"OFF")+" | contact debt "+physics.PhysicsDebtSeconds.ToString("F2")+"s";
        }
        void SyncOrigin()
        {
            if(visual==null||boot.Vista==null)return;var origin=boot.Vista.RenderOrigin;double x=instance.X-origin.X,y=instance.Y-origin.Y,z=instance.Z-origin.Z;
            if(Math.Abs(x)>10000||Math.Abs(y)>10000||Math.Abs(z)>10000){Restore();return;}
            visual.transform.SetPositionAndRotation(new Vector3((float)x,(float)y,(float)z),Quaternion.identity);
        }
        static NV Extents(NQ q)
        {var a=NV.Transform(NV.UnitX,q);var b=NV.Transform(NV.UnitY,q);var c=NV.Transform(NV.UnitZ,q);return new NV(Math.Abs(a.X)+Math.Abs(b.X)+Math.Abs(c.X),Math.Abs(a.Y)+Math.Abs(b.Y)+Math.Abs(c.Y),Math.Abs(a.Z)+Math.Abs(b.Z)+Math.Abs(c.Z))*.05f;}
        static bool CubeBody(NV p,NQ q,NV feet,double radius,double height)
        {var e=Extents(q);return p.X+e.X>feet.X-radius&&p.X-e.X<feet.X+radius&&p.Z+e.Z>feet.Z-radius&&p.Z-e.Z<feet.Z+radius&&p.Y+e.Y>feet.Y&&p.Y-e.Y<feet.Y+height;}
        public bool BlocksBody(double x0,double y0,double z0,double x1,double y1,double z1)
        {
            if(physics==null)return false;var feet=new NV((float)(x0-instance.X),(float)(y0-instance.Y),(float)(z0-instance.Z));
            for(int i=0;i<physics.Count;i++)
            {
                var c=physics.Read(i);if(c.Owner==NaturalTreePhysics.Ownership.Cargo||!MaterialCatalog.BlocksActorBody(c.Matter.Material))continue;var e=Extents(c.Rotation);
                if(c.Position.X+e.X>feet.X&&c.Position.X-e.X<x1-instance.X&&c.Position.Z+e.Z>feet.Z&&c.Position.Z-e.Z<z1-instance.Z&&c.Position.Y+e.Y>feet.Y&&c.Position.Y-e.Y<y1-instance.Y)return true;
            }
            return false;
        }
        public double WeightedSoftVoxels(WorldPoint feet,double radius,double height)
        {
            if(physics==null)return 0;var f=Relative(feet);double sum=0;
            for(int i=0;i<physics.Count;i++){var c=physics.Read(i);if(c.Owner==NaturalTreePhysics.Ownership.Cargo||c.Matter.Material!=MaterialId.Leaves)continue;var e=Extents(c.Rotation);sum+=Math.Max(0,Math.Min(c.Position.X+e.X,f.X+radius)-Math.Max(c.Position.X-e.X,f.X-radius))*Math.Max(0,Math.Min(c.Position.Y+e.Y,f.Y+height)-Math.Max(c.Position.Y-e.Y,f.Y))*Math.Max(0,Math.Min(c.Position.Z+e.Z,f.Z+radius)-Math.Max(c.Position.Z-e.Z,f.Z-radius))*1000;}
            return sum;
        }
        public bool TryGetSupportHeight(double x,double z,double radius,double low,double high,out double height)
        {
            height=double.NegativeInfinity;if(physics==null)return false;double px=x-instance.X,pz=z-instance.Z;
            for(int i=0;i<physics.Count;i++)
            {
                var c=physics.Read(i);if(c.Owner==NaturalTreePhysics.Ownership.Cargo||!MaterialCatalog.BlocksActorBody(c.Matter.Material))continue;var e=Extents(c.Rotation);
                double dx=px-Math.Max(c.Position.X-e.X,Math.Min(c.Position.X+e.X,px)),dz=pz-Math.Max(c.Position.Z-e.Z,Math.Min(c.Position.Z+e.Z,pz)),top=instance.Y+c.Position.Y+e.Y;
                if(dx*dx+dz*dz<=radius*radius&&top>=low&&top<=high)height=Math.Max(height,top);
            }
            return !double.IsNegativeInfinity(height);
        }
        void BuildFixedMesh()
        {
            vertices.Clear();normals.Clear();colors.Clear();indices.Clear();
            for(int i=0;i<physics.Count;i++)
            {
                var cube=physics.Read(i);uint rgb=cube.Matter.Tint==0?VoxelColor.Base(cube.Matter.Material):cube.Matter.Tint;
                var color=new Color32((byte)(rgb>>16),(byte)(rgb>>8),(byte)rgb,VertexAppearance.Pack(cube.Matter.Material,3));
                for(int axis=0;axis<3;axis++)for(int sign=-1;sign<=1;sign+=2)
                {
                    int start=vertices.Count;
                    for(int corner=0;corner<4;corner++){vertices.Add(Vector3.zero);normals.Add(Vector3.zero);colors.Add(color);}
                    indices.Add(start);indices.Add(start+(sign>0?1:3));indices.Add(start+2);indices.Add(start);indices.Add(start+2);indices.Add(start+(sign>0?3:1));
                }
            }
            UpdateMeshVertices();mesh.SetColors(colors);mesh.SetTriangles(indices,0);
        }
        void UpdateMeshVertices()
        {
            bool haveBounds=false;Bounds bounds=default;
            for(int i=0;i<physics.Count;i++)
            {
                var cube=physics.Read(i);bool visible=cube.Owner!=NaturalTreePhysics.Ownership.Cargo;
                for(int axis=0;axis<3;axis++)for(int side=0;side<2;side++)
                {
                    int sign=side==0?-1:1,u=(axis+1)%3,v=(axis+2)%3;var normal=NV.Zero;if(axis==0)normal.X=sign;else if(axis==1)normal.Y=sign;else normal.Z=sign;
                    bool faceVisible=visible&&!physics.CoherentNeighbour(i,axis,sign);
                    Vector3 n=Unity(NV.Transform(normal,cube.Rotation));
                    for(int corner=0;corner<4;corner++)
                    {
                        var p=Vector3.one*(-.05f);p[axis]=sign*.05f;p[u]=(corner==1||corner==2)?.05f:-.05f;p[v]=corner>=2?.05f:-.05f;
                        int at=i*24+(axis*2+side)*4+corner;vertices[at]=faceVisible?Unity(cube.Position+NV.Transform(Numeric(p),cube.Rotation)):Unity(cube.Position);normals[at]=n;
                    }
                }
                if(visible){var b=new Bounds(Unity(cube.Position),Vector3.one*.18f);if(!haveBounds){bounds=b;haveBounds=true;}else bounds.Encapsulate(b);}
            }
            mesh.SetVertices(vertices);mesh.SetNormals(normals);mesh.bounds=haveBounds?bounds:new Bounds(Vector3.zero,Vector3.one*.01f);
        }
        void LateUpdate(){SyncOrigin();if(active&&physics!=null&&mesh!=null)UpdateMeshVertices();}
        void OnGUI()
        {
            if(!ShowBetaHud||boot==null||!boot.Ready)return;
            GUI.Box(new Rect(12,Screen.height-380,940,110),"EXPERIMENTAL NATURAL TREE — full young birch0/1 only;4096-cell cap; session rollback");
            GUI.Label(new Rect(24,Screen.height-354,910,24),status);
            if(GUI.Button(new Rect(24,Screen.height-322,230,28),"T: Enable nearby natural tree"))ActivateNearby();
            if(GUI.Button(new Rect(264,Screen.height-322,190,28),SuctionEnabled?"I: Vacuum ON":"I: Vacuum OFF"))SuctionEnabled=!SuctionEnabled;
            if(GUI.Button(new Rect(464,Screen.height-322,210,28),"O: Restore / discard cargo"))Restore();
            GUI.Label(new Rect(24,Screen.height-291,900,20),"LMB removes aimed wood cells. Gravity/stump contact first; actual terrain impact crumbles. G explicitly kicks loose cubes.");
        }
        void OnDisable(){Restore();}
        void OnDestroy(){Restore();}
    }
}
