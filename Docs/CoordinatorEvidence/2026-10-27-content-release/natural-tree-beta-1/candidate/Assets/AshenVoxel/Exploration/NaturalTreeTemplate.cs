using System;
using AshenVoxel.Core;
using AshenVoxel.Core.Trees;
namespace AshenVoxel.Exploration.Experimental
{
    public static class NaturalTreeTemplate
    {
        public const int MaximumCells=4096;
        public const string PackIdentity="41556b474ce7a26342e4dd7bcff2e4fe658ab0333e8360058262ee21c0f011aa";
        public static VoxelWorld Replay(int id,out Int3 anchor,out TreeResult result)
        {
            if(id!=0&&id!=1)throw new ArgumentOutOfRangeException(nameof(id),"Only complete naturally populated young birch0/1 fits this activation policy.");
            int seed=id==0?714103:271828;var p=TreePresets.Get(TreeSpecies.Birch).With(age:.45f);
            double reach=p.CrownRadius*Math.Pow(p.Age,.85)*1.5+p.LeafClusterRadius*Math.Pow(p.Age,.65)*3+p.TrunkRadius*Math.Pow(p.Age,1.15)*(1.7+p.RootFlare*1.1)+3;
            int side=(int)Math.Ceiling(reach*2/.1);if(side%2!=0)side++;
            int height=(int)Math.Ceiling((p.Height*p.Age*1.25+4)/.1);
            var world=new VoxelWorld(side,height,side,.1f,seed);anchor=new Int3(side/2,4,side/2);
            result=TreeGenerator.Generate(world,p,anchor.x*.1f,anchor.y*.1f,anchor.z*.1f,seed,new TreeGenerationBudget(8192,160000000,16384));
            int count=0;foreach(var chunk in world.Chunks.Values)count+=chunk.NonAirCount;
            if(result.Truncated||count!=(id==0?3395:3408)||count>MaximumCells)throw new InvalidOperationException("Complete pinned tree required; original remains intact.");
            return world;
        }
    }
}
