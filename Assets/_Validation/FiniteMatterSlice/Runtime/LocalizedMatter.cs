using System;
using System.Collections.Generic;
using AshenVoxel.Core;

namespace AshenVoxel.Core.Experimental
{
    public readonly struct CargoKey : IEquatable<CargoKey>
    {
        public readonly MaterialId Material; public readonly uint Tint;
        public CargoKey(MaterialId material,uint tint){Material=material;Tint=tint;}
        public bool Equals(CargoKey other)=>Material==other.Material&&Tint==other.Tint;
        public override bool Equals(object other)=>other is CargoKey key&&Equals(key);
        public override int GetHashCode()=>((int)Material*397)^Tint.GetHashCode();
    }
    public readonly struct DebrisVoxel
    {
        public readonly long Id;public readonly Int3 Cell;public readonly CargoKey Matter;
        public DebrisVoxel(long id,Int3 cell,CargoKey matter){Id=id;Cell=cell;Matter=matter;}
    }
    public enum MatterOperation { None,RemoveSphere,FindIsland,DropIsland,BreakIsland,CapacityBlocked }

    /// <summary>CPU experiment for ONE explicit finite-region operation at a time.
    /// All world mutations use Set. Removed matter transfers to bounded debris,
    /// then to an exactly-once bounded cargo ledger. No Unity or alternate world.
    /// Single-threaded ownership is required; Changed listeners must not mutate
    /// reentrantly. Caller schedules existing granular Simulation separately.
    /// Candidate and mutation budgets count work, not elapsed milliseconds.</summary>
    public sealed class LocalizedMatter : IDisposable
    {
        readonly VoxelWorld world;readonly Int3 min,max;
        readonly int maxIsland,maxDebris,maxCargoKeys;
        readonly CargoKey[] transferPayload;
        readonly List<DebrisVoxel> debris=new List<DebrisVoxel>();
        readonly HashSet<Int3> debrisCells=new HashSet<Int3>();
        readonly Dictionary<CargoKey,long> cargo=new Dictionary<CargoKey,long>();
        readonly IReadOnlyList<DebrisVoxel> debrisView;readonly IReadOnlyDictionary<CargoKey,long> cargoView;
        readonly HashSet<long> debrisProcessed=new HashSet<long>();
        readonly List<Int3> island=new List<Int3>();readonly HashSet<Int3> islandSet=new HashSet<Int3>();
        readonly Queue<Int3> frontier=new Queue<Int3>();
        bool internalWrite,disposed,islandInvalidated,breakIsland;
        Int3 seed,brushMin,brushMax;int brushX,brushY,brushZ,debrisCursor;
        double brushCenterX,brushCenterY,brushCenterZ,brushRadius,brushPower;
        long nextDebrisId=1;
        public MatterOperation Operation {get;private set;}
        public string Status {get;private set;}="Idle";
        public int LastCandidates {get;private set;}
        public int LastMutations {get;private set;}
        public int LastDebrisCandidates {get;private set;}
        public int LastDebrisMoves {get;private set;}
        public int LastCollections {get;private set;}
        public long TotalWithdrawnCells {get;private set;}
        public long CreditedCells {get;private set;}
        public long RigidCellSteps {get;private set;}
        public int DebrisCount=>debris.Count;
        public int CargoKinds=>cargo.Count;
        public int IslandCells=>island.Count;
        public int PendingSearchCandidates=>frontier.Count;
        public bool Pending=>Operation!=MatterOperation.None;
        public IReadOnlyList<DebrisVoxel> Debris=>debrisView;
        public IReadOnlyDictionary<CargoKey,long> Cargo=>cargoView;
        public long PendingRemovalCandidates
        {
            get
            {
                if(Operation!=MatterOperation.RemoveSphere)return 0;
                long nx=brushMax.x-brushMin.x+1,ny=brushMax.y-brushMin.y+1;
                return ((long)brushMax.z-brushZ)*nx*ny+((long)brushMax.y-brushY)*nx+brushMax.x-brushX+1;
            }
        }
        static readonly Int3[] Directions={new Int3(1,0,0),new Int3(-1,0,0),new Int3(0,1,0),new Int3(0,-1,0),new Int3(0,0,1),new Int3(0,0,-1)};
        public LocalizedMatter(VoxelWorld world,Int3 minimum,Int3 maximum,int maximumIslandCells=256,int maximumDebrisCells=512,int maximumCargoKinds=256)
        {
            this.world=world??throw new ArgumentNullException(nameof(world));
            if(!world.InBounds(minimum)||!world.InBounds(maximum)||minimum.x>maximum.x||minimum.y>maximum.y||minimum.z>maximum.z)throw new ArgumentOutOfRangeException(nameof(minimum));
            long volume=(long)(maximum.x-minimum.x+1)*(maximum.y-minimum.y+1)*(maximum.z-minimum.z+1);
            if(volume>32768||maximumIslandCells<1||maximumIslandCells>4096||maximumDebrisCells<1||maximumDebrisCells>4096||maximumCargoKinds<1||maximumCargoKinds>4096)throw new ArgumentOutOfRangeException("Explicit bounded region/capacity required.");
            min=minimum;max=maximum;maxIsland=maximumIslandCells;maxDebris=maximumDebrisCells;maxCargoKeys=maximumCargoKinds;
            transferPayload=new CargoKey[maxIsland];
            debrisView=debris.AsReadOnly();cargoView=new System.Collections.ObjectModel.ReadOnlyDictionary<CargoKey,long>(cargo);
            world.Changed+=OnChanged;world.Cleared+=OnCleared;
        }
        public int DebrisCapacityRemaining=>maxDebris-debris.Count;
        // Host supplies ONE composed actor ownership query. The excluded owner
        // is nonnull only during that owner's atomic aggregate->fragment transfer.
        // Query must be pure and must not recurse through this sink.IsOccupied.
        public Func<Int3,object,bool> AdditionalOccupancy {get;set;}
        bool ExtraOccupied(Int3 p,object excluding=null)=>AdditionalOccupancy!=null&&AdditionalOccupancy(p,excluding);
        internal bool TryAdmitAggregateFragment(Int3 p,CargoKey matter,object releasingOwner=null)
        {
            Alive();if(!MaterialCatalog.IsValid(matter.Material)||matter.Material==MaterialId.Air||!VoxelColor.IsValid(matter.Tint))throw new ArgumentOutOfRangeException(nameof(matter));
            if(!Inside(p)||p.y<1||world.Get(p)!=MaterialId.Air||debrisCells.Contains(p)||ExtraOccupied(p,releasingOwner)||debris.Count>=maxDebris||nextDebrisId==long.MaxValue)return false;
            debris.Add(new DebrisVoxel(nextDebrisId++,p,matter));debrisCells.Add(p);return true;
        }
        internal bool TryCreditAggregateMatter(CargoKey matter)
        {
            Alive();if(!MaterialCatalog.IsValid(matter.Material)||matter.Material==MaterialId.Air||!VoxelColor.IsValid(matter.Tint))throw new ArgumentOutOfRangeException(nameof(matter));
            cargo.TryGetValue(matter,out long count);
            if(count==long.MaxValue||CreditedCells==long.MaxValue||count==0&&cargo.Count>=maxCargoKeys)return false;
            cargo[matter]=count+1;CreditedCells++;return true;
        }
        public bool IsOccupied(Int3 p)=>debrisCells.Contains(p)||ExtraOccupied(p);
        /// <summary>Host edit facade: rejects placing new matter through detached debris.
        /// Raw VoxelWorld.Set remains available; integration must route edits here.</summary>
        public bool TrySetWorld(Int3 p,MaterialId material,uint tint=0)
        {Alive();return (material==MaterialId.Air||!IsOccupied(p))&&world.Set(p,material,tint);}
        bool Inside(Int3 p)=>p.x>=min.x&&p.y>=min.y&&p.z>=min.z&&p.x<=max.x&&p.y<=max.y&&p.z<=max.z;
        static bool Finite(double value)=>!double.IsNaN(value)&&!double.IsInfinity(value);
        static bool Coherent(MaterialId id){var d=MaterialCatalog.Get(id);return d.Solid&&!d.Granular;}
        void Alive(){if(disposed)throw new ObjectDisposedException(nameof(LocalizedMatter));}
        void OnChanged(Int3 ignored){if(!internalWrite&&Operation!=MatterOperation.None&&Operation!=MatterOperation.RemoveSphere)islandInvalidated=true;}
        void OnCleared(){if(Operation!=MatterOperation.None&&Operation!=MatterOperation.RemoveSphere)islandInvalidated=true;}
        public void Dispose(){if(disposed)return;world.Changed-=OnChanged;world.Cleared-=OnCleared;disposed=true;}
        /// <summary>Explicit cancellation never deletes matter, debris or cargo.</summary>
        public void Cancel(){Alive();Operation=MatterOperation.None;frontier.Clear();island.Clear();islandSet.Clear();islandInvalidated=false;Status="Cancelled; matter retained";}
        public bool TryBeginRemoval(double x,double y,double z,double radius,double power)
        {
            Alive();if(!Finite(x)||!Finite(y)||!Finite(z)||!Finite(radius)||!Finite(power)||radius<0||power<0)throw new ArgumentOutOfRangeException(nameof(radius));
            if(Pending)return false;
            double s=world.VoxelSize;
            int Low(double c,int lower,int upper)=>(int)Math.Max(lower,Math.Min(upper+1,Math.Floor((c-radius)/s)));
            int High(double c,int lower,int upper)=>(int)Math.Max(lower-1,Math.Min(upper,Math.Ceiling((c+radius)/s)));
            brushMin=new Int3(Low(x,min.x,max.x),Low(y,Math.Max(1,min.y),max.y),Low(z,min.z,max.z));
            brushMax=new Int3(High(x,min.x,max.x),High(y,Math.Max(1,min.y),max.y),High(z,min.z,max.z));
            if(brushMin.x>brushMax.x||brushMin.y>brushMax.y||brushMin.z>brushMax.z){Status="Empty brush";return true;}
            brushX=brushMin.x;brushY=brushMin.y;brushZ=brushMin.z;brushCenterX=x;brushCenterY=y;brushCenterZ=z;brushRadius=radius;brushPower=power;
            Operation=MatterOperation.RemoveSphere;Status="Removal queued";return true;
        }
        /// <summary>Caller explicitly seeds a coherent component after an edit.
        /// Drop mode moves it down one cell per completed search/commit, until
        /// supported. Break mode transfers an unsupported component to debris.
        /// No arbitrary impact threshold, torque, fracture strength or fluid rule.</summary>
        public bool TryBeginIsland(Int3 start,bool breakUnsupported=false)
        {
            Alive();if(!Inside(start))throw new ArgumentOutOfRangeException(nameof(start));if(Pending)return false;
            seed=start;breakIsland=breakUnsupported;RestartSearch();return true;
        }
        void RestartSearch()
        {
            frontier.Clear();island.Clear();islandSet.Clear();islandInvalidated=false;
            if(!Inside(seed)||!Coherent(world.Get(seed))){Operation=MatterOperation.None;Status="Seed no longer contains coherent matter";return;}
            frontier.Enqueue(seed);island.Add(seed);islandSet.Add(seed);Operation=MatterOperation.FindIsland;Status="Searching coherent support";
        }
        void Complete(string status){Operation=MatterOperation.None;frontier.Clear();island.Clear();islandSet.Clear();Status=status;}
        void AdvanceBrush()
        {
            if(++brushX<=brushMax.x)return;brushX=brushMin.x;
            if(++brushY<=brushMax.y)return;brushY=brushMin.y;
            if(++brushZ>brushMax.z)Complete("Removal complete");
        }
        void Withdraw(Int3 p)
        {
            var matter=new CargoKey(world.Get(p),world.GetTint(p));
            debris.Add(new DebrisVoxel(nextDebrisId++,p,matter));debrisCells.Add(p);
            world.Set(p,MaterialId.Air);TotalWithdrawnCells++;
        }
        public void Step(int candidateBudget,int mutationBudget)
        {
            Alive();if(candidateBudget<0||mutationBudget<0)throw new ArgumentOutOfRangeException(nameof(candidateBudget));
            LastCandidates=0;LastMutations=0;
            if(islandInvalidated)RestartSearch();
            if(Operation==MatterOperation.RemoveSphere)
            {
                while(Pending&&LastCandidates<candidateBudget)
                {
                    Int3 p=new Int3(brushX,brushY,brushZ);LastCandidates++;
                    double s=world.VoxelSize,dx=(p.x+.5)*s-brushCenterX,dy=(p.y+.5)*s-brushCenterY,dz=(p.z+.5)*s-brushCenterZ;
                    MaterialId material=world.Get(p);
                    bool inSphere=brushRadius==0?dx==0&&dy==0&&dz==0:(dx/brushRadius)*(dx/brushRadius)+(dy/brushRadius)*(dy/brushRadius)+(dz/brushRadius)*(dz/brushRadius)<=1;
                    if(inSphere&&material!=MaterialId.Air&&MaterialCatalog.Get(material).Hardness<=brushPower)
                    {
                        if(LastMutations>=mutationBudget){Status="Waiting for mutation budget";break;}
                        if(debris.Count>=maxDebris||IsOccupied(p)||nextDebrisId==long.MaxValue){Status="Waiting for debris capacity/ownership; source retained";break;}
                        internalWrite=true;try{Withdraw(p);}finally{internalWrite=false;}LastMutations++;
                    }
                    AdvanceBrush();
                }
                return;
            }
            while(Operation==MatterOperation.FindIsland&&frontier.Count>0&&LastCandidates<candidateBudget)
            {
                Int3 p=frontier.Dequeue();LastCandidates++;
                if(p.y==0){Complete("Anchored at bedrock");return;}
                foreach(Int3 d in Directions)
                {
                    Int3 q=p+d;if(!Coherent(world.Get(q)))continue;
                    if(!Inside(q)){Complete("Conservatively anchored to matter outside region");return;}
                    if(islandSet.Contains(q))continue;
                    if(island.Count==maxIsland){Operation=MatterOperation.CapacityBlocked;Status="Island limit reached; source retained until edit or explicit cancellation";return;}
                    islandSet.Add(q);island.Add(q);frontier.Enqueue(q);
                }
            }
            if(Operation==MatterOperation.FindIsland&&frontier.Count==0){Operation=breakIsland?MatterOperation.BreakIsland:MatterOperation.DropIsland;Status="Component ready for atomic preflight";}
            if(Operation!=MatterOperation.DropIsland&&Operation!=MatterOperation.BreakIsland)return;
            // Preflight has an explicit whole-component cost. Do not pretend
            // partial validation permits a partially moved rigid component.
            if(candidateBudget-LastCandidates<island.Count){Status="Waiting for whole-component preflight budget";return;}
            foreach(Int3 p in island)
            {
                LastCandidates++;Int3 to=p+new Int3(0,-1,0);
                if(to.y<1||!Inside(to)||(!islandSet.Contains(to)&&(world.Get(to)!=MaterialId.Air||IsOccupied(to)))){Complete("Component supported or at region boundary");return;}
            }
            int writes=island.Count*(breakIsland?1:2);
            if(mutationBudget<writes){Status="Waiting for atomic mutation budget";return;}
            if(breakIsland&&(maxDebris-debris.Count<island.Count||nextDebrisId>long.MaxValue-island.Count)){Status="Waiting for whole-island debris capacity; source retained";return;}
            if(breakIsland)foreach(Int3 p in island)if(IsOccupied(p)){Status="Waiting for matter ownership; source retained";return;}
            if(!breakIsland)for(int i=0;i<island.Count;i++)transferPayload[i]=new CargoKey(world.Get(island[i]),world.GetTint(island[i]));
            internalWrite=true;
            try
            {
                if(breakIsland){foreach(Int3 p in island)Withdraw(p);}
                else
                {
                    foreach(Int3 p in island)world.Set(p,MaterialId.Air);
                    for(int i=0;i<island.Count;i++)world.Set(island[i]+new Int3(0,-1,0),transferPayload[i].Material,transferPayload[i].Tint);
                    RigidCellSteps+=island.Count;
                }
            }
            finally{internalWrite=false;}
            LastMutations=writes;
            if(breakIsland)Complete("Unsupported component became debris");
            else{seed+=new Int3(0,-1,0);RestartSearch();Status="Rigid one-cell descent; support revalidation pending";}
        }
        /// <summary>Each call moves each processed debris record at most one cell.
        /// Suction is a grid-axis pull with obstacle checks, not a fluid or force
        /// solver. Without suction it uses downward gravity. Capture is exact cell.
        /// Cargo capacity/obstacles retain debris; they never award partial credit.</summary>
        public void StepDebris(int candidateBudget,bool suction,Int3 collector)
        {
            Alive();if(candidateBudget<0)throw new ArgumentOutOfRangeException(nameof(candidateBudget));
            if(suction&&(!Inside(collector)||collector.y<1))throw new ArgumentOutOfRangeException(nameof(collector));
            LastDebrisCandidates=0;LastDebrisMoves=0;LastCollections=0;debrisProcessed.Clear();int initialCount=debris.Count;
            while(debris.Count>0&&LastDebrisCandidates<candidateBudget&&debrisProcessed.Count<initialCount)
            {
                if(debrisCursor>=debris.Count)debrisCursor=0;DebrisVoxel item=debris[debrisCursor];
                if(!debrisProcessed.Add(item.Id))break;LastDebrisCandidates++;
                if(suction&&item.Cell==collector)
                {
                    cargo.TryGetValue(item.Matter,out long old);
                    if(world.Get(collector)==MaterialId.Air&&!ExtraOccupied(collector)&&old<long.MaxValue&&(old>0||cargo.Count<maxCargoKeys)&&CreditedCells<long.MaxValue)
                    {
                        cargo[item.Matter]=old+1;CreditedCells++;LastCollections++;debrisCells.Remove(item.Cell);
                        int last=debris.Count-1;debris[debrisCursor]=debris[last];debris.RemoveAt(last);continue;
                    }
                }
                else
                {
                    Int3 d=new Int3(0,-1,0);
                    if(suction)
                    {
                        Int3 delta=collector-item.Cell;int ax=Math.Abs(delta.x),ay=Math.Abs(delta.y),az=Math.Abs(delta.z);
                        d=ax>=ay&&ax>=az?new Int3(Math.Sign(delta.x),0,0):ay>=az?new Int3(0,Math.Sign(delta.y),0):new Int3(0,0,Math.Sign(delta.z));
                    }
                    Int3 to=item.Cell+d;
                    if(to.y>0&&Inside(to)&&world.Get(to)==MaterialId.Air&&!IsOccupied(to))
                    {debrisCells.Remove(item.Cell);debrisCells.Add(to);debris[debrisCursor]=new DebrisVoxel(item.Id,to,item.Matter);LastDebrisMoves++;}
                }
                debrisCursor++;
            }
        }
    }
}
