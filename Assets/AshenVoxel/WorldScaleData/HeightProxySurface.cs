using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using AshenVoxel.Core;

namespace AshenVoxel.WorldScale
{
    /// <summary>Replaceable surface render cache. Shared indexed grid, continuous
    /// sample heights and conservative skirts; never gameplay matter/collision.</summary>
    public sealed class HeightProxySurface
    {
        public const int Version=2,VertexStride=24,MaximumQuads=256;
        [StructLayout(LayoutKind.Sequential,Pack=1)]
        public readonly struct Vertex
        {
            public readonly float X,Y,Z;
            public readonly sbyte NX,NY,NZ,NW;
            public readonly byte R,G,B,A,Roughness,Wetness,Snow,Material;
            static sbyte Pack(double n)=>(sbyte)Math.Round(Math.Max(-1,Math.Min(1,n))*127);
            public Vertex(float x,float y,float z,double nx,double ny,double nz,uint tint,byte rough,byte wet,MaterialId material)
            {X=x;Y=y;Z=z;NX=Pack(nx);NY=Pack(ny);NZ=Pack(nz);NW=0;R=(byte)(tint>>16);G=(byte)(tint>>8);B=(byte)tint;A=255;
                Roughness=rough;Wetness=wet;Snow=material==MaterialId.Snow?(byte)255:(byte)0;Material=(byte)(128+(int)material);}
            public Vertex With(float y,double nx,double ny,double nz)=>new Vertex(X,y,Z,nx,ny,nz,0xFF000000u|((uint)R<<16)|((uint)G<<8)|B,Roughness,Wetness,(MaterialId)(Material-128));
        }
        readonly struct Sample
        {
            public readonly double Height;public readonly uint Tint;public readonly MaterialId Material;public readonly byte Rough,Wet;
            public Sample(SurfaceColumn c)
            {
                Height=c.SurfaceHeightMeters;Tint=c.SurfaceTint;Material=c.SurfaceMaterial;
                double rough=Material==MaterialId.Ice ? .18 : Material==MaterialId.Snow ? .92 : Math.Max(.45,(Material==MaterialId.Mud||Material==MaterialId.Clay ? .82 : .95)-c.Wetness*.24);
                Rough=(byte)Math.Round(rough*255);Wet=(byte)Math.Round(Math.Max(0,Math.Min(1,c.Wetness))*255);
            }
        }
        public readonly int Quads;
        public readonly double AnchorY;
        public readonly Vertex[] Vertices;
        public readonly int[] Indices;
        public readonly float[] Heights;
        public readonly float MinimumLocalY,MaximumLocalY;
        public readonly string SourceIdentity;
        public long MeshPayloadBytes=>Vertices.LongLength*VertexStride+Indices.LongLength*4;
        public long ManagedResultBytes=>MeshPayloadBytes+Heights.LongLength*4;
        HeightProxySurface(int n,double anchor,Vertex[] vertices,int[] indices,float[] heights,float lo,float hi,string identity)
        {Quads=n;AnchorY=anchor;Vertices=vertices;Indices=indices;Heights=heights;MinimumLocalY=lo;MaximumLocalY=hi;SourceIdentity=identity;}

        // Only the validated replaceable disk-cache reader calls this seam.
        internal static HeightProxySurface RestoreCached(int n,double anchor,Vertex[] vertices,int[] indices,float[] heights,float lo,float hi,string identity)
            =>new HeightProxySurface(n,anchor,vertices,indices,heights,lo,hi,identity);

        /// <summary>Exact triangle-profile bridge, not a downward skirt. Coordinates
        /// must include every breakpoint of both adjacent piecewise-linear edges.</summary>
        public static int[] SeamRibbonIndices(int pointCount)
        {
            if(pointCount<2||pointCount>4096)throw new ArgumentOutOfRangeException(nameof(pointCount));
            var result=new int[(pointCount-1)*12];int at=0;
            for(int i=0;i<pointCount-1;i++)
            {
                int a=i*2,b=a+2;
                result[at++]=a;result[at++]=b;result[at++]=a+1;
                result[at++]=b;result[at++]=b+1;result[at++]=a+1;
                result[at++]=a+1;result[at++]=b;result[at++]=a;
                result[at++]=a+1;result[at++]=b+1;result[at++]=b;
            }
            return result;
        }

        public static double TriangleHeight(float[] heights,int quads,double anchor,double x,double z)
        {
            if(heights==null||quads<1||heights.Length!=(quads+1)*(quads+1))throw new ArgumentException("Triangle grid shape");
            x=Math.Max(0,Math.Min(quads,x));z=Math.Max(0,Math.Min(quads,z));
            int ix=Math.Min(quads-1,(int)Math.Floor(x)),iz=Math.Min(quads-1,(int)Math.Floor(z)),stride=quads+1,a=ix+iz*stride;
            double fx=x-ix,fz=z-iz,h00=heights[a],h10=heights[a+1],h01=heights[a+stride],h11=heights[a+stride+1];
            return anchor+(fx+fz<=1?h00+(h10-h00)*fx+(h01-h00)*fz:h10*(1-fz)+h01*(1-fx)+h11*(fx+fz-1));
        }

        public static HeightProxySurface Build(SurfaceMaterialSampler sampler,LodTile tile,int n,double skirtDepth,CancellationToken cancel=default,bool parallelSampling=false)
        {
            if(sampler==null||tile==null)throw new ArgumentNullException();
            if(n<8||n>MaximumQuads||(n&(n-1))!=0||tile.Key.Level<0||tile.Key.Level>14)throw new ArgumentOutOfRangeException(nameof(n));
            Numeric.Positive(skirtDepth,nameof(skirtDepth));
            long cells=256L<<tile.Key.Level;
            if(Math.Abs(tile.SizeMeters-cells*.1)>1e-5||Math.Abs(tile.OriginXMeters-tile.Key.X*cells*.1)>1e-4||Math.Abs(tile.OriginZMeters-tile.Key.Z*cells*.1)>1e-4)
                throw new ArgumentException("Height proxies require the same25.6m dyadic render grid.");
            int pitch=n+3,stride=n+1;double spacing=tile.SizeMeters/n;
            var samples=new Sample[pitch*pitch];int At(int x,int z)=>x+1+(z+1)*pitch;
            void Row(int z)
            {for(int x=-1;x<=n+1;x++){cancel.ThrowIfCancellationRequested();samples[At(x,z)]=new Sample(sampler.EvaluateColumn(tile.OriginXMeters+x*spacing,tile.OriginZMeters+z*spacing,spacing));}}
            if(parallelSampling&&n>=128&&Environment.ProcessorCount>2)
                Parallel.For(-1,n+2,new ParallelOptions{MaxDegreeOfParallelism=2,CancellationToken=cancel},Row);
            else for(int z=-1;z<=n+1;z++)Row(z);
            double anchor=samples[At(0,0)].Height;var heights=new float[stride*stride];
            var vertices=new Vertex[stride*stride+8*stride];var indices=new int[n*n*6+n*24];
            float lo=float.MaxValue,hi=float.MinValue;
            for(int z=0;z<=n;z++)for(int x=0;x<=n;x++)
            {
                cancel.ThrowIfCancellationRequested();var s=samples[At(x,z)];
                double dx=(samples[At(x+1,z)].Height-samples[At(x-1,z)].Height)/(2*spacing),dz=(samples[At(x,z+1)].Height-samples[At(x,z-1)].Height)/(2*spacing);
                double inv=1/Math.Sqrt(dx*dx+dz*dz+1);float y=(float)(s.Height-anchor);int i=x+z*stride;heights[i]=y;
                vertices[i]=new Vertex((float)(x*spacing),y,(float)(z*spacing),-dx*inv,inv,-dz*inv,s.Tint,s.Rough,s.Wet,s.Material);lo=Math.Min(lo,y);hi=Math.Max(hi,y);
            }
            int index=0;
            void Tri(int a,int b,int c){indices[index++]=a;indices[index++]=b;indices[index++]=c;}
            for(int z=0;z<n;z++)for(int x=0;x<n;x++)
            {cancel.ThrowIfCancellationRequested();int a=x+z*stride,b=a+stride;Tri(a,b,a+1);Tri(a+1,b,b+1);}
            int next=stride*stride;
            for(int edge=0;edge<4;edge++)
            {
                int rim=next;double nx=edge==1?1:edge==3?-1:0,nz=edge==0?-1:edge==2?1:0;
                for(int i=0;i<=n;i++)
                {
                    cancel.ThrowIfCancellationRequested();int a=edge==0?i:edge==1?i*stride+n:edge==2?n*stride+n-i:(n-i)*stride;
                    Vertex top=vertices[a];float bottom=(float)(top.Y-skirtDepth);
                    vertices[next++]=top.With(top.Y,nx,0,nz);vertices[next++]=top.With(bottom,nx,0,nz);lo=Math.Min(lo,bottom);
                }
                for(int i=0;i<n;i++){int a=rim+i*2;Tri(a,a+2,a+1);Tri(a+2,a+3,a+1);}
            }
            return new HeightProxySurface(n,anchor,vertices,indices,heights,lo,hi,sampler.SourceIdentity+"/height-proxy-cache-v"+Version+"/"+tile.Key+"/"+n);
        }
    }
}
