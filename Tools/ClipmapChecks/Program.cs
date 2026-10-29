using System;
using System.IO;
using System.Text.Json;
using AshenVoxel.Exploration.Editor;
static class Program
{
    static void Main(string[] args)
    {
        var report=ClipmapChecks.Run();
        File.WriteAllText(args[0],JsonSerializer.Serialize(report,new JsonSerializerOptions{WriteIndented=true,IncludeFields=true}));
        if(report.status!="PASS")throw new InvalidOperationException(report.error);
        Console.WriteLine("PASS "+report.assertions+" clipmap CPU assertions");
    }
}
