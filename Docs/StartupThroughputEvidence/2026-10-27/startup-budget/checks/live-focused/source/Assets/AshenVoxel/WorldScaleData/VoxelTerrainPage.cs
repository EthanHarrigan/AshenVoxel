using System;
using System.Collections.Generic;
using System.Threading;
using AshenVoxel.Core;

namespace AshenVoxel.WorldScale
{
    public readonly struct TerrainVoxelFace
    {
        public readonly int X,Y,Z,Axis;
        public readonly bool Positive;
        public readonly uint Tint;
        public readonly byte AO0,AO1,AO2,AO3;
        public TerrainVoxelFace(int x,int y,int z,int axis,bool positive,uint tint,byte a,byte b,byte c,byte d)
        {X=x;Y=y;Z=z;Axis=axis;Positive=positive;Tint=tint;AO0=a;AO1=b;AO2=c;AO3=d;}
    }

    /// <summary>Versioned 10cm surface-volume conversion, independent of render triangles.
    /// Resident skin is real VoxelWorld matter; buried terrain remains implicit.
    /// No edits or terrain dynamics are enabled by this conversion.</summary>
    public sealed class VoxelTerrainPage
    {
        public const int Version=1,Side=32,MaximumHeightCells=256,MaximumFaces=65536;
        public const double WidthMeters=Side*WorldCell.SizeMeters;
        public readonly long PageX,PageZ,OriginCellY;
        public readonly VoxelWorld Matter;
        public readonly IReadOnlyList<TerrainVoxelFace> Faces;
        public readonly string SourceIdentity;
        public readonly int ResidentCells;
        VoxelTerrainPage(long x,long z,long y,VoxelWorld matter,List<TerrainVoxelFace> faces,string identity,int cells)
        {PageX=x;PageZ=z;OriginCellY=y;Matter=matter;Faces=faces;SourceIdentity=identity;ResidentCells=cells;}
        public static long TopCell(double surfaceHeight)
        {
            Numeric.Finite(surfaceHeight,nameof(surfaceHeight));if(Math.Abs(surfaceHeight)>1e12)throw new ArgumentOutOfRangeException(nameof(surfaceHeight));
            long top=checked((long)Math.Floor(surfaceHeight*10-.5));
            if((top+.5)*.1>surfaceHeight)top--;
            else if((top+1.5)*.1<=surfaceHeight)top++;
            return top;
        }
        public static double SurfaceTop(double surfaceHeight)=>(TopCell(surfaceHeight)+1)*WorldCell.SizeMeters;
        public static long PageAt(double meters)=>WorldCell.FloorDivide(WorldCell.FromMeters(new WorldPoint(meters,0,0)).X,Side);

        public static VoxelTerrainPage Build(SurfaceMaterialSampler sampler,long pageX,long pageZ,CancellationToken cancel=default)
        {
            if(sampler==null)throw new ArgumentNullException(nameof(sampler));
            long ox=checked(pageX*Side),oz=checked(pageZ*Side);
            const int stride=Side+2;var columns=new SurfaceColumn[stride*stride];var tops=new long[columns.Length];
            long low=long.MaxValue,high=long.MinValue;
            for(int z=-1;z<=Side;z++)for(int x=-1;x<=Side;x++)
            {
                cancel.ThrowIfCancellationRequested();int i=x+1+(z+1)*stride;
                columns[i]=sampler.EvaluateColumn((ox+x+.5)*.1,(oz+z+.5)*.1);tops[i]=TopCell(columns[i].SurfaceHeightMeters);
                low=Math.Min(low,tops[i]);high=Math.Max(high,tops[i]);
            }
            // A one-metre retained skin and boundary apron, never a sea-to-summit column.
            long bottom=low-10,span=high-bottom+1;
            if(span>MaximumHeightCells)throw new InvalidOperationException("Voxel page exceeds its 25.6m vertical budget; height proxy retained.");
            var matter=new VoxelWorld(Side,(int)span,Side,.1f,sampler.Profile.Seed);
            int cells=0;
            for(int z=0;z<Side;z++)for(int x=0;x<Side;x++)
            {
                cancel.ThrowIfCancellationRequested();int i=x+1+(z+1)*stride;long top=tops[i];
                long first=Math.Min(top-9,Math.Min(Math.Min(tops[i-1],tops[i+1]),Math.Min(tops[i-stride],tops[i+stride])));
                for(long y=first;y<=top;y++)
                {
                    MatterSample sample=sampler.SampleMatter(new WorldCell(ox+x,y,oz+z),columns[i]);
                    if(sample.Material==MaterialId.Air)throw new InvalidOperationException("Quantized solid cell disagrees with surface material authority.");
                    matter.Set(x,(int)(y-bottom),z,sample.Material,sample.Tint);cells++;
                }
            }
            var faces=new List<TerrainVoxelFace>(Side*Side*2);
            bool Solid(int x,long y,int z)=>y<=tops[x+1+(z+1)*stride];
            void Face(int x,long y,int z,int axis,bool positive)
            {
                if(faces.Count>=MaximumFaces)throw new InvalidOperationException("Voxel page face budget reached; height proxy retained.");
                int u=(axis+1)%3,v=(axis+2)%3;var ao=new byte[4];
                for(int corner=0;corner<4;corner++)
                {
                    int su=corner==0||corner==3?-1:1,sv=corner<2?-1:1;
                    int ax=x,az=z;long ay=y;
                    if(axis==0)ax+=positive?1:-1;else if(axis==1)ay+=positive?1:-1;else az+=positive?1:-1;
                    bool Sample(int du,int dv)
                    {
                        int sx=ax,sz=az;long sy=ay;
                        if(u==0)sx+=du;else if(u==1)sy+=du;else sz+=du;
                        if(v==0)sx+=dv;else if(v==1)sy+=dv;else sz+=dv;
                        return Solid(sx,sy,sz);
                    }
                    bool a=Sample(su,0),b=Sample(0,sv),d=Sample(su,sv);
                    int value=a&&b?0:3-(a?1:0)-(b?1:0)-(d?1:0);ao[corner]=(byte)(64+value*191/3);
                }
                faces.Add(new TerrainVoxelFace(x,(int)(y-bottom),z,axis,positive,matter.GetColor(x,(int)(y-bottom),z),ao[0],ao[1],ao[2],ao[3]));
            }
            for(int z=0;z<Side;z++)for(int x=0;x<Side;x++)
            {
                cancel.ThrowIfCancellationRequested();int i=x+1+(z+1)*stride;long top=tops[i];Face(x,top,z,1,true);
                for(int direction=0;direction<4;direction++)
                {
                    int axis=direction<2?0:2;bool positive=(direction&1)!=0;
                    long neighbor=tops[i+(axis==0?(positive?1:-1):(positive?stride:-stride))];
                    bool boundary=axis==0?(positive?x==Side-1:x==0):(positive?z==Side-1:z==0);
                    // Aprons close the <=5cm height quantization seam to an unconverted proxy.
                    long first=boundary?Math.Min(neighbor+1,top-9):neighbor+1;
                    for(long y=first;y<=top;y++)Face(x,y,z,axis,positive);
                }
            }
            matter.ClearDirty();
            return new VoxelTerrainPage(pageX,pageZ,bottom,matter,faces,sampler.SourceIdentity+"/voxel-surface-v"+Version,cells);
        }
    }

    /// <summary>Body contact uses the same globally fixed cell centres and occupied
    /// top faces as voxel conversion, independent of mesh readiness or LOD.</summary>
    public sealed class VoxelTerrainCollision:IExplorationCollision,IExplorationResistance,IExplorationGroundSlope
    {
        readonly IExplorationCollision props;
        readonly IWorldField surface;
        public VoxelTerrainCollision(IWorldField surface,IExplorationCollision props)
        {this.surface=surface??throw new ArgumentNullException(nameof(surface));this.props=props??throw new ArgumentNullException(nameof(props));}
        public bool IsReady(double x,double z,double radius)=>props.IsReady(x,z,radius);
        public bool TryGetGroundHeight(double x,double z,out double height)
        {
            WorldCell cell=WorldCell.FromMeters(new WorldPoint(x,0,z));
            height=VoxelTerrainPage.SurfaceTop(surface.Sample(cell.Center.X,cell.Center.Z).HeightMeters);return true;
        }
        // Classify the underlying slope, not the alternating flat treads and
        // vertical risers introduced by 10cm contact quantization.
        public bool TryGetGroundGradient(double x,double z,out double gradientX,out double gradientZ)
        {
            const double probe=.3;
            gradientX=(surface.Sample(x+probe,z).HeightMeters-surface.Sample(x-probe,z).HeightMeters)/(2*probe);
            gradientZ=(surface.Sample(x,z+probe).HeightMeters-surface.Sample(x,z-probe).HeightMeters)/(2*probe);
            return !double.IsNaN(gradientX)&&!double.IsInfinity(gradientX)&&!double.IsNaN(gradientZ)&&!double.IsInfinity(gradientZ);
        }
        public bool OverlapsBody(WorldPoint feet,double radius,double height)=>props.OverlapsBody(feet,radius,height);
        public bool TryGetSupportHeight(double x,double z,double radius,double minimumHeight,double maximumHeight,out double height)=>props.TryGetSupportHeight(x,z,radius,minimumHeight,maximumHeight,out height);
        public double WeightedSoftVoxels(WorldPoint feet,double radius,double height)=>props is IExplorationResistance resistance?resistance.WeightedSoftVoxels(feet,radius,height):0;
    }
}
