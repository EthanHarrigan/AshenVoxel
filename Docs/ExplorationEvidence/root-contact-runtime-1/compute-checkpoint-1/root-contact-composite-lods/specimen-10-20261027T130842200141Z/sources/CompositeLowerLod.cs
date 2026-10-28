using System;
using System.Collections.Generic;
using System.Linq;
using AshenVoxel.Core;
using AshenVoxel.Rendering;
using UnityEngine;

namespace CompositeRootCandidate
{
    // Isolated render-cache candidate. The caller retains authoritative 10 cm
    // root matter; this job never edits it or publishes a partial cache.
    public readonly struct RootCell
    {
        public readonly Int3 Position; public readonly uint Color;
        public RootCell(Int3 p,uint color){Position=p;Color=color;}
    }
    public readonly struct FixedSample
    {
        public readonly Int3 Cell;public readonly MaterialId Material;public readonly uint Color;
        public readonly int Rank,Index;
        public FixedSample(Int3 cell,MaterialId material,uint color,int rank,int index){Cell=cell;Material=material;Color=color;Rank=rank;Index=index;}
    }
    public sealed class CanonicalOrder
    {
        readonly Dictionary<Int3,int> ranks;
        public int Count=>ranks.Count;
        public CanonicalOrder(IEnumerable<Int3> orderedChunks)
        {ranks=new Dictionary<Int3,int>();foreach(var p in orderedChunks){if(ranks.Count>=8192||ranks.ContainsKey(p))throw new ArgumentException("Shared canonical rank cap/duplicate");ranks.Add(p,ranks.Count);}}
        public int Rank(Int3 p)=>ranks.TryGetValue(p,out int r)?r:-1;
        public IEnumerable<KeyValuePair<Int3,int>> Entries=>ranks.OrderBy(p=>p.Value);
    }
    public sealed class Template
    {
        readonly FixedSample[] samples;readonly Dictionary<Int3,uint> pinned;
        public readonly CanonicalOrder Order;
        public readonly int Factor,Cut,FirstUpper,Seed;
        public readonly string Identity;
        public int Count=>samples.Length;
        public FixedSample At(int i)=>samples[i];
        public bool Pinned(Int3 p,uint color)=>pinned.TryGetValue(p,out uint value)&&value==color;
        public int PinnedCount=>pinned.Count;
        public Template(int factor,int cut,int seed,string identity,CanonicalOrder order,IEnumerable<FixedSample> values,IEnumerable<RootCell> row29)
        {
            if(Array.IndexOf(new[]{1,2,4,16,64},factor)<0||cut!=factor*(32/factor+2)-4||string.IsNullOrEmpty(identity)||identity.Length>4096)throw new ArgumentException("Template identity/lattice");
            Factor=factor;Cut=cut;FirstUpper=(cut+4)/factor;Seed=seed;Identity=identity;Order=order??throw new ArgumentNullException(nameof(order));
            var bounded=new List<FixedSample>();foreach(var sample in values){if(bounded.Count>=65536)throw new ArgumentException("Shared fixed sample cap");bounded.Add(sample);}samples=bounded.ToArray();
            var unique=new HashSet<Int3>();foreach(var s in samples)if(!unique.Add(s.Cell)||s.Cell.x<0||s.Cell.x>=(470+factor-1)/factor||s.Cell.z<0||s.Cell.z>=(470+factor-1)/factor||s.Cell.y<0||s.Cell.y>FirstUpper||s.Material==MaterialId.Air||!MaterialCatalog.IsValid(s.Material)||!VoxelColor.IsValid(s.Color)||s.Color==0||s.Rank<0||s.Rank>=Order.Count||s.Index<0||s.Index>=4096)throw new ArgumentException("Fixed sample");
            pinned=new Dictionary<Int3,uint>();foreach(var c in row29){if(c.Position.y!=29||pinned.Count>=65536)throw new ArgumentException("Pinned row");pinned.Add(c.Position,c.Color);}
        }
    }
    public enum Phase { Fixed,Root,Faces,Mesh,Complete,Cancelled,Rejected }
    public sealed class Job
    {
        public const int Shift=1024,AnchorX=235,AnchorY=4,AnchorZ=235;
        public const int MaxCells=65536,MaxChunks=128,MaxRawFaces=65536;
        readonly Template template;readonly RootCell[] root;readonly string revision;
        readonly Dictionary<Int3,Winner> winners=new Dictionary<Int3,Winner>();
        readonly HashSet<Int3> rootPositions=new HashSet<Int3>(),rootChunks=new HashSet<Int3>();
        readonly List<Int3> occupied=new List<Int3>(),chunks=new List<Int3>();
        readonly VoxelWorld world;readonly RootBaseChunkMesher mesher=new RootBaseChunkMesher();
        readonly List<(Int3,Mesh)> output=new List<(Int3,Mesh)>();
        int at,direction,meshAt,pinnedCount;
        struct Winner {public MaterialId Material;public uint Color;public int Rank,Index;public Int3 Chunk;}
        public Phase State {get;private set;}=Phase.Fixed;public string Failure {get;private set;}
        public long FixedCandidates {get;private set;}public long RootCandidates {get;private set;}public long FaceCandidates {get;private set;}
        public int MeshChunkCalls {get;private set;}public int RawFaces {get;private set;}
        public int OccupiedCount=>occupied.Count;public int ChunkCount=>chunks.Count;
        public int RemainingFixed=>State==Phase.Fixed?template.Count-at:0;
        public int RemainingRoots=>State==Phase.Fixed?root.Length:State==Phase.Root?root.Length-at:0;
        public long RemainingFaces=>State==Phase.Fixed||State==Phase.Root?-1:State==Phase.Faces?(occupied.Count-at)*6L-direction:0;
        public int RemainingMeshChunks=>State==Phase.Complete?0:chunks.Count-meshAt;
        public bool Terminal=>State==Phase.Complete||State==Phase.Rejected||State==Phase.Cancelled;
        public VoxelWorld PublishedWorld=>State==Phase.Complete?world:null;
        public IReadOnlyList<(Int3 coordinate,Mesh mesh)> PublishedMeshes=>State==Phase.Complete?output:null;
        public Job(Template template,RootCell[] cells,string revision)
        {
            this.template=template??throw new ArgumentNullException(nameof(template));
            if(cells==null||cells.Length>MaxCells||string.IsNullOrEmpty(revision)||revision.Length>4096)throw new ArgumentException("Root cap/revision");
            root=(RootCell[])cells.Clone();this.revision=revision;
            world=new VoxelWorld((470+template.Factor-1)/template.Factor,Shift/template.Factor+template.FirstUpper+1,(470+template.Factor-1)/template.Factor,.1f*template.Factor,template.Seed);
        }
        public void Cancel(){if(!Terminal){State=Phase.Cancelled;Failure="Cancelled";}}
        void Fail(string why){State=Phase.Rejected;Failure=why;}
        static int CompareChunks(Int3 a,Int3 b){int c=a.z.CompareTo(b.z);if(c==0)c=a.y.CompareTo(b.y);if(c==0)c=a.x.CompareTo(b.x);return c;}
        static bool Earlier(Winner a,Winner b)
        {
            if(a.Rank>=0&&b.Rank<0)return true;if(a.Rank<0&&b.Rank>=0)return false;
            int c=a.Rank>=0?a.Rank.CompareTo(b.Rank):CompareChunks(a.Chunk,b.Chunk);
            return c<0||c==0&&a.Index<b.Index;
        }
        void Offer(Int3 c,Winner candidate)
        {
            if(winners.TryGetValue(c,out var old))
            {
                bool cw=candidate.Material==MaterialId.Wood,ow=old.Material==MaterialId.Wood;
                if(!(cw&&!ow||cw==ow&&Earlier(candidate,old)))return;
            }
            else
            {
                if(occupied.Count>=MaxCells){Fail("CoarseCellCap");return;}
                var chunk=new Int3(c.x>>4,c.y>>4,c.z>>4);
                if(!world.Chunks.ContainsKey(chunk)){if(chunks.Count>=MaxChunks){Fail("CoarseChunkCap");return;}chunks.Add(chunk);}
                occupied.Add(c);
            }
            winners[c]=candidate;world.Set(c,candidate.Material,candidate.Color);
        }
        static readonly Int3[] Directions={new Int3(1,0,0),new Int3(-1,0,0),new Int3(0,1,0),new Int3(0,-1,0),new Int3(0,0,1),new Int3(0,0,-1)};
        // Records and neighbor candidates are resumable. A mesh budget unit is
        // explicitly ONE atomic 16^3 chunk build (not one voxel or a time bound).
        public void Step(int recordBudget,int faceBudget,int meshChunkBudget,string currentRevision)
        {
            if(recordBudget<0||faceBudget<0||meshChunkBudget<0)throw new ArgumentOutOfRangeException();if(Terminal)return;
            if(currentRevision!=revision){Fail("StaleSource");return;}
            while(!Terminal)
            {
                if(State==Phase.Fixed)
                {
                    if(at==template.Count){at=0;State=Phase.Root;continue;}if(recordBudget--==0)return;
                    var s=template.At(at++);FixedCandidates++;Offer(new Int3(s.Cell.x,s.Cell.y+Shift/template.Factor,s.Cell.z),new Winner{Material=s.Material,Color=s.Color,Rank=s.Rank,Index=s.Index});
                }
                else if(State==Phase.Root)
                {
                    if(at==root.Length){if(pinnedCount!=template.PinnedCount){Fail("MissingPinnedRow");return;}at=0;State=Phase.Faces;continue;}if(recordBudget--==0)return;
                    var r=root[at++];RootCandidates++;var p=r.Position;
                    if(p.x<-128||p.x>127||p.z<-128||p.z>127||p.y<-64||p.y>29||r.Color==0||!VoxelColor.IsValid(r.Color)||!rootPositions.Add(p)){Fail("RootDomainOrDuplicate");return;}
                    // Existing builder uses a translated local VoxelWorld for its
                    // own chunk cap; preserve its certified payload cap instead
                    // of reinterpreting the authoritative chunk lattice here.
                    var source=new Int3(p.x+AnchorX,p.y+AnchorY,p.z+AnchorZ);
                    var chunk=new Int3(VoxelWorld.FloorDiv(source.x,16),VoxelWorld.FloorDiv(source.y,16),VoxelWorld.FloorDiv(source.z,16));rootChunks.Add(chunk);
                    if(p.y==29){if(!template.Pinned(p,r.Color)){Fail("ChangedPinnedRow");return;}pinnedCount++;continue;}
                    int lx=source.x-chunk.x*16,ly=source.y-chunk.y*16,lz=source.z-chunk.z*16;
                    var c=new Int3(VoxelWorld.FloorDiv(source.x,template.Factor),VoxelWorld.FloorDiv(source.y,template.Factor)+Shift/template.Factor,VoxelWorld.FloorDiv(source.z,template.Factor));
                    Offer(c,new Winner{Material=MaterialId.Wood,Color=r.Color,Rank=template.Order.Rank(chunk),Index=Chunk.Index(lx,ly,lz),Chunk=chunk});
                }
                else if(State==Phase.Faces)
                {
                    if(at==occupied.Count){State=Phase.Mesh;continue;}if(faceBudget--==0)return;
                    var p=occupied[at];var d=Directions[direction];FaceCandidates++;
                    if(p.y<Shift/template.Factor+template.FirstUpper&&world.Get(p)!=world.Get(p.x+d.x,p.y+d.y,p.z+d.z)&&++RawFaces>MaxRawFaces){Fail("RawFaceCap");return;}
                    if(++direction==6){direction=0;at++;}
                }
                else if(State==Phase.Mesh)
                {
                    if(meshAt==chunks.Count){State=Phase.Complete;return;}if(meshChunkBudget--==0)return;
                    var chunk=chunks[meshAt++];var mesh=new Mesh();MeshChunkCalls++;mesher.Build(world,chunk,mesh,Shift/template.Factor+template.FirstUpper);output.Add((chunk,mesh));
                }
            }
        }
    }
}
