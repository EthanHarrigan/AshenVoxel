using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.IO;
using AshenVoxel.Core;
using AshenVoxel.Core.Details;
using AshenVoxel.WorldScale;
static class Program
{
 static int checks;
 static void Check(bool yes,string name){if(!yes)throw new Exception(name);checks++;Console.WriteLine("PASS "+name);}
 static void Invalid(Action a,string name){bool rejected=false;try{a();}catch(ArgumentException){rejected=true;}Check(rejected,name);}
 sealed class Field:IWorldField,IClimateWorldField
 {
  readonly byte biome,effective;readonly double h,temp,rain,slope;readonly bool land;
  public double MinX=double.MaxValue,MaxX=double.MinValue,MinZ=double.MaxValue,MaxZ=double.MinValue;public int Queries;
  public Field(byte biome=6,double h=1.05,double temp=14,double rain=1300,double slope=0,bool land=true,byte effective=6)
  {this.biome=biome;this.h=h;this.temp=temp;this.rain=rain;this.slope=slope;this.land=land;this.effective=effective;}
  public string SourceIdentity=>"controlled-groundcover-test/"+biome+"/"+effective;
  public SurfaceSample Sample(double x,double z){Queries++;MinX=Math.Min(MinX,x);MaxX=Math.Max(MaxX,x);MinZ=Math.Min(MinZ,z);MaxZ=Math.Max(MaxZ,z);return new SurfaceSample(h+x*slope,biome,effective,land);}
  public bool TrySampleClimate(double x,double z,out SurfaceClimate c){c=new SurfaceClimate(temp,2,rain,30,-.0065);return true;}
 }
 static GroundCoverScatter Scatter(Field f,int seed=714103,int candidates=10000,int placements=4096,double density=1,bool plants=true,bool rocks=true,MaterialId bedrock=MaterialId.Granite)
 =>new GroundCoverScatter(new SurfaceMaterialSampler(f,new SurfaceProfile(seed,enableSnow:false,forcedBedrockMaterial:bedrock)),new GroundCoverProfile(seed,density,plants,rocks,candidates,placements));
 static string Key(GroundCoverPlacement p)=>p.Layer+":"+p.GridX+":"+p.GridZ+":"+p.Anchor.X+":"+p.Anchor.Y+":"+p.Anchor.Z+":"+p.Seed+":"+(p.IsRock?"rock":p.Plant.Kind.ToString());
 static string[] Keys(GroundCoverPlan p)=>p.Placements.Select(Key).OrderBy(x=>x,StringComparer.Ordinal).ToArray();
 static ulong Signature(VoxelWorld w)
 {
  ulong h=1469598103934665603;unchecked{for(int z=0;z<w.SizeZ;z++)for(int y=0;y<w.SizeY;y++)for(int x=0;x<w.SizeX;x++)
  {var m=w.Get(x,y,z);if(m==MaterialId.Air)continue;h=(h^(ulong)(x+1000*y+1000000*z))*1099511628211;h=(h^(byte)m)*1099511628211;h=(h^w.GetTint(x,y,z))*1099511628211;}}return h;
 }
 static VoxelWorld Ground(){var w=new VoxelWorld(200,64,200,.1f,714103);for(int z=0;z<200;z++)for(int x=0;x<200;x++)w.Set(x,10,z,MaterialId.Grass,0xff506438);return w;}
 static void Main(string[] args)
 {
  var field=new Field();var s=Scatter(field);var p=s.Plan(-8,-8,8,8);
  Check(!p.Truncated&&p.Placements.Count>50,"forest floor has bounded nonempty habitat variety");
  Check(Keys(p).SequenceEqual(Keys(s.Plan(-8,-8,8,8))),"same global seed/profile repeats candidate ownership and plant kinds");
  Check(!Keys(p).SequenceEqual(Keys(Scatter(new Field(),19).Plan(-8,-8,8,8))),"new seed changes plant and rock placement");
  var q=new List<GroundCoverPlacement>();foreach(var rect in new[]{new[]{-8d,-8,0,0},new[]{0d,-8,8,0},new[]{-8d,0,0,8},new[]{0d,0,8,8}})q.AddRange(s.Plan(rect[0],rect[1],rect[2],rect[3]).Placements);
  Check(Keys(p).SequenceEqual(q.Select(Key).OrderBy(x=>x,StringComparer.Ordinal)),"four half-open region plans equal one whole plan without duplicates");
  Check(p.Placements.All(v=>v.Anchor.Center.X>=-8&&v.Anchor.Center.X<8&&v.Anchor.Center.Z>=-8&&v.Anchor.Center.Z<8),"negative global candidates remain inside requested half-open ownership");
  Check(p.Placements.All(v=>v.Anchor.Y==11),"first-air anchor is correct when terrain lies exactly at a10cm cell center");
  bool spacing=true;for(int i=0;i<p.Placements.Count;i++)for(int j=i+1;j<p.Placements.Count;j++)
  {var a=p.Placements[i];var b=p.Placements[j];if(a.Layer!=b.Layer)continue;double dx=(a.Anchor.X-b.Anchor.X)*.1,dz=(a.Anchor.Z-b.Anchor.Z)*.1,d=a.Layer==0?1.9:a.Layer==1?1.45:.64;spacing&=dx*dx+dz*dz>=d*d-1e-7;}
  Check(spacing,"same-layer hard-core distances hold independently of terrain/LOD");
  var rockOnly=Scatter(new Field(),density:2,plants:false).Plan(-32,-32,32,32);
  var families=new HashSet<AshenVoxel.Core.Rocks.RockShape>();bool shapedDimensions=true,sawSlab=false,sawSpire=false;
  foreach(var placement in rockOnly.Placements.Concat(Scatter(new Field(),density:2,plants:false,bedrock:MaterialId.Stone).Plan(-32,-32,32,32).Placements))
  {
   var rock=placement.Rock;families.Add(rock.ResolvedShape);
   if(rock.ResolvedShape==AshenVoxel.Core.Rocks.RockShape.Slab){sawSlab=true;shapedDimensions&=rock.HeightMeters/rock.WidthMeters<.28;}
   if(rock.ResolvedShape==AshenVoxel.Core.Rocks.RockShape.Spire){sawSpire=true;shapedDimensions&=rock.HeightMeters/rock.WidthMeters>1.4;}
  }
  Check(families.Count>=4&&sawSlab&&sawSpire&&shapedDimensions,"world rock scatter preserves multiple resolved shape families with flat slabs and tall spire proportions");
  Check(field.MinX>=-40.00001&&field.MaxX<=40.00001&&field.MinZ>=-40.00001&&field.MaxZ<=40.00001,"placement queries fit the existing32m material halo; neighbors need only hashes");
  Check(Keys(Scatter(new Field(1,temp:30,rain:65,effective:6)).Plan(-16,-16,16,16)).SequenceEqual(Keys(Scatter(new Field(1,temp:30,rain:65,effective:1)).Plan(-16,-16,16,16))),"forest vegetation override does not change source-desert ground plants");
  var habitats=new Dictionary<byte,int>();
  for(byte biome=1;biome<=13;biome++)
  {
   var f=new Field(biome,temp:biome==11?-20:biome==10?-2:biome==2?8:20,rain:biome==1||biome==2||biome==13?90:biome==12?5000:1400);
   var hp=Scatter(f).Plan(-16,-16,16,16);var plants=hp.Placements.Where(v=>!v.IsRock).ToArray();habitats[biome]=plants.Length;
   Check(biome==11?plants.Length==0:plants.Length>0,"source biome"+biome+" uses intentional exposed-ground habitat rules");
   if(biome==1||biome==13)Check(plants.All(v=>v.Plant.Kind==PlantKind.Cactus||v.Plant.Kind==PlantKind.Succulent||v.Plant.Kind==PlantKind.GrassTuft),"desert biome"+biome+" has xeric forms rather than forest ferns");
   if(biome==10)Check(plants.All(v=>v.Plant.Kind==PlantKind.Cushion||v.Plant.Kind==PlantKind.GrassTuft),"tundra has low cushions and grass rather than tropical bushes");
   if(biome==12)Check(plants.Any(v=>v.Plant.Kind==PlantKind.Reed),"wet emergent ground includes reed clusters");
  }
  Check(Scatter(new Field(0,-2,land:false)).Plan(-8,-8,8,8).Placements.Count==0,"marine and submerged ground has no terrestrial clutter");
  Check(Scatter(new Field(slope:1)).Plan(-8,-8,8,8).Placements.Count==0,"steep slopes reject rooted clutter and boulders");
  var dry=Scatter(new Field(6,temp:20,rain:60)).Plan(-16,-16,16,16);
  Check(dry.Placements.Where(v=>!v.IsRock).All(v=>v.Plant.Kind!=PlantKind.Fern&&v.Plant.Kind!=PlantKind.Mushroom),"actual dry climate overrides wet-forest fern and fungus choices");
  var snowS=new GroundCoverScatter(new SurfaceMaterialSampler(new Field(6,temp:-18,rain:5000),new SurfaceProfile(maxSnowDepthMeters:5,forcedBedrockMaterial:MaterialId.Granite)));
  var snowy=snowS.Plan(-16,-16,16,16);
  Check(snowy.Placements.All(v=>snowS.Surface.EvaluateColumn(v.Anchor.Center.X,v.Anchor.Center.Z).SnowDepthMeters<.099),"snow-covered columns do not spawn plants or rocks on top of snow");
  Check(Scatter(new Field(),density:0).Plan(-8,-8,8,8).Placements.Count==0,"density zero removes all new clutter without changing terrain");
  var disabled=Scatter(new Field(),plants:false,rocks:false).Plan(-8,-8,8,8);Check(disabled.Placements.Count==0&&disabled.ScannedCandidates==0,"disabled layers perform no candidate/source work");
  var limited=Scatter(new Field(),candidates:5).Plan(-100000,-100000,100000,100000);Check(limited.Truncated&&limited.ScannedCandidates==5,"huge requested regions stop at an observable candidate cap");
  limited=Scatter(new Field(),placements:2,density:2).Plan(-16,-16,16,16);Check(limited.Truncated&&limited.Placements.Count==2,"placement cap is bounded and observable");
  var old=CultureInfo.CurrentCulture;CultureInfo.CurrentCulture=new CultureInfo("fr-FR");string id=new GroundCoverProfile(density:.73).Identity;CultureInfo.CurrentCulture=old;
  Check(id==new GroundCoverProfile(density:.73).Identity,"profile identity is independent of decimal culture");
  Invalid(()=>new GroundCoverProfile(density:double.NaN),"nonfinite placement density is rejected");
  Invalid(()=>new GroundCoverProfile(maxCandidates:0),"zero candidate budget is rejected");
  Invalid(()=>s.Plan(1,0,0,1),"inverted ownership region is rejected");
  Invalid(()=>s.Plan(-1e14,0,0,1),"unreliable double coordinate range is rejected");
  var w=Ground();var r=GroundCoverScatter.Materialize(w,new WorldCell(-100,0,-100),p);ulong signature=Signature(w);
  Check(r.WrittenCells>1000&&r.Plants>10&&r.Rocks>0,"finite forest plan materializes actual leaves, wood, fungi and rocks");
  Check(w.Get(0,10,0)==MaterialId.Grass&&w.GetTint(0,10,0)==0xff506438,"existing terrain matter and pigment survive nonoverwriting scatter");
  var w2=Ground();var r2=GroundCoverScatter.Materialize(w2,new WorldCell(-100,0,-100),p);
  Check(signature==Signature(w2)&&r.WrittenCells==r2.WrittenCells,"canonical finite materialization repeats matter and stored colors");
  var capped=GroundCoverScatter.Materialize(Ground(),new WorldCell(-100,0,-100),p,new GroundCoverBuildBudget(maxGeneratorCandidates:30,maxWrittenCells:20));
  Check(capped.Truncated&&capped.GeneratorCandidates<=30&&capped.WrittenCells<=20,"aggregate generation caps stop and report even inside the final specimen");
  var clipped=GroundCoverScatter.Materialize(new VoxelWorld(1,12,1,.1f,1),new WorldCell(p.Placements[0].Anchor.X,0,p.Placements[0].Anchor.Z),p,new GroundCoverBuildBudget(maxPlacements:1));
  Check(clipped.Truncated&&clipped.ClippedPlacements>0&&clipped.ClippedRockCells>0,"rock shape boundary clipping remains visible when its anchor is inside");
  Invalid(()=>GroundCoverScatter.Materialize(w,new WorldCell(long.MinValue,0,0),p),"invalid origins cannot overflow long local subtraction");
  Invalid(()=>GroundCoverScatter.Materialize(new VoxelWorld(16,16,16,.2f,1),new WorldCell(0,0,0),p),"materialization cannot silently change10cm cell scale");
  foreach(var mat in new[]{MaterialId.Leaves,MaterialId.Fungus,MaterialId.Petal})
  {
   var target=new VoxelWorld(16,16,16,.1f,1);target.Set(8,5,5,mat);
   Check(!MaterialCatalog.BlocksActorBody(mat)&&MaterialCatalog.BlocksActorBody(mat,true)&&VoxelRaycast.TryCast(target,.1f,.55f,.55f,1,0,0,2,out VoxelHit hit)&&target.Get(hit.Cell.x,hit.Cell.y,hit.Cell.z)==mat,"soft "+mat+" is body-passable yet remains tool-targetable and optionally solid");
  }
  Check(MaterialCatalog.BlocksActorBody(MaterialId.Wood)&&MaterialCatalog.BlocksActorBody(MaterialId.Granite)&&MaterialCatalog.BlocksActorBody(MaterialId.Grass)&&!MaterialCatalog.BlocksActorBody(MaterialId.Air),"woody stems and ground remain body barriers while air does not");
  File.WriteAllText(args[0],JsonSerializer.Serialize(new{status="PASS",checks,scope="Actual engine-free habitat planner and materialization; not Unity, streaming or ecosystem simulation",habitats,forest=new{placements=p.Placements.Count,plants=r.Plants,rocks=r.Rocks,cells=r.WrittenCells,candidates=r.GeneratorCandidates,incomplete=r.IncompletePlacements,truncated=r.Truncated}},new JsonSerializerOptions{WriteIndented=true}));
  Console.WriteLine("All "+checks+" ground-cover checks passed.");
 }
}
