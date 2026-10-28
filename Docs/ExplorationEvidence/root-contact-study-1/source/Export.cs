using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using AshenVoxel.Core;
using AshenVoxel.Core.Trees;
using AshenVoxel.WorldScale;

static class Export
{
    static void Main(string[] args)
    {
        string output=args[0],main=args[1];var facts=new List<object>();
        foreach(int variant in new[]{0,1})foreach(bool narrow in new[]{false,true})
        {
            int seed=variant==0?714103:271828;
            var p=TreePresets.Get(TreeSpecies.Redwood).With(age:1,foliageDensity:0);
            if(narrow)p=p.With(rootFlare:.2f);
            double reach=p.CrownRadius*1.5+p.LeafClusterRadius*3+p.TrunkRadius*(1.7+p.RootFlare*1.1)+3;
            int side=(int)Math.Ceiling(reach*2/.1);if(side%2!=0)side++;
            int height=(int)Math.Ceiling((p.Height*1.25+4)/.1);
            var world=new VoxelWorld(side,height,side,.1f,seed);var anchor=new Int3(side/2,4,side/2);
            var watch=Stopwatch.StartNew();
            var generated=TreeGenerator.Generate(world,p,anchor.x*.1f,anchor.y*.1f,anchor.z*.1f,seed,new TreeGenerationBudget(8192,160000000,16384));
            if(generated.Truncated)throw new Exception(generated.TruncationReason);
            string name=$"wood-{10+variant}-{(narrow?"narrow":"baseline")}.bin";int count=0;
            using(var writer=new BinaryWriter(File.Create(Path.Combine(output,name))))
            {
                writer.Write(0);
                foreach(var chunk in world.Chunks)for(int i=0;i<Chunk.Volume;i++)
                {
                    if(chunk.Value.Cells[i]!=MaterialId.Wood)continue;var local=Chunk.Decode(i);
                    int x=chunk.Key.x*16+local.x,y=chunk.Key.y*16+local.y,z=chunk.Key.z*16+local.z;
                    writer.Write(checked((short)(x-anchor.x)));writer.Write(checked((short)(y-anchor.y)));writer.Write(checked((short)(z-anchor.z)));
                    writer.Write(world.GetColor(x,y,z));count++;
                }
                writer.BaseStream.Position=0;writer.Write(count);
            }
            facts.Add(new{name,seed,count,root_flare=p.RootFlare,trunk_radius=p.TrunkRadius,nominal_height=p.Height,elapsed_ms=watch.Elapsed.TotalMilliseconds});
            world=null;GC.Collect();
        }
        string samples=Path.Combine(main,"Assets/AshenVoxel/Exploration/Samples");using var metadata=JsonDocument.Parse(File.ReadAllText(Path.Combine(samples,"exploration-world.json")));var meta=metadata.RootElement;
        HeightAtlas macro,fine;ClimateAtlas mc,fc;ExplorationBiomeAtlas biomes;
        using(var s=File.OpenRead(Path.Combine(samples,meta.GetProperty("macro").GetProperty("height_file").GetString())))macro=AtlasIO.Read(s);
        using(var s=File.OpenRead(Path.Combine(samples,meta.GetProperty("fine").GetProperty("height_file").GetString())))fine=AtlasIO.Read(s);
        using(var s=File.OpenRead(Path.Combine(samples,meta.GetProperty("macro").GetProperty("climate_file").GetString())))mc=ClimateArchiveIO.Read(s);
        using(var s=File.OpenRead(Path.Combine(samples,meta.GetProperty("fine").GetProperty("climate_file").GetString())))fc=ClimateArchiveIO.Read(s);
        using(var s=File.OpenRead(Path.Combine(samples,meta.GetProperty("source_biomes").GetProperty("file").GetString())))biomes=ExplorationBiomeAtlasIO.Read(s);
        var atlas=new ExplorationAtlasField(macro,mc,fine,fc,meta.GetProperty("default_fine_blend_m").GetDouble(),macro.ExtentX,macro.ExtentZ,1,biomes,macro.ExtentX,macro.ExtentZ);
        if(atlas.WorldId!=meta.GetProperty("world_id").GetString())throw new Exception("World ID mismatch");
        var surface=new SurfaceMaterialSampler(new TerrainDetailField(atlas,714103),new SurfaceProfile(seed:714103,maxSnowDepthMeters:2.4f,winterFraction:.2f));
        using var sites=JsonDocument.Parse(File.ReadAllText(Path.Combine(output,"sites.json")));
        foreach(var site in sites.RootElement.EnumerateArray())
        {
            int id=site.GetProperty("id").GetInt32();double x=site.GetProperty("x").GetDouble(),z=site.GetProperty("z").GetDouble();
            long gx=(long)Math.Floor(x*10)-128,gz=(long)Math.Floor(z*10)-128;
            using var w=new BinaryWriter(File.Create(Path.Combine(output,$"terrain-{id}.bin")));
            w.Write(gx);w.Write(gz);w.Write(256);
            for(int iz=0;iz<256;iz++)for(int ix=0;ix<256;ix++)
            {
                double wx=(gx+ix+.5)*.1,wz=(gz+iz+.5)*.1;var column=surface.EvaluateColumn(wx,wz);
                long top=VoxelTerrainPage.TopCell(column.SurfaceHeightMeters),bare=VoxelTerrainPage.TopCell(column.GroundHeightMeters);
                w.Write(top);w.Write(bare);
                // Exact top material/pigment; below it use a representative
                // actual half-metre-deep pigment for this shape-only preview.
                var topMatter=surface.SampleMatter(new WorldPoint(wx,(top+.5)*.1,wz),column);
                var deep=surface.SampleMatter(new WorldPoint(wx,column.GroundHeightMeters-.5,wz),column);
                w.Write(topMatter.Tint);w.Write(deep.Tint);
            }
        }
        File.WriteAllText(Path.Combine(output,"export.json"),JsonSerializer.Serialize(new{world_id=atlas.WorldId,source=surface.SourceIdentity,trees=facts,scope="CPU actual 10cm wood data and globally aligned terrain heights; foliage suppressed. No Unity, no runtime mutation or adoption."},new JsonSerializerOptions{WriteIndented=true}));
        Console.WriteLine("PASS exported four wood variants and two natural mature-redwood sites.");
    }
}
