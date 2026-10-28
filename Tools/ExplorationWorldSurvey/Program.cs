using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Text.Json;
using AshenVoxel.WorldScale;

static class Program
{
    const int Seed=714103;
    const double Grid=640,Radius=.28,Height=1.8;
    sealed class Candidate
    {
        public double X,Z,Distance;
        public SurfaceColumn Column;
        public ForestStand Stand;
    }
    static readonly Dictionary<PopulationKey,PopulationCell> Cells=new Dictionary<PopulationKey,PopulationCell>();
    static PopulationCell Cell(ExplorationPopulation population,PopulationKey key)
    {if(!Cells.TryGetValue(key,out PopulationCell cell)){cell=population.Build(key);Cells.Add(key,cell);}return cell;}

    static bool Clear(ExplorationPopulation population,Dictionary<int,ExplorationPrototype> models,double reach,Candidate point)
    {
        double y=point.Column.SurfaceHeightMeters+.015;
        for(int layer=0;layer<3;layer++)
        {
            double size=layer==2?16:64,margin=layer==2?3:reach;
            long x0=(long)Math.Floor((point.X-Radius-margin)/size),x1=(long)Math.Floor((point.X+Radius+margin)/size);
            long z0=(long)Math.Floor((point.Z-Radius-margin)/size),z1=(long)Math.Floor((point.Z+Radius+margin)/size);
            for(long z=z0;z<=z1;z++)for(long x=x0;x<=x1;x++)foreach(PopulationInstance instance in Cell(population,new PopulationKey(x,z,layer)).Instances)
            {
                ExplorationPrototype model=models[instance.PrototypeId];if(!model.HasCollision)continue;
                double r=model.Radius*1.415;
                if(point.X+Radius<=instance.Position.X-r||point.X-Radius>=instance.Position.X+r||point.Z+Radius<=instance.Position.Z-r||point.Z-Radius>=instance.Position.Z+r||y+Height<=instance.Position.Y+model.MinY||y>=instance.Position.Y+model.MaxY)continue;
                double a=instance.YawDegrees*Math.PI/180,c=Math.Cos(a),s=Math.Sin(a),dx=point.X-instance.Position.X,dz=point.Z-instance.Position.Z;
                double lx=dx*c-dz*s,lz=dx*s+dz*c,extent=Radius*(Math.Abs(c)+Math.Abs(s));
                if(model.BlocksLocal(lx-extent,y+.002-instance.Position.Y,lz-extent,lx+extent,y+Height-instance.Position.Y,lz+extent))return false;
            }
        }
        return true;
    }
    static string Group(ForestStand stand)=>stand.Habitat+"/"+stand.Stage;
    static void Main(string[] args)
    {
        string root=args[0],samples=Path.Combine(root,"Assets/AshenVoxel/Exploration/Samples");
        using JsonDocument metadata=JsonDocument.Parse(File.ReadAllText(Path.Combine(samples,"exploration-world.json")));
        JsonElement meta=metadata.RootElement,spawn=meta.GetProperty("recommended_spawn");double sx=spawn.GetProperty("x_m").GetDouble(),sz=spawn.GetProperty("z_m").GetDouble();
        HeightAtlas macro,fine;ClimateAtlas macroClimate,fineClimate;ExplorationBiomeAtlas biomes;
        using(var s=File.OpenRead(Path.Combine(samples,meta.GetProperty("macro").GetProperty("height_file").GetString())))macro=AtlasIO.Read(s);
        using(var s=File.OpenRead(Path.Combine(samples,meta.GetProperty("macro").GetProperty("climate_file").GetString())))macroClimate=ClimateArchiveIO.Read(s);
        using(var s=File.OpenRead(Path.Combine(samples,meta.GetProperty("fine").GetProperty("height_file").GetString())))fine=AtlasIO.Read(s);
        using(var s=File.OpenRead(Path.Combine(samples,meta.GetProperty("fine").GetProperty("climate_file").GetString())))fineClimate=ClimateArchiveIO.Read(s);
        using(var s=File.OpenRead(Path.Combine(samples,meta.GetProperty("source_biomes").GetProperty("file").GetString())))biomes=ExplorationBiomeAtlasIO.Read(s);
        var field=new ExplorationAtlasField(macro,macroClimate,fine,fineClimate,meta.GetProperty("default_fine_blend_m").GetDouble(),meta.GetProperty("world_width_m").GetDouble(),meta.GetProperty("world_depth_m").GetDouble(),meta.GetProperty("vertical_height_scale").GetDouble(),biomes,macro.ExtentX,macro.ExtentZ);
        if(field.WorldId!=meta.GetProperty("world_id").GetString())throw new Exception("World metadata mismatch.");
        var surface=new SurfaceMaterialSampler(field,new SurfaceProfile(seed:Seed));
        var population=new ExplorationPopulation(surface,Seed){TreeDensity=1,PlantDensity=1,RockDensity=1};
        var counts=new SortedDictionary<string,int>();var within40=new SortedDictionary<string,int>();var sourceCounts=new SortedDictionary<byte,int>();
        var candidates=new SortedDictionary<string,List<Candidate>>();int sampled=0,land=0,walkable=0;
        for(double z=-field.WorldDepthMeters*.5+Grid*.5;z<field.WorldDepthMeters*.5;z+=Grid)
        for(double x=-field.WorldWidthMeters*.5+Grid*.5;x<field.WorldWidthMeters*.5;x+=Grid)
        {
            sampled++;SurfaceColumn column=surface.EvaluateColumn(x,z);ForestStand stand=ForestBiomes.Sample(column,x,z,Seed);string group=Group(stand);
            counts[group]=counts.TryGetValue(group,out int n)?n+1:1;sourceCounts[column.SourceBiome]=sourceCounts.TryGetValue(column.SourceBiome,out int b)?b+1:1;
            double distance=Math.Sqrt((x-sx)*(x-sx)+(z-sz)*(z-sz));if(distance<=40000)within40[group]=within40.TryGetValue(group,out n)?n+1:1;
            if(column.IsLand&&!column.HasWater)land++;
            if(!stand.HasTrees||column.HasWater||!column.IsLand||column.SlopeDegrees>30)continue;
            walkable++;if(!candidates.TryGetValue(group,out List<Candidate> list)){list=new List<Candidate>();candidates.Add(group,list);}
            list.Add(new Candidate{X=x,Z=z,Column=column,Stand=stand,Distance=distance});
        }
        var models=new Dictionary<int,ExplorationPrototype>();double reach=24;
        using(var reader=new ExplorationPrototypeReader(File.ReadAllBytes(Path.Combine(root,"Assets/AshenVoxel/Exploration/Prototypes/ExplorationPrototypes.bytes"))))
        while(reader.TryRead(out ExplorationPrototype model)){model.ReleaseMeshData();models.Add(model.Id,model);reach=Math.Max(reach,model.Radius*1.415+2);}
        var reports=new List<object>();var absent=new List<string>();
        foreach(ForestHabitat habitat in Enum.GetValues(typeof(ForestHabitat)))
        {
            if(habitat==ForestHabitat.None)continue;
            foreach(ForestStage stage in Enum.GetValues(typeof(ForestStage)))
            {
                string group=habitat+"/"+stage;var chosen=new List<Candidate>();var positions=new List<object>();
                if(candidates.TryGetValue(group,out List<Candidate> list))
                foreach(Candidate c in list.OrderBy(x=>x.Distance))
                {
                    if(chosen.Any(p=>(p.X-c.X)*(p.X-c.X)+(p.Z-c.Z)*(p.Z-c.Z)<1000*1000)||!Clear(population,models,reach,c))continue;
                    int species=(int)c.Stand.Species,age=c.Stand.Stage==ForestStage.YoungDense?0:1;
                    var nearby=new List<PopulationInstance>();long cx=(long)Math.Floor(c.X/64),cz=(long)Math.Floor(c.Z/64);
                    for(long z=cz-1;z<=cz+1;z++)for(long x=cx-1;x<=cx+1;x++)nearby.AddRange(Cell(population,new PopulationKey(x,z,0)).Instances.Where(i=>i.PrototypeId/4==species&&(i.PrototypeId%4)/2==age));
                    if(nearby.Count==0)continue;
                    chosen.Add(c);positions.Add(new{x_m=c.X,y_feet_m=c.Column.SurfaceHeightMeters+.015,z_m=c.Z,distance_from_spawn_m=Math.Round(c.Distance,1),within_40km_of_spawn=c.Distance<=40000,source_biome=c.Column.SourceBiome,slope_degrees=c.Column.SlopeDegrees,surface_material=c.Column.SurfaceMaterial.ToString(),temperature_c=c.Column.TemperatureC,precipitation_mm=c.Column.PrecipitationMm,stand_density=c.Stand.Density,nearest_tree_m=nearby.Min(i=>Math.Sqrt((i.Position.X-c.X)*(i.Position.X-c.X)+(i.Position.Z-c.Z)*(i.Position.Z-c.Z))),matching_tree_instances_in_3x3_cells=nearby.Count,actual_prototype_ids=nearby.Select(i=>i.PrototypeId).Distinct().OrderBy(i=>i),model_spacing_m=field.ModelResolutionMeters(c.X,c.Z),fine_blend_weight=field.FineWeightAt(c.X,c.Z),hard_prop_body_clear=true});
                    if(chosen.Count==3)break;
                }
                if(positions.Count==0)absent.Add(group);
                reports.Add(new{habitat=habitat.ToString(),stage=stage.ToString(),grid_points=counts.TryGetValue(group,out int n)?n:0,grid_points_within_40km_of_spawn=within40.TryGetValue(group,out int local)?local:0,walkable_samples=positions});
                Console.WriteLine(group+": "+(counts.TryGetValue(group,out n)?n:0)+" points; "+positions.Count+" clear samples; nearest "+(chosen.Count>0?Math.Round(chosen[0].Distance/1000,2)+"km":"NONE"));
            }
        }
        File.WriteAllText(args[1],JsonSerializer.Serialize(new{status=absent.Count==0?"PASS":"MISSING_HABITAT_STAGE",scope="Actual shipped immutable learned world and default surface/population data; static walkability/real hard-prop clearance, not native Unity movement or visual validation",world_id=field.WorldId,seed=Seed,grid_spacing_m=Grid,total_grid_points=sampled,dry_land_grid_points=land,gentle_forest_grid_points=walkable,world_width_m=field.WorldWidthMeters,world_depth_m=field.WorldDepthMeters,spawn_x_m=sx,spawn_z_m=sz,sampling_note="Cell-center rectangular640m lattice, counts are point counts rather than exact areas. Samples are nearest grid candidates with dry land, slope<=30deg, full-resolution hard-prop clearance for0.28m radius/1.8m height, and actual matching tree instances nearby. No runtime settings or world generation changed.",all_habitat_stage_counts=counts,source_biome_grid_counts=sourceCounts,habitats=reports,missing_habitat_stages=absent},new JsonSerializerOptions{WriteIndented=true})+"\n");
        if(absent.Count>0)Console.WriteLine("MISSING "+string.Join(", ",absent));
    }
}
