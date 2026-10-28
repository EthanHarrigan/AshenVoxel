using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using AshenVoxel.Core;
using AshenVoxel.Core.Trees;

static class Program
{
    static int checks;
    static readonly Int3[] Neighbors={new Int3(1,0,0),new Int3(-1,0,0),new Int3(0,1,0),new Int3(0,-1,0),new Int3(0,0,1),new Int3(0,0,-1)};
    sealed class Summary
    {
        public string Species {get;set;}
        public int Wood {get;set;}public int Leaves {get;set;}public int Fruit {get;set;}public int Segments {get;set;}
        public int Clusters {get;set;}public int LeafColors {get;set;}public int WhiteBark {get;set;}public int DarkBark {get;set;}
        public int Components {get;set;}public int GroundedComponents {get;set;}
        public bool AllFoliageSupported {get;set;}public bool EveryFruitTouchesLeaf {get;set;}
        public double Height {get;set;}public double Width {get;set;}public double Milliseconds {get;set;}
        public long Candidates {get;set;}public string Truncation {get;set;}public ulong Hash {get;set;}
        public int AllocatedChunks {get;set;}public long MaterialBytes {get;set;}public long TintBytes {get;set;}
    }
    static void Expect(bool condition,string name){if(!condition)throw new Exception("FAIL "+name);checks++;Console.WriteLine("PASS "+name);}
    static void Reject(Action action,string name){try{action();}catch(ArgumentException){Expect(true,name);return;}throw new Exception("FAIL accepts "+name);}
    static VoxelWorld World(TreeParameters p)
    {
        int width=(int)Math.Ceiling((p.CrownRadius*3.1+5)/.1);
        int height=(int)Math.Ceiling((p.Height*1.32+4)/.1);
        var w=new VoxelWorld(width,height,width,.1f,714103);
        for(int z=0;z<width;z++)for(int x=0;x<width;x++)
        {w.Set(x,0,z,MaterialId.Stone);w.Set(x,1,z,MaterialId.Dirt);w.Set(x,2,z,MaterialId.Grass);}
        return w;
    }
    static TreeResult Generate(VoxelWorld w,TreeParameters p,int seed=714103,TreeGenerationBudget budget=null)
        =>TreeGenerator.Generate(w,p,w.SizeX*w.VoxelSize*.5f,.3f,w.SizeZ*w.VoxelSize*.5f,seed,budget);
    static Summary Inspect(VoxelWorld w,TreeResult result)
    {
        var wood=new HashSet<Int3>();var leaves=new HashSet<Int3>();var fruit=new HashSet<Int3>();var colors=new HashSet<uint>();
        int white=0,dark=0;ulong hash=1469598103934665603;
        var keys=new List<Int3>(w.Chunks.Keys);keys.Sort((a,b)=>{int c=a.z.CompareTo(b.z);if(c==0)c=a.y.CompareTo(b.y);return c==0?a.x.CompareTo(b.x):c;});
        foreach(Int3 key in keys)
        {
            Chunk chunk=w.Chunks[key];
            for(int i=0;i<Chunk.Volume;i++)
            {
                MaterialId id=chunk.Cells[i];if(id==MaterialId.Air)continue;
                Int3 local=Chunk.Decode(i),cell=new Int3(key.x*16+local.x,key.y*16+local.y,key.z*16+local.z);
                uint tint=chunk.GetTint(local.x,local.y,local.z);
                unchecked{hash=(hash^(uint)cell.x)*1099511628211;hash=(hash^(uint)cell.y)*1099511628211;hash=(hash^(uint)cell.z)*1099511628211;hash=(hash^(byte)id)*1099511628211;hash=(hash^tint)*1099511628211;}
                if(id==MaterialId.Wood||id==MaterialId.Leaves||id==MaterialId.Fruit)
                    if((tint>>24)!=255)throw new Exception("Tree appearance missing/invalid at "+cell);
                if(id==MaterialId.Wood)
                {
                    wood.Add(cell);int r=(int)((tint>>16)&255),g=(int)((tint>>8)&255),b=(int)(tint&255);
                    if(r>140&&g>135&&b>110)white++;if(r<110&&g<110&&b<100)dark++;
                }
                if(id==MaterialId.Leaves){leaves.Add(cell);colors.Add(tint);}
                if(id==MaterialId.Fruit)fruit.Add(cell);
            }
        }
        var remaining=new HashSet<Int3>(wood);int components=0,grounded=0;
        while(remaining.Count>0)
        {
            Int3 first=default;foreach(Int3 c in remaining){first=c;break;}
            remaining.Remove(first);var queue=new Queue<Int3>();queue.Enqueue(first);bool anchored=false;
            while(queue.Count>0)
            {
                Int3 c=queue.Dequeue();
                foreach(Int3 d in Neighbors)
                {
                    Int3 n=c+d;if(remaining.Remove(n))queue.Enqueue(n);
                    MaterialId id=w.Get(n);
                    if(id==MaterialId.Stone||id==MaterialId.Dirt||id==MaterialId.Grass||id==MaterialId.Mud||id==MaterialId.Litter)anchored=true;
                }
            }
            components++;if(anchored)grounded++;
        }
        var pending=new HashSet<Int3>(leaves);pending.UnionWith(fruit);
        var bfs=new Queue<Int3>();
        foreach(Int3 c in wood)foreach(Int3 d in Neighbors){Int3 n=c+d;if(pending.Remove(n))bfs.Enqueue(n);}
        while(bfs.Count>0){Int3 c=bfs.Dequeue();foreach(Int3 d in Neighbors){Int3 n=c+d;if(pending.Remove(n))bfs.Enqueue(n);}}
        bool attached=true;
        foreach(Int3 c in fruit)
        {bool found=false;foreach(Int3 d in Neighbors)if(leaves.Contains(c+d))found=true;if(!found)attached=false;}
        int tinted=0;foreach(var chunk in w.Chunks.Values)if(chunk.HasTints)tinted++;
        return new Summary{Species=result.Species.ToString(),Wood=wood.Count,Leaves=leaves.Count,Fruit=fruit.Count,Segments=result.Branches.Count,
            Clusters=result.FoliageClusters,LeafColors=colors.Count,WhiteBark=white,DarkBark=dark,Components=components,GroundedComponents=grounded,
            AllFoliageSupported=pending.Count==0,EveryFruitTouchesLeaf=attached,Height=result.BoundsMax.y-result.BoundsMin.y,
            Width=Math.Max(result.BoundsMax.x-result.BoundsMin.x,result.BoundsMax.z-result.BoundsMin.z),Candidates=result.VoxelCandidates,
            Truncation=result.TruncationReason,Hash=hash,AllocatedChunks=w.Chunks.Count,MaterialBytes=(long)w.Chunks.Count*Chunk.Volume,TintBytes=(long)tinted*Chunk.Volume*4};
    }
    sealed class Section
    {
        public double HeightM {get;set;}public int WoodCells {get;set;}public double EquivalentDiameterM {get;set;}
        public double MinRadiusM {get;set;}public double MaxRadiusM {get;set;}
    }
    static Section CrossSection(VoxelWorld w,double height)
    {
        int cy=(int)Math.Floor((.3+height)/w.VoxelSize),count=0;
        double cx=w.SizeX*w.VoxelSize*.5,cz=w.SizeZ*w.VoxelSize*.5,s=w.VoxelSize;
        for(int z=0;z<w.SizeZ;z++)for(int x=0;x<w.SizeX;x++)if(w.Get(x,cy,z)==MaterialId.Wood)count++;
        double min=double.MaxValue,max=0;
        // Radial rays measure the first outer boundary, independent of grid-cell enumeration.
        for(int i=0;i<128;i++)
        {
            double angle=i*Math.PI*2/128,r=0;
            while(r<12&&w.Get((int)Math.Floor((cx+Math.Cos(angle)*r)/s),cy,(int)Math.Floor((cz+Math.Sin(angle)*r)/s))==MaterialId.Wood)r+=s*.25;
            min=Math.Min(min,r);max=Math.Max(max,r);
        }
        return new Section{HeightM=height,WoodCells=count,EquivalentDiameterM=2*Math.Sqrt(count*s*s/Math.PI),MinRadiusM=min,MaxRadiusM=max};
    }
    static void Skeleton(TreeResult tree)
    {
        bool valid=true;
        for(int i=0;i<tree.Branches.Count;i++)
        {
            TreeBranch b=tree.Branches[i];
            if(b.ParentIndex>=i||b.ParentIndex< -1||b.StartRadius<=0||b.EndRadius<=0||b.EndRadius>b.StartRadius+.000001||(b.End-b.Start).Length<=0)valid=false;
            if(b.ParentIndex>=0)
            {
                TreeBranch parent=tree.Branches[b.ParentIndex];TreePoint delta=parent.End-parent.Start;
                double t=Math.Max(0,Math.Min(1,TreePoint.Dot(b.Start-parent.Start,delta)/Math.Max(delta.LengthSquared,1e-20)));
                if((b.Start-TreePoint.Lerp(parent.Start,parent.End,t)).Length>Math.Max(parent.StartRadius,parent.EndRadius)+.15)valid=false;
                double parentRadius=parent.StartRadius+(parent.EndRadius-parent.StartRadius)*t;
                if(b.Kind!=TreeBranchKind.Root&&b.StartRadius>parentRadius+.00001)valid=false;
            }
        }
        Expect(valid,tree.Species+" ordered skeleton has valid attachments, monotone taper and no reverse-thickening child joints");
        bool area=true;
        for(int parent=0;parent<tree.Branches.Count;parent++)
        {
            TreeBranch bearing=tree.Branches[parent];double childArea=0;int children=0;
            for(int i=parent+1;i<tree.Branches.Count;i++)
            {
                TreeBranch child=tree.Branches[i];
                if(child.ParentIndex==parent&&child.Level==bearing.Level+1&&bearing.Level>=1&&(child.Start-bearing.End).LengthSquared<1e-12)
                {childArea+=child.StartRadius*child.StartRadius;children++;}
            }
            if(children>1&&childArea>bearing.EndRadius*bearing.EndRadius*1.001+1e-12)area=false;
        }
        Expect(area,tree.Species+" recursive fork cross sections fit within parent tip area");
    }
    static void Main(string[] args)
    {
        var summaries=new List<Summary>();var truncated=new List<Summary>();var redwoodSections=new List<Section>();ulong birchHash=0;double birchHeight=0;
        foreach(TreeSpecies species in Enum.GetValues(typeof(TreeSpecies)))
        {
            TreeParameters p=TreePresets.Get(species);var w=World(p);var clock=Stopwatch.StartNew();TreeResult r=Generate(w,p);clock.Stop();
            Summary a=Inspect(w,r);a.Milliseconds=clock.Elapsed.TotalMilliseconds;summaries.Add(a);
            Console.WriteLine(species+" generated "+a.Height.ToString("F1")+"m; "+a.Candidates+" candidates; "+a.Clusters+" clusters; "+a.Truncation);
            Expect(!r.Truncated,species+" mature preset completes within default budgets and domain");
            Expect(a.Wood>100&&a.Leaves>100&&a.Segments>30,species+" emits substantial wood, foliage and scaffold");
            Expect(a.Components==a.GroundedComponents&&a.Components>0,species+" every six-connected wood component is ground anchored");
            Expect(a.AllFoliageSupported&&a.EveryFruitTouchesLeaf,species+" every foliage component connects to wood");
            Expect(a.LeafColors>=3&&a.LeafColors<=48,species+" restrained persistent multi-scale leaf palette");
            Expect(a.Wood==r.WoodCells&&a.Leaves==r.LeafCells&&a.Fruit==r.FruitCells,species+" reported cell stats match actual authoritative world");
            Expect(a.Candidates<=new TreeGenerationBudget().MaxVoxelCandidates,species+" candidate cap respected");
            Skeleton(r);
            if(species==TreeSpecies.Birch)
            {
                Expect(a.WhiteBark>30&&a.DarkBark>30,"birch includes white mature bark and dark lenticels/twigs");birchHash=a.Hash;birchHeight=a.Height;
                var copy=World(p);TreeResult identical=Generate(copy,p);Expect(Inspect(copy,identical).Hash==birchHash,"identical genome/seed reproduces exact material and packed appearance");
                var varied=World(p);TreeResult different=Generate(varied,p,714104);Expect(Inspect(varied,different).Hash!=birchHash,"individual seed changes actual scaffold and material appearance");
                var younger=World(p);TreeResult young=Generate(younger,p.With(age:.45f));Expect(Inspect(younger,young).Height<birchHeight*.7,"age changes physical growth scale independently of species preset");
            }
            if(species==TreeSpecies.Redwood)
            {
                Expect(a.Height>=60&&a.Height<=100&&p.Height==75&&p.TrunkRadius>=2,"redwood represents a mature 60-100m coast redwood rather than a scaled sapling");
                foreach(double h in new[]{.5,1.3,3.0,6.0,15.0,25.0})redwoodSections.Add(CrossSection(w,h));
                Expect(redwoodSections[1].EquivalentDiameterM>=3&&redwoodSections[1].EquivalentDiameterM<=5.8,"redwood breast-height wood section has plausible mature diameter including buttresses");
                Expect(redwoodSections[0].EquivalentDiameterM>redwoodSections[2].EquivalentDiameterM&&redwoodSections[2].EquivalentDiameterM>redwoodSections[4].EquivalentDiameterM,"basal flare narrows into the tapered mature bole in physical meters");
                Expect(redwoodSections[3].MaxRadiusM-redwoodSections[3].MinRadiusM>=.15,"redwood outer boundary retains actual 10cm-readable longitudinal bark relief above the root collar");
                bool rootedWedges=true;int outerRootColumns=0;
                double center=w.SizeX*w.VoxelSize*.5,rootStart=p.TrunkRadius*1.5;
                for(int z=0;z<w.SizeZ;z++)for(int x=0;x<w.SizeX;x++)
                {
                    double dx=(x+.5)*w.VoxelSize-center,dz=(z+.5)*w.VoxelSize-center;
                    if(dx*dx+dz*dz<rootStart*rootStart)continue;
                    int top=0;for(int y=3;y<=30;y++)if(w.Get(x,y,z)==MaterialId.Wood)top=y;
                    if(top==0)continue;outerRootColumns++;
                    for(int y=3;y<top;y++)if(w.Get(x,y,z)==MaterialId.Air)rootedWedges=false;
                }
                Expect(rootedWedges&&outerRootColumns>100,"outer redwood buttresses join local ground as solid wedges rather than suspended radial legs");
                Expect(p.CrownStart>=.4&&p.BranchAngle>=90&&p.BranchCount>=48,"mature redwood has high open bole, sweeping branches and a layered substantial crown");
            }
            if(species==TreeSpecies.Oak)
            {
                var fruitWorld=World(p);TreeResult fruitTree=Generate(fruitWorld,p.With(fruitProbability:1));Summary fa=Inspect(fruitWorld,fruitTree);
                Expect(fa.Fruit>10&&fa.EveryFruitTouchesLeaf&&fa.AllFoliageSupported,"optional oak acorns are attached to supported foliage");
                bool brown=true;foreach(var pair in fruitWorld.Chunks)for(int i=0;i<Chunk.Volume;i++)if(pair.Value.Cells[i]==MaterialId.Fruit)
                {Int3 c=Chunk.Decode(i);uint tint=pair.Value.GetTint(c.x,c.y,c.z);int red=(int)((tint>>16)&255),green=(int)((tint>>8)&255),blue=(int)(tint&255);if(!(red>=green&&green>blue))brown=false;}
                Expect(brown,"oak fruit palette is brown acorn-like rather than red orchard fruit");
                using(var stream=new MemoryStream())
                {Persistence.Save(stream,fruitWorld);stream.Position=0;VoxelWorld loaded=Persistence.Load(stream);Expect(Inspect(loaded,fruitTree).Hash==fa.Hash,"colored branches leaves and acorns survive snapshot round trip");}
            }
        }
        TreeParameters preset=TreePresets.Get(TreeSpecies.Birch),small=preset.Scaled(.5f);
        foreach(int cap in new[]{100,500,2000,10000})
        {
            var w=World(small);TreeResult r=Generate(w,small,714103,new TreeGenerationBudget(maxVoxelCandidates:cap));Summary a=Inspect(w,r);truncated.Add(a);
            Expect((r.Truncation&TreeTruncation.VoxelBudget)!=0&&r.VoxelCandidates<=cap,"voxel budget "+cap+" explicitly truncates without exceeding cap");
            Expect(a.Components==a.GroundedComponents&&a.AllFoliageSupported,"budget "+cap+" cannot leave disconnected partial wood shells or floating foliage");
        }
        {
            var w=World(small);TreeResult r=Generate(w,small,714103,new TreeGenerationBudget(maxBranches:12));Summary a=Inspect(w,r);
            Expect((r.Truncation&TreeTruncation.BranchBudget)!=0&&r.Branches.Count<=12&&a.Components==a.GroundedComponents,"branch budget truncates explicitly and retains rooted matter");
        }
        {
            var w=World(small);TreeResult r=Generate(w,small,714103,new TreeGenerationBudget(maxFoliageClusters:2));
            Expect((r.Truncation&TreeTruncation.FoliageBudget)!=0&&r.FoliageClusters<=2,"foliage cluster budget is explicit and bounded");
        }
        {
            var w=World(small);TreeResult r=TreeGenerator.Generate(w,small,.1f,.3f,.1f,714103);
            Expect((r.Truncation&TreeTruncation.WorldBoundary)!=0,"clipped domain reports world-boundary truncation");
        }
        {
            var w=World(small);TreeResult r=Generate(w,small.With(foliageDensity:0,fruitProbability:1));
            Expect(r.LeafCells==0&&r.FruitCells==0,"zero foliage creates no unsupported fruit");
        }
        Expect(Math.Abs(preset.Height-10)<.001&&Math.Abs(preset.With(height:7).Height-7)<.001,"With preserves immutable preset while changing requested controls");
        Expect(preset.With(foliageStyle:TreeFoliageStyle.NeedleFan,barkStyle:TreeBarkStyle.Pine).FoliageStyle==TreeFoliageStyle.NeedleFan,"artists can combine advanced habits beyond four species presets");
        {
            TreeParameters p=TreePresets.Get(TreeSpecies.Redwood).With(height:40,trunkRadius:1.2f,crownRadius:2,rootFlare:0,foliageDensity:0,branchCount:1,branchLevels:1,crownStart:.8f);
            var rough=World(p);Generate(rough,p);var smooth=World(p);Generate(smooth,p.With(barkRelief:0));
            int changed=0;foreach(var pair in rough.Chunks)for(int i=0;i<Chunk.Volume;i++)
            {
                Int3 local=Chunk.Decode(i),c=new Int3(pair.Key.x*16+local.x,pair.Key.y*16+local.y,pair.Key.z*16+local.z);
                if(rough.Get(c)!=smooth.Get(c))changed++;
            }
            Expect(changed>150,"bark relief changes authoritative material silhouette rather than only painting color");
            Expect(Math.Abs(p.Scaled(.5f).BarkRelief-p.BarkRelief*.5f)<.00001&&p.With(barkRidgeCount:7).BarkRidgeCount==7,"bark relief scales physically and ridge count remains independently editable");
            var lowGround=World(p.With(height:10));
            TreeResult lowRoots=TreeGenerator.Generate(lowGround,p.With(height:10),lowGround.SizeX*.05f,1,lowGround.SizeZ*.05f,714103);
            Summary low=Inspect(lowGround,lowRoots);
            Expect(!lowRoots.Truncated&&low.Components==low.GroundedComponents&&low.Components>0,"redwood buttresses retain lower surface-seeking root ends when ground lies below the collar");
        }
        Reject(()=>preset.With(height:float.NaN),"rejects nonfinite physical dimensions");
        Reject(()=>preset.With(branchLevels:12),"rejects runaway recursive branch depth");
        Reject(()=>preset.With(age:0),"rejects invalid growth age");
        Reject(()=>preset.With(barkColor:0),"rejects ambiguous transparent tree palette");
        Reject(()=>preset.With(barkRelief:-1),"rejects negative radial bark relief");
        Reject(()=>preset.With(barkRidgeCount:1000),"rejects unbounded radial ridge count");
        Reject(()=>new TreeGenerationBudget(maxVoxelCandidates:0),"rejects zero voxel budget");
        var report=new{status="PASS",checks,scope="Actual engine-independent tree generator; not Unity or botanical ground-truth",generator_version=Generation.Version,voxel_m=.1,seed=714103,species=summaries,redwood_cross_sections=redwoodSections,budget_cutoffs=truncated};
        string json=JsonSerializer.Serialize(report,new JsonSerializerOptions{WriteIndented=true});Console.WriteLine(json);
        if(args.Length>0)File.WriteAllText(args[0],json+Environment.NewLine);
    }
}
