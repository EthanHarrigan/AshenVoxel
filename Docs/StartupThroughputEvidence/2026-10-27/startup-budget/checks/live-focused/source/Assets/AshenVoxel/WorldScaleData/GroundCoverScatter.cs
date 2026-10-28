using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using AshenVoxel.Core;
using AshenVoxel.Core.Details;
using AshenVoxel.Core.Rocks;

namespace AshenVoxel.WorldScale
{
    /// <summary>Static habitat-directed placement settings. Density changes thinning,
    /// not candidate positions. Neither vegetation override nor LOD chooses habitats.</summary>
    public sealed class GroundCoverProfile
    {
        public const int Version=2;
        public readonly int Seed,MaxCandidates,MaxPlacements;
        public readonly double Density;
        public readonly bool EnablePlants,EnableRocks;
        public string Identity {get;}
        public GroundCoverProfile(int seed=714103,double density=1,bool enablePlants=true,bool enableRocks=true,int maxCandidates=4096,int maxPlacements=256)
        {
            Numeric.Finite(density,nameof(density));
            if(density<0||density>2||maxCandidates<1||maxCandidates>100000||maxPlacements<1||maxPlacements>4096)throw new ArgumentOutOfRangeException("ground cover profile");
            Seed=seed;Density=density;EnablePlants=enablePlants;EnableRocks=enableRocks;MaxCandidates=maxCandidates;MaxPlacements=maxPlacements;
            Identity="groundcover-v"+Version+"/plant-v"+PlantGenerator.Version+"/rock-v"+RockGenerator.Version+"/rock-appearance-v"+RockGenerator.AppearanceVersion+"/catalog-v"+MaterialCatalog.Version+"/"+seed+"/"+
                density.ToString("R",CultureInfo.InvariantCulture)+"/"+enablePlants+"/"+enableRocks+"/"+maxCandidates+"/"+maxPlacements;
        }
    }
    public readonly struct GroundCoverPlacement
    {
        public readonly WorldCell Anchor;
        public readonly int Seed,Layer;
        public readonly long GridX,GridZ;
        public readonly byte SourceBiome;
        public readonly PlantProfile Plant;
        public readonly RockProfile Rock;
        public bool IsRock=>Rock!=null;
        internal GroundCoverPlacement(WorldCell anchor,int seed,int layer,long gridX,long gridZ,byte sourceBiome,PlantProfile plant,RockProfile rock)
        {Anchor=anchor;Seed=seed;Layer=layer;GridX=gridX;GridZ=gridZ;SourceBiome=sourceBiome;Plant=plant;Rock=rock;}
    }
    public sealed class GroundCoverPlan
    {
        public IReadOnlyList<GroundCoverPlacement> Placements {get;}
        public int ScannedCandidates {get;}
        public bool Truncated {get;}
        public string SourceIdentity {get;}
        internal GroundCoverPlan(List<GroundCoverPlacement> placements,int candidates,bool truncated,string identity)
        {Placements=new ReadOnlyCollection<GroundCoverPlacement>(placements);ScannedCandidates=candidates;Truncated=truncated;SourceIdentity=identity;}
    }
    public sealed class GroundCoverBuildBudget
    {
        public readonly int MaxPlacements,MaxWrittenCells;
        public readonly long MaxGeneratorCandidates;
        public GroundCoverBuildBudget(int maxPlacements=256,long maxGeneratorCandidates=1000000,int maxWrittenCells=60000)
        {
            if(maxPlacements<1||maxPlacements>4096||maxGeneratorCandidates<1||maxGeneratorCandidates>16000000||maxWrittenCells<1||maxWrittenCells>1000000)throw new ArgumentOutOfRangeException("ground cover build budget");
            MaxPlacements=maxPlacements;MaxGeneratorCandidates=maxGeneratorCandidates;MaxWrittenCells=maxWrittenCells;
        }
    }
    public sealed class GroundCoverBuildResult
    {
        public int ProcessedPlacements,Plants,Rocks,WrittenCells,BlockedCells,ClippedPlacements,ClippedRockCells,IncompletePlacements;
        public long GeneratorCandidates;
        public bool Truncated;
    }
    /// <summary>Global keyed jittered candidates with fixed-distance priority exclusion,
    /// followed by habitat thinning. This is not a blue-noise spectral claim or an ecosystem solver.
    /// Neighbor acceptance uses only hashes: terrain queries stay within each requested region
    /// plus the existing SurfaceProfile.SampleRadiusMax halo.</summary>
    public sealed class GroundCoverScatter
    {
        public readonly SurfaceMaterialSampler Surface;
        public readonly GroundCoverProfile Profile;
        public string SourceIdentity=>Surface.SourceIdentity+"/"+Profile.Identity;
        public GroundCoverScatter(SurfaceMaterialSampler surface,GroundCoverProfile profile=null)
        {Surface=surface??throw new ArgumentNullException(nameof(surface));Profile=profile??new GroundCoverProfile(surface.Profile.Seed);}

        public GroundCoverPlan Plan(double minimumX,double minimumZ,double maximumX,double maximumZ)
        {
            Bounds(minimumX,minimumZ,maximumX,maximumZ);
            var placements=new List<GroundCoverPlacement>();int scanned=0;bool limited=false;
            for(int layer=0;layer<3;layer++)
            {
                if(layer==0?!Profile.EnableRocks:!Profile.EnablePlants)continue;
                double spacing=Spacing(layer);
                long x0=(long)Math.Floor(minimumX/spacing)-1,x1=(long)Math.Floor(maximumX/spacing)+1;
                long z0=(long)Math.Floor(minimumZ/spacing)-1,z1=(long)Math.Floor(maximumZ/spacing)+1;
                for(long z=z0;z<=z1;z++)for(long x=x0;x<=x1;x++)
                {
                    // Count all attempted positions, including border and rejected hashes.
                    if(scanned>=Profile.MaxCandidates||placements.Count>=Profile.MaxPlacements){limited=true;goto Done;}
                    scanned++;
                    Position(x,z,layer,out double px,out double pz);
                    if(px<minimumX||px>=maximumX||pz<minimumZ||pz>=maximumZ||!Retain(x,z,layer,px,pz))continue;
                    ulong hash=Hash(x,z,layer,Profile.Seed);
                    SurfaceColumn column=Surface.EvaluateColumn(px,pz);
                    if(!column.IsLand||column.HasWater||column.SnowDepthMeters>=.099||column.SurfaceMaterial==MaterialId.Ice||column.SlopeDegrees>43)continue;
                    double patch=.30+.70*Noise01(px/7,pz/7,unchecked(Profile.Seed+181));
                    double probability=Probability(column,layer)*Profile.Density*patch;
                    if(Unit(Mix(hash+31))>=Math.Min(1,probability))continue;
                    int seed=unchecked((int)Mix(hash+53));
                    // Cell-center occupancy in the surface sampler is <= height. The next
                    // center, rather than floor(height/.1)+1, is the first truly empty cell.
                    long cy=(long)Math.Floor(column.SurfaceHeightMeters/WorldCell.SizeMeters-.5+1e-8)+1;
                    var anchor=new WorldCell((long)Math.Floor(px*10),cy,(long)Math.Floor(pz*10));
                    if(layer==0)
                    {
                        double size=.42+Unit(Mix(hash+61))*1.55;
                        RockProfile rock=GroundRock(column.BedrockMaterial,seed,size,Unit(Mix(hash+67)),Unit(Mix(hash+71)));
                        placements.Add(new GroundCoverPlacement(anchor,seed,layer,x,z,column.SourceBiome,null,rock));
                    }
                    else
                    {
                        PlantKind kind=Choose(column,layer,Unit(Mix(hash+83)));
                        if(kind==PlantKind.Fern&&(column.Wetness<.38||column.PrecipitationMm<400))continue;
                        if(kind==PlantKind.Mushroom&&(column.Wetness<.50||column.PrecipitationMm<700||column.TemperatureC< -4||column.TemperatureC>35))continue;
                        if(kind==PlantKind.Reed&&column.Wetness<.60)continue;
                        if(kind==PlantKind.Cactus&&column.TemperatureC<5)continue;
                        PlantProfile plant=Adapt(kind,column,hash);
                        placements.Add(new GroundCoverPlacement(anchor,seed,layer,x,z,column.SourceBiome,plant,null));
                    }
                }
            }
            Done:
            return new GroundCoverPlan(placements,scanned,limited,SourceIdentity);
        }
        static void Bounds(double minX,double minZ,double maxX,double maxZ)
        {
            Numeric.Finite(minX,nameof(minX));Numeric.Finite(minZ,nameof(minZ));Numeric.Finite(maxX,nameof(maxX));Numeric.Finite(maxZ,nameof(maxZ));
            if(minX>=maxX||minZ>=maxZ||Math.Abs(minX)>1e12||Math.Abs(maxX)>1e12||Math.Abs(minZ)>1e12||Math.Abs(maxZ)>1e12)throw new ArgumentOutOfRangeException("ground cover bounds");
        }
        static double Spacing(int layer)=>layer==0?3.2:layer==1?2.4:.9;
        static RockProfile GroundRock(MaterialId material,int seed,double size,double u,double v)
        {
            RockProfile p=RockPresets.For(material,seed);
            double width=size,height=size*(.48+.24*u),depth=size*(.68+.3*v);
            // Shape survives placement: a tall shard must not be flattened into
            // the old uniform short-boulder proportions by the habitat layer.
            switch(p.ResolvedShape)
            {
                case RockShape.Slab:width=size*1.05;height=Math.Max(.1,size*(.16+.12*u));depth=size*(.45+.25*v);break;
                case RockShape.Wedge:height=size*(.45+.3*u);depth=size*(.6+.28*v);break;
                case RockShape.Spire:width=size*(.55+.15*u);height=size*(1+.45*v);depth=size*(.45+.23*v);break;
                case RockShape.SplitOutcrop:width=size*1.05;height=size*(.68+.32*u);break;
                case RockShape.Cluster:width=size*1.12;height=size*(.42+.36*u);break;
            }
            return p.With(widthMeters:width,heightMeters:height,depthMeters:depth,buriedDepthMeters:.2,maxCandidates:100000,maxWrittenCells:12000);
        }
        void Position(long x,long z,int layer,out double px,out double pz)
        {
            ulong h=Hash(x,z,layer,Profile.Seed);double spacing=Spacing(layer);
            // Quantize once globally. Cells and region partitioning see the same center.
            px=(Math.Floor((x+.1+Unit(Mix(h+7))*.8)*spacing*10)+.5)*.1;
            pz=(Math.Floor((z+.1+Unit(Mix(h+13))*.8)*spacing*10)+.5)*.1;
        }
        bool Retain(long x,long z,int layer,double px,double pz)
        {
            ulong priority=Hash(x,z,layer,Profile.Seed);double distance=layer==0?1.9:layer==1?1.45:.64;
            for(int dz=-1;dz<=1;dz++)for(int dx=-1;dx<=1;dx++)
            {
                if(dx==0&&dz==0)continue;
                ulong other=Hash(x+dx,z+dz,layer,Profile.Seed);
                if(other>priority||other==priority&&(dz>0||dz==0&&dx>0))continue;
                Position(x+dx,z+dz,layer,out double nx,out double nz);
                if((px-nx)*(px-nx)+(pz-nz)*(pz-nz)<distance*distance)return false;
            }
            return true;
        }
        static double Probability(SurfaceColumn c,int layer)
        {
            byte b=c.BiomeUsed;
            if(layer==0)return c.SlopeDegrees>6?.26:c.BaseSurfaceMaterial==MaterialId.Gravel?.30:.13;
            if(b==0||b==11||c.TemperatureC< -8||c.SlopeDegrees>31)return 0;
            bool bare=SurfaceProfile.IsRock(c.BaseSurfaceMaterial);
            if(bare&&b!=2&&b!=10)return 0;
            double p;
            switch(b)
            {
                case 1:p=.11;break;case 2:p=.15;break;case 3:p=.66;break;case 4:p=.90;break;
                case 5:p=.72;break;case 6:p=.93;break;case 7:p=1;break;case 8:p=1;break;
                case 9:p=.65;break;case 10:p=.40;break;case 12:p=.90;break;case 13:p=.065;break;
                default:p=.5;break;
            }
            if(layer==1)p*=b==1||b==13?.45:b==9?.85:.55;
            if(bare)p*=.2;
            return p;
        }
        static PlantKind Choose(SurfaceColumn c,int layer,double u)
        {
            byte b=c.BiomeUsed;
            if(layer==1)
            {
                if(b==1||b==13)return u<.55?PlantKind.Cactus:PlantKind.Succulent;
                if(b==10||b==2)return PlantKind.Cushion;
                if(b==12)return c.Wetness>.60?PlantKind.Reed:PlantKind.Shrub;
                return PlantKind.Shrub;
            }
            switch(b)
            {
                case 1:case 13:return u<.72?PlantKind.Succulent:PlantKind.GrassTuft;
                case 2:return u<.55?PlantKind.Cushion:PlantKind.GrassTuft;
                case 3:return u<.82?PlantKind.GrassTuft:u<.94?PlantKind.BroadleafHerb:PlantKind.Flower;
                case 4:return u<.58?PlantKind.GrassTuft:u<.77?PlantKind.Flower:PlantKind.BroadleafHerb;
                case 5:return u<.35?PlantKind.GrassTuft:u<.65?PlantKind.Fern:u<.9?PlantKind.BroadleafHerb:PlantKind.Flower;
                case 7:return u<.47?PlantKind.Fern:u<.8?PlantKind.BroadleafHerb:u<.94?PlantKind.Mushroom:PlantKind.Flower;
                case 8:return u<.43?PlantKind.Fern:u<.78?PlantKind.Mushroom:PlantKind.BroadleafHerb;
                case 9:return u<.35?PlantKind.GrassTuft:u<.65?PlantKind.Mushroom:u<.9?PlantKind.Cushion:PlantKind.BroadleafHerb;
                case 10:return u<.78?PlantKind.Cushion:PlantKind.GrassTuft;
                case 12:return c.Wetness>.60&&u<.65?PlantKind.Reed:u<.82?PlantKind.BroadleafHerb:PlantKind.Mushroom;
                default:return u<.36?PlantKind.Fern:u<.60?PlantKind.BroadleafHerb:u<.79?PlantKind.Mushroom:u<.94?PlantKind.GrassTuft:PlantKind.Flower;
            }
        }
        static PlantProfile Adapt(PlantKind kind,SurfaceColumn c,ulong hash)
        {
            PlantProfile p=PlantPresets.For(kind);byte b=c.BiomeUsed;
            double scale=.7+Unit(Mix(hash+101))*.5;
            if(b==7||b==8)scale*=1.12;
            if(b==2||b==9||b==10)scale*=.65;
            uint foliage=b==1||b==2||b==3||b==13?0xff8b8651:b==7?0xff416c3b:b==8?0xff42634d:b==9?0xff53664b:b==10?0xff7b8052:0xff526c40;
            if(kind==PlantKind.Mushroom)foliage=0xffd3c7a7;
            if(kind==PlantKind.Cactus||kind==PlantKind.Succulent)foliage=0xff778b67;
            uint accent=kind==PlantKind.Mushroom?(Unit(Mix(hash+113))<.5?0xffa16342:0xffb39970):p.AccentColor;
            double fruit=kind==PlantKind.Shrub&&(b==6||b==8||b==9)? .17:0;
            // Sparse dwarf mushrooms are genuinely one occupied10cm cell, not scaled meshes.
            if(kind==PlantKind.Mushroom&&Unit(Mix(hash+127))<.22)return p.With(heightMeters:.1,radiusMeters:.05,foliageColor:foliage,accentColor:accent,paletteSteps:4);
            return p.With(heightMeters:Math.Max(.1,p.HeightMeters*scale),radiusMeters:Math.Max(.05,p.RadiusMeters*scale),
                foliageColor:foliage,accentColor:accent,fruitProbability:fruit,variation:.2,paletteSteps:4);
        }
        /// <summary>Finite-region materialization, canonical rocks/shrubs/herbs order.
        /// This is not persistent chunk streaming: overlapping separately built regions
        /// still need a future stable cross-region cell-composition contract.</summary>
        public static GroundCoverBuildResult Materialize(VoxelWorld world,WorldCell globalOrigin,GroundCoverPlan plan,GroundCoverBuildBudget budget=null)
        {
            if(world==null||plan==null)throw new ArgumentNullException(world==null?nameof(world):nameof(plan));
            if(Math.Abs(world.VoxelSize-.1f)>1e-7)throw new ArgumentException("Ground cover requires10cm matter.");
            if(Math.Abs((double)globalOrigin.X)>1e13||Math.Abs((double)globalOrigin.Y)>1e13||Math.Abs((double)globalOrigin.Z)>1e13)throw new ArgumentOutOfRangeException(nameof(globalOrigin));
            budget=budget??new GroundCoverBuildBudget();var r=new GroundCoverBuildResult{Truncated=plan.Truncated};
            foreach(GroundCoverPlacement p in plan.Placements)
            {
                if(r.ProcessedPlacements>=budget.MaxPlacements||r.GeneratorCandidates>=budget.MaxGeneratorCandidates||r.WrittenCells>=budget.MaxWrittenCells){r.Truncated=true;break;}
                r.ProcessedPlacements++;
                long x=p.Anchor.X-globalOrigin.X,y=p.Anchor.Y-globalOrigin.Y,z=p.Anchor.Z-globalOrigin.Z;
                if(x<0||x>=world.SizeX||y<1||y>=world.SizeY||z<0||z>=world.SizeZ){r.ClippedPlacements++;continue;}
                int candidates=(int)Math.Min(4000000,budget.MaxGeneratorCandidates-r.GeneratorCandidates),cells=budget.MaxWrittenCells-r.WrittenCells;
                var anchor=new Int3((int)x,(int)y,(int)z);
                if(p.IsRock)
                {
                    RockResult result=RockGenerator.Generate(world,anchor,p.Rock.With(maxCandidates:candidates,maxWrittenCells:Math.Min(1000000,cells)));
                    r.GeneratorCandidates+=result.CandidateCount;r.WrittenCells+=result.WrittenCells;r.BlockedCells+=result.BlockedCells;
                    if(result.WrittenCells>0)r.Rocks++;
                    if(result.Truncated||result.ClippedCells>0){r.IncompletePlacements++;r.Truncated=true;}
                    if(result.ClippedCells>0){r.ClippedPlacements++;r.ClippedRockCells+=result.ClippedCells;}
                }
                else
                {
                    PlantResult result=PlantGenerator.Generate(world,p.Plant,anchor,p.Seed,new PlantGenerationBudget(candidates,Math.Min(200000,cells)));
                    r.GeneratorCandidates+=result.Candidates;r.WrittenCells+=result.WrittenCells;r.BlockedCells+=result.BlockedCells;
                    if(result.WrittenCells>0)r.Plants++;
                    if(result.Truncated)r.IncompletePlacements++;
                    if((result.Truncation&(PlantTruncation.CandidateBudget|PlantTruncation.CellBudget|PlantTruncation.WorldBoundary))!=0)r.Truncated=true;
                    if((result.Truncation&PlantTruncation.WorldBoundary)!=0)r.ClippedPlacements++;
                }
            }
            return r;
        }
        static ulong Hash(long x,long z,int layer,int seed){unchecked{return Mix((ulong)x*0x9e3779b185ebca87UL^(ulong)z*0xc2b2ae3d27d4eb4fUL^(uint)seed^(ulong)(layer+1)*0x165667b19e3779f9UL);}}
        static ulong Mix(ulong n){unchecked{n^=n>>30;n*=0xbf58476d1ce4e5b9UL;n^=n>>27;n*=0x94d049bb133111ebUL;return n^(n>>31);}}
        static double Unit(ulong n)=>(n>>11)*(1.0/9007199254740992.0);
        static double Noise01(double x,double z,int seed)
        {
            long ix=(long)Math.Floor(x),iz=(long)Math.Floor(z);double u=x-ix,v=z-iz;u=u*u*(3-2*u);v=v*v*(3-2*v);
            double a=Unit(Hash(ix,iz,5,seed)),b=Unit(Hash(ix+1,iz,5,seed)),c=Unit(Hash(ix,iz+1,5,seed)),d=Unit(Hash(ix+1,iz+1,5,seed));
            return (a+(b-a)*u)*(1-v)+(c+(d-c)*u)*v;
        }
    }
}
