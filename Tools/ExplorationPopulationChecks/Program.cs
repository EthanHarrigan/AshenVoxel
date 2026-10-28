using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AshenVoxel.Core;
using AshenVoxel.Core.Trees;
using AshenVoxel.WorldScale;
static class Program
{
 static int checks;
 static void Check(bool yes,string message){if(!yes)throw new Exception(message);checks++;Console.WriteLine("PASS "+message);}
 static void Reject(Action a,string message){bool yes=false;try{a();}catch(ArgumentException){yes=true;}catch(InvalidDataException){yes=true;}catch(EndOfStreamException){yes=true;}Check(yes,message);}
 sealed class Field:IWorldField,IClimateWorldField
 {
  readonly byte biome;readonly bool land;readonly double height,temperature;public Field(byte b=6,bool land=true,double height=double.NaN,double temperature=double.NaN){biome=b;this.land=land;this.height=double.IsNaN(height)?(land?120:-10):height;this.temperature=temperature;}
  public string SourceIdentity=>"controlled-population/"+biome;
  public SurfaceSample Sample(double x,double z)=>new SurfaceSample(height,biome,6,land);
  public bool TrySampleClimate(double x,double z,out SurfaceClimate c){c=new SurfaceClimate(!double.IsNaN(temperature)?temperature:biome==9?5:15,3,biome==8?2300:biome==1||biome==13?120:1300,30,-.0065);return true;}
 }
 static ExplorationPopulation Population(byte b=6)=>new ExplorationPopulation(new SurfaceMaterialSampler(new Field(b),new SurfaceProfile(enableSnow:false)),714103);
 static string Key(PopulationInstance p)=>p.PrototypeId+"/"+p.StableKey+"/"+p.Position.X.ToString("R",System.Globalization.CultureInfo.InvariantCulture)+"/"+p.Position.Z.ToString("R",System.Globalization.CultureInfo.InvariantCulture);
 static string HashMesh(PrototypeMeshData data)
 {
  using(var stream=new MemoryStream())using(var w=new BinaryWriter(stream,Encoding.UTF8,true))
  {for(int i=0;i<data.VertexCount;i++){w.Write(data.Positions[i*3]);w.Write(data.Positions[i*3+1]);w.Write(data.Positions[i*3+2]);w.Write(data.Normals[i]);w.Write(data.Colors[i]);}foreach(int value in data.Indices)w.Write(value);w.Flush();return Convert.ToHexString(SHA256.HashData(stream.ToArray())).ToLowerInvariant();}
 }
 static byte[] OpticsFixture(int version,int material=7,int quadCount=1)
 {
  byte[] raw;using(var stream=new MemoryStream())using(var w=new BinaryWriter(stream)){
   w.Write(0);w.Write((byte)0);w.Write((ushort)1);w.Write((byte)'a');foreach(float v in new float[]{0,0,0,1,1,1})w.Write(v);w.Write((byte)1);
   if(version==1){w.Write(4);w.Write(6);foreach(var p in new[]{new short[]{0,0,0},new short[]{0,0,1},new short[]{1,0,1},new short[]{1,0,0}}){foreach(short v in p)w.Write(v);w.Write((byte)2);w.Write(0xff336699u);}foreach(int i in new[]{0,1,2,0,2,3})w.Write(i);}
   else{w.Write(quadCount);w.Write(1);w.Write(new byte[]{153,102,51});w.Write(new byte[]{0,0,0,1,1,2,0,255});if(version==4)w.Write((byte)material);}
   w.Write(0);if(version>=3){w.Write(0);w.Write(0);}raw=stream.ToArray();
  }
  byte[] packed;using(var s=new MemoryStream()){using(var gz=new GZipStream(s,CompressionLevel.Optimal,true))gz.Write(raw,0,raw.Length);packed=s.ToArray();}
  using(var s=new MemoryStream())using(var w=new BinaryWriter(s)){w.Write(Encoding.ASCII.GetBytes("AVPM000"+version));w.Write(1);w.Write(packed.Length);w.Write(raw.Length);w.Write(ExplorationPrototypeReader.Crc(raw));w.Write(packed);return s.ToArray();}
 }
 static void ReadOne(byte[] b){using var r=new ExplorationPrototypeReader(b);r.TryRead(out _);}
 static void Main(string[] args)
 {
  for(int version=1;version<=4;version++){
   using var fixture=new ExplorationPrototypeReader(OpticsFixture(version));fixture.TryRead(out var proto);
   Check(proto.Mesh(0).Colors.All(c=>VertexAppearance.Material((byte)(c>>24))==(version==4?7:31)&&VertexAppearance.Occlusion((byte)(c>>24))==255),"format"+version+" migration preserves AO and explicit material/legacy identity");
  }
  Reject(()=>ReadOne(OpticsFixture(4,255)),"format4 rejects unknown material even with valid CRC");
  Reject(()=>ReadOne(OpticsFixture(4,0)),"format4 rejects air render faces even with valid CRC");
  Reject(()=>ReadOne(OpticsFixture(4,7,1000001)),"format4 rejects geometry exceeding allocation limits");
  byte[] minimal=OpticsFixture(4);Reject(()=>ReadOne(minimal.Take(minimal.Length-1).ToArray()),"format4 rejects truncated compressed payload");
  var population=Population();var a=population.Build(new PopulationKey(-1,-1,0));var b=population.Build(new PopulationKey(0,-1,0));
  Check(a.Instances.Length>0&&a.Instances.Select(Key).SequenceEqual(population.Build(new PopulationKey(-1,-1,0)).Instances.Select(Key)),"global population cells repeat exact prototype selection and positions");
  Check(!a.Instances.Select(x=>x.StableKey).Intersect(b.Instances.Select(x=>x.StableKey)).Any(),"adjacent cells have disjoint candidate ownership");
  Check(a.Instances.All(x=>x.Position.X>=-64&&x.Position.X<0&&x.Position.Z>=-64&&x.Position.Z<0&&x.Scale==1),"negative ownership bounds preserve physical10cm cell scale without instance scaling");
  var reordered=population.Build(new PopulationKey(0,-1,0));Check(b.Instances.Select(Key).SequenceEqual(reordered.Instances.Select(Key)),"request order never changes cell RNG state");
  var habitats=new HashSet<ForestHabitat>();var stages=new HashSet<ForestStage>();double minYoung=double.MaxValue,maxOld=0;
  for(int z=-30000;z<=30000;z+=1000)for(int x=-30000;x<=30000;x+=1000)
  {ForestStand s=population.StandAt(x,z);habitats.Add(s.Habitat);stages.Add(s.Stage);if(s.Stage==ForestStage.YoungDense)minYoung=Math.Min(minYoung,s.Density);else maxOld=Math.Max(maxOld,s.Density);}
  Check(habitats.SetEquals(new[]{ForestHabitat.OakWoodland,ForestHabitat.BirchGrove}),"temperate forests select coherent oak or birch regions instead of mixing all trees");
  Check(stages.Count==2&&minYoung>maxOld,"young forest patches are denser than mature open patches");
  Check(Population(9).StandAt(0,0).Species==TreeSpecies.Pine,"source taiga retains pine dominance despite forest display override");
  Check(Population(8).StandAt(0,0).Species==TreeSpecies.Redwood,"warm wet temperate rainforest can select redwood stands");
  foreach(byte biome in new byte[]{1,2,10,11,13})Check(Population(biome).Build(new PopulationKey(0,0,0)).Instances.All(x=>x.PrototypeId>=LabFlora.First),"source biome"+biome+" does not receive inappropriate forest trees");
  var disabled=Population();disabled.TreeDensity=0;Check(disabled.Build(new PopulationKey(0,0,0)).Instances.Length==0,"tree density zero removes trees without changing terrain");
  var plants=population.Build(new PopulationKey(0,0,2));Check(plants.Instances.All(x=>(x.PrototypeId>=100&&x.PrototypeId<=122||LabFlora.IsLab(x.PrototypeId))&&x.Scale==1)&&plants.Instances.Any(x=>x.PrototypeId<=122)&&plants.Instances.Any(x=>LabFlora.IsLab(x.PrototypeId)),"understory selects actual plant prototype variants and preserves voxel scale");
  var rocks=population.Build(new PopulationKey(0,0,1));Check(rocks.Instances.All(x=>(x.PrototypeId>=200&&x.PrototypeId<=259||LabFlora.IsLab(x.PrototypeId))&&x.Scale==1),"rock family/material/size are independent baked10cm variants");
  {
   ExplorationPopulation P(byte b,bool land=true,double h=double.NaN,double t=double.NaN)=>new ExplorationPopulation(new SurfaceMaterialSampler(new Field(b,land,h,t),new SurfaceProfile(enableSnow:false)),714103);
   int[] Ids(ExplorationPopulation p,int layer,int n=4){var r=new List<int>();for(int z=0;z<n;z++)for(int x=0;x<n;x++)r.AddRange(p.Build(new PopulationKey(x*37,z*53,layer)).Instances.Select(i=>i.PrototypeId));return r.ToArray();}
   var desert=P(1);var dCanopy=Ids(desert,0,8);
   Check(dCanopy.Any(i=>i>=LabFlora.SaguaroFourArm&&i<=LabFlora.SaguaroTwoArm+1),"hot desert grows saguaro stands in the canopy tier ("+dCanopy.Length+" in 8x8 cells)");
   Check(Ids(desert,1).Any(i=>i==LabFlora.CreosoteOpenPaired||i==LabFlora.CreosoteOpenPaired+1||i>=LabFlora.ChollaCandelabra&&i<=LabFlora.ChollaTree+1),"hot desert midstory has creosote or cholla");
   var jungle=P(7);int[] ju=Ids(jungle,2),jm=Ids(jungle,1);
   Console.WriteLine("jungle understory "+ju.Length+" lab "+ju.Count(LabFlora.IsLab)+" mid "+jm.Length+" midlab "+jm.Count(LabFlora.IsLab));
   Check(ju.Count(LabFlora.IsLab)>ju.Length*.2&&jm.Any(i=>i>=LabFlora.JungleBeehiveGinger&&i<=LabFlora.JungleYoungBanana+1),"rainforest floor gets dense lab understory and a ginger/heliconia/banana midstory");
   var taiga=P(9);Check(Ids(taiga,2).Any(i=>i==LabFlora.LichenReindeerMat||i==LabFlora.LichenReindeerMat+1||i==LabFlora.BearberryCreepingRed||i==LabFlora.BearberryCreepingRed+1),"boreal floor gets lichen/bearberry");
   var tundra=P(10);Check(Ids(tundra,2).Any(i=>i==LabFlora.AlpineCushion||i==LabFlora.AlpineCushion+1||i==LabFlora.WillowProstrateCatkins||i==LabFlora.WillowProstrateCatkins+1),"tundra gets cushions and prostrate willow");
   var warmSea=P(0,false,-4,26);int[] reef=Ids(warmSea,2,8).Concat(Ids(warmSea,1,6)).ToArray();
   Check(reef.Any(i=>i>=LabFlora.CoralDigitateFingers&&i<=LabFlora.CoralTieredPlates+1)&&reef.Any(i=>i>=LabFlora.MarineManateegrass&&i<=LabFlora.MarineTurtlegrass+1||i>=LabFlora.ReefIsolatedPatch&&i<=LabFlora.ReefSpurGrooveSlice+1),"warm shallow sea grows coral reefs and seagrass");
   var coldSea=P(0,false,-6,8);int[] kelp=Ids(coldSea,1,8);Check(kelp.Any(i=>i>=LabFlora.MarineBullKelp&&i<=LabFlora.MarineBullKelp+1||i>=LabFlora.MarineGiantKelpYoung&&i<=LabFlora.MarineGiantKelpYoung+1),"cold sea grows kelp forests");
   Check(Ids(P(11),2).All(i=>!LabFlora.IsLab(i)),"glacier receives no lab flora");
   var deep=P(0,false,-60,26);Check(Ids(deep,2).Length==0&&Ids(deep,1).Length==0,"deep water stays bare");
   int infill=0,trees=0;var forest=P(6);
   for(int z=-40;z<40;z+=4)for(int x=-40;x<40;x+=4){var cell=forest.Build(new PopulationKey(x,z,0));trees+=cell.Instances.Count(i=>i.PrototypeId<16);}
   Check(trees>0,"temperate forest still grows trees ("+trees+" sampled)");
   double maxDensity=0;for(int z=-30000;z<=30000;z+=500)for(int x=-30000;x<=30000;x+=500)maxDensity=Math.Max(maxDensity,forest.StandAt(x,z).Density);
   Check(maxDensity>1.1,"young thickets exceed one tree per 6 m grid point (max stand density "+maxDensity.ToString("F2")+") so the infill grid engages");
   var cellKeys=forest.Build(new PopulationKey(3,-7,0)).Instances.Select(i=>i.StableKey).ToArray();Check(cellKeys.Distinct().Count()==cellKeys.Length,"tree, thicket infill and canopy flora candidates have distinct stable keys");
   _=infill;
  }
  Reject(()=>new PopulationKey(0,0,3),"unknown population layers reject");
  Reject(()=>population.Build(new PopulationKey(long.MaxValue,0,0)),"unsupported global cell keys reject before loops");
  var softModel=new ExplorationPrototype(0,0,"span fixture",new[]{0f,0f,0f,.2f,.1f,.1f},Array.Empty<PrototypeMeshData>(),new Dictionary<int,PrototypeCollisionColumn>(),
   new Dictionary<int,PrototypeCollisionColumn>{{0,new PrototypeCollisionColumn(0,0,new short[]{0,1})}},new Dictionary<int,PrototypeCollisionColumn>{{1,new PrototypeCollisionColumn(1,0,new short[]{0,1})}});
  Check(Math.Abs(softModel.ResistanceLocal(0,0,0,.1,.1,.1)-1)<1e-8,"one leaf voxel supplies exactly one overlap unit");
  Check(Math.Abs(softModel.ResistanceLocal(.1,0,0,.2,.1,.1)-4)<1e-8,"same-volume stick supplies four leaf-equivalent overlap units");
  Check(Math.Abs(softModel.ResistanceLocal(0,0,0,.05,.1,.1)-.5)<1e-8,"fractional body contact scales resistance by intersected volume");
  Check(!softModel.BlocksLocal(0,0,0,.2,.1,.1)&&!softModel.SupportLocal(0,0,.1,0,1,out _),"leaf/stick density never becomes a blocking surface or support");
  if(args.Length>1)
  {
   byte[] pack=File.ReadAllBytes(args[1]);var names=new HashSet<int>();int totalLods=0;long vertices=0;var models=new Dictionary<int,ExplorationPrototype>();
   using(var reader=new ExplorationPrototypeReader(pack))
   {
    Check(reader.FormatVersion==4&&reader.Count>=99&&reader.Count<=ExplorationPrototypeReader.MaximumModels,"published pack uses version4 material-aware exact-quads plus separate body/resistance spans: 99 core records plus "+(reader.Count-99)+" foliage-lab records");
    Check(ExplorationPrototypeReader.CanonicalIdentity(pack)=="41556b474ce7a26342e4dd7bcff2e4fe658ab0333e8360058262ee21c0f011aa","canonical core-pack identity is unchanged by appended lab records (tree/shrub/root pins stay valid)");
    while(reader.TryRead(out ExplorationPrototype p))
    {
     names.Add(p.Id);models.Add(p.Id,p);bool valid=true;
     if(LabFlora.IsLab(p.Id)){for(int lod=0;lod<p.LodCount;lod++)valid&=p.Mesh(lod).VertexCount>0&&p.Mesh(lod).Indices.Length%3==0;Check(valid&&p.LodCount>=4,p.Name+" lab record decodes all LODs");p.ReleaseMeshData();continue;}
     totalLods+=p.LodCount;
     for(int lod=0;lod<p.LodCount;lod++)
     {
      PrototypeMeshData mesh=p.Mesh(lod);vertices+=mesh.VertexCount;
      using var meta=JsonDocument.Parse(File.ReadAllText(args[2]));
      JsonElement record=meta.RootElement.GetProperty("records").EnumerateArray().First(r=>r.GetProperty("id").GetInt32()==p.Id);
      string expected=record.GetProperty("roundtrip_meshes")[lod].GetProperty("exact_expanded_sha256").GetString();valid&=HashMesh(mesh)==expected;
     }
     Check(valid,p.Name+" expanded positions/normals/RGB/AO/indices match the original mesher bytes exactly");
     p.ReleaseMeshData();
    }
   }
   Check(names.Count(i=>i<LabFlora.First)==99&&totalLods==412&&vertices>10000000,"all full-resolution,near,mid andfar prototype LODs are present");
   var fern=models[100];Check(!fern.HasCollision&&!fern.BlocksLocal(-.1,0,-.1,.1,.2,.1)&&!fern.SupportLocal(0,0,.2,-1,1,out _),"visible fern tissue is passable and supplies no false support");
   Check(fern.HasResistance&&fern.ResistanceLocal(fern.MinX,fern.MinY,fern.MinZ,fern.MaxX,fern.MaxY,fern.MaxZ)>100,"actual fern cells produce resistance after visual data is released");
   var shrub=models[102];Check(!shrub.HasCollision&&shrub.HasResistance&&shrub.ResistanceLocal(shrub.MinX,shrub.MinY,shrub.MinZ,shrub.MaxX,shrub.MaxY,shrub.MaxZ)>700,"thin woody plant stems pass and contribute stronger stick resistance");
   var birch=models[0];Check(birch.HasCollision&&birch.BlocksLocal(-.01,.1,-.01,.01,.2,.01),"exact young-birch wood remains a hard obstacle after releasing render geometry");
   Check(birch.SupportLocal(0,0,.1,-1,4,out double top)&&top>0,"wood collision columns expose real support heights");
   Check(!birch.BlocksLocal(100,0,100,100.1,.2,100.1),"distant local queries cannot alias packed negative column keys");
   Reject(()=>birch.BlocksLocal(double.NaN,0,0,1,1,1),"nonfinite body query rejects before integer casts");
   Reject(()=>birch.BlocksLocal(1,0,0,0,1,1),"inverted body query rejects");
   Reject(()=>birch.SupportLocal(0,0,double.PositiveInfinity,0,1,out _),"unbounded support radius rejects");
   Reject(()=>new ExplorationPrototypeReader(new byte[12]),"unknown prototype magic rejects");
   byte[] corrupt=(byte[])pack.Clone();corrupt[20]^=1;Reject(()=>{using var r=new ExplorationPrototypeReader(corrupt);r.TryRead(out _);},"valid compressed records require the original checksum");
   byte[] trailing=pack.Concat(new byte[]{1}).ToArray();Reject(()=>{using var r=new ExplorationPrototypeReader(trailing);while(r.TryRead(out var p))p.ReleaseMeshData();},"trailing pack data rejects");
   Check(models[10].MaxY>65&&models[11].MaxY>65,"mature redwood prototypes retain realistic75m-class scale");
   var used=new HashSet<int>();foreach(var f in typeof(LabFlora).GetFields())if(f.IsLiteral&&f.Name!="First"&&f.Name!="Last"){int id=(int)f.GetRawConstantValue();used.Add(id);}
   Check(used.All(id=>models.ContainsKey(id)&&models.ContainsKey(id+1)),"every LabFlora constant ("+used.Count+" keys x2 seeds) exists in the pack");
   Check(models.Values.Where(m=>LabFlora.IsLab(m.Id)).All(m=>m.Kind<=2),"lab records use tree/plant/rock kinds");
  }
  File.WriteAllText(args[0],JsonSerializer.Serialize(new{status="PASS",checks,scope="Actual engine-free population and exact-quad prototype decoder; not Unity/player/GPU verification",forest_habitats=habitats.Select(x=>x.ToString()),age_stages=stages.Select(x=>x.ToString())},new JsonSerializerOptions{WriteIndented=true}));Console.WriteLine("All "+checks+" exploration population checks passed.");
 }
}
