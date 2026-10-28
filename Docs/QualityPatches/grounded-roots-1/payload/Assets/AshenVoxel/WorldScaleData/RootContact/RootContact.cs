using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using AshenVoxel.Core;

namespace RootContactPrototype
{
    // Prototype policy, not a production generator version. Local units are exact 10cm cells.
    public static class Policy
    {
        public const int GuardY=30, MaxDeformation=20, MinBelowSeam=3;
        public const int MaxOccupied=65536, MaxRawFaces=65536, MaxColumns=8192, MaxChunks=128, MaxIdentityLength=4096;
        public const int MinCoordinate=-128, MaxCoordinate=127, MinSourceY=-64;
        public const string Identity="root-contact-cpu-v2;cell=.1;deform-each-overlap=20;clearance=3;occupied=65536;rawfaces=65536;chunks=128;guard=30;SATeps=1e-9;connected-to-pinned-seam";
        public static bool Finite(double d)=>!double.IsNaN(d)&&!double.IsInfinity(d);
        public static string Hash(byte[] data)
        {
            using var sha=SHA256.Create();byte[] digest=sha.ComputeHash(data);var text=new System.Text.StringBuilder(digest.Length*2);
            foreach(byte b in digest)text.Append(b.ToString("x2",CultureInfo.InvariantCulture));return text.ToString();
        }
    }
    public sealed class Column
    {
        public readonly int X,Z;
        readonly short[] ys; readonly uint[] tints;
        public int Count=>ys.Length;
        public int Y(int i)=>ys[i]; public uint Tint(int i)=>tints[i];
        public Column(int x,int z,short[] y,uint[] tint)
        {
            if(x<Policy.MinCoordinate||x>Policy.MaxCoordinate||z<Policy.MinCoordinate||z>Policy.MaxCoordinate||y==null||tint==null||y.Length==0||y.Length!=tint.Length||y.Length>96)throw new ArgumentException("Invalid bounded column");
            X=x;Z=z;ys=(short[])y.Clone();tints=(uint[])tint.Clone();
            for(int i=0;i<Count;i++)if(ys[i]<Policy.MinSourceY||ys[i]>Policy.GuardY||(i>0&&ys[i]<=ys[i-1])||tints[i]>>24!=255)throw new ArgumentException("Invalid source cell or pigment");
        }
    }
    public sealed class Template
    {
        readonly Column[] columns;
        public readonly string Identity, SourceIdentity;
        public readonly int DeformSeam, MinX,MinY,MinZ,MaxX,MaxZ,SourceCells;
        public int Count=>columns.Length; public Column GetColumn(int i)=>columns[i];
        public Template(Column[] input,int deformSeam,string sourceIdentity)
        {
            if(input==null||input.Length==0||input.Length>Policy.MaxColumns||(deformSeam!=29&&deformSeam!=30)||string.IsNullOrEmpty(sourceIdentity)||sourceIdentity.Length>Policy.MaxIdentityLength)throw new ArgumentException("Invalid template");
            DeformSeam=deformSeam;SourceIdentity=sourceIdentity;columns=(Column[])input.Clone();
            var seen=new HashSet<(int,int)>();int xmin=128,zmin=128,ymin=31,xmax=-129,zmax=-129,n=0;
            using var stream=new MemoryStream();using var writer=new BinaryWriter(stream);
            writer.Write(sourceIdentity);writer.Write(deformSeam);
            foreach(Column c in columns)
            {
                if(c==null||!seen.Add((c.X,c.Z)))throw new ArgumentException("Duplicate/null column");
                xmin=Math.Min(xmin,c.X);xmax=Math.Max(xmax,c.X);zmin=Math.Min(zmin,c.Z);zmax=Math.Max(zmax,c.Z);ymin=Math.Min(ymin,c.Y(0));
                n+=c.Count;if(n>Policy.MaxOccupied)throw new ArgumentException("Source cell cap");
                // v2 must explicitly carry row29 underneath every canonical row30 context cell.
                bool guard=false,pinned=false;writer.Write(c.X);writer.Write(c.Z);writer.Write(c.Count);
                for(int j=0;j<c.Count;j++){guard|=c.Y(j)==30;pinned|=c.Y(j)==29;writer.Write(c.Y(j));writer.Write(c.Tint(j));}
                if(deformSeam==29&&guard&&!pinned)throw new ArgumentException("Missing canonical row29 beneath guard");
            }
            MinX=xmin;MaxX=xmax;MinZ=zmin;MaxZ=zmax;MinY=ymin;SourceCells=n;writer.Flush();Identity=Policy.Hash(stream.ToArray());
        }
    }
    // Provider identity must be an immutable content/revision identity, never reused after mutation.
    // Bounds are inclusive GLOBAL 10cm cell coordinates. Top is the bare authoritative ground top.
    public interface ITerrain
    {
        string SourceIdentity {get;}
        long MinX {get;} long MaxX {get;} long MinZ {get;} long MaxZ {get;}
        bool TryGetBareTop(long globalX,long globalZ,out double meters);
    }
    public readonly struct Pose
    {
        public readonly double X,Y,Z,YawDegrees;
        public Pose(double x,double y,double z,double yaw)
        {
            if(!Policy.Finite(x)||!Policy.Finite(y)||!Policy.Finite(z)||!Policy.Finite(yaw)||Math.Abs(x)>1e9||Math.Abs(z)>1e9||Math.Abs(y)>1e6||Math.Abs(yaw)>360000)throw new ArgumentException("Finite bounded unit-scale pose required");
            X=x;Y=y;Z=z;YawDegrees=yaw;
        }
        public override string ToString()=>string.Join(",",X.ToString("R",CultureInfo.InvariantCulture),Y.ToString("R",CultureInfo.InvariantCulture),Z.ToString("R",CultureInfo.InvariantCulture),YawDegrees.ToString("R",CultureInfo.InvariantCulture));
    }
    public readonly struct Budget
    {
        public readonly int Queries,Cells,Faces;
        public Budget(int queries,int cells,int faces){if(queries<0||cells<0||faces<0)throw new ArgumentException("Negative budget");Queries=queries;Cells=cells;Faces=faces;}
    }
    public enum Phase { Queries,Cells,Connectivity,Faces,Complete,Cancelled,Failed }
    public sealed class Result
    {
        readonly VoxelWorld world; readonly List<Int3> occupied;
        public readonly Int3 Origin; public readonly Pose Pose; public readonly string Provenance; public readonly int RawFaces;
        public readonly string SourceIdentity,TemplateIdentity,PolicyIdentity;
        internal Result(VoxelWorld w,List<Int3> cells,Int3 origin,Pose pose,string provenance,int faces,string sourceIdentity,string templateIdentity,int seam)
        {world=w;occupied=cells;Origin=origin;Pose=pose;Provenance=provenance;RawFaces=faces;SourceIdentity=sourceIdentity;TemplateIdentity=templateIdentity;PolicyIdentity=Policy.Identity+";seam="+seam;}
        public int OccupiedCount=>occupied.Count;
        public int AllocatedChunks=>world.Chunks.Count;
        public Int3 Cell(int i)=>occupied[i];
        public MaterialId NeighborMaterial(Int3 local)=>world.Get(local.x-Origin.x,local.y-Origin.y,local.z-Origin.z);
        public uint Color(Int3 local)=>world.GetColor(local.x-Origin.x,local.y-Origin.y,local.z-Origin.z);
        // Both renderer emission and gameplay/collision ownership MUST apply this domain.
        // Row30 exists only for neighbor/AO context; it is owned by the unchanged upper tree.
        public bool OwnsGameplayCell(Int3 local)=>local.y<Policy.GuardY&&NeighborMaterial(local)!=MaterialId.Air;
    }
    public sealed class Builder
    {
        readonly Template template; readonly ITerrain terrain; readonly Pose pose;
        readonly string sourceIdentity,provenance;readonly long minX,maxX,minZ,maxZ;
        readonly int[] targets;readonly double co,si,radius;readonly Int3 origin;
        VoxelWorld world;List<Int3> occupied;
        HashSet<Int3> visited=new HashSet<Int3>();List<Int3> traversal=new List<Int3>();
        int seedIndex,traversalIndex,traversalDirection;
        int queryColumn,cellColumn,sourceRow,nextY,endY,faceCell,faceDirection,finishedRows;bool queryActive,rowActive;
        long qx,qz,qxmin,qxmax,qzmax;double centerX,centerZ,minimum;
        int queryHits;uint rowTint;
        public Phase State {get;private set;}=Phase.Queries;
        public string Failure {get;private set;}="";
        public Result Published {get;private set;}
        public long QueryCandidates {get;private set;} public long SourceQueries {get;private set;}
        public long CellCandidates {get;private set;} public long FaceCandidates {get;private set;}
        // Connectivity uses the same Step Faces budget, but keeps a separate observable counter.
        public long ConnectivityCandidates {get;private set;}
        public int ConnectedCells {get;private set;}
        public int PendingTraversalCells=>traversal==null?0:traversal.Count-traversalIndex;
        public int RawFaces {get;private set;}
        public int PeakAllocatedChunks {get;private set;}
        public int PeakOccupiedCells {get;private set;}
        public int PendingColumns=>template.Count-queryColumn;
        public int PendingSourceRows=>template.SourceCells-finishedRows;
        public int PendingMappedCellsInCurrentRow=>rowActive?endY-nextY:0;
        public int AllocatedChunks=>world?.Chunks.Count??Published?.AllocatedChunks??0;
        public int OccupiedCount=>occupied?.Count??Published?.OccupiedCount??0;
        public int ProcessedFaceCells=>faceCell;
        public bool Terminal=>State==Phase.Complete||State==Phase.Cancelled||State==Phase.Failed;
        static readonly Int3[] directions={new Int3(1,0,0),new Int3(-1,0,0),new Int3(0,1,0),new Int3(0,-1,0),new Int3(0,0,1),new Int3(0,0,-1)};
        public Builder(Template input,ITerrain provider,Pose originalPose)
        {
            template=input??throw new ArgumentNullException(nameof(input));terrain=provider??throw new ArgumentNullException(nameof(provider));pose=originalPose;
            sourceIdentity=provider.SourceIdentity;if(string.IsNullOrEmpty(sourceIdentity)||sourceIdentity.Length>Policy.MaxIdentityLength)throw new ArgumentException("Bounded source identity required (or use a content hash)");
            minX=provider.MinX;maxX=provider.MaxX;minZ=provider.MinZ;maxZ=provider.MaxZ;
            if(minX>maxX||minZ>maxZ||minX< -10000001000L||maxX>10000001000L||minZ< -10000001000L||maxZ>10000001000L)throw new ArgumentException("Finite source bounds required");
            targets=new int[template.Count];double angle=pose.YawDegrees*Math.PI/180;co=Math.Cos(angle);si=Math.Sin(angle);radius=(Math.Abs(co)+Math.Abs(si))*.5;
            origin=new Int3(template.MinX,template.MinY-Policy.MaxDeformation,template.MinZ);
            world=new VoxelWorld(template.MaxX-origin.x+1,Policy.GuardY-origin.y+1,template.MaxZ-origin.z+1,.1f,714103);
            occupied=new List<Int3>();
            provenance=Policy.Hash(System.Text.Encoding.UTF8.GetBytes(Policy.Identity+";seam="+template.DeformSeam+";template="+template.Identity+";source="+sourceIdentity+";pose="+pose));
        }
        public void Cancel(){if(!Terminal){State=Phase.Cancelled;Failure="Cancelled";Discard();}}
        void Discard(){world=null;occupied=null;visited=null;traversal=null;Published=null;}
        void Fail(string reason){State=Phase.Failed;Failure=reason;Discard();}
        bool Current()=>terrain.SourceIdentity==sourceIdentity&&terrain.MinX==minX&&terrain.MaxX==maxX&&terrain.MinZ==minZ&&terrain.MaxZ==maxZ;
        public void Step(Budget budget)
        {
            if(Terminal)return;
            try
            {
                if(!Current()){Fail("StaleSource");return;}
                int queries=budget.Queries,cells=budget.Cells,faces=budget.Faces;
                while(State==Phase.Queries&&queries>0){queries--;QueryCandidates++;QueryOne();}
                while(State==Phase.Cells&&cells>0){cells--;CellCandidates++;CellOne();}
                while((State==Phase.Connectivity||State==Phase.Faces&&faceCell<occupied.Count)&&faces>0)
                {
                    faces--;
                    if(State==Phase.Connectivity){ConnectivityCandidates++;ConnectivityOne();}
                    else {FaceCandidates++;FaceOne();}
                }
                if(State==Phase.Faces&&faceCell==occupied.Count)
                {
                    if(!Current()){Fail("StaleSource");return;}
                    Published=new Result(world,occupied,origin,pose,provenance,RawFaces,sourceIdentity,template.Identity,template.DeformSeam);State=Phase.Complete;
                    world=null;occupied=null;
                }
                else if(!Terminal&&!Current())Fail("StaleSource");
            }
            catch(Exception e){Fail("ProviderOrBuildException:"+e.GetType().Name);}
        }
        void QueryOne()
        {
            Column c=template.GetColumn(queryColumn);
            if(!queryActive)
            {
                if(c.Y(0)>=template.DeformSeam){targets[queryColumn]=c.Y(0);FinishQueryColumn();return;}
                centerX=pose.X*10+co*(c.X+.5)+si*(c.Z+.5);centerZ=pose.Z*10-si*(c.X+.5)+co*(c.Z+.5);
                qxmin=(long)Math.Floor(centerX-radius);qxmax=(long)Math.Ceiling(centerX+radius)-1;
                qz=(long)Math.Floor(centerZ-radius);qzmax=(long)Math.Ceiling(centerZ+radius)-1;qx=qxmin;
                minimum=double.PositiveInfinity;queryHits=0;queryActive=true;
            }
            double dx=qx+.5-centerX,dz=qz+.5-centerZ,limit=.5+radius-1e-9;
            // Four separating axes; shared edges alone have zero area and are excluded.
            if(Math.Abs(dx)<limit&&Math.Abs(dz)<limit&&Math.Abs(co*dx-si*dz)<limit&&Math.Abs(si*dx+co*dz)<limit)
            {
                if(qx<minX||qx>maxX||qz<minZ||qz>maxZ){Fail("SourceBounds");return;}
                SourceQueries++;
                if(!terrain.TryGetBareTop(qx,qz,out double height)||!Policy.Finite(height)||Math.Abs(height)>1e6){Fail("InvalidSourceHeight");return;}
                // A low overlap must not conceal a large upward cliff in this same root column.
                // No extra queries: every intersecting ground cell must satisfy the displacement cap.
                if(Math.Abs(Math.Floor((height-pose.Y)/.1+1e-7)-c.Y(0))>Policy.MaxDeformation){Fail("DeformationCap");return;}
                minimum=Math.Min(minimum,height);queryHits++;
            }
            if(++qx>qxmax){qx=qxmin;qz++;}
            if(qz>qzmax)
            {
                if(queryHits==0){Fail("NoOverlap");return;}
                double target=Math.Floor((minimum-pose.Y)/.1+1e-7);
                if(Math.Abs(target-c.Y(0))>Policy.MaxDeformation){Fail("DeformationCap");return;}
                // Deliberately retain the visual proof's stricter '< seam-3' boundary.
                if(target>=template.DeformSeam-Policy.MinBelowSeam){Fail("SeamClearance");return;}
                targets[queryColumn]=(int)target;FinishQueryColumn();
            }
        }
        void FinishQueryColumn(){queryActive=false;if(++queryColumn==template.Count)State=Phase.Cells;}
        void CellOne()
        {
            Column c=template.GetColumn(cellColumn);
            if(!rowActive)
            {
                int y=c.Y(sourceRow);rowTint=c.Tint(sourceRow);
                if(y>=template.DeformSeam){nextY=y;endY=y+1;}
                else
                {
                    double ratio=(template.DeformSeam-(double)targets[cellColumn])/(template.DeformSeam-c.Y(0));
                    nextY=(int)Math.Floor(targets[cellColumn]+(y-c.Y(0))*ratio+1e-7);
                    endY=Math.Min(template.DeformSeam,(int)Math.Ceiling(targets[cellColumn]+(y+1-c.Y(0))*ratio-1e-7));
                }
                rowActive=true;
            }
            if(nextY<endY)
            {
                int x=c.X-origin.x,y=nextY-origin.y,z=c.Z-origin.z;
                if(!world.InBounds(x,y,z)){Fail("OutputBounds");return;}
                if(!world.Chunks.ContainsKey(new Int3(x>>4,y>>4,z>>4))&&world.Chunks.Count>=Policy.MaxChunks){Fail("ChunkCap");return;}
                if(world.Get(x,y,z)==MaterialId.Air)
                {
                    if(occupied.Count==Policy.MaxOccupied){Fail("OccupiedCap");return;}
                    occupied.Add(new Int3(c.X,nextY,c.Z));
                }
                world.Set(x,y,z,MaterialId.Wood,rowTint);nextY++;
                PeakAllocatedChunks=Math.Max(PeakAllocatedChunks,world.Chunks.Count);PeakOccupiedCells=Math.Max(PeakOccupiedCells,occupied.Count);
            }
            if(nextY>=endY)
            {
                rowActive=false;finishedRows++;if(++sourceRow==c.Count){sourceRow=0;if(++cellColumn==template.Count)State=Phase.Connectivity;}
            }
        }
        void Visit(Int3 p)
        {
            if(visited.Contains(p))return;
            if(visited.Count>=Policy.MaxOccupied||traversal.Count>=Policy.MaxOccupied){Fail("ConnectivityCap");return;}
            visited.Add(p);traversal.Add(p);ConnectedCells=visited.Count;
        }
        void ConnectivityOne()
        {
            // One occupied-cell seed candidate or one six-neighbor candidate per budget unit.
            if(seedIndex<occupied.Count)
            {
                Int3 p=occupied[seedIndex++];if(p.y>=template.DeformSeam)Visit(p);
                if(seedIndex==occupied.Count&&traversal.Count==0)Fail("NoSeamAnchor");
                return;
            }
            Int3 cell=traversal[traversalIndex],d=directions[traversalDirection];
            var neighbor=new Int3(cell.x+d.x,cell.y+d.y,cell.z+d.z);
            if(world.Get(neighbor.x-origin.x,neighbor.y-origin.y,neighbor.z-origin.z)!=MaterialId.Air)Visit(neighbor);
            if(Terminal)return;
            if(++traversalDirection==6){traversalDirection=0;traversalIndex++;}
            if(traversalIndex==traversal.Count)
            {
                if(visited.Count!=occupied.Count){Fail("DisconnectedBase");return;}
                visited=null;traversal=null;State=Phase.Faces;
            }
        }
        void FaceOne()
        {
            if(faceCell==occupied.Count)return;
            Int3 p=occupied[faceCell],d=directions[faceDirection];
            if(p.y<Policy.GuardY&&world.Get(p.x+d.x-origin.x,p.y+d.y-origin.y,p.z+d.z-origin.z)==MaterialId.Air)
            {if(++RawFaces>Policy.MaxRawFaces){Fail("RawFaceCap");return;}}
            if(++faceDirection==6){faceDirection=0;faceCell++;}
        }
    }
}
