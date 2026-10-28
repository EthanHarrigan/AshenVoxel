using System;
using System.Collections.Generic;
using AshenVoxel.Core;

namespace AshenVoxel.Core.Experimental
{
    public readonly struct AggregateVoxel
    {
        public readonly Int3 Local;
        public readonly CargoKey Matter;
        public AggregateVoxel(Int3 local,CargoKey matter){Local=local;Matter=matter;}
    }

    /// <summary>One explicitly admitted connected component. Matter ownership is
    /// world -> aggregate -> debris/cargo; rigid pose never changes its ledger.
    /// Capture is atomic within an explicit count budget, not a time guarantee.
    /// One host may hold one active aggregate; other cuts remain in world until
    /// admission. Native motion belongs to an ordinary aggregate Rigidbody.
    /// This candidate does not serialize pending/owned aggregate state.</summary>
    public sealed class FallingAggregate
    {
        static readonly Int3[] Neighbors={new Int3(1,0,0),new Int3(-1,0,0),new Int3(0,1,0),new Int3(0,-1,0),new Int3(0,0,1),new Int3(0,0,-1)};
        static readonly Int3[] Probes={new Int3(0,0,0),new Int3(1,0,0),new Int3(-1,0,0),new Int3(0,1,0),new Int3(0,0,1),new Int3(0,0,-1),new Int3(0,-1,0)};
        public const int MaximumCells=4096;
        readonly AggregateVoxel[] voxels;
        readonly bool[] emitted;
        readonly Dictionary<Int3,int> localIndex;
        readonly LocalizedMatter sink;
        readonly VoxelWorld world;
        int cursor,probe,collectionCursor;
        double px,py,pz,qx,qy,qz,qw=1;
        public float CellSize {get;}
        public int OriginalCount=>voxels.Length;
        public int Remaining {get;private set;}
        public int Version {get;private set;}
        public int LastCandidates {get;private set;}
        public bool ImpactReported {get;private set;}
        public long RequiredStaticRevision {get;private set;}
        public string Status {get;private set;}
        public double OriginX {get;}
        public double OriginY {get;}
        public double OriginZ {get;}
        public double MassCue {get;}
        FallingAggregate(VoxelWorld world,LocalizedMatter sink,List<Int3> cells)
        {
            this.sink=sink;this.world=world;CellSize=world.VoxelSize;
            int x=int.MaxValue,y=x,z=x;foreach(var p in cells){x=Math.Min(x,p.x);y=Math.Min(y,p.y);z=Math.Min(z,p.z);}
            OriginX=px=x*(double)CellSize;OriginY=py=y*(double)CellSize;OriginZ=pz=z*(double)CellSize;
            voxels=new AggregateVoxel[cells.Count];emitted=new bool[cells.Count];localIndex=new Dictionary<Int3,int>(cells.Count);
            double mass=0;
            for(int i=0;i<cells.Count;i++)
            {
                Int3 p=cells[i],local=new Int3(p.x-x,p.y-y,p.z-z);
                var matter=new CargoKey(world.Get(p),world.GetTint(p));
                voxels[i]=new AggregateVoxel(local,matter);localIndex.Add(local,i);
                mass+=MaterialCatalog.Get(matter.Material).Density*(double)CellSize*CellSize*CellSize;
            }
            MassCue=mass;Remaining=cells.Count;Status="Captured; wait for static collision revision before native activation";
        }
        static bool Coherent(MaterialId m){var d=MaterialCatalog.Get(m);return d.Solid&&!d.Granular;}
        public static bool TryCapture(VoxelWorld world,LocalizedMatter sink,IReadOnlyList<Int3> component,int candidateBudget,int mutationBudget,out FallingAggregate aggregate,out string reason)
        {
            if(world==null||sink==null||component==null)throw new ArgumentNullException();
            aggregate=null;reason=null;
            if(sink.Pending){reason="Another material operation is pending; source retained";return false;}
            int count=component.Count;
            if(count<1||count>MaximumCells){reason="Component count outside admitted bound; source retained";return false;}
            if(candidateBudget<count*16||mutationBudget<count){reason="Atomic validation/mutation allowance insufficient; source retained";return false;}
            var cells=new List<Int3>(count);var set=new HashSet<Int3>();
            foreach(var p in component)
            {
                if(!world.InBounds(p)||p.y<1||!Coherent(world.Get(p))||sink.IsOccupied(p)||!set.Add(p))
                {reason="Invalid, occupied or duplicate component cell; source retained";return false;}
                cells.Add(p);
            }
            foreach(var p in cells)
            {
                foreach(var d in Neighbors)
                {var q=p+d;if(Coherent(world.Get(q))&&!set.Contains(q)){reason="Still connected to coherent world matter";return false;}}
                var below=p+new Int3(0,-1,0);
                if(below.y<1||(!set.Contains(below)&&(world.Get(below)!=MaterialId.Air||sink.IsOccupied(below))))
                {reason="Supported component; no transfer";return false;}
            }
            var seen=new HashSet<Int3>();var queue=new Queue<Int3>();queue.Enqueue(cells[0]);seen.Add(cells[0]);
            while(queue.Count>0){var p=queue.Dequeue();foreach(var d in Neighbors){var n=p+d;if(set.Contains(n)&&seen.Add(n))queue.Enqueue(n);}}
            if(seen.Count!=count){reason="Disconnected request must be admitted as separate components";return false;}
            cells.Sort((a,b)=>{int c=a.y.CompareTo(b.y);if(c==0)c=a.z.CompareTo(b.z);return c==0?a.x.CompareTo(b.x):c;});
            // Allocate complete payload before withdrawing any world matter.
            aggregate=new FallingAggregate(world,sink,cells);
            foreach(var p in cells)world.Set(p,MaterialId.Air);
            aggregate.RequiredStaticRevision=world.Revision;return true;
        }
        public AggregateVoxel GetVoxel(int index)=>voxels[index];
        public bool IsRemaining(int index)=>!emitted[index];
        public bool HasRemaining(Int3 local,out CargoKey matter)
        {if(localIndex.TryGetValue(local,out int i)&&!emitted[i]){matter=voxels[i].Matter;return true;}matter=default;return false;}
        public void SetPose(double x,double y,double z,double a,double b,double c,double d)
        {
            double norm=a*a+b*b+c*c+d*d;
            if(!Finite(x)||!Finite(y)||!Finite(z)||!Finite(norm)||norm<1e-12)throw new ArgumentOutOfRangeException(nameof(x));
            double s=1/Math.Sqrt(norm);px=x;py=y;pz=z;qx=a*s;qy=b*s;qz=c*s;qw=d*s;
        }
        static bool Finite(double n)=>!double.IsNaN(n)&&!double.IsInfinity(n);
        public void WorldCenter(int index,out double x,out double y,out double z)
        {
            var p=voxels[index].Local;double a=(p.x+.5)*CellSize,b=(p.y+.5)*CellSize,c=(p.z+.5)*CellSize;
            double tx=2*(qy*c-qz*b),ty=2*(qz*a-qx*c),tz=2*(qx*b-qy*a);
            x=px+a+qw*tx+qy*tz-qz*ty;y=py+b+qw*ty+qz*tx-qx*tz;z=pz+c+qw*tz+qx*ty-qy*tx;
        }
        public void ReportImpact(){if(ImpactReported)return;ImpactReported=true;Status="Impact: bounded breakup pending";}
        public int EmitFragments(int candidateBudget)
        {
            if(candidateBudget<0)throw new ArgumentOutOfRangeException(nameof(candidateBudget));LastCandidates=0;
            if(!ImpactReported||Remaining==0)return 0;
            if(sink.DebrisCapacityRemaining==0){Status="Inactive recoverable aggregate: fragment capacity full";return 0;}
            int moved=0;
            while(cursor<voxels.Length&&LastCandidates<candidateBudget)
            {
                if(emitted[cursor]){LastCandidates++;cursor++;probe=0;continue;}
                WorldCenter(cursor,out double x,out double y,out double z);
                // Guard before narrowing arbitrary external body positions.
                double gx=Math.Floor(x/CellSize),gy=Math.Floor(y/CellSize),gz=Math.Floor(z/CellSize);
                if(gx<int.MinValue+2||gy<int.MinValue+2||gz<int.MinValue+2||gx>int.MaxValue-2||gy>int.MaxValue-2||gz>int.MaxValue-2)
                {Status="Outside finite sink; aggregate matter retained";break;}
                var target=new Int3((int)gx,(int)gy,(int)gz)+Probes[probe];LastCandidates++;
                if(sink.TryAdmitAggregateFragment(target,voxels[cursor].Matter,this))
                {emitted[cursor]=true;Remaining--;Version++;moved++;cursor++;probe=0;}
                else if(++probe==Probes.Length){probe=0;Status="Fragment destinations blocked; aggregate matter retained";break;}
                if(sink.DebrisCapacityRemaining==0)break;
            }
            if(Remaining==0)Status="Aggregate fully transferred; native actor can retire";
            return moved;
        }
        // Overflow matter remains directly recoverable even if fragment capacity
        // is full. Tests cover repeated calls; only this owner clears its bit.
        public int CollectNearby(double x,double y,double z,double radius,int candidateBudget)
        {
            if(!Finite(x)||!Finite(y)||!Finite(z)||!Finite(radius)||radius<0||candidateBudget<0)throw new ArgumentOutOfRangeException(nameof(radius));
            int collected=0;LastCandidates=0;
            while(LastCandidates<Math.Min(candidateBudget,voxels.Length)&&Remaining>0)
            {
                int i=collectionCursor++;if(collectionCursor==voxels.Length)collectionCursor=0;
                LastCandidates++;if(emitted[i])continue;WorldCenter(i,out double a,out double b,out double c);
                double dx=(a-x)/Math.Max(radius,1e-12),dy=(b-y)/Math.Max(radius,1e-12),dz=(c-z)/Math.Max(radius,1e-12);
                if(dx*dx+dy*dy+dz*dz>1)continue;
                double distance=Math.Sqrt((a-x)*(a-x)+(b-y)*(b-y)+(c-z)*(c-z));
                if(distance>1e-7&&VoxelRaycast.TryCast(world,(float)x,(float)y,(float)z,(float)(a-x),(float)(b-y),(float)(c-z),(float)Math.Max(0,distance-CellSize*.001),out _))continue;
                if(!sink.TryCreditAggregateMatter(voxels[i].Matter))continue;
                emitted[i]=true;Remaining--;Version++;collected++;
            }
            return collected;
        }
    }

    /// <summary>Single host slot. Full slot leaves later unsupported matter in
    /// its authoritative world representation; it is never discarded to fit a
    /// physics-body cap. Empty old actors must be retired by the native owner.</summary>
    public sealed class FallingAggregateSlot
    {
        public FallingAggregate Current {get;private set;}
        public bool TryCapture(VoxelWorld world,LocalizedMatter sink,IReadOnlyList<Int3> component,int candidates,int mutations,out string reason)
        {
            if(Current!=null&&Current.Remaining>0){reason="Physics slot occupied; additional matter retained in world";return false;}
            if(!FallingAggregate.TryCapture(world,sink,component,candidates,mutations,out FallingAggregate next,out reason))return false;
            Current=next;return true;
        }
    }
}
