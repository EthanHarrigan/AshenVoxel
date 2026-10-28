using System;
using System.Threading;
using AshenVoxel.Core;
using RootContactArrayMesher;

namespace RootContactManagedComposite
{
    public readonly struct RootCell {public readonly Int3 Position;public readonly uint Color;public RootCell(Int3 p,uint color){Position=p;Color=color;}}
    public readonly struct FixedSample
    {public readonly Int3 Cell;public readonly MaterialId Material;public readonly uint Color;public readonly int Rank,Index;public FixedSample(Int3 cell,MaterialId material,uint color,int rank,int index){Cell=cell;Material=material;Color=color;Rank=rank;Index=index;}}
    public readonly struct GridTransform
    {
        public readonly Int3 Anchor;public readonly int FineYShift,SourceSizeX,SourceSizeZ,Seed;
        public GridTransform(Int3 anchor,int fineYShift,int sourceSizeX,int sourceSizeZ,int seed)
        {if(anchor.y!=4||fineYShift<1024||fineYShift%1024!=0||fineYShift>3072||sourceSizeX<1||sourceSizeX>4096||sourceSizeZ<1||sourceSizeZ>4096||anchor.x<128||anchor.x+128>=sourceSizeX||anchor.z<128||anchor.z+128>=sourceSizeZ)throw new ArgumentException("Supported root/source lattice required");Anchor=anchor;FineYShift=fineYShift;SourceSizeX=sourceSizeX;SourceSizeZ=sourceSizeZ;Seed=seed;}
        public Float3 LocalMeterOffset=>new Float3(-Anchor.x*.1f,-(FineYShift+Anchor.y)*.1f,-Anchor.z*.1f);
    }
    // These bound declared array-element payload. CLR/Core object overhead,
    // caller-owned inputs, allocator behavior and unreachable GC backlog are separate.
    public sealed class Limits
    {
        public readonly long MaxTemplateBytes,MaxRootBytes,MaxScratchBytes,MaxOutputBytes,MaxTotalBytes;
        public Limits(long maxTemplateBytes=4*1024*1024,long maxRootBytes=65536*16+8192,long maxScratchBytes=12*1024*1024,long maxOutputBytes=8*1024*1024,long maxTotalBytes=24*1024*1024)
        {if(maxTemplateBytes<0||maxRootBytes<0||maxScratchBytes<0||maxOutputBytes<0||maxTotalBytes<0)throw new ArgumentOutOfRangeException();MaxTemplateBytes=maxTemplateBytes;MaxRootBytes=maxRootBytes;MaxScratchBytes=maxScratchBytes;MaxOutputBytes=maxOutputBytes;MaxTotalBytes=maxTotalBytes;}
    }
    static class Validate
    {
        public static void Current(Func<bool> current,CancellationToken token){if(current==null)throw new ArgumentNullException(nameof(current));token.ThrowIfCancellationRequested();if(!current())throw new InvalidOperationException("StaleSource");token.ThrowIfCancellationRequested();}
        public static int Position(Int3 a,Int3 b){int c=a.z.CompareTo(b.z);if(c==0)c=a.y.CompareTo(b.y);if(c==0)c=a.x.CompareTo(b.x);return c;}
        public static long Identity(string s){if(string.IsNullOrEmpty(s)||s.Length>4096)throw new ArgumentException("Bounded complete caller identity required");return 2L*s.Length;}
    }
    public sealed class CanonicalSource
    {
        readonly struct RankEntry{public readonly Int3 Chunk;public readonly int Rank;public RankEntry(Int3 c,int r){Chunk=c;Rank=r;}}
        readonly RankEntry[] ranks;readonly RootCell[] pins;
        public readonly GridTransform Transform;public readonly string Identity;public readonly long DeclaredElementBytes;
        public int RankCount=>ranks.Length;public int PinnedCount=>pins.Length;
        public RootCell PinnedAt(int i)=>pins[i];
        public CanonicalSource(Int3[] orderedChunks,RootCell[] pinnedRow,GridTransform transform,string identity,Limits limits,Func<bool> current,CancellationToken token=default)
        {
            Validate.Current(current,token);if(limits==null||orderedChunks==null||pinnedRow==null||orderedChunks.Length>8192||pinnedRow.Length>65536)throw new ArgumentException("Canonical input caps");
            DeclaredElementBytes=16L*(orderedChunks.Length+pinnedRow.Length)+Validate.Identity(identity);if(DeclaredElementBytes>limits.MaxTemplateBytes)throw new ArgumentException("TemplateBytes");
            Transform=new GridTransform(transform.Anchor,transform.FineYShift,transform.SourceSizeX,transform.SourceSizeZ,transform.Seed);Identity=identity;ranks=new RankEntry[orderedChunks.Length];for(int i=0;i<ranks.Length;i++)ranks[i]=new RankEntry(orderedChunks[i],i);Array.Sort(ranks,(a,b)=>Validate.Position(a.Chunk,b.Chunk));
            for(int i=1;i<ranks.Length;i++)if(ranks[i].Chunk.Equals(ranks[i-1].Chunk))throw new ArgumentException("Duplicate canonical rank");
            pins=(RootCell[])pinnedRow.Clone();Array.Sort(pins,(a,b)=>Validate.Position(a.Position,b.Position));for(int i=0;i<pins.Length;i++)if(pins[i].Position.y!=29||pins[i].Position.x<-128||pins[i].Position.x>127||pins[i].Position.z<-128||pins[i].Position.z>127||pins[i].Color>>24!=255||i>0&&pins[i].Position.Equals(pins[i-1].Position))throw new ArgumentException("Invalid pinned row");Validate.Current(current,token);
        }
        internal int Rank(Int3 c){int lo=0,hi=ranks.Length-1;while(lo<=hi){int mid=(lo+hi)/2,n=Validate.Position(c,ranks[mid].Chunk);if(n==0)return ranks[mid].Rank;if(n<0)hi=mid-1;else lo=mid+1;}return -1;}
        internal bool Pin(RootCell c){int lo=0,hi=pins.Length-1;while(lo<=hi){int mid=(lo+hi)/2,n=Validate.Position(c.Position,pins[mid].Position);if(n==0)return c.Color==pins[mid].Color;if(n<0)hi=mid-1;else lo=mid+1;}return false;}
    }
    public sealed class LodTemplate
    {
        readonly FixedSample[] samples;public readonly CanonicalSource Source;public readonly int Factor,Cut,FirstUpper;public readonly string Identity;public readonly long DeclaredElementBytes;
        public int Count=>samples.Length;public FixedSample At(int i)=>samples[i];
        public LodTemplate(CanonicalSource source,int factor,int cut,FixedSample[] fixedSamples,string identity,Limits limits,Func<bool> current,CancellationToken token=default)
        {
            Validate.Current(current,token);if(source==null||limits==null||fixedSamples==null||fixedSamples.Length>65536||Array.IndexOf(new[]{1,2,4,16,64},factor)<0||cut!=factor*(32/factor+2)-4)throw new ArgumentException("Template count/lattice");
            DeclaredElementBytes=32L*fixedSamples.Length+Validate.Identity(identity);if(DeclaredElementBytes+source.DeclaredElementBytes>limits.MaxTemplateBytes)throw new ArgumentException("TemplateBytes");
            Source=source;Factor=factor;Cut=cut;FirstUpper=(cut+4)/factor;Identity=identity;samples=(FixedSample[])fixedSamples.Clone();Array.Sort(samples,(a,b)=>Validate.Position(a.Cell,b.Cell));
            for(int i=0;i<samples.Length;i++){var s=samples[i];if(s.Cell.x<0||s.Cell.z<0||s.Cell.x>=(source.Transform.SourceSizeX+factor-1)/factor||s.Cell.z>=(source.Transform.SourceSizeZ+factor-1)/factor||s.Cell.y<0||s.Cell.y>FirstUpper||s.Material==MaterialId.Air||!MaterialCatalog.IsValid(s.Material)||s.Color>>24!=255||s.Rank<0||s.Rank>=source.RankCount||s.Index<0||s.Index>=4096||i>0&&s.Cell.Equals(samples[i-1].Cell))throw new ArgumentException("Invalid fixed sample");}Validate.Current(current,token);
        }
    }
    public readonly struct MeshPart {public readonly Int3 Coordinate;public readonly MeshData Data;internal MeshPart(Int3 c,MeshData d){Coordinate=c;Data=d;}}
    public sealed class LodOutput
    {
        readonly MeshPart[] parts;public readonly int Factor,Cut;public readonly string Identity;public readonly GridTransform Transform;public readonly long RetainedMeshBytes;
        public int Count=>parts.Length;public MeshPart At(int i)=>parts[i];
        internal LodOutput(LodTemplate t,MeshPart[] source,int count,string identity){Factor=t.Factor;Cut=t.Cut;Transform=t.Source.Transform;Identity=identity;parts=new MeshPart[count];Array.Copy(source,parts,count);foreach(var part in parts)RetainedMeshBytes+=part.Data.RetainedBufferBytes;}
    }
    public sealed class CompositeOutput
    {
        readonly LodOutput[] lods;public readonly string Identity;public readonly long RetainedMeshBytes,DeclaredOutputElementBytes;
        public int Count=>lods.Length;public LodOutput At(int i)=>lods[i];
        internal CompositeOutput(string identity,LodOutput[] source,long bytes){Identity=identity;lods=(LodOutput[])source.Clone();DeclaredOutputElementBytes=bytes;foreach(var lod in lods)RetainedMeshBytes+=lod.RetainedMeshBytes;}
    }
    public enum Phase{Fixed,Root,Faces,Mesh,Complete,Cancelled,Stale,Rejected,Failed}
    public sealed class CompositeJob
    {
        const int MaxCells=65536,MaxChunks=128,MaxRawFaces=65536;
        sealed class CacheChunk{public readonly Int3 Coordinate;public readonly int[] Winners=new int[4096];public int Faces;public CacheChunk(Int3 c){Coordinate=c;}}
        sealed class SeenChunk{public readonly Int3 Coordinate;public readonly ulong[] Bits=new ulong[64];public SeenChunk(Int3 c){Coordinate=c;}}
        readonly LodTemplate[] templates;readonly RootCell[] root;readonly Limits limits;readonly Func<bool> current;readonly CancellationToken token;readonly string identity;
        readonly Int3[] occupied;readonly CacheChunk[] chunks=new CacheChunk[128];readonly SeenChunk[] seen=new SeenChunk[128];readonly MeshPart[] parts=new MeshPart[128];readonly LodOutput[] outputs=new LodOutput[5];
        VoxelWorld world;int lod,at,direction,occupiedCount,chunkCount,seenCount,meshAt,pins,rawFaces;long outputBytes,meshBytes,extraScratch;
        public Phase State{get;private set;}=Phase.Fixed;public string Failure{get;private set;}="";public CompositeOutput Published{get;private set;}
        public readonly long TemplateElementBytes,RootElementBytes;
        public long PeakDeclaredScratchBytes{get;private set;}public long PeakDeclaredTotalBytes{get;private set;}
        public long FixedCandidates{get;private set;}public long RootCandidates{get;private set;}public long FaceCandidates{get;private set;}public long HaloCellsRead{get;private set;}public int MeshChunkCalls{get;private set;}public int CompletedLods=>lod;
        public bool Terminal=>State>=Phase.Complete;public int RemainingFixed=>State==Phase.Fixed?T.Count-at:0;public int RemainingRoots=>State==Phase.Fixed?root.Length:State==Phase.Root?root.Length-at:0;public long RemainingFaces=>State==Phase.Faces?(occupiedCount-at)*6L-direction:State<Phase.Faces?-1:0;public int RemainingMeshChunks=>State==Phase.Mesh?chunkCount-meshAt:0;
        public long RetainedMeshBytes=>meshBytes;public int ActiveCoarseWorlds=>world==null?0:1;
        LodTemplate T=>templates[lod];GridTransform Transform=>templates[0].Source.Transform;
        long Scratch=>occupied.Length*12L+128L*(8+8+24)+40+chunkCount*4096L*9+seenCount*64L*8+extraScratch;
        public CompositeJob(LodTemplate[] fiveTemplates,RootCell[] rootArray,string identity,Limits limits,Func<bool> current,CancellationToken token=default)
        {
            Validate.Current(current,token);if(limits==null||fiveTemplates==null||fiveTemplates.Length!=5||rootArray==null||rootArray.Length>MaxCells)throw new ArgumentException("Job input caps");
            this.limits=limits;this.current=current;this.token=token;this.identity=identity;RootElementBytes=rootArray.Length*16L+Validate.Identity(identity);if(RootElementBytes>limits.MaxRootBytes)throw new ArgumentException("RootBytes");
            int[] fs={1,2,4,16,64};int maxFixed=0;for(int i=0;i<5;i++){var t=fiveTemplates[i];if(t==null||t.Factor!=fs[i]||!ReferenceEquals(t.Source,fiveTemplates[0].Source))throw new ArgumentException("Complete ordered canonical templates required");TemplateElementBytes+=t.DeclaredElementBytes;maxFixed=Math.Max(maxFixed,t.Count);}TemplateElementBytes+=fiveTemplates[0].Source.DeclaredElementBytes+40;
            int count=Math.Min(MaxCells,rootArray.Length+maxFixed);long baseScratch=count*12L+128L*40+40;
            if(TemplateElementBytes>limits.MaxTemplateBytes||baseScratch>limits.MaxScratchBytes||TemplateElementBytes+RootElementBytes+baseScratch>limits.MaxTotalBytes)throw new ArgumentException("Aggregate admission");
            templates=(LodTemplate[])fiveTemplates.Clone();root=(RootCell[])rootArray.Clone();occupied=new Int3[count];Validate.Current(current,token);Init();
        }
        bool Guard()
        {
            if(Terminal)return false;try{if(token.IsCancellationRequested){Stop(Phase.Cancelled,"Cancelled");return false;}bool good=current();if(token.IsCancellationRequested){Stop(Phase.Cancelled,"Cancelled");return false;}if(!good){Stop(Phase.Stale,"StaleSource");return false;}return true;}catch(Exception){Stop(Phase.Failed,"CurrentPredicate");return false;}
        }
        void Stop(Phase state,string why){State=state;Failure=why;Published=null;ReleaseWorld();Array.Clear(outputs,0,outputs.Length);outputBytes=meshBytes=0;}
        void ReleaseWorld(){world=null;Array.Clear(chunks,0,chunks.Length);Array.Clear(seen,0,seen.Length);Array.Clear(parts,0,parts.Length);chunkCount=seenCount=0;extraScratch=0;}
        bool Admit(long scratchExtra=0,long outputExtra=0)
        {
            long scratch=Scratch+scratchExtra,total=TemplateElementBytes+RootElementBytes+scratch+outputBytes+outputExtra;PeakDeclaredScratchBytes=Math.Max(PeakDeclaredScratchBytes,scratch);PeakDeclaredTotalBytes=Math.Max(PeakDeclaredTotalBytes,total);
            if(scratch>limits.MaxScratchBytes||outputBytes+outputExtra>limits.MaxOutputBytes||total>limits.MaxTotalBytes){Stop(Phase.Rejected,"ByteCapacity");return false;}return true;
        }
        void Init(){if(!Guard()||!Admit())return;int f=T.Factor;world=new VoxelWorld((Transform.SourceSizeX+f-1)/f,Transform.FineYShift/f+T.FirstUpper+1,(Transform.SourceSizeZ+f-1)/f,.1f*f,Transform.Seed);at=direction=occupiedCount=meshAt=pins=rawFaces=0;State=Phase.Fixed;Guard();}
        public void Cancel(){if(!Terminal)Stop(Phase.Cancelled,"Cancelled");}
        int Find(Int3 c){for(int i=0;i<chunkCount;i++)if(chunks[i].Coordinate.Equals(c))return i;return -1;}
        void Winner(int value,out MaterialId material,out uint color,out int rank,out int index,out Int3 chunk)
        {
            if(value>0){var s=T.At(value-1);material=s.Material;color=s.Color;rank=s.Rank;index=s.Index;chunk=default;return;}
            var r=root[-value-1];var p=new Int3(r.Position.x+Transform.Anchor.x,r.Position.y+Transform.Anchor.y,r.Position.z+Transform.Anchor.z);chunk=new Int3(VoxelWorld.FloorDiv(p.x,16),VoxelWorld.FloorDiv(p.y,16),VoxelWorld.FloorDiv(p.z,16));material=MaterialId.Wood;color=r.Color;rank=T.Source.Rank(chunk);index=Chunk.Index(p.x-chunk.x*16,p.y-chunk.y*16,p.z-chunk.z*16);
        }
        bool Earlier(int a,int b)
        {
            Winner(a,out var am,out _,out int ar,out int ai,out var ac);Winner(b,out var bm,out _,out int br,out int bi,out var bc);bool aw=am==MaterialId.Wood,bw=bm==MaterialId.Wood;if(aw!=bw)return aw;
            if(ar>=0&&br<0)return true;if(ar<0&&br>=0)return false;int c=ar>=0?ar.CompareTo(br):Validate.Position(ac,bc);return c<0||c==0&&ai<bi;
        }
        void Offer(Int3 p,int winner)
        {
            var c=new Int3(p.x>>4,p.y>>4,p.z>>4);int ci=Find(c);if(ci<0){if(chunkCount==MaxChunks){Stop(Phase.Rejected,"CoarseChunkCap");return;}if(!Admit(4096L*9))return;ci=chunkCount++;chunks[ci]=new CacheChunk(c);}
            int index=Chunk.Index(p.x&15,p.y&15,p.z&15),old=chunks[ci].Winners[index];if(old!=0&&!Earlier(winner,old))return;
            if(old==0){if(occupiedCount==occupied.Length){Stop(Phase.Rejected,"CoarseCellCap");return;}occupied[occupiedCount++]=p;}
            Winner(winner,out var m,out uint color,out _,out _,out _);chunks[ci].Winners[index]=winner;if(!world.Set(p,m,color)&&world.Get(p)!=m)Stop(Phase.Rejected,"CoarseDomain");
        }
        bool RootRecord(int index)
        {
            var r=root[index];var p=r.Position;if(p.x<-128||p.x>127||p.z<-128||p.z>127||p.y<-64||p.y>29||r.Color>>24!=255){Stop(Phase.Rejected,"RootDomain");return false;}
            var source=new Int3(p.x+Transform.Anchor.x,p.y+Transform.Anchor.y,p.z+Transform.Anchor.z);var c=new Int3(VoxelWorld.FloorDiv(source.x,16),VoxelWorld.FloorDiv(source.y,16),VoxelWorld.FloorDiv(source.z,16));int si=0;while(si<seenCount&&!seen[si].Coordinate.Equals(c))si++;
            if(si==seenCount){if(seenCount==MaxChunks){Stop(Phase.Rejected,"RootChunkCap");return false;}if(!Admit(512))return false;seen[seenCount++]=new SeenChunk(c);}
            int bit=Chunk.Index(source.x-c.x*16,source.y-c.y*16,source.z-c.z*16);ulong mask=1UL<<(bit&63);if((seen[si].Bits[bit>>6]&mask)!=0){Stop(Phase.Rejected,"DuplicateRoot");return false;}seen[si].Bits[bit>>6]|=mask;
            if(p.y==29){if(!T.Source.Pin(r)){Stop(Phase.Rejected,"ChangedPinnedRow");return false;}pins++;return true;}
            Offer(new Int3(VoxelWorld.FloorDiv(source.x,T.Factor),VoxelWorld.FloorDiv(source.y,T.Factor)+Transform.FineYShift/T.Factor,VoxelWorld.FloorDiv(source.z,T.Factor)),-index-1);return !Terminal;
        }
        static readonly Int3[] Directions={new Int3(1,0,0),new Int3(-1,0,0),new Int3(0,1,0),new Int3(0,-1,0),new Int3(0,0,1),new Int3(0,0,-1)};
        void MeshChunk()
        {
            if(!Guard())return;var chunk=chunks[meshAt];int quads=chunk.Faces;
            // Both caller halo arrays and ChunkInput's private copy coexist;
            // working+compacted meshes can coexist, plus mask arrays and p[3].
            extraScratch=2*ChunkInput.HaloCells*5L+2*quads*136L+2060;if(!Admit())return;
            var materials=new MaterialId[ChunkInput.HaloCells];var colors=new uint[materials.Length];int bx=chunk.Coordinate.x*16,by=chunk.Coordinate.y*16,bz=chunk.Coordinate.z*16;
            if(!Guard())return;for(int z=-1;z<=16;z++)for(int y=-1;y<=16;y++)for(int x=-1;x<=16;x++){int i=(x+1)+18*((y+1)+18*(z+1));materials[i]=world.Get(bx+x,by+y,bz+z);colors[i]=materials[i]==MaterialId.Air?0:world.GetColor(bx+x,by+y,bz+z);HaloCellsRead++;}if(!Guard())return;
            var input=new ChunkInput(chunk.Coordinate,world.VoxelSize,Transform.FineYShift/T.Factor+T.FirstUpper,identity,materials,colors);if(!Guard())return;
            var attempt=ArrayRootMesher.Build(input,new MeshCapacity(quads,136L*quads),Guard,token);MeshChunkCalls++;if(!Guard())return;
            if(attempt.Status!=BuildStatus.Complete){Stop(attempt.Status==BuildStatus.Cancelled?Phase.Cancelled:attempt.Status==BuildStatus.Stale?Phase.Stale:attempt.Status==BuildStatus.CapacityRejected?Phase.Rejected:Phase.Failed,"ArrayMesher:"+attempt.Status);return;}
            long bytes=attempt.Output.RetainedBufferBytes;extraScratch=0;if(!Admit(0,bytes+24))return;parts[meshAt++]=new MeshPart(chunk.Coordinate,attempt.Output);outputBytes+=bytes+24;meshBytes+=bytes;Guard();
        }
        public void Step(int recordBudget,int faceBudget,int chunkBudget)
        {
            if(recordBudget<0||faceBudget<0||chunkBudget<0)throw new ArgumentOutOfRangeException();if(!Guard())return;
            try
            {
                while(!Terminal)
                {
                    if(!Guard())return;
                    if(State==Phase.Fixed){if(at==T.Count){at=0;State=Phase.Root;continue;}if(recordBudget--==0)return;var s=T.At(at);FixedCandidates++;Offer(new Int3(s.Cell.x,s.Cell.y+Transform.FineYShift/T.Factor,s.Cell.z),++at);}
                    else if(State==Phase.Root){if(at==root.Length){if(pins!=T.Source.PinnedCount){Stop(Phase.Rejected,"MissingPinnedRow");return;}at=0;State=Phase.Faces;continue;}if(recordBudget--==0)return;RootCandidates++;RootRecord(at++);}
                    else if(State==Phase.Faces)
                    {
                        if(at==occupiedCount){State=Phase.Mesh;continue;}if(faceBudget--==0)return;var p=occupied[at];var d=Directions[direction];FaceCandidates++;
                        if(p.y<Transform.FineYShift/T.Factor+T.FirstUpper&&world.Get(p)!=world.Get(p.x+d.x,p.y+d.y,p.z+d.z)){if(++rawFaces>MaxRawFaces){Stop(Phase.Rejected,"RawFaceCap");return;}chunks[Find(new Int3(p.x>>4,p.y>>4,p.z>>4))].Faces++;}
                        if(++direction==6){direction=0;at++;}
                    }
                    else if(State==Phase.Mesh)
                    {
                        if(meshAt==chunkCount)
                        {
                            if(!Guard()||!Admit(24L*chunkCount))return;var result=new LodOutput(T,parts,chunkCount,identity);if(!Guard())return;outputs[lod++]=result;ReleaseWorld();
                            if(lod==5){if(!Admit(40,40)||!Guard())return;var done=new CompositeOutput(identity,outputs,outputBytes+40);if(!Guard())return;Published=done;if(!Guard())return;State=Phase.Complete;return;}Init();continue;
                        }
                        if(chunkBudget--==0)return;MeshChunk();
                    }
                }
            }
            catch(Exception e){Stop(Phase.Failed,e.GetType().Name);}
        }
    }
}
