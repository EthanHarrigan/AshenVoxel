using System;
using System.Collections.Generic;
using System.IO;
using AshenVoxel.Core;
using RootContactPrototype;

namespace RootContactInteraction
{
    public readonly struct WoodCell
    {
        public readonly Int3 Position;public readonly uint Color;
        public WoodCell(Int3 position,uint color){Position=position;Color=color;}
    }
    // Shared immutable canonical occupancy, including AIR, is complete over rows29..32.
    // Loading/validating/hashing this once-per-template object is outside per-instance Step.
    public sealed class UpperContext
    {
        public const int MaxCells=16384;
        readonly WoodCell[] cells;readonly Dictionary<Int3,uint> colors;
        public readonly string Identity,CanonicalSourceIdentity;
        public int Count=>cells.Length;
        public WoodCell Cell(int i)=>cells[i];
        public bool TryColor(Int3 p,out uint tint)=>colors.TryGetValue(p,out tint);
        public UpperContext(WoodCell[] input,string canonicalSourceIdentity)
        {
            if(input==null||input.Length>MaxCells||string.IsNullOrEmpty(canonicalSourceIdentity)||canonicalSourceIdentity.Length>4096)throw new ArgumentException("Bounded complete canonical context required");
            cells=(WoodCell[])input.Clone();colors=new Dictionary<Int3,uint>();CanonicalSourceIdentity=canonicalSourceIdentity;
            Array.Sort(cells,(a,b)=>a.Position.x!=b.Position.x?a.Position.x.CompareTo(b.Position.x):a.Position.y!=b.Position.y?a.Position.y.CompareTo(b.Position.y):a.Position.z.CompareTo(b.Position.z));
            using var stream=new MemoryStream();using var w=new BinaryWriter(stream);w.Write(canonicalSourceIdentity);w.Write("complete-canonical-wood-rows29..32");
            foreach(var cell in cells)
            {
                Int3 p=cell.Position;if(p.y<29||p.y>32||p.x< -131||p.x>130||p.z< -131||p.z>130||cell.Color>>24!=255||colors.ContainsKey(p))throw new ArgumentException("Invalid context cell");
                colors.Add(p,cell.Color);w.Write(p.x);w.Write(p.y);w.Write(p.z);w.Write(cell.Color);
            }
            w.Flush();Identity=Policy.Hash(stream.ToArray());
        }
    }
    // Revision strings must never be reused after mutation. Single-owner synchronous API.
    public interface ICurrentIdentities {string SourceIdentity{get;}string ContextIdentity{get;}}
    public enum Phase { ValidateRoot,ValidateContext,Erode,Dilate,LeaderClosest,LeaderLocal,LeaderKeep,Classify,Complete,Cancelled,Failed }
    public enum Channel { None,Hard,Stick }
    public sealed class Result
    {
        readonly RootContactPrototype.Result root;readonly bool[] hard;
        public readonly int HardCount,StickCount;
        public readonly string SourceIdentity,ContextIdentity,RootProvenance,PolicyIdentity,Provenance;
        internal Result(RootContactPrototype.Result source,bool[] retained,int hardCount,int stickCount,UpperContext context,string policy,string provenance)
        {root=source;hard=retained;HardCount=hardCount;StickCount=stickCount;SourceIdentity=source.SourceIdentity;ContextIdentity=context.Identity;RootProvenance=source.Provenance;PolicyIdentity=policy;Provenance=provenance;}
        public int RootCellCount=>root.OccupiedCount;
        public Int3 Cell(int i)=>root.Cell(i);
        public Channel At(int i)=>root.Cell(i).y>=30?Channel.None:hard[i]?Channel.Hard:Channel.Stick;
        public MaterialId Material(int i)=>root.NeighborMaterial(root.Cell(i));
        public uint Color(int i)=>root.Color(root.Cell(i));
    }
    public sealed class Builder
    {
        public const string InteractionPolicy="AVPM-tree-hard=wood&dilate5(erode3(wood));leader-from-y0:near4,closest-xz-tie,local3,keep3;output-y<=29;context-through32;v1";
        readonly RootContactPrototype.Result root;readonly UpperContext context;readonly ICurrentIdentities current;
        readonly string provenance;bool[] hard;HashSet<Int3> core=new HashSet<Int3>();
        List<int>[] rows=new List<int>[30];int index,neighbor,leaderY,leaderIndex,localCount;
        int emittedCount,hardCount,stickCount;double centerX,centerZ,closestDistance=double.PositiveInfinity,sumX,sumZ;
        Int3 closest;bool hasNear;
        public Phase State{get;private set;}=Phase.ValidateRoot;
        public string Failure{get;private set;}="";
        public Result Published{get;private set;}
        public long Candidates{get;private set;}public long MorphologyQueries{get;private set;}public long LeaderPointCandidates{get;private set;}
        public int CoreCells{get;private set;}public int LeaderRowsKept{get;private set;}
        public int PendingPhaseItems=>State==Phase.ValidateRoot||State==Phase.Dilate||State==Phase.Classify?root.OccupiedCount-index:State==Phase.ValidateContext?context.Count-index:State==Phase.Erode?root.OccupiedCount+context.Count-index:IsLeader?rows[leaderY].Count-leaderIndex:0;
        public int PendingNeighborhoodProbes=>State==Phase.Erode?27-neighbor:State==Phase.Dilate?125-neighbor:0;
        public bool Terminal=>State==Phase.Complete||State==Phase.Cancelled||State==Phase.Failed;
        bool IsLeader=>State==Phase.LeaderClosest||State==Phase.LeaderLocal||State==Phase.LeaderKeep;
        public Builder(RootContactPrototype.Result source,UpperContext upper,ICurrentIdentities identities)
        {
            root=source??throw new ArgumentNullException(nameof(source));context=upper??throw new ArgumentNullException(nameof(upper));current=identities??throw new ArgumentNullException(nameof(identities));
            if(root.OccupiedCount<1||root.OccupiedCount>Policy.MaxOccupied)throw new ArgumentException("Bounded root required");
            hard=new bool[root.OccupiedCount];
            provenance=Policy.Hash(System.Text.Encoding.UTF8.GetBytes(InteractionPolicy+";root="+root.Provenance+";context="+context.Identity+";source="+root.SourceIdentity));
        }
        bool Current()=>current.SourceIdentity==root.SourceIdentity&&current.ContextIdentity==context.Identity;
        void Discard(){hard=null;core=null;rows=null;Published=null;}
        void Fail(string reason){Failure=reason;State=Phase.Failed;Discard();}
        public void Cancel(){if(!Terminal){Failure="Cancelled";State=Phase.Cancelled;Discard();}}
        public void Step(int candidateBudget)
        {
            if(candidateBudget<0)throw new ArgumentOutOfRangeException(nameof(candidateBudget));if(Terminal)return;
            try
            {
                if(!Current()){Fail("StaleIdentity");return;}
                while(!Terminal&&candidateBudget-->0)
                {
                    Candidates++;
                    switch(State)
                    {
                        case Phase.ValidateRoot:ValidateRootOne();break;
                        case Phase.ValidateContext:ValidateContextOne();break;
                        case Phase.Erode:ErodeOne();break;
                        case Phase.Dilate:DilateOne();break;
                        case Phase.LeaderClosest:LeaderClosestOne();break;
                        case Phase.LeaderLocal:LeaderLocalOne();break;
                        case Phase.LeaderKeep:LeaderKeepOne();break;
                        case Phase.Classify:ClassifyOne();break;
                    }
                }
                if(!Terminal&&!Current())Fail("StaleIdentity");
            }
            catch(Exception e){Fail("SourceOrBuildException:"+e.GetType().Name);}
        }
        void Move(Phase phase){State=phase;index=0;neighbor=0;}
        bool Wood(Int3 p)=>p.y<30?root.NeighborMaterial(p)==MaterialId.Wood:context.TryColor(p,out _);
        void ValidateRootOne()
        {
            Int3 p=root.Cell(index);
            if(p.y>30||p.y<Policy.MinSourceY-Policy.MaxDeformation||p.x<Policy.MinCoordinate||p.x>Policy.MaxCoordinate||p.z<Policy.MinCoordinate||p.z>Policy.MaxCoordinate||root.NeighborMaterial(p)!=MaterialId.Wood){Fail("InvalidRootMatter");return;}
            if(p.y>=29&&(!context.TryColor(p,out uint tint)||tint!=root.Color(p))){Fail("PinnedContextMismatch");return;}
            if(p.y<30)
            {
                emittedCount++;
                if(p.y>=0){if(rows[p.y]==null)rows[p.y]=new List<int>();rows[p.y].Add(index);}
            }
            if(++index==root.OccupiedCount)Move(context.Count==0?Phase.Erode:Phase.ValidateContext);
        }
        void ValidateContextOne()
        {
            var cell=context.Cell(index);Int3 p=cell.Position;
            if(p.y<=30&&(root.NeighborMaterial(p)!=MaterialId.Wood||root.Color(p)!=cell.Color)){Fail("PinnedContextMismatch");return;}
            if(++index==context.Count)Move(Phase.Erode);
        }
        void ErodeOne()
        {
            bool isContext=index>=root.OccupiedCount;Int3 p=isContext?context.Cell(index-root.OccupiedCount).Position:root.Cell(index);
            // Row32 only provides erosion halo. Duplicate canonical29/30 were already covered by root.
            if(isContext&&(p.y<=30||p.y>31)){FinishErosionCell();return;}
            int dx=neighbor%3-1,dy=neighbor/3%3-1,dz=neighbor/9-1;MorphologyQueries++;
            if(!Wood(new Int3(p.x+dx,p.y+dy,p.z+dz))){FinishErosionCell();return;}
            if(++neighbor==27)
            {
                if(core.Count>=Policy.MaxOccupied+UpperContext.MaxCells){Fail("CoreCap");return;}
                core.Add(p);CoreCells=core.Count;FinishErosionCell();
            }
        }
        void FinishErosionCell(){neighbor=0;if(++index==root.OccupiedCount+context.Count)Move(Phase.Dilate);}
        void DilateOne()
        {
            Int3 p=root.Cell(index);if(p.y>=30){FinishDilationCell();return;}
            int dx=neighbor%5-2,dy=neighbor/5%5-2,dz=neighbor/25-2;MorphologyQueries++;
            if(core.Contains(new Int3(p.x+dx,p.y+dy,p.z+dz))){hard[index]=true;FinishDilationCell();return;}
            if(++neighbor==125)FinishDilationCell();
        }
        void FinishDilationCell(){neighbor=0;if(++index==root.OccupiedCount){core=null;BeginLeaderRow();}}
        void BeginLeaderRow()
        {
            // Empty or remote rows permanently stop leader tracking, exactly like the bake's break.
            if(leaderY==30||rows[leaderY]==null||rows[leaderY].Count==0){Move(Phase.Classify);return;}
            leaderIndex=0;closestDistance=double.PositiveInfinity;hasNear=false;State=Phase.LeaderClosest;
        }
        static double Distance(Int3 p,double x,double z){double dx=p.x-x,dz=p.z-z;return dx*dx+dz*dz;}
        void LeaderClosestOne()
        {
            Int3 p=root.Cell(rows[leaderY][leaderIndex]);double d=Distance(p,centerX,centerZ);LeaderPointCandidates++;
            hasNear|=d<=16;
            if(d<closestDistance||d==closestDistance&&(p.x<closest.x||p.x==closest.x&&p.z<closest.z)){closestDistance=d;closest=p;}
            if(++leaderIndex==rows[leaderY].Count)
            {if(!hasNear){Move(Phase.Classify);return;}leaderIndex=0;sumX=0;sumZ=0;localCount=0;State=Phase.LeaderLocal;}
        }
        void LeaderLocalOne()
        {
            Int3 p=root.Cell(rows[leaderY][leaderIndex]);LeaderPointCandidates++;
            if(Distance(p,centerX,centerZ)<=16&&Distance(p,closest.x,closest.z)<=9){sumX+=p.x;sumZ+=p.z;localCount++;}
            if(++leaderIndex==rows[leaderY].Count)
            {if(localCount==0){Fail("InvalidLeaderLocal");return;}centerX=sumX/localCount;centerZ=sumZ/localCount;leaderIndex=0;State=Phase.LeaderKeep;}
        }
        void LeaderKeepOne()
        {
            int cellIndex=rows[leaderY][leaderIndex];Int3 p=root.Cell(cellIndex);LeaderPointCandidates++;
            if(Distance(p,centerX,centerZ)<=9)hard[cellIndex]=true;
            if(++leaderIndex==rows[leaderY].Count){LeaderRowsKept++;leaderY++;BeginLeaderRow();}
        }
        void ClassifyOne()
        {
            if(root.Cell(index).y<30){if(hard[index])hardCount++;else stickCount++;}
            if(++index==root.OccupiedCount)
            {
                if(hardCount+stickCount!=emittedCount){Fail("PartitionMismatch");return;}
                if(!Current()){Fail("StaleIdentity");return;}
                Published=new Result(root,hard,hardCount,stickCount,context,InteractionPolicy,provenance);hard=null;rows=null;State=Phase.Complete;
            }
        }
    }
}
