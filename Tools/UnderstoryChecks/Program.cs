using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using AshenVoxel.Core;
using AshenVoxel.Core.Details;

static class Program
{
    static int checks;
    static void Check(bool value,string name){if(!value)throw new Exception("FAIL "+name);checks++;Console.WriteLine("PASS "+name);}
    static void Reject(Action operation,string name){try{operation();}catch(ArgumentException){Check(true,name);return;}throw new Exception("FAIL accepted "+name);}
    static Dictionary<Int3,(MaterialId material,uint tint)> Read(VoxelWorld world)
    {
        var cells=new Dictionary<Int3,(MaterialId,uint)>();foreach(var pair in world.Chunks)
            for(int i=0;i<Chunk.Volume;i++)if(pair.Value.Cells[i]!=MaterialId.Air)
            {Int3 l=Chunk.Decode(i),at=new Int3(pair.Key.x*16+l.x,pair.Key.y*16+l.y,pair.Key.z*16+l.z);cells.Add(at,(world.Get(at),world.GetTint(at)));}
        return cells;
    }
    static ulong Hash(Dictionary<Int3,(MaterialId material,uint tint)> cells,Int3 origin)
    {
        ulong h=1469598103934665603;unchecked{foreach(var pair in cells.OrderBy(p=>p.Key.z).ThenBy(p=>p.Key.y).ThenBy(p=>p.Key.x))
            {foreach(uint n in new[]{(uint)(pair.Key.x-origin.x),(uint)(pair.Key.y-origin.y),(uint)(pair.Key.z-origin.z),(uint)pair.Value.material,pair.Value.tint}){h^=n;h*=1099511628211;}}}return h;
    }
    static bool Rooted(Dictionary<Int3,(MaterialId material,uint tint)> cells,int rootY)
    {
        var remaining=new HashSet<Int3>(cells.Keys);Int3[] neighbors={new Int3(1,0,0),new Int3(-1,0,0),new Int3(0,1,0),new Int3(0,-1,0),new Int3(0,0,1),new Int3(0,0,-1)};
        while(remaining.Count>0)
        {
            Int3 first=remaining.First();remaining.Remove(first);var queue=new Queue<Int3>();queue.Enqueue(first);bool root=false;
            while(queue.Count>0){Int3 at=queue.Dequeue();root|=at.y==rootY;foreach(var d in neighbors){Int3 next=at+d;if(remaining.Remove(next))queue.Enqueue(next);}}
            if(!root)return false;
        }
        return true;
    }
    static void Main(string[] args)
    {
        Reject(()=>new PlantProfile((PlantKind)100),"undefined plant identities rejected");
        Reject(()=>new PlantProfile(PlantKind.Fern,heightMeters:double.NaN),"nonfinite morphology rejected");
        Reject(()=>new PlantProfile(PlantKind.Fern,heightMeters:5),"height envelope bounded before rasterization");
        Reject(()=>new PlantProfile(PlantKind.Fern,radiusMeters:3),"width envelope bounded before rasterization");
        Reject(()=>new PlantProfile(PlantKind.Fern,density:1.1),"invalid density rejected");
        Reject(()=>new PlantProfile(PlantKind.Fern,paletteSteps:7),"palette count bounded");
        Reject(()=>new PlantProfile(PlantKind.Fern,foliageColor:0x00557744),"translucent intrinsic tints rejected");
        Reject(()=>new PlantGenerationBudget(maxCandidates:0),"zero candidate budget rejected");
        Reject(()=>new PlantGenerationBudget(maxCells:0),"zero cell budget rejected");
        Reject(()=>PlantGenerator.Generate(new VoxelWorld(16,16,16,.05f,1),PlantPresets.For(PlantKind.Fern),new Int3(8,1,8),1),"matter scale stays explicitly10cm");
        Reject(()=>PlantGenerator.Generate(new VoxelWorld(16,16,16,.1f,1),PlantPresets.For(PlantKind.Fern),new Int3(int.MaxValue,0,0),1),"integer-overflow anchors rejected");
        var observed=new Dictionary<string,object>();int different=0;
        foreach(PlantKind kind in Enum.GetValues(typeof(PlantKind)))
        {
            var p=PlantPresets.For(kind);var a=new VoxelWorld(96,80,96,.1f,1);var b=new VoxelWorld(96,80,96,.1f,1);var c=new VoxelWorld(96,80,96,.1f,1);
            Int3 origin=new Int3(44,2,44),shift=new Int3(3,1,2);PlantResult result=PlantGenerator.Generate(a,p,origin,714103);
            PlantResult copy=PlantGenerator.Generate(b,p,origin,714103);PlantGenerator.Generate(c,p,origin+shift,714103);
            var cells=Read(a);ulong hash=Hash(cells,origin);
            Check(result.WrittenCells>0&&!result.Truncated&&result.WrittenCells==cells.Count&&copy.WrittenCells==result.WrittenCells,kind+" nonempty complete specimen with exact unique write count");
            Check(Hash(Read(b),origin)==hash&&Hash(Read(c),origin+shift)==hash,kind+" seeded repeatability and integer translation invariance");
            Check(cells.Keys.All(q=>q.x>=result.MinCell.x&&q.x<=result.MaxCell.x&&q.y>=result.MinCell.y&&q.y<=result.MaxCell.y&&q.z>=result.MinCell.z&&q.z<=result.MaxCell.z)&&
                result.MinCell.x>=result.RequestedMinCell.x&&result.MinCell.y>=result.RequestedMinCell.y&&result.MinCell.z>=result.RequestedMinCell.z&&result.MaxCell.x<=result.RequestedMaxCell.x&&result.MaxCell.y<=result.RequestedMaxCell.y&&result.MaxCell.z<=result.RequestedMaxCell.z,kind+" actual occupied cells fit requested conservative envelope");
            Check(cells.Values.GroupBy(v=>v.material).All(g=>g.Select(v=>v.tint).Distinct().Count()<=p.PaletteSteps)&&cells.Values.All(v=>(v.tint>>24)==255),kind+" small explicit opaque palette per material");
            Check(Rooted(cells,origin.y),kind+" all six-connected components reach ground anchor level");
            Check(result.MaterialCounts.Sum(kv=>kv.Value)==cells.Count&&result.Candidates<=250000,kind+" bounded work and material inventory agree");
            var changed=new VoxelWorld(96,80,96,.1f,1);PlantGenerator.Generate(changed,p,origin,714104);if(Hash(Read(changed),origin)!=hash)different++;
            var pixelWorld=new VoxelWorld(16,16,16,.1f,1);var pixel=PlantGenerator.Generate(pixelWorld,p.With(heightMeters:.1,radiusMeters:.05),new Int3(8,1,8),5);
            Check(pixel.WrittenCells==1&&!pixel.Truncated,kind+" supports one-cell physical mockup");
            observed.Add(kind.ToString(),new{cells=result.WrittenCells,candidates=result.Candidates,dimensions_cells=new[]{result.MaxCell.x-result.MinCell.x+1,result.MaxCell.y-result.MinCell.y+1,result.MaxCell.z-result.MinCell.z+1},material_counts=result.MaterialCounts.ToDictionary(kv=>kv.Key.ToString(),kv=>kv.Value),hash=hash.ToString("x16")});
        }
        Check(different==10,"all ten morphology families vary with specimen seed");
        var anchor=new Int3(32,2,32);var tinyBudget=new VoxelWorld(64,64,64,.1f,1);var stopped=PlantGenerator.Generate(tinyBudget,PlantPresets.For(PlantKind.Fern),anchor,1,new PlantGenerationBudget(maxCandidates:3));
        Check(stopped.Candidates==3&&(stopped.Truncation&PlantTruncation.CandidateBudget)!=0&&Read(tinyBudget).Count<=3,"candidate budget never overruns and reports incomplete specimen");
        var cellBudget=new VoxelWorld(64,64,64,.1f,1);var capped=PlantGenerator.Generate(cellBudget,PlantPresets.For(PlantKind.Shrub),anchor,1,new PlantGenerationBudget(maxCells:3));
        Check(capped.WrittenCells==3&&(capped.Truncation&PlantTruncation.CellBudget)!=0&&Read(cellBudget).Count==3,"unique-cell budget never overruns and reports incomplete specimen");
        var edgeWorld=new VoxelWorld(16,16,16,.1f,1);var edge=PlantGenerator.Generate(edgeWorld,PlantPresets.For(PlantKind.Fern),new Int3(0,1,0),1);
        Check((edge.Truncation&PlantTruncation.WorldBoundary)!=0&&Read(edgeWorld).Keys.All(edgeWorld.InBounds),"world-boundary clipping is observable and never writes outside finite matter");
        var blockedWorld=new VoxelWorld(64,64,64,.1f,1);blockedWorld.Set(anchor,MaterialId.Granite,0xff445566);long revision=blockedWorld.Revision;
        var blocked=PlantGenerator.Generate(blockedWorld,PlantPresets.For(PlantKind.Shrub),anchor,1);
        Check(blockedWorld.Get(anchor)==MaterialId.Granite&&blockedWorld.GetTint(anchor)==0xff445566&&blocked.BlockedCells>0&&(blocked.Truncation&PlantTruncation.Occupied)!=0&&blockedWorld.Revision>revision,"preexisting authoritative matter and tint survive intersecting plant generation");
        var mushroomWorld=new VoxelWorld(64,64,64,.1f,1);var mushroom=PlantGenerator.Generate(mushroomWorld,PlantPresets.For(PlantKind.Mushroom),anchor,1);
        Check(mushroom.MaterialCounts.Count==1&&mushroom.MaterialCounts.ContainsKey(MaterialId.Fungus),"mushroom stalk and cap share distinct Fungus matter rather than recolored leaves");
        var shrubWorld=new VoxelWorld(64,64,64,.1f,1);var shrub=PlantGenerator.Generate(shrubWorld,PlantPresets.For(PlantKind.Shrub).With(fruitProbability:1),anchor,1);
        Check(shrub.WrittenCells>=200&&shrub.MaterialCounts.ContainsKey(MaterialId.Wood)&&shrub.MaterialCounts.ContainsKey(MaterialId.Leaves)&&shrub.MaterialCounts.ContainsKey(MaterialId.Fruit),"large shrubs use hundreds of cells with shared wood leaves and optional berries");
        var fernWorld=new VoxelWorld(64,64,64,.1f,1);var fern=PlantGenerator.Generate(fernWorld,PlantPresets.For(PlantKind.Fern),anchor,1);double box=(fern.MaxCell.x-fern.MinCell.x+1)*(fern.MaxCell.y-fern.MinCell.y+1)*(fern.MaxCell.z-fern.MinCell.z+1);
        Check(fern.MaterialCounts.Count==1&&fern.MaterialCounts.ContainsKey(MaterialId.Leaves)&&fern.WrittenCells/box<.10,"fern uses shared Leaves with open frond/pinna negative space at10cm");
        var reedWorld=new VoxelWorld(64,64,64,.1f,1);PlantGenerator.Generate(reedWorld,PlantPresets.For(PlantKind.Reed),anchor,714103);
        var crossSection=new HashSet<Int3>(Read(reedWorld).Keys.Where(k=>k.y==anchor.y+3));int axes=0;
        Int3[] planarNeighbors={new Int3(1,0,0),new Int3(-1,0,0),new Int3(0,0,1),new Int3(0,0,-1)};
        while(crossSection.Count>0){axes++;Int3 first=crossSection.First();crossSection.Remove(first);var queue=new Queue<Int3>();queue.Enqueue(first);
            while(queue.Count>0){Int3 at=queue.Dequeue();foreach(Int3 d in planarNeighbors)if(crossSection.Remove(at+d))queue.Enqueue(at+d);}}
        Check(axes>=4,"reed clump retains multiple separated main axes above the ground rhizomes");
        Check(shrubWorld.GenerationVersion==Generation.Version,"detail generation does not rewrite original world-generator provenance");
        int sweepCount=0;bool sweep=true;
        foreach(PlantKind kind in Enum.GetValues(typeof(PlantKind)))for(int sample=0;sample<12;sample++)
        {
            var profile=PlantPresets.For(kind);if(sample>=8)profile=profile.With(heightMeters:Math.Max(.12,profile.HeightMeters*.55),radiusMeters:Math.Max(.12,profile.RadiusMeters*.6),paletteSteps:2+sample%5);
            var specimen=new VoxelWorld(96,80,96,.1f,1);var result=PlantGenerator.Generate(specimen,profile,new Int3(44,2,44),sample*7919+17);
            var cells=Read(specimen);sweep&=!result.Truncated&&result.WrittenCells>0&&Rooted(cells,2)&&cells.Values.GroupBy(v=>v.material).All(g=>g.Select(v=>v.tint).Distinct().Count()<=profile.PaletteSteps);sweepCount++;
        }
        Check(sweep&&sweepCount==120,"120 seed and juvenile-size specimens preserve ground support budgets and small palettes");
        var giantReedWorld=new VoxelWorld(128,96,128,.1f,1);PlantGenerator.Generate(giantReedWorld,PlantPresets.For(PlantKind.Reed).With(heightMeters:4,radiusMeters:2.5),new Int3(64,2,64),2);
        Check(Rooted(Read(giantReedWorld),2),"large custom reed blades attach at actual curved-axis positions");
        var report=new{status="PASS",checks,scope="Actual engine-free deterministic plant matter on0.1m grid; no Unity, growth/season/structural simulation or performance claim",plant_generation_version=PlantGenerator.Version,presets=observed};
        File.WriteAllText(args[0],JsonSerializer.Serialize(report,new JsonSerializerOptions{WriteIndented=true})+"\n");Console.WriteLine("All "+checks+" understory plant checks passed.");
    }
}
