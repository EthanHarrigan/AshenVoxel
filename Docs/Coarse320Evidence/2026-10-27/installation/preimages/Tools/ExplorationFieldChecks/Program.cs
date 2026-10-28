using System;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Collections.Generic;
using AshenVoxel.WorldScale;
static class Program
{
 static int checks;static string id=new string('a',64);static void Check(bool yes,string name){if(!yes)throw new Exception("FAIL "+name);checks++;Console.WriteLine("PASS "+name);}
 static void Reject(Action a,string name){bool bad=false;try{a();}catch(ArgumentException){bad=true;}catch(InvalidDataException){bad=true;}Check(bad,name);}
 static HeightAtlas Height(int cols,int rows,double x,double z,double w,double d,float value,byte biome=6,string world=null)
 {var h=new float[cols*rows];var b=new byte[h.Length];var land=new bool[h.Length];for(int i=0;i<h.Length;i++){h[i]=value;b[i]=biome;land[i]=value>=0;}return new HeightAtlas(cols,rows,x,z,w,d,h,b,land,"{\"provider\":\"InfiniteDiffusion\",\"world_id\":\""+(world??id)+"\"}");}
 static ClimateAtlas Climate(HeightAtlas h,float temp=10,float season=4,float rain=1200,float cv=30,float beta=-.005f)
 {int count=h.Columns*h.Rows;var data=new float[count*5];for(int i=0;i<count;i++){data[i]=temp;data[i+count]=season;data[i+2*count]=rain;data[i+3*count]=cv;data[i+4*count]=beta;}return new ClimateAtlas(h.Columns,h.Rows,h.OriginX,h.OriginZ,h.ExtentX,h.ExtentZ,ClimateArchiveIO.HeightHash(h),h.SourceIdentity,data);}
 static ExplorationAtlasField Field(double vertical=1,double width=2000,double depth=2000,ExplorationBiomeAtlas biomes=null)
 {var m=Height(5,5,-1000,-1000,2000,2000,100,6);var f=Height(5,5,0,-500,800,1000,200,8);return new ExplorationAtlasField(m,Climate(m),f,Climate(f,5),fineBlendMeters:200,worldWidthMeters:width,worldDepthMeters:depth,verticalScale:vertical,biomeAtlas:biomes,originalWidthMeters:2000,originalDepthMeters:2000);}
 static void Main(string[] args)
 {
  var field=Field();Check(field.WorldId==id&&field.HasDecodedDetail,"paired field retains checked neural world identity and decoded availability");
  Check(field.Sample(-500,0).HeightMeters==100&&field.Sample(400,0).HeightMeters==200,"off-center decoded crop uses its actual global origin rather than recentering at zero");
  Check(field.FineWeightAt(0,0)==0&&field.Sample(0,0).HeightMeters==100&&field.FineWeightAt(400,0)==1,"fine boundaries match macro and deep interior is exact decoded terrain");
  Check(Math.Abs(field.Sample(100,0).HeightMeters-150)<1e-9,"static smooth blend uses the same height result for any caller");
  Check(Math.Abs(field.Sample(.00001,0).HeightMeters-field.Sample(-.00001,0).HeightMeters)<1e-6,"fine boundary has continuous elevation without a camera-dependent terrain jump");
  Check(field.TrySampleClimate(100,0,out var climate)&&Math.Abs(climate.TemperatureC-7.5)<1e-6,"climate adjusts each source only to blended elevation, without double original lapse");
  Check(Field(2).TrySampleClimate(100,0,out climate)&&Math.Abs(climate.TemperatureC-6.75)<1e-5,"vertical scaling applies temperature lapse only for additional final height");
  var small=Field(width:1000,depth:1000);Check(small.Sample(200,0).HeightMeters==200&&small.FineMinimumX==0&&small.FineMaximumX==400,"horizontal resizing transforms the off-center crop and base consistently");
  Check(small.ModelResolutionMeters(-300,0)==250&&small.ModelResolutionMeters(200,0)==125,"source spacing reports the horizontal transform rather than claiming voxel-generated relief");
  Check(field.Sample(2000,0).HeightMeters==-1000&&!field.Sample(2000,0).IsLand&&!field.TrySampleClimate(2000,0,out climate),"outside authored map is explicit ocean context with unavailable climate");
  bool bound=true;var random=new Random(714103);for(int n=0;n<1000;n++)
  {double x0=random.NextDouble()*2600-1300,z0=random.NextDouble()*2600-1300,x1=x0+random.NextDouble()*300,z1=z0+random.NextDouble()*300;HeightBounds b=field.QueryBounds(x0,z0,x1,z1);for(int p=0;p<5;p++){double h=field.Sample(x0+(x1-x0)*random.NextDouble(),z0+(z1-z0)*random.NextDouble()).HeightMeters;bound&=h>=b.Minimum&&h<=b.Maximum;}}
  Check(bound,"5000 arbitrary sampled heights remain inside declared conservative bounds across blend and map edges");
  var categories=new ExplorationBiomeAtlas(2,2,-1000,-1000,1000,id,new byte[]{1,6,9,12});var explicitBiomes=Field(biomes:categories);
  Check(explicitBiomes.Sample(-.01,-.01).SourceBiome==1&&explicitBiomes.Sample(0,-.01).SourceBiome==6&&explicitBiomes.Sample(-.01,0).SourceBiome==9,"source biome categories use exact global half-open cells across negative coordinates");
  Check(explicitBiomes.Sample(400,0).SourceBiome==12&&field.Sample(400,0).SourceBiome==8,"explicit source cells remain independent of fine/macro height blending");
  Check(categories.Sample(-1000,-1000)==1&&categories.Sample(1000,1000)==0,"source biome cell bounds are half-open with explicit exterior marine ID");
  var altered=Field(biomes:new ExplorationBiomeAtlas(2,2,-1000,-1000,1000,id,new byte[]{1,6,9,3}));Check(altered.SourceIdentity!=explicitBiomes.SourceIdentity,"same-grid biome changes alter source identity through a payload digest");
  var macro=Height(3,3,-1000,-1000,2000,2000,100);var only=new ExplorationAtlasField(macro,Climate(macro),worldWidthMeters:2000,worldDepthMeters:2000,originalWidthMeters:2000,originalDepthMeters:2000);
  Check(!only.HasDecodedDetail&&only.FineWeightAt(0,0)==0&&only.Sample(0,0).HeightMeters==100,"coarse-only mode is explicit and does not invent a decoded sample");
  Reject(()=>new ExplorationAtlasField(macro,Climate(Height(3,3,-1000,-1000,2000,2000,101)),worldWidthMeters:2000,worldDepthMeters:2000,originalWidthMeters:2000,originalDepthMeters:2000),"wrong paired climate height hash is rejected");
  var foreign=Height(3,3,0,0,100,100,200,world:new string('b',64));Reject(()=>new ExplorationAtlasField(macro,Climate(macro),foreign,Climate(foreign),worldWidthMeters:2000,worldDepthMeters:2000,originalWidthMeters:2000,originalDepthMeters:2000),"detail belonging to another model world is rejected");
  Reject(()=>new ExplorationAtlasField(macro,Climate(macro),biomeAtlas:new ExplorationBiomeAtlas(1,1,0,0,100,new string('b',64),new byte[]{6}),worldWidthMeters:2000,worldDepthMeters:2000,originalWidthMeters:2000,originalDepthMeters:2000),"different-world categorical maps cannot silently retag the terrain");
  Reject(()=>new ExplorationAtlasField(macro,Climate(macro),fine:macro,worldWidthMeters:2000,worldDepthMeters:2000,originalWidthMeters:2000,originalDepthMeters:2000),"unpaired decoded height requires its matching climate");
  Reject(()=>new ExplorationAtlasField(macro,Climate(macro),fineBlendMeters:0,worldWidthMeters:2000,worldDepthMeters:2000,originalWidthMeters:2000,originalDepthMeters:2000),"zero-width discontinuous blend is rejected");
  Reject(()=>new ExplorationAtlasField(macro,Climate(macro),worldWidthMeters:double.NaN),"nonfinite client sizing is rejected");
  Reject(()=>new ExplorationAtlasField(macro,Climate(macro)),"undersized macro cannot masquerade as whole-map coverage");
  Reject(()=>field.QueryBounds(1,0,0,1),"inverted conservative bounds are rejected");
  if(args.Length>1)
  {
   string path=args[1];HeightAtlas m,f;ClimateAtlas cm,cf;ExplorationBiomeAtlas bi;
   using(var s=File.OpenRead(Path.Combine(path,"ExplorationMacro.bytes")))m=AtlasIO.Read(s);using(var s=File.OpenRead(Path.Combine(path,"ExplorationMacro.climate.bytes")))cm=ClimateArchiveIO.Read(s);
   using(var s=File.OpenRead(Path.Combine(path,"ExplorationFine.bytes")))f=AtlasIO.Read(s);using(var s=File.OpenRead(Path.Combine(path,"ExplorationFine.climate.bytes")))cf=ClimateArchiveIO.Read(s);using(var s=File.OpenRead(Path.Combine(path,"ExplorationBiomes.bytes")))bi=ExplorationBiomeAtlasIO.Read(s);
   var actual=new ExplorationAtlasField(m,cm,f,cf,biomeAtlas:bi);var metadata=JsonDocument.Parse(File.ReadAllText(Path.Combine(path,"exploration-world.json")));var spawn=metadata.RootElement.GetProperty("recommended_spawn");double x=spawn.GetProperty("x_m").GetDouble(),z=spawn.GetProperty("z_m").GetDouble();SurfaceSample terrain=actual.Sample(x,z);
   Check(actual.WorldWidthMeters==299520&&actual.WorldDepthMeters==149760&&m.Columns==235&&m.Rows==118,"actual model samples preserve the exact scale5 map extent");
   Check(actual.WorldId==metadata.RootElement.GetProperty("world_id").GetString()&&actual.FineWeightAt(x,z)==1,"actual recommended spawn is in full decoded terrain with matching view identity");
   Check(actual.ModelResolutionMeters(x,z)==5&&actual.TrySampleClimate(x,z,out climate)&&terrain.IsLand&&terrain.HeightMeters>0,"actual native model decode supplies5m geometry and physical climate at spawn");
   byte[] damaged=File.ReadAllBytes(Path.Combine(path,"ExplorationBiomes.bytes"));damaged[damaged.Length-5]^=1;Reject(()=>ExplorationBiomeAtlasIO.Read(new MemoryStream(damaged)),"source biome archive corruption is rejected before publication");
   Console.WriteLine("ACTUAL EXPLORATION "+terrain.HeightMeters+"m, biome "+terrain.SourceBiome+", climate "+climate.TemperatureC+"C");
  }
  File.WriteAllText(args[0],JsonSerializer.Serialize(new{status="PASS",checks,scope="Actual engine-free static neural atlas blend/units/categorical transform; not Unity or fresh inference"},new JsonSerializerOptions{WriteIndented=true}));Console.WriteLine("All "+checks+" exploration field checks passed.");
 }
}
