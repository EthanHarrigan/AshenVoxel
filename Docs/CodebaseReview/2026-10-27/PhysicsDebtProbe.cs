using System;
using AshenVoxel.Core;
using AshenVoxel.Exploration.Experimental;

// Review reproduction only. Synthetic CPU workload; not Unity or natural-tree acceptance.
public static class PhysicsDebtProbe
{
    public static void Main()
    {
        const int count = 3380;
        var world = new VoxelWorld(32, 32, 32, .1f, 1);
        var cells = new Int3[count];
        for (int i = 0; i < count; i++)
        {
            cells[i] = new Int3(2 + i % 16, 2 + i / 256, 2 + i / 16 % 16);
            world.Set(cells[i], MaterialId.Wood);
        }
        var physics = new NaturalTreePhysics(world, new Int3(0, 0, 0), 0);
        foreach (var cell in cells)
            if (!physics.Cut(cell)) throw new Exception("Fixture cut refused");
        physics.TerrainHeight = (x, z) => -100;
        const int frames = 50;
        for (int frame = 0; frame < frames; frame++) physics.Step(.02f);
        Console.WriteLine("Synthetic3380IndividualCubes: elapsed={0:F6}; debt={1:F6}; advanced={2:F6}; pairPending={3}; conserved={4}",
            frames * .02, physics.PhysicsDebtSeconds, frames * .02 - physics.PhysicsDebtSeconds,
            physics.PairWorkPending, physics.Conserved);
        if (!physics.Conserved || physics.PhysicsDebtSeconds <= .1) throw new Exception("Reproduction expectation failed");

        var small = new VoxelWorld(4, 4, 4, .1f, 1);
        var one = new Int3(1, 1, 1);
        small.Set(one, MaterialId.Wood);
        var recovery = new NaturalTreePhysics(small, new Int3(0, 0, 0), 0);
        recovery.Cut(one);
        recovery.Step(.1f); // One admitted long tick creates .08 seconds of debt.
        double before = recovery.PhysicsDebtSeconds;
        for (int frame = 0; frame < 100; frame++) recovery.Step(.02f);
        Console.WriteLine("SingleCubeDebtRecovery: before={0:F6}; after100NormalTicks={1:F6}; conserved={2}",
            before, recovery.PhysicsDebtSeconds, recovery.Conserved);
        if (Math.Abs(recovery.PhysicsDebtSeconds - before) > .00001)
            throw new Exception("Expected two-substep ceiling to prevent repayment");
    }
}
