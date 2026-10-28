using System;
using System.Collections.Generic;
using AshenVoxel.WorldScale;

// Isolated LOD0 cache transformation. Existing vertices are shared by upper
// and authored-base submeshes; only crossing-edge vertices are appended.
// Original material/RGB/AO and normal attributes are preserved exactly.
static class SharedUpperSplit
{
    public sealed class Result
    {
        public readonly PrototypeMeshData Original;
        public readonly short[] ExtraPositions;
        public readonly byte[] ExtraNormals;
        public readonly uint[] ExtraColors;
        public readonly int[] UpperIndices,BaseIndices;
        public readonly int CrossingQuads,Seam;
        public int VertexCount=>Original.VertexCount+ExtraColors.Length;
        public Result(PrototypeMeshData original,short[] p,byte[] n,uint[] c,int[] upper,int[] lower,int crossings,int seam)
        {Original=original;ExtraPositions=p;ExtraNormals=n;ExtraColors=c;UpperIndices=upper;BaseIndices=lower;CrossingQuads=crossings;Seam=seam;}
        public short Position(int vertex,int axis)=>vertex<Original.VertexCount?Original.Positions[vertex*3+axis]:ExtraPositions[(vertex-Original.VertexCount)*3+axis];
        public byte Normal(int vertex)=>vertex<Original.VertexCount?Original.Normals[vertex]:ExtraNormals[vertex-Original.VertexCount];
        public uint Color(int vertex)=>vertex<Original.VertexCount?Original.Colors[vertex]:ExtraColors[vertex-Original.VertexCount];
    }
    public static Result Build(PrototypeMeshData mesh,int seam=30)
    {
        if(mesh==null||seam<short.MinValue||seam>short.MaxValue||mesh.VertexCount%4!=0||mesh.Indices.Length!=mesh.VertexCount/4*6)throw new ArgumentException("Bounded quad mesh required");
        int upperCount=0,baseCount=0,crossings=0;
        int Side(int q)
        {
            int lo=int.MaxValue,hi=int.MinValue;for(int i=0;i<4;i++){int y=mesh.Positions[(q+i)*3+1];lo=Math.Min(lo,y);hi=Math.Max(hi,y);}
            if(lo==hi&&lo==seam)return mesh.Normals[q]==2?-1:1; // Horizontal face ownership, not plane location alone.
            if(hi<=seam)return -1;if(lo>=seam)return 1;return 0;
        }
        for(int q=0;q<mesh.VertexCount;q+=4)
        {
            int side=Side(q);if(side>=0)upperCount+=6;if(side<=0)baseCount+=6;
            if(side==0)
            {
                // Actual fine-cell greedy quads only cross an integer seam
                // after merging uniform AO. Do not interpolate a crease away.
                for(int i=1;i<4;i++)if(mesh.Colors[q+i]!=mesh.Colors[q]||mesh.Normals[q+i]!=mesh.Normals[q])throw new InvalidOperationException("Nonuniform crossing quad requires a different seam or explicit shading reconstruction");
                crossings++;
            }
        }
        var upper=new int[upperCount];var lower=new int[baseCount];var ep=new short[crossings*6];var en=new byte[crossings*2];var ec=new uint[crossings*2];
        int ui=0,li=0,extra=0;var original=new int[4];var intersections=new int[4];var up=new int[4];var down=new int[4];
        short Pos(int v,int d)=>v<mesh.VertexCount?mesh.Positions[v*3+d]:ep[(v-mesh.VertexCount)*3+d];
        void Triangulate(int[] poly,int count,byte normal,int[] target,ref int at)
        {
            if(count!=4)throw new InvalidOperationException("Expected clipped axial quad");
            int a=poly[0],b=poly[1],c=poly[2],axis=normal/2;
            long ax=Pos(b,0)-Pos(a,0),ay=Pos(b,1)-Pos(a,1),az=Pos(b,2)-Pos(a,2),bx=Pos(c,0)-Pos(a,0),by=Pos(c,1)-Pos(a,1),bz=Pos(c,2)-Pos(a,2);
            long cross=axis==0?ay*bz-az*by:axis==1?az*bx-ax*bz:ax*by-ay*bx;bool forward=(normal%2==0?cross:-cross)>0;
            if(cross==0)throw new InvalidOperationException("Degenerate clipping");
            for(int triangle=0;triangle<2;triangle++){int second=triangle+1,third=triangle+2;target[at++]=poly[0];target[at++]=poly[forward?second:third];target[at++]=poly[forward?third:second];}
        }
        for(int q=0;q<mesh.VertexCount;q+=4)
        {
            int side=Side(q),at=q/4*6;
            if(side!=0)
            {
                var target=side>0?upper:lower;int targetAt=side>0?ui:li;Array.Copy(mesh.Indices,at,target,targetAt,6);if(side>0)ui+=6;else li+=6;continue;
            }
            for(int i=0;i<4;i++){original[i]=q+i;intersections[i]=-1;}
            int crossingsHere=0;
            for(int i=0;i<4;i++)
            {
                int a=original[i],b=original[(i+1)%4],ya=Pos(a,1),yb=Pos(b,1);
                if((ya<seam&&yb>seam)||(ya>seam&&yb<seam))
                {
                    if(Pos(a,0)!=Pos(b,0)||Pos(a,2)!=Pos(b,2))throw new InvalidOperationException("Non-axial edge");
                    int v=mesh.VertexCount+extra;ep[extra*3]=Pos(a,0);ep[extra*3+1]=(short)seam;ep[extra*3+2]=Pos(a,2);en[extra]=mesh.Normals[q];ec[extra]=mesh.Colors[q];extra++;intersections[i]=v;crossingsHere++;
                }
            }
            if(crossingsHere!=2)throw new InvalidOperationException("Crossing quad must have two vertical edges");
            int un=0,dn=0;
            for(int i=0;i<4;i++)
            {
                int v=original[i];if(Pos(v,1)>seam)up[un++]=v;else down[dn++]=v;
                if(intersections[i]>=0){up[un++]=intersections[i];down[dn++]=intersections[i];}
            }
            Triangulate(up,un,mesh.Normals[q],upper,ref ui);Triangulate(down,dn,mesh.Normals[q],lower,ref li);
        }
        if(ui!=upper.Length||li!=lower.Length||extra!=ec.Length)throw new InvalidOperationException("Partition count mismatch");
        return new Result(mesh,ep,en,ec,upper,lower,crossings,seam);
    }
}
