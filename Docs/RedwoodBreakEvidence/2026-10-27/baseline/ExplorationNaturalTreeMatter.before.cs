using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using AshenVoxel.Core;
using AshenVoxel.Exploration.Experimental;
using AshenVoxel.WorldScale;
using UnityEngine;
using UnityEngine.InputSystem;
using NV=System.Numerics.Vector3;
using NQ=System.Numerics.Quaternion;

namespace AshenVoxel.Exploration
{
    [DefaultExecutionOrder(270)]
    public sealed class ExplorationNaturalTreeMatter:MonoBehaviour,IShrubMatterOwner
    {
        sealed class Activation {public VoxelWorld World;public Int3 Anchor;public NaturalTreePhysics Physics;}
        ExplorationBootstrap boot;VegetationStreaming registry;VegetationStreaming.ShrubLease lease;
        NaturalTreePhysics physics;VoxelWorld world;Int3 anchor;WorldPoint instance;
        NaturalMatterRenderer matterRenderer;Task<Activation> preparation,drainingPreparation;
        NaturalMatterRenderSource renderSource;
        NaturalMatterRigidBodies rigidBodies;
        ExplorationLiveRefinement terrainEvents;
        readonly Dictionary<Int3,float> ground=new Dictionary<Int3,float>();
        readonly Queue<Int3> groundQueue=new Queue<Int3>();readonly HashSet<Int3> groundPending=new HashSet<Int3>();
        bool groundBounds;float groundMinX,groundMinZ,groundMaxX,groundMaxZ;
        bool requested,active,restoring,pendingFirstCut,requireFreshPress;
        bool approached;
        float nextCut,statusUntil,nextApproach,lastApproachAim;
        const float ApproachReach=12,ApproachInterval=.35f,ApproachRetention=2;
        string status="LMB: break a sphere from trees, rocks or plants | automatic pickup | O: undo session";
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
        public int PairContacts=>0;
        public double PhysicsDebtSeconds=>0;
        public int PendingSupport=>physics?.PendingSupport??0;
        public int AwakeDebris=>physics?.AwakeDebris??0;
        public int DormantDebris=>physics?.DormantDebris??0;
        public int PendingWakeCandidates=>physics?.PendingWakeCandidates??0;
        public int PendingGroundColumns=>groundQueue.Count;
        public int CachedGroundColumns=>ground.Count;
        public double LastGroundSamplingMilliseconds {get;private set;}
        public int ExplicitKickCommands {get;private set;}
        public bool ConservationIntact=>physics==null||physics.Conserved;
        public bool SuctionEnabled {get=>physics!=null&&physics.SuctionEnabled;set{if(physics!=null)physics.SuctionEnabled=value;}}
        public long InventoryCount(MaterialId material)=>physics?.Collected(material)??0;
        public static ExplorationNaturalTreeMatter Attach(ExplorationBootstrap boot)
        {
            if(boot==null)throw new ArgumentNullException(nameof(boot));var result=boot.GetComponent<ExplorationNaturalTreeMatter>();
            if(result==null)result=boot.gameObject.AddComponent<ExplorationNaturalTreeMatter>();result.boot=boot;return result;
        }
        void Feedback(string value){status=value;statusUntil=Time.unscaledTime+2;}
        bool PackMatches()
        {
            // Loading already verifies the same immutable bytes. Do not clone and
            // hash the12MB TextAsset on the player's first punch.
            return boot.Population!=null&&boot.Population.PrototypePackIdentity==NaturalTreeTemplate.PackIdentity;
        }
        public bool ActivateNearby()=>Activate(false);
        bool Activate(bool aimed,bool approach=false)
        {
            if(boot==null||!boot.Ready||boot.Player==null||lease!=null||drainingPreparation!=null)return false;
            if(!PackMatches()){Feedback("This prototype pack does not match the breakable matter recipes");return false;}
            registry=boot.Population;if(registry==null)return false;
            var forward=boot.Vista.MainCamera.transform.forward;
            bool reserved=approach?registry.TryReserveNaturalMatterRay(boot.Player.Eye,new WorldPoint(forward.x,forward.y,forward.z),ApproachReach,100000,this,out lease,out _):
                aimed?registry.TryReserveNaturalMatterPunch(boot.Player.Eye,new WorldPoint(forward.x,forward.y,forward.z),100000,this,out lease,out status):registry.TryReserveNaturalTree(boot.Player.Feet,100000,this,out lease,out status);
            if(!reserved)return false;
            if(!registry.TryGetNaturalMatterRenderSource(lease,out renderSource))
            {registry.RestoreShrub(lease);if(!approach)Feedback("Waiting for the nearby specimen's surface");return false;}
            instance=lease.Instance.Position;pendingFirstCut=aimed&&!approach;approached=approach;lastApproachAim=Time.unscaledTime;
            if(!registry.TryPrepareLeasedMatterAsync(lease,out var template,out string reason)){registry.RestoreShrub(lease);Feedback(reason);return false;}
            float yaw=lease.Instance.YawDegrees*Mathf.Deg2Rad;
            preparation=Task.Run(async ()=>
            {
                var prepared=await template.ConfigureAwait(false);var copy=prepared.World;var a=prepared.Anchor;
                var solver=new NaturalTreePhysics(copy,a,yaw);
                return new Activation{World=copy,Anchor=a,Physics=solver};
            });
            Feedback("Preparing exact matter in the background; original remains visible");return true;
        }
        void PrepareApproachingMatter()
        {
            if(Time.unscaledTime<nextApproach||restoring)return;nextApproach=Time.unscaledTime+ApproachInterval;
            if(boot.Player==null||!boot.Player.InputEnabled||!Application.isFocused||Cursor.lockState!=CursorLockMode.Locked)return;
            if(lease==null)
            {
                // Speculative preparation uses the existing single session slot,
                // and yields when the terrain reports system memory pressure.
                var vista=boot.Vista;
                if(drainingPreparation==null&&!vista.VoxelMemoryPressureBlocked&&
                    (vista.VoxelAvailableCommitBytes<0||vista.VoxelAvailableCommitBytes>=vista.VoxelRequiredCommitBytes+512L*1048576))Activate(true,true);
                return;
            }
            if(!approached||physics!=null&&physics.HasCut)return;
            var f=boot.Vista.MainCamera.transform.forward;
            if(registry.NaturalMatterLeaseAimed(lease,boot.Player.Eye,new WorldPoint(f.x,f.y,f.z),ApproachReach))lastApproachAim=Time.unscaledTime;
            else if(!pendingFirstCut&&Time.unscaledTime-lastApproachAim>=ApproachRetention)Restore();
        }
        NV Relative(WorldPoint p)=>new NV((float)(p.X-instance.X),(float)(p.Y-instance.Y),(float)(p.Z-instance.Z));
        static NV Numeric(Vector3 p)=>new NV(p.x,p.y,p.z);
        static Vector3 Unity(NV p)=>new Vector3(p.X,p.Y,p.Z);
        float GroundHeight(float x,float z)
        {
            if(!groundBounds){groundMinX=groundMaxX=x;groundMinZ=groundMaxZ=z;groundBounds=true;}
            else {groundMinX=Math.Min(groundMinX,x);groundMinZ=Math.Min(groundMinZ,z);groundMaxX=Math.Max(groundMaxX,x);groundMaxZ=Math.Max(groundMaxZ,z);}
            var key=new Int3((int)Math.Floor(x*10),0,(int)Math.Floor(z*10));
            if(ground.TryGetValue(key,out float height))return height;
            if(ground.Count+groundPending.Count<131072&&groundPending.Count<4096&&groundPending.Add(key))groundQueue.Enqueue(key);
            return float.NaN;
        }
        void TrackTerrainChanges()
        {
            var next=physics!=null&&boot!=null?boot.LiveRefinement:null;if(terrainEvents==next)return;
            UntrackTerrainChanges();terrainEvents=next;if(terrainEvents!=null)terrainEvents.RegionChanged+=NotifyTerrainChanged;
        }
        void UntrackTerrainChanges(){if(terrainEvents!=null)terrainEvents.RegionChanged-=NotifyTerrainChanged;terrainEvents=null;}
        // Global metre coordinates. Future terrain-edit publishers can use this
        // same entrypoint; source publication and retirement already call it.
        public void NotifyTerrainChanged(ExplorationRefinementBounds bounds)
        {
            if(physics==null)return;
            float minX=(float)(bounds.MinimumX-instance.X),minZ=(float)(bounds.MinimumZ-instance.Z);
            float maxX=(float)(bounds.MaximumX-instance.X),maxZ=(float)(bounds.MaximumZ-instance.Z);
            if(groundBounds&&maxX>=groundMinX-.1f&&minX<=groundMaxX+.1f&&maxZ>=groundMinZ-.1f&&minZ<=groundMaxZ+.1f)
                ground.Clear(); // Bounded cache; queued samples read the current surface when serviced.
            physics.NotifyTerrainChanged(minX,minZ,maxX,maxZ);
        }
        void CaptureGround()
        {
            LastGroundSamplingMilliseconds=0;var sampler=boot.Vista.SurfaceSampler;if(sampler==null||groundQueue.Count==0)return;
            long started=Stopwatch.GetTimestamp();
            for(int budget=0;groundQueue.Count>0&&budget<64;budget++)
            {
                if(budget>0&&(Stopwatch.GetTimestamp()-started)*1000.0/Stopwatch.Frequency>=.75)break;
                var key=groundQueue.Dequeue();groundPending.Remove(key);
                var column=sampler.EvaluateColumn(instance.X+(key.x+.5)*.1,instance.Z+(key.z+.5)*.1);
                ground[key]=(float)(VoxelTerrainPage.SurfaceTop(column.SurfaceHeightMeters)-instance.Y);
            }
            LastGroundSamplingMilliseconds=(Stopwatch.GetTimestamp()-started)*1000.0/Stopwatch.Frequency;
        }
        void Update()
        {
            if(boot==null||!boot.Ready)return;
            if(drainingPreparation!=null&&drainingPreparation.IsCompleted)
            {if(drainingPreparation.IsFaulted){var observed=drainingPreparation.Exception;}drainingPreparation=null;}
            PrepareApproachingMatter();
            if(pendingFirstCut&&(Mouse.current==null||!Mouse.current.leftButton.isPressed||!Application.isFocused||Cursor.lockState!=CursorLockMode.Locked))pendingFirstCut=false;
            if(preparation!=null&&preparation.IsCompleted&&!restoring)
            {
                var task=preparation;preparation=null;
                try
                {
                    var result=task.GetAwaiter().GetResult();world=result.World;anchor=result.Anchor;physics=result.Physics;physics.TerrainHeight=GroundHeight;TrackTerrainChanges();
                    var template=Resources.Load<Material>("VoxelSurface");if(template==null)throw new InvalidOperationException("VoxelSurface unavailable");
                    matterRenderer=new NaturalMatterRenderer(boot.transform,template);matterRenderer.SetPhysics(physics,renderSource);matterRenderer.SetVisible(false);
                    rigidBodies=new NaturalMatterRigidBodies(boot.transform,physics,world,instance,boot.Vista.SurfaceSampler);
                    Feedback("Matter ready; preparing bounded surface geometry");
                }
                catch(Exception error){Feedback("Original retained: "+error.Message);Restore();}
            }
            if(matterRenderer!=null&&physics!=null)
            {
                if(active&&!restoring)physics.StepSupport(32768,1.25);
                var offset=RenderOffset();matterRenderer.Tick(offset);rigidBodies?.Tick(offset,matterRenderer.Current);CaptureGround();
                if(rigidBodies?.Error!=null||matterRenderer.Error!=null){Feedback("Original retained: "+(rigidBodies?.Error??matterRenderer.Error));Restore();}
                if(!requested&&!restoring&&matterRenderer.Ready&&rigidBodies!=null&&rigidBodies.Ready){requested=true;if(!registry.RequestShrubOwnership(lease))Restore();}
            }
            if(boot.Player==null||!boot.Player.InputEnabled)return;
            var keys=Keyboard.current;
            if(keys!=null)
            {
                if(keys.tKey.wasPressedThisFrame)ActivateNearby();
                if(keys.iKey.wasPressedThisFrame&&physics!=null)physics.SuctionEnabled=!physics.SuctionEnabled;
                if(keys.oKey.wasPressedThisFrame)Restore();
                if(keys.gKey.wasPressedThisFrame)Kick();
            }
            var mouse=Mouse.current;
            if(mouse!=null&&mouse.leftButton.wasPressedThisFrame&&active)requireFreshPress=false;
            if(mouse!=null&&Application.isFocused&&Cursor.lockState==CursorLockMode.Locked&&
                (mouse.leftButton.wasPressedThisFrame||mouse.leftButton.isPressed&&!requireFreshPress&&active&&!restoring&&physics!=null&&physics.PendingSupport==0&&physics.PendingCuts==0&&matterRenderer!=null&&matterRenderer.Current&&Time.unscaledTime>=nextCut))
            {CutTrunk();nextCut=Time.unscaledTime+.15f;}
        }
        public bool CutTrunk()
        {
            if(lease==null)return Activate(true);
            if(!active||restoring)
            {
                if(!restoring&&approached)
                {
                    var f=boot.Vista.MainCamera.transform.forward;
                    pendingFirstCut=registry.NaturalMatterLeaseAimed(lease,boot.Player.Eye,new WorldPoint(f.x,f.y,f.z),NaturalMatterPunch.Reach);
                }
                Feedback("Specimen preparation is still pending");return false;
            }
            if(physics.PendingSupport>0||physics.PendingCuts>0){Feedback("Checking the last cut; no delayed extra cut queued");return false;}
            bool cut=physics.CutPunch(Relative(boot.Player.Eye),Numeric(boot.Vista.MainCamera.transform.forward));if(cut)approached=false;
            Feedback(cut?"Sphere broken; checking connected support":"Aim at remaining tree, rock or plant within 2.5 m");return cut;
        }
        public int Kick()
        {
            if(!active||restoring)return 0;int count=physics.Kick(Relative(boot.Player.Eye),Numeric(boot.Vista.MainCamera.transform.forward));
            if(count>0)ExplicitKickCommands++;return count;
        }
        public void PublishShrubOwnership(bool value)
        {
            if(value)
            {
                active=true;matterRenderer.SetVisible(true);Feedback("Breakable matter active; nearby debris collects automatically");
                requireFreshPress=true;
                if(pendingFirstCut)
                {
                    pendingFirstCut=false;
                    // Async readiness is not a new click. Only a still-held gameplay
                    // gesture may continue, using the live aim/reach rather than old coordinates.
                    var mouse=Mouse.current;
                    if(mouse!=null&&mouse.leftButton.isPressed&&Application.isFocused&&boot.Player!=null&&boot.Player.InputEnabled&&Cursor.lockState==CursorLockMode.Locked)
                    {requireFreshPress=false;physics.CutPunch(Relative(boot.Player.Eye),Numeric(boot.Vista.MainCamera.transform.forward));}
                }
            }
            else
            {
                active=false;UntrackTerrainChanges();rigidBodies?.Dispose();rigidBodies=null;physics=null;world=null;matterRenderer?.Dispose();matterRenderer=null;
                // An abandoned background copy/solver must finish before another
                // large specimen starts; observe faults without replaying its result.
                if(preparation!=null){drainingPreparation=preparation;preparation.ContinueWith(t=>{var observed=t.Exception;},TaskContinuationOptions.OnlyOnFaulted);preparation=null;}
                lease=null;renderSource=null;requested=restoring=pendingFirstCut=requireFreshPress=approached=false;ground.Clear();groundPending.Clear();groundQueue.Clear();groundBounds=false;
            }
        }
        // Activation preserves the original hard-cell collision mask. Being close is allowed.
        public bool CanPublishShrubOwnership(bool value)=>value?!restoring&&matterRenderer!=null&&matterRenderer.Ready&&rigidBodies!=null&&rigidBodies.Ready:OriginalClearOfPlayer();
        bool OriginalClearOfPlayer()
        {
            if(lease==null||boot.Player==null)return true;var p=boot.Player.Feet;
            return !SourceBox(p.X-.3,p.Y,p.Z-.3,p.X+.3,p.Y+1.8,p.Z+.3,true);
        }
        public void Restore()
        {
            nextApproach=Time.unscaledTime+ApproachRetention;if(lease==null)return;restoring=true;
            Feedback("Undo session pending; step clear of original trunk/rock. Session inventory is rolled back.");registry.RestoreShrub(lease);
        }
        void FixedUpdate()
        {
            if(!active||restoring||physics==null)return;
            physics.Hand=Relative(boot.Player.Eye)+Numeric(boot.Vista.MainCamera.transform.forward)*.55f;
            physics.MovementEnabled=matterRenderer!=null&&matterRenderer.Current;
            rigidBodies?.ReadBack(Time.fixedDeltaTime);
            physics.Step(Time.fixedDeltaTime,0);
            if(Time.unscaledTime>=statusUntil)status="Attached "+physics.SourceCells+" | falling "+physics.IslandCells+" | debris "+physics.IndividualCells+" cells ("+physics.AwakeDebris+" awake / "+physics.DormantDebris+" dormant) | collected "+physics.CargoCells+" | pending support "+physics.PendingSupport+" / wake "+physics.PendingWakeCandidates;
        }
        Vector3 RenderOffset()
        {
            var origin=boot.Vista.RenderOrigin;double x=instance.X-origin.X,y=instance.Y-origin.Y,z=instance.Z-origin.Z;
            if(Math.Abs(x)>10000||Math.Abs(y)>10000||Math.Abs(z)>10000){Restore();return Vector3.zero;}
            return new Vector3((float)x,(float)y,(float)z);
        }
        void LocalBounds(double x0,double y0,double z0,double x1,double y1,double z1,out NV min,out NV max)
        {
            float yaw=lease.Instance.YawDegrees*Mathf.Deg2Rad;var q=NQ.CreateFromAxisAngle(NV.UnitY,-yaw);
            var c=NV.Transform(new NV((float)((x0+x1)*.5-instance.X),(float)((y0+y1)*.5-instance.Y),(float)((z0+z1)*.5-instance.Z)),q);
            float co=Math.Abs((float)Math.Cos(yaw)),si=Math.Abs((float)Math.Sin(yaw));
            var e=new NV((float)((x1-x0)*.5*co+(z1-z0)*.5*si),(float)((y1-y0)*.5),(float)((x1-x0)*.5*si+(z1-z0)*.5*co));min=c-e;max=c+e;
        }
        bool SourceBox(double x0,double y0,double z0,double x1,double y1,double z1,bool original)
        {
            if(lease==null)return false;LocalBounds(x0,y0,z0,x1,y1,z1,out var min,out var max);
            int ax=(int)Math.Floor(min.X*10),ay=(int)Math.Floor(min.Y*10),az=(int)Math.Floor(min.Z*10);
            int bx=(int)Math.Ceiling(max.X*10),by=(int)Math.Ceiling(max.Y*10),bz=(int)Math.Ceiling(max.Z*10);
            for(int z=az;z<bz;z++)for(int x=ax;x<bx;x++)for(int y=ay;y<by;y++)
            {
                if(!original&&(world==null||world.Get(x+anchor.x,y+anchor.y,z+anchor.z)==MaterialId.Air))continue;
                if(registry.OriginalHardCell(lease,x,y,z))return true;
            }
            return false;
        }
        public bool BlocksBody(double x0,double y0,double z0,double x1,double y1,double z1)=>SourceBox(x0,y0,z0,x1,y1,z1,false);
        public double WeightedSoftVoxels(WorldPoint feet,double radius,double height)=>0;
        public bool TryGetSupportHeight(double x,double z,double radius,double low,double high,out double height)
        {
            height=double.NegativeInfinity;if(world==null||lease==null)return false;
            LocalBounds(x-radius,low,z-radius,x+radius,high,z+radius,out var min,out var max);
            int ax=(int)Math.Floor(min.X*10),az=(int)Math.Floor(min.Z*10),bx=(int)Math.Ceiling(max.X*10),bz=(int)Math.Ceiling(max.Z*10);
            for(int iy=(int)Math.Floor(max.Y*10);iy>=(int)Math.Floor(min.Y*10);iy--)
                for(int iz=az;iz<=bz;iz++)for(int ix=ax;ix<=bx;ix++)
                    if(world.Get(ix+anchor.x,iy+anchor.y,iz+anchor.z)!=MaterialId.Air&&registry.OriginalHardCell(lease,ix,iy,iz))
                    {double top=instance.Y+(iy+1)*.1;if(top>=low&&top<=high)height=Math.Max(height,top);}
            return !double.IsNegativeInfinity(height);
        }
        void OnGUI()
        {
            if(!ShowBetaHud||boot==null||!boot.Ready)return;
            GUI.Box(new Rect(12,Screen.height-240,1040,125),"BREAKABLE MATTER — automatic aim / pickup; one session, O rolls back");
            GUI.Label(new Rect(24,Screen.height-214,1010,24),status);
            string counts="Inventory:";for(int i=1;i<MaterialCatalog.Count;i++){long n=InventoryCount((MaterialId)i);if(n>0)counts+="  "+MaterialCatalog.Get((MaterialId)i).Name+" "+n;}
            GUI.Label(new Rect(24,Screen.height-187,1010,24),counts);
            GUI.Label(new Rect(24,Screen.height-160,1010,24),"LMB sphere · 0.25 m radius · 2.5 m reach | I toggle pickup | O undo session");
            if(Cursor.lockState==CursorLockMode.Locked)GUI.Label(new Rect(Screen.width*.5f-5,Screen.height*.5f-10,20,20),"+");
        }
        void OnEnable(){TrackTerrainChanges();}
        void OnDisable(){UntrackTerrainChanges();Restore();}
        void OnDestroy(){UntrackTerrainChanges();Restore();rigidBodies?.Dispose();rigidBodies=null;matterRenderer?.Dispose();matterRenderer=null;}
    }
}
