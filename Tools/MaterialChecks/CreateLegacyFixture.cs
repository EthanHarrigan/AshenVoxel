// Compile only against the Core sources from the published pre-material-update
// AshenVoxel-InfiniteDiffusion-Worlds.zip, NOT together with Program.cs.
using System;
using System.IO;
using AshenVoxel.Core;
static class CreateLegacyFixture
{
    static void Main(string[] args)
    {
        var world=new VoxelWorld(16,16,16,.1f,714103);
        for(int id=1;id<=10;id++)world.Set(id,3,2,(MaterialId)id,EnvironmentColors.Sample((MaterialId)id,id,3,2,.1f,714103));
        using(var stream=File.Create(args[0]))Persistence.Save(stream,world);
        ulong hash=1469598103934665603;
        unchecked
        {
            for(int id=0;id<=10;id++)for(int seed=-1;seed<=1;seed++)for(int n=0;n<37;n++)
            {
                uint color=EnvironmentColors.Sample((MaterialId)id,n*13-241,n*5-91,n*-17+117,.1f,seed*714103);
                hash=(hash^color)*1099511628211;
            }
        }
        Console.WriteLine("LEGACY_ENVIRONMENT_COLOR_HASH "+hash);
    }
}
