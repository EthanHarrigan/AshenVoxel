using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using AshenVoxel.Core;
using AshenVoxel.WorldScale;
using RootContactArrayMesher;

namespace RootContactUploadPrototype
{
    public sealed class UploadLimits
    {
        public readonly int Vertices,Indices;public readonly long PayloadBytes;
        // Explicit caller reservation, not a native/CLR heap guarantee.
        public UploadLimits(int vertices,int indices,long payloadBytes)
        {if(vertices<0||vertices>8000000||indices<0||indices>12000000||payloadBytes<0||payloadBytes>512L*1024*1024)throw new ArgumentException("upload-limits");Vertices=vertices;Indices=indices;PayloadBytes=payloadBytes;}
        internal void Admit(long vertices,long indices)
        {if(vertices>Vertices||indices>Indices||28*vertices+4*indices>PayloadBytes)throw new InvalidOperationException("upload-capacity");}
    }
    public readonly struct PreparedBounds
    {
        public readonly Float3 Minimum,Maximum;public readonly bool HasVertices;
        internal PreparedBounds(Float3[] p)
        {HasVertices=p.Length!=0;Float3 min=HasVertices?p[0]:default,max=min;for(int i=1;i<p.Length;i++)for(int d=0;d<3;d++){min[d]=Math.Min(min[d],p[i][d]);max[d]=Math.Max(max[d],p[i][d]);}Minimum=min;Maximum=max;}
    }
    public sealed class PreparedMesh
    {
        readonly Float3[] positions,normals;readonly Rgba32[] colors;readonly int[][] parts;
        public readonly string Identity;public readonly int Lod,CutCells,CrossingQuads;
        public readonly bool CanonicalPartition;public readonly PreparedBounds Bounds;
        public int VertexCount=>positions.Length;public int PartCount=>parts.Length;
        public ReadOnlySpan<Float3> Positions=>positions;public ReadOnlySpan<Float3> Normals=>normals;public ReadOnlySpan<Rgba32> Colors=>colors;
        public ReadOnlySpan<int> Indices(int part)=>parts[part];
        public int Triangles(int part)=>parts[part].Length/3;
        public long IndexCount {get;} public long GpuPayloadBytes=>28L*VertexCount+4*IndexCount;
        internal PreparedMesh(Float3[] p,Float3[] n,Rgba32[] c,int[][] indices,string identity,int lod,int crossings,bool canonical)
        {positions=p;normals=n;colors=c;parts=indices;Identity=identity;Lod=lod;CutCells=UploadPreparation.Cut(lod);CrossingQuads=crossings;CanonicalPartition=canonical;Bounds=new PreparedBounds(p);foreach(var a in indices)IndexCount+=a.Length;}
    }
    public static class UploadPreparation
    {
        public const int UpperPart=0,AuthoredBasePart=1,LowerPart=0,MaxChunks=128;
        public const int AnchorX=235,AnchorY=1028,AnchorZ=235;
        public const string Policy="root-upload-v1;avpm4;original-local;.1;shared-vbo;cuts30,32,36,60,124;anchor235,1028,235";
        public static int Cut(int lod)=>lod==0?30:lod==1?32:lod==2?36:lod==3?60:lod==4?124:throw new ArgumentOutOfRangeException(nameof(lod));
        public static int Factor(int lod)=>lod==0?1:lod==1?2:lod==2?4:lod==3?16:lod==4?64:throw new ArgumentOutOfRangeException(nameof(lod));
        static void Identity(string value){if(string.IsNullOrEmpty(value)||value.Length>4096)throw new ArgumentException("bounded-identity");}
        static string Digest(string value)
        {using var sha=System.Security.Cryptography.SHA256.Create();var b=sha.ComputeHash(Encoding.UTF8.GetBytes(value));var s=new StringBuilder(64);foreach(byte v in b)s.Append(v.ToString("x2",CultureInfo.InvariantCulture));return s.ToString();}
        static bool Finite(float v)=>!float.IsNaN(v)&&!float.IsInfinity(v);
        static byte Normal(Float3 n)
        {if(n.x==1&&n.y==0&&n.z==0)return 0;if(n.x==-1&&n.y==0&&n.z==0)return 1;if(n.y==1&&n.x==0&&n.z==0)return 2;if(n.y==-1&&n.x==0&&n.z==0)return 3;if(n.z==1&&n.x==0&&n.y==0)return 4;if(n.z==-1&&n.x==0&&n.y==0)return 5;throw new ArgumentException("nonaxial-normal");}
        static Float3 Normal(byte n)=>n==0?new Float3(1,0,0):n==1?new Float3(-1,0,0):n==2?new Float3(0,1,0):n==3?new Float3(0,-1,0):n==4?new Float3(0,0,1):n==5?new Float3(0,0,-1):throw new ArgumentException("normal-range");
        static void Packed(byte alpha){if(alpha>>2==0||!MaterialCatalog.IsValid((MaterialId)(alpha>>2)))throw new ArgumentException("packed-material");}
        // Validate the rectangular cyclic quad contract BEFORE the frozen splitter allocates.
        static void Quad(short[] positions,byte[] normals,uint[] colors,int[] indices,int q)
        {
            byte normal=normals[q];int axis=normal/2,u=(axis+1)%3,v=(axis+2)%3;
            if(normal>5)throw new ArgumentException("normal-range");
            int minU=int.MaxValue,maxU=int.MinValue,minV=int.MaxValue,maxV=int.MinValue;
            for(int j=0;j<4;j++)
            {
                int k=q+j;Packed((byte)(colors[k]>>24));
                if(normals[k]!=normal||positions[k*3+axis]!=positions[q*3+axis]||(colors[k]>>26)!=(colors[q]>>26))throw new ArgumentException("quad-plane-material");
                minU=Math.Min(minU,positions[k*3+u]);maxU=Math.Max(maxU,positions[k*3+u]);minV=Math.Min(minV,positions[k*3+v]);maxV=Math.Max(maxV,positions[k*3+v]);
            }
            if(minU==maxU||minV==maxV)throw new ArgumentException("degenerate-quad");
            int corners=0;
            for(int j=0;j<4;j++)
            {
                int a=(q+j)*3,b=(q+(j+1)%4)*3,cu=positions[a+u],cv=positions[a+v];
                if((cu!=minU&&cu!=maxU)||(cv!=minV&&cv!=maxV))throw new ArgumentException("nonrectangular-quad");
                int bit=1<<((cu==maxU?1:0)+(cv==maxV?2:0));if((corners&bit)!=0)throw new ArgumentException("duplicate-corner");corners|=bit;
                if((cu==positions[b+u])==(cv==positions[b+v]))throw new ArgumentException("nonaxial-edge");
            }
            long twiceArea=0;int mask=0,at=q/4*6,firstTriangleMask=0,secondTriangleMask=0;
            for(int t=0;t<2;t++)
            {
                int a=indices[at+t*3],b=indices[at+t*3+1],c=indices[at+t*3+2];
                if(a<q||a>=q+4||b<q||b>=q+4||c<q||c>=q+4)throw new ArgumentException("index-outside-quad");
                int triangleMask=(1<<(a-q))|(1<<(b-q))|(1<<(c-q));mask|=triangleMask;if(t==0)firstTriangleMask=triangleMask;else secondTriangleMask=triangleMask;
                long cross=(long)(positions[b*3+u]-positions[a*3+u])*(positions[c*3+v]-positions[a*3+v])-(long)(positions[b*3+v]-positions[a*3+v])*(positions[c*3+u]-positions[a*3+u]);
                if(cross==0||(normal%2==0?cross:-cross)<=0)throw new ArgumentException("triangle-winding");twiceArea+=Math.Abs(cross);
            }
            int distinct=firstTriangleMask^secondTriangleMask;
            if(mask!=15||(distinct!=5&&distinct!=10)||twiceArea!=2L*(maxU-minU)*(maxV-minV))throw new ArgumentException("quad-coverage");
        }
        public static PreparedMesh PrepareCanonical(PrototypeMeshData mesh,int lod,string sourceIdentity,UploadLimits limits)
        {
            int cut=Cut(lod);Identity(sourceIdentity);if(mesh==null||limits==null||mesh.Positions==null||mesh.Normals==null||mesh.Colors==null||mesh.Indices==null)throw new ArgumentNullException();
            int n=mesh.VertexCount;if(n%4!=0||mesh.Positions.LongLength!=3L*n||mesh.Normals.Length!=n||mesh.Indices.LongLength!=n/4L*6)throw new ArgumentException("quad-array-layout");
            limits.Admit(n,mesh.Indices.Length);int crossings=0;
            for(int q=0;q<n;q+=4)
            {
                Quad(mesh.Positions,mesh.Normals,mesh.Colors,mesh.Indices,q);int lo=int.MaxValue,hi=int.MinValue;for(int j=0;j<4;j++){lo=Math.Min(lo,mesh.Positions[(q+j)*3+1]);hi=Math.Max(hi,mesh.Positions[(q+j)*3+1]);}
                if(lo<cut&&hi>cut){for(int j=1;j<4;j++)if(mesh.Colors[q+j]!=mesh.Colors[q])throw new ArgumentException("nonuniform-packed-crossing");crossings++;}
            }
            limits.Admit(n+2L*crossings,mesh.Indices.LongLength+6L*crossings);
            var split=SharedUpperSplit.Build(mesh,cut);var p=new Float3[split.VertexCount];var normal=new Float3[p.Length];var colors=new Rgba32[p.Length];
            for(int i=0;i<p.Length;i++){p[i]=new Float3(split.Position(i,0)*.1f,split.Position(i,1)*.1f,split.Position(i,2)*.1f);normal[i]=Normal(split.Normal(i));uint c=split.Color(i);colors[i]=new Rgba32((byte)c,(byte)(c>>8),(byte)(c>>16),(byte)(c>>24));}
            return new PreparedMesh(p,normal,colors,new[]{split.UpperIndices,split.BaseIndices},Digest(Policy+"/canonical/"+lod+"/"+sourceIdentity),lod,crossings,true);
        }
        static short Local(float value,int factor,int anchor)
        {
            if(!Finite(value))throw new ArgumentException("nonfinite-position");
            double cell=value/(double)(.1f*factor),rounded=Math.Round(cell);
            if(Math.Abs(cell-rounded)>.001||rounded<int.MinValue||rounded>int.MaxValue)throw new ArgumentException("off-grid-position");
            long local=checked((long)rounded*factor-anchor);if(local<short.MinValue||local>short.MaxValue)throw new ArgumentException("local-short-range");return (short)local;
        }
        // Whole completed LOD only; input chunks must share the caller's immutable composite revision.
        public static PreparedMesh PrepareLower(IReadOnlyList<MeshData> chunks,int lod,string compositeIdentity,UploadLimits limits)
        {
            int factor=Factor(lod),cut=Cut(lod);Identity(compositeIdentity);
            if(chunks==null||limits==null||chunks.Count>MaxChunks)throw new ArgumentException("chunk-limit");
            long nv=0,ni=0;var ids=new StringBuilder(Policy+"/lower/"+lod+"/"+compositeIdentity);
            for(int k=0;k<chunks.Count;k++){var m=chunks[k]??throw new ArgumentException("null-chunk");Identity(m.Identity);if(m.VertexCount%4!=0||m.IndexCount!=m.VertexCount/4*6)throw new ArgumentException("lower-quad-layout");nv+=m.VertexCount;ni+=m.IndexCount;ids.Append('/').Append(m.Identity.Length).Append(':').Append(m.Identity);}
            limits.Admit(nv,ni);var p=new short[checked((int)nv*3)];var n=new byte[(int)nv];var c=new uint[(int)nv];var indices=new int[(int)ni];int vi=0,ii=0;
            for(int k=0;k<chunks.Count;k++)
            {
                var m=chunks[k];for(int i=0;i<m.VertexCount;i++)
                {Float3 a=m.Vertices[i];p[(vi+i)*3]=Local(a.x,factor,AnchorX);p[(vi+i)*3+1]=Local(a.y,factor,AnchorY);p[(vi+i)*3+2]=Local(a.z,factor,AnchorZ);n[vi+i]=Normal(m.Normals[i]);var color=m.Colors[i];Packed(color.a);c[vi+i]=(uint)(color.r|color.g<<8|color.b<<16|color.a<<24);if(p[(vi+i)*3+1]>cut)throw new ArgumentException("upper-owned-vertex");}
                for(int i=0;i<m.IndexCount;i++){int index=m.Indices[i];if(index<0||index>=m.VertexCount)throw new ArgumentException("lower-index");indices[ii++]=vi+index;}vi+=m.VertexCount;
            }
            for(int q=0;q<vi;q+=4){Quad(p,n,c,indices,q);if(n[q]==3&&p[q*3+1]==cut)throw new ArgumentException("upper-owned-cut-face");}
            var positions=new Float3[vi];var normals=new Float3[vi];var colors=new Rgba32[vi];
            for(int i=0;i<vi;i++){positions[i]=new Float3(p[i*3]*.1f,p[i*3+1]*.1f,p[i*3+2]*.1f);normals[i]=Normal(n[i]);uint color=c[i];colors[i]=new Rgba32((byte)color,(byte)(color>>8),(byte)(color>>16),(byte)(color>>24));}
            return new PreparedMesh(positions,normals,colors,new[]{indices},Digest(ids.ToString()),lod,0,false);
        }
    }
}
