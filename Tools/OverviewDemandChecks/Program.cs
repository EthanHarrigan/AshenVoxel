using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Text.Json;
using AshenVoxel.WorldScale;
static class Program
{
    static int checks;
    static void Check(bool valid,string message){if(!valid)throw new Exception(message);checks++;Console.WriteLine("PASS "+message);}
    static bool Same(ExplorationOverviewDemand.Cell a,ExplorationOverviewDemand.Cell b)=>a.CellX==b.CellX&&a.CellZ==b.CellZ;
    static ExplorationOverviewPatchInfo Cover(ExplorationOverviewDemand.Cell cell,int spacing)
    {int side=3200/spacing+1;return new ExplorationOverviewPatchInfo(new ExplorationOverviewPatch(cell.CellX,cell.CellZ,spacing,new float[side*side],new string('a',64)));}
    static void Throws(Action action,string message)
    {try{action();}catch(ArgumentException){Check(true,message);return;}Check(false,message);}
    static void Main(string[] args)
    {
        var center=new WorldPoint(0,100,0);var empty=Array.Empty<ExplorationOverviewPatchInfo>();
        var whole=new ExplorationRefinementBounds(-149760,-74880,149760,74880);
        var large=ExplorationOverviewDemand.Build(center,100000,whole,empty);
        Check(large.ScannedCells==7080&&large.Desired.Length<=8192&&large.TotalCells>1000,"actual299.52x149.76km centered map is fully scanned within8192 cells");
        Check(large.Desired.Any(c=>c.CoreBounds.MaximumZ>whole.MaximumZ)&&large.Desired.Any(c=>c.CoreBounds.MinimumZ<whole.MinimumZ),"canonical overview edge cells intersecting map bounds are included without truncating provider cores");
        var keys=new HashSet<(long,long)>(large.Desired.Select(c=>(c.CellX,c.CellZ)));
        Check(large.Desired.All(c=>keys.Contains((-c.CellX-1,-c.CellZ-1))),"yaw-independent100km demand covers opposite directions symmetrically");
        Check(large.Desired.All(c=>c.CoreBounds.DistanceSquared(0,0)<=100000.0*100000&&c.InfluenceBounds.DistanceSquared(0,0)>2048.0*2048),"every requested core intersects horizon and its full320m influence stays outside2048m player protection");
        bool ordered=true;for(int i=1;i<large.Desired.Length;i++)
        {var a=large.Desired[i-1];var b=large.Desired[i];ordered&=a.DistanceSquared>b.DistanceSquared||a.DistanceSquared==b.DistanceSquared&&(a.CellZ<b.CellZ||a.CellZ==b.CellZ&&a.CellX<b.CellX);}
        Check(ordered&&large.PassSpacingMeters==160&&Same(large.NextRequest.Value,large.Desired[0]),"one request starts the deterministic farthest-first160m learned-source pass");
        var repeat=ExplorationOverviewDemand.Build(center,100000,whole,empty);
        Check(repeat.Desired.Select(c=>c.Key).SequenceEqual(large.Desired.Select(c=>c.Key)),"stationary repeated planning retains exactly the same bounded demand order");
        var bounds=new ExplorationRefinementBounds(-7680,-7680,7680,7680);var plan=ExplorationOverviewDemand.Build(center,20000,bounds,empty);
        var first80=Cover(plan.Desired[0],80);var partly=ExplorationOverviewDemand.Build(center,20000,bounds,new[]{first80});
        Check(partly.Completed160Cells==1&&partly.Completed80Cells==1&&partly.PassSpacingMeters==160&&Same(partly.NextRequest.Value,plan.Desired[1]),"existing80m source satisfies160m coverage while missing160m cores retain first-pass priority");
        var all160=plan.Desired.Select(c=>Cover(c,160)).ToArray();var second=ExplorationOverviewDemand.Build(center,20000,bounds,all160);
        Check(second.PassSpacingMeters==80&&second.CompletedCells==0&&second.Completed160Cells==plan.TotalCells&&Same(second.NextRequest.Value,plan.Desired[0]),"80m refinement starts only after every current-demand core has160m source");
        var mixed=ExplorationOverviewDemand.Build(center,20000,bounds,all160.Concat(new[]{first80}).ToArray());
        Check(mixed.PassSpacingMeters==80&&mixed.CompletedCells==1&&Same(mixed.NextRequest.Value,plan.Desired[1]),"duplicate coarse/fine entries retain finest exact cell coverage and skip completed80m work");
        var all80=plan.Desired.Select(c=>Cover(c,80)).ToArray();var done=ExplorationOverviewDemand.Build(center,20000,bounds,all80);
        Check(done.PassSpacingMeters==0&&done.CompletedCells==done.TotalCells&&!done.NextRequest.HasValue,"fully refined current demand returns no fabricated request or unfinished pass");
        var moved=ExplorationOverviewDemand.Build(new WorldPoint(3000,100,0),20000,bounds,all80);
        Check(moved.PassSpacingMeters==160&&moved.NextRequest.HasValue,"moving protection reveals new cores and restarts required160m coverage before further80m requests");
        var tangent=ExplorationOverviewDemand.Build(new WorldPoint(192,100,0),20000,bounds,empty);
        var clear=ExplorationOverviewDemand.Build(new WorldPoint(191.999,100,0),20000,bounds,empty);
        Check(!tangent.Desired.Any(c=>c.CellX==1&&c.CellZ==0)&&clear.Desired.Any(c=>c.CellX==1&&c.CellZ==0),"influence tangent to player protection is excluded with explicit meter/cell boundaries");
        var away=ExplorationOverviewDemand.Build(new WorldPoint(1000000,0,1000000),20000,bounds,empty);
        Check(away.TotalCells==0&&away.PassSpacingMeters==0&&!away.NextRequest.HasValue,"no intersecting world demand returns an honestly empty completed plan");
        bool capacity=false;try{ExplorationOverviewDemand.Build(center,20000,new ExplorationRefinementBounds(-200000,-100000,200000,100000),empty);}catch(InvalidOperationException error){capacity=error.Message.Contains("8192");}
        Check(capacity,"overcapacity whole-world scan fails explicitly instead of silently dropping far cells");
        Throws(()=>ExplorationOverviewDemand.Build(center,19999,bounds,empty),"too-small horizon rejected");Throws(()=>ExplorationOverviewDemand.Build(center,100001,bounds,empty),"too-large horizon rejected");
        Throws(()=>ExplorationOverviewDemand.Build(center,double.NaN,bounds,empty),"nonfinite horizon rejected");Throws(()=>ExplorationOverviewDemand.Build(center,20000,default,empty),"degenerate baked bounds rejected");
        Throws(()=>ExplorationOverviewDemand.Build(center,20000,bounds,new ExplorationOverviewPatchInfo[8193]),"unbounded coverage metadata rejected");
        Throws(()=>ExplorationOverviewDemand.Build(center,20000,bounds,new[]{default(ExplorationOverviewPatchInfo)}),"invalid coverage spacing rejected before it can claim readiness");
        File.WriteAllText(args[0],JsonSerializer.Serialize(new{status="PASS",checks,whole_world_scanned=large.ScannedCells,whole_world_demand=large.TotalCells,scope="Engine-free .NET controlled overview metadata/planner checks; no actual neural inference, Unity rendering or throughput claim"},new JsonSerializerOptions{WriteIndented=true}));
    }
}
