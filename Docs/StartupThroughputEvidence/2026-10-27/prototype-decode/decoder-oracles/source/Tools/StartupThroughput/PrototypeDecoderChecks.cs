using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.Json;
using AshenVoxel.WorldScale;

static class Program
{
    static int checks;
    static void Check(bool value,string message){if(!value)throw new Exception(message);checks++;}
    static uint OracleCrc(byte[] data)
    {uint crc=0xffffffff;foreach(byte b in data){crc^=b;for(int i=0;i<8;i++)crc=(crc>>1)^((crc&1)!=0?0xedb88320u:0);}return ~crc;}
    static void Var(BinaryWriter w,uint n){while(n>=128){w.Write((byte)((n&127)|128));n>>=7;}w.Write((byte)n);}
    static void Delta(BinaryWriter w,int n)=>Var(w,unchecked((uint)((n<<1)^(n>>31))));
    static byte[] Pack(byte[] raw,bool corruptCrc=false)
    {
        byte[] compressed;using(var s=new MemoryStream()){using(var z=new GZipStream(s,CompressionLevel.Fastest,true))z.Write(raw);compressed=s.ToArray();}
        using var output=new MemoryStream();using var w=new BinaryWriter(output);
        w.Write(Encoding.ASCII.GetBytes("AVPM0004"));w.Write(1);w.Write(compressed.Length);w.Write(raw.Length);w.Write(OracleCrc(raw)^(corruptCrc?1u:0));w.Write(compressed);return output.ToArray();
    }
    static byte[] Raw(int[] origin,int normal,int pattern,uint width,uint height,byte ao,int material=7,uint color=0)
    {
        using var stream=new MemoryStream();using var w=new BinaryWriter(stream);
        w.Write(0);w.Write((byte)0);w.Write((ushort)1);w.Write((byte)'X');
        foreach(float v in new[]{-200f,-200f,-200f,200f,200f,200f})w.Write(v);
        w.Write((byte)1);w.Write(1);w.Write(1);w.Write(new byte[]{19,83,171});
        foreach(int v in origin)Delta(w,v);Var(w,width);Var(w,height);w.Write((byte)(normal|(pattern<<3)));Var(w,color);w.Write(ao);w.Write((byte)material);
        w.Write(0);w.Write(0);w.Write(0);return stream.ToArray();
    }
    static ExplorationPrototype Read(byte[] raw)
    {using var reader=new ExplorationPrototypeReader(Pack(raw));Check(reader.TryRead(out var p)&&!reader.TryRead(out _),"one exact record");return p;}
    static void Reject(byte[] raw,string name)
    {
        bool rejected=false;try{Read(raw);}catch(InvalidDataException){rejected=true;}catch(EndOfStreamException){rejected=true;}
        Check(rejected,name);
    }
    static void Main(string[] args)
    {
        Check(ExplorationPrototypeReader.Crc(Encoding.ASCII.GetBytes("123456789"))==0xcbf43926,"standard CRC32 check value");
        var random=new Random(714103);
        foreach(int length in Enumerable.Range(0,266).Concat(new[]{511,512,513,1023,1024,1025,65535,65536,65537}))
        {var data=new byte[length];random.NextBytes(data);Check(ExplorationPrototypeReader.Crc(data)==OracleCrc(data),"CRC length/tail "+length);}
        int[][] patterns={new[]{0,1,2,0,2,3},new[]{0,2,1,0,3,2},new[]{0,1,3,1,2,3},new[]{0,3,1,1,3,2}};
        int cases=0;
        for(int normal=0;normal<6;normal++)for(int pattern=0;pattern<4;pattern++)for(int sample=0;sample<12;sample++)
        {
            int[] origin={random.Next(-1000,1001),random.Next(-1000,1001),random.Next(-1000,1001)};
            uint width=(uint)random.Next(1,1001),height=(uint)random.Next(1,1001);byte ao=(byte)random.Next(256);int material=random.Next(1,25);
            var p=Read(Raw(origin,normal,pattern,width,height,ao,material));var mesh=p.Mesh(0);int u=(normal/2+1)%3,v=(normal/2+2)%3;
            for(int corner=0;corner<4;corner++)
            {
                for(int axis=0;axis<3;axis++)
                {long expected=origin[axis]+(axis==u&&(corner==1||corner==2)?width:0)+(axis==v&&(corner==2||corner==3)?height:0);Check(mesh.Positions[corner*3+axis]==expected,"corner oracle");}
                Check(mesh.Normals[corner]==normal,"normal oracle");
                Check(mesh.Colors[corner]==(0xab5313u|((uint)((material<<2)|((ao>>(corner*2))&3))<<24)),"material/AO/color oracle");
            }
            Check(mesh.Indices.SequenceEqual(patterns[pattern]),"winding/diagonal oracle");cases++;
        }
        Read(Raw(new[]{-2065,0,0},0,0,1,1,255));Read(Raw(new[]{2065,0,0},0,0,1,1,255));
        Reject(Raw(new[]{-2066,0,0},0,0,1,1,255),"lower enclosure rejects");
        Reject(Raw(new[]{2066,0,0},0,0,1,1,255),"upper enclosure rejects");
        Reject(Raw(new[]{0,2050,0},0,0,20,1,255),"positive extent outside enclosure rejects");
        Reject(Raw(new[]{32767,0,0},2,0,1,32767,255),"short endpoint overflow rejects");
        foreach(uint size in new[]{0u,32768u,uint.MaxValue})Reject(Raw(new[]{0,0,0},0,0,size,1,255),"invalid width rejects");
        Reject(Raw(new[]{0,0,0},6,0,1,1,255),"invalid normal rejects");
        Reject(Raw(new[]{0,0,0},0,4,1,1,255),"reserved flags reject");
        Reject(Raw(new[]{0,0,0},0,0,1,1,255,0),"air material rejects");
        Reject(Raw(new[]{0,0,0},0,0,1,1,255,255),"unknown material rejects");
        Reject(Raw(new[]{0,0,0},0,0,1,1,255,7,1),"palette overflow rejects");
        byte[] valid=Raw(new[]{0,0,0},0,0,1,1,255);bool bad=false;
        try{using var reader=new ExplorationPrototypeReader(Pack(valid,true));reader.TryRead(out _);}catch(InvalidDataException){bad=true;}
        Check(bad,"independent checksum mismatch rejects");
        for(int n=1;n<=8;n++)Reject(valid.Take(valid.Length-n).ToArray(),"truncated decoded record rejects");
        File.WriteAllText(args[0],JsonSerializer.Serialize(new{status="PASS",checks,quadCases=cases,crcCases=276,scope="Independent bitwise CRC oracle, direct corner/winding/material oracle, valid-CRC corrupt geometry, bounds/length/flag/material rejection; no native renderer."},new JsonSerializerOptions{WriteIndented=true}));
        Console.WriteLine("PASS "+checks+" decoder checks, "+cases+" independently generated quad cases");
    }
}
