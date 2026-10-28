using System;
using System.IO;
using System.Text.Json;
using AshenVoxel.WorldScale;
static class OverviewProgram
{
 const string Id="aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";static int checks;
 static void Check(bool ok,string why){if(!ok)throw new Exception(why);checks++;Console.WriteLine("PASS "+why);}
 static void Throws(Action f,string why){try{f();}catch(ArgumentException){Check(true,why);return;}throw new Exception(why);}
 static ClimateAtlas Climate(HeightAtlas h){int count=h.Columns*h.Rows;var values=new float[count*5];for(int i=0;i<count;i++){values[i]=20;values[count+i]=4;values[count*2+i]=1000;values[count*3+i]=30;values[count*4+i]=-.006f;}return new ClimateAtlas(h.Columns,h.Rows,h.OriginX,h.OriginZ,h.ExtentX,h.ExtentZ,ClimateArchiveIO.HeightHash(h),h.SourceIdentity,values);}
 static HeightAtlas Atlas(int n,double x,double z,double extent,float height){var h=new float[n*n];var b=new byte[h.Length];var l=new bool[h.Length];for(int i=0;i<h.Length;i++){h[i]=height;b[i]=6;l[i]=true;}return new HeightAtlas(n,n,x,z,extent,extent,h,b,l,"{\"world_id\":\""+Id+"\"}");}
 static ExplorationOverviewPatch Patch(long x,long z,int spacing,float height){int n=3200/spacing+1;var a=new float[n*n];Array.Fill(a,height);return new ExplorationOverviewPatch(x,z,spacing,a,Id);}
 static void Main(string[] args)
 {
  var macro=Atlas(161,-25600,-25600,51200,100);var fine=Atlas(9,10500,100,40,150);
  var baked=new ExplorationAtlasField(macro,Climate(macro),fine,Climate(fine),10,51200,51200,1,null,51200,51200);
  var live=new ExplorationRefinementField(baked,Id);var field=new ExplorationOverviewField(baked,live,Id);var observer=new WorldPoint(0,200,0);
  Check(field.ResidentPatches==0&&field.ResolutionMetersAt(12000,1000)==320,"initial source is actual320m baked atlas");
  Check(!field.TryPublish(Patch(0,0,160,200),observer,out _,out _),"2048m guard protects insertion of complete patch plus blend");
  var values=new float[441];Array.Fill(values,200);var p=new ExplorationOverviewPatch(4,0,160,values,Id);values[0]=999;
  Check(field.TryPublish(p,observer,out var changed,out _)&&changed.MinimumX==9920,"whole160m grid publishes with full changed influence bounds");
  Check(field.Sample(12000,1000).HeightMeters==200&&field.ResolutionMetersAt(12000,1000)==160,"160m SOURCE patch replaces core height and exposes actual source spacing");
  Check(Math.Abs(field.Sample(9920,1000).HeightMeters-100)<1e-8&&Math.Abs(field.Sample(10080,1000).HeightMeters-150)<1e-6,"320m overlap blends smoothly to unchanged base");
  Check(Math.Abs(field.Sample(10520,120).HeightMeters-baked.Sample(10520,120).HeightMeters)<1e-8,"existing baked fine interior remains exact");
  Check(field.TrySampleClimate(12000,1000,out var climate)&&Math.Abs(climate.TemperatureC-19.4)<.00001&&climate.PrecipitationMm==1000,"height-only source keeps climate with one lapse adjustment");
  Check(field.Sample(12000,1000).SourceBiome==6,"original biome identity is preserved");
  var snapshot=field.CaptureSnapshot();var identity=snapshot.SourceIdentity;
  Check(field.TryPublish(Patch(4,0,80,300),observer,out _,out _)&&field.Sample(12000,1000).HeightMeters==300&&field.ResolutionMetersAt(12000,1000)==80,"80m pass refines the SAME fixed source core atomically");
  Check(snapshot.Sample(12000,1000).HeightMeters==200&&snapshot.SourceIdentity==identity&&field.SourceIdentity!=identity,"prior snapshot retains immutable height content and identity");
  Check(!field.TryPublish(p,observer,out _,out _),"coarser160m replacement cannot erase published80m source");
  Check(!field.TryRetire(4,0,new WorldPoint(11000,200,1000),out _,out _),"observer guard protects retirement");
  Check(!field.TryPublish(Patch(4,0,80,400),new WorldPoint(11000,200,1000),out _,out _),"observer guard protects changed-source replacements");
  var copy=new ExplorationOverviewField(baked,live,Id);copy.TryPublish(Patch(4,0,80,300),observer,out _,out _);
  Check(copy.SourceIdentity==field.SourceIdentity,"identical base and height content produce deterministic identity");
  field.TryPublish(Patch(5,0,160,250),observer,out _,out _);copy.TryPublish(Patch(5,0,160,250),observer,out _,out _);
  Check(copy.SourceIdentity==field.SourceIdentity,"identity is independent of publication counters");
  var bounds=field.QueryBounds(9900,0,15500,2560);Check(bounds.Minimum<=100&&bounds.Maximum>=300,"conservative bounds include every blended source extreme");
  var detail=ExplorationRefinementPatch.CopySnapshot(baked,"live-five",Id,new ExplorationRefinementBounds(11000,1000,11040,1040),40,32,5);
  Check(live.TryPublish(detail,observer,out _),"controlled5m source publishes before snapshot refresh");field.RefreshBaseSnapshot();
  Check(Math.Abs(field.Sample(11020,1020).HeightMeters-live.Sample(11020,1020).HeightMeters)<1e-8&&field.ResolutionMetersAt(11020,1020)==5,"refreshed live5m interior overrides coarser overview exactly");
  Check(snapshot.Sample(12000,1000).HeightMeters==200,"base refresh cannot mutate earlier snapshot");
  Throws(()=>new ExplorationOverviewPatch(1,1,40,new float[6561],Id),"unsupported spacing refuses");Throws(()=>new ExplorationOverviewPatch(1,1,160,new float[440],Id),"incomplete grid refuses");
  var invalid=new float[441];invalid[22]=float.NaN;Throws(()=>new ExplorationOverviewPatch(1,1,160,invalid,Id),"nonfinite source refuses before publication");
  Check(field.TryRetire(4,0,observer,out _,out _)&&!field.HasCoreCoverage(4,0,160),"safe retirement reports removal and coverage");
  Check(field.ResidentHeightBytes<=ExplorationOverviewField.MaximumResidentHeightBytes&&field.CapturePublishedCoverage().Length==field.ResidentPatches,"owned resident bytes and immutable planning coverage agree");
  Check(field.TryPublish(Patch(-4,-2,160,-200),observer,out _,out _),"negative-coordinate height patch publishes");
  Check(!field.Sample(-9000,-4000).IsLand&&field.Sample(-9000,-4000).HeightMeters==-200,"positive base becomes water when overview final height is negative");
  var small=field.QueryBounds(-9001,-4001,-8999,-3999);Check(small.Minimum<=-200&&small.Maximum>=100,"small negative-coordinate cell query includes blended source extrema");
  var empty=field.QueryBounds(-18000,-18000,-17999,-17999);Check(empty.Minimum>=-1000&&empty.Maximum<200,"empty small query does not include unrelated overview extrema");
  var huge=field.QueryBounds(-1e12,-1e12,1e12,1e12);Check(huge.Minimum<=-200&&huge.Maximum>=250,"huge query conservatively includes residents without traversing empty world cells");
  var touching=field.QueryBounds(-10560,-4000,-10560,-4000);Check(touching.Minimum<=-200,"exact negative influence boundary remains conservatively included");
  var oceanAtlas=Atlas(161,-25600,-25600,51200,-100);var ocean=new ExplorationAtlasField(oceanAtlas,Climate(oceanAtlas),null,null,512,51200,51200,1,null,51200,51200);
  var oceanLive=new ExplorationRefinementField(ocean,Id);var shore=new ExplorationOverviewField(ocean,oceanLive,Id);
  Check(shore.TryPublish(Patch(4,0,160,25),observer,out _,out _)&&shore.Sample(12000,1000).IsLand,"negative base becomes land when overview final height is positive");
  Check(shore.TryPublish(Patch(4,0,80,0),observer,out _,out _)&&shore.Sample(12000,1000).IsLand,"zero-height shoreline uses the existing nonnegative land convention");
  Check(shore.Sample(12000,1000).SourceBiome==ocean.Sample(12000,1000).SourceBiome,"shoreline reclassification preserves original biome identity");
  File.WriteAllText(args[0],JsonSerializer.Serialize(new{status="PASS",checks,scope="engine-free actual source publication and snapshots; no inference/native/FPS"}));Console.WriteLine("All "+checks+" overview checks passed");
 }
}
