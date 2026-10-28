using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Text.Json;
using AshenVoxel.Core;
using AshenVoxel.Core.Details;
using AshenVoxel.Core.Rocks;

static class ExportGroundCover
{
    sealed class Specimen
    {
        public VoxelWorld World; public Int3 Min,Max; public int Seed,BaseCellY; public object Profile,Result; public string Name;
    }
    static readonly int[] Seeds={714103,271828,161803};
    static readonly JsonSerializerOptions Json=new JsonSerializerOptions{WriteIndented=true,IncludeFields=true};
    static void Main(string[] args)
    {
        Directory.CreateDirectory(args[0]);
        if(args.Length>1&&args[1]=="shapes")
        {
            foreach(RockShape shape in new[]{RockShape.AngularBoulder,RockShape.Slab,RockShape.Wedge,RockShape.Spire,RockShape.SplitOutcrop,RockShape.Cluster})
            {
                var shapes=new List<Specimen>();foreach(int seed in Seeds)shapes.Add(Rock(MaterialId.Granite,seed,shape));
                string shapeLabel=shape==RockShape.AngularBoulder?"Angular boulder":shape==RockShape.SplitOutcrop?"Split outcrop":shape.ToString();
                Group(args[0],"shape-"+shape.ToString().ToLowerInvariant(),"rock-shape",shapeLabel+" / same granite",shapes,MaterialId.Gravel);
            }
            return;
        }
        foreach(PlantKind kind in Enum.GetValues(typeof(PlantKind)))
        {
            var specimens=new List<Specimen>();
            foreach(int seed in Seeds)specimens.Add(Plant(kind,seed));
            Group(args[0],"plant-"+kind.ToString().ToLowerInvariant(),"plant",kind.ToString(),specimens,PlantGround(kind));
        }
        foreach(MaterialId material in new[]{MaterialId.Granite,MaterialId.Limestone,MaterialId.Sandstone,MaterialId.Shale,MaterialId.Stone})
        {
            var specimens=new List<Specimen>();foreach(int seed in Seeds)specimens.Add(Rock(material,seed));
            Group(args[0],"rock-"+material.ToString().ToLowerInvariant(),"rock",material==MaterialId.Stone?"Basalt":material.ToString(),specimens,MaterialId.Gravel);
        }
        var berries=new List<Specimen>();foreach(int seed in Seeds)berries.Add(Plant(PlantKind.Shrub,seed,false,true));
        Group(args[0],"berry-shrub","plant","Shrub / optional berries",berries,MaterialId.Litter);
        var micro=new List<Specimen>();
        foreach(PlantKind kind in new[]{PlantKind.Mushroom,PlantKind.BroadleafHerb,PlantKind.Flower})micro.Add(Plant(kind,Seeds[0],true));
        Group(args[0],"micro-plants","micro","One cell per design",micro,MaterialId.Litter);
        Arrangement(args[0],false);Arrangement(args[0],true);
    }
    static Specimen Plant(PlantKind kind,int seed,bool micro=false,bool berries=false)
    {
        PlantProfile profile=PlantPresets.For(kind);if(micro)profile=profile.With(heightMeters:.1,radiusMeters:.05,stems:1);
        if(berries)profile=profile.With(fruitProbability:1);
        var world=new VoxelWorld(128,96,128,.1f,seed);var result=PlantGenerator.Generate(world,profile,new Int3(64,4,64),seed);
        if(result.WrittenCells==0||result.Truncated)throw new Exception("Standalone plant incomplete: "+kind+" "+result.Truncation);
        return new Specimen{World=world,Min=result.MinCell,Max=result.MaxCell,Seed=seed,BaseCellY=4,Profile=profile,Result=result,Name=kind.ToString()};
    }
    static Specimen Rock(MaterialId material,int seed,RockShape? shape=null)
    {
        RockProfile profile=RockPresets.For(material,seed);if(shape.HasValue)profile=profile.With(shape:shape.Value);
        var world=new VoxelWorld(128,96,128,.1f,seed);
        var result=RockGenerator.Generate(world,new Int3(64,8,64),profile);
        if(!result.HasMatter||result.Truncated||result.ClippedCells>0)throw new Exception("Standalone rock incomplete: "+material);
        return new Specimen{World=world,Min=result.BoundsMinCellInclusive,Max=result.BoundsMaxCellInclusive,Seed=seed,BaseCellY=8,Profile=profile,Result=result,Name=material.ToString()};
    }
    static MaterialId PlantGround(PlantKind kind)=>kind==PlantKind.Cactus||kind==PlantKind.Succulent?MaterialId.Sand:
        kind==PlantKind.Cushion?MaterialId.Gravel:kind==PlantKind.Reed?MaterialId.Mud:MaterialId.Litter;
    static void Group(string folder,string name,string category,string label,List<Specimen> specimens,MaterialId ground)
    {
        const int pad=2,gap=5;int width=2,depth=0,height=0;
        foreach(var s in specimens){width+=s.Max.x-s.Min.x+1+pad*2+gap;depth=Math.Max(depth,s.Max.z-s.Min.z+1+pad*2);height=Math.Max(height,s.Max.y-s.Min.y+1+2);}
        var world=new VoxelWorld(width+2,height+2,depth+2,.1f,Seeds[0]);var details=new List<object>();int cursor=2;
        foreach(var s in specimens)
        {
            int w=s.Max.x-s.Min.x+1,d=s.Max.z-s.Min.z+1;
            int z0=(depth-d)/2+1;Int3 offset=new Int3(cursor+pad-s.Min.x,2-s.BaseCellY,z0-s.Min.z);
            for(int x=cursor;x<cursor+w+pad*2;x++)for(int z=z0-pad;z<z0+d+pad;z++)
            {
                world.Set(x,0,z,ground,GroundColor(ground,x,z));world.Set(x,1,z,ground,GroundColor(ground,x,z));
            }
            foreach(var pair in s.World.Chunks)for(int i=0;i<Chunk.Volume;i++)
            {
                MaterialId m=pair.Value.Cells[i];if(m==MaterialId.Air)continue;Int3 local=Chunk.Decode(i);Int3 p=new Int3(pair.Key.x*16,pair.Key.y*16,pair.Key.z*16)+local;
                world.Set(p+offset,m,s.World.GetColor(p));
            }
            details.Add(new{name=s.Name,seed=s.Seed,profile=s.Profile,result=s.Result,actual_dimensions_m=new[]{w*.1,(s.Max.y-s.Min.y+1)*.1,d*.1},
                requested_shape=s.Profile is RockProfile rockProfile?rockProfile.Shape.ToString():null,
                resolved_shape=s.Profile is RockProfile resolvedProfile?resolvedProfile.ResolvedShape.ToString():null,
                generated_cells=Count(s.World),material_counts=Counts(s.World),palette=Palettes(s.World),packed_offset_cells=offset});
            cursor+=w+pad*2+gap;
        }
        Export(folder,name,world,new{category,label,seeds=specimens.Select(s=>s.Seed).ToArray(),specimens=details,
            note="Three independent actual generator specimens packed side by side on separate voxel presentation pads; no proxy plants or substitute rock mesh."});
    }
    static void Arrangement(string folder,bool dry)
    {
        var world=new VoxelWorld(96,64,96,.1f,Seeds[0]);var placements=new List<object>();
        for(int z=0;z<96;z++)for(int x=0;x<96;x++)
        {
            int top=5+(int)Math.Floor(1.5*Math.Sin(x*.07)+1.2*Math.Cos(z*.09));
            for(int y=0;y<=top;y++)
            {
                MaterialId m=y<top-1?(dry?MaterialId.Sandstone:MaterialId.Granite):y==top?(dry?MaterialId.Sand:MaterialId.Litter):MaterialId.Dirt;
                if(!dry&&y==top&&Math.Sin(x*.12)+Math.Cos(z*.14)>.6)m=MaterialId.Grass;
                world.Set(x,y,z,m,GroundColor(m,x,z));
            }
        }
        int[] rockXs={25,73},rockZs={59,29};
        for(int i=0;i<2;i++)
        {
            Int3 at=new Int3(rockXs[i],Top(world,rockXs[i],rockZs[i])+1,rockZs[i]);
            RockProfile profile=RockPresets.For(dry?MaterialId.Sandstone:MaterialId.Granite,Seeds[i]).With(widthMeters:1.7,heightMeters:.8,depthMeters:1.35);
            RockResult result=RockGenerator.Generate(world,at,profile);placements.Add(new{type="rock",position=at,profile,result});
        }
        PlantKind[] kinds=dry?new[]{PlantKind.Cactus,PlantKind.Succulent,PlantKind.GrassTuft,PlantKind.Succulent,PlantKind.Cactus,PlantKind.GrassTuft,PlantKind.Succulent,PlantKind.GrassTuft}:
            new[]{PlantKind.Shrub,PlantKind.Shrub,PlantKind.Fern,PlantKind.Fern,PlantKind.Fern,PlantKind.Fern,PlantKind.BroadleafHerb,PlantKind.Mushroom,PlantKind.Mushroom,PlantKind.Flower,PlantKind.GrassTuft,PlantKind.GrassTuft,PlantKind.Mushroom,PlantKind.BroadleafHerb};
        int[] xs=dry?new[]{19,41,78,58,72,21,46,82}:new[]{20,75,44,24,66,52,39,62,31,78,18,72,43,49};
        int[] zs=dry?new[]{21,28,72,68,49,76,51,18}:new[]{23,73,30,73,48,68,58,23,46,20,45,55,79,48};
        for(int i=0;i<kinds.Length;i++)
        {
            Int3 at=new Int3(xs[i],Top(world,xs[i],zs[i])+1,zs[i]);PlantProfile profile=PlantPresets.For(kinds[i]);
            if(kinds[i]==PlantKind.Shrub)profile=profile.With(heightMeters:1.1,radiusMeters:.85,fruitProbability:.22);
            if(dry&&kinds[i]==PlantKind.GrassTuft)profile=profile.With(foliageColor:0xff92865a,heightMeters:.32,radiusMeters:.28);
            PlantResult result=PlantGenerator.Generate(world,profile,at,Seeds[i%3]+i*17);placements.Add(new{type=kinds[i].ToString(),position=at,profile,result});
        }
        Export(folder,dry?"dry-floor":"forest-floor",world,new{category="arrangement",label=dry?"Dry ground study":"Forest ground study",placements,
            note="Authored arrangement of actual procedural plant/rock specimens on controlled voxel terrain. This is a small composition mockup, not neural terrain, ecological world scatter, a biome simulator or a Unity screenshot."});
    }
    static int Top(VoxelWorld world,int x,int z){for(int y=world.SizeY-1;y>=0;y--)if(world.Get(x,y,z)!=MaterialId.Air)return y;return 0;}
    static uint GroundColor(MaterialId m,int x,int z)
    {
        uint c=VoxelColor.Base(m);int band=(x/7+z/9)%4;double scale=.94+.04*band;
        return VoxelColor.Pack((byte)Math.Min(255,((c>>16)&255)*scale),(byte)Math.Min(255,((c>>8)&255)*scale),(byte)Math.Min(255,(c&255)*scale));
    }
    static int Count(VoxelWorld world){int n=0;foreach(var c in world.Chunks.Values)n+=c.NonAirCount;return n;}
    static Dictionary<string,int> Counts(VoxelWorld world)
    {
        var d=new Dictionary<string,int>();foreach(var c in world.Chunks.Values)foreach(MaterialId m in c.Cells)
        {if(m==MaterialId.Air)continue;string key=m.ToString();d.TryGetValue(key,out int n);d[key]=n+1;}return d;
    }
    static Dictionary<string,uint[]> Palettes(VoxelWorld world)
    {
        var d=new Dictionary<string,HashSet<uint>>();foreach(var pair in world.Chunks)for(int i=0;i<Chunk.Volume;i++)
        {MaterialId m=pair.Value.Cells[i];if(m==MaterialId.Air)continue;Int3 p=new Int3(pair.Key.x*16,pair.Key.y*16,pair.Key.z*16)+Chunk.Decode(i);string key=m.ToString();if(!d.TryGetValue(key,out var set))d[key]=set=new HashSet<uint>();set.Add(world.GetColor(p));}
        return d.ToDictionary(p=>p.Key,p=>p.Value.OrderBy(x=>x).ToArray());
    }
    static void Export(string folder,string name,VoxelWorld world,object scene)
    {
        int count=Count(world);Int3 lo=new Int3(int.MaxValue,int.MaxValue,int.MaxValue),hi=new Int3(0,0,0);
        using(var w=new BinaryWriter(File.Create(Path.Combine(folder,name+".bin"))))
        {
            w.Write(world.SizeX);w.Write(world.SizeY);w.Write(world.SizeZ);w.Write(world.VoxelSize);w.Write(count);
            foreach(var pair in world.Chunks)for(int i=0;i<Chunk.Volume;i++)
            {
                MaterialId m=pair.Value.Cells[i];if(m==MaterialId.Air)continue;Int3 p=new Int3(pair.Key.x*16,pair.Key.y*16,pair.Key.z*16)+Chunk.Decode(i);
                w.Write((ushort)p.x);w.Write((ushort)p.y);w.Write((ushort)p.z);w.Write((byte)m);w.Write(world.GetColor(p));
                lo=new Int3(Math.Min(lo.x,p.x),Math.Min(lo.y,p.y),Math.Min(lo.z,p.z));hi=new Int3(Math.Max(hi.x,p.x),Math.Max(hi.y,p.y),Math.Max(hi.z,p.z));
            }
        }
        var meta=new{name,voxel_m=.1,dimensions_cells=new[]{world.SizeX,world.SizeY,world.SizeZ},occupied_cells=count,
            bounds_min_m=new[]{lo.x*.1,lo.y*.1,lo.z*.1},bounds_max_m=new[]{(hi.x+1)*.1,(hi.y+1)*.1,(hi.z+1)*.1},material_counts=Counts(world),scene,
            generation_version=new{plant=PlantGenerator.Version,rock=RockGenerator.Version,catalog=MaterialCatalog.Version},
            note="Exact occupied10cm cells generated by actual shared C# code and VoxelWorld.Set. Offline camera/lighting only; NOT Unity or neural terrain."};
        File.WriteAllText(Path.Combine(folder,name+".json"),JsonSerializer.Serialize(meta,Json));Console.WriteLine(name+": "+count+" cells");
    }
}
