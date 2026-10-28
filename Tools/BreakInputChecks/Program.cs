using System;
using System.IO;
using System.Text.Json;
using AshenVoxel.Exploration.Experimental;

static class Program
{
    static int assertions;
    static void Check(bool condition,string message)
    {assertions++;if(!condition)throw new Exception(message);}
    static void Main(string[] args)
    {
        var input=new NaturalMatterBreakInput();
        Check(input.Poll(true,true,true,true,0)==NaturalMatterBreakIntent.Attempt,"ready press acts immediately");
        Check(input.Poll(false,true,true,true,.1)==NaturalMatterBreakIntent.None,"held repeat respects cadence");
        Check(input.Poll(false,true,true,true,.15)==NaturalMatterBreakIntent.Attempt,"held repeat at150ms");
        Check(input.Poll(true,true,true,true,.16)==NaturalMatterBreakIntent.Attempt,"new ready tap is immediate");
        Check(input.Poll(false,false,true,true,.17)==NaturalMatterBreakIntent.None,"release clears gesture");
        Check(input.Poll(false,true,true,true,1)==NaturalMatterBreakIntent.None,"held without fresh press cannot begin gesture");

        Check(input.Poll(true,true,true,false,2)==NaturalMatterBreakIntent.Waiting,"busy press signals waiting");
        Check(input.Poll(false,false,true,false,2.01)==NaturalMatterBreakIntent.None,"busy tap release clears gesture");
        Check(input.Poll(false,false,true,true,3)==NaturalMatterBreakIntent.None,"released tap never replays at ready");
        Check(input.Poll(true,true,true,false,4)==NaturalMatterBreakIntent.Waiting,"cold held press waits");
        for(int i=1;i<200;i++)Check(input.Poll(false,true,true,false,4+i*.01)==NaturalMatterBreakIntent.Waiting,"busy held gesture retains no edits");
        Check(input.Poll(false,true,true,true,6)==NaturalMatterBreakIntent.Attempt,"held resumes after two seconds of readiness blocking");
        Check(input.Poll(false,true,true,true,10)==NaturalMatterBreakIntent.Attempt,"long frame emits one attempt");
        Check(input.Poll(false,true,true,true,10)==NaturalMatterBreakIntent.None,"long frame does not accumulate catchup burst");

        foreach(string reason in new[]{"focus","UI","restore"})
        {
            Check(input.Poll(true,true,true,true,11)==NaturalMatterBreakIntent.Attempt,reason+" setup");
            Check(input.Poll(false,true,false,true,12)==NaturalMatterBreakIntent.None,reason+" disables gesture");
            Check(input.Poll(false,true,true,true,13)==NaturalMatterBreakIntent.None,reason+" held cannot replay after re-enable");
        }
        Check(input.Poll(true,false,true,true,14)==NaturalMatterBreakIntent.Attempt,"same-frame press/release still taps once");
        Check(input.Poll(false,true,true,true,15)==NaturalMatterBreakIntent.None,"same-frame tap does not arm future hold");
        Check(input.Poll(true,true,true,true,double.NaN)==NaturalMatterBreakIntent.None,"invalid time rejects and clears");
        Check(input.Poll(false,true,true,true,16)==NaturalMatterBreakIntent.None,"invalid-time reset needs fresh press");
        Check(input.Poll(true,true,false,true,16.1)==NaturalMatterBreakIntent.None,"press in UI is not a gameplay gesture");
        Check(input.Poll(false,true,true,true,16.2)==NaturalMatterBreakIntent.None,"UI press cannot carry into gameplay");
        input.Poll(true,true,true,true,17);input.Reset();
        Check(input.Poll(false,true,true,true,18)==NaturalMatterBreakIntent.None,"explicit session reset clears hold");

        // Model a slow six-frame publication while taps arrive every other frame.
        // Each accepted input dirties the surface. Rejected presses cannot reset
        // the remaining work; the model must publish repeatedly during the storm.
        input.Reset();int work=0,accepted=0,published=0,blocked=0;
        for(int frame=0;frame<600;frame++)
        {
            if(work>0&&--work==0)published++;
            bool press=frame%2==0;var result=input.Poll(press,press,true,work==0,frame/60.0);
            if(result==NaturalMatterBreakIntent.Attempt){Check(work==0,"tap admission never invalidates unpublished work");accepted++;work=6;}
            if(result==NaturalMatterBreakIntent.Waiting)blocked++;
        }
        Check(accepted==100&&published==99&&blocked==200,"rapid taps publish while clicking continues");
        input.Reset();work=0;accepted=published=0;int sampledAim=-1;
        for(int frame=0;frame<600;frame++)
        {
            if(work>0&&--work==0)published++;
            var result=input.Poll(frame==0,true,true,work==0,frame/60.0);
            if(result==NaturalMatterBreakIntent.Attempt){Check(work==0,"held admission never invalidates unpublished work");sampledAim=frame;accepted++;work=20;}
        }
        Check(accepted==30&&published==29&&sampledAim==580,"held slow mesh follows current aim without gesture queue");
        var report=new{status="PASS",assertions,scope="Engine-free input policy and simulated publication; not native gameplay, rendering or measured latency",rapid_tap_publications=99,held_publications=published};
        File.WriteAllText(args[0],JsonSerializer.Serialize(report,new JsonSerializerOptions{WriteIndented=true}));
        Console.WriteLine("PASS "+assertions+" break input checks");
    }
}
