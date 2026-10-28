using System;
using AshenVoxel.Exploration;
static class Program
{
    static int checks;
    static void Check(bool ok,string why){checks++;if(!ok)throw new Exception(why);}
    static void Main()
    {
        var p=new WorldLoadingProgress();p.Reset(100);
        Check(!p.Stalled(119.9),"stall grace period");
        Check(p.Stalled(120),"no completed work becomes visibly stalled");
        Check(p.Idle(121)==21,"heartbeat does not fabricate progress");
        p.SetPhase(WorldLoadingPhase.Terrain,125);Check(p.StageElapsed(126)==1&&p.Elapsed(126)==26,"stage and total clocks are distinct");
        p.Observe(3,127);Check(p.Idle(127)==0,"real completed work resets stall clock");
        p.Observe(3,140);p.Observe(1,146);p.Observe(double.NaN,146);p.Observe(double.PositiveInfinity,146);
        Check(p.Stalled(147),"repeated, decreasing or invalid counters cannot hide a stall");
        p.SetPhase(WorldLoadingPhase.Terrain,149);Check(p.Stalled(149),"repeated same-stage updates retain stall evidence");
        p.Observe(4,150);Check(!p.Stalled(150),"completed work recovers a stalled stage");
        p.SetPhase(WorldLoadingPhase.Objects,151);p.Observe(1,152);Check(p.Idle(153)==1,"new stage accepts independent counters");
        p.SetPhase(WorldLoadingPhase.Complete,200);Check(!p.Stalled(1000),"completed loading is not stalled");
        p.SetPhase(WorldLoadingPhase.Failed,201);Check(!p.Stalled(1000),"failure remains distinct from inactivity");
        p.Reset(300);Check(p.Elapsed(300)==0&&!p.Stalled(300)&&p.Phase==WorldLoadingPhase.Data,"retry clears old clocks and progress");
        Check(WorldLoadingProgress.Fraction(0,0)==0,"unknown total never reports complete");
        Check(WorldLoadingProgress.Fraction(5,10)==.5f&&WorldLoadingProgress.Fraction(11,10)==1&&WorldLoadingProgress.Fraction(-1,10)==0,"actual completion ratio is bounded");
        Check(WorldLoadingProgress.Duration(65)=="1m 05s"&&WorldLoadingProgress.Duration(-1)=="0s","readable bounded elapsed labels");
        Console.WriteLine("PASS "+checks+" loading progress checks");
    }
}
