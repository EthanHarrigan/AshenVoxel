using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AshenVoxel.Core;

namespace AshenVoxel.WorldScale
{
    /// <summary>One merged rectangle of a stepped terrain tile. Coordinates are tile-local
    /// metres, Y relative to <see cref="VoxelStepSurface.AnchorY"/>. U/V follow the
    /// VoxelLodFace convention: U spans axis (Axis+1)%3 and V spans axis (Axis+2)%3.
    /// For walls the vertical extent may be negative: such a wall is only exposed by the
    /// parent geometry and is back-facing (culled) until the morph turns it around.
    /// MorphLow/MorphHigh are the local Y targets of the edge at Y and of the opposite
    /// vertical edge once the tile has fully morphed into its parent (twice the block size).</summary>
    public readonly struct VoxelStepQuad
    {
        public readonly float X,Y,Z,U,V,MorphLow,MorphHigh;
        public readonly byte Axis;
        public readonly bool Positive;
        public readonly MaterialId Material;
        public readonly byte Wetness;
        public VoxelStepQuad(float x,float y,float z,float u,float v,int axis,bool positive,float morphLow,float morphHigh,MaterialId material,byte wetness)
        {X=x;Y=y;Z=z;U=u;V=v;Axis=(byte)axis;Positive=positive;MorphLow=morphLow;MorphHigh=morphHigh;Material=material;Wetness=wetness;}
        /// <summary>Signed vertical extent of a wall; zero for tops.</summary>
        public float Height=>Axis==0?U:Axis==2?V:0;
    }

    /// <summary>Merged, cubic-stepped terrain render cache.
    ///
    /// Geometry is merged by shape only; colour never splits a rectangle. Each tile also
    /// publishes compact per-column colour and height texels which the ground shader paints
    /// back onto the merged rectangles, so every rendered block keeps the pigment of its own
    /// authoritative cell. Blocks are cubic at every level (10, 20, 40, 80 cm ...): tops are
    /// quantised to the level step vertically as well as horizontally.
    ///
    /// Each vertex also carries its height in the parent level (twice the block size). The
    /// shader blends towards that target with view distance, so one level turns into the next
    /// continuously instead of popping. Merging therefore keys on the level AND parent heights.
    /// Walls are emitted from a side whenever the level or the parent geometry exposes it; a
    /// wall whose morph inverts turns its back to the camera (culled) while the opposite wall
    /// grows into place.
    ///
    /// Replaceable render cache only: authoritative 10 cm matter, collision and generator
    /// identity are unchanged.</summary>
    public sealed class VoxelStepSurface
    {
        public const int Version=1,MaximumQuads=256,MaximumFaces=1048576;
        /// <summary>Exact colour slices per column (block depths 0..ExactSlices-1) plus one deep slice.</summary>
        public const int ExactSlices=3,ColorSlices=ExactSlices+1,ParentSliceColumns=2;
        public const int HeightBias=32768;
        /// <summary>Vertical block size cap in cells (1.6 m). Blocks stay cubic up to this size; coarser
        /// (always distant) levels get wider blocks but never quantise heights by more than this.</summary>
        public const int MaximumVerticalCells=16;
        public static int VerticalCells(int step)=>Math.Min(step,MaximumVerticalCells);
        /// <summary>Packed GPU vertex: UNorm16 x4 (column x, cell y, column z, morph cell y) + SNorm8 x4 normal.</summary>
        public const int VertexStride=12,IndexStride=2,MaximumQuadsPerSubmesh=16384;

        public readonly int Quads,Step,VerticalStep;
        public readonly long AnchorCell;
        public readonly double AnchorY,CellMeters,VerticalMeters;
        /// <summary>n*n local level top heights (metres above anchor): the LocalHeights contract of voxel columns.</summary>
        public readonly float[] Heights;
        public readonly IReadOnlyList<VoxelStepQuad> Faces;
        /// <summary>Packed texels (see PackTexel), width <see cref="ColorWidth"/>=n. Rows [k*n,(k+1)*n) hold level
        /// slice k. Parent slice k (n/2 square) starts at column (k%2)*n/2, row n*ColorSlices+(k/2)*n/2.</summary>
        public readonly uint[] Colors;
        /// <summary>16-bit texels, width n+2. Rows [0,n+2): level top planes with a one-column halo
        /// (texel x+1,z+1 is column x,z). Rows [n+2,n+2+n/2+2): parent top planes with a halo.
        /// Value = plane cell - AnchorCell + HeightBias.</summary>
        public readonly ushort[] HeightTexels;
        public readonly int ColorWidth,ColorHeight,HeightWidth,HeightHeight;
        public readonly int UnmergedFaceCount;
        public readonly string SourceIdentity;
        public int ParentQuads=>Quads/2;
        public long TextureBytes=>Colors.LongLength*4+HeightTexels.LongLength*2;
        public long MeshBytes=>(long)Faces.Count*(4*VertexStride+6*IndexStride);
        public long PayloadBytes=>MeshBytes+TextureBytes;

        VoxelStepSurface(int n,int step,long anchorCell,float[] heights,List<VoxelStepQuad> faces,uint[] colors,ushort[] heightTexels,int unmerged,string identity)
        {
            Quads=n;Step=step;VerticalStep=VerticalCells(step);AnchorCell=anchorCell;AnchorY=anchorCell*WorldCell.SizeMeters;CellMeters=step*WorldCell.SizeMeters;VerticalMeters=VerticalStep*WorldCell.SizeMeters;
            Heights=heights;Faces=faces;Colors=colors;HeightTexels=heightTexels;UnmergedFaceCount=unmerged;SourceIdentity=identity;
            ColorWidth=n;ColorHeight=ColorRows(n,ColorSlices);HeightWidth=n+2;HeightHeight=(n+2)+(n/2+2);
        }

        /// <summary>Exclusive top plane (in cells) of a column whose highest solid cell is
        /// <paramref name="topCell"/>, rounded to the nearest multiple of the level step.</summary>
        public static long QuantizedTop(long topCell,int step)
        {
            long plane=checked(topCell+1);if(step<=1)return plane;
            return checked((long)Math.Floor((double)plane/step+.5)*step);
        }
        /// <summary>First texel row after parent slice band b (b=ColorSlices gives the texture height).</summary>
        public static int ColorRows(int n,int slices)=>n*ColorSlices+((slices+ParentSliceColumns-1)/ParentSliceColumns)*(n/2);
        /// <summary>Texel index of parent slice k, parent column (x,z).</summary>
        public static int ParentTexel(int n,int k,int x,int z)=>(k%ParentSliceColumns)*(n/2)+x+(n*ColorSlices+(k/ParentSliceColumns)*(n/2)+z)*n;
        /// <summary>Colour texels as RGBA32 bytes in texture memory order (bottom row first, matching texel rows).</summary>
        public byte[] ColorBytesRGBA()
        {
            var bytes=new byte[Colors.Length*4];
            for(int i=0;i<Colors.Length;i++){uint c=Colors[i];bytes[i*4]=(byte)(c>>16);bytes[i*4+1]=(byte)(c>>8);bytes[i*4+2]=(byte)c;bytes[i*4+3]=(byte)(c>>24);}
            return bytes;
        }
        /// <summary>RGB = sRGB pigment; alpha = material id (5 bits) and wetness (3 bits).
        /// Material and wetness live in the texel so that they never split merged geometry.</summary>
        public static uint PackTexel(uint tint,MaterialId material,double wetness)
        {
            uint w=(uint)Math.Round(Math.Max(0,Math.Min(1,wetness))*7);
            uint alpha=(((uint)material&31u)<<3)|w;
            return (tint&0x00FFFFFFu)|(alpha<<24);
        }
        public static MaterialId TexelMaterial(uint texel)=>(MaterialId)((texel>>27)&31u);
        /// <summary>Cell sampled for colour slice k of a column. Never above the real surface.</summary>
        public static long SliceCell(long plane,long realTop,int step,int k)
        {
            long centre=plane-1-(long)k*step-(step-1)/2;
            return Math.Min(centre,realTop-(long)k*step);
        }

        public static VoxelStepSurface Build(SurfaceMaterialSampler sampler,LodTile tile,int n,double seamFloor,CancellationToken cancel=default,bool parallelSampling=false)
        {
            if(sampler==null||tile==null)throw new ArgumentNullException();
            if(n<8||n>MaximumQuads||(n&(n-1))!=0||tile.Key.Level<0||tile.Key.Level>14)throw new ArgumentOutOfRangeException(nameof(n));
            long cells=256L<<tile.Key.Level;
            if(Math.Abs(tile.SizeMeters-cells*.1)>1e-5||Math.Abs(tile.OriginXMeters-tile.Key.X*cells*.1)>1e-4||Math.Abs(tile.OriginZMeters-tile.Key.Z*cells*.1)>1e-4)
                throw new ArgumentException("Voxel step tiles must share the global25.6m dyadic grid.");
            Numeric.Finite(seamFloor,nameof(seamFloor));
            int step=checked((int)(cells/n)),pstep=step*2,pn=n/2;
            long ox=checked(tile.Key.X*cells),oz=checked(tile.Key.Z*cells);
            float spacing=(float)(step*.1);
            bool parallel=parallelSampling&&n>=128&&Environment.ProcessorCount>2;
            var options=new ParallelOptions{MaxDegreeOfParallelism=2,CancellationToken=cancel};

            // Level and parent columns, each with a one-column halo.
            int pitch=n+2,ppitch=pn+2;
            var columns=new SurfaceColumn[pitch*pitch];var top=new long[pitch*pitch];var T=new long[pitch*pitch];
            var pcolumns=new SurfaceColumn[ppitch*ppitch];var ptop=new long[ppitch*ppitch];var P=new long[ppitch*ppitch];
            int At(int x,int z)=>x+1+(z+1)*pitch;
            int PAt(int x,int z)=>x+1+(z+1)*ppitch;
            long CellX(int x,int s)=>ox+(long)x*s+s/2;
            long CellZ(int z,int s)=>oz+(long)z*s+s/2;
            void LevelRow(int z)
            {
                for(int x=-1;x<=n;x++)
                {
                    cancel.ThrowIfCancellationRequested();int i=At(x,z);WorldPoint p=new WorldCell(CellX(x,step),0,CellZ(z,step)).Center;
                    columns[i]=sampler.EvaluateColumn(p.X,p.Z);top[i]=VoxelTerrainPage.TopCell(columns[i].SurfaceHeightMeters);T[i]=QuantizedTop(top[i],VerticalCells(step));
                }
            }
            void ParentRow(int z)
            {
                for(int x=-1;x<=pn;x++)
                {
                    cancel.ThrowIfCancellationRequested();int i=PAt(x,z);WorldPoint p=new WorldCell(CellX(x,pstep),0,CellZ(z,pstep)).Center;
                    pcolumns[i]=sampler.EvaluateColumn(p.X,p.Z);ptop[i]=VoxelTerrainPage.TopCell(pcolumns[i].SurfaceHeightMeters);P[i]=QuantizedTop(ptop[i],VerticalCells(pstep));
                }
            }
            if(parallel){Parallel.For(-1,n+1,options,LevelRow);Parallel.For(-1,pn+1,options,ParentRow);}
            else{for(int z=-1;z<=n;z++)LevelRow(z);for(int z=-1;z<=pn;z++)ParentRow(z);}

            long anchorCell=T[At(0,0)];
            float Local(long plane)=>(float)((plane-anchorCell)*.1);
            int ParentIndex(int v)=>v<0?-1:v>=n?pn:v>>1;
            long PT(int x,int z)=>P[PAt(ParentIndex(x),ParentIndex(z))];

            // Colour slices and per-column top attributes.
            var material=new MaterialId[n*n];var wet=new byte[n*n];var heights=new float[n*n];
            var colors=new uint[n*ColorRows(n,ColorSlices)];
            uint Slice(SurfaceColumn column,long realTop,long plane,int s,int k,long cx,long cz,out MaterialId id)
            {
                long y;
                if(k<ExactSlices)y=SliceCell(plane,realTop,s,k);
                else
                {
                    // Deep slice: below every soft layer, the representative cliff/strata material.
                    double depth=column.IceDepthMeters+column.GroundCoverDepthMeters+column.OrganicDepthMeters+column.SoilDepthMeters+column.SedimentDepthMeters+1.0;
                    y=Math.Min(realTop-(long)ExactSlices*s,VoxelTerrainPage.TopCell(column.GroundHeightMeters-depth));
                }
                MatterSample m=sampler.SampleMatter(new WorldCell(cx,y,cz),column);
                if(m.Material==MaterialId.Air)m=sampler.SampleMatter(new WorldCell(cx,realTop,cz),column);
                id=m.Material;return PackTexel(m.Tint,m.Material,column.Wetness);
            }
            void ColorRow(int z)
            {
                for(int x=0;x<n;x++)
                {
                    cancel.ThrowIfCancellationRequested();int i=At(x,z);long cx=CellX(x,step),cz=CellZ(z,step);
                    for(int k=0;k<ColorSlices;k++)
                    {
                        colors[x+(z+k*n)*n]=Slice(columns[i],top[i],T[i],VerticalCells(step),k,cx,cz,out MaterialId id);
                        if(k==0)material[x+z*n]=id;
                    }
                    wet[x+z*n]=(byte)Math.Round(Math.Max(0,Math.Min(1,columns[i].Wetness))*255);
                    heights[x+z*n]=Local(T[i]);
                }
            }
            void ParentColorRow(int z)
            {
                for(int x=0;x<pn;x++)
                {
                    cancel.ThrowIfCancellationRequested();int i=PAt(x,z);long cx=CellX(x,pstep),cz=CellZ(z,pstep);
                    for(int k=0;k<ColorSlices;k++)colors[ParentTexel(n,k,x,z)]=Slice(pcolumns[i],ptop[i],P[i],VerticalCells(pstep),k,cx,cz,out _);
                }
            }
            if(parallel){Parallel.For(0,n,options,ColorRow);Parallel.For(0,pn,options,ParentColorRow);}
            else{for(int z=0;z<n;z++)ColorRow(z);for(int z=0;z<pn;z++)ParentColorRow(z);}

            var texels=new ushort[(n+2)*((n+2)+(pn+2))];
            ushort Pack(long plane){long v=plane-anchorCell+HeightBias;return (ushort)Math.Max(0,Math.Min(65535,v));}
            for(int z=-1;z<=n;z++)for(int x=-1;x<=n;x++)texels[(x+1)+(z+1)*(n+2)]=Pack(T[At(x,z)]);
            for(int z=-1;z<=pn;z++)for(int x=-1;x<=pn;x++)texels[(x+1)+(n+2+z+1)*(n+2)]=Pack(P[PAt(x,z)]);

            var faces=new List<VoxelStepQuad>(n*8);
            void Add(VoxelStepQuad q){if(faces.Count>=MaximumFaces)throw new VoxelLodCapacityException();faces.Add(q);}
            int unmerged=n*n;

            // Tops: greedy rectangles of equal level height, parent height and material.
            var used=new bool[n*n];
            bool SameTop(int x,int z,int reference,int rx,int rz)=>!used[x+z*n]&&T[At(x,z)]==T[reference]&&PT(x,z)==PT(rx,rz);
            for(int z=0;z<n;z++)for(int x=0;x<n;x++)
            {
                cancel.ThrowIfCancellationRequested();if(used[x+z*n])continue;int i=At(x,z);
                int w=1,d=1;
                while(x+w<n&&SameTop(x+w,z,i,x,z))w++;
                bool extend=true;
                while(z+d<n&&extend){for(int xx=x;xx<x+w;xx++)if(!SameTop(xx,z+d,i,x,z)){extend=false;break;}if(extend)d++;}
                for(int zz=z;zz<z+d;zz++)for(int xx=x;xx<x+w;xx++)used[xx+zz*n]=true;
                float y=Local(T[i]),py=Local(PT(x,z));
                // For axis Y, U runs +Z and V runs +X.
                Add(new VoxelStepQuad(x*spacing,y,z*spacing,d*spacing,w*spacing,1,true,py,py,material[x+z*n],wet[x+z*n]));
            }

            // Walls: runs along each column boundary with identical level/parent heights on both sides.
            // Outer tile boundaries get an apron down below every possible neighbouring LOD instead.
            long floorPlane=VoxelTerrainPage.TopCell(seamFloor);
            for(int side=0;side<4;side++)
            {
                bool alongX=side<2,positive=(side&1)!=0;int dx=alongX?(positive?1:-1):0,dz=alongX?0:(positive?1:-1);
                for(int line=0;line<n;line++)
                {
                    int run=0;long rTA=0,rTB=0,rPA=0,rPB=0;MaterialId rMat=MaterialId.Air;byte rWet=0;bool rApron=false;int start=0;
                    void Flush(int end)
                    {
                        if(run==0)return;
                        int ax=alongX?line:start,az=alongX?start:line;
                        float px=alongX?(line+(positive?1:0))*spacing:start*spacing;
                        float pz=alongX?start*spacing:(line+(positive?1:0))*spacing;
                        float len=(end-start)*spacing;
                        long bottom=rApron?rTB:rTB,top=rTA;
                        float y=Local(bottom),h=Local(top)-y;
                        float low=rApron?y:Local(rPB),high=Local(rPA);
                        Add(new VoxelStepQuad(px,y,pz,alongX?h:len,alongX?len:h,alongX?0:2,positive,low,high,rMat,rWet));
                        run=0;
                    }
                    for(int along=0;along<n;along++)
                    {
                        int x=alongX?line:along,z=alongX?along:line,nx=x+dx,nz=z+dz;
                        bool boundary=nx<0||nz<0||nx>=n||nz>=n;
                        long ta=T[At(x,z)],tb=T[At(nx,nz)],pa=PT(x,z),pb=PT(nx,nz);
                        MaterialId mat=material[x+z*n];byte w=wet[x+z*n];
                        bool emit;
                        if(boundary)
                        {
                            // Apron: from the column top to below the lowest neighbour/floor, static bottom edge.
                            tb=Math.Min(floorPlane,Math.Min(Math.Min(tb,pb),Math.Min(ta,pa))-2*VerticalCells(pstep));pb=tb;emit=true;
                            if(ta>T[At(nx,nz)])unmerged+=(int)((ta-T[At(nx,nz)])/VerticalCells(step));
                        }
                        else
                        {
                            emit=ta>tb||pa>pb;
                            if(ta>tb)unmerged+=(int)((ta-tb)/VerticalCells(step));
                        }
                        bool same=run>0&&emit&&rApron==boundary&&rTA==ta&&rTB==tb&&rPA==pa&&rPB==pb;
                        if(!same)
                        {
                            Flush(along);
                            if(emit){run=1;start=along;rTA=ta;rTB=tb;rPA=pa;rPB=pb;rMat=mat;rWet=w;rApron=boundary;}
                        }
                        else run++;
                    }
                    Flush(n);
                }
            }
            return new VoxelStepSurface(n,step,anchorCell,heights,faces,colors,texels,unmerged,
                sampler.SourceIdentity+"/voxel-step-v"+Version+"/"+tile.Key+"/"+n);
        }
    }
}
