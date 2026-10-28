using System;
using System.IO;
using System.Collections.Generic;
using System.Text.Json;
using AshenVoxel.Core;
using AshenVoxel.WorldScale;
static class ExportSurfaces
{
    sealed class Fixture:IWorldFieldBounds
    {
        public readonly string Name;public readonly double Height;
        public Fixture(string name,double height){Name=name;Height=height;}
        public string SourceIdentity=>"CONTROLLED-SURFACE-FIXTURE/"+Name+"/v1";
        public SurfaceSample Sample(double x,double z)
        {
            double h;byte biome=6;
            if(Name=="alpine")h=Height+2.2*Math.Sin(x*.35)+Math.Cos(z*.4)+1.8*Math.Tanh((x-10)*1.5);
            else if(Name=="forest")h=Height+.14*Math.Sin(x*.42)+.12*Math.Cos(z*.5);
            else if(Name=="arid"){h=Height+.8*Math.Sin(x*.3)+3.1*Math.Tanh((x-10)*1.6)+z*.07;biome=13;}
            else {h=x*.15-1.8+.10*Math.Sin(z*.45)+.9*Math.Tanh((z-12)*1.1);biome=Name=="black-shore"?(byte)1:(byte)4;}
            return new SurfaceSample(h,h>=0?biome:(byte)0,h>=0?(byte)6:(byte)0,h>=0);
        }
        public HeightBounds QueryBounds(double x0,double z0,double x1,double z1)
        {
            if(Name=="alpine")return new HeightBounds(Height-5,Height+5);
            if(Name=="forest")return new HeightBounds(Height-.26,Height+.26);
            if(Name=="arid")return new HeightBounds(Height-3.9+z0*.07,Height+3.9+z1*.07);
            return new HeightBounds(x0*.15-2.8,x1*.15-.8);
        }
    }
    static void Main(string[] args)
    {
        Directory.CreateDirectory(args[0]);
        Export(args[0],new Fixture("alpine",4800),new SurfaceProfile(forcedBedrockMaterial:MaterialId.Granite,climateOverride:new SurfaceClimate(-14,6,1600,25,-.0065)),false,false);
        Export(args[0],new Fixture("forest",100),new SurfaceProfile(enableSnow:false,forcedBedrockMaterial:MaterialId.Granite,climateOverride:new SurfaceClimate(18,0,1100,35,-.0065)),true,false);
        Export(args[0],new Fixture("pale-shore",0),new SurfaceProfile(enableSnow:false,forcedBedrockMaterial:MaterialId.Limestone,climateOverride:new SurfaceClimate(20,0,500,35,-.0065)),false,true);
        Export(args[0],new Fixture("black-shore",0),new SurfaceProfile(enableSnow:false,forcedBedrockMaterial:MaterialId.Stone,climateOverride:new SurfaceClimate(24,0,650,40,-.0065)),false,true);
        Export(args[0],new Fixture("arid",160),new SurfaceProfile(enableSnow:false,forcedBedrockMaterial:MaterialId.Sandstone,climateOverride:new SurfaceClimate(30,0,80,70,-.0065)),true,false);
    }
    static void Export(string folder,Fixture fixture,SurfaceProfile profile,bool cutaway,bool water)
    {
        const int width=192;const double s=.1;var sampler=new SurfaceMaterialSampler(fixture,profile);var columns=new SurfaceColumn[width*width];
        double lo=double.MaxValue,hi=double.MinValue,minSnow=double.MaxValue,maxSnow=0;int snowy=0;var depths=new HashSet<int>();
        for(int z=0;z<width;z++)for(int x=0;x<width;x++)
        {SurfaceColumn c=sampler.EvaluateColumn((x+.5)*s,(z+.5)*s);columns[x+width*z]=c;lo=Math.Min(lo,c.GroundHeightMeters);hi=Math.Max(hi,c.SurfaceHeightMeters);
            minSnow=Math.Min(minSnow,c.SnowDepthMeters);maxSnow=Math.Max(maxSnow,c.SnowDepthMeters);depths.Add((int)Math.Round(c.SnowDepthMeters*10));if(c.SnowDepthMeters>0)snowy++;}
        double baseY=Math.Floor((lo-2.2)/s)*s;
        int height=(int)Math.Ceiling((hi-baseY+1)/s);if(height>256)throw new Exception("Fixture vertical cap exceeded");
        var world=new VoxelWorld(width,height,width,.1f,profile.Seed);int removed=0;
        for(int z=0;z<width;z++)for(int x=0;x<width;x++)
        {
            SurfaceColumn c=columns[x+width*z];int top=Math.Min(height-1,(int)Math.Ceiling((c.SurfaceHeightMeters-baseY)/s));
            for(int y=0;y<=top;y++)
            {
                double py=baseY+(y+.5)*s;MatterSample m=sampler.SampleMatter(new WorldPoint((x+.5)*s,py,(z+.5)*s),c);
                if(cutaway&&x>=68&&x<=124&&z<58&&py>c.GroundHeightMeters-1.65){if(m.Material!=MaterialId.Air)removed++;continue;}
                if(m.Material!=MaterialId.Air)world.Set(x,y,z,m.Material,m.Tint);
            }
        }
        var counts=new Dictionary<string,int>();int records=0;foreach(var c in world.Chunks.Values)records+=c.NonAirCount;
        string name=fixture.Name;
        using(var w=new BinaryWriter(File.Create(Path.Combine(folder,name+".bin"))))
        {
            w.Write(width);w.Write(height);w.Write(width);w.Write(.1f);w.Write(records);
            foreach(var pair in world.Chunks)for(int i=0;i<Chunk.Volume;i++)
            {
                MaterialId id=pair.Value.Cells[i];if(id==MaterialId.Air)continue;
                Int3 p=Chunk.Decode(i);int x=pair.Key.x*16+p.x,y=pair.Key.y*16+p.y,z=pair.Key.z*16+p.z;
                w.Write((ushort)x);w.Write((ushort)y);w.Write((ushort)z);w.Write((byte)id);w.Write(world.GetColor(x,y,z));
                string key=id.ToString();counts.TryGetValue(key,out int n);counts[key]=n+1;
            }
        }
        if(name=="alpine"&&(!counts.ContainsKey("Snow")||!counts.ContainsKey("Granite")||depths.Count<3))throw new Exception("Alpine example lacks real snow/rock/depth variation");
        if(name=="forest"&&(!counts.ContainsKey("Grass")||!counts.ContainsKey("Litter")||!counts.ContainsKey("Dirt")))throw new Exception("Forest example lacks actual layer materials");
        if(name=="black-shore"&&!counts.ContainsKey("BlackSand"))throw new Exception("Volcanic example lacks actual black sand");
        var metadata=new{name,fixture_kind="Controlled synthetic field/profile exercising the actual shared sampler; NOT Ceteviles or neural output",source_identity=sampler.SourceIdentity,
            voxel_m=.1,dimensions_cells=new[]{width,height,width},world_origin_m=new[]{0.0,baseY,0.0},bounds_min_m=new[]{0.0,0.0,0.0},bounds_max_m=new[]{width*s,hi-baseY,width*s},
            seed=profile.Seed,material_counts=counts,cutaway_removed_cells=removed,water_plane=water,water_local_height_m=profile.SeaLevelMeters-baseY,
            snow=new{minimum_m=minSnow,maximum_m=maxSnow,distinct_depths=depths.Count,covered_columns=snowy,total_columns=columns.Length},
            profile=new{profile.MaxSnowDepthMeters,profile.WindStrength,profile.WindAzimuthDegrees,profile.SolarAzimuthDegrees,forced_rock=profile.ForcedBedrockMaterial.ToString(),climate=profile.ClimateOverride},
            note="Matter created with SurfaceMaterialSampler.SampleMatter and VoxelWorld.Set; optional water is a separate illustrative offline surface, not a solid material or the URP shader."};
        File.WriteAllText(Path.Combine(folder,name+".json"),JsonSerializer.Serialize(metadata,new JsonSerializerOptions{WriteIndented=true,IncludeFields=true}));
        Console.WriteLine(name+": "+records+" actual matter cells; "+counts.Count+" material types; snow="+minSnow.ToString("F1")+".."+maxSnow.ToString("F1")+"m; cutaway="+removed);
    }
}
