using System;
using System.IO;
using System.Text.Json;
using AshenVoxel.Exploration.Editor;
static class Program
{
    static void Main(string[] args)
    {
        int checks=BakedOverviewChecks.RunSynthetic();
        File.WriteAllText(args[0],JsonSerializer.Serialize(new{status="PASS",checks,scope="Engine-free .NET synthetic field bounds/snapshot checks. Native 10,000-point real-patch comparison and Unity import pending."},new JsonSerializerOptions{WriteIndented=true}));
        Console.WriteLine("PASS "+checks+" synthetic baked overview checks");
    }
}
