using System;
using System.Collections.Generic;
namespace AshenVoxel.Core
{
    /// <summary>Budgeted single-writer cellular gravity, not structural rigidity. Settled cells sleep until a nearby edit.</summary>
    public sealed class Simulation : IDisposable
    {
        readonly VoxelWorld world;
        readonly Queue<Int3> frontier=new Queue<Int3>();
        readonly HashSet<Int3> queued=new HashSet<Int3>();
        readonly HashSet<Int3> movedThisStep=new HashSet<Int3>();
        List<Int3> wakeChunks;
        int wakeChunkIndex,wakeCellIndex;
        /// <summary>Remaining cell candidates in the current allocated-chunk rescan, not the active gravity queue.</summary>
        public long PendingWakeCandidates => wakeChunks==null?0:((long)wakeChunks.Count-wakeChunkIndex)*Chunk.Volume-wakeCellIndex;
        public int LastWakeCandidates {get;private set;}
        static readonly Int3[] Directions={new Int3(1,-1,0),new Int3(-1,-1,0),new Int3(0,-1,1),new Int3(0,-1,-1)};
        public int ActiveCount => frontier.Count;
        public int LastProcessed {get;private set;}
        public int LastMoves {get;private set;}
        public long TotalMoves {get;private set;}
        public long Tick {get;private set;}
        public Simulation(VoxelWorld world)
        {
            this.world=world??throw new ArgumentNullException(nameof(world));
            world.Changed+=NotifyEdit;world.Cleared+=Clear;
        }
        public void Dispose(){world.Changed-=NotifyEdit;world.Cleared-=Clear;}
        public void Clear(){frontier.Clear();queued.Clear();movedThisStep.Clear();wakeChunks=null;wakeChunkIndex=0;wakeCellIndex=0;LastWakeCandidates=0;}
        /// <summary>Begin a deterministic allocated-chunk scan to recover unstable grains after loading a material snapshot.
        /// Only chunk keys are captured here. Call WakeBudget on subsequent ticks; edits during scanning wake normally.</summary>
        public void BeginWakeAll()
        {
            wakeChunks=new List<Int3>(world.Chunks.Keys);
            wakeChunks.Sort((a,b)=>{int c=a.z.CompareTo(b.z);if(c==0)c=a.y.CompareTo(b.y);return c==0?a.x.CompareTo(b.x):c;});
            wakeChunkIndex=0;wakeCellIndex=0;LastWakeCandidates=0;
            if(wakeChunks.Count==0)wakeChunks=null;
        }
        /// <summary>Inspect at most budget cell candidates from the captured chunks; true means the scan completed.
        /// Uses current world reads, tolerating chunks removed during the scan. Newly added chunks wake via Changed.</summary>
        public bool WakeBudget(int budget)
        {
            if(budget<0)throw new ArgumentOutOfRangeException(nameof(budget));
            LastWakeCandidates=0;
            while(wakeChunks!=null&&LastWakeCandidates<budget)
            {
                Int3 key=wakeChunks[wakeChunkIndex],local=Chunk.Decode(wakeCellIndex);
                Enqueue(new Int3(key.x*Chunk.Size+local.x,key.y*Chunk.Size+local.y,key.z*Chunk.Size+local.z));
                LastWakeCandidates++;wakeCellIndex++;
                if(wakeCellIndex==Chunk.Volume)
                {
                    wakeCellIndex=0;wakeChunkIndex++;
                    if(wakeChunkIndex==wakeChunks.Count){wakeChunks=null;wakeChunkIndex=0;}
                }
            }
            return wakeChunks==null;
        }
        void Enqueue(Int3 p)
        {
            if(world.InBounds(p)&&p.y>0&&MaterialCatalog.Get(world.Get(p)).Granular&&queued.Add(p))frontier.Enqueue(p);
        }
        public void NotifyEdit(Int3 p)
        {
            // All 26 neighbors can lose a diagonal path/support as a result of an edit.
            for(int z=-1;z<=1;z++)for(int y=-1;y<=1;y++)for(int x=-1;x<=1;x++) Enqueue(new Int3(p.x+x,p.y+y,p.z+z));
        }
        public void ActivateSphere(float x,float y,float z,float radius)
        {
            if(!WorldEdits.Finite(x)||!WorldEdits.Finite(y)||!WorldEdits.Finite(z)||!WorldEdits.Finite(radius)||radius<0)throw new ArgumentOutOfRangeException(nameof(radius));
            double s=world.VoxelSize;
            int xmin=(int)Math.Max(0,Math.Min(world.SizeX,Math.Floor(((double)x-radius)/s))), xmax=(int)Math.Max(-1,Math.Min(world.SizeX-1,Math.Ceiling(((double)x+radius)/s)));
            int ymin=(int)Math.Max(1,Math.Min(world.SizeY,Math.Floor(((double)y-radius)/s))), ymax=(int)Math.Max(0,Math.Min(world.SizeY-1,Math.Ceiling(((double)y+radius)/s)));
            int zmin=(int)Math.Max(0,Math.Min(world.SizeZ,Math.Floor(((double)z-radius)/s))), zmax=(int)Math.Max(-1,Math.Min(world.SizeZ-1,Math.Ceiling(((double)z+radius)/s)));
            for(int iz=zmin;iz<=zmax;iz++)for(int iy=ymin;iy<=ymax;iy++)for(int ix=xmin;ix<=xmax;ix++)
            { double dx=(ix+.5)*s-x,dy=(iy+.5)*s-y,dz=(iz+.5)*s-z;if(dx*dx+dy*dy+dz*dz<=(double)radius*radius)Enqueue(new Int3(ix,iy,iz)); }
        }
        /// <summary>Processes at most budget queued cells, and never moves any particle twice during one call.</summary>
        public int Step(int budget)
        {
            if(budget<0)throw new ArgumentOutOfRangeException(nameof(budget));
            Tick++;LastMoves=0;LastProcessed=0;movedThisStep.Clear();
            int count=Math.Min(budget,frontier.Count);
            for(int n=0;n<count;n++)
            {
                Int3 from=frontier.Dequeue();queued.Remove(from);LastProcessed++;
                if(movedThisStep.Contains(from)){Enqueue(from);continue;}
                MaterialId id=world.Get(from);if(!MaterialCatalog.Get(id).Granular)continue;
                Int3 to=new Int3(from.x,from.y-1,from.z);
                bool falls=CanMove(to);
                if(!falls)
                {
                    int offset=(int)(Generation.Hash(from.x,from.z,world.Seed+(int)Tick)&3);
                    for(int i=0;i<4;i++)
                    {
                        Int3 d=Directions[(i+offset)&3];
                        to=from+d;
                        // Do not let grains slip through a solid corner.
                        if(CanMove(to)&&world.Get(from.x+d.x,from.y,from.z+d.z)==MaterialId.Air){falls=true;break;}
                    }
                }
                if(!falls)continue;
                movedThisStep.Add(to);
                uint tint=world.GetTint(from);
                world.Set(from,MaterialId.Air);world.Set(to,id,tint);
                Enqueue(to);LastMoves++;TotalMoves++;
            }
            return LastMoves;
        }
        bool CanMove(Int3 p) => p.y>0&&world.InBounds(p)&&world.Get(p)==MaterialId.Air;
    }
}
