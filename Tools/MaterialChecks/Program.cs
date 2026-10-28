using System;
using System.IO;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text.Json;
using AshenVoxel.Core;

static class Program
{
    static int checks;
    static void Check(bool ok,string name){if(!ok)throw new Exception("FAIL "+name);checks++;Console.WriteLine("PASS "+name);}
    static byte[] Save(VoxelWorld w){using(var s=new MemoryStream()){Persistence.Save(s,w);return s.ToArray();}}
    static uint Crc(byte[] bytes,int offset,int count)
    {uint crc=0xffffffff;for(int n=offset;n<offset+count;n++){crc^=bytes[n];for(int i=0;i<8;i++)crc=(crc>>1)^((crc&1)!=0?0xedb88320u:0u);}return ~crc;}
    static void Reject(byte[] bytes,string name)
    {try{Persistence.Load(new MemoryStream(bytes));}catch(InvalidDataException){Check(true,name);return;}throw new Exception("FAIL accepted "+name);}
    static void Invalid(Action call,string name)
    {try{call();}catch(ArgumentException){Check(true,name);return;}throw new Exception("FAIL accepted "+name);}
    static ulong Signature(VoxelWorld w,bool grainsOnly=false)
    {
        var records=new List<ulong>();
        foreach(var pair in w.Chunks)for(int i=0;i<Chunk.Volume;i++)
        {MaterialId id=pair.Value.Cells[i];if(id==MaterialId.Air||(grainsOnly&&!MaterialCatalog.Get(id).Granular))continue;Int3 p=Chunk.Decode(i);uint tint=pair.Value.GetTint(p.x,p.y,p.z);records.Add(((ulong)(byte)id<<32)|tint);}
        records.Sort();ulong h=1469598103934665603;unchecked{foreach(ulong r in records)h=(h^r)*1099511628211;}return h;
    }
    static void Main(string[] args)
    {
        Check(MaterialCatalog.Count==25&&MaterialCatalog.Version==4,"catalog revision4 has25 append-only material entries");
        MaterialId[] old={MaterialId.Air,MaterialId.Stone,MaterialId.Mud,MaterialId.Dirt,MaterialId.Litter,MaterialId.Grass,MaterialId.Wood,MaterialId.Leaves,MaterialId.Sand,MaterialId.Ember,MaterialId.Fruit};
        bool stable=true;for(int i=0;i<old.Length;i++)stable&=(byte)old[i]==i;
        Check(stable,"historical IDs0..10 remain identical, including Basalt atStone1 and Fruit10");
        MaterialId[] additions={MaterialId.Granite,MaterialId.Limestone,MaterialId.Sandstone,MaterialId.Shale,MaterialId.Gravel,MaterialId.Clay,MaterialId.BlackSand,MaterialId.Snow,MaterialId.Ice,MaterialId.Silt,MaterialId.Peat,MaterialId.Fungus,MaterialId.Petal,MaterialId.Web};
        stable=true;for(int i=0;i<additions.Length;i++)stable&=(byte)additions[i]==i+11;
        Check(stable,"persisted material IDs11..21 retain their values; tissues/web append22..24 without aliases or reuse");
        var unique=new HashSet<byte>();foreach(MaterialId id in Enum.GetValues(typeof(MaterialId)))unique.Add((byte)id);
        Check(unique.Count==MaterialCatalog.Count&&Enum.GetValues(typeof(MaterialId)).Length==MaterialCatalog.Count,"enum and catalog have complete one-to-one coverage");
        bool valid=true;foreach(MaterialId id in Enum.GetValues(typeof(MaterialId)))
        {MaterialDefinition d=MaterialCatalog.Get(id);valid&=!string.IsNullOrWhiteSpace(d.Name)&&d.Hardness>=0&&d.Density>=0&&VoxelColor.IsValid(VoxelColor.Base(id))&&(id==MaterialId.Air?VoxelColor.Base(id)==0:(VoxelColor.Base(id)>>24)==255);}
        Check(valid,"all materials have names, valid physical cues and opaque base colors");
        Check(!MaterialCatalog.IsValid((MaterialId)25)&&!MaterialCatalog.IsValid((MaterialId)255),"unknown future material IDs are invalid");
        Invalid(()=>MaterialCatalog.Get((MaterialId)25),"catalog rejects unknown IDs");
        Invalid(()=>VoxelColor.Base((MaterialId)255),"palette rejects unknown IDs");
        Check(MaterialCatalog.Get(MaterialId.Granite).Hardness>MaterialCatalog.Get(MaterialId.Stone).Hardness&&MaterialCatalog.Get(MaterialId.Stone).Hardness>MaterialCatalog.Get(MaterialId.Limestone).Hardness&&MaterialCatalog.Get(MaterialId.Limestone).Hardness>MaterialCatalog.Get(MaterialId.Sandstone).Hardness&&MaterialCatalog.Get(MaterialId.Sandstone).Hardness>MaterialCatalog.Get(MaterialId.Shale).Hardness,"rock tool resistance distinguishes hard granite/basalt from softer sedimentary stone");
        bool rigid=true;foreach(MaterialId id in new[]{MaterialId.Granite,MaterialId.Limestone,MaterialId.Sandstone,MaterialId.Shale,MaterialId.Ice})rigid&=MaterialCatalog.Get(id).Solid&&!MaterialCatalog.Get(id).Granular&&!MaterialCatalog.Get(id).Flammable;
        Check(rigid,"coherent rocks and ice remain solid static material rather than loose grains");
        bool loose=true;foreach(MaterialId id in new[]{MaterialId.Gravel,MaterialId.Clay,MaterialId.BlackSand,MaterialId.Snow,MaterialId.Silt,MaterialId.Peat})loose&=MaterialCatalog.Get(id).Solid&&MaterialCatalog.Get(id).Granular;
        Check(loose,"loose sediment, snow and peat participate in the shared granular approximation");
        Check(MaterialCatalog.Get(MaterialId.Peat).Flammable&&!MaterialCatalog.Get(MaterialId.Snow).Flammable&&!MaterialCatalog.Get(MaterialId.Gravel).Flammable,"organic peat and mineral/snow flammability remain distinct");
        Check(MaterialCatalog.Get(MaterialId.Snow).Density<MaterialCatalog.Get(MaterialId.Ice).Density&&MaterialCatalog.Get(MaterialId.Ice).Density<MaterialCatalog.Get(MaterialId.Granite).Density,"snow, ice and rock retain distinct density cues");
        ulong legacyHash=1469598103934665603;
        unchecked{for(int id=0;id<=10;id++)for(int seed=-1;seed<=1;seed++)for(int n=0;n<37;n++)legacyHash=(legacyHash^EnvironmentColors.Sample((MaterialId)id,n*13-241,n*5-91,n*-17+117,.1f,seed*714103))*1099511628211;}
        Check(legacyHash==12189621461467217345UL,"1221 historical material/seed/position colors match the pre-update published Core golden hash");
        var paletteCounts=new Dictionary<string,int>();
        foreach(MaterialId id in additions)
        {
            var colors=new HashSet<uint>();bool deterministic=true,opaque=true;
            for(int n=0;n<256;n++)
            {
                uint a=EnvironmentColors.Sample(id,n%16-8,n/16-8,n%7-3,.1f,714103);
                colors.Add(a);deterministic&=a==EnvironmentColors.Sample(id,n%16-8,n/16-8,n%7-3,.1f,714103);opaque&=(a>>24)==255;
            }
            paletteCounts[id.ToString()]=colors.Count;
            Check(deterministic&&opaque&&colors.Count>1,id+" intrinsic palette is repeatable, opaque and spatially varied");
            Check(EnvironmentColors.Sample(id,31,-8,17,.1f,714103,0)==VoxelColor.Base(id),id+" zero variation preserves exact base color");
        }
        uint custom=VoxelColor.Pack(73,119,153);
        Check(EnvironmentColors.Sample(MaterialId.Granite,1,2,3,.1f,1,0,custom)==custom,"artist base-color override remains independent from material identity");
        Invalid(()=>EnvironmentColors.Sample(MaterialId.Snow,1,2,3,.1f,1,float.NaN),"color sampler rejects nonfinite variation");
        Invalid(()=>EnvironmentColors.Sample(MaterialId.Ice,1,2,3,0,1),"color sampler rejects invalid cell scale");
        var w=new VoxelWorld(32,16,16,.1f,714103);
        foreach(MaterialId id in additions)w.Set((byte)id,5,7,id,EnvironmentColors.Sample(id,(byte)id,5,7,.1f,714103));
        byte[] saved=Save(w);var loaded=Persistence.Load(new MemoryStream(saved));
        Check(Signature(w)==Signature(loaded),"all new material identities and tints survive current snapshot roundtrip");
        Check(Convert.ToBase64String(Save(loaded))==Convert.ToBase64String(saved),"new-material snapshot serialization is stable after load");
        Check(BitConverter.ToInt32(saved,4)==2,"append-only materials retain snapshot Format2 rather than inventing an incompatible container");
        var one=new VoxelWorld(16,16,16,.1f,3);one.Set(0,0,0,MaterialId.Granite);
        byte[] unknown=Save(one);unknown[53]=255;int payload=BitConverter.ToInt32(unknown,8);Array.Copy(BitConverter.GetBytes(Crc(unknown,12,payload)),0,unknown,12+payload,4);
        Reject(unknown,"unknown material payload rejected even with a repaired valid CRC");
        byte[] legacy=File.ReadAllBytes(args[0]);
        Check(Convert.ToHexString(SHA256.HashData(legacy))=="DCBFCF0A9C01964E8B1326448055C22DE23935DDD04F22B24F78C07C5DF3AD58","legacy fixture retains bytes written by the prior published Core");
        var historical=Persistence.Load(new MemoryStream(legacy));bool preserved=true;
        for(int id=1;id<=10;id++)preserved&=historical.Get(id,3,2)==(MaterialId)id&&historical.GetTint(id,3,2)==EnvironmentColors.Sample((MaterialId)id,id,3,2,.1f,714103);
        Check(preserved,"old material/color snapshots load with their exact historical IDs and appearance");
        var tool=new VoxelWorld(32,8,8,1,1);int px=2;
        foreach(MaterialId id in new[]{MaterialId.Granite,MaterialId.Stone,MaterialId.Limestone,MaterialId.Sandstone,MaterialId.Shale}){tool.Set(px,3,3,id);px+=5;}
        int removed=WorldEdits.CarveSphere(tool,15,3,3,100,3);
        Check(removed==3&&tool.Get(2,3,3)==MaterialId.Granite&&tool.Get(7,3,3)==MaterialId.Stone,"digging power removes softer rocks while preserving harder granite and basalt");
        var falling=new VoxelWorld(16,24,16,.1f,1);
        for(int z=0;z<16;z++)for(int x=0;x<16;x++)falling.Set(x,0,z,MaterialId.Stone);
        using(var sim=new Simulation(falling))
        {
            px=1;foreach(MaterialId id in new[]{MaterialId.Gravel,MaterialId.Clay,MaterialId.BlackSand,MaterialId.Snow,MaterialId.Silt,MaterialId.Peat})
            {falling.Set(px,20,4,id,VoxelColor.Pack((byte)(31+(byte)id),67,94));px+=2;}
            falling.Set(12,18,12,MaterialId.Ice);falling.Set(13,18,12,MaterialId.Granite);
            ulong before=Signature(falling,true);bool budget=true;
            for(int i=0;i<3000&&sim.ActiveCount>0;i++){sim.Step(1);budget&=sim.LastProcessed<=1;}
            Check(budget&&sim.ActiveCount==0,"new granular materials settle under the one-candidate work budget");
            Check(Signature(falling,true)==before,"gravity conserves every new grain material and its paired intrinsic color");
            Check(falling.Get(12,18,12)==MaterialId.Ice&&falling.Get(13,18,12)==MaterialId.Granite,"suspended ice and granite do not silently become granular particles");
        }
        File.WriteAllText(args[1],JsonSerializer.Serialize(new{status="PASS",checks,catalog_version=MaterialCatalog.Version,material_count=MaterialCatalog.Count,
            scope="Actual engine-independent catalog, color, persistence, digging and shared granular solver; not geological simulation or Unity rendering.",
            historical_palette_hash=legacyHash,legacy_fixture_sha256=Convert.ToHexString(SHA256.HashData(legacy)),palette_counts=paletteCounts},new JsonSerializerOptions{WriteIndented=true}));
        Console.WriteLine("All "+checks+" material checks passed.");
    }
}
