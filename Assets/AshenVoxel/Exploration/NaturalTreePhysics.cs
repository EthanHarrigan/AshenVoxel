using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Numerics;
using AshenVoxel.Core;
using AshenVoxel.Core.Experimental;

namespace AshenVoxel.Exploration.Experimental
{
    /// <summary>Small parallel-ray fist footprint; measured from the eye in metres.
    /// Center hits win. Assistance only applies when the exact center misses.</summary>
    public static class NaturalMatterPunch
    {
        // Only absorb voxel-boundary rounding (0.01mm), not extra gameplay reach.
        public const float Reach=2.5f,Radius=.075f,BoundaryTolerance=.00001f;
        public const float BreakRadius=.25f;
        public const int Rays=9;
        public static Vector3 Offset(Vector3 direction,int ray)
        {
            if(ray==0)return Vector3.Zero;
            var forward=Vector3.Normalize(direction);
            var right=Vector3.Normalize(Vector3.Cross(Math.Abs(forward.Y)>.95f?Vector3.UnitZ:Vector3.UnitY,forward));
            var up=Vector3.Cross(forward,right);float angle=(ray-1)*(float)(Math.PI/4);
            return (right*(float)Math.Cos(angle)+up*(float)Math.Sin(angle))*Radius;
        }
        public static float RayReach(int ray)=>ray==0?Reach:(float)Math.Sqrt(Reach*Reach-Radius*Radius);
    }
    // Exact 10cm matter ownership, approximate motion. Only connected sections are
    // rigid. Loose cells have no pair, actor, angular or static-volume solver.
    public sealed class NaturalTreePhysics
    {
        public enum Ownership : byte { Source, Island, Individual, Cargo }
        public struct Cube
        {
            public Int3 Cell; public CargoKey Matter; public Ownership Owner; public int Island;
            public Vector3 Position, Velocity, AngularVelocity, Offset; public Quaternion Rotation;
            public float Mass;
        }
        sealed class Island
        {
            public readonly Vector3[] Probes=new Vector3[14]; public Vector3 TipAxis; public bool Tall;
            public readonly Dictionary<CargoKey,int> Assembling=new Dictionary<CargoKey,int>();
            public int[] Members; public int Count, ReleaseCursor,Generation; public bool Active, Releasing;
            public Vector3 Position, Velocity, AngularVelocity, Min, Max; public Quaternion Rotation;
            public float Mass, ContactAge, RestAge; public bool Touched;
        }
        const float Cell=.1f, Half=.05f;
        readonly VoxelWorld source; readonly Int3 anchor; readonly Quaternion initialRotation, inverseInitial;
        public readonly struct RenderCell
        {
            public readonly Int3 Cell;public readonly CargoKey Matter;
            internal RenderCell(Int3 cell,CargoKey matter){Cell=cell;Matter=matter;}
        }
        public sealed class RenderSnapshot
        {
            readonly RenderCell[] cells;readonly int[] groups;readonly Vector3[] centers;
            public readonly int Revision;public readonly bool HasCut;public readonly Int3 LastCutCell,Anchor;public readonly int SizeX,SizeY,SizeZ;public readonly Quaternion SourceRotation;
            public Vector3 CanonicalCenter(int group)=>centers[group];
            public int Count=>cells.Length;public RenderCell CellAt(int i)=>cells[i];public int GroupAt(int i)=>groups[i];
            public Vector3 LocalPosition(int i)
            {
                var p=cells[i].Cell;var value=new Vector3((p.x-Anchor.x+.5f)*Cell,(p.y-Anchor.y+.5f)*Cell,(p.z-Anchor.z+.5f)*Cell);
                int group=groups[i];return group>0?value-centers[group-1]:value;
            }
            internal RenderSnapshot(NaturalTreePhysics owner)
            {
                cells=owner.renderCells;groups=(int[])owner.renderGroups.Clone();centers=(Vector3[])owner.renderCenters.Clone();
                SizeX=owner.source.SizeX;SizeY=owner.source.SizeY;SizeZ=owner.source.SizeZ;Revision=owner.StructureRevision;HasCut=owner.HasCut;LastCutCell=owner.LastCutCell;Anchor=owner.anchor;SourceRotation=owner.initialRotation;
            }
        }
        // Main-owner-thread capture only, at a stable ownership boundary. Workers
        // retain immutable cell records and private compact membership/COM copies.
        public RenderSnapshot CaptureRenderSnapshot()
        {
            if(StructureBusy)throw new InvalidOperationException("Render ownership is being committed");return new RenderSnapshot(this);
        }
        readonly RenderCell[] renderCells;readonly int[] renderGroups;readonly Vector3[] renderCenters=new Vector3[8];
        readonly Cube[] cubes; readonly CellIndex lookup;
        readonly Island[] islands=new Island[8];
        readonly Dictionary<CargoKey,long> cargo=new Dictionary<CargoKey,long>();
        readonly long[] materialTotals=new long[MaterialCatalog.Count];
        readonly IReadOnlyDictionary<CargoKey,long> cargoView;
        readonly bool[] seen, sleeping;readonly bool[] supported;readonly int[] roots;readonly bool localSupportCertified;
        readonly List<int> affected=new List<int>();readonly HashSet<int> affectedSet=new HashSet<int>();int affectedCursor,markCursor;bool localGroup,markingSupported; readonly double[] updated;
        readonly int[] parcelNext,parcelWeight;
        readonly int[] queue, group; readonly List<int> loose=new List<int>();
        // Rendering/ownership retains every parcel. Only this compact leader list
        // is visited for motion; no extra arrays sized to the original specimen.
        readonly List<int> awake=new List<int>();
        readonly Dictionary<int,int> awakeSlots=new Dictionary<int,int>(),looseSlots=new Dictionary<int,int>();
        readonly Dictionary<int,Int3> restingColumns=new Dictionary<int,Int3>();
        Vector3 dormantMin,dormantMax,lastHand;
        float lastRadius,terrainMinX,terrainMinZ,terrainMaxX,terrainMaxZ;
        bool lastSuction,wakePending,wakeAgain,terrainWake,singleWakeTurn,singleBodyTurn;
        int wakeCursor,awakeCursor,bodyCursor;
        readonly Queue<Int3> cuts=new Queue<Int3>();
        readonly Queue<Int3> brushes=new Queue<Int3>();
        readonly Dictionary<Int3,int> pile=new Dictionary<Int3,int>();
        int head,tail,scan,groupCount,seedCursor,commitSlot=-1,commitCursor,commitPhase;
        bool searching,groupSearch,seeding,waitingForIsland; double time;
        Vector3 commitSum,commitMin,commitMax; float commitMass; bool commitWood;
        public int Count=>cubes.Length;
        public int SourceCells {get;private set;}
        public int IslandCells {get;private set;}
        public int IndividualCells {get;private set;}
        public int CargoCells {get;private set;}
        public int GroundImpacts {get;private set;}
        public int PairContacts=>0;
        public int LastPairCandidates=>0;
        public bool PairWorkPending=>false;
        public int PairCursor=>0;
        public double PhysicsDebtSeconds=>0; // Each visited particle catches up analytically to its own clock.
        public int LastBodyCandidates {get;private set;}
        public int LastSupportCandidates {get;private set;}
        public int PendingSupport=>searching?Math.Max(1,Count-scan+tail-head):0;
        public bool WaitingForIslandSlot=>waitingForIsland;
        public int PendingCuts=>cuts.Count+brushes.Count;
        public int PendingDebris=>loose.Count;
        public int AwakeDebris=>awake.Count;
        public int DormantDebris {get;private set;}
        public int PendingWakeCandidates=>wakePending?Math.Max(1,loose.Count-wakeCursor)+(wakeAgain?loose.Count:0):0;
        public int LastWakeCandidates {get;private set;}
        public bool IsDormant(int index)=>sleeping[parcelNext[index]>=0?parcelNext[index]:index];
        public int DebrisWeight=4;
        public int LooseIndex(int index)=>loose[index];
        public int LooseWeight(int index)=>parcelWeight[index];
        public Quaternion SourceRotation=>initialRotation;
        public Int3 SourceAnchor=>anchor;
        public bool StructureBusy {get {if(searching&&!waitingForIsland||commitSlot>=0)return true;foreach(var body in islands)if(body.Releasing&&body.Active)return true;return false;}}
        public bool HasCut {get;private set;}
        public Int3 LastCutCell {get;private set;}
        public int StructureRevision {get;private set;}
        public int PoseRevision {get;private set;}
        public bool Conserved=>SourceCells+IslandCells+IndividualCells+CargoCells==Count;
        public string Status {get;private set;}="Aim and break; detached sections remain connected until settled";
        public IReadOnlyDictionary<CargoKey,long> Cargo=>cargoView;
        public long Collected(MaterialId material)=>materialTotals[(int)material];
        public Func<float,float,float> TerrainHeight;
        public Func<Vector3,bool> OtherStaticOverlap; // Compatibility; intentionally unused for visual debris.
        public bool SuctionEnabled=true,PlayerPresent,MovementEnabled=true;
        public bool ExternalRigidBodies;
        public Vector3 Hand,PlayerFeet,PlayerVelocity;
        public float CollectionRadius=4;
        static readonly Vector3[] ProbeDirections={Vector3.UnitX,-Vector3.UnitX,Vector3.UnitY,-Vector3.UnitY,Vector3.UnitZ,-Vector3.UnitZ,
            new Vector3(1,1,1),new Vector3(1,1,-1),new Vector3(1,-1,1),new Vector3(1,-1,-1),new Vector3(-1,1,1),new Vector3(-1,1,-1),new Vector3(-1,-1,1),new Vector3(-1,-1,-1)};
        static readonly Int3[] Neighbours={new Int3(1,0,0),new Int3(-1,0,0),new Int3(0,1,0),new Int3(0,-1,0),new Int3(0,0,1),new Int3(0,0,-1)};

        public NaturalTreePhysics(VoxelWorld source,Int3 anchor,float yawRadians)
        {
            this.source=source??throw new ArgumentNullException(nameof(source));this.anchor=anchor;
            int count=0;foreach(var chunk in source.Chunks.Values)count+=chunk.NonAirCount;
            if(count<1||count>2000000||Math.Abs(source.VoxelSize-Cell)>1e-6)throw new ArgumentOutOfRangeException(nameof(source));
            initialRotation=Quaternion.CreateFromAxisAngle(Vector3.UnitY,yawRadians);inverseInitial=Quaternion.Conjugate(initialRotation);
            cubes=new Cube[count];renderCells=new RenderCell[count];renderGroups=new int[count];lookup=new CellIndex(source.Chunks.Count);seen=new bool[count];supported=new bool[count];sleeping=new bool[count];updated=new double[count];queue=new int[count];group=new int[count];parcelNext=new int[count];parcelWeight=new int[count];Array.Fill(parcelNext,-1);
            int at=0;foreach(var pair in source.Chunks)
            {
            var chunkCells=pair.Value.Cells;var chunkTints=pair.Value.Tints;bool tinted=pair.Value.HasTints;
            for(int i=0;i<Chunk.Volume;i++)if(chunkCells[i]!=MaterialId.Air)
            {
                // Read the chunk directly (same enumeration order and values as source.Get/GetTint).
                var local=Chunk.Decode(i);var p=new Int3(pair.Key.x*16+local.x,pair.Key.y*16+local.y,pair.Key.z*16+local.z);
                var material=chunkCells[i];uint tint=tinted?chunkTints[i]:0u;var canonical=new Vector3((p.x-anchor.x+.5f)*Cell,(p.y-anchor.y+.5f)*Cell,(p.z-anchor.z+.5f)*Cell);
                cubes[at]=new Cube{Cell=p,Matter=new CargoKey(material,tint),Owner=Ownership.Source,Island=-1,Position=Vector3.Transform(canonical,initialRotation),Rotation=initialRotation,Mass=Math.Max(.001f,MaterialCatalog.Get(material).Density)};
                renderCells[at]=new RenderCell(p,cubes[at].Matter);lookup.Add(p,at++);
            }
            }
            for(int i=0;i<islands.Length;i++)islands[i]=new Island();
            var rootList=new List<int>();for(int i=0;i<count;i++)if(Root(i))rootList.Add(i);roots=rootList.ToArray();
            // One background construction proof permits cut-neighbor component searches.
            // Uncertified fixtures retain the general full-world discovery path.
            int first=0,last=0;foreach(int i in roots){seen[i]=true;queue[last++]=i;}
            while(first<last){int i=queue[first++];foreach(var d in Neighbours)if(lookup.TryGetValue(cubes[i].Cell+d,out int j)&&!seen[j]){seen[j]=true;queue[last++]=j;}}
            localSupportCertified=last==count;Array.Clear(seen,0,seen.Length);
            DebrisWeight=Math.Max(4,(count+4095)/4096);SourceCells=count;cargoView=new System.Collections.ObjectModel.ReadOnlyDictionary<CargoKey,long>(cargo);
        }
        public Cube Read(int index)
        {
            var cube=ReadLocal(index);if(cube.Owner==Ownership.Island){var body=islands[cube.Island];cube.Position=body.Position+Vector3.Transform(cube.Offset,body.Rotation);cube.Rotation=body.Rotation;cube.Velocity=body.Velocity;}return cube;
        }
        public Cube ReadLocal(int index)
        {
            var c=cubes[index];if(c.Owner==Ownership.Individual&&parcelNext[index]>=0)
            {var leader=cubes[parcelNext[index]];c.Owner=leader.Owner;c.Position=leader.Position;c.Velocity=leader.Velocity;c.Rotation=leader.Rotation;}return c;
        }
        public bool BodyPose(int index,out Vector3 position,out Quaternion rotation)
        {
            var body=islands[index];position=body.Position;rotation=body.Rotation;return body.Active&&!body.Releasing||commitSlot==index;
        }
        public bool RigidBodyShape(int index,out Vector3[] points,out float mass)
        {
            var body=islands[index];mass=body.Mass*.001f;points=null;
            if(!body.Active||body.Releasing)return false;
            points=(Vector3[])body.Probes.Clone();return true;
        }
        public bool RigidBodyActive(int index)=>islands[index].Active&&!islands[index].Releasing;
        public int RigidBodyGeneration(int index)=>islands[index].Generation;
        public void ReadRigidBody(int index,Vector3 position,Quaternion rotation,Vector3 velocity,bool touchingGround,bool sleepingBody,float elapsed)
        {
            var body=islands[index];if(!ExternalRigidBodies||!body.Active||body.Releasing)return;
            body.Position=position;body.Rotation=rotation;body.Velocity=velocity;PoseRevision++;
            if(touchingGround&&!body.Touched){body.Touched=true;GroundImpacts++;}
            if(body.Touched)body.ContactAge+=elapsed;
            body.RestAge=body.Touched&&sleepingBody?body.RestAge+elapsed:0;
            if(body.Touched&&(body.RestAge>=.6f||body.ContactAge>=3))body.Releasing=true;
        }
        public bool CoherentNeighbour(int index,int axis,int sign)
        {
            var c=cubes[index];if(c.Owner!=Ownership.Source&&c.Owner!=Ownership.Island)return false;
            var d=axis==0?new Int3(sign,0,0):axis==1?new Int3(0,sign,0):new Int3(0,0,sign);
            if(!lookup.TryGetValue(c.Cell+d,out int j))return false;var n=cubes[j];
            return n.Owner==c.Owner&&n.Matter.Material==c.Matter.Material&&(c.Owner==Ownership.Source||n.Island==c.Island);
        }
        public bool Cut(Int3 cell)
        {
            if(!lookup.TryGetValue(cell,out int i)||cubes[i].Owner!=Ownership.Source||!MaterialCatalog.BlocksActorBody(cubes[i].Matter.Material))return false;
            if(searching){if(cuts.Contains(cell))return true;if(cuts.Count==32){Status="Cut queue full; pending cuts retained";return false;}cuts.Enqueue(cell);return true;}
            RemoveCell(i);BeginSupport();return true;
        }
        void RemoveCell(int i)
        {
            HasCut=true;LastCutCell=cubes[i].Cell;
            renderGroups[i]=-1;cubes[i].Owner=Ownership.Individual;SourceCells--;IndividualCells++;parcelWeight[i]=1;AddLoose(i);
            source.Set(cubes[i].Cell,MaterialId.Air);
            foreach(var d in Neighbours)if(lookup.TryGetValue(cubes[i].Cell+d,out int j)&&cubes[j].Owner==Ownership.Source&&affectedSet.Add(j))affected.Add(j);
            StructureRevision++;
        }
        public bool CutRay(Vector3 origin,Vector3 direction,float distance=5)
            =>TryRayCell(origin,direction,distance,out var cell,out _)&&Cut(cell);
        public bool CutPunch(Vector3 origin,Vector3 direction)
        {
            if(!Finite(direction.LengthSquared())||direction.LengthSquared()<1e-10f)return false;
            if(TryRayCell(origin,direction,NaturalMatterPunch.Reach,out var center,out _,true))return CutSphere(center)>0;
            bool found=false;float closest=float.PositiveInfinity;Int3 best=default;
            for(int ray=1;ray<NaturalMatterPunch.Rays;ray++)
                if(TryRayCell(origin+NaturalMatterPunch.Offset(direction,ray),direction,NaturalMatterPunch.RayReach(ray),out var cell,out float hit,true)&&hit<closest)
                {found=true;closest=hit;best=cell;}
            return found&&CutSphere(best)>0;
        }
        public int CutSphere(Int3 center)
        {
            if(searching)
            {if(brushes.Contains(center))return 1;if(brushes.Count>=8)return 0;brushes.Enqueue(center);return 1;}
            int removed=0,reach=(int)Math.Ceiling(NaturalMatterPunch.BreakRadius/Cell);
            for(int z=-reach;z<=reach;z++)for(int y=-reach;y<=reach;y++)for(int x=-reach;x<=reach;x++)
            {
                if((x*x+y*y+z*z)*Cell*Cell>NaturalMatterPunch.BreakRadius*NaturalMatterPunch.BreakRadius+1e-6f)continue;
                var p=center+new Int3(x,y,z);
                if(!lookup.TryGetValue(p,out int i)||cubes[i].Owner!=Ownership.Source||source.Get(p)==MaterialId.Air)continue;
                RemoveCell(i);removed++;
            }
            if(removed>0){LastCutCell=center;BeginSupport();}return removed;
        }
        bool TryRayCell(Vector3 origin,Vector3 direction,float distance,out Int3 selected,out float hit,bool includeSoft=false)
        {
            selected=default;hit=0;
            if(!Finite(origin.X)||!Finite(origin.Y)||!Finite(origin.Z)||!Finite(direction.LengthSquared())||direction.LengthSquared()<1e-10f||!Finite(distance)||distance<0||distance>12)return false;
            var o=Vector3.Transform(origin,inverseInitial)+new Vector3(anchor.x*Cell,anchor.y*Cell,anchor.z*Cell);
            var d=Vector3.Normalize(Vector3.Transform(direction,inverseInitial));float t=0;
            int x=(int)Math.Floor(o.X/Cell),y=(int)Math.Floor(o.Y/Cell),z=(int)Math.Floor(o.Z/Cell);
            for(int n=0;n<400&&t<=distance+NaturalMatterPunch.BoundaryTolerance;n++)
            {
                var cell=new Int3(x,y,z);var material=source.Get(cell);if(includeSoft?material!=MaterialId.Air:MaterialCatalog.BlocksActorBody(material)){selected=cell;hit=Math.Min(t,distance);return true;}
                float Edge(float a,float v,int c)=>Math.Abs(v)<1e-8f?float.PositiveInfinity:(((v>0?c+1:c)*Cell-a)/v);
                float tx=Edge(o.X,d.X,x),ty=Edge(o.Y,d.Y,y),tz=Edge(o.Z,d.Z,z),step=Math.Min(tx,Math.Min(ty,tz));
                if(!Finite(step))break;t=step;
                if(tx<=step+1e-6f)x+=Math.Sign(d.X);if(ty<=step+1e-6f)y+=Math.Sign(d.Y);if(tz<=step+1e-6f)z+=Math.Sign(d.Z);
            }
            return false;
        }
        static bool Finite(float n)=>!float.IsNaN(n)&&!float.IsInfinity(n);
        bool Root(int i)=>cubes[i].Cell.y<=anchor.y&&cubes[i].Matter.Material!=MaterialId.Air;
        void BeginSupport(){Array.Clear(seen,0,seen.Length);Array.Clear(supported,0,supported.Length);head=tail=scan=groupCount=seedCursor=affectedCursor=0;localGroup=markingSupported=false;groupSearch=false;waitingForIsland=false;searching=seeding=true;}
        public int IntactAnchoredCells()
        {
            var visited=new bool[Count];var work=new int[Count];int start=0,end=0;
            for(int i=0;i<Count;i++)if(Root(i)){visited[i]=true;work[end++]=i;}
            while(start<end){int i=work[start++];foreach(var d in Neighbours)if(lookup.TryGetValue(cubes[i].Cell+d,out int j)&&!visited[j]){visited[j]=true;work[end++]=j;}}return end;
        }
        public void StepSupport(int budget,double milliseconds=double.PositiveInfinity)
        {
            if(budget<0||double.IsNaN(milliseconds)||milliseconds<0)throw new ArgumentOutOfRangeException(nameof(budget));LastSupportCandidates=0;waitingForIsland=false;
            long started=Stopwatch.GetTimestamp();
            while(searching&&LastSupportCandidates<budget)
            {
                if((LastSupportCandidates&63)==0&&(Stopwatch.GetTimestamp()-started)*1000.0/Stopwatch.Frequency>=milliseconds)return;
                if(localSupportCertified){StepLocalSupport();if(waitingForIsland)return;continue;}
                if(commitSlot>=0){CommitOne();LastSupportCandidates++;continue;}
                if(seeding)
                {
                    if(seedCursor<roots.Length){int i=roots[seedCursor++];LastSupportCandidates++;if(cubes[i].Owner==Ownership.Source){seen[i]=true;queue[tail++]=i;}}
                    if(seedCursor==roots.Length)seeding=false;continue;
                }
                if(head<tail)
                {
                    int i=queue[head++];LastSupportCandidates++;if(groupSearch)group[groupCount++]=i;
                    foreach(var d in Neighbours)if(lookup.TryGetValue(cubes[i].Cell+d,out int j)&&!seen[j]&&cubes[j].Owner==Ownership.Source){seen[j]=true;queue[tail++]=j;}continue;
                }
                if(groupSearch)
                {
                    int slot=-1;for(int i=0;i<8;i++)if(!islands[i].Active){slot=i;break;}
                    if(slot<0){waitingForIsland=true;Status="Eight sections active; disconnected work remains queued";return;}
                    commitSlot=slot;commitCursor=commitPhase=0;commitSum=Vector3.Zero;commitMass=0;commitWood=false;
                    commitMin=new Vector3(float.MaxValue);commitMax=new Vector3(float.MinValue);continue;
                }
                if(scan==Count){EndSupport();break;}
                int next=scan++;LastSupportCandidates++;if(seen[next]||cubes[next].Owner!=Ownership.Source)continue;
                head=tail=0;seen[next]=true;queue[tail++]=next;groupSearch=true;
            }
        }
        void EndSupport(){searching=false;affected.Clear();affectedSet.Clear();Status="Support current";}
        void StepLocalSupport()
        {
            if(markingSupported)
            {
                supported[queue[markCursor++]]=true;LastSupportCandidates++;
                if(markCursor==tail){markingSupported=false;localGroup=groupSearch=false;}
                return;
            }
            if(commitSlot>=0){CommitOne();LastSupportCandidates++;if(commitSlot<0)localGroup=false;return;}
            if(!localGroup)
            {
                if(affectedCursor==affected.Count){EndSupport();return;}
                int seed=affected[affectedCursor++];LastSupportCandidates++;
                if(cubes[seed].Owner!=Ownership.Source||supported[seed])return;
                head=0;tail=1;groupCount=0;queue[0]=seed;seen[seed]=true;localGroup=groupSearch=true;return;
            }
            if(head<tail)
            {
                int i=queue[head++];group[groupCount++]=i;LastSupportCandidates++;
                bool anchored=Root(i)||supported[i];
                foreach(var d in Neighbours)if(lookup.TryGetValue(cubes[i].Cell+d,out int j)&&cubes[j].Owner==Ownership.Source)
                {if(supported[j])anchored=true;else if(!seen[j]){seen[j]=true;queue[tail++]=j;}}
                if(anchored){markingSupported=true;markCursor=0;}
                return;
            }
            int slot=-1;for(int i=0;i<8;i++)if(!islands[i].Active){slot=i;break;}
            if(slot<0){waitingForIsland=true;Status="Eight sections active; disconnected work remains queued";LastSupportCandidates++;return;}
            commitSlot=slot;commitCursor=commitPhase=0;commitSum=Vector3.Zero;commitMass=0;commitWood=false;
            commitMin=new Vector3(float.MaxValue);commitMax=new Vector3(float.MinValue);
        }
        void CommitOne()
        {
            var body=islands[commitSlot];int i=group[commitCursor++];ref Cube c=ref cubes[i];
            if(commitPhase==0)
            {
                var p=Vector3.Transform(c.Position,inverseInitial);commitSum+=p*c.Mass;commitMass+=c.Mass;
                commitWood|=c.Matter.Material==MaterialId.Wood;
                commitMin=Vector3.Min(commitMin,p);commitMax=Vector3.Max(commitMax,p);
                for(int probe=0;probe<body.Probes.Length;probe++)if(commitCursor==1||Vector3.Dot(p,ProbeDirections[probe])>Vector3.Dot(body.Probes[probe],ProbeDirections[probe]))body.Probes[probe]=p;
                if(commitCursor<groupCount)return;
                var com=commitSum/commitMass;renderCenters[commitSlot]=com;body.Position=Vector3.Transform(com,initialRotation);body.Rotation=initialRotation;
                body.Mass=commitMass;body.Min=commitMin-com-new Vector3(Half);body.Max=commitMax-com+new Vector3(Half);
                var lean=com-body.Probes[3];lean.Y=0;if(lean.LengthSquared()<.0001f)lean=Vector3.UnitX;
                body.TipAxis=Vector3.Transform(Vector3.Cross(Vector3.UnitY,Vector3.Normalize(lean)),initialRotation);
                // Broad crowns must not make a severed trunk appear stable.
                var extent=body.Max-body.Min;body.Tall=extent.Y>Math.Max(extent.X,extent.Z)*1.2f||commitWood&&extent.Y>=1;
                for(int probe=0;probe<body.Probes.Length;probe++)body.Probes[probe]-=com;
                body.Count=groupCount;body.Members=new int[groupCount];body.Velocity=body.AngularVelocity=Vector3.Zero;
                body.Assembling.Clear();body.ContactAge=body.RestAge=0;body.Touched=body.Releasing=false;body.ReleaseCursor=0;commitPhase=1;commitCursor=0;return;
            }
            c.Offset=Vector3.Transform(c.Position,inverseInitial)-commitSum/commitMass;c.Owner=Ownership.Island;c.Island=commitSlot;renderGroups[i]=commitSlot+1;body.Members[commitCursor-1]=i;
            source.Set(c.Cell,MaterialId.Air);SourceCells--;IslandCells++;
            if(commitCursor<groupCount)return;
            body.Generation++;body.Active=true;commitSlot=-1;groupSearch=false;groupCount=0;StructureRevision++;
        }
        static Quaternion Advance(Quaternion q,Vector3 w,float dt)
        {float speed=w.Length();return speed<1e-8f?q:Quaternion.Normalize(Quaternion.CreateFromAxisAngle(w/speed,speed*dt)*q);}
        void StepIsland(Island body,float dt)
        {
            if(body.Releasing)return;
            var previous=body.Position;var previousRotation=body.Rotation;
            body.Velocity.Y-=9.81f*dt;body.Position+=body.Velocity*dt;body.Rotation=Advance(body.Rotation,body.AngularVelocity,dt);
            float depth=0;Vector3 contact=Vector3.Zero;bool ground=false,missing=false;
            // Actual member extrema retain a narrow trunk base instead of treating
            // the entire canopy bounding box as a wide flat foot. Fixed cost per section.
            for(int k=0;k<body.Probes.Length;k++)
            {
                var p=body.Position+Vector3.Transform(body.Probes[k],body.Rotation);p.Y-=.086603f;
                float floor=TerrainHeight?.Invoke(p.X,p.Z)??float.NaN;
                if(!Finite(floor))missing=true;
                if(Finite(floor)&&floor-p.Y>=depth){depth=floor-p.Y;contact=p;ground=true;}
                var canonical=Vector3.Transform(p,inverseInitial);
                int x=(int)Math.Floor(canonical.X/Cell)+anchor.x,y=(int)Math.Floor(canonical.Y/Cell)+anchor.y,z=(int)Math.Floor(canonical.Z/Cell)+anchor.z;
                for(int dy=0;dy<=1;dy++)if(MaterialCatalog.BlocksActorBody(source.Get(x,y-dy,z)))
                {float top=(y-dy+1-anchor.y)*Cell;if(top-p.Y>=depth){depth=top-p.Y;contact=p;}}
            }
            if(missing){body.Position=previous;body.Rotation=previousRotation;body.Velocity=Vector3.Zero;return;}
            if(depth<=0)return;
            body.Position.Y+=Math.Max(0,depth);body.Velocity.Y=Math.Max(0,body.Velocity.Y)*.05f;
            if(ground&&!body.Touched){body.Touched=true;GroundImpacts++;}
            if(body.Touched)body.ContactAge+=dt;var arm=contact-body.Position;
            // Gravity about the support edge supplies the tip; no per-cell inertia/SAT.
            float span=Math.Max(.1f,(body.Max-body.Min).LengthSquared());
            body.AngularVelocity+=Vector3.Cross(arm,Vector3.UnitY)*(9.81f*dt/span);
            // Historical CPU-only contact approximation; live native bodies bypass StepIsland.
            if(body.Tall&&Vector3.Transform(Vector3.UnitY,body.Rotation).Y>.15f)body.AngularVelocity+=body.TipAxis*(1.4f*dt);
            body.AngularVelocity*=Math.Max(0,1-1.8f*dt);body.Velocity.X*=Math.Max(0,1-3*dt);body.Velocity.Z*=Math.Max(0,1-3*dt);
            if(body.AngularVelocity.LengthSquared()+body.Velocity.LengthSquared()<.02f)body.RestAge+=dt;else body.RestAge=0;
            if(body.Touched&&(body.RestAge>.6f||body.ContactAge>3)){body.Releasing=true;Status="Settled section becoming floor-aware debris";}
            PoseRevision++;
        }
        void ReleaseOne(Island body)
        {
            int i=body.Members[body.ReleaseCursor++];ref Cube c=ref cubes[i];c.Position=body.Position+Vector3.Transform(c.Offset,body.Rotation);
            c.Rotation=Quaternion.Identity;c.Velocity=body.Velocity;c.AngularVelocity=Vector3.Zero;c.Owner=Ownership.Individual;c.Island=-1;renderGroups[i]=-1;
            IslandCells--;IndividualCells++;updated[i]=time;
            var assembling=body.Assembling;
            if(assembling.TryGetValue(c.Matter,out int leader))
            {parcelNext[i]=leader;parcelWeight[leader]++;}
            else {leader=i;parcelWeight[i]=1;assembling[c.Matter]=i;}
            if(parcelWeight[leader]>=Math.Max(1,DebrisWeight))
            {AddLoose(leader);assembling.Remove(c.Matter);}
            StructureRevision++;
            if(body.ReleaseCursor==body.Count)
            {
                foreach(var partial in assembling.Values)AddLoose(partial);
                assembling.Clear();body.Active=false;body.Members=null;
            }
        }

        void AddLoose(int i)
        {
            looseSlots.Add(i,loose.Count);loose.Add(i);awakeSlots.Add(i,awake.Count);awake.Add(i);updated[i]=time;
        }
        static void RemoveIndex(List<int> list,Dictionary<int,int> slots,int i)
        {
            int slot=slots[i],last=list[list.Count-1];list[slot]=last;slots[last]=slot;list.RemoveAt(list.Count-1);slots.Remove(i);
        }
        void Wake(int i)
        {
            if(!sleeping[i])return;
            sleeping[i]=false;DormantDebris--;updated[i]=time; // Dormancy is not simulation debt.
            var column=restingColumns[i];int remaining=pile[column]-parcelWeight[i];
            if(remaining==0)pile.Remove(column);else pile[column]=remaining;
            restingColumns.Remove(i);awakeSlots.Add(i,awake.Count);awake.Add(i);
        }
        void Sleep(int i,Int3 column)
        {
            sleeping[i]=true;restingColumns.Add(i,column);
            var p=cubes[i].Position;
            if(DormantDebris++==0){dormantMin=p;dormantMax=p;}
            else {dormantMin=Vector3.Min(dormantMin,p);dormantMax=Vector3.Max(dormantMax,p);}
            // Gravity can carry a parcel into pickup range during this step.
            if(PickupContains(p))RequestWake();
        }
        bool PickupContains(Vector3 p)=>SuctionEnabled&&CollectionRadius>0&&Finite(CollectionRadius)&&(p-Hand).LengthSquared()<CollectionRadius*CollectionRadius;
        bool PickupOverlapsDormant()
        {
            if(DormantDebris==0||!SuctionEnabled||!Finite(CollectionRadius)||CollectionRadius<=0)return false;
            return Vector3.DistanceSquared(Hand,Vector3.Clamp(Hand,dormantMin,dormantMax))<CollectionRadius*CollectionRadius;
        }
        void RequestWake()
        {
            if(DormantDebris==0)return;
            if(wakePending)wakeAgain=true;else {wakePending=true;wakeCursor=0;}
        }
        /// <summary>Local metres, inclusive XZ bounds. The caller invalidates its
        /// terrain-height cache first. Work is retained and charged to Step's budget.</summary>
        public void NotifyTerrainChanged(float minX,float minZ,float maxX,float maxZ)
        {
            if(!Finite(minX)||!Finite(minZ)||!Finite(maxX)||!Finite(maxZ)||minX>maxX||minZ>maxZ)throw new ArgumentOutOfRangeException(nameof(minX));
            // Include the10cm sampled column at region boundaries.
            minX-=Cell;minZ-=Cell;maxX+=Cell;maxZ+=Cell;
            if(DormantDebris==0||maxX<dormantMin.X||minX>dormantMax.X||maxZ<dormantMin.Z||minZ>dormantMax.Z)return;
            if(terrainWake){terrainMinX=Math.Min(terrainMinX,minX);terrainMinZ=Math.Min(terrainMinZ,minZ);terrainMaxX=Math.Max(terrainMaxX,maxX);terrainMaxZ=Math.Max(terrainMaxZ,maxZ);}
            else {terrainWake=true;terrainMinX=minX;terrainMinZ=minZ;terrainMaxX=maxX;terrainMaxZ=maxZ;}
            RequestWake();
        }
        void ScanWake(int budget)
        {
            while(budget-->0&&wakePending)
            {
                if(DormantDebris==0||loose.Count==0){wakePending=wakeAgain=terrainWake=false;break;}
                if(wakeCursor<loose.Count)
                {
                    int i=loose[wakeCursor++];LastWakeCandidates++;LastBodyCandidates++;
                    var p=cubes[i].Position;
                    if(sleeping[i]&&(PickupContains(p)||terrainWake&&p.X>=terrainMinX&&p.X<=terrainMaxX&&p.Z>=terrainMinZ&&p.Z<=terrainMaxZ))Wake(i);
                }
                if(wakeCursor>=loose.Count)
                {
                    if(wakeAgain){wakeAgain=false;wakeCursor=0;}
                    else {wakePending=false;terrainWake=false;}
                }
            }
        }
        void StepLoose(int i)
        {
            ref Cube c=ref cubes[i];float dt=(float)(time-updated[i]);updated[i]=time;
            var to=Hand-c.Position;float distance2=to.LengthSquared();
            if(PickupContains(c.Position))
            {
                float travel=10*dt;if(distance2<=Math.Max(.18f,travel)*Math.Max(.18f,travel))
                {
                    cargo.TryGetValue(c.Matter,out long count);int weight=parcelWeight[i];cargo[c.Matter]=count+weight;materialTotals[(int)c.Matter.Material]+=weight;
                    c.Owner=Ownership.Cargo; // Followers resolve their owner through this leader; O(1) exact weighted credit.
                    IndividualCells-=weight;CargoCells+=weight;PoseRevision++;return;
                }
                c.Position+=Vector3.Normalize(to)*travel;c.Velocity=Vector3.Zero;sleeping[i]=false;PoseRevision++;return;
            }
            if(sleeping[i]||dt<=0)return;
            var old=c.Position;c.Position+=c.Velocity*dt-Vector3.UnitY*(4.905f*dt*dt);c.Velocity.Y-=9.81f*dt;
            float floor=TerrainHeight?.Invoke(c.Position.X,c.Position.Z)??float.NaN;
            if(!Finite(floor)){c.Position=old;c.Velocity=Vector3.Zero;return;}// Retain until the bounded terrain page supplies this location.
            if(c.Position.Y-Half<=floor)
            {
                // Coarse occupancy makes a loose pile; no particle-particle queries.
                var column=new Int3((int)Math.Floor(c.Position.X/.4f),0,(int)Math.Floor(c.Position.Z/.4f));
                pile.TryGetValue(column,out int n);pile[column]=n+parcelWeight[i];
                c.Position.Y=floor+Half+(n/16)*Cell;c.Velocity=Vector3.Zero;c.Rotation=Quaternion.Identity;Sleep(i,column);
            }
            PoseRevision++;
        }
        public int Kick(Vector3 eye,Vector3 direction)
        {
            if(!Finite(direction.LengthSquared())||!Finite(eye.LengthSquared())||direction.LengthSquared()<1e-8f)return 0;direction=Vector3.Normalize(direction);int count=0;
            int limit=Math.Min(loose.Count,8192);for(int k=0;k<limit;k++){int i=loose[k];var d=cubes[i].Position-eye;float along=Vector3.Dot(d,direction);if(along<0||along>2||(d-direction*along).LengthSquared()>.09f)continue;Wake(i);cubes[i].Velocity=direction*4;updated[i]=time;count++;}return count;
        }
        public void Step(float elapsed,int supportBudget=8192,int bodyBudget=8192,int pairBudget=131072)
        {
            if(!Finite(elapsed)||elapsed<0||elapsed>.25f||bodyBudget<1)throw new ArgumentOutOfRangeException(nameof(elapsed));
            time+=elapsed;LastBodyCandidates=LastWakeCandidates=0;StepSupport(supportBudget);
            if(lastSuction!=SuctionEnabled||lastHand!=Hand||lastRadius!=CollectionRadius)
            {
                lastSuction=SuctionEnabled;lastHand=Hand;lastRadius=CollectionRadius;
                if(PickupOverlapsDormant())RequestWake();
            }
            if(!searching&&cuts.Count>0){var cell=cuts.Dequeue();Cut(cell);}
            if(!searching&&brushes.Count>0)CutSphere(brushes.Dequeue());
            int sectionBudget=Math.Max(1,bodyBudget/2);
            if(bodyBudget==1&&(awake.Count>0||wakePending)){singleBodyTurn=!singleBodyTurn;if(!singleBodyTurn)sectionBudget=0;}
            for(int scanBody=0;scanBody<islands.Length&&LastBodyCandidates<sectionBudget;scanBody++)
            {
                var body=islands[bodyCursor];bodyCursor=(bodyCursor+1)%islands.Length;if(!body.Active)continue;
                if(body.Releasing){while(body.Active&&LastBodyCandidates<sectionBudget){ReleaseOne(body);LastBodyCandidates++;}}
                else if(MovementEnabled&&!ExternalRigidBodies){StepIsland(body,elapsed);LastBodyCandidates++;PoseRevision++;}
            }
            int remaining=bodyBudget-LastBodyCandidates,wakeBudget=0;
            if(wakePending&&remaining>0)
            {
                wakeBudget=awake.Count==0?Math.Min(256,remaining):remaining>1?Math.Min(256,Math.Max(1,remaining/4)):0;
                if(remaining==1&&awake.Count>0){singleWakeTurn=!singleWakeTurn;if(singleWakeTurn)wakeBudget=1;}
            }
            ScanWake(wakeBudget);
            int visits=Math.Min(awake.Count,bodyBudget-LastBodyCandidates);
            while(visits-->0&&awake.Count>0)
            {
                if(awakeCursor>=awake.Count)awakeCursor=0;int i=awake[awakeCursor];
                // Swap removal can bring an already visited leader around the cursor.
                if(updated[i]<time)StepLoose(i);LastBodyCandidates++;
                if(sleeping[i]||cubes[i].Owner==Ownership.Cargo)RemoveIndex(awake,awakeSlots,i);else awakeCursor++;
                if(cubes[i].Owner==Ownership.Cargo)
                {
                    RemoveIndex(loose,looseSlots,i);
                    if(wakePending)wakeAgain=true; // A swapped unvisited leader may now be behind the scan cursor.
                }
            }
            if(DormantDebris==0)wakePending=wakeAgain=terrainWake=false;
            if(!Conserved)throw new InvalidOperationException("Matter ownership count failed");
        }
    }
    /// <summary>Cell → cube index over the specimen's own 16³ chunks. Replaces a per-cell
    /// Dictionary: about a third of the memory for large trees and much faster neighbour
    /// queries (most hit the same chunk as the previous query).</summary>
    sealed class CellIndex
    {
        sealed class Slot {public Int3 Key;public int[] Cells;}
        readonly Dictionary<Int3,int[]> chunks;Slot last=new Slot{Key=new Int3(int.MinValue,int.MinValue,int.MinValue)};
        public CellIndex(int capacity){chunks=new Dictionary<Int3,int[]>(Math.Max(1,capacity));}
        static int Local(Int3 p)=>Chunk.Index(p.x&15,p.y&15,p.z&15);
        public void Add(Int3 p,int index)
        {
            var key=new Int3(p.x>>4,p.y>>4,p.z>>4);
            if(!chunks.TryGetValue(key,out var cells)){cells=new int[Chunk.Volume];Array.Fill(cells,-1);chunks.Add(key,cells);}
            if(cells[Local(p)]>=0)throw new ArgumentException("Duplicate matter cell");cells[Local(p)]=index;
        }
        public bool TryGetValue(Int3 p,out int index)
        {
            var key=new Int3(p.x>>4,p.y>>4,p.z>>4);var slot=last;int[] cells;
            if(slot.Key.x==key.x&&slot.Key.y==key.y&&slot.Key.z==key.z)cells=slot.Cells;
            else{if(!chunks.TryGetValue(key,out cells)){index=-1;return false;}last=new Slot{Key=key,Cells=cells};}
            index=cells[Local(p)];return index>=0;
        }
    }
}
