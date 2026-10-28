using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace AshenVoxel.Core.Trees
{
    public enum TreeSpecies { Birch, Oak, Redwood, Pine }
    public enum TreeFoliageStyle { Broadleaf, NeedleFan, ScaleSpray }
    public enum TreeBarkStyle { Birch, Oak, Redwood, Pine }
    public enum TreeBranchKind { Trunk, Root, Primary, Secondary, Twig }
    [Flags] public enum TreeTruncation { None=0, BranchBudget=1, VoxelBudget=2, FoliageBudget=4, WorldBoundary=8 }

    public readonly struct TreePoint
    {
        public readonly double x,y,z;
        public TreePoint(double x,double y,double z){this.x=x;this.y=y;this.z=z;}
        public double LengthSquared=>x*x+y*y+z*z;
        public double Length=>Math.Sqrt(LengthSquared);
        public TreePoint Normalized=>LengthSquared<1e-20?new TreePoint(0,1,0):this*(1/Length);
        public static TreePoint operator +(TreePoint a,TreePoint b)=>new TreePoint(a.x+b.x,a.y+b.y,a.z+b.z);
        public static TreePoint operator -(TreePoint a,TreePoint b)=>new TreePoint(a.x-b.x,a.y-b.y,a.z-b.z);
        public static TreePoint operator *(TreePoint a,double b)=>new TreePoint(a.x*b,a.y*b,a.z*b);
        public static double Dot(TreePoint a,TreePoint b)=>a.x*b.x+a.y*b.y+a.z*b.z;
        public static TreePoint Cross(TreePoint a,TreePoint b)=>new TreePoint(a.y*b.z-a.z*b.y,a.z*b.x-a.x*b.z,a.x*b.y-a.y*b.x);
        public static TreePoint Lerp(TreePoint a,TreePoint b,double t)=>a+(b-a)*t;
        public override string ToString()=>$"({x:F3}, {y:F3}, {z:F3})";
    }
    public readonly struct TreeBranch
    {
        public readonly int ParentIndex,Level;
        public readonly TreePoint Start,End;
        public readonly double StartRadius,EndRadius,PathDistance;
        public readonly TreeBranchKind Kind;
        public TreeBranch(int parentIndex,int level,TreePoint start,TreePoint end,double startRadius,double endRadius,double pathDistance,TreeBranchKind kind)
        {ParentIndex=parentIndex;Level=level;Start=start;End=end;StartRadius=startRadius;EndRadius=endRadius;PathDistance=pathDistance;Kind=kind;}
    }
    public sealed class TreeGenerationBudget
    {
        public readonly int MaxBranches,MaxFoliageClusters;
        public readonly long MaxVoxelCandidates;
        public TreeGenerationBudget(int maxBranches=4096,long maxVoxelCandidates=16000000,int maxFoliageClusters=4096)
        {
            if(maxBranches<1||maxBranches>32768)throw new ArgumentOutOfRangeException(nameof(maxBranches));
            if(maxVoxelCandidates<1||maxVoxelCandidates>200000000)throw new ArgumentOutOfRangeException(nameof(maxVoxelCandidates));
            if(maxFoliageClusters<0||maxFoliageClusters>16384)throw new ArgumentOutOfRangeException(nameof(maxFoliageClusters));
            MaxBranches=maxBranches;MaxVoxelCandidates=maxVoxelCandidates;MaxFoliageClusters=maxFoliageClusters;
        }
    }
    public sealed class TreeResult
    {
        public TreeSpecies Species {get;internal set;}
        public int Seed {get;internal set;}
        public IReadOnlyList<TreeBranch> Branches {get;internal set;}
        public TreePoint BoundsMin {get;internal set;}
        /// <summary>Exclusive upper bound of written cells, in meters.</summary>
        public TreePoint BoundsMax {get;internal set;}
        public Int3 MinCell {get;internal set;}
        /// <summary>Inclusive upper cell coordinate; zero when no cells were written.</summary>
        public Int3 MaxCell {get;internal set;}
        public int WoodCells {get;internal set;}
        public int LeafCells {get;internal set;}
        public int FruitCells {get;internal set;}
        public int FoliageClusters {get;internal set;}
        public long VoxelCandidates {get;internal set;}
        public TreeTruncation Truncation {get;internal set;}
        public bool Truncated=>Truncation!=TreeTruncation.None;
        public string TruncationReason=>Truncation.ToString();
        public float ActualHeight=> (float)(BoundsMax.y-BoundsMin.y);
        internal static IReadOnlyList<TreeBranch> Freeze(List<TreeBranch> branches)=>new ReadOnlyCollection<TreeBranch>(branches);
    }

    /// <summary>Immutable growth genome. Lengths are meters; angles are degrees; Height is nominal at Age=1.
    /// Species is identity; habit controls drive the shared grammar rather than separate generators.</summary>
    public sealed class TreeParameters
    {
        public readonly TreeSpecies Species;
        public readonly float Height,TrunkRadius,CrownRadius,BranchAngle,ApicalDominance,Tropism,FoliageDensity,LeafClusterRadius,FruitProbability,Variation,Age;
        public readonly int BranchLevels,BranchCount;
        public readonly uint BarkColor,LeafColor,FruitColor;
        public readonly TreeFoliageStyle FoliageStyle;
        public readonly TreeBarkStyle BarkStyle;
        public readonly int WhorlSize;
        public readonly float CrownStart,ConeTaper,LeaderHeightFraction,BranchDroop,Gnarl,RootFlare,ForkAngle;
        /// <summary>Actual radial bark displacement in meters, separate from broad basal wood fluting.</summary>
        public readonly float BarkRelief;
        public readonly int BarkRidgeCount;
        public TreeParameters(TreeSpecies species,float height,float trunkRadius,float crownRadius,int branchLevels=3,int branchCount=12,
            float branchAngle=65,float apicalDominance=.45f,float tropism=.12f,float foliageDensity=.8f,float leafClusterRadius=.4f,
            float fruitProbability=0,float variation=.5f,float age=1,uint barkColor=0xff554338,uint leafColor=0xff5c753b,uint fruitColor=0xffa73e28,
            TreeFoliageStyle foliageStyle=TreeFoliageStyle.Broadleaf,TreeBarkStyle barkStyle=TreeBarkStyle.Oak,int whorlSize=1,
            float crownStart=.33f,float coneTaper=0,float leaderHeightFraction=.82f,float branchDroop=.1f,float gnarl=.3f,float rootFlare=.25f,float forkAngle=35,
            float barkRelief=0,int barkRidgeCount=12)
        {
            if(!Enum.IsDefined(typeof(TreeSpecies),species)||!Enum.IsDefined(typeof(TreeFoliageStyle),foliageStyle)||!Enum.IsDefined(typeof(TreeBarkStyle),barkStyle))throw new ArgumentOutOfRangeException(nameof(species));
            Range(height,.4f,120,nameof(height));Range(trunkRadius,.025f,6,nameof(trunkRadius));Range(crownRadius,.15f,35,nameof(crownRadius));
            Range(branchAngle,5,115,nameof(branchAngle));Range(apicalDominance,0,1,nameof(apicalDominance));Range(tropism,-1,1,nameof(tropism));
            Range(foliageDensity,0,1,nameof(foliageDensity));Range(leafClusterRadius,.06f,3,nameof(leafClusterRadius));Range(fruitProbability,0,1,nameof(fruitProbability));
            Range(variation,0,1,nameof(variation));Range(age,.15f,1.25f,nameof(age));Range(crownStart,.05f,.85f,nameof(crownStart));
            Range(coneTaper,0,3,nameof(coneTaper));Range(leaderHeightFraction,.5f,1,nameof(leaderHeightFraction));Range(branchDroop,0,1,nameof(branchDroop));
            Range(gnarl,0,1,nameof(gnarl));Range(rootFlare,0,2,nameof(rootFlare));Range(forkAngle,5,85,nameof(forkAngle));
            Range(barkRelief,0,1.5f,nameof(barkRelief));
            if(barkRidgeCount<3||barkRidgeCount>64)throw new ArgumentOutOfRangeException(nameof(barkRidgeCount));
            if(branchLevels<1||branchLevels>5)throw new ArgumentOutOfRangeException(nameof(branchLevels));
            if(branchCount<1||branchCount>160)throw new ArgumentOutOfRangeException(nameof(branchCount));
            if(whorlSize<1||whorlSize>12)throw new ArgumentOutOfRangeException(nameof(whorlSize));
            if(trunkRadius>height*.4f||crownRadius>height*2.5f)throw new ArgumentException("Trunk/crown proportions exceed supported genome bounds.");
            if((barkColor>>24)!=255||(leafColor>>24)!=255||(fruitColor>>24)!=255)throw new ArgumentException("Tree palette colors must be opaque 0xFFRRGGBB values.");
            Species=species;Height=height;TrunkRadius=trunkRadius;CrownRadius=crownRadius;BranchLevels=branchLevels;BranchCount=branchCount;
            BranchAngle=branchAngle;ApicalDominance=apicalDominance;Tropism=tropism;FoliageDensity=foliageDensity;LeafClusterRadius=leafClusterRadius;
            FruitProbability=fruitProbability;Variation=variation;Age=age;BarkColor=barkColor;LeafColor=leafColor;FruitColor=fruitColor;
            FoliageStyle=foliageStyle;BarkStyle=barkStyle;WhorlSize=whorlSize;CrownStart=crownStart;ConeTaper=coneTaper;
            LeaderHeightFraction=leaderHeightFraction;BranchDroop=branchDroop;Gnarl=gnarl;RootFlare=rootFlare;ForkAngle=forkAngle;
            BarkRelief=barkRelief;BarkRidgeCount=barkRidgeCount;
        }
        static void Range(float n,float min,float max,string name)
        {if(float.IsNaN(n)||float.IsInfinity(n)||n<min||n>max)throw new ArgumentOutOfRangeException(name,$"Expected finite {min}..{max}.");}
        public TreeParameters With(float? height=null,float? trunkRadius=null,float? crownRadius=null,int? branchLevels=null,int? branchCount=null,
            float? branchAngle=null,float? apicalDominance=null,float? tropism=null,float? foliageDensity=null,float? leafClusterRadius=null,
            float? fruitProbability=null,float? variation=null,float? age=null,uint? barkColor=null,uint? leafColor=null,uint? fruitColor=null,
            int? whorlSize=null,float? crownStart=null,float? coneTaper=null,float? leaderHeightFraction=null,float? branchDroop=null,
            float? gnarl=null,float? rootFlare=null,float? forkAngle=null,TreeFoliageStyle? foliageStyle=null,TreeBarkStyle? barkStyle=null,
            float? barkRelief=null,int? barkRidgeCount=null)
            =>new TreeParameters(Species,height??Height,trunkRadius??TrunkRadius,crownRadius??CrownRadius,branchLevels??BranchLevels,branchCount??BranchCount,
                branchAngle??BranchAngle,apicalDominance??ApicalDominance,tropism??Tropism,foliageDensity??FoliageDensity,leafClusterRadius??LeafClusterRadius,
                fruitProbability??FruitProbability,variation??Variation,age??Age,barkColor??BarkColor,leafColor??LeafColor,fruitColor??FruitColor,
                foliageStyle??FoliageStyle,barkStyle??BarkStyle,whorlSize??WhorlSize,crownStart??CrownStart,coneTaper??ConeTaper,leaderHeightFraction??LeaderHeightFraction,
                branchDroop??BranchDroop,gnarl??Gnarl,rootFlare??RootFlare,forkAngle??ForkAngle,barkRelief??BarkRelief,barkRidgeCount??BarkRidgeCount);
        public TreeParameters Scaled(float scale)
        {
            Range(scale,.05f,10,nameof(scale));
            return With(height:Height*scale,trunkRadius:Math.Max(.025f,TrunkRadius*scale),crownRadius:Math.Max(.15f,CrownRadius*scale),leafClusterRadius:Math.Max(.06f,LeafClusterRadius*scale),barkRelief:BarkRelief*scale);
        }
    }
}
