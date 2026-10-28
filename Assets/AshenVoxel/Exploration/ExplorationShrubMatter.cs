using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using AshenVoxel.Core;
using AshenVoxel.Core.Experimental;
using AshenVoxel.Exploration.Experimental;
using AshenVoxel.Rendering;
using AshenVoxel.WorldScale;
using UnityEngine;
using UnityEngine.InputSystem;

namespace AshenVoxel.Exploration
{
    // One genuine currently-published shrub. Private cargo never leaves this
    // explicit rollback session; original instance is restored on release.
    [DefaultExecutionOrder(260)]
    public sealed class ExplorationShrubMatter:MonoBehaviour,IShrubMatterOwner
    {
        const string PackPin="41556b474ce7a26342e4dd7bcff2e4fe658ab0333e8360058262ee21c0f011aa";
        ExplorationBootstrap boot;VegetationStreaming population;VegetationStreaming.ShrubLease lease;
        VoxelWorld world;LocalizedMatter matter;ChunkWorldRenderer cache;
        GameObject root;Mesh debrisMesh;MeshFilter debrisFilter;
        readonly List<Int3> source=new List<Int3>(512);
        readonly Dictionary<Int3,MaterialId> originalMaterial=new Dictionary<Int3,MaterialId>(512);
        readonly List<Vector3> vertices=new List<Vector3>(12288),normals=new List<Vector3>(12288);
        readonly List<Color32> colors=new List<Color32>(12288);readonly List<int> indices=new List<int>(18432);
        bool active,requested,suction,retiring,packChecked,packValid;float clock;
        double co,si;WorldPoint position;int initial,removalCursor;
        string status="U: break nearby existing shrub | I: suction | O: restore";
        public bool ShowBetaHud=true;
        public string Status=>status;
        public bool SuctionEnabled {get=>suction;set=>suction=value;}
        public bool HasMatterSession=>lease!=null;
        public bool SessionActive=>active;
        public int InitialCells=>initial;
        public int SourcePrototypeId=>lease==null?0:lease.Instance.PrototypeId;
        public int DebrisCount=>matter?.DebrisCount??0;
        public long CargoCells=>matter?.CreditedCells??0;
        public int RemainingSourceCells=>Math.Max(0,source.Count-removalCursor);

        public long OwnedCells
        {get{if(world==null||matter==null)return 0;long total=matter.DebrisCount+matter.CreditedCells;foreach(var chunk in world.Chunks.Values)total+=chunk.NonAirCount;return total;}}
        public static ExplorationShrubMatter Attach(ExplorationBootstrap boot)
        {
            if(boot==null)throw new ArgumentNullException(nameof(boot));var result=boot.GetComponent<ExplorationShrubMatter>();
            if(result==null){result=boot.gameObject.AddComponent<ExplorationShrubMatter>();result.boot=boot;}return result;
        }
        bool CheckPack()
        {
            if(packChecked)return packValid;packChecked=true;
            if(boot.ActiveSettings==null||boot.ActiveSettings.PrototypePack==null)return false;
            byte[] bytes=boot.ActiveSettings.PrototypePack.bytes;
            packValid=ExplorationPrototypeReader.CanonicalIdentity(bytes)==PackPin;
            return packValid;
        }
        public bool BreakNearby()
        {
            if(boot==null||!boot.Ready||boot.Player==null||lease!=null||retiring)return false;
            if(!CheckPack()){status="Canonical shrub pack differs; transfer refused";return false;}
            population=boot.Population;
            if(population==null||!population.TryReserveShrub(boot.Player.Feet,512,this,out lease,out status))return false;
            try
            {
                position=lease.Instance.Position;double angle=lease.Instance.YawDegrees*Math.PI/180;co=Math.Cos(angle);si=Math.Sin(angle);
                world=ShrubMatterTemplate.Replay(lease.Instance.PrototypeId,out var generated);
                initial=generated.WrittenCells;
                if(initial!=(lease.Instance.PrototypeId==102?510:497))throw new InvalidOperationException("Canonical whole-cell count changed");
                source.Clear();foreach(var pair in world.Chunks)for(int i=0;i<Chunk.Volume;i++)if(pair.Value.Cells[i]!=MaterialId.Air)
                {var p=Chunk.Decode(i);source.Add(new Int3(pair.Key.x*16+p.x,pair.Key.y*16+p.y,pair.Key.z*16+p.z));}
                if(source.Count!=initial)throw new InvalidOperationException("Source ownership count mismatch");
                source.Sort((a,b)=>{int c=b.y.CompareTo(a.y);if(c==0)c=a.z.CompareTo(b.z);return c==0?a.x.CompareTo(b.x):c;});
                foreach(var cell in source)originalMaterial.Add(cell,world.Get(cell));
                matter=new LocalizedMatter(world,new Int3(24,1,24),new Int3(55,31,55),512,512,64);
                matter.AdditionalOccupancy=(cell,excluded)=>DestinationBlocked(cell);
                // Inactive prepared data is not gameplay ownership. Do not move
                // the player into a newly hard shrub on the publication boundary.
                var feet=boot.Player.Feet;
                if(BlocksBody(feet.X-.4,feet.Y,feet.Z-.4,feet.X+.4,feet.Y+1.9,feet.Z+.4))throw new InvalidOperationException("Step clear of the shrub before breaking it");
                root=new GameObject("Experimental existing shrub "+lease.Instance.StableKey);root.transform.SetParent(boot.transform,false);root.SetActive(false);
                cache=root.AddComponent<ChunkWorldRenderer>();cache.GenerateColliders=false;cache.Initialize(world);
                var debris=new GameObject("512-cell shared debris cache");debris.transform.SetParent(root.transform,false);
                debrisFilter=debris.AddComponent<MeshFilter>();debris.AddComponent<MeshRenderer>().sharedMaterial=cache.SurfaceMaterial;
                debrisMesh=new Mesh{name="Existing-shrub debris"};debrisMesh.MarkDynamic();debrisFilter.sharedMesh=debrisMesh;
                SyncOrigin();status="Preparing complete shrub; original remains visible/collidable";return true;
            }
            catch(Exception error){status="Transfer refused: "+error.Message;population.RestoreShrub(lease);return false;}
        }
        void Local(double x,double y,double z,out double a,out double b,out double c)
        {double dx=x-position.X,dz=z-position.Z;a=dx*co-dz*si+4;b=y-position.Y+.4;c=dx*si+dz*co+4;}
        void Global(Int3 cell,out double x,out double y,out double z)
        {double a=(cell.x+.5)*.1-4,c=(cell.z+.5)*.1-4;x=position.X+a*co+c*si;y=position.Y+(cell.y+.5)*.1-.4;z=position.Z-a*si+c*co;}
        void SyncOrigin()
        {
            if(root==null||boot==null||boot.Vista==null)return;var origin=boot.Vista.RenderOrigin;
            double x=position.X-4*co-4*si-origin.X,y=position.Y-.4-origin.Y,z=position.Z+4*si-4*co-origin.Z;
            // Streaming restores the one leased source at24m; this larger bound
            // only protects conversion during teleports, with retained ownership.
            if(Math.Abs(x)>10000||Math.Abs(y)>10000||Math.Abs(z)>10000){Restore();return;}
            root.transform.SetPositionAndRotation(new Vector3((float)x,(float)y,(float)z),Quaternion.Euler(0,lease.Instance.YawDegrees,0));
        }
        void Update()
        {
            if(boot==null||!boot.Ready)return;var keys=Keyboard.current;
            if(boot.Player!=null&&boot.Player.InputEnabled&&keys!=null)
            {if(keys.uKey.wasPressedThisFrame)BreakNearby();if(keys.iKey.wasPressedThisFrame)succionToggle();if(keys.oKey.wasPressedThisFrame)Restore();}
            if(cache!=null&&!retiring)
            {
                cache.RebuildBudget(2);
                if(!requested&&cache.PendingChunks==0){requested=true;if(!population.RequestShrubOwnership(lease)){status="Stale instance; transfer refused";Restore();}}
            }
        }
        void succionToggle(){suction=!suction;}
        void LateUpdate(){SyncOrigin();}
        public bool CanPublishShrubOwnership(bool value)
        {
            if(boot==null||boot.Player==null)return !value;
            var feet=boot.Player.Feet;
            if(value)return !retiring&&root!=null&&cache!=null&&cache.PendingChunks==0&&
                !BlocksBody(feet.X-.4,feet.Y,feet.Z-.4,feet.X+.4,feet.Y+1.9,feet.Z+.4);
            BoundsLocal(feet.X-.4,feet.Y,feet.Z-.4,feet.X+.4,feet.Y+1.9,feet.Z+.4,out double a,out double b,out double c,out double d,out double e,out double f);
            foreach(var pair in originalMaterial)if(MaterialCatalog.BlocksActorBody(pair.Value)&&Overlap(pair.Key,a,b,c,d,e,f))return false;
            return true;
        }
        public void PublishShrubOwnership(bool value)
        {
            if(value)
            {
                // No work/allocation here except the bounded brush command. Mesh
                // was ready before the streamer accepted suppression.
                active=true;root.SetActive(true);SyncOrigin();
                removalCursor=0;
                status="Whole shrub breakup:512-cell cap; I pulls to your hand";
            }
            else
            {
                active=false;if(root!=null)root.SetActive(false);matter?.Dispose();matter=null;world=null;cache=null;
                if(debrisMesh!=null)Destroy(debrisMesh);debrisMesh=null;if(root!=null)Destroy(root);root=null;
                source.Clear();originalMaterial.Clear();lease=null;requested=false;retiring=false;initial=0;removalCursor=0;
            }
        }
        public void Restore()
        {if(lease==null)return;retiring=true;status="Restore queued; private owner stays authoritative until batch swap";population.RestoreShrub(lease);}
        bool PlayerCollector(out Int3 collector)
        {
            collector=default;if(boot.Player==null)return false;
            var eye=boot.Player.Eye;double yaw=boot.Player.Yaw*Math.PI/180;
            Local(eye.X+Math.Sin(yaw)*.55,eye.Y,eye.Z+Math.Cos(yaw)*.55,out double x,out double y,out double z);
            if(x<2.4||x>=5.6||z<2.4||z>=5.6||y<.1||y>=3.2)return false;
            collector=new Int3((int)Math.Floor(x/.1),(int)Math.Floor(y/.1),(int)Math.Floor(z/.1));return true;
        }
        bool DestinationBlocked(Int3 cell)
        {
            if(world.Get(cell)!=MaterialId.Air)return false; // Existing source can be withdrawn even at its original terrain overlap.
            Global(cell,out double x,out double y,out double z);
            double ground=boot.WorldField.Sample(x,z).HeightMeters;
            if(y-.05<Math.Ceiling(ground*10)/10-1e-6)return true;
            var feet=boot.Player.Feet;
            double half=.05*(Math.Abs(co)+Math.Abs(si));
            if(x+half>feet.X-.32&&x-half<feet.X+.32&&z+half>feet.Z-.32&&z-half<feet.Z+.32&&y+.05>feet.Y&&y-.05<feet.Y+1.8)return true;
            return population.BlocksBodyExcludingShrub(x-half+1e-6,y-.05+1e-6,z-half+1e-6,x+half-1e-6,y+.05-1e-6,z+half-1e-6);
        }
        void FixedUpdate()
        {
            if(!active||retiring||matter==null)return;
            Int3 collector=default;bool pull=suction&&PlayerCollector(out collector);
            // Reuse the already bounded complete source list, not a large brush
            // full of air. Each tiny existing brush inspects at most8 candidates.
            // One active command plus remaining source indices is the whole queue.
            int candidates=0,writes=0;
            for(int commands=0;commands<16&&removalCursor<source.Count;commands++)
            {
                if(!matter.Pending)
                {
                    var cell=source[removalCursor];
                    if(world.Get(cell)==MaterialId.Air){removalCursor++;continue;}
                    double cellSize=world.VoxelSize;
                    if(!matter.TryBeginRemoval((cell.x+.5)*cellSize,(cell.y+.5)*cellSize,(cell.z+.5)*cellSize,.001,100))break;
                }
                matter.Step(256-candidates,16-writes);candidates+=matter.LastCandidates;writes+=matter.LastMutations;
                if(!matter.Pending)removalCursor++;else break;
                if(candidates==256||writes==16)break;
            }
            matter.StepDebris(32,pull,collector);
            clock+=Time.fixedDeltaTime;if(clock>=.05f){clock=0;RebuildDebris();}
            if(OwnedCells!=initial){status="CONSERVATION FAILURE; motion frozen";retiring=true;Debug.LogError(status);}
            else status="Shrub "+lease.Instance.PrototypeId+": owned "+OwnedCells+"/"+initial+"; source queue "+(source.Count-removalCursor)+"; debris "+matter.DebrisCount+"/512; cargo "+matter.CreditedCells+(suction&&!pull?"; move hand inside local shrub region":"");
        }
        void BoundsLocal(double x0,double y0,double z0,double x1,double y1,double z1,out double a,out double b,out double c,out double d,out double e,out double f)
        {
            Local((x0+x1)*.5,(y0+y1)*.5,(z0+z1)*.5,out double x,out double y,out double z);
            double hx=(x1-x0)*.5,hz=(z1-z0)*.5,ex=Math.Abs(co)*hx+Math.Abs(si)*hz,ez=Math.Abs(si)*hx+Math.Abs(co)*hz;
            a=x-ex;b=y-(y1-y0)*.5;c=z-ez;d=x+ex;e=y+(y1-y0)*.5;f=z+ez;
        }
        bool Overlap(Int3 cell,double a,double b,double c,double d,double e,double f)=>d>cell.x*.1+1e-8&&a<(cell.x+1)*.1-1e-8&&e>cell.y*.1+1e-8&&b<(cell.y+1)*.1-1e-8&&f>cell.z*.1+1e-8&&c<(cell.z+1)*.1-1e-8;
        public bool BlocksBody(double x0,double y0,double z0,double x1,double y1,double z1)
        {
            if(world==null)return false;BoundsLocal(x0,y0,z0,x1,y1,z1,out double a,out double b,out double c,out double d,out double e,out double f);
            foreach(var cell in source)if(MaterialCatalog.BlocksActorBody(world.Get(cell))&&Overlap(cell,a,b,c,d,e,f))return true;
            if(matter!=null)foreach(var voxel in matter.Debris)if(MaterialCatalog.BlocksActorBody(voxel.Matter.Material)&&Overlap(voxel.Cell,a,b,c,d,e,f))return true;
            return false;
        }
        public double WeightedSoftVoxels(WorldPoint feet,double radius,double height)
        {
            if(world==null)return 0;BoundsLocal(feet.X-radius,feet.Y,feet.Z-radius,feet.X+radius,feet.Y+height,feet.Z+radius,out double a,out double b,out double c,out double d,out double e,out double f);
            double total=0;
            double Volume(Int3 cell)=>Math.Max(0,Math.Min(d,(cell.x+1)*.1)-Math.Max(a,cell.x*.1))*Math.Max(0,Math.Min(e,(cell.y+1)*.1)-Math.Max(b,cell.y*.1))*Math.Max(0,Math.Min(f,(cell.z+1)*.1)-Math.Max(c,cell.z*.1))*1000;
            foreach(var cell in source)if(world.Get(cell)==MaterialId.Leaves)total+=Volume(cell);
            if(matter!=null)foreach(var voxel in matter.Debris)if(voxel.Matter.Material==MaterialId.Leaves)total+=Volume(voxel.Cell);
            double extent=radius*(Math.Abs(co)+Math.Abs(si));return extent==0?0:total*radius*radius/(extent*extent);
        }
        public bool TryGetSupportHeight(double x,double z,double radius,double low,double high,out double height)
        {
            double top=double.NegativeInfinity;Local(x,0,z,out double a,out _,out double c);
            void Consider(Int3 cell,MaterialId material)
            {
                if(!MaterialCatalog.BlocksActorBody(material))return;
                double dx=a-Math.Max(cell.x*.1,Math.Min((cell.x+1)*.1,a)),dz=c-Math.Max(cell.z*.1,Math.Min((cell.z+1)*.1,c));
                double y=position.Y+(cell.y+1)*.1-.4;
                if(dx*dx+dz*dz<=radius*radius+1e-10&&y>=low-1e-7&&y<=high+1e-7)top=Math.Max(top,y);
            }
            if(world!=null){foreach(var cell in source)Consider(cell,world.Get(cell));if(matter!=null)foreach(var voxel in matter.Debris)Consider(voxel.Cell,voxel.Matter.Material);}
            height=top;return !double.IsNegativeInfinity(top);
        }
        void RebuildDebris()
        {
            vertices.Clear();normals.Clear();colors.Clear();indices.Clear();
            foreach(var voxel in matter.Debris)
            {
                uint color=voxel.Matter.Tint==0?VoxelColor.Base(voxel.Matter.Material):voxel.Matter.Tint;
                var rgba=new Color32((byte)(color>>16),(byte)(color>>8),(byte)color,VertexAppearance.Pack(voxel.Matter.Material,3));
                for(int axis=0;axis<3;axis++)for(int sign=-1;sign<=1;sign+=2)
                {
                    int start=vertices.Count,u=(axis+1)%3,v=(axis+2)%3;
                    for(int corner=0;corner<4;corner++)
                    {var point=new Vector3(voxel.Cell.x,voxel.Cell.y,voxel.Cell.z);point[axis]+=sign>0?1:0;point[u]+=(corner==1||corner==2)?1:0;point[v]+=corner>=2?1:0;var normal=Vector3.zero;normal[axis]=sign;vertices.Add(point*.1f);normals.Add(normal);colors.Add(rgba);}
                    indices.Add(start);indices.Add(start+(sign>0?1:3));indices.Add(start+2);indices.Add(start);indices.Add(start+2);indices.Add(start+(sign>0?3:1));
                }
            }
            debrisMesh.Clear();debrisMesh.SetVertices(vertices);debrisMesh.SetNormals(normals);debrisMesh.SetColors(colors);debrisMesh.SetTriangles(indices,0);debrisMesh.RecalculateBounds();
        }
        void OnGUI()
        {
            if(!ShowBetaHud||boot==null||!boot.Ready)return;
            GUI.Box(new Rect(12,Screen.height-260,730,100),"EXPERIMENTAL actual shrub breakup — session rollback; no mature tree felling");
            GUI.Label(new Rect(24,Screen.height-234,705,22),status);
            if(GUI.Button(new Rect(24,Screen.height-207,200,28),"U: Break nearby shrub"))BreakNearby();
            if(GUI.Button(new Rect(234,Screen.height-207,180,28),suction?"I: Suction ON":"I: Suction OFF"))succionToggle();
            if(GUI.Button(new Rect(424,Screen.height-207,220,28),"O: Restore + discard cargo"))Restore();
        }
        void OnDisable(){Restore();}
        void OnDestroy(){Restore();}
    }
}
