using System;
using System.IO;
using System.Text.Json;
using AshenVoxel.WorldScale;

static class OverviewConsumerCheck
{
    static int checks;
    static void Check(bool value,string name){if(!value)throw new Exception(name);checks++;}
    static void Reject(Action action,string name)
    {try{action();}catch(InvalidDataException){checks++;return;}throw new Exception(name);}
    static int Main(string[] args)
    {
        string folder=args[0],report=args[1];
        byte[] raw=File.ReadAllBytes(Path.Combine(folder,"Coarse320.bytes"));
        HeightAtlas height=AtlasIO.Read(new MemoryStream(raw));
        byte[] climateRaw=File.ReadAllBytes(Path.Combine(folder,"Coarse320.climate.bytes"));
        ClimateAtlas climate=ClimateArchiveIO.Read(new MemoryStream(climateRaw));
        Check(height.Columns==937&&height.Rows==469,"320m whole-world dimensions");
        Check(height.ExtentX==299520&&height.ExtentZ==149760,"physical world size preserved");
        Check(height.OriginX==-149760&&height.OriginZ==-74880,"centered world origin");
        Check(height.ExtentX/(height.Columns-1)==320&&height.ExtentZ/(height.Rows-1)==320,"spacing");
        Check(ClimateArchiveIO.HeightHash(height)==climate.HeightAtlasSha256,"exact height/climate pairing");
        var field=new ClimateAtlasField(new AtlasWorldField(height),climate);
        var random=new Random(320);
        for(int i=0;i<1000;i++)
        {
            int x=random.Next(height.Columns),z=random.Next(height.Rows);
            double wx=height.OriginX+x*320,wz=height.OriginZ+z*320;
            Check(Math.Abs(field.Sample(wx,wz).HeightMeters-height.HeightAt(x,z))<.001,"node coordinate agreement");
            Check(field.TrySampleClimate(wx,wz,out _),"paired climate query");
            Check(height.LandAt(x,z)==(height.HeightAt(x,z)>=0),"generated land mask");
            var bounds=height.QueryBounds(Math.Max(0,x-2),Math.Max(0,z-2),Math.Min(height.Columns-1,x+2),Math.Min(height.Rows-1,z+2));
            Check(height.HeightAt(x,z)>=bounds.Minimum&&height.HeightAt(x,z)<=bounds.Maximum,"conservative source bounds");
        }
        Reject(()=>AtlasIO.Read(new MemoryStream(raw,0,raw.Length-7)),"truncation rejected");
        byte[] damaged=(byte[])raw.Clone();damaged[damaged.Length-12]^=1;
        Reject(()=>AtlasIO.Read(new MemoryStream(damaged)),"corruption rejected");
        Reject(()=>ClimateArchiveIO.Read(new MemoryStream(climateRaw,0,climateRaw.Length-7)),"climate truncation rejected");
        File.WriteAllText(report,JsonSerializer.Serialize(new{status="PASS",checks,
            scope="Actual engine-free project C# archive readers and source sampling; not Unity import/render",
            columns=height.Columns,rows=height.Rows,heightSha256=climate.HeightAtlasSha256},new JsonSerializerOptions{WriteIndented=true}));
        Console.WriteLine("PASS "+checks+" actual C# overview consumer checks");return 0;
    }
}
