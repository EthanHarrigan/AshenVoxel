using System;
using System.Collections.Generic;
using AshenVoxel.Core;
using AshenVoxel.Core.Details;

namespace AshenVoxel.Exploration.Experimental
{
    // Complete replay of the two shipped shrub recipes, never mesh voxelization.
    // Call only after matching the pack + generator preimages in the manifest.
    // Does not claim an instance lease, suppress static draws, or award cargo.
    public static class ShrubMatterTemplate
    {
        public const int MaximumCells=4096;
        public static readonly Int3 ExportAnchor=new Int3(40,4,40);
        public static VoxelWorld Replay(int prototypeId,out PlantResult result)
        {
            if(prototypeId!=102&&prototypeId!=103)throw new ArgumentOutOfRangeException(nameof(prototypeId));
            int seed=prototypeId==102?714103:271828;
            var world=new VoxelWorld(80,64,80,.1f,seed);
            result=PlantGenerator.Generate(world,PlantPresets.For(PlantKind.Shrub).With(fruitProbability:.35),ExportAnchor,seed,
                new PlantGenerationBudget(250000,MaximumCells));
            if(result.Truncated||result.WrittenCells==0)throw new InvalidOperationException("Complete canonical shrub required; reject without instance transfer.");
            return world;
        }
    }
}
