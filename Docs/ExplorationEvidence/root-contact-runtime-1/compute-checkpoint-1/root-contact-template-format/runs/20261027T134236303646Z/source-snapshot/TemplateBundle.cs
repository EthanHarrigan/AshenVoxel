using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using AshenVoxel.Core;
using RootContactPrototype;
using RootContactInteraction;

namespace RootContactTemplateCache
{
    // Replaceable companion cache, not an AVPM or gameplay-save format.
    // Version 1 rejects unsupported versions; fallback is the complete original tree.
    public readonly struct FixedRecord
    {
        public readonly Int3 Cell; public readonly MaterialId Material;
        public readonly uint Color; public readonly int Rank, Index;
        internal FixedRecord(Int3 cell, MaterialId material, uint color, int rank, int index)
        { Cell=cell; Material=material; Color=color; Rank=rank; Index=index; }
    }
    public sealed class LodTemplate
    {
        readonly FixedRecord[] samples;
        public readonly int Factor, Cut;
        public int Count=>samples.Length;
        public FixedRecord At(int index)=>samples[index];
        internal LodTemplate(int factor,int cut,FixedRecord[] records)
        { Factor=factor;Cut=cut;samples=records; }
    }
    public sealed class TemplateBundle
    {
        public const int FormatVersion=1, MaximumPayloadBytes=2*1024*1024;
        public const string PolicyIdentity="root-companion-cache-v1;models10,11;source470x978x470;anchor235,4,235;root-v2;ownership<=29;context29..32;cuts30,32,36,60,124;canonical-order-v1";
        readonly Int3[] order; readonly LodTemplate[] lods;
        public readonly int ModelId,Seed;
        public readonly string FileIdentity,PackIdentity,CanonicalSourceIdentity,RootSourceIdentity;
        public readonly Template Root;
        public readonly UpperContext Context;
        public int OrderCount=>order.Length;
        public Int3 OrderedChunk(int index)=>order[index];
        public int LodCount=>lods.Length;
        public LodTemplate Lod(int index)=>lods[index];
        // Serialized retained data is separate from instantiated CLR object/dictionary overhead.
        public readonly int SerializedBytes;
        TemplateBundle(int id,int seed,string file,string pack,string rootIdentity,string canonical,
            Template root,UpperContext context,Int3[] ranks,LodTemplate[] lower,int serialized)
        {ModelId=id;Seed=seed;FileIdentity=file;PackIdentity=pack;RootSourceIdentity=rootIdentity;
            CanonicalSourceIdentity=canonical;Root=root;Context=context;order=ranks;lods=lower;SerializedBytes=serialized;}

        static void Require(bool value,string why){if(!value)throw new InvalidDataException(why);}
        static bool Hex64(string s)
        {if(s==null||s.Length!=64)return false;foreach(char c in s)if(!(c>='0'&&c<='9'||c>='a'&&c<='f'))return false;return true;}
        static string Hex(byte[] data){var b=new StringBuilder(data.Length*2);foreach(byte v in data)b.Append(v.ToString("x2"));return b.ToString();}
        static string Digest(byte[] data){using var h=SHA256.Create();return Hex(h.ComputeHash(data));}
        static byte[] ReadExact(BinaryReader reader,int bytes)
        {byte[] value=reader.ReadBytes(bytes);Require(value.Length==bytes,"Truncated");return value;}
        static int Count(BinaryReader r,int maximum,int recordBytes,string name,bool nonempty=false)
        {
            int n=r.ReadInt32();Require(n>=(nonempty?1:0)&&n<=maximum,name+"Count");
            Require((long)n*recordBytes<=r.BaseStream.Length-r.BaseStream.Position,name+"Length");return n;
        }
        static Int3 Cell16(BinaryReader r)=>new Int3(r.ReadInt16(),r.ReadInt16(),r.ReadInt16());

        // expectedFileSha256 is a trusted manifest pin, not a digest read from this file.
        // This binds geometry to reviewed authoring provenance as well as detecting corruption.
        // source must be owned by the caller; this method never closes it.
        public static bool TryRead(Stream source,string expectedFileSha256,string expectedPackSha256,
            int expectedModelId,out TemplateBundle output,out string failure)
        {
            output=null;failure="";
            if(source==null||!Hex64(expectedFileSha256)||!Hex64(expectedPackSha256)||
                expectedModelId!=10&&expectedModelId!=11){failure="InvalidExpectedIdentity";return false;}
            try
            {
                Require(source.CanRead,"UnreadableStream");
                using var reader=new BinaryReader(source,Encoding.UTF8,true);
                byte[] header=ReadExact(reader,16);
                using var hr=new BinaryReader(new MemoryStream(header,false));
                Require(Encoding.ASCII.GetString(hr.ReadBytes(8))=="AVRC0001","Magic");
                Require(hr.ReadInt32()==FormatVersion,"Version");int length=hr.ReadInt32();
                Require(length>0&&length<=MaximumPayloadBytes,"PayloadLimit");
                byte[] hash=ReadExact(reader,32),payload=ReadExact(reader,length);
                Require(source.ReadByte()==-1,"TrailingBytes");
                Require(Digest(payload)==Hex(hash),"PayloadChecksum");
                using(var sha=SHA256.Create())
                {
                    sha.TransformBlock(header,0,header.Length,null,0);sha.TransformBlock(hash,0,hash.Length,null,0);
                    sha.TransformFinalBlock(payload,0,payload.Length);Require(Hex(sha.Hash)==expectedFileSha256,"FileIdentity");
                }
                using var r=new BinaryReader(new MemoryStream(payload,false));
                int id=r.ReadInt32(),seed=r.ReadInt32();
                Require(id==expectedModelId&&seed==(id==10?714103:271828),"ModelSeed");
                Require(r.ReadInt32()==470&&r.ReadInt32()==978&&r.ReadInt32()==470,"Dimensions");
                Require(r.ReadInt32()==235&&r.ReadInt32()==4&&r.ReadInt32()==235,"Anchor");
                string pack=Hex(ReadExact(r,32)),rootSource=Hex(ReadExact(r,32)),canonical=Hex(ReadExact(r,32));
                Require(pack==expectedPackSha256,"PackIdentity");
                Require(r.ReadInt32()==2&&r.ReadInt32()==29,"RootPolicy");
                int columnsCount=Count(r,Policy.MaxColumns,8,"Columns",true),sourceCells=0;
                var columns=new Column[columnsCount];
                for(int i=0;i<columns.Length;i++)
                {
                    int x=r.ReadInt16(),z=r.ReadInt16();int n=Count(r,96,6,"ColumnCells",true);
                    sourceCells=checked(sourceCells+n);Require(sourceCells<=Policy.MaxOccupied,"RootCellLimit");
                    var ys=new short[n];var colors=new uint[n];
                    for(int j=0;j<n;j++){ys[j]=r.ReadInt16();colors[j]=r.ReadUInt32();}
                    columns[i]=new Column(x,z,ys,colors);
                }
                var root=new Template(columns,29,"fixture-sha256:"+rootSource);
                int contextCount=Count(r,UpperContext.MaxCells,10,"Context",true);var contextCells=new WoodCell[contextCount];
                for(int i=0;i<contextCells.Length;i++)contextCells[i]=new WoodCell(Cell16(r),r.ReadUInt32());
                var context=new UpperContext(contextCells,"canonical-file-sha256:"+canonical);
                // Every root row29/30 cell must match complete canonical context. Conversely
                // every canonical row29/30 wood cell must appear in the root template.
                var seam=new Dictionary<Int3,uint>();
                foreach(var c in columns)for(int j=0;j<c.Count;j++)if(c.Y(j)>=29)
                {var p=new Int3(c.X,c.Y(j),c.Z);Require(context.TryColor(p,out uint tint)&&tint==c.Tint(j),"RootContextMismatch");seam.Add(p,tint);}
                foreach(var c in contextCells)if(c.Position.y<=30)Require(seam.TryGetValue(c.Position,out uint tint)&&tint==c.Color,"MissingRootContext");
                int rankCount=Count(r,8192,6,"Ranks",true);var ranks=new Int3[rankCount];var rankSet=new HashSet<Int3>();
                for(int i=0;i<rankCount;i++)
                {var p=Cell16(r);Require(p.x>=0&&p.x<30&&p.y>=0&&p.y<62&&p.z>=0&&p.z<30&&rankSet.Add(p),"RankDomainOrDuplicate");ranks[i]=p;}
                Require(r.ReadInt32()==5,"LodCount");var lods=new LodTemplate[5];int[] factors={1,2,4,16,64},cuts={30,32,36,60,124};
                for(int k=0;k<5;k++)
                {
                    int factor=r.ReadInt32(),cut=r.ReadInt32();Require(factor==factors[k]&&cut==cuts[k],"LodLattice");
                    int count=Count(r,65536,17,"FixedSamples");var samples=new FixedRecord[count];var unique=new HashSet<Int3>();
                    for(int i=0;i<count;i++)
                    {
                        var p=Cell16(r);var material=(MaterialId)r.ReadByte();uint color=r.ReadUInt32();int rank=r.ReadInt32(),index=r.ReadUInt16();
                        Require(p.x>=0&&p.x<(470+factor-1)/factor&&p.z>=0&&p.z<(470+factor-1)/factor&&p.y>=0&&p.y<=(cut+4)/factor&&unique.Add(p),"SampleDomainOrDuplicate");
                        Require(material!=MaterialId.Air&&MaterialCatalog.IsValid(material)&&color>>24==255&&rank>=0&&rank<rankCount&&index<4096,"SampleAttributes");
                        samples[i]=new FixedRecord(p,material,color,rank,index);
                    }
                    lods[k]=new LodTemplate(factor,cut,samples);
                }
                Require(r.BaseStream.Position==r.BaseStream.Length,"PayloadTrailingBytes");
                output=new TemplateBundle(id,seed,expectedFileSha256,pack,rootSource,canonical,root,context,ranks,lods,length+48);return true;
            }
            catch(Exception e){failure=e is InvalidDataException?e.Message:e.GetType().Name;output=null;return false;}
        }
    }
}
