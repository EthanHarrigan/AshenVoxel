using System;
using System.IO;
using System.Reflection;
using System.Text.Json;
using AshenVoxel.Core;
using AshenVoxel.WorldScale;
using RootContactArrayMesher;
using RootContactUploadPrototype;
static class Checks
{
    static int checks,cases;static readonly UploadLimits limits=new UploadLimits(10000,15000,400000);
    static void A(bool yes,string message){checks++;if(!yes)throw new Exception(message);}
    static void Reject(Action action,string message){bool rejected=false;try{action();}catch(ArgumentException){rejected=true;}catch(InvalidOperationException){rejected=true;}A(rejected,message);}
    static PrototypeMeshData Data(short[] p,byte[] n,uint[] c,int[] t)=>(PrototypeMeshData)Activator.CreateInstance(typeof(PrototypeMeshData),BindingFlags.Instance|BindingFlags.NonPublic,null,new object[]{p,n,c,t},null);
    static MeshData Fake(Float3[] p,Float3[] n,Rgba32[] c,int[] t)=>(MeshData)Activator.CreateInstance(typeof(MeshData),BindingFlags.Instance|BindingFlags.NonPublic,null,new object[]{"fixture",p,n,c,t,p.Length,t.Length},null);
    static PrototypeMeshData Quad(int normal,int y0,int y1,bool varied=false)
    {
        int axis=normal/2,u=(axis+1)%3,v=(axis+2)%3;var p=new short[12];var n=new byte[4];var c=new uint[4];
        for(int i=0;i<4;i++)
        {p[i*3+axis]=(short)(axis==1?y0:3);p[i*3+u]=(short)(i==1||i==2?7:-2);p[i*3+v]=(short)(i>=2?5:-3);if(axis!=1)p[i*3+1]=(short)(p[i*3+1]<0?y0:y1);n[i]=(byte)normal;c[i]=0x1B332211u;if(varied)c[i]=(uint)((24+i)<<24)|0x00332211;}
        int[] t=normal%2==0?new[]{0,1,2,0,2,3}:new[]{0,2,1,0,3,2};return Data(p,n,c,t);
    }
    static long TwiceArea(PreparedMesh p,int part)
    {
        long total=0;var t=p.Indices(part);
        for(int i=0;i<t.Length;i+=3)
        {
            var a=p.Positions[t[i]];var b=p.Positions[t[i+1]];var c=p.Positions[t[i+2]];var n=p.Normals[t[i]];
            int axis=n.x!=0?0:n.y!=0?1:2,u=(axis+1)%3,v=(axis+2)%3;
            long ax=(long)Math.Round(a[u]*10),ay=(long)Math.Round(a[v]*10),bx=(long)Math.Round(b[u]*10),by=(long)Math.Round(b[v]*10),cx=(long)Math.Round(c[u]*10),cy=(long)Math.Round(c[v]*10);
            long area=(bx-ax)*(cy-ay)-(by-ay)*(cx-ax);A(area*n[axis]>0,"outward split triangle");total+=Math.Abs(area);
        }return total;
    }
    static void Canonical()
    {
        for(int lod=0;lod<5;lod++)for(int normal=0;normal<6;normal++)foreach(int type in new[]{0,1,2,3})
        {
            int cut=UploadPreparation.Cut(lod),lo=type==0?cut-5:type==1?cut+1:type==2?cut:cut-3,hi=type==3?cut+4:lo+2;
            var source=Quad(normal,lo,hi,type!=3);var copy=(short[])source.Positions.Clone();var output=UploadPreparation.PrepareCanonical(source,lod,"pack/10/lod"+lod,limits);cases++;
            bool cross=normal/2!=1&&type==3;A(output.PartCount==2&&output.CanonicalPartition,"canonical two parts");A(output.CrossingQuads==(cross?1:0),"crossings");A(output.VertexCount==(cross?6:4),"one vbo plus2");A(output.IndexCount==(cross?12:6),"index union");
            int axis=normal/2,u=(axis+1)%3,v=(axis+2)%3;long expected=2L*Math.Abs(source.Positions[3+u]-source.Positions[u])*Math.Abs(source.Positions[6+v]-source.Positions[3+v]);
            A(TwiceArea(output,0)+TwiceArea(output,1)==expected,"exact union area");
            for(int i=0;i<4;i++){A(output.Positions[i].x==copy[i*3]*.1f&&output.Positions[i].y==copy[i*3+1]*.1f&&output.Positions[i].z==copy[i*3+2]*.1f,"original vertex exact");A(output.Colors[i].a==(byte)(source.Colors[i]>>24)&&output.Colors[i].r==0x11,"packed attributes");}
            for(int part=0;part<2;part++)foreach(int index in output.Indices(part))A(part==0?output.Positions[index].y>=cut*.1f:output.Positions[index].y<=cut*.1f,"part side");
            if(normal==2&&lo==cut)A(output.Triangles(1)==2&&output.Triangles(0)==0,"positive horizontal at cut belongsbase");
            if(normal==3&&lo==cut)A(output.Triangles(0)==2&&output.Triangles(1)==0,"negative horizontal at cut belongsupper");
            A(output.GpuPayloadBytes==28L*output.VertexCount+4*output.IndexCount,"payload accounting");
            source.Positions[0]=999;source.Indices[0]=3;source.Colors[0]=0;A(output.Positions[0].x==copy[0]*.1f&&output.Colors[0].a!=0,"output owns values");
        }
        var mixed=Quad(0,29,31,true);Reject(()=>UploadPreparation.PrepareCanonical(mixed,0,"x",limits),"AO crossing rejects");
        var rgb=Quad(0,29,31);rgb.Colors[1]^=1;Reject(()=>UploadPreparation.PrepareCanonical(rgb,0,"x",limits),"RGB crossing rejects");
        var invalid=Quad(0,29,31);invalid.Indices[5]=99;Reject(()=>UploadPreparation.PrepareCanonical(invalid,0,"x",limits),"invalid indices");
        var overlap=Quad(0,10,20);Array.Copy(new[]{0,1,2,0,1,3},overlap.Indices,6);Reject(()=>UploadPreparation.PrepareCanonical(overlap,0,"x",limits),"overlap triangles reject");
        var material=Quad(0,10,20);material.Colors[0]=0xFF332211;Reject(()=>UploadPreparation.PrepareCanonical(material,0,"x",limits),"legacy or invalid alpha");
        Reject(()=>UploadPreparation.PrepareCanonical(Quad(0,29,31),0,"x",new UploadLimits(4,6,136)),"expansion reservation");
        Reject(()=>UploadPreparation.PrepareCanonical(Quad(0,29,31),5,"x",limits),"unsupported lod");
    }
    static void Lower()
    {
        for(int lod=0;lod<5;lod++)
        {
            int f=UploadPreparation.Factor(lod),gy=(1028+UploadPreparation.Cut(lod))/f-2;
            var coordinate=new Int3(0,gy/16,0);int ly=gy%16;var material=new MaterialId[ChunkInput.HaloCells];var color=new uint[material.Length];int k=1+18*((ly+1)+18*1);material[k]=MaterialId.Wood;color[k]=0xFF123456;
            var input=new ChunkInput(coordinate,.1f*f,(1028+UploadPreparation.Cut(lod))/f,"composite/lod"+lod,material,color);
            var built=ArrayRootMesher.Build(input,new MeshCapacity(6,816),()=>true);A(built.Status==BuildStatus.Complete,"actual arraymesher tinyfixture");
            var mesh=built.Output;var output=UploadPreparation.PrepareLower(new[]{mesh},lod,"revision",limits);cases++;
            A(output.PartCount==1&&!output.CanonicalPartition&&output.VertexCount==24&&output.IndexCount==36,"lower counts");
            for(int i=0;i<mesh.VertexCount;i++)
            {
                for(int d=0;d<3;d++){int anchor=d==1?1028:235;int grid=(int)Math.Round(mesh.Vertices[i][d]/(.1f*f));float expected=(grid*f-anchor)*.1f;A(output.Positions[i][d]==expected,"exact original local transform");A(output.Normals[i][d]==mesh.Normals[i][d],"normal unchanged");}
                var a=mesh.Colors[i];var b=output.Colors[i];A(a.r==b.r&&a.g==b.g&&a.b==b.b&&a.a==b.a,"RGB material AO unchanged");
            }
            for(int i=0;i<mesh.IndexCount;i++)A(output.Indices(0)[i]==mesh.Indices[i],"indices unchanged");
            A(output.Bounds.Minimum.x==-23.5f,"shift notchild transform");
            Reject(()=>UploadPreparation.PrepareLower(new[]{mesh},lod,"revision",new UploadLimits(23,36,10000)),"lower reservation");
            if(lod==0)
            {
                var p=mesh.Vertices.ToArray();p[0].x+=.01f;var offgrid=Fake(p,mesh.Normals.ToArray(),mesh.Colors.ToArray(),mesh.Indices.ToArray());Reject(()=>UploadPreparation.PrepareLower(new[]{offgrid},0,"x",limits),"offgrid reject");
                p=mesh.Vertices.ToArray();p[0].x=float.NaN;var nonfinite=Fake(p,mesh.Normals.ToArray(),mesh.Colors.ToArray(),mesh.Indices.ToArray());Reject(()=>UploadPreparation.PrepareLower(new[]{nonfinite},0,"x",limits),"nonfinite reject");
                p=mesh.Vertices.ToArray();p[0].y=1060*.1f;var upper=Fake(p,mesh.Normals.ToArray(),mesh.Colors.ToArray(),mesh.Indices.ToArray());Reject(()=>UploadPreparation.PrepareLower(new[]{upper},0,"x",limits),"upper owned reject");
                var normals=mesh.Normals.ToArray();normals[0]=new Float3(.5f,0,.5f);var angled=Fake(mesh.Vertices.ToArray(),normals,mesh.Colors.ToArray(),mesh.Indices.ToArray());Reject(()=>UploadPreparation.PrepareLower(new[]{angled},0,"x",limits),"nonaxial reject");
            }
        }
        Reject(()=>UploadPreparation.PrepareLower(new MeshData[129],0,"revision",limits),"chunk cap");
        Reject(()=>UploadPreparation.PrepareLower(Array.Empty<MeshData>(),0,new string('x',4097),limits),"identity cap");
        var empty=UploadPreparation.PrepareLower(Array.Empty<MeshData>(),0,"empty",limits);A(empty.VertexCount==0&&empty.IndexCount==0&&!empty.Bounds.HasVertices,"empty bounded mesh");
    }
    static void Main(string[] args)
    {Canonical();Lower();var result=new{status="PASS",checks,cases,scope="Small synthetic CPU partition/transform tests with actual frozen splitter and finalized ArrayRootMesher. No Unity execution, pack/fulltree loading or native upload."};File.WriteAllText(Path.Combine(args[0],"checks.json"),JsonSerializer.Serialize(result,new JsonSerializerOptions{WriteIndented=true}));Console.WriteLine(JsonSerializer.Serialize(result));}
}
