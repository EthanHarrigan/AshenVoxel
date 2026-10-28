using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using AshenVoxel.Core;
using AshenVoxel.WorldScale;
static class Program
{
    static int checks;
    static void Check(bool b,string s){if(!b)throw new Exception("FAIL "+s);checks++;Console.WriteLine("PASS "+s);}
    static void Reject(Action a,string s){try{a();}catch(ArgumentException){Check(true,s);return;}throw new Exception("FAIL accepts "+s);}
    sealed class TestField:IWorldFieldBounds,IClimateWorldField
    {
        public double Height=5000,SlopeX=0,SlopeZ=0,CurveX=0;
        public byte Biome=6;public bool ForceWater,UseClimate=true;
        public int Queries;
        public SurfaceClimate Climate=new SurfaceClimate(-10,0,1800,35,-.0065);
        public string SourceIdentity=>"surface-fixture/"+Height+"/"+SlopeX+"/"+Biome;
        public SurfaceSample Sample(double x,double z){Queries++;double h=Height+x*SlopeX+z*SlopeZ+x*x*CurveX;return new SurfaceSample(h,Biome,6,!ForceWater&&h>=0);}
        public HeightBounds QueryBounds(double x0,double z0,double x1,double z1)=>new HeightBounds(-12000,48000);
        public bool TrySampleClimate(double x,double z,out SurfaceClimate climate){climate=Climate;return UseClimate;}
    }
    static SurfaceProfile Profile(MaterialId rock=MaterialId.Granite,bool snow=true)=>new SurfaceProfile(forcedBedrockMaterial:rock,enableSnow:snow);
    static ulong Hash(SurfaceColumn c)
    {unchecked{ulong h=1469598103934665603;foreach(long n in new[]{BitConverter.DoubleToInt64Bits(c.SurfaceHeightMeters),BitConverter.DoubleToInt64Bits(c.SnowDepthMeters),(long)c.SurfaceMaterial,(long)c.BedrockMaterial,(long)c.SurfaceTint})h=(h^(ulong)n)*1099511628211;return h;}}
    static void Main(string[] args)
    {
        var watch=Stopwatch.StartNew();
        var flat=new TestField();var sampler=new SurfaceMaterialSampler(flat,Profile());
        var depths=new HashSet<int>();int bare=0,snowy=0;double maximum=0,meanFlat=0;
        for(int z=-20;z<=20;z++)for(int x=-20;x<=20;x++)
        {
            var c=sampler.EvaluateColumn(x*9.7,z*11.3);depths.Add((int)Math.Round(c.SnowDepthMeters*10));
            CheckColumn(c);if(c.SnowDepthMeters==0)bare++;else snowy++;maximum=Math.Max(maximum,c.SnowDepthMeters);meanFlat+=c.SnowDepthMeters;
        }
        meanFlat/=41*41;
        Check(snowy>500&&bare>20,"cold snowy terrain has both supported snow and coherent bare patches");
        Check(depths.Count>=6&&maximum>.5,"physical snow depth varies across multiple10cm layers instead of a white blanket");
        var steep=new TestField{SlopeX=Math.Tan(65*Math.PI/180)};var steepSampler=new SurfaceMaterialSampler(steep,Profile());
        double steepSnow=0;int steepRocks=0;
        for(int i=-40;i<=40;i++){var c=steepSampler.EvaluateColumn(i*.3,2);steepSnow+=c.SnowDepthMeters;if(SurfaceProfile.IsRock(c.SurfaceMaterial))steepRocks++;}
        Check(steepSnow==0&&steepRocks==81,"steep unsupported mountain faces expose rock despite subzero climate");
        var windStill=new SurfaceMaterialSampler(flat,Profile().With(windStrength:0));
        var windy=new SurfaceMaterialSampler(flat,Profile().With(windStrength:2));double stillDepth=0,windDepth=0;
        for(int i=0;i<400;i++){stillDepth+=windStill.EvaluateColumn(i*2.1,i*1.3).SnowDepthMeters;windDepth+=windy.EvaluateColumn(i*2.1,i*1.3).SnowDepthMeters;}
        Check(stillDepth>windDepth*1.5,"wind exposure removes cover and reduces retained depth");
        var ridge=new SurfaceMaterialSampler(new TestField{CurveX=-.01},Profile());var hollow=new SurfaceMaterialSampler(new TestField{CurveX=.01},Profile());double ridgeSnow=0,hollowSnow=0;
        for(int z=0;z<120;z++){ridgeSnow+=ridge.EvaluateColumn(0,z*4).SnowDepthMeters;hollowSnow+=hollow.EvaluateColumn(0,z*4).SnowDepthMeters;}
        Check(hollowSnow>ridgeSnow*1.7,"sheltered concave ground retains more snow than windswept convex crests");
        var warm=new SurfaceMaterialSampler(new TestField{Climate=new SurfaceClimate(8,0,1800,35,-.0065)},Profile());
        var warmColumn=warm.EvaluateColumn(100,100);
        Check(warmColumn.SnowDepthMeters==0&&Math.Abs(warmColumn.TemperatureC-8)<1e-8&&warmColumn.ClimateFromField,"actual surface temperature is used once without applying altitude lapse again");
        var dry=new SurfaceMaterialSampler(new TestField{Climate=new SurfaceClimate(-10,0,20,35,-.0065)},Profile());
        double dryDepth=0;for(int i=0;i<100;i++)dryDepth+=dry.EvaluateColumn(i*6,i*3).SnowDepthMeters;
        Check(dryDepth<meanFlat*100*.25,"cold arid mountains do not receive wet-climate snow depths");
        var noSnow=new SurfaceMaterialSampler(flat,Profile(snow:false));
        Check(noSnow.EvaluateColumn(100,100).SnowDepthMeters==0,"snow generation can be disabled explicitly");
        var shallowCap=new SurfaceMaterialSampler(flat,Profile().With(maxSnowDepthMeters:.35));bool cap=true;
        for(int i=0;i<200;i++)cap&=shallowCap.EvaluateColumn(i*3,3).SnowDepthMeters<=.35+1e-9;
        Check(cap,"custom snow thickness cap bounds generated cover");
        var sea=new SurfaceMaterialSampler(new TestField{Height=0,Biome=0,ForceWater=true},Profile());
        var seaCol=sea.EvaluateColumn(0,0);
        Check(seaCol.HasWater&&seaCol.SnowDepthMeters==0,"source marine surface at sea level cannot grow unsupported terrestrial snow");
        var ocean=new SurfaceMaterialSampler(new TestField{Height=-5,Biome=0,ForceWater=true},Profile());var oc=ocean.EvaluateColumn(0,0);
        Check(oc.HasWater&&oc.WaterLevelMeters==0&&ocean.SampleMatter(new WorldPoint(0,-1,0),oc).Material==MaterialId.Air,"visual water occupies no fake solid cells above the seabed");
        var coast=new TestField{Height=.05,SlopeX=.1,Climate=new SurfaceClimate(18,0,800,35,-.0065)};
        var blackCoast=new SurfaceMaterialSampler(coast,Profile(MaterialId.Stone,false)).EvaluateColumn(0,0);
        var paleCoast=new SurfaceMaterialSampler(coast,Profile(MaterialId.Granite,false)).EvaluateColumn(0,0);
        Check(blackCoast.SurfaceMaterial==MaterialId.BlackSand&&paleCoast.SurfaceMaterial==MaterialId.Sand,"black beach sand follows volcanic substrate rather than random biome noise");
        var desert=new TestField{Height=100,Biome=13,Climate=new SurfaceClimate(29,0,65,80,-.0065)};
        var desertCol=new SurfaceMaterialSampler(desert,Profile(MaterialId.Stone,false)).EvaluateColumn(0,0);
        Check(desertCol.SurfaceMaterial==MaterialId.Sand&&desertCol.BiomeUsed==13&&desertCol.EffectiveBiome==6,"vegetation Forest override does not erase SandSea surface identity");
        var wetland=new TestField{Height=100,Biome=12,Climate=new SurfaceClimate(14,0,1900,35,-.0065)};
        var wet=new SurfaceMaterialSampler(wetland,Profile(snow:false)).EvaluateColumn(20,20);
        Check(wet.SurfaceMaterial==MaterialId.Peat&&wet.SoilMaterial==MaterialId.Mud,"wetland moisture and organic ground create peat above wet soil");
        var humid=new TestField{Height=100,Biome=7,Climate=new SurfaceClimate(25,0,2600,35,-.0065)};
        var humidCol=new SurfaceMaterialSampler(humid,Profile(snow:false)).EvaluateColumn(0,0);
        Check(humidCol.SurfaceMaterial!=MaterialId.Peat,"rain alone does not turn every flat rainforest into a peat bog");
        var climateMissing=new TestField{Height=100,Biome=13,UseClimate=false};
        var missingCol=new SurfaceMaterialSampler(climateMissing,Profile(snow:false)).EvaluateColumn(0,0);
        Check(!missingCol.ClimateFromField&&missingCol.PrecipitationMm<100&&missingCol.SurfaceMaterial==MaterialId.Sand,"missing model climate falls back to explicit source-biome estimates");
        var plain=new TestField{Height=100,UseClimate=false};var colors=new HashSet<uint>();
        foreach(byte b in new byte[]{3,4,6,7,8,9,10,12})
        {plain.Biome=b;var s=new SurfaceMaterialSampler(plain,Profile(snow:false));colors.Add(s.Color(MaterialId.Grass,123,100,456,b,.4,MaterialId.Granite));}
        Check(colors.Count>=7,"grass palettes differ coherently across ecological biomes without new grass IDs");
        var geology=new SurfaceMaterialSampler(plain,new SurfaceProfile(enableSnow:false));var rocks=new HashSet<MaterialId>();int boundaries=0;
        for(int i=0;i<1000;i++)
        {double x=i*173.37,z=i*367.51;MaterialId a=geology.RockAt(x,123,z),b=geology.RockAt(x+.1,123,z);rocks.Add(a);if(a!=b)boundaries++;}
        Check(rocks.Count>=4&&boundaries<30,"geological provinces and strata are coherent across adjacent10cm cells");
        var contactField=new TestField{Height=108,SlopeX=2,Climate=new SurfaceClimate(12,0,800,30,-.0065)};
        var contactSampler=new SurfaceMaterialSampler(contactField,new SurfaceProfile(enableSnow:false));var contact=contactSampler.EvaluateColumn(0,0);
        var contactMatter=contactSampler.SampleMatter(new WorldPoint(0,107.95,0),contact);
        Check(contact.SurfaceMaterial==contactMatter.Material&&contact.SurfaceTint==contactMatter.Tint&&contact.GroundCoverDepthMeters==0,"exposed steep rock proxy and top physical voxel agree at a stratum contact");
        Check(contactMatter.Material!=contactSampler.SampleMatter(new WorldPoint(0,106,0),contact).Material,"stratum-contact fixture reveals a different underlying rock when excavated");
        var windField=new TestField{SlopeX=.3};var eastWind=new SurfaceMaterialSampler(windField,Profile().With(windAzimuthDegrees:0));
        var westWind=new SurfaceMaterialSampler(windField,Profile().With(windAzimuthDegrees:180));
        Check(eastWind.EvaluateColumn(0,0).Exposure>westWind.EvaluateColumn(0,0).Exposure,"prevailing wind azimuth distinguishes exposed windward and sheltered leeward slopes");
        var sunward=new SurfaceMaterialSampler(new TestField{SlopeX=-.3},Profile().With(windStrength:0,solarAzimuthDegrees:0));
        var shaded=new SurfaceMaterialSampler(new TestField{SlopeX=.3},Profile().With(windStrength:0,solarAzimuthDegrees:0));double sunDepth=0,shadeDepth=0;
        for(int i=0;i<200;i++){sunDepth+=sunward.EvaluateColumn(0,i*3).SnowDepthMeters;shadeDepth+=shaded.EvaluateColumn(0,i*3).SnowDepthMeters;}
        Check(shadeDepth>sunDepth*1.05,"solar aspect retains more static snow on shaded slopes than otherwise identical sun-facing slopes");
        var forestSampler=new SurfaceMaterialSampler(new TestField{Height=100,Climate=new SurfaceClimate(18,0,1000,35,-.0065)},Profile(snow:false));
        SurfaceColumn forest=default;double forestX=0;for(int i=0;i<100;i++){forestX=i*2.1;forest=forestSampler.EvaluateColumn(forestX,0);if(forest.BaseSurfaceMaterial==MaterialId.Grass)break;}
        Check(forest.OrganicDepthMeters>0&&forestSampler.SampleMatter(new WorldPoint(forestX,100-.05,0),forest).Material==MaterialId.Grass&&
            forestSampler.SampleMatter(new WorldPoint(forestX,100-forest.GroundCoverDepthMeters-forest.OrganicDepthMeters*.5,0),forest).Material==MaterialId.Litter&&
            forestSampler.SampleMatter(new WorldPoint(forestX,100-forest.GroundCoverDepthMeters-forest.OrganicDepthMeters-forest.SoilDepthMeters*.5,0),forest).Material==MaterialId.Dirt,
            "forest vertical stack places organic litter above dirt instead of burying it beneath soil");
        var iceSampler=new SurfaceMaterialSampler(new TestField{Biome=11,CurveX=.01,Climate=new SurfaceClimate(-12,0,2400,35,-.0065)},Profile());
        bool iceFound=false;for(int i=0;i<100;i++){var c=iceSampler.EvaluateColumn(0,i*3);if(c.IceDepthMeters>.1){iceFound=iceSampler.SampleMatter(new WorldPoint(0,4999.95,i*3),c).Material==MaterialId.Ice;break;}}
        Check(iceFound,"cold sheltered glacier cover can expose packed ice beneath snow");
        bool forced=true;for(int i=0;i<100;i++)forced&=sampler.RockAt(i*999, -i*21, i*333)==MaterialId.Granite;
        Check(forced,"geology profile override stays stable across provinces and depth");
        double sx=0,sz=0;SurfaceColumn snowColumn=default;bool found=false;
        for(int i=0;i<500;i++){sx=i*3.3;sz=i*2.7;snowColumn=sampler.EvaluateColumn(sx,sz);if(snowColumn.SnowDepthMeters>=.3){found=true;break;}}
        Check(found,"snow-layer matter fixture finds a supported physical deposit");
        Check(sampler.SampleMatter(new WorldPoint(sx,5000+.05,sz),snowColumn).Material==MaterialId.Snow,"snow thickness becomes actual material above original ground");
        Check(sampler.SampleMatter(new WorldPoint(sx,snowColumn.SurfaceHeightMeters+.01,sz),snowColumn).Material==MaterialId.Air,"air remains above generated snow instead of filling an enclosing volume");
        Check(sampler.SampleMatter(new WorldPoint(sx,4999.95,sz),snowColumn).Material!=MaterialId.Snow,"excavation below snow reveals the original surface layer");
        var layerSampler=new SurfaceMaterialSampler(wetland,Profile(snow:false));var lc=layerSampler.EvaluateColumn(0,0);
        Check(layerSampler.SampleMatter(new WorldPoint(0,100-.05,0),lc).Material==MaterialId.Peat&&
            layerSampler.SampleMatter(new WorldPoint(0,100-lc.GroundCoverDepthMeters-.1,0),lc).Material==MaterialId.Mud&&
            SurfaceProfile.IsRock(layerSampler.SampleMatter(new WorldPoint(0,90,0),lc).Material),"surface cover, wet soil and underlying bedrock form distinct matter strata");
        var decorated=new SurfaceWorldField(flat,Profile());var dc=decorated.Sampler.EvaluateColumn(sx,sz);
        Check(Math.Abs(decorated.Sample(sx,sz).HeightMeters-dc.SurfaceHeightMeters)<1e-9,"far heightfield and voxel column share the same physical snow surface");
        var db=decorated.QueryBounds(-100,-100,100,100);Check(db.Minimum==-12000&&Math.Abs(db.Maximum-(48000+Profile().MaxSnowDepthMeters))<1e-8,"surface wrapper conservatively expands LOD bounds by maximum snow depth");
        var aCol=sampler.EvaluateColumn(-1e9+.25,1e9+.75);sampler.EvaluateColumn(600,-200);var bCol=sampler.EvaluateColumn(-1e9+.25,1e9+.75);
        Check(Hash(aCol)==Hash(bCol),"global negative/continental coordinates are deterministic independent of query order");
        Check(new SurfaceMaterialSampler(flat,Profile().With(seed:123)).SourceIdentity!=sampler.SourceIdentity,"seed and profile changes enter source identity");
        var overrideProfile=Profile().With(climateOverride:new SurfaceClimate(10,0,500,35,-.0065));
        Check(overrideProfile.With(clearForcedBedrock:true,clearClimateOverride:true).ForcedBedrockMaterial==null&&overrideProfile.With(clearClimateOverride:true).ClimateOverride==null,"artist test overrides can be explicitly cleared");
        Check(Profile().Identity.Contains("catalog-v"+MaterialCatalog.Version)&&Profile().With(windAzimuthDegrees:200).Identity!=Profile().Identity&&Profile().With(solarAzimuthDegrees:100).Identity!=Profile().Identity,"surface cache identity includes material catalog and directional exposure settings");
        flat.Queries=0;var reuse=sampler.EvaluateColumn(32,64);int queries=flat.Queries;for(int y=0;y<256;y++)sampler.SampleMatter(new WorldPoint(32,4980+y*.1,64),reuse);
        Check(queries<=11&&flat.Queries==queries,"column analysis bounds source lookups and voxel filling reuses its result");
        // Warm then verify the actual hot path has no heap allocation.
        for(int i=0;i<40;i++)sampler.EvaluateColumn(i,33);
        long before=GC.GetAllocatedBytesForCurrentThread();for(int i=0;i<1000;i++)sampler.EvaluateColumn(i*3.3,74);long allocated=GC.GetAllocatedBytesForCurrentThread()-before;
        Check(allocated==0,"warmed surface-column analysis allocates no managed heap memory");
        Reject(()=>new SurfaceProfile(maxSnowDepthMeters:9),"snow depth beyond declared bound is rejected");
        Reject(()=>new SurfaceProfile(forcedBedrockMaterial:MaterialId.Sand),"sediment cannot masquerade as a forced bedrock province");
        Reject(()=>new SurfaceClimate(4,1000,1000,30,-.0065),"physical climate API rejects centi-Celsius seasonality values");
        Reject(()=>sampler.EvaluateColumn(double.NaN,0),"nonfinite terrain coordinates rejected");
        Reject(()=>sampler.EvaluateColumn(1e13,0),"coordinates beyond declared double precision scope rejected");
        Reject(()=>new SurfaceProfile().With(clearForcedBedrock:true,forcedBedrockMaterial:MaterialId.Stone),"ambiguous set-and-clear overrides rejected");
        var report=new{status="PASS",checks,scope="Shared static surface/geology/snow algorithms; not Unity, thermal snow physics or fluid simulation",surface_version=SurfaceProfile.Version,
            snow_fixture=new{bare,snowy,unique_depths=depths.Count,maximum_depth_m=maximum,mean_depth_m=meanFlat},rock_types=rocks.Count,adjacent_rock_boundaries=boundaries,
            column_source_queries=queries,hot_path_allocated_bytes=allocated,elapsed_ms=watch.Elapsed.TotalMilliseconds};
        if(args.Length>0)File.WriteAllText(args[0],JsonSerializer.Serialize(report,new JsonSerializerOptions{WriteIndented=true}));
        Console.WriteLine("All "+checks+" surface checks passed.");
    }
    static void CheckColumn(SurfaceColumn c)
    {
        if(double.IsNaN(c.SurfaceHeightMeters)||double.IsInfinity(c.SurfaceHeightMeters)||c.SnowDepthMeters<0||c.SnowDepthMeters>2.40000001||
            Math.Abs(c.SnowDepthMeters*10-Math.Round(c.SnowDepthMeters*10))>1e-7||!MaterialCatalog.IsValid(c.SurfaceMaterial)||(c.SurfaceTint>>24)!=255)
            throw new Exception("Invalid generated column");
    }
}
