using System;
using System.IO;
using System.Collections.Generic;
using System.Text.Json;
using AshenVoxel.WorldScale;
static class Program
{
    static int checks;
    static void Check(bool value,string text){if(!value)throw new Exception("FAIL "+text);checks++;Console.WriteLine("PASS "+text);}
    static void Reject(Action action,string text){try{action();}catch(ArgumentException){Check(true,text);return;}catch(InvalidDataException){Check(true,text);return;}throw new Exception("FAIL accepts "+text);}
    static byte[] Save(ClimateAtlas value){using(var s=new MemoryStream()){ClimateArchiveIO.Write(s,value);return s.ToArray();}}
    static ClimateAtlas Load(byte[] value){using(var s=new MemoryStream(value))return ClimateArchiveIO.Read(s);}
    static void Crc(byte[] value){uint crc=AtlasIO.Crc32(value,value.Length-4);Array.Copy(BitConverter.GetBytes(crc),0,value,value.Length-4,4);}
    static void Main(string[] args)
    {
        var heights=new float[]{1000,1100,1200,1300};var ids=new byte[]{6,6,6,6};var land=new bool[]{true,true,true,true};
        var height=new HeightAtlas(2,2,-50,-100,100,200,heights,ids,land,"climate-fixture");
        var values=new float[]{10,8,6,4,7,8,9,10,1000,1200,1400,1600,20,30,40,50,-.0065f,-.0065f,-.0065f,-.0065f};
        string hash=ClimateArchiveIO.HeightHash(height);var climate=new ClimateAtlas(2,2,-50,-100,100,200,hash,"physical-test",values);
        values[4]=1000;Check(climate.ClimateAt(0,0).SeasonalityC==7,"climate atlas owns immutable cloned physical channels");
        Check(ClimateArchiveIO.IsHash(hash)&&hash.Length==64,"paired height identity is a canonical SHA256");
        byte[] raw=Save(climate);var round=Load(raw);Check(Convert.ToBase64String(Save(round))==Convert.ToBase64String(raw),"physical archive has exact stable byte roundtrip");
        Check(round.ClimateAt(1,1).TemperatureC==4&&round.ClimateAt(1,1).SeasonalityC==10,"temperature and seasonality remain physical Celsius");
        using(var s=new MemoryStream(raw)){ClimateArchiveIO.Read(s);Check(s.CanRead,"archive read leaves caller stream open");}
        using(var s=new MemoryStream()){ClimateArchiveIO.Write(s,climate);Check(s.CanWrite,"archive write leaves caller stream open");}
        byte[] corrupt=(byte[])raw.Clone();corrupt[90]^=1;Reject(()=>Load(corrupt),"CRC detects corrupted climate archive");
        var truncated=new byte[raw.Length-1];Array.Copy(raw,truncated,truncated.Length);Reject(()=>Load(truncated),"truncated archive rejected");
        var future=(byte[])raw.Clone();future[7]=(byte)'2';Crc(future);Reject(()=>Load(future),"future climate schema rejected with valid CRC");
        var large=(byte[])raw.Clone();Array.Copy(BitConverter.GetBytes(int.MaxValue),0,large,8,4);Crc(large);Reject(()=>Load(large),"oversized climate dimensions rejected before allocation");
        var bad=(byte[])raw.Clone();int start=82+BitConverter.ToUInt16(raw,80);Array.Copy(BitConverter.GetBytes(float.NaN),0,bad,start,4);Crc(bad);Reject(()=>Load(bad),"NaN climate rejected with valid CRC");
        var trailing=new byte[raw.Length+1];Array.Copy(raw,trailing,raw.Length-4);Crc(trailing);Reject(()=>Load(trailing),"valid-CRC trailing payload rejected");
        Reject(()=>climate.ClimateAt(-1,0),"negative climate indices rejected");
        Reject(()=>new ClimateAtlas(2,2,0,0,100,200,"bad","x",new float[20]),"unpaired climate hash rejected");
        var native=(float[])values.Clone();native[4]=1078;Reject(()=>new ClimateAtlas(2,2,-50,-100,100,200,hash,"x",native),"centi-C cannot masquerade as physical seasonality");
        var field=new ClimateAtlasField(new AtlasWorldField(height),climate);SurfaceClimate c;
        Check(field.TrySampleClimate(0,0,out c)&&Math.Abs(c.TemperatureC-7)<1e-7&&Math.Abs(c.SeasonalityC-8.5)<1e-7,"paired physical climate bilinearly samples the same world transform");
        Check(Math.Abs(c.TemperatureC-7)<1e-7,"original surface temperature is not lapse-adjusted twice");
        Check(!field.TrySampleClimate(1000,0,out c),"outside climate coverage explicitly reports unavailable");
        var wide=new ClimateAtlasField(new AtlasWorldField(height,new WorldMapSettings(200,400)),climate);
        Check(wide.TrySampleClimate(-100,-200,out c)&&c.TemperatureC==10,"horizontal landmass resize preserves climate-to-height registration");
        var scaled=new ClimateAtlasField(new AtlasWorldField(height,new WorldMapSettings(100,200,2)),climate);
        Check(scaled.TrySampleClimate(0,0,out c)&&Math.Abs(c.TemperatureC-(7-1150*.0065))<1e-5,"vertical scale changes apply lapse only to the elevation difference");
        Check(scaled.Sample(0,0).HeightMeters==2300,"climate decorator preserves height scaling");
        var altered=new HeightAtlas(2,2,-50,-100,100,200,new float[]{1001,1100,1200,1300},ids,land,"climate-fixture");
        Reject(()=>new ClimateAtlasField(new AtlasWorldField(altered),climate),"same dimensions and name cannot hide changed height values");
        var moved=new ClimateAtlas(2,2,-49,-100,100,200,hash,"moved",new float[]{10,8,6,4,7,8,9,10,1000,1200,1400,1600,20,30,40,50,-.0065f,-.0065f,-.0065f,-.0065f});
        Reject(()=>new ClimateAtlasField(new AtlasWorldField(height),moved),"shifted climate lattice cannot attach to the height atlas");
        Check(field.QueryBounds(-50,-100,50,100).Maximum==1300,"climate decorator retains conservative ground bounds");
        double? actualStd=null;
        if(args.Length>=3)
        {
            HeightAtlas actualHeight;ClimateAtlas actualClimate;
            using(var s=File.OpenRead(args[1]))actualHeight=AtlasIO.Read(s);using(var s=File.OpenRead(args[2]))actualClimate=ClimateArchiveIO.Read(s);
            var actual=new ClimateAtlasField(new AtlasWorldField(actualHeight),actualClimate);
            Check(actual.TrySampleClimate(0,0,out c),"actual Python physical archive pairs through the Csharp reader");actualStd=c.SeasonalityC;
            Check(c.SeasonalityC>0&&c.SeasonalityC<30,"actual model seasonality is Celsius rather than hundreds of centi-C");
            var column=new SurfaceMaterialSampler(actual).EvaluateColumn(0,0);
            Check(column.ClimateFromField&&Math.Abs(column.TemperatureC-(c.TemperatureC-c.SeasonalityC*.2))<1e-4,"surface classifier consumes real baked physical climate without an extra lapse adjustment");
        }
        File.WriteAllText(args[0],JsonSerializer.Serialize(new{status="PASS",checks,actual_seasonality_c=actualStd,scope="Actual engine-independent climate archive and paired surface field; not Unity or model inference"},new JsonSerializerOptions{WriteIndented=true}));
        Console.WriteLine("All "+checks+" climate checks passed.");
    }
}
