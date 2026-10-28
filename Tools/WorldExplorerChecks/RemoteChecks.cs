using System;
using System.IO;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using System.Reflection;
using AshenVoxel.WorldScale;
using AshenVoxel.WorldScaleRuntime;

static class RemoteChecks
{
    sealed class Rough:IWorldField
    {
        public string SourceIdentity=>"explicit-rough-test-field";
        public SurfaceSample Sample(double x,double z)=>new SurfaceSample(100,2,6,true);
    }
    sealed class Transport:HttpMessageHandler
    {
        public byte[] Bytes;
        public int Posts;
        public HttpStatusCode PostCode=HttpStatusCode.OK;
        public bool Oversized;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)
        {
            if(request.Method==HttpMethod.Get)
                return new HttpResponseMessage(HttpStatusCode.OK) { Content=new StringContent("{\"ready\":true,\"world_id\":\""+new string('a',64)+"\",\"spacing_m\":30,\"max_region_side\":1024,\"max_region_samples\":262144,\"backend\":\"FAKE-CONTRACT-TEST-NOT-NEURAL\"}") };
            Posts++;
            string body=await request.Content.ReadAsStringAsync();
            if(!body.Contains("\"row0\":-1")||!body.Contains("\"col0\":-1")||!body.Contains("\"rows\":4")||!body.Contains("\"cols\":4"))throw new Exception("Unexpected request coordinate mapping: "+body);
            var content=new ByteArrayContent(Bytes);
            if(Oversized)content.Headers.ContentLength=200000000;
            return new HttpResponseMessage(PostCode) { Content=content };
        }
    }
    static void ExpectThrows(Action action,Action<bool,string> check,string name)
    {
        try{action();}catch(Exception e)when(e is InvalidDataException||e is InvalidOperationException||e is ArgumentException){check(true,name);return;}
        check(false,name);
    }
    static byte[] WithHeight(byte[] source,float height)
    {
        byte[] copy=(byte[])source.Clone();int offset=12+BitConverter.ToInt32(copy,8);
        for(int i=0;i<16;i++)BitConverter.GetBytes(height).CopyTo(copy,offset+i*4);
        uint crc=0xffffffff;
        for(int i=0;i<copy.Length-4;i++){crc^=copy[i];for(int bit=0;bit<8;bit++)crc=(crc>>1)^((crc&1)==0?0:0xedb88320);}
        BitConverter.GetBytes(~crc).CopyTo(copy,copy.Length-4);return copy;
    }
    static byte[] WithClimateEncoding(byte[] source,string encoding,float seasonality,bool omit=false)
    {
        int oldLength=BitConverter.ToInt32(source,8),oldOffset=12+oldLength;
        var metadata=JsonSerializer.Deserialize<Dictionary<string,object>>(Encoding.UTF8.GetString(source,12,oldLength));
        if(omit){metadata.Remove("climate_encoding");metadata.Remove("climate_value_units");}
        else
        {
            metadata["climate_encoding"]=encoding;
            metadata["climate_value_units"]=new[]{"degC",encoding=="upstream-native-v1"?"centi_degC":"degC","mm/year","percent","degC/m"};
        }
        byte[] json=JsonSerializer.SerializeToUtf8Bytes(metadata);
        byte[] copy=new byte[12+json.Length+(source.Length-oldOffset-4)+4];
        Array.Copy(source,copy,8);BitConverter.GetBytes(json.Length).CopyTo(copy,8);json.CopyTo(copy,12);
        Array.Copy(source,oldOffset,copy,12+json.Length,source.Length-oldOffset-4);
        for(int i=0;i<16;i++)BitConverter.GetBytes(seasonality).CopyTo(copy,12+json.Length+16*4+16*4+i*4);
        uint crc=0xffffffff;
        for(int i=0;i<copy.Length-4;i++){crc^=copy[i];for(int bit=0;bit<8;bit++)crc=(crc>>1)^((crc&1)==0?0:0xedb88320);}
        BitConverter.GetBytes(~crc).CopyTo(copy,copy.Length-4);return copy;
    }
    public static void Run(string path,Action<bool,string> check)
    {
        byte[] bytes=File.ReadAllBytes(path);
        var transport=new Transport { Bytes=bytes };
        using(var rejected=new RemoteTerrainField(new Rough(),handler:transport))
            ExpectThrows(()=>rejected.InitializeAsync().GetAwaiter().GetResult(),check,"live client rejects an explicitly fake backend by default");
        transport=new Transport { Bytes=bytes };
        IWorldField snapshot;
        using(var client=new RemoteTerrainField(new Rough(),handler:transport,allowTestBackend:true))
        {
            client.InitializeAsync().GetAwaiter().GetResult();
            check(client.Sample(15,15).HeightMeters==100&&transport.Posts==0,"missing model samples stay explicit rough visual data and never trigger synchronous network I/O");
            ExpectThrows(()=>client.ReadySnapshot(0,0,30,30),check,"authoritative snapshot is blocked before the complete model region arrives");
            Task a=client.PrefetchAsync(0,0,30,30),b=client.PrefetchAsync(0,0,30,30);
            check(object.ReferenceEquals(a,b),"identical asynchronous native windows share one pending request");
            Task.WhenAll(a,b).GetAwaiter().GetResult();
            SurfaceSample sample=client.Sample(15,15);
            check(transport.Posts==1&&client.IsReady(0,0,30,30)&&client.ResidentRegions==1,"Python-produced ATDR data completes one bounded cached request");
            check(Math.Abs(sample.HeightMeters-6502)<1e-6&&sample.SourceBiome==12&&sample.EffectiveBiome==6&&sample.IsLand,"float32 meter heights, global row/column mapping and source-biome forest override decode correctly");
            snapshot=client.ReadySnapshot(0,0,30,30);
            check(client.TrySampleClimate(15,15,out SurfaceClimate climate)&&Math.Abs(climate.TemperatureC+5)<1e-6&&Math.Abs(climate.SeasonalityC-8)<1e-6&&climate.PrecipitationMm==1200&&Math.Abs(climate.LapseRateCPerMeter+.0065)<1e-7,
                "all five physical climate channels are retained without applying a second altitude lapse");
            check(client.ResidentPayloadBytes==16*26,"decoded cache accounts for all five climate channels in its bounded resident payload");
            var explorer=new WorldExplorer();
            typeof(WorldExplorer).GetField("remote",BindingFlags.NonPublic|BindingFlags.Instance).SetValue(explorer,client);
            bool tileReady=(bool)typeof(WorldExplorer).GetMethod("TileReady",BindingFlags.NonPublic|BindingFlags.Instance).Invoke(explorer,new object[]{new LodTile(new TileKey(0,0,0),0,0,30,6500,6506)});
            check(!tileReady,"model tile readiness requires the full32m surface-attribute halo, not merely the2m old normal halo");
            ExpectThrows(()=>client.PrefetchAsync(-1e7,-1e7,1e7,1e7),check,"client rejects huge native inference rectangles before any HTTP request");
        }
        check(Math.Abs(snapshot.Sample(15,15).HeightMeters-6502)<1e-6,"authoritative inspection snapshot retains immutable bytes after cache disposal");
        check(((IClimateWorldField)snapshot).TrySampleClimate(15,15,out SurfaceClimate retained)&&retained.PrecipitationMm==1200,
            "immutable inspection snapshot retains real climate after client cache disposal");
        transport=new Transport { Bytes=bytes };
        using(var client=new RemoteTerrainField(new Rough(),handler:transport,allowTestBackend:true,maximumResidentBytes:1024))
        {
            client.InitializeAsync().GetAwaiter().GetResult();
            ExpectThrows(()=>client.PrefetchAsync(0,0,300,300),check,"decoded five-channel budget rejects oversized residency before HTTP");
            check(transport.Posts==0,"residency refusal does not spend inference or network work");
        }
        foreach(string encoding in new[]{"physical-si-v2","upstream-native-v1","legacy-missing","unknown-v9",""})
        {
            bool legacy=encoding=="upstream-native-v1"||encoding=="legacy-missing";
            transport=new Transport { Bytes=WithClimateEncoding(bytes,encoding,legacy?1079:10.79f,encoding=="legacy-missing") };
            using(var client=new RemoteTerrainField(new Rough(),handler:transport,allowTestBackend:true))
            {
                client.InitializeAsync().GetAwaiter().GetResult();
                if(encoding=="unknown-v9"||encoding=="")ExpectThrows(()=>client.PrefetchAsync(0,0,30,30).GetAwaiter().GetResult(),check,"unknown or explicitly empty climate unit encoding is rejected before caching usable terrain");
                else
                {
                    client.PrefetchAsync(0,0,30,30).GetAwaiter().GetResult();
                    check(client.TrySampleClimate(15,15,out SurfaceClimate climate)&&Math.Abs(climate.SeasonalityC-10.79)<.00001,
                        encoding+" resolves actual model seasonality to10.79 Celsius exactly once");
                }
            }
        }
        byte[] corrupt=(byte[])bytes.Clone();corrupt[corrupt.Length-1]^=1;
        transport=new Transport { Bytes=corrupt };
        using(var client=new RemoteTerrainField(new Rough(),handler:transport,allowTestBackend:true))
        {
            client.InitializeAsync().GetAwaiter().GetResult();
            ExpectThrows(()=>client.PrefetchAsync(0,0,30,30).GetAwaiter().GetResult(),check,"wire CRC corruption is rejected before terrain becomes ready");
            check(!client.IsReady(0,0,30,30),"failed payloads never enter the usable model cache");
        }
        transport=new Transport { Bytes=bytes,Oversized=true };
        using(var client=new RemoteTerrainField(new Rough(),handler:transport,allowTestBackend:true))
        {
            client.InitializeAsync().GetAwaiter().GetResult();
            ExpectThrows(()=>client.PrefetchAsync(0,0,30,30).GetAwaiter().GetResult(),check,"oversized HTTP payload is rejected before reading the advertised body");
        }
        transport=new Transport { Bytes=bytes,PostCode=HttpStatusCode.Conflict };
        using(var client=new RemoteTerrainField(new Rough(),handler:transport,allowTestBackend:true))
        {
            client.InitializeAsync().GetAwaiter().GetResult();
            ExpectThrows(()=>client.PrefetchAsync(0,0,30,30).GetAwaiter().GetResult(),check,"HTTP409 rejects stale model-world identity rather than blending sources");
        }
        foreach(float height in new[]{-12000f,48000f,60000f,-12001f,float.NaN})
        {
            transport=new Transport { Bytes=WithHeight(bytes,height) };
            using(var client=new RemoteTerrainField(new Rough(),handler:transport,allowTestBackend:true))
            {
                client.InitializeAsync().GetAwaiter().GetResult();
                if(height==-12000||height==48000)
                {
                    client.PrefetchAsync(0,0,30,30).GetAwaiter().GetResult();
                    check(client.Sample(15,15).HeightMeters==height,"wire height at conservative boundary"+height+" is accepted exactly");
                }
                else ExpectThrows(()=>client.PrefetchAsync(0,0,30,30).GetAwaiter().GetResult(),check,"wire height"+height+" outside supported conservative LOD bounds is rejected despite valid CRC");
            }
        }
    }
    public static void Live(string endpoint,Action<bool,string> check)
    {
        using(var client=new RemoteTerrainField(new Rough(),endpoint))
        {
            client.InitializeAsync().GetAwaiter().GetResult();
            check(client.Connected&&client.Status.backend.StartsWith("upstream-pretrained",StringComparison.Ordinal),"live provider reports the real pretrained backend and immutable world identity");
            // Exactly row608,col1120,33x33 after the client's one-native-cell halo.
            // The integration fixture is already in the provider's canonical owner
            // cache, so this does not request a new large model rectangle.
            double minX=1121*30.0,minZ=609*30.0,maxX=1151*30.0,maxZ=639*30.0;
            client.PrefetchAsync(minX,minZ,maxX,maxZ).GetAwaiter().GetResult();
            check(client.IsReady(minX,minZ,maxX,maxZ)&&client.ResidentRegions==1,"live HTTP returns a complete checksum-verified native region through the actual C# adapter");
            IWorldField snapshot=client.ReadySnapshot(minX,minZ,maxX,maxZ);
            SurfaceSample sample=snapshot.Sample(1136*30.0,624*30.0);
            check(!double.IsNaN(sample.HeightMeters)&&!double.IsInfinity(sample.HeightMeters)&&Math.Abs(sample.HeightMeters)<100000&&snapshot.SourceIdentity.Contains(client.Status.world_id),"live model meter height and retained region identity reach the immutable matter source");
            check(sample.EffectiveBiome==(sample.IsLand?SurfaceSample.ForestBiome:sample.SourceBiome),"live source biome survives the explicit forest override");
            check(((IClimateWorldField)snapshot).TrySampleClimate(1136*30.0,624*30.0,out SurfaceClimate climate)&&climate.SeasonalityC>6.5&&climate.SeasonalityC<6.7,
                "actual pretrained seasonality is converted from its legacy centi-C wire value into physical Celsius");
            Console.WriteLine("LIVE_MODEL_PROBE {\"world_id\":\""+client.Status.world_id+"\",\"backend\":\""+client.Status.backend+"\",\"height_m\":"+sample.HeightMeters.ToString("R",CultureInfo.InvariantCulture)+",\"seasonality_c\":"+climate.SeasonalityC.ToString("R",CultureInfo.InvariantCulture)+",\"row0\":608,\"col0\":1120,\"rows\":33,\"cols\":33,\"source_biome\":"+sample.SourceBiome+",\"effective_biome\":"+sample.EffectiveBiome+",\"note\":\"Actual HTTP provider and actual C# adapter with JsonUtility substitute; not native Unity\"}");
        }
    }
}
