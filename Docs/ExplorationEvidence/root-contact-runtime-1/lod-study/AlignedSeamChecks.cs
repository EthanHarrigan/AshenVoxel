using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using AshenVoxel.WorldScale;

static class AlignedSeamChecks
{
    static int Seam=30;static long assertions;static string context;
    static void Check(bool yes,string message){assertions++;if(!yes)throw new Exception(context+": "+message);}
    static string Digest(PrototypeMeshData m)
    {
        using var hash=IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(MemoryMarshal.AsBytes(m.Positions.AsSpan()));hash.AppendData(m.Normals);hash.AppendData(MemoryMarshal.AsBytes(m.Colors.AsSpan()));hash.AppendData(MemoryMarshal.AsBytes(m.Indices.AsSpan()));return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }
    static void Bounds(PrototypeMeshData m,int q,out int lo,out int hi)
    {lo=short.MaxValue;hi=short.MinValue;for(int k=0;k<4;k++){lo=Math.Min(lo,m.Positions[(q+k)*3+1]);hi=Math.Max(hi,m.Positions[(q+k)*3+1]);}}
    static long Winding(int a,int b,int c,Func<int,int,short> position,byte normal)
    {
        long x=position(b,0)-position(a,0),y=position(b,1)-position(a,1),z=position(b,2)-position(a,2),u=position(c,0)-position(a,0),v=position(c,1)-position(a,1),w=position(c,2)-position(a,2);
        long dot=normal/2==0?y*w-z*v:normal/2==1?z*u-x*w:x*v-y*u;return normal%2==0?dot:-dot;
    }
    static HashSet<(byte normal,int plane,int along)> Contour(PrototypeMeshData mesh,bool above)
    {
        var result=new HashSet<(byte,int,int)>();
        for(int q=0;q<mesh.VertexCount;q+=4)
        {
            byte n=mesh.Normals[q];if(n/2==1)continue;Bounds(mesh,q,out int lo,out int hi);
            if(above?!(lo<=Seam&&hi>Seam):!(lo<Seam&&hi>=Seam))continue;
            int axis=n/2,along=axis==0?2:0,min=int.MaxValue,max=int.MinValue;
            for(int k=0;k<4;k++){min=Math.Min(min,mesh.Positions[(q+k)*3+along]);max=Math.Max(max,mesh.Positions[(q+k)*3+along]);}
            for(int unit=min;unit<max;unit++)result.Add((n,mesh.Positions[q*3+axis],unit));
        }
        return result;
    }
    static HashSet<int> VerifyClipped(PrototypeMeshData original,int q,SharedUpperSplit.Result split,int[] indices,int start,bool above)
    {
        byte normal=original.Normals[q];int axis=normal/2,along=axis==0?2:0;Check(axis!=1,"crossing must be vertical axial face");
        Bounds(original,q,out int low,out int high);int a0=int.MaxValue,a1=int.MinValue;
        for(int k=0;k<4;k++){a0=Math.Min(a0,original.Positions[(q+k)*3+along]);a1=Math.Max(a1,original.Positions[(q+k)*3+along]);}
        int y0=above?Seam:low,y1=above?high:Seam,plane=original.Positions[q*3+axis];
        var expected=new HashSet<(int y,int a)>{(y0,a0),(y0,a1),(y1,a0),(y1,a1)};var actual=new HashSet<(int y,int a)>();
        var vertices=new HashSet<int>();var first=new HashSet<int>();var second=new HashSet<int>();long area2=0;
        for(int t=0;t<2;t++)
        {
            for(int k=0;k<3;k++)
            {
                int vertex=indices[start+t*3+k];Check(vertex>=0&&vertex<split.VertexCount,"valid clipped vertex index");vertices.Add(vertex);(t==0?first:second).Add(vertex);
                Check(split.Position(vertex,axis)==plane,"clipped vertex remains on original face plane");actual.Add((split.Position(vertex,1),split.Position(vertex,along)));
                Check(split.Normal(vertex)==normal&&split.Color(vertex)==original.Colors[q],"clipped normal/RGB/material/AO preserved exactly");
            }
            long area=Winding(indices[start+t*3],indices[start+t*3+1],indices[start+t*3+2],split.Position,normal);Check(area>0,"nondegenerate outward triangle winding");area2+=area;
        }
        Check(actual.SetEquals(expected)&&vertices.Count==4,"clipped triangles use exactly the four expected rectangle corners");
        Check(area2==2L*(y1-y0)*(a1-a0),"clipped rectangle area exact");first.IntersectWith(second);Check(first.Count==2,"two triangles share one edge");var diagonal=first.ToArray();
        Check(split.Position(diagonal[0],1)!=split.Position(diagonal[1],1)&&split.Position(diagonal[0],along)!=split.Position(diagonal[1],along),"shared triangle edge is a diagonal; no overlapping halves or uncovered corner");
        var seamVertices=new HashSet<int>(vertices.Where(v=>split.Position(v,1)==Seam));Check(seamVertices.Count==2,"one complete seam edge per clipped rectangle");return seamVertices;
    }
    sealed class Row
    {
        public int prototype_id,lod,source_vertices,source_indices,crossing_quads,nonuniform_crossings,nonuniform_rgb_crossings,nonuniform_material_crossings,nonuniform_ao_crossings,nonuniform_normal_crossings,appended_vertices,upper_indices,base_indices,horizontal_up_at_seam,horizontal_down_at_seam,upper_contour_segments,base_contour_segments;
        public long? added_vertex_bytes_28,added_index_bytes_32,added_gpu_arithmetic_bytes,split_array_bytes;
        public long original_index_bytes;
        public int seam_cells,y_min,y_max,y_lattice_gcd,y_lattice_residue,distinct_y_coordinates;
        public bool seam_on_observed_y_lattice;public int[] y_residues_mod32,y_residues_mod48,y_residues_mod64;
        public bool supported,input_unchanged;public string name,failure,source_mesh_sha256;
        public int upper_missing_vs_lod0,upper_added_vs_lod0,base_missing_vs_lod0,base_added_vs_lod0;
    }
    static int Gcd(int a,int b){while(b!=0){int t=a%b;a=b;b=t;}return a;}
    static Row Verify(ExplorationPrototype p,int lod,HashSet<(byte,int,int)> fineUpper,HashSet<(byte,int,int)> fineBase)
    {
        PrototypeMeshData mesh=p.Mesh(lod);context=p.Name+" LOD"+lod;var row=new Row{prototype_id=p.Id,lod=lod,name=context,source_vertices=mesh.VertexCount,source_indices=mesh.Indices.Length,source_mesh_sha256=Digest(mesh),original_index_bytes=mesh.Indices.Length*4L};
        row.seam_cells=Seam;var ys=new SortedSet<int>();for(int v=0;v<mesh.VertexCount;v++)ys.Add(mesh.Positions[v*3+1]);row.y_min=ys.Min;row.y_max=ys.Max;row.distinct_y_coordinates=ys.Count;
        foreach(int y in ys)row.y_lattice_gcd=Gcd(row.y_lattice_gcd,y-row.y_min);
        row.y_lattice_residue=row.y_lattice_gcd==0?row.y_min:((row.y_min%row.y_lattice_gcd)+row.y_lattice_gcd)%row.y_lattice_gcd;
        row.seam_on_observed_y_lattice=row.y_lattice_gcd>0&&(Seam-row.y_min)%row.y_lattice_gcd==0;
        int[] Residues(int modulus)=>ys.Select(y=>((y%modulus)+modulus)%modulus).Distinct().OrderBy(y=>y).ToArray();
        row.y_residues_mod32=Residues(32);row.y_residues_mod48=Residues(48);row.y_residues_mod64=Residues(64);
        var upperContour=Contour(mesh,true);var baseContour=Contour(mesh,false);row.upper_contour_segments=upperContour.Count;row.base_contour_segments=baseContour.Count;
        row.upper_missing_vs_lod0=fineUpper.Except(upperContour).Count();row.upper_added_vs_lod0=upperContour.Except(fineUpper).Count();row.base_missing_vs_lod0=fineBase.Except(baseContour).Count();row.base_added_vs_lod0=baseContour.Except(fineBase).Count();
        for(int q=0;q<mesh.VertexCount;q+=4)
        {
            Bounds(mesh,q,out int lo,out int hi);
            if(lo<Seam&&hi>Seam)
            {
                row.crossing_quads++;bool rgb=false,material=false,ao=false,normal=false;
                for(int k=1;k<4;k++){rgb|=(mesh.Colors[q+k]&0xffffff)!=(mesh.Colors[q]&0xffffff);material|=(mesh.Colors[q+k]>>26)!=(mesh.Colors[q]>>26);ao|=((mesh.Colors[q+k]>>24)&3)!=((mesh.Colors[q]>>24)&3);normal|=mesh.Normals[q+k]!=mesh.Normals[q];}
                if(rgb||material||ao||normal)row.nonuniform_crossings++;if(rgb)row.nonuniform_rgb_crossings++;if(material)row.nonuniform_material_crossings++;if(ao)row.nonuniform_ao_crossings++;if(normal)row.nonuniform_normal_crossings++;
            }
            if(lo==Seam&&hi==Seam){if(mesh.Normals[q]==2)row.horizontal_up_at_seam++;else if(mesh.Normals[q]==3)row.horizontal_down_at_seam++;}
        }
        SharedUpperSplit.Result split;
        try{split=SharedUpperSplit.Build(mesh,Seam);}
        catch(InvalidOperationException e)
        {
            Check(row.nonuniform_crossings>0&&e.Message.Contains("Nonuniform crossing"),"only independently detected nonuniform crossing rejection is expected");row.failure=e.Message;row.input_unchanged=Digest(mesh)==row.source_mesh_sha256;Check(row.input_unchanged,"reject path does not mutate input");return row;
        }
        Check(row.nonuniform_crossings==0,"nonuniform crossings must reject, never interpolate");Check(ReferenceEquals(split.Original,mesh),"original vertex arrays shared");Check(split.CrossingQuads==row.crossing_quads,"independently counted crossings agree");
        int upperAt=0,baseAt=0;var extraSeen=new HashSet<int>();Func<int,int,short> originalPosition=(v,a)=>mesh.Positions[v*3+a];
        for(int q=0;q<mesh.VertexCount;q+=4)
        {
            Bounds(mesh,q,out int lo,out int hi);bool crossing=lo<Seam&&hi>Seam;int sourceStart=q/4*6;
            for(int t=0;t<2;t++)
            {int a=mesh.Indices[sourceStart+t*3],b=mesh.Indices[sourceStart+t*3+1],c=mesh.Indices[sourceStart+t*3+2];Check(a>=q&&a<q+4&&b>=q&&b<q+4&&c>=q&&c<q+4,"source quad owns its triangle vertices");Check(Winding(a,b,c,originalPosition,mesh.Normals[q])>0,"source triangle winding verified");}
            if(crossing)
            {
                var top=VerifyClipped(mesh,q,split,split.UpperIndices,upperAt,true);var bottom=VerifyClipped(mesh,q,split,split.BaseIndices,baseAt,false);
                Check(top.SetEquals(bottom),"upper/base share exact seam indices, not merely rounded positions");foreach(int v in top){Check(v>=mesh.VertexCount,"crossing seam uses appended vertices");Check(extraSeen.Add(v),"crossing seam vertices belong to one source quad");}
                upperAt+=6;baseAt+=6;
            }
            else
            {
                bool upper=lo>=Seam;if(lo==hi&&lo==Seam)upper=mesh.Normals[q]!=2;
                var target=upper?split.UpperIndices:split.BaseIndices;int at=upper?upperAt:baseAt;
                for(int i=0;i<6;i++)Check(target[at+i]==mesh.Indices[sourceStart+i],"uncut quad retains exact index order and vertex attributes");
                for(int i=0;i<6;i++)Check(upper?split.Position(target[at+i],1)>=Seam:split.Position(target[at+i],1)<=Seam,"submesh stays on assigned side of seam");
                if(lo==hi&&lo==Seam)Check((mesh.Normals[q]==2&&!upper)||(mesh.Normals[q]==3&&upper),"horizontal seam cap has one normal-directed owner");
                if(upper)upperAt+=6;else baseAt+=6;
            }
        }
        Check(upperAt==split.UpperIndices.Length&&baseAt==split.BaseIndices.Length,"every output triangle owned exactly once");
        Check(extraSeen.Count==split.ExtraColors.Length&&split.ExtraColors.Length==2*row.crossing_quads,"no missing or orphan appended seam vertices");
        row.input_unchanged=Digest(mesh)==row.source_mesh_sha256;Check(row.input_unchanged,"all input arrays byte-identical after split");row.supported=true;row.appended_vertices=split.ExtraColors.Length;row.upper_indices=upperAt;row.base_indices=baseAt;
        row.added_vertex_bytes_28=row.appended_vertices*28L;row.added_index_bytes_32=(upperAt+(long)baseAt-mesh.Indices.Length)*4;row.added_gpu_arithmetic_bytes=row.added_vertex_bytes_28+row.added_index_bytes_32;
        row.split_array_bytes=(upperAt+(long)baseAt)*4+row.appended_vertices*11L;row.original_index_bytes=mesh.Indices.Length*4L;return row;
    }
    static void RejectTests()
    {
        context="adversarial synthetic crossing";
        foreach(byte normal in new byte[]{0,1,4,5})foreach(int variant in new[]{0,1,2})
        {
            short[] positions=normal/2==0?new short[]{0,20,-2,0,40,-2,0,40,2,0,20,2}:new short[]{-2,20,0,2,20,0,2,40,0,-2,40,0};
            var normals=Enumerable.Repeat(normal,4).ToArray();var colors=Enumerable.Repeat(0x0f885522u,4).ToArray();
            if(variant==0)colors[1]^=1u;else if(variant==1)colors[2]^=1u<<24;else normals[3]=(byte)(normal^1);
            var mesh=new PrototypeMeshData(positions,normals,colors,new[]{0,1,2,0,2,3});string hash=Digest(mesh);bool rejected=false;
            try{SharedUpperSplit.Build(mesh,Seam);}catch(InvalidOperationException e){rejected=e.Message.Contains("Nonuniform crossing");}
            Check(rejected,"varying RGB, packed material/AO or normals explicitly rejected");Check(hash==Digest(mesh),"failed build leaves source attributes unchanged");
        }
    }
    static void Main(string[] args)
    {
        RejectTests();var rows=new List<Row>();var counts=new Dictionary<int,int>();
        using(var reader=new ExplorationPrototypeReader(File.ReadAllBytes(args[0])))while(reader.TryRead(out var p))
        {
            if(p.Id==10||p.Id==11)
            {
                counts.Add(p.Id,(p.LodCount-2)*3);
                foreach(int seam in new[]{32,48,64})
                {
                    Seam=seam;var upper=Contour(p.Mesh(0),true);var lower=Contour(p.Mesh(0),false);
                    for(int lod=2;lod<p.LodCount;lod++)
                    {
                        var row=Verify(p,lod,upper,lower);rows.Add(row);
                        Console.WriteLine(row.name+" seam"+Seam+": supported="+row.supported+", crossings="+row.crossing_quads+", varying AO="+row.nonuniform_ao_crossings+", Y gcd="+row.y_lattice_gcd+", lattice residue="+row.y_lattice_residue+", extra bytes="+row.added_gpu_arithmetic_bytes);
                    }
                }
            }
            p.ReleaseMeshData();if(p.Id>=11)break;GC.Collect();
        }
        Check(counts.Count==2&&rows.Count==counts.Values.Sum(),"every requested aligned seam and coarse LOD of both trees visited");
        File.WriteAllText(args[1],JsonSerializer.Serialize(new{status="PASS",assertions,seams_cells=new[]{32,48,64},mechanically_supported_all_selected_cases=rows.All(r=>r.supported),scope="Supplemental unchanged-helper CPU analysis for packed LOD2/3/4 at seams32/48/64; same independent rectangle/index/attribute/winding/seam checks. Y residues are observed packed coordinates. Contour differences compare original authored geometry with original LOD0 at the same seam, not any proposed root base. Original seam30 evidence unchanged; no native/runtime/newroot pairing claim.",rows},new JsonSerializerOptions{WriteIndented=true,IncludeFields=true}));
        Console.WriteLine("PASS "+assertions+" independent aligned-seam assertions");
    }
}
