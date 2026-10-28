using System;
using System.IO;
using System.Text;
using System.Collections.Generic;
using System.Text.Json;
using AshenVoxel.Core;
using AshenVoxel.WorldScale;
static class Program
{
    static int checks;
    static void Check(bool value,string name){if(!value)throw new Exception("FAIL "+name);checks++;Console.WriteLine("PASS "+name);}
    static void Reject(Action action,string name){try{action();}catch(ArgumentException){Check(true,name);return;}catch(InvalidDataException){Check(true,name);return;}catch(InvalidOperationException){Check(true,name);return;}throw new Exception("FAIL accepted "+name);}
    sealed class Flat : IWorldField
    {
        public string SourceIdentity=>"flat-fixture-v1";readonly double h;
        public Flat(double height){h=height;}
        public SurfaceSample Sample(double x,double z)=>new SurfaceSample(h,4,6,true);
    }
    sealed class Nonseek : Stream
    {
        readonly MemoryStream data;public bool Closed;
        public Nonseek(byte[] bytes){data=new MemoryStream(bytes);}
        public override bool CanRead=>true;public override bool CanSeek=>false;public override bool CanWrite=>false;
        public override long Length=>throw new NotSupportedException();public override long Position{get=>throw new NotSupportedException();set=>throw new NotSupportedException();}
        public override int Read(byte[] b,int o,int c)=>data.Read(b,o,c);public override void Flush(){}
        public override long Seek(long o,SeekOrigin s)=>throw new NotSupportedException();public override void SetLength(long n)=>throw new NotSupportedException();public override void Write(byte[]b,int o,int c)=>throw new NotSupportedException();
        protected override void Dispose(bool disposing){Closed=true;base.Dispose(disposing);}
    }
    static HeightAtlas Atlas(float[] heights=null)=>new HeightAtlas(2,2,100,200,400,800,heights??new float[]{-20,1000,3000,6000},new byte[]{0,4,8,9},new[]{false,true,true,true},"fixture/avwf-v1");
    static byte[] Save(HeightAtlas atlas){using(var stream=new MemoryStream()){AtlasIO.Write(stream,atlas);return stream.ToArray();}}
    static void Repair(byte[] bytes){uint c=AtlasIO.Crc32(bytes,bytes.Length-4);Array.Copy(BitConverter.GetBytes(c),0,bytes,bytes.Length-4,4);}
    public static void Main(string[] args)
    {
        Check(WorldCell.FromMeters(new WorldPoint(-.01,-.1,.3)).Equals(new WorldCell(-1,-1,3)),"10cm global cells floor correctly around negative and exact boundaries");
        Check(WorldCell.FloorDivide(-1,16)==-1&&WorldCell.FloorDivide(-16,16)==-1&&WorldCell.FloorDivide(-17,16)==-2,"signed chunk division uses floor not truncation");
        var far=new WorldPoint(100000000.05,6500.05,-100000000.05);var origin=FloatingOrigin.Snap(far);var local=FloatingOrigin.Local(far,origin);
        Check(Math.Abs((origin.X+local.X)-far.X)<1e-8&&Math.Abs((origin.Z+local.Z)-far.Z)<1e-8&&Math.Abs(local.X)<1024,"double subtraction preserves detail before Unityfloat conversion at continental coordinates");
        var cells=new HashSet<WorldCell>{new WorldCell(long.MinValue,0,long.MaxValue),new WorldCell(0,0,0)};Check(cells.Count==2,"signed64bit world keys hash and compare without coordinate packing loss");
        Reject(()=>new WorldPoint(double.NaN,0,0),"nonfinite global position rejected");Reject(()=>WorldCell.FromMeters(new WorldPoint(1e20,0,0)),"global cell overflow rejected");Reject(()=>WorldCell.FloorDivide(1,0),"invalid chunk divisor rejected");
        float[] h={-20,1000,3000,6000};var atlas=Atlas(h);h[3]=99999;Check(atlas.HeightAt(1,1)==6000,"atlas clones channel inputs to remain immutable");
        var field=new AtlasWorldField(atlas,new WorldMapSettings(400,800,1,true));
        Check(field.Sample(-200,-400).HeightMeters==-20&&field.Sample(200,400).HeightMeters==6000,"world extent is centered and reaches source endpoints");
        Check(Math.Abs(field.Sample(0,0).HeightMeters-2495)<1e-9,"atlas heights interpolate bilinearly in meters");
        Check(field.Sample(200,-400).SourceBiome==4&&field.Sample(200,-400).EffectiveBiome==6,"forest override retains imported source biome");
        Check(!field.Sample(-200,-400).IsLand&&field.Sample(-200,-400).EffectiveBiome==0,"forest override leaves source water as water");
        Check(!field.Sample(201,400).IsLand&&field.Sample(201,400).HeightMeters<0,"beyond finite imported landmass returns explicit water");
        var doubled=new AtlasWorldField(atlas,new WorldMapSettings(800,1600));Check(doubled.Sample(400,800).HeightMeters==field.Sample(200,400).HeightMeters,"horizontal landmass scaling preserves independent altitude");
        var vertical=new AtlasWorldField(atlas,new WorldMapSettings(400,800,2,false));Check(vertical.Sample(200,400).HeightMeters==12000&&vertical.Sample(200,400).EffectiveBiome==9,"vertical scaling independent and source biome restored when override disabled");
        Check(vertical.SourceIdentity!=field.SourceIdentity,"source identity includes map transform and biome override");
        byte[] golden=Encoding.ASCII.GetBytes("123456789");Check(AtlasIO.Crc32(golden,golden.Length)==0xcbf43926,"AVWFCRC32 matches independent standard IEEE golden vector");
        byte[] bytes=Save(atlas);Check(Encoding.ASCII.GetString(bytes,0,8)=="AVWF0001"&&BitConverter.ToInt32(bytes,8)==2&&BitConverter.ToInt32(bytes,12)==2,"AVWF header layout matches external converter contract");
        using(var s=new Nonseek(bytes)){var loaded=AtlasIO.Read(s);Check(loaded.HeightAt(1,1)==6000&&loaded.BiomeAt(1,0)==4&&loaded.SourceIdentity==atlas.SourceIdentity&&!s.Closed,"nonseekable atlas roundtrip keeps caller stream open");Check(Convert.ToBase64String(Save(loaded))==Convert.ToBase64String(bytes),"atlas save bytes stable after load");}
        var corrupt=(byte[])bytes.Clone();corrupt[60]^=1;Reject(()=>AtlasIO.Read(new MemoryStream(corrupt)),"corrupt atlas checksum rejected");
        var truncate=new byte[bytes.Length-1];Array.Copy(bytes,truncate,truncate.Length);Reject(()=>AtlasIO.Read(new MemoryStream(truncate)),"truncated atlas rejected");
        var future=(byte[])bytes.Clone();future[7]=(byte)'2';Repair(future);Reject(()=>AtlasIO.Read(new MemoryStream(future)),"future atlas schema rejected despite valid checksum");
        var dimensions=(byte[])bytes.Clone();Array.Copy(BitConverter.GetBytes(int.MaxValue),0,dimensions,8,4);Repair(dimensions);Reject(()=>AtlasIO.Read(new MemoryStream(dimensions)),"atlas dimensions validated before channel allocation");
        var mask=(byte[])bytes.Clone();int first=50+Encoding.UTF8.GetByteCount(atlas.SourceIdentity);mask[first+5]=2;Repair(mask);Reject(()=>AtlasIO.Read(new MemoryStream(mask)),"nonbinary land mask rejected despite valid checksum");
        var invalidHeight=(byte[])bytes.Clone();Array.Copy(BitConverter.GetBytes(float.NaN),0,invalidHeight,first,4);Repair(invalidHeight);Reject(()=>AtlasIO.Read(new MemoryStream(invalidHeight)),"nonfinite atlas height rejected despite valid checksum");
        var trailing=new byte[bytes.Length+1];Array.Copy(bytes,trailing,bytes.Length);Repair(trailing);Reject(()=>AtlasIO.Read(new MemoryStream(trailing)),"valid-checksum trailing atlas bytes rejected");
        Reject(()=>new HeightAtlas(3000,3000,0,0,1,1,Array.Empty<float>(),Array.Empty<byte>(),Array.Empty<bool>(),"bad"),"oversize atlas rejected before cloning");
        var nativeExtent=new AtlasWorldField(atlas);Check(nativeExtent.Settings.WidthMeters==400&&nativeExtent.Settings.DepthMeters==800,"default atlas field honors imported physical aspect and extent");
        var spikes=new float[65*65];var spikeBiomes=new byte[spikes.Length];var spikeLand=new bool[spikes.Length];for(int i=0;i<spikes.Length;i++){spikes[i]=1000;spikeBiomes[i]=6;spikeLand[i]=true;}spikes[23*65+17]=6200;
        var spikeAtlas=new HeightAtlas(65,65,0,0,1024,1024,spikes,spikeBiomes,spikeLand,"needle-peak-fixture");var spikeField=new AtlasWorldField(spikeAtlas);
        HeightBounds spikeBounds=spikeField.QueryBounds(-256,-192,-192,-128);Check(spikeBounds.Maximum==6200&&spikeBounds.Minimum==1000,"conservative atlas rectangle bounds retain a narrow peak missed by nine corner/center samples");
        bool boundOracle=true;var random=new Random(419);for(int n=0;n<100;n++){int x0=random.Next(65),x1=random.Next(65),z0=random.Next(65),z1=random.Next(65);if(x0>x1){int t=x0;x0=x1;x1=t;}if(z0>z1){int t=z0;z0=z1;z1=t;}double min=double.MaxValue,max=double.MinValue;for(int z=z0;z<=z1;z++)for(int x=x0;x<=x1;x++){double value=spikes[z*65+x];min=Math.Min(min,value);max=Math.Max(max,value);}HeightBounds range=spikeAtlas.QueryBounds(x0,z0,x1,z1);boundOracle&=range.Minimum==min&&range.Maximum==max;}
        Check(boundOracle,"block min/max queries equal independent exhaustive range scans over100 rectangles");
        var spikePlan=LodPlanner.Build(spikeField,new WorldPoint(-240,6200,-144),new LodSettings(baseTileSizeMeters:16,maxLevel:7,farDistanceMeters:512,maxTiles:192));bool peakRefined=false;foreach(var tile in spikePlan.Tiles)if(-240>=tile.OriginXMeters&&-240<tile.OriginXMeters+tile.SizeMeters&&-144>=tile.OriginZMeters&&-144<tile.OriginZMeters+tile.SizeMeters)peakRefined=tile.Key.Level==0&&tile.MaximumHeight==6200;
        Check(peakRefined,"LOD refines the actual needle summit at player altitude instead of coarsening it as valley floor");
        var analytic=new ProceduralMountainField();double summit=double.MinValue;for(int z=-10;z<=10;z++)for(int x=-10;x<=10;x++)summit=Math.Max(summit,analytic.Sample(x*500,z*500).HeightMeters);
        Check(summit>3000,"analytic fallback supplies multi-thousand-meter mountain terrain");
        Check(analytic.Sample(1000,2000).HeightMeters==new ProceduralMountainField().Sample(1000,2000).HeightMeters,"analytic field is stable under random sample order and repeated seed");
        Check(analytic.Sample(1000,2000).HeightMeters!=new ProceduralMountainField(seed:555).Sample(1000,2000).HeightMeters,"different world seeds alter terrain");
        Check(!analytic.Sample(90000,90000).IsLand,"analytic landmass has ocean beyond its continental footprint");
        var matter=new ImplicitMatter(new Flat(6500),2,surfaceProfile:new SurfaceProfile(enableSnow:false,forcedBedrockMaterial:MaterialId.Stone));Check(matter.Sample(new WorldCell(0,60000,0)).Material==MaterialId.Stone,"stone below6500m terrain is implicit without deep chunk allocation");
        Check(matter.Sample(new WorldCell(0,70000,0)).Material==MaterialId.Air&&matter.EditCount==0,"sampling empty sky and deep stone creates no edit residency");
        var edit=new WorldCell(-1,60000,1);matter.Set(edit,MaterialId.Air);Check(matter.Sample(edit).Material==MaterialId.Air&&matter.EditCount==1,"sparse air excavation overrides implicit stone");
        matter.Set(new WorldCell(1,60000,1),MaterialId.Sand,0xff123456);Check(matter.Sample(new WorldCell(1,60000,1)).Tint==0xff123456,"edited cells preserve intrinsic tint separately from material");
        Reject(()=>matter.Set(new WorldCell(2,60000,1),MaterialId.Air),"edit capacity reports failure instead of evicting unsaved state");
        Check(matter.EditCount==2&&matter.Reset(edit)&&matter.Sample(edit).Material==MaterialId.Stone,"reset restores generator truth without altering neighboring edits");
        Reject(()=>matter.Set(edit,MaterialId.Air,0xff112233),"air cannot retain pigment");
        var settings=new LodSettings(maxTiles:192);var camera=new WorldPoint(50,6500,-30);var plan=LodPlanner.Build(new Flat(6500),camera,settings);
        Check(plan.Tiles.Count<=192&&plan.Tiles.Count>20,"quadtree tile residency remains bounded");
        bool balanced=true,overlap=false;for(int i=0;i<plan.Tiles.Count;i++)for(int j=i+1;j<plan.Tiles.Count;j++)
        {var a=plan.Tiles[i];var b=plan.Tiles[j];if(LodPlanner.Adjacent(a,b)&&Math.Abs(a.Key.Level-b.Key.Level)>1)balanced=false;
        if(Math.Min(a.OriginXMeters+a.SizeMeters,b.OriginXMeters+b.SizeMeters)>Math.Max(a.OriginXMeters,b.OriginXMeters)&&Math.Min(a.OriginZMeters+a.SizeMeters,b.OriginZMeters+b.SizeMeters)>Math.Max(a.OriginZMeters,b.OriginZMeters))overlap=true;}
        Check(balanced,"adjacentLODleaf levels differ by at most one even at capacity");Check(!overlap,"quadtree leaf interiors do not overlap");
        bool covered=true;for(int n=0;n<360;n+=7){double a=n*Math.PI/180;double x=camera.X+Math.Cos(a)*31999,z=camera.Z+Math.Sin(a)*31999;bool hit=false;foreach(var t in plan.Tiles)if(x>=t.OriginXMeters&&x<=t.OriginXMeters+t.SizeMeters&&z>=t.OriginZMeters&&z<=t.OriginZMeters+t.SizeMeters)hit=true;covered&=hit;}
        Check(covered,"selected roots cover entire32km horizon disk without tile gaps");
        var repeated=LodPlanner.Build(new Flat(6500),camera,settings);bool same=plan.Tiles.Count==repeated.Tiles.Count;for(int i=0;same&&i<plan.Tiles.Count;i++)same&=plan.Tiles[i].Key.Equals(repeated.Tiles[i].Key);Check(same,"LODplan stable for identical camera and settings");
        var capped=LodPlanner.Build(new Flat(6500),camera,new LodSettings(maxTiles:12));Check(capped.CapacityLimited&&capped.PendingSplits>0&&capped.Tiles.Count<=12,"LODcapacity leaves observable refinement backlog and retains coarse coverage");
        var high=LodPlanner.Build(new Flat(0),new WorldPoint(0,15000,0),settings);int minimumLevel=24;foreach(var t in high.Tiles)minimumLevel=Math.Min(minimumLevel,t.Key.Level);Check(minimumLevel>0,"altitude-aware refinement does not fully refine valleys15km below camera");
        var away=LodPlanner.Build(new Flat(0),new WorldPoint(0,100000,0),settings);Check(away.Tiles.Count==0,"conservative3Dheight bounds cull terrain beyond far range vertically");
        var continental=LodPlanner.Build(new Flat(6500),new WorldPoint(-100000000,6500,100000000),settings);Check(continental.Tiles.Count>0&&continental.Tiles.Count<=settings.MaxTiles,"globalLODkeys support negative continental coordinates without float accumulation");
        Reject(()=>LodPlanner.Build(new Flat(20000),camera,settings),"field heights exceeding conservative visibility bounds are rejected");
        Reject(()=>new LodSettings(maxTiles:1),"invalidLODtilecapacity rejected");
        if(args.Length>1){using(var stream=File.OpenRead(args[1])){var imported=AtlasIO.Read(stream);Check(imported.Columns>=2&&imported.Rows>=2,"externalPythonAVWFatlas loads through actualCsharpreader");if(imported.SourceIdentity.Contains("Synthetic Four Cells"))Check(imported.HeightAt(3,3)==5000&&imported.BiomeAt(3,3)==11&&imported.LandAt(3,3)&&imported.HeightAt(1,1)==-50&&!imported.LandAt(1,1),"Python/Csharpinterop preserves exact5000mpeak,sourcebiome11 and ocean mask");Console.WriteLine("EXTERNAL ATLAS "+imported.SourceIdentity);}}
        var summary=new{status="PASS",checks,scope="Engine-independent world-scale prototype; not Unity runtime, neural inference or FPS",summit_m=summit,lod_tiles=plan.Tiles.Count,lod_pending_splits=plan.PendingSplits,lod_samples=plan.SampleCount,atlas_bytes=bytes.Length};
        if(args.Length>0)File.WriteAllText(args[0],JsonSerializer.Serialize(summary,new JsonSerializerOptions{WriteIndented=true}));Console.WriteLine("All "+checks+" world-scale checks passed.");
    }
}
