using System;
using System.IO;
using System.Text.Json;
using System.Collections.Generic;
using AshenVoxel.Core;
using AshenVoxel.Core.Trees;

public static class ExportTrees
{
    static uint Mix(uint v){unchecked{v^=v>>16;v*=0x7feb352du;v^=v>>15;v*=0x846ca68bu;return v^(v>>16);}}
    public static void Main(string[] args)
    {
        Directory.CreateDirectory(args[0]);
        string only=args.Length>1?args[1]:null;
        if(string.Equals(only,"all",StringComparison.OrdinalIgnoreCase))only=null;
        int variations=args.Length>2?int.Parse(args[2]):3;
        if(variations<1||variations>5)throw new ArgumentOutOfRangeException(nameof(variations));
        if(only!=null&&!Enum.TryParse<TreeSpecies>(only,true,out _))throw new ArgumentException("Unknown tree species");
        foreach(TreeSpecies species in Enum.GetValues(typeof(TreeSpecies)))
        {
            if(only!=null&&!species.ToString().Equals(only,StringComparison.OrdinalIgnoreCase))continue;
            for(int variation=0;variation<variations;variation++) Export(args[0],species,variation,false);
        }
        if(only==null||only.Equals("Oak",StringComparison.OrdinalIgnoreCase))Export(args[0],TreeSpecies.Oak,0,true);
    }
    static void Export(string output,TreeSpecies species,int variation,bool fruit)
    {
        var p=TreePresets.Get(species);
        if(fruit)p=TreePresets.FruitedOak;
        int seed=714103+variation*9173;
        float span=Math.Max(10,p.CrownRadius*2.8f+4);
        int size=(int)Math.Ceiling(span/.1f),height=(int)Math.Ceiling((p.Height*1.3f+3)/.1f);
        var w=new VoxelWorld(size,height,size,.1f,seed);
        float cx=size*.05f,cz=cx;
        // Actual matter plane; tinted grass/litter, never a painted background.
        for(int z=0;z<size;z++)for(int x=0;x<size;x++)
        {
            double field=Generation.Noise(x*.035,z*.035,seed+77);
            bool litter=field>.29 && Math.Pow(x*.1-cx,2)+Math.Pow(z*.1-cz,2)<p.CrownRadius*p.CrownRadius*1.3;
            uint tint=litter?VoxelColor.Pack((byte)(77+field*9),(byte)(66+field*7),(byte)(43+field*5)):
                VoxelColor.Pack((byte)(61+field*9),(byte)(78+field*10),(byte)(41+field*7));
            w.Set(x,0,z,MaterialId.Stone);
            w.Set(x,1,z,MaterialId.Dirt);
            w.Set(x,2,z,litter?MaterialId.Litter:MaterialId.Grass,tint);
        }
        var result=TreeGenerator.Generate(w,p,cx,.3f,cz,seed);
        string name=species.ToString().ToLowerInvariant()+(fruit?"-fruit":"")+"-"+variation;
        var min=new double[]{double.MaxValue,double.MaxValue,double.MaxValue};
        var max=new double[]{double.MinValue,double.MinValue,double.MinValue};
        var counts=new Dictionary<string,long>();
        int records=0;
        foreach(var c in w.Chunks.Values)records+=c.NonAirCount;
        using(var f=new BinaryWriter(File.Create(Path.Combine(output,name+".bin"))))
        {
            f.Write(size);f.Write(height);f.Write(size);f.Write(.1f);f.Write(records);
            foreach(var pair in w.Chunks)
            {
                var chunk=pair.Value;
                for(int i=0;i<Chunk.Volume;i++)
                {
                    var id=chunk.Cells[i];if(id==MaterialId.Air)continue;
                    var local=Chunk.Decode(i);
                    int x=chunk.Coordinate.x*16+local.x,y=chunk.Coordinate.y*16+local.y,z=chunk.Coordinate.z*16+local.z;
                    if(!w.InBounds(x,y,z))continue;
                    f.Write((ushort)x);f.Write((ushort)y);f.Write((ushort)z);f.Write((byte)id);f.Write(w.GetColor(x,y,z));
                    string key=id.ToString();if(!counts.ContainsKey(key))counts[key]=0;counts[key]++;
                    if(id==MaterialId.Wood||id==MaterialId.Leaves||key=="Fruit")
                    {
                        min[0]=Math.Min(min[0],x*.1);min[1]=Math.Min(min[1],y*.1);min[2]=Math.Min(min[2],z*.1);
                        max[0]=Math.Max(max[0],(x+1)*.1);max[1]=Math.Max(max[1],(y+1)*.1);max[2]=Math.Max(max[2],(z+1)*.1);
                    }
                }
            }
        }
        var meta=new {name,species=species.ToString(),seed,voxel_m=.1,dimensions_cells=new[]{size,height,size},bounds_min_m=min,bounds_max_m=max,center_m=new[]{cx,.3f,cz},fruit_enabled=fruit,branch_count=result.Branches.Count,voxel_candidates=result.VoxelCandidates,foliage_clusters=result.FoliageClusters,truncated=result.Truncated,truncation_reason=result.TruncationReason,material_counts=counts,parameters=new{p.Height,p.TrunkRadius,p.CrownRadius,p.BranchLevels,p.BranchCount,p.BranchAngle,p.ApicalDominance,p.Tropism,p.FoliageDensity,p.LeafClusterRadius,p.FruitProbability,p.Variation,p.Age,p.WhorlSize,p.CrownStart,p.ConeTaper,p.BranchDroop,p.Gnarl,p.RootFlare,p.ForkAngle,p.BarkRelief,p.BarkRidgeCount,BarkColor=p.BarkColor.ToString("X8"),LeafColor=p.LeafColor.ToString("X8"),FruitColor=p.FruitColor.ToString("X8")}};
        File.WriteAllText(Path.Combine(output,name+".json"),JsonSerializer.Serialize(meta,new JsonSerializerOptions{WriteIndented=true}));
        Console.WriteLine("Exported "+name+"; "+records+" occupied cells");
    }
}
