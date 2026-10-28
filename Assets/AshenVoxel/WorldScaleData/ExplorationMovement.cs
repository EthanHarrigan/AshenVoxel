using System;

namespace AshenVoxel.WorldScale
{
    public interface IExplorationResistance
    {
        double WeightedSoftVoxels(WorldPoint feet,double radius,double height);
    }
    /// <summary>Hard props only, in global meters. Terrain is queried separately.
    /// Soft foliage is intentionally excluded by the registry. No Unity physics
    /// or delayed mesh collider is required for these authoritative queries.</summary>
    public interface IExplorationObstacles
    {
        bool IsReady(double x,double z,double radius);
        bool BlocksBody(double minimumX,double minimumY,double minimumZ,double maximumX,double maximumY,double maximumZ);
        bool TryGetSupportHeight(double x,double z,double radius,double minimumHeight,double maximumHeight,out double height);
    }
    public sealed class EmptyExplorationObstacles:IExplorationObstacles
    {
        public static readonly EmptyExplorationObstacles Instance=new EmptyExplorationObstacles();
        public bool IsReady(double x,double z,double radius)=>true;
        public bool BlocksBody(double x0,double y0,double z0,double x1,double y1,double z1)=>false;
        public bool TryGetSupportHeight(double x,double z,double radius,double minimumHeight,double maximumHeight,out double height){height=0;return false;}
    }
    /// <summary>Optional walkability slope from the underlying terrain surface.
    /// Coordinates are global metres; derivatives are height metres per horizontal metre.
    /// False means unavailable/invalid: ascent fails closed and sliding is suppressed.
    /// Contact remains supplied independently by IExplorationCollision.</summary>
    public interface IExplorationGroundSlope
    {
        bool TryGetGroundGradient(double x,double z,out double gradientX,out double gradientZ);
    }
    public interface IExplorationCollision
    {
        bool IsReady(double x,double z,double radius);
        bool TryGetGroundHeight(double x,double z,out double height);
        bool OverlapsBody(WorldPoint feet,double radius,double height);
        bool TryGetSupportHeight(double x,double z,double radius,double minimumHeight,double maximumHeight,out double height);
    }
    public sealed class FieldExplorationCollision:IExplorationCollision,IExplorationResistance
    {
        public IWorldField Terrain {get;}
        public IExplorationObstacles Obstacles {get;set;}
        public FieldExplorationCollision(IWorldField terrain,IExplorationObstacles obstacles=null)
        {Terrain=terrain??throw new ArgumentNullException(nameof(terrain));Obstacles=obstacles??EmptyExplorationObstacles.Instance;}
        public bool IsReady(double x,double z,double radius)=>Obstacles!=null&&Obstacles.IsReady(x,z,radius)&&TryGetGroundHeight(x,z,out _);
        public bool TryGetGroundHeight(double x,double z,out double height)
        {height=Terrain.Sample(x,z).HeightMeters;return !double.IsNaN(height)&&!double.IsInfinity(height);}
        public double WeightedSoftVoxels(WorldPoint feet,double radius,double height)=>Obstacles is IExplorationResistance soft?soft.WeightedSoftVoxels(feet,radius,height):0;
        public bool OverlapsBody(WorldPoint feet,double radius,double height)=>Obstacles!=null&&Obstacles.BlocksBody(
            feet.X-radius,feet.Y+.002,feet.Z-radius,feet.X+radius,feet.Y+height,feet.Z+radius);
        public bool TryGetSupportHeight(double x,double z,double radius,double minimumHeight,double maximumHeight,out double height)
        {if(Obstacles!=null)return Obstacles.TryGetSupportHeight(x,z,radius,minimumHeight,maximumHeight,out height);height=0;return false;}
    }
    /// <summary>Immutable physical movement controls. Feet and queries are meters,
    /// independent of render origin. The body uses a conservative circular-footprint
    /// AABB for props; smooth terrain contact uses continuous source heights.</summary>
    /// <summary>Opt-in traversal experiment; null on movement settings preserves the legacy solver.
    /// Rates are metres/second squared; timers consume solver time, never wall time.</summary>
    public sealed class ExplorationTraversalSettings
    {
        public readonly double GroundAcceleration,GroundBraking,SprintReleaseBraking,AirAcceleration,CoyoteSeconds,JumpBufferSeconds;
        public readonly double CrouchHeight,CrouchSpeedFactor,SlideMinimumSpeed,SlideBraking;
        public ExplorationTraversalSettings(double groundAcceleration=32,double groundBraking=24,double sprintReleaseBraking=10,
            double airAcceleration=6,double coyoteSeconds=.09,double jumpBufferSeconds=.10,
            double crouchHeight=1.05,double crouchSpeedFactor=.6,double slideMinimumSpeed=5,double slideBraking=3)
        {
            Validate(groundAcceleration,.1,200,nameof(groundAcceleration));Validate(groundBraking,.1,200,nameof(groundBraking));
            Validate(sprintReleaseBraking,.1,200,nameof(sprintReleaseBraking));Validate(airAcceleration,0,100,nameof(airAcceleration));
            Validate(coyoteSeconds,0,.25,nameof(coyoteSeconds));Validate(jumpBufferSeconds,0,.25,nameof(jumpBufferSeconds));
            Validate(crouchHeight,.3,5,nameof(crouchHeight));Validate(crouchSpeedFactor,.1,1,nameof(crouchSpeedFactor));
            Validate(slideMinimumSpeed,.1,100,nameof(slideMinimumSpeed));Validate(slideBraking,.1,100,nameof(slideBraking));
            GroundAcceleration=groundAcceleration;GroundBraking=groundBraking;SprintReleaseBraking=sprintReleaseBraking;
            AirAcceleration=airAcceleration;CoyoteSeconds=coyoteSeconds;JumpBufferSeconds=jumpBufferSeconds;
            CrouchHeight=crouchHeight;CrouchSpeedFactor=crouchSpeedFactor;SlideMinimumSpeed=slideMinimumSpeed;SlideBraking=slideBraking;
        }
        static void Validate(double v,double min,double max,string name)
        {if(double.IsNaN(v)||double.IsInfinity(v)||v<min||v>max)throw new ArgumentOutOfRangeException(name);}
    }
    public sealed class ExplorationMovementSettings
    {
        public readonly double WalkSpeed,SprintSpeed,FlightSpeed,FlightBoost,Gravity,JumpSpeed,Radius,Height,EyeHeight,StepHeight,GroundSnap,MaxSlopeDegrees,Skin,SweepStep;
        public readonly int MaxSubsteps;
        public readonly double DragPerSoftVoxel,MinimumVegetationSpeed;
        public readonly ExplorationTraversalSettings Traversal;
        public ExplorationMovementSettings(double walkSpeed=4,double sprintSpeed=7,double flightSpeed=40,double flightBoost=4,
            double gravity=20,double jumpSpeed=6,double radius=.28,double height=1.8,double eyeHeight=1.65,
            double stepHeight=.32,double groundSnap=.24,double maxSlopeDegrees=50,double skin=.015,double sweepStep=.04,int maxSubsteps=512,
            double dragPerSoftVoxel=.0008,double minimumVegetationSpeed=.65,ExplorationTraversalSettings traversal=null)
        {
            Range(walkSpeed,.1,50,nameof(walkSpeed));Range(sprintSpeed,.1,100,nameof(sprintSpeed));Range(flightSpeed,.1,1000,nameof(flightSpeed));Range(flightBoost,1,10,nameof(flightBoost));
            Range(gravity,.1,100,nameof(gravity));Range(jumpSpeed,0,30,nameof(jumpSpeed));Range(radius,.05,2,nameof(radius));Range(height,.3,5,nameof(height));Range(eyeHeight,.1,height,nameof(eyeHeight));
            Range(stepHeight,0,1,nameof(stepHeight));Range(groundSnap,0,1,nameof(groundSnap));Range(maxSlopeDegrees,1,85,nameof(maxSlopeDegrees));Range(skin,.001,.05,nameof(skin));Range(sweepStep,.005,.045,nameof(sweepStep));
            if(maxSubsteps<8||maxSubsteps>4096)throw new ArgumentOutOfRangeException(nameof(maxSubsteps));
            Range(dragPerSoftVoxel,0,.02,nameof(dragPerSoftVoxel));Range(minimumVegetationSpeed,.2,1,nameof(minimumVegetationSpeed));
            DragPerSoftVoxel=dragPerSoftVoxel;MinimumVegetationSpeed=minimumVegetationSpeed;
            Traversal=traversal;
            WalkSpeed=walkSpeed;SprintSpeed=sprintSpeed;FlightSpeed=flightSpeed;FlightBoost=flightBoost;Gravity=gravity;JumpSpeed=jumpSpeed;Radius=radius;Height=height;EyeHeight=eyeHeight;
            StepHeight=stepHeight;GroundSnap=groundSnap;MaxSlopeDegrees=maxSlopeDegrees;Skin=skin;SweepStep=sweepStep;MaxSubsteps=maxSubsteps;
        }
        static void Range(double v,double min,double max,string name){if(double.IsNaN(v)||double.IsInfinity(v)||v<min||v>max)throw new ArgumentOutOfRangeException(name);}
        public ExplorationMovementSettings WithTraversal(ExplorationTraversalSettings traversal)=>new ExplorationMovementSettings(
            WalkSpeed,SprintSpeed,FlightSpeed,FlightBoost,Gravity,JumpSpeed,Radius,Height,EyeHeight,
            StepHeight,GroundSnap,MaxSlopeDegrees,Skin,SweepStep,MaxSubsteps,DragPerSoftVoxel,MinimumVegetationSpeed,traversal);
    }
    public readonly struct ExplorationMoveInput
    {
        /// <summary>World-oriented horizontal desired motion, normally derived from yaw.</summary>
        public readonly double X,Z,Vertical;
        public readonly bool Sprint,Jump,Crouch;
        public ExplorationMoveInput(double x,double z,double vertical=0,bool sprint=false,bool jump=false,bool crouch=false)
        {Numeric.Finite(x,nameof(x));Numeric.Finite(z,nameof(z));Numeric.Finite(vertical,nameof(vertical));X=x;Z=z;Vertical=vertical;Sprint=sprint;Jump=jump;Crouch=crouch;}
    }
    public sealed class ExplorationMotor
    {
        bool initialGroundSnap=true;
        double coyoteRemaining,jumpBufferRemaining,crouchedBodyHeight;
        bool jumpQueued;
        ExplorationTraversalSettings activeTraversal;
        public IExplorationCollision Collision {get;set;}
        public ExplorationMovementSettings Settings {get;set;}
        public WorldPoint Feet {get;private set;}
        public bool Grounded {get;private set;}
        public bool Flying {get;private set;}
        public bool NoClip {get;set;}
        public bool WaitingForWorld {get;private set;}
        public double VerticalVelocity {get;private set;}
        public double HorizontalVelocityX {get;private set;}
        public double HorizontalVelocityZ {get;private set;}
        public bool Crouched {get;private set;}
        public bool Sliding {get;private set;}
        public double BodyHeight=>Crouched?Math.Min(Settings.Height,crouchedBodyHeight):Settings.Height;
        public double ViewHeight=>Crouched?Math.Min(Settings.EyeHeight,BodyHeight-.15):Settings.EyeHeight;
        /// <summary>Discard traversal momentum and queued intent after focus/input/loading interruptions.
        /// Keep crouched clearance until a subsequent ready tick can safely stand.</summary>
        public void ClearTraversalIntent()
        {HorizontalVelocityX=HorizontalVelocityZ=coyoteRemaining=jumpBufferRemaining=0;jumpQueued=false;Sliding=false;}
        public int LastSubsteps {get;private set;}
        public double SimulatedSeconds {get;private set;}
        public double DeferredSeconds {get;private set;}
        public bool BudgetLimited=>DeferredSeconds>1e-9;
        public double VegetationSpeedFactor {get;private set;}=1;
        public double LastSoftVoxelOverlap {get;private set;}
        public ExplorationMotor(IExplorationCollision collision,WorldPoint feet,ExplorationMovementSettings settings=null)
        {Collision=collision??throw new ArgumentNullException(nameof(collision));Settings=settings??new ExplorationMovementSettings();ResetAt(feet);}
        public void ResetAt(WorldPoint feet,bool flying=false)
        {Feet=feet;Flying=flying;Grounded=false;VerticalVelocity=0;WaitingForWorld=false;DeferredSeconds=0;initialGroundSnap=true;Crouched=false;ClearTraversalIntent();}
        public bool SetFlying(bool flying)
        {
            if(flying&&Crouched&&Collision.OverlapsBody(Feet,Settings.Radius,Settings.Height))return false;
            if(!flying)
            {
                if(!Ready(Feet.X,Feet.Z)||Collision.OverlapsBody(Feet,Settings.Radius,Settings.Height)||!Ground(Feet.X,Feet.Z,out double h)||Feet.Y<h-Settings.Skin)return false;
                NoClip=false;
            }
            Flying=flying;Grounded=false;VerticalVelocity=0;initialGroundSnap=true;Crouched=false;ClearTraversalIntent();return true;
        }
        public void Tick(ExplorationMoveInput input,double deltaSeconds)
        {
            Numeric.Finite(deltaSeconds,nameof(deltaSeconds));if(deltaSeconds<0)throw new ArgumentOutOfRangeException(nameof(deltaSeconds));
            if(Collision==null||Settings==null)throw new InvalidOperationException("Movement needs a collision source and physical settings.");
            LastSubsteps=0;SimulatedSeconds=0;DeferredSeconds=0;WaitingForWorld=false;
            if(activeTraversal!=Settings.Traversal){ClearTraversalIntent();activeTraversal=Settings.Traversal;}
            if(deltaSeconds==0)return;
            bool free=Flying&&NoClip;
            if(!free&&!Ready(Feet.X,Feet.Z)){WaitingForWorld=true;ClearTraversalIntent();if(Settings.Traversal!=null)VerticalVelocity=Math.Min(0,VerticalVelocity);return;}
            bool traversal=!Flying&&Settings.Traversal!=null;
            // Never expand an experimental crouched body into a solid ceiling,
            // even when the experiment is disabled while inside a low passage.
            if(Crouched&&(Flying||Settings.Traversal==null)&&Collision.OverlapsBody(Feet,Settings.Radius,Settings.Height))
            {ClearTraversalIntent();return;}
            if(!traversal)Crouched=false;
            else if(input.Crouch){Crouched=true;crouchedBodyHeight=Math.Min(Settings.Height,Settings.Traversal.CrouchHeight);}
            else if(Crouched&&!Collision.OverlapsBody(Feet,Settings.Radius,Settings.Height))Crouched=false;
            double x=input.X,z=input.Z,vertical=Flying?input.Vertical:0;
            double length=Math.Sqrt(x*x+z*z+vertical*vertical);if(length>1){x/=length;z/=length;vertical/=length;}
            double speed=Flying?Settings.FlightSpeed*(input.Sprint?Settings.FlightBoost:1):input.Sprint?Settings.SprintSpeed:Settings.WalkSpeed;
            VegetationSpeedFactor=1;LastSoftVoxelOverlap=0;
            // One nearby span query per motor tick, never per collision substep.
            // Only horizontal walking changes: jumping/gravity/flight remain stable.
            if(!Flying&&Settings.DragPerSoftVoxel>0&&Collision is IExplorationResistance foliage)
            {
                LastSoftVoxelOverlap=Math.Max(0,foliage.WeightedSoftVoxels(Feet,Settings.Radius,BodyHeight));
                VegetationSpeedFactor=Math.Max(Settings.MinimumVegetationSpeed,1/(1+LastSoftVoxelOverlap*Settings.DragPerSoftVoxel));
                speed*=VegetationSpeedFactor;
            }
            if(!Flying)
            {
                RefreshGround();
                if(!traversal&&input.Jump&&Grounded){VerticalVelocity=Settings.JumpSpeed;Grounded=false;}
                if(traversal&&input.Jump){jumpQueued=true;jumpBufferRemaining=Settings.Traversal.JumpBufferSeconds;}
            }
            // Preserve a bounded simulation remainder rather than taking a long
            // collision jump after a slow frame. The caller may carry this time.
            double dt=Math.Min(.25,deltaSeconds);
            double horizontalSpeed=Math.Sqrt(x*x+z*z)*speed;
            // Current overspeed (e.g. sprint release) must participate in the sweep
            // bound. The steering target cannot exceed max(current, desired).
            if(traversal)horizontalSpeed=Math.Max(horizontalSpeed,Math.Sqrt(HorizontalVelocityX*HorizontalVelocityX+HorizontalVelocityZ*HorizontalVelocityZ));
            double worstSpeed=Flying?Math.Sqrt(x*x+z*z+vertical*vertical)*speed:
                Math.Sqrt(horizontalSpeed*horizontalSpeed+Math.Pow(Math.Max(Math.Abs(VerticalVelocity),traversal&&jumpQueued?Settings.JumpSpeed:0)+Settings.Gravity*dt,2))+Settings.WalkSpeed;
            int needed=(int)Math.Min(int.MaxValue,Math.Ceiling(Math.Max(dt*120,dt*worstSpeed/Settings.SweepStep)));
            if(needed>Settings.MaxSubsteps){dt*=Settings.MaxSubsteps/(double)needed;needed=Settings.MaxSubsteps;}
            int steps=Math.Max(1,needed);double step=dt/steps;
            for(int i=0;i<steps;i++)
            {
                if(!free&&!Ready(Feet.X,Feet.Z)){WaitingForWorld=true;break;}
                if(Flying)
                {
                    if(free)Feet=new WorldPoint(Feet.X+x*speed*step,Feet.Y+vertical*speed*step,Feet.Z+z*speed*step);
                    else {MoveHorizontal(x*speed*step,z*speed*step,false);MoveFlightVertical(vertical*speed*step);}
                }
                else
                {
                    if(traversal)MoveTraversal(input,x,z,speed,step);
                    else MoveHorizontal(x*speed*step,z*speed*step,true);
                    if(traversal&&WaitingForWorld)break;
                    ApplySteepSlide(step);
                    double dy=VerticalVelocity*step-Settings.Gravity*step*step*.5;
                    VerticalVelocity=Math.Max(-80,VerticalVelocity-Settings.Gravity*step);
                    MoveVertical(dy);
                    if(traversal)
                    {
                        coyoteRemaining=Math.Max(0,coyoteRemaining-step);
                        jumpBufferRemaining=Math.Max(0,jumpBufferRemaining-step);
                        if(jumpBufferRemaining<=0)jumpQueued=false;
                    }
                }
                LastSubsteps++;SimulatedSeconds+=step;
            }
            // Intentional streaming holds do not queue old user input, which
            // would otherwise launch the player when collision becomes ready.
            DeferredSeconds=WaitingForWorld?0:Math.Max(0,deltaSeconds-SimulatedSeconds);
            if(WaitingForWorld){ClearTraversalIntent();if(traversal)VerticalVelocity=Math.Min(0,VerticalVelocity);}
        }
        void MoveTraversal(ExplorationMoveInput input,double x,double z,double speed,double step)
        {
            var feel=Settings.Traversal;
            if(Grounded)coyoteRemaining=feel.CoyoteSeconds;
            if(jumpQueued&&(Grounded||coyoteRemaining>0))
            {VerticalVelocity=Settings.JumpSpeed;Grounded=false;coyoteRemaining=0;jumpQueued=false;jumpBufferRemaining=0;}
            double magnitude=Math.Sqrt(HorizontalVelocityX*HorizontalVelocityX+HorizontalVelocityZ*HorizontalVelocityZ);
            Sliding=Crouched&&input.Crouch&&Grounded&&magnitude>=feel.SlideMinimumSpeed;
            double targetX=x*speed,targetZ=z*speed;
            double acceleration=feel.GroundAcceleration;
            if(Sliding)
            {targetX=targetZ=0;acceleration=feel.SlideBraking;}
            else if(!Grounded)
            {
                double wish=Math.Sqrt(x*x+z*z);
                if(wish<1e-12){targetX=HorizontalVelocityX;targetZ=HorizontalVelocityZ;}
                else {double airSpeed=Math.Max(magnitude,speed*wish);targetX=x/wish*airSpeed;targetZ=z/wish*airSpeed;}
                acceleration=feel.AirAcceleration;
            }
            else if(Crouched){targetX*=feel.CrouchSpeedFactor;targetZ*=feel.CrouchSpeedFactor;acceleration=feel.GroundBraking;}
            else if(Math.Abs(x)+Math.Abs(z)<1e-12)acceleration=feel.GroundBraking;
            else if(magnitude>speed)acceleration=feel.SprintReleaseBraking;
            // Exact integration along a straight velocity segment. In free space
            // constant-input travel is independent of substep partition at arrival.
            double vx=HorizontalVelocityX,vz=HorizontalVelocityZ;
            double differenceX=targetX-vx,differenceZ=targetZ-vz,difference=Math.Sqrt(differenceX*differenceX+differenceZ*differenceZ);
            double moving=acceleration>0?Math.Min(step,difference/acceleration):0;
            double fraction=difference>1e-12?Math.Min(1,acceleration*step/difference):1;
            HorizontalVelocityX=vx+differenceX*fraction;HorizontalVelocityZ=vz+differenceZ*fraction;
            double dx=(vx+HorizontalVelocityX)*.5*moving+HorizontalVelocityX*(step-moving);
            double dz=(vz+HorizontalVelocityZ)*.5*moving+HorizontalVelocityZ*(step-moving);
            double beforeX=Feet.X,beforeZ=Feet.Z;
            MoveHorizontal(dx,dz,true);
            // This solver supplies axis rejection rather than contact normals.
            // Remove blocked momentum and retain the successfully swept tangent.
            if(Math.Abs(Feet.X-beforeX-dx)>1e-8)HorizontalVelocityX=0;
            if(Math.Abs(Feet.Z-beforeZ-dz)>1e-8)HorizontalVelocityZ=0;
        }
        bool Ready(double x,double z)=>Collision.IsReady(x,z,Settings.Radius+Settings.SweepStep);
        bool Ground(double x,double z,out double h)=>Collision.TryGetGroundHeight(x,z,out h);
        bool BodyFree(WorldPoint feet)=>!Collision.OverlapsBody(feet,Settings.Radius,BodyHeight);
        bool Support(double x,double z,double min,double max,out double height)
        {
            bool has=Ground(x,z,out double terrain);height=terrain;
            if(!has)return false;
            if(Collision.TryGetSupportHeight(x,z,Settings.Radius,min,max,out double prop))height=Math.Max(height,prop);
            return true;
        }
        void RefreshGround()
        {
            bool maySnap=Grounded||initialGroundSnap;initialGroundSnap=false;
            if(!Support(Feet.X,Feet.Z,Feet.Y-Settings.GroundSnap-Settings.Skin,Feet.Y+Settings.Skin,out double h))return;
            double contact=h+Settings.Skin,distance=Feet.Y-contact;
            if(maySnap&&distance>=-.03&&distance<=Settings.GroundSnap&&VerticalVelocity<=0)
            {
                var candidate=new WorldPoint(Feet.X,contact,Feet.Z);
                if(BodyFree(candidate)){Feet=candidate;Grounded=true;VerticalVelocity=0;return;}
            }
            Grounded=false;
        }
        void MoveHorizontal(double dx,double dz,bool allowStep)
        {
            if(Math.Abs(dx)+Math.Abs(dz)<1e-12)return;
            if(TryHorizontal(dx,dz,allowStep))return;
            // Try the dominant axis first. Remaining motion slides along a wall
            // instead of cancelling both input axes at an ordinary corner.
            if(Math.Abs(dx)>=Math.Abs(dz)){TryHorizontal(dx,0,allowStep);TryHorizontal(0,dz,allowStep);}
            else {TryHorizontal(0,dz,allowStep);TryHorizontal(dx,0,allowStep);}
        }
        bool TryHorizontal(double dx,double dz,bool allowStep)
        {
            if(Math.Abs(dx)+Math.Abs(dz)<1e-12)return false;
            double nx=Feet.X+dx,nz=Feet.Z+dz;
            if(!Ready(nx,nz)){WaitingForWorld=true;return false;}
            if(!Ground(nx,nz,out double ground)||!Ground(Feet.X,Feet.Z,out double oldGround))return false;
            double targetY=Feet.Y;
            if(!Flying)
            {
                if(ground>Feet.Y+Settings.StepHeight+Settings.Skin)return false;
                if(ground>oldGround+.00001&&ground>Feet.Y-.05&&TooSteep(nx,nz))return false;
                if(Grounded&&allowStep&&Support(nx,nz,Feet.Y-Settings.GroundSnap-Settings.Skin,Feet.Y+Settings.StepHeight,out double support))
                {
                    double contact=support+Settings.Skin;
                    if(contact>=Feet.Y-Settings.GroundSnap&&contact<=Feet.Y+Settings.StepHeight+Settings.Skin)targetY=contact;
                }
            }
            else if(targetY<ground+Settings.Skin)return false;
            var candidate=new WorldPoint(nx,targetY,nz);
            if(BodyFree(candidate)){Feet=candidate;return true;}
            if(!Flying&&Grounded&&allowStep&&Collision.TryGetSupportHeight(nx,nz,Settings.Radius,Feet.Y-Settings.Skin,Feet.Y+Settings.StepHeight,out double top))
            {
                var stepped=new WorldPoint(nx,Math.Max(ground,top)+Settings.Skin,nz);
                if(stepped.Y<=Feet.Y+Settings.StepHeight+Settings.Skin&&BodyFree(stepped)){Feet=stepped;VerticalVelocity=0;return true;}
            }
            return false;
        }
        bool TooSteep(double x,double z)
        {
            if(!Gradient(x,z,out double gx,out double gz))return true;
            double maximum=Math.Tan(Settings.MaxSlopeDegrees*Math.PI/180);return gx*gx+gz*gz>maximum*maximum;
        }
        bool Gradient(double x,double z,out double gx,out double gz)
        {
            if(Collision is IExplorationGroundSlope slope)return slope.TryGetGroundGradient(x,z,out gx,out gz);
            const double probe=.3;gx=gz=0;
            if(!Ground(x+probe,z,out double e)||!Ground(x-probe,z,out double w)||!Ground(x,z+probe,out double n)||!Ground(x,z-probe,out double s))return false;
            gx=(e-w)/(probe*2);gz=(n-s)/(probe*2);return true;
        }
        void ApplySteepSlide(double dt)
        {
            if(!Grounded||!Ground(Feet.X,Feet.Z,out double ground)||Feet.Y>ground+Settings.Skin+.03||!Gradient(Feet.X,Feet.Z,out double gx,out double gz))return;
            double length=Math.Sqrt(gx*gx+gz*gz),maximum=Math.Tan(Settings.MaxSlopeDegrees*Math.PI/180);
            if(length<=maximum)return;
            double speed=Math.Min(Settings.WalkSpeed,Settings.WalkSpeed*(length/maximum-1));
            MoveHorizontal(-gx/length*speed*dt,-gz/length*speed*dt,false);
            if(Ground(Feet.X,Feet.Z,out double next)&&next<=ground&&Feet.Y-next-Settings.Skin<=Settings.GroundSnap)
            {
                var contact=new WorldPoint(Feet.X,next+Settings.Skin,Feet.Z);
                if(BodyFree(contact))Feet=contact;
            }
        }
        void MoveFlightVertical(double dy)
        {
            if(Math.Abs(dy)<1e-12)return;
            if(!Ground(Feet.X,Feet.Z,out double h))return;
            double y=Math.Max(Feet.Y+dy,h+Settings.Skin);
            var candidate=new WorldPoint(Feet.X,y,Feet.Z);
            if(BodyFree(candidate))Feet=candidate;
        }
        void MoveVertical(double dy)
        {
            double target=Feet.Y+dy;
            if(dy<=0&&Support(Feet.X,Feet.Z,target-Settings.Skin*2,Feet.Y+Settings.Skin,out double support)&&target<=support+Settings.Skin)
            {
                var contact=new WorldPoint(Feet.X,support+Settings.Skin,Feet.Z);
                if(BodyFree(contact)){Feet=contact;VerticalVelocity=0;Grounded=true;return;}
            }
            var candidate=new WorldPoint(Feet.X,target,Feet.Z);
            if(BodyFree(candidate)){Feet=candidate;Grounded=false;return;}
            // Refine the last safe height without depending on collision meshes.
            double safe=Feet.Y,blocked=target;
            for(int i=0;i<8;i++)
            {double middle=(safe+blocked)*.5;if(BodyFree(new WorldPoint(Feet.X,middle,Feet.Z)))safe=middle;else blocked=middle;}
            Feet=new WorldPoint(Feet.X,safe,Feet.Z);VerticalVelocity=0;Grounded=dy<0;
        }
        /// <summary>Bounded spawn search. Unknown collision regions are not treated
        /// as empty; callers should retry after their streaming queue catches up.</summary>
        public bool TryFindClearSpawn(WorldPoint near,double searchRadius=12)
        {
            Numeric.Finite(searchRadius,nameof(searchRadius));if(searchRadius<0||searchRadius>100)throw new ArgumentOutOfRangeException(nameof(searchRadius));
            for(int ring=0;ring<=6;ring++)
            {
                double radius=searchRadius*ring/6;int count=ring==0?1:16;
                for(int i=0;i<count;i++)
                {
                    double angle=i*Math.PI*2/count,x=near.X+Math.Cos(angle)*radius,z=near.Z+Math.Sin(angle)*radius;
                    if(!Ready(x,z)||!Ground(x,z,out double ground)||TooSteep(x,z))continue;
                    double height=ground;
                    if(Collision.TryGetSupportHeight(x,z,Settings.Radius,ground,ground+Settings.StepHeight,out double top))height=Math.Max(height,top);
                    var candidate=new WorldPoint(x,height+Settings.Skin,z);
                    if(!BodyFree(candidate))continue;
                    ResetAt(candidate);Grounded=true;return true;
                }
            }
            return false;
        }
    }
}
