using System;
using System.Reflection;
using System.Threading;
using AshenVoxel.WorldScaleRuntime;
using AshenVoxel.Exploration;
static class Program
{
    static int checks;
    static readonly MethodInfo tick=typeof(TerrainClipmap).GetMethod("LateUpdate",BindingFlags.NonPublic|BindingFlags.Instance);
    static void Check(bool ok,string name){if(!ok)throw new Exception(name);checks++;Console.WriteLine("PASS "+name);}
    static void Tick(TerrainClipmap renderer){tick.Invoke(renderer,null);if(renderer.Failed)throw new Exception(renderer.Status);}
    static TerrainClipmap Start(out WorldExplorer world)
    {
        world=new WorldExplorer();var r=new TerrainClipmap{SamplesPerJob=512,UploadSamplesPerFrame=1024};
        r.Initialize(world,64,.2,10,10,10);r.SetLoadingScreenActive(true);return r;
    }
    static void Main()
    {
        var r=Start(out var world);var gpu=TerrainClipmapResources.Last;
        var deadline=DateTime.UtcNow.AddSeconds(10);
        while(!r.IsReady&&DateTime.UtcNow<deadline){Tick(r);Thread.Sleep(1);}
        Check(r.IsReady,"hidden renderer completes sampling and publication");
        Check(r.SamplesCompleted>0&&gpu.Publications==r.State.LevelCount,"all levels publish while hidden");
        Check(r.IsFineReadyArea(0,0,2),"visible cache and fine readiness update while hidden");
        Check(r.HasCoverage&&r.VisibleLevels==r.State.LevelCount,"complete visible chain remains queryable");
        Check(gpu.DrawInvocations==0&&r.SubmittedFrames==0&&r.DrawCalls==0&&r.Triangles==0,"hidden renderer submits no draws and reports zero");
        var bootstrap=new ExplorationBootstrap{Clipmap=r,Vista=world};
        var handoff=bootstrap.Handoff();
        Check(handoff.MoveNext()&&!r.LoadingScreenActive,"production handoff opens draw gate and yields a covered frame");
        Tick(r);
        Check(r.SubmittedFrames==1&&r.DrawCalls>0,"one covered frame submits visible geometry");
        Check(!handoff.MoveNext(),"handoff finishes after covered submission");
        r.Invalidate(-1,-1,1,1);long submitted=r.SubmittedFrames;Tick(r);
        Check(!r.IsReady&&r.SubmittedFrames>submitted,"gameplay source refresh keeps drawing retained coverage");
        r.SetLoadingScreenActive(true);
        Check(r.DrawCalls==0&&r.Triangles==0,"re-entered loader clears stale draw counters immediately");
        Tick(r);Check(r.SubmittedFrames==submitted+1,"suppressed frame does not count as submitted");
        // Finish the refresh first; the bounded warmup does not waive readiness.
        deadline=DateTime.UtcNow.AddSeconds(10);
        while(!r.IsReady&&DateTime.UtcNow<deadline){Tick(r);Thread.Sleep(1);}
        handoff=bootstrap.Handoff();int waits=0;while(handoff.MoveNext())waits++;
        Check(waits==2&&!r.LoadingScreenActive,"handoff remains bounded without render updates or Game view focus");
        r.SetLoadingScreenActive(true);handoff=bootstrap.Handoff();Check(handoff.MoveNext(),"fallback fixture begins covered warmup");
        gpu.ThrowOnDraw=true;tick.Invoke(r,null);
        Check(r.Failed&&!world.UsingClipmap,"first covered draw failure restores Legacy");
        Check(handoff.MoveNext(),"fallback keeps loader while Legacy coverage is absent");
        world.LegacyLocalReady=true;
        Check(handoff.MoveNext(),"local ground alone does not waive distant coverage");
        world.LegacyDistantReady=true;world.LegacyLocalReady=false;
        Check(handoff.MoveNext(),"distant ground alone does not waive local coverage");
        world.LegacyLocalReady=true;Check(!handoff.MoveNext(),"fallback handoff completes after both Legacy readiness conditions");
        Check(!r.LoadingScreenActive&&!world.UsingClipmap&&r.State==null,"shutdown releases suppression, mirrors and clipmap attachment");
        r=Start(out world);gpu=TerrainClipmapResources.Last;Tick(r);
        Check(r.LoadingScreenActive&&gpu.DrawInvocations==0,"retry gets an independent hidden renderer");
        r.Shutdown(true);
        Console.WriteLine("PASS "+checks+" production runtime/loading lifecycle checks; graphics calls modeled, no native rendering.");
    }
}
