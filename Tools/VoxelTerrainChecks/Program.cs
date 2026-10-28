using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Text.Json;
using System.Collections.Generic;
using AshenVoxel.Core;
using AshenVoxel.WorldScale;

static class Program
{
    sealed class Field:IWorldField
    {
        public Func<double,double,double> Height;
        public byte Biome=6;
        public string SourceIdentity=>"explicit-test-fixture";
        public SurfaceSample Sample(double x,double z)=>new SurfaceSample(Height(x,z),Biome,Biome,true);
    }
    static int checks;
    static void Check(bool value,string name){if(!value)throw new Exception("FAIL "+name);checks++;Console.WriteLine("PASS "+name);}
    static void Reject(Action action,string name){bool failed=false;try{action();}catch(InvalidOperationException){failed=true;}Check(failed,name);}
    static SurfaceMaterialSampler Sampler(Func<double,double,double> h)=>new SurfaceMaterialSampler(new Field{Height=h},new SurfaceProfile(enableSnow:false));
    static void Main(string[] args)
    {
        var clock=System.Diagnostics.Stopwatch.StartNew();
        Check(VoxelTerrainPage.PageAt(-.001)==-1&&VoxelTerrainPage.PageAt(-3.2)==-1&&VoxelTerrainPage.PageAt(3.2)==1,"negative and positive global page ownership use floor division");
        for(int i=-100;i<=100;i++)
        {
            double h=i*.1+.05;long top=VoxelTerrainPage.TopCell(h);
            if((top+.5)*.1>h||(top+1.5)*.1<=h)throw new Exception("Invalid centre occupancy at "+h);
        }
        Check(true,"top-cell rounding agrees with exact occupied cell centres across zero");
        Check(VoxelTerrainPage.TopCell(1.05-1e-9)<VoxelTerrainPage.TopCell(1.05+1e-9),"subepsilon height differences do not invent a solid cell above the source");
        var sampler=Sampler((x,z)=>10);var flat=VoxelTerrainPage.Build(sampler,0,0);
        Check(flat.Matter.VoxelSize==.1f&&flat.Matter.SizeX==32&&flat.Matter.SizeZ==32,"resident matter uses existing16-cubed chunks at10cm, with3.2m pages");
        Check(flat.ResidentCells==10240&&flat.Matter.SizeY<=11,"flat page retains one-metre volume without full mountain allocation");
        Check(flat.Faces.Count(f=>f.Axis==1&&f.Positive)==1024&&!flat.Faces.Any(f=>f.Axis==1&&!f.Positive),"exactly one top quad per column and no artificial underside");
        Check(flat.Faces.Where(f=>f.Axis!=1).All(f=>f.Axis==0?(f.Positive?f.X==31:f.X==0):(f.Positive?f.Z==31:f.Z==0)),"flat-page lateral faces only occur in transition aprons");
        Check(flat.Faces.All(f=>(f.Tint>>24)==255&&f.AO0>=64&&f.AO1>=64&&f.AO2>=64&&f.AO3>=64),"persistent opaque pigment stays separate from vertex AO");
        Check(flat.Matter.Chunks.Count<=8&&flat.Matter.DirtyChunks.Count==0,"finite material skin is sparse and initial dirty queue is consumed");
        var tall=VoxelTerrainPage.Build(Sampler((x,z)=>10000),0,0);
        Check(tall.ResidentCells==flat.ResidentCells&&tall.Faces.Count==flat.Faces.Count,"kilometre elevation does not increase resident cell or face counts");
        Check(flat.SourceIdentity.EndsWith("/voxel-surface-v1"),"conversion has explicit geometry provenance separate from the height source");
        var slope=VoxelTerrainPage.Build(Sampler((x,z)=>10+x*.5-z*.2),-1,0);
        Check(slope.Faces.Any(f=>f.Axis==0&&f.X>0&&f.X<31)||slope.Faces.Any(f=>f.Axis==2&&f.Z>0&&f.Z<31),"slopes become real cubic risers rather than smoothed triangles");
        Check(slope.Faces.All(f=>slope.Matter.Get(f.X,f.Y,f.Z)!=MaterialId.Air&&slope.Matter.GetColor(f.X,f.Y,f.Z)==f.Tint),"every face comes from an actual occupied material cell and its stored pigment");
        var cliffSampler=Sampler((x,z)=>x<1.6?10:14);
        var cliff=VoxelTerrainPage.Build(cliffSampler,0,0);
        var exposed=cliff.Faces.Where(f=>f.Axis==0&&!f.Positive&&f.X==16).ToArray();
        Check(exposed.Length==32*40,"four-metre cliff exposes the complete sidewall without cell gaps");
        Check(exposed.Any(f=>SurfaceProfile.IsRock(cliff.Matter.Get(f.X,f.Y,f.Z))),"cut slopes expose catalog bedrock below surface material horizons");
        var left=VoxelTerrainPage.Build(cliffSampler,-1,0);var right=VoxelTerrainPage.Build(cliffSampler,0,0);
        long edgeTop=VoxelTerrainPage.TopCell(10);
        Check(left.Matter.Get(31,(int)(edgeTop-left.OriginCellY),0)==right.Matter.Get(0,(int)(edgeTop-right.OriginCellY),0)&&
            left.Faces.First(f=>f.X==31&&f.Z==0&&f.Axis==1).Y+left.OriginCellY==right.Faces.First(f=>f.X==0&&f.Z==0&&f.Axis==1).Y+right.OriginCellY,
            "adjacent pages retain global height and material across different vertical origins");
        Reject(()=>VoxelTerrainPage.Build(Sampler((x,z)=>x<1.6?0:40),0,0),"oversized vertical pages fail explicitly before publishing a clipped surface");
        var canceled=new CancellationTokenSource();canceled.Cancel();bool stopped=false;
        try{VoxelTerrainPage.Build(sampler,0,0,canceled.Token);}catch(OperationCanceledException){stopped=true;}
        Check(stopped,"worker cancellation is observable and interrupts column generation");
        var field=new SurfaceWorldField(new Field{Height=(x,z)=>10+x*.5-z*.2},new SurfaceProfile(enableSnow:false));
        var collision=new VoxelTerrainCollision(field,new FieldExplorationCollision(field));
        bool contact=true;foreach(var face in slope.Faces.Where(f=>f.Axis==1))
        {
            double x=(-32+face.X+.5)*.1,z=(face.Z+.5)*.1;
            collision.TryGetGroundHeight(x,z,out double y);
            if(Math.Abs(y-(slope.OriginCellY+face.Y+1)*.1)>1e-9)contact=false;
        }
        Check(contact,"collision and all quantized top faces agree at negative world coordinates");
        foreach(int hz in new[]{30,60,144})
        {
            var gentle=new SurfaceWorldField(new Field{Height=(x,z)=>10+x*.15},new SurfaceProfile(enableSnow:false));
            var body=new VoxelTerrainCollision(gentle,new FieldExplorationCollision(gentle));
            var motor=new ExplorationMotor(body,new WorldPoint(0,12,0));
            bool spawned=motor.TryFindClearSpawn(new WorldPoint(0,12,0));
            for(int frame=0;frame<hz*2;frame++)motor.Tick(new ExplorationMoveInput(1,0),1.0/hz);
            body.TryGetGroundHeight(motor.Feet.X,motor.Feet.Z,out double support);
            Check(spawned&&motor.Feet.X>7.5&&Math.Abs(motor.Feet.Y-support-motor.Settings.Skin)<.001,
                "actual motor traverses cubic10cm hillside steps without blocking or sinking at"+hz+"Hz");
        }
        Check(flat.Matter.Chunks.Values.SelectMany(c=>c.Cells.ToArray()).Where(m=>m!=MaterialId.Air).All(MaterialCatalog.IsValid),"all resident values use the existing material catalog");
        string samples=args[1];HeightAtlas macro,fine;ClimateAtlas mc,fc;
        using(var s=File.OpenRead(Path.Combine(samples,"ExplorationMacro.bytes")))macro=AtlasIO.Read(s);
        using(var s=File.OpenRead(Path.Combine(samples,"ExplorationFine.bytes")))fine=AtlasIO.Read(s);
        using(var s=File.OpenRead(Path.Combine(samples,"ExplorationMacro.climate.bytes")))mc=ClimateArchiveIO.Read(s);
        using(var s=File.OpenRead(Path.Combine(samples,"ExplorationFine.climate.bytes")))fc=ClimateArchiveIO.Read(s);
        ExplorationBiomeAtlas biomes;using(var s=File.OpenRead(Path.Combine(samples,"ExplorationBiomes.bytes")))biomes=ExplorationBiomeAtlasIO.Read(s);
        var atlas=new ExplorationAtlasField(macro,mc,fine,fc,512,macro.ExtentX,macro.ExtentZ,1,biomes,macro.ExtentX,macro.ExtentZ);
        var actualSampler=new SurfaceMaterialSampler(atlas,new SurfaceProfile());
        var actual=VoxelTerrainPage.Build(actualSampler,VoxelTerrainPage.PageAt(85760),VoxelTerrainPage.PageAt(15330));
        Check(actual.Faces.Count>=1024&&actual.ResidentCells>=10240&&actual.SourceIdentity.Contains(atlas.WorldId),"bundled original-model terrain converts to material-bearing voxels with retained source identity");
        Check(actual.Faces.All(f=>actual.Matter.Get(f.X,f.Y,f.Z)!=MaterialId.Air),"actual-data surface mesh contains no face sourced from absent matter");
        Directory.CreateDirectory(args[0]);
        foreach(var item in new[]{("actual-spawn",actual),("layered-cliff-fixture",cliff)})
            File.WriteAllText(Path.Combine(args[0],item.Item1+".json"),JsonSerializer.Serialize(new{scope=item.Item1=="actual-spawn"?"Actual bundled learned data, offline voxel conversion; not Unity":"Explicit controlled cliff test fixture; not learned terrain",cells=item.Item2.ResidentCells,faces=item.Item2.Faces.Select(f=>new{f.X,f.Y,f.Z,f.Axis,f.Positive,f.Tint,f.AO0,f.AO1,f.AO2,f.AO3}),identity=item.Item2.SourceIdentity}));
        clock.Stop();File.WriteAllText(Path.Combine(args[0],"validation.json"),JsonSerializer.Serialize(new{status="PASS",checks,scope="Actual engine-free voxel conversion and cell/contact tests, including bundled learned terrain. .NET CPU; no native Unity or GPU timing.",elapsed_seconds=clock.Elapsed.TotalSeconds,actual_cells=actual.ResidentCells,actual_faces=actual.Faces.Count,geometry_version=VoxelTerrainPage.Version,world_id=atlas.WorldId}));
        Console.WriteLine("All "+checks+" voxel terrain checks passed.");
    }
}
