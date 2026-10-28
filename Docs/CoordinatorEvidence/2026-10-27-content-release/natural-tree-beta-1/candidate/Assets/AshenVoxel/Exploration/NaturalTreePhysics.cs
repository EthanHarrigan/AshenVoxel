using System;
using System.Collections.Generic;
using System.Numerics;
using AshenVoxel.Core;
using AshenVoxel.Core.Experimental;

namespace AshenVoxel.Exploration.Experimental
{
    // One finite natural tree, at most4096 cubes and8 connected falling islands.
    // Poses are metre offsets from the immutable original instance world anchor.
    // No Unity objects, random launch velocities, collection timer or grid motion.
    public sealed class NaturalTreePhysics
    {
        public enum Ownership:byte { Source,Island,Individual,Cargo }
        public struct Cube
        {
            public Int3 Cell;public CargoKey Matter;public Ownership Owner;public int Island;
            public Vector3 Position,Velocity,AngularVelocity,Offset;public Quaternion Rotation;
            public float Mass;
        }
        sealed class Island
        {
            public readonly int[] Members;public int Count;public bool Active;
            public Vector3 Position,Velocity,AngularVelocity,InverseInertia;public Quaternion Rotation;public float Mass;
            public Island(int count){Members=new int[count];}
        }
        const float Half=.05f,Cell=.1f;
        readonly VoxelWorld source;readonly Int3 anchor;readonly Quaternion initialRotation,inverseInitial;
        readonly Cube[] cubes;readonly Island[] islands=new Island[8];
        readonly Dictionary<Int3,int> lookup=new Dictionary<Int3,int>();
        readonly Dictionary<CargoKey,long> cargo=new Dictionary<CargoKey,long>();
        readonly IReadOnlyDictionary<CargoKey,long> cargoView;
        readonly bool[] seen;readonly int[] queue,group;int head,tail,scan,groupCount;bool searching,groupSearch;
        readonly Dictionary<Int3,float> stumpTops=new Dictionary<Int3,float>();bool stumpDirty=true;
        readonly int[] heads=new int[32768],next,bx,by,bz;bool pairsPending;int pairI,pairOffset,pairChain=-2;
        double timeDebt;int lastBodies;
        public int Count=>cubes.Length;
        public int SourceCells {get;private set;}
        public int IslandCells {get;private set;}
        public int IndividualCells {get;private set;}
        public int CargoCells {get;private set;}
        public int GroundImpacts {get;private set;}
        public int PairContacts {get;private set;}
        public int LastPairCandidates {get;private set;}
        public int LastSupportCandidates {get;private set;}
        public int PendingSupport=>searching?Math.Max(0,tail-head)+(Count-scan):0;
        public bool PairWorkPending=>pairsPending;
        public int PairCursor=>pairI;
        public double PhysicsDebtSeconds=>timeDebt;
        public bool Conserved=>SourceCells+IslandCells+IndividualCells+CargoCells==Count;
        public string Status {get;private set;}="Intact complete tree; cut actual wood cells";
        public IReadOnlyDictionary<CargoKey,long> Cargo=>cargoView;
        public Func<float,float,float> TerrainHeight;
        public Func<Vector3,bool> OtherStaticOverlap;
        public bool SuctionEnabled;
        public Vector3 Hand,PlayerFeet,PlayerVelocity;
        public bool PlayerPresent;
        public int LastBodyCandidates=>lastBodies;
        static readonly Int3[] Neighbours={new Int3(1,0,0),new Int3(-1,0,0),new Int3(0,1,0),new Int3(0,-1,0),new Int3(0,0,1),new Int3(0,0,-1)};
        public NaturalTreePhysics(VoxelWorld source,Int3 anchor,float yawRadians)
        {
            this.source=source??throw new ArgumentNullException(nameof(source));this.anchor=anchor;
            int count=0;foreach(var chunk in source.Chunks.Values)count+=chunk.NonAirCount;
            if(count<1||count>4096)throw new ArgumentOutOfRangeException(nameof(source));
            initialRotation=Quaternion.CreateFromAxisAngle(Vector3.UnitY,yawRadians);inverseInitial=Quaternion.Conjugate(initialRotation);
            cubes=new Cube[count];seen=new bool[count];queue=new int[count];group=new int[count];next=new int[count];bx=new int[count];by=new int[count];bz=new int[count];
            int at=0;foreach(var pair in source.Chunks)for(int i=0;i<Chunk.Volume;i++)if(pair.Value.Cells[i]!=MaterialId.Air)
            {
                var local=Chunk.Decode(i);var cell=new Int3(pair.Key.x*16+local.x,pair.Key.y*16+local.y,pair.Key.z*16+local.z);
                var material=source.Get(cell);var canonical=new Vector3((cell.x-anchor.x+.5f)*Cell,(cell.y-anchor.y+.5f)*Cell,(cell.z-anchor.z+.5f)*Cell);
                cubes[at]=new Cube{Cell=cell,Matter=new CargoKey(material,source.GetTint(cell)),Owner=Ownership.Source,Island=-1,Position=Vector3.Transform(canonical,initialRotation),Rotation=initialRotation,Mass=Math.Max(.001f,MaterialCatalog.Get(material).Density)};
                lookup.Add(cell,at++);
            }
            for(int i=0;i<islands.Length;i++)islands[i]=new Island(count);
            SourceCells=count;cargoView=new System.Collections.ObjectModel.ReadOnlyDictionary<CargoKey,long>(cargo);
        }
        public Cube Read(int index)
        {
            var cube=cubes[index];if(cube.Owner==Ownership.Island){var body=islands[cube.Island];cube.Position=body.Position+Vector3.Transform(cube.Offset,body.Rotation);cube.Rotation=body.Rotation;cube.Velocity=body.Velocity+Vector3.Cross(body.AngularVelocity,cube.Position-body.Position);cube.AngularVelocity=body.AngularVelocity;}return cube;
        }
        public bool CoherentNeighbour(int index,int axis,int sign)
        {
            var cube=cubes[index];if(cube.Owner!=Ownership.Source&&cube.Owner!=Ownership.Island)return false;
            var d=axis==0?new Int3(sign,0,0):axis==1?new Int3(0,sign,0):new Int3(0,0,sign);
            if(!lookup.TryGetValue(cube.Cell+d,out int other))return false;var n=cubes[other];
            return n.Owner==cube.Owner&&n.Matter.Material==cube.Matter.Material&&(cube.Owner==Ownership.Source||n.Island==cube.Island);
        }
        public bool Cut(Int3 cell)
        {
            if(pairsPending){Status="Collision backlog; cut rejected until current tick completes";return false;}
            if(!lookup.TryGetValue(cell,out int i)||cubes[i].Owner!=Ownership.Source||cubes[i].Matter.Material!=MaterialId.Wood)return false;
            cubes[i].Owner=Ownership.Individual;IndividualCells++;SourceCells--;source.Set(cell,MaterialId.Air);stumpDirty=true;BeginSupport();return true;
        }
        public bool CutRay(Vector3 origin,Vector3 direction,float distance=5)
        {
            // A bounded10cm DDA. Soft foliage does not hide the trunk from tools.
            if(direction.LengthSquared()<1e-10f)return false;
            var o=Vector3.Transform(origin,inverseInitial)+new Vector3(anchor.x*Cell,anchor.y*Cell,anchor.z*Cell);
            var d=Vector3.Normalize(Vector3.Transform(direction,inverseInitial));
            float t=0;int x=(int)Math.Floor(o.X/Cell),y=(int)Math.Floor(o.Y/Cell),z=(int)Math.Floor(o.Z/Cell);
            for(int n=0;n<192&&t<=distance;n++)
            {
                var cell=new Int3(x,y,z);if(source.Get(cell)==MaterialId.Wood)return Cut(cell);
                float Edge(float a,float v,int c)=>Math.Abs(v)<1e-8f?float.PositiveInfinity:(((v>0?c+1:c)*Cell-a)/v);
                float tx=Edge(o.X,d.X,x),ty=Edge(o.Y,d.Y,y),tz=Edge(o.Z,d.Z,z),step=Math.Min(tx,Math.Min(ty,tz));
                if(!Finite(step))break;t=step;
                if(tx<=step+1e-6f)x+=Math.Sign(d.X);if(ty<=step+1e-6f)y+=Math.Sign(d.Y);if(tz<=step+1e-6f)z+=Math.Sign(d.Z);
            }
            return false;
        }
        static bool Finite(float n)=>!float.IsNaN(n)&&!float.IsInfinity(n);
        void BeginSupport()
        {
            Array.Clear(seen,0,seen.Length);head=tail=scan=groupCount=0;groupSearch=false;searching=true;
            for(int i=0;i<Count;i++)if(cubes[i].Owner==Ownership.Source&&cubes[i].Cell.y<=anchor.y&&cubes[i].Matter.Material==MaterialId.Wood){seen[i]=true;queue[tail++]=i;}
        }
        public void StepSupport(int budget)
        {
            if(budget<0)throw new ArgumentOutOfRangeException(nameof(budget));LastSupportCandidates=0;
            while(searching&&LastSupportCandidates<budget)
            {
                if(head<tail)
                {
                    int i=queue[head++];LastSupportCandidates++;if(groupSearch)group[groupCount++]=i;
                    foreach(var delta in Neighbours)if(lookup.TryGetValue(cubes[i].Cell+delta,out int j)&&!seen[j]&&cubes[j].Owner==Ownership.Source){seen[j]=true;queue[tail++]=j;}
                    continue;
                }
                if(groupSearch)
                {
                    int slot=-1;for(int i=0;i<islands.Length;i++)if(!islands[i].Active){slot=i;break;}
                    if(slot<0){Status="Eight-island cap; remaining unsupported source retained";return;}
                    CommitIsland(slot);groupSearch=false;groupCount=0;
                }
                while(scan<Count&&LastSupportCandidates<budget)
                {
                    int i=scan++;LastSupportCandidates++;if(seen[i]||cubes[i].Owner!=Ownership.Source)continue;
                    head=tail=0;seen[i]=true;queue[tail++]=i;groupSearch=true;break;
                }
                if(scan==Count&&!groupSearch){searching=false;Status="Connectivity current; gravity-only island motion";}
            }
        }
        public int IntactAnchoredCells()
        {
            // Admission-only proof on the complete specimen; no mutation.
            var visited=new bool[Count];var work=new int[Count];int start=0,end=0;
            for(int i=0;i<Count;i++)if(cubes[i].Cell.y<=anchor.y&&cubes[i].Matter.Material==MaterialId.Wood){visited[i]=true;work[end++]=i;}
            while(start<end){int i=work[start++];foreach(var d in Neighbours)if(lookup.TryGetValue(cubes[i].Cell+d,out int j)&&!visited[j]){visited[j]=true;work[end++]=j;}}
            return end;
        }
        void CommitIsland(int slot)
        {
            var body=islands[slot];body.Count=groupCount;body.Mass=0;Vector3 com=Vector3.Zero;
            for(int k=0;k<groupCount;k++){int i=group[k];body.Members[k]=i;com+=Vector3.Transform(cubes[i].Position,inverseInitial)*cubes[i].Mass;body.Mass+=cubes[i].Mass;}
            com/=body.Mass;body.Position=Vector3.Transform(com,initialRotation);body.Rotation=initialRotation;body.Velocity=body.AngularVelocity=Vector3.Zero;
            Vector3 inertia=Vector3.Zero;
            for(int k=0;k<groupCount;k++)
            {
                int i=group[k];var r=Vector3.Transform(cubes[i].Position,inverseInitial)-com;cubes[i].Offset=r;cubes[i].Owner=Ownership.Island;cubes[i].Island=slot;
                float m=cubes[i].Mass,own=m*Cell*Cell/6;inertia+=new Vector3(m*(r.Y*r.Y+r.Z*r.Z)+own,m*(r.X*r.X+r.Z*r.Z)+own,m*(r.X*r.X+r.Y*r.Y)+own);
                source.Set(cubes[i].Cell,MaterialId.Air);
            }
            body.InverseInertia=new Vector3(1/inertia.X,1/inertia.Y,1/inertia.Z);body.Active=true;SourceCells-=groupCount;IslandCells+=groupCount;stumpDirty=true;
        }
        static Quaternion Advance(Quaternion q,Vector3 w,float dt)
        {float speed=w.Length();return speed<1e-8f?q:Quaternion.Normalize(Quaternion.CreateFromAxisAngle(w/speed,speed*dt)*q);}
        Vector3 InverseInertia(Island body,Vector3 world)
        {var local=Vector3.Transform(world,Quaternion.Conjugate(body.Rotation))*body.InverseInertia;return Vector3.Transform(local,body.Rotation);}
        float StumpTop(float x,float z)
        {
            var p=Vector3.Transform(new Vector3(x,0,z),inverseInitial);int gx=(int)Math.Floor(p.X/Cell)+anchor.x,gz=(int)Math.Floor(p.Z/Cell)+anchor.z;
            // Replaceable column cache; ALL authoritative source mutations above
            // invalidate it. It is derived from the still-owned source cells.
            if(stumpDirty)
            {
                stumpTops.Clear();foreach(var cube in cubes)if(cube.Owner==Ownership.Source)
                {var key=new Int3(cube.Cell.x,0,cube.Cell.z);float top=(cube.Cell.y+1-anchor.y)*Cell;if(!stumpTops.TryGetValue(key,out float old)||top>old)stumpTops[key]=top;}
                stumpDirty=false;
            }
            return stumpTops.TryGetValue(new Int3(gx,0,gz),out float height)?height:float.NegativeInfinity;
        }
        bool GroundContact(Vector3 p,Quaternion q,out float depth,out Vector3 contact)
        {
            depth=0;contact=Vector3.Zero;if(TerrainHeight==null)return false;
            for(int corner=0;corner<8;corner++)
            {
                var point=p+Vector3.Transform(new Vector3((corner&1)==0?-Half:Half,(corner&2)==0?-Half:Half,(corner&4)==0?-Half:Half),q);
                float h=TerrainHeight(point.X,point.Z),d=h-point.Y;
                if(d>=depth&&Finite(h)){depth=d;contact=point;}
            }
            return depth>0;
        }
        void StepIsland(Island body,float dt)
        {
            body.Velocity+=new Vector3(0,-9.81f*dt,0);body.Position+=body.Velocity*dt;body.Rotation=Advance(body.Rotation,body.AngularVelocity,dt);
            float deepest=0;Vector3 support=Vector3.Zero;bool ground=false;int contacts=0;
            for(int k=0;k<body.Count;k++)
            {
                var cube=cubes[body.Members[k]];var p=body.Position+Vector3.Transform(cube.Offset,body.Rotation);
                if(GroundContact(p,body.Rotation,out _,out _)){ground=true;break;}
                for(int corner=0;corner<8;corner++)
                {
                    var point=p+Vector3.Transform(new Vector3((corner&1)==0?-Half:Half,(corner&2)==0?-Half:Half,(corner&4)==0?-Half:Half),body.Rotation);
                    float top=StumpTop(point.X,point.Z),depth=top-point.Y;
                    if(depth<=0)continue;
                    if(depth>deepest+.002f){deepest=depth;support=point;contacts=1;}
                    else if(depth>=deepest-.002f){support+=point;contacts++;}
                }
            }
            if(ground){Crumble(body);return;}
            if(contacts>0)
            {
                support/=contacts;body.Position+=Vector3.UnitY*deepest;var r=support-body.Position;var velocity=body.Velocity+Vector3.Cross(body.AngularVelocity,r);
                if(velocity.Y<0)
                {
                    var cross=Vector3.Cross(r,Vector3.UnitY);float denom=1/body.Mass+Vector3.Dot(Vector3.Cross(InverseInertia(body,cross),r),Vector3.UnitY);
                    float impulse=-velocity.Y/Math.Max(denom,1e-8f);body.Velocity+=Vector3.UnitY*(impulse/body.Mass);body.AngularVelocity+=InverseInertia(body,Vector3.Cross(r,Vector3.UnitY*impulse));
                }
            }
        }
        void Crumble(Island body)
        {
            // Every member becomes an independent cube AT its current pose;
            // inherit linear and angular velocity, never add an explosion/lift.
            for(int k=0;k<body.Count;k++)
            {
                int i=body.Members[k];var r=Vector3.Transform(cubes[i].Offset,body.Rotation);
                cubes[i].Position=body.Position+r;cubes[i].Rotation=body.Rotation;cubes[i].Velocity=body.Velocity+Vector3.Cross(body.AngularVelocity,r);cubes[i].AngularVelocity=body.AngularVelocity;cubes[i].Owner=Ownership.Individual;cubes[i].Island=-1;
            }
            IslandCells-=body.Count;IndividualCells+=body.Count;body.Active=false;GroundImpacts++;Status="Actual terrain impact: independent rotating10cm cube bodies";
        }
        void StepCube(int i,float dt)
        {
            ref Cube cube=ref cubes[i];Vector3 old=cube.Position;
            cube.Velocity+=new Vector3(0,-9.81f*dt,0);
            if(SuctionEnabled&&SourceLineClear(Hand,cube.Position))
            {
                Vector3 to=Hand-cube.Position;float distance=to.Length();
                if(distance<.18f)
                {cargo.TryGetValue(cube.Matter,out long n);cargo[cube.Matter]=n+1;cube.Owner=Ownership.Cargo;IndividualCells--;CargoCells++;return;}
                if(distance<4&&distance>1e-5f)cube.Velocity+=to/distance*(18*dt);
            }
            if(cube.Velocity.LengthSquared()>225)cube.Velocity=Vector3.Normalize(cube.Velocity)*15;
            cube.Position+=cube.Velocity*dt;cube.Rotation=Advance(cube.Rotation,cube.AngularVelocity,dt);
            if(!Finite(cube.Position.X)||!Finite(cube.Position.Y)||!Finite(cube.Position.Z))throw new InvalidOperationException("Invalid cube pose");
            if(Math.Abs(cube.Position.X)>6.2f||Math.Abs(cube.Position.Z)>6.2f)
            {cube.Position=old;cube.Velocity=Vector3.Zero;Status="Contact-page boundary retains cube; restore or collect explicitly";}
            bool grounded=GroundContact(cube.Position,cube.Rotation,out float depth,out var contact);
            if(grounded)
            {
                float oldBottom=old.Y-Half;
                if(contact.Y+depth>oldBottom+.12f)
                {cube.Position.X=old.X;cube.Position.Z=old.Z;cube.Velocity.X*=-.1f;cube.Velocity.Z*=-.1f;grounded=GroundContact(cube.Position,cube.Rotation,out depth,out contact);}
                if(grounded)
                {
                    cube.Position.Y+=Math.Max(0,depth);
                    var r=contact-cube.Position;Vector3 v=cube.Velocity+Vector3.Cross(cube.AngularVelocity,r);
                    if(v.Y<0)
                    {float invI=6/(cube.Mass*Cell*Cell);float denom=1/cube.Mass+invI*Vector3.Cross(r,Vector3.UnitY).LengthSquared();float impulse=-v.Y/denom;cube.Velocity.Y+=impulse/cube.Mass;cube.AngularVelocity+=Vector3.Cross(r,Vector3.UnitY*impulse)*invI;}
                    cube.Velocity.X*=.8f;cube.Velocity.Z*=.8f;cube.AngularVelocity*=.85f;
                }
            }
            float stump=StumpTop(cube.Position.X,cube.Position.Z);
            if(cube.Position.Y-Half<stump&&old.Y-Half>=stump-.02f){cube.Position.Y=stump+Half;if(cube.Velocity.Y<0)cube.Velocity.Y=0;}
            ResolveStaticCube(ref cube);
            if(OtherStaticOverlap!=null&&OtherStaticOverlap(cube.Position)){cube.Position=old;cube.Velocity=Vector3.Zero;}
            if(PlayerPresent)
            {
                var center=PlayerFeet+Vector3.UnitY*.9f;var d=cube.Position-center;
                if(Math.Abs(d.X)<.44f&&Math.Abs(d.Z)<.44f&&Math.Abs(d.Y)<.98f)
                {Vector3 away=new Vector3(d.X,0,d.Z);if(away.LengthSquared()>1e-6f){away=Vector3.Normalize(away);cube.Position+=away*.012f;cube.Velocity+=new Vector3(PlayerVelocity.X,0,PlayerVelocity.Z)*.5f;cube.AngularVelocity+=Vector3.Cross(new Vector3(.03f,0,0),cube.Velocity)*2;}}
            }
        }
        bool SourceLineClear(Vector3 from,Vector3 to)
        {
            Vector3 a=Vector3.Transform(from,inverseInitial)+new Vector3(anchor.x*Cell,anchor.y*Cell,anchor.z*Cell),d=Vector3.Transform(to-from,inverseInitial);
            return !VoxelRaycast.TryCast(source,a.X,a.Y,a.Z,d.X,d.Y,d.Z,Math.Max(0,d.Length()-.075f),out _);
        }
        void ResolveStaticCube(ref Cube cube)
        {
            var center=Vector3.Transform(cube.Position,inverseInitial)+new Vector3(anchor.x*Cell,anchor.y*Cell,anchor.z*Cell);var q=inverseInitial*cube.Rotation;
            var a=Vector3.Transform(Vector3.UnitX,q);var b=Vector3.Transform(Vector3.UnitY,q);var c=Vector3.Transform(Vector3.UnitZ,q);
            var e=new Vector3(Math.Abs(a.X)+Math.Abs(b.X)+Math.Abs(c.X),Math.Abs(a.Y)+Math.Abs(b.Y)+Math.Abs(c.Y),Math.Abs(a.Z)+Math.Abs(b.Z)+Math.Abs(c.Z))*Half;
            int x0=(int)Math.Floor((center.X-e.X)/Cell),x1=(int)Math.Floor((center.X+e.X)/Cell),y0=(int)Math.Floor((center.Y-e.Y)/Cell),y1=(int)Math.Floor((center.Y+e.Y)/Cell),z0=(int)Math.Floor((center.Z-e.Z)/Cell),z1=(int)Math.Floor((center.Z+e.Z)/Cell);
            for(int z=z0;z<=z1;z++)for(int y=y0;y<=y1;y++)for(int x=x0;x<=x1;x++)if(source.Get(x,y,z)!=MaterialId.Air)
            {
                var p=Vector3.Transform(new Vector3((x-anchor.x+.5f)*Cell,(y-anchor.y+.5f)*Cell,(z-anchor.z+.5f)*Cell),initialRotation);
                if(CubeContact(cube,new Cube{Position=p,Rotation=initialRotation},out var normal,out float depth))
                {cube.Position-=normal*Math.Min(depth,.03f);float toward=Vector3.Dot(cube.Velocity,normal);if(toward>0)cube.Velocity-=normal*toward;}
            }
        }
        static int Hash(int x,int y,int z)=>unchecked((x*73856093^y*19349663^z*83492791)&32767);
        void BeginPairs()
        {
            Array.Fill(heads,-1);
            for(int i=0;i<Count;i++)if(cubes[i].Owner==Ownership.Individual)
            {var p=cubes[i].Position;bx[i]=(int)Math.Floor(p.X/.2f);by[i]=(int)Math.Floor(p.Y/.2f);bz[i]=(int)Math.Floor(p.Z/.2f);int h=Hash(bx[i],by[i],bz[i]);next[i]=heads[h];heads[h]=i;}
            pairI=pairOffset=0;pairChain=-2;pairsPending=true;
        }
        void ResolvePairs(int budget)
        {
            LastPairCandidates=0;
            while(pairI<Count&&LastPairCandidates<budget)
            {
                if(cubes[pairI].Owner!=Ownership.Individual){pairI++;pairOffset=0;pairChain=-2;LastPairCandidates++;continue;}
                int ox=pairOffset%3-1,oy=pairOffset/3%3-1,oz=pairOffset/9-1;
                int x=bx[pairI]+ox,y=by[pairI]+oy,z=bz[pairI]+oz;
                if(pairChain==-2){pairChain=heads[Hash(x,y,z)];LastPairCandidates++;if(LastPairCandidates==budget)break;}
                if(pairChain<0){pairOffset++;pairChain=-2;if(pairOffset==27){pairI++;pairOffset=0;}continue;}
                int j=pairChain;pairChain=next[j];LastPairCandidates++;
                if(j<=pairI||cubes[j].Owner!=Ownership.Individual||bx[j]!=x||by[j]!=y||bz[j]!=z)continue;
                if((cubes[j].Position-cubes[pairI].Position).LengthSquared()>.0301f)continue;
                if(CubeContact(cubes[pairI],cubes[j],out var normal,out float depth)){PairImpulse(pairI,j,normal,depth);PairContacts++;}
            }
            if(pairI==Count)pairsPending=false;
        }
        static bool CubeContact(Cube a,Cube b,out Vector3 normal,out float depth)
        {
            Vector3 delta=b.Position-a.Position;var ax=Vector3.Transform(Vector3.UnitX,a.Rotation);var ay=Vector3.Transform(Vector3.UnitY,a.Rotation);var az=Vector3.Transform(Vector3.UnitZ,a.Rotation);
            var bx=Vector3.Transform(Vector3.UnitX,b.Rotation);var by=Vector3.Transform(Vector3.UnitY,b.Rotation);var bz=Vector3.Transform(Vector3.UnitZ,b.Rotation);
            Vector3 best=Vector3.UnitY;float overlap=float.MaxValue;
            bool Axis(Vector3 n)
            {
                float length=n.Length();if(length<1e-6f)return true;n/=length;
                float radius=Half*(Math.Abs(Vector3.Dot(n,ax))+Math.Abs(Vector3.Dot(n,ay))+Math.Abs(Vector3.Dot(n,az))+Math.Abs(Vector3.Dot(n,bx))+Math.Abs(Vector3.Dot(n,by))+Math.Abs(Vector3.Dot(n,bz)));
                float distance=Vector3.Dot(delta,n),d=radius-Math.Abs(distance);if(d<-.00001f)return false;
                if(d<overlap){overlap=d;best=distance>=0?n:-n;}return true;
            }
            bool hit=Axis(ax)&&Axis(ay)&&Axis(az)&&Axis(bx)&&Axis(by)&&Axis(bz);
            if(hit)for(int i=0;i<3&&hit;i++)for(int j=0;j<3&&hit;j++)hit=Axis(Vector3.Cross(i==0?ax:i==1?ay:az,j==0?bx:j==1?by:bz));
            normal=best;depth=Math.Max(0,overlap);return hit;
        }
        void PairImpulse(int i,int j,Vector3 normal,float depth)
        {
            ref Cube a=ref cubes[i];ref Cube b=ref cubes[j];float ia=1/a.Mass,ib=1/b.Mass;
            float correction=Math.Min(depth*.5f,.01f)/(ia+ib);a.Position-=normal*(correction*ia);b.Position+=normal*(correction*ib);
            Vector3 point=(a.Position+b.Position)*.5f,ra=point-a.Position,rb=point-b.Position;
            Vector3 relative=b.Velocity+Vector3.Cross(b.AngularVelocity,rb)-a.Velocity-Vector3.Cross(a.AngularVelocity,ra);float speed=Vector3.Dot(relative,normal);if(speed>=0)return;
            float iia=6/(a.Mass*Cell*Cell),iib=6/(b.Mass*Cell*Cell);
            float denom=ia+ib+iia*Vector3.Cross(ra,normal).LengthSquared()+iib*Vector3.Cross(rb,normal).LengthSquared();var impulse=normal*(-speed/Math.Max(denom,1e-8f));
            a.Velocity-=impulse*ia;b.Velocity+=impulse*ib;a.AngularVelocity-=Vector3.Cross(ra,impulse)*iia;b.AngularVelocity+=Vector3.Cross(rb,impulse)*iib;
        }
        public int Kick(Vector3 eye,Vector3 direction)
        {
            if(direction.LengthSquared()<1e-8f||pairsPending)return 0;direction=Vector3.Normalize(direction);int count=0;
            foreach(var body in islands)if(body.Active)
            {
                for(int k=0;k<body.Count;k++)
                {
                    var point=body.Position+Vector3.Transform(cubes[body.Members[k]].Offset,body.Rotation);var delta=point-eye;float along=Vector3.Dot(delta,direction);
                    if(along<0||along>2||(delta-direction*along).LengthSquared()>.09f)continue;
                    // An explicit40Ns player kick at the actual hit point. It is
                    // never called automatically to manufacture a falling clip.
                    var impulse=direction*40;body.Velocity+=impulse/body.Mass;body.AngularVelocity+=InverseInertia(body,Vector3.Cross(point-body.Position,impulse));count++;break;
                }
            }
            for(int i=0;i<Count;i++)if(cubes[i].Owner==Ownership.Individual)
            {
                var delta=cubes[i].Position-eye;float along=Vector3.Dot(delta,direction);
                if(along<0||along>2||(delta-direction*along).LengthSquared()>.09f)continue;
                cubes[i].Velocity+=direction*4;cubes[i].AngularVelocity+=Vector3.Cross(new Vector3(.03f,0,.02f),direction*4)*600;count++;
            }
            return count;
        }
        public void Step(float elapsed,int supportBudget=2048,int bodyBudget=8192,int pairBudget=131072)
        {
            if(!Finite(elapsed)||elapsed<0||elapsed>.25f||bodyBudget<Count||pairBudget<1)throw new ArgumentOutOfRangeException(nameof(elapsed));
            StepSupport(supportBudget);timeDebt+=elapsed;lastBodies=0;int pairWork=0;
            if(pairsPending){ResolvePairs(pairBudget);pairWork=LastPairCandidates;if(pairsPending)return;}
            for(int substep=0;substep<2&&timeDebt>1e-8&&lastBodies+Count<=bodyBudget&&pairWork<pairBudget;substep++)
            {
                float dt=(float)Math.Min(.01,timeDebt);timeDebt-=dt;
                // Newly crumbled cubes start independently on the NEXT substep,
                // so no cube advances once as an island and again as a particle.
                for(int i=0;i<Count;i++)if(cubes[i].Owner==Ownership.Individual){StepCube(i,dt);lastBodies++;}
                foreach(var body in islands)if(body.Active){StepIsland(body,dt);lastBodies+=body.Count;}
                BeginPairs();ResolvePairs(pairBudget-pairWork);pairWork+=LastPairCandidates;if(pairsPending)break;
            }
            LastPairCandidates=pairWork;
            if(!Conserved)throw new InvalidOperationException("Tree matter ownership count failed");
        }
    }
}
