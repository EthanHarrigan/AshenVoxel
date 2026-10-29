using System;
using System.IO;
using System.Text.Json;
using System.Collections.Generic;
using AshenVoxel.WorldScale;

static class Program
{
    static int assertions,publications;static long sampled;
    static void Check(bool value,string message){assertions++;if(!value)throw new InvalidOperationException(message);}
    static FarAppearanceSample Value(long x,long z,int revision=1)
    {unchecked{return new FarAppearanceSample(0xff000000u|((uint)(x*104729+z*15485863+revision*32452843)&0xffffff),((uint)(x*49999)^((uint)z*61169u))+(uint)revision);}}
    // Independent oracle uses floor division, not production's modulo implementation.
    static int Index(long x,long z,int size)
    {int a=(int)(x-size*(long)Math.Floor(x/(double)size)),b=(int)(z-size*(long)Math.Floor(z/(double)size));return a+b*size;}
    static bool Same(FarAppearanceSample a,FarAppearanceSample b)=>a.Tint==b.Tint&&a.Appearance==b.Appearance;
    static FarAppearanceSample[] Fill(FarAppearanceJob job,int revision=1)
    {
        var r=job.Rect;var result=new FarAppearanceSample[r.Count];int n=0;
        for(long z=r.Z;z<r.EndZ;z++)for(long x=r.X;x<r.EndX;x++)result[n++]=Value(x,z,revision);
        return result;
    }
    static FarAppearancePublication Drain(TerrainFarAppearanceState state,int size,int revision=1,long expected=-1)
    {
        long count=0;int guard=0;
        while(state.TryTakeJob(8192,out var job))
        {
            Check(++guard<10000,"Queue failed to quiesce");Check(job.Rect.Count>0&&job.Rect.Count<=8192,"Job budget exceeded");
            Check(state.InFlightSamples==job.Rect.Count,"In-flight count differs from owned job");
            Check(!state.TryTakeJob(8192,out _),"More than one job admitted");
            Check(state.PendingSamples>=state.InFlightSamples&&state.PendingSamples<=(long)size*size,"Pending work not bounded");
            Check(!state.TryTakePublication(out _),"Partial publication while worker outstanding");
            count+=job.Rect.Count;Check(state.Complete(job,Fill(job,revision)),"Current completion rejected");
            Check(!state.Complete(job,Fill(job,revision)),"Duplicate completion accepted");
        }
        if(expected>=0)Check(count==expected,"Sample count mismatch: "+count+" expected "+expected);
        sampled+=count;Check(state.InFlightSamples==0,"Leaked in-flight samples");
        Check(state.TryTakePublication(out var publication),"Complete candidate never became publishable");
        Check(state.TryTakePublication(out var repeat)&&ReferenceEquals(publication,repeat),"Publication token not stable");
        Check(publication.Samples.Length==size*size,"Wrong publication storage size");
        Check(state.Publish(publication),"Valid publication rejected");publications++;
        Check(!state.Publish(publication),"Duplicate publication accepted");
        Check(!state.TryTakeJob(8192,out _),"Published quiescent window repeated work");
        return publication;
    }
    static void AssertWindow(FarAppearancePublication pub,int revision=1)
    {
        var w=pub.Window;for(long z=w.OriginZ;z<w.OriginZ+w.Size;z++)for(long x=w.OriginX;x<w.OriginX+w.Size;x++)
            Check(Same(pub.Samples[Index(x,z,w.Size)],Value(x,z,revision)),"Wrong world-addressed appearance at "+x+","+z);
    }
    static void Torus()
    {
        foreach(int size in new[]{384,768})
        {
            var occupied=new bool[size*size];long ox=-size*3-17,oz=-size*2+37;
            for(long z=oz;z<oz+size;z++)for(long x=ox;x<ox+size;x++)
            {int n=TerrainFarAppearanceState.ToroidalIndex(x,z,size);Check(n>=0&&n<occupied.Length,"Toroidal index escaped array");Check(!occupied[n],"Toroidal cell collision");occupied[n]=true;Check(n==Index(x,z,size),"Negative/non-power-two address mismatch");}
        }
    }
    static void ScrollAndSpacing()
    {
        const int size=768;var state=new TerrainFarAppearanceState(size);long ox=-401,oz=-911;
        Check(state.ResidentBytes==2L*size*size*8,"CPU mirrors exceed two-window payload bound");
        state.Request(ox,oz,4);var first=Drain(state,size,expected:(long)size*size);AssertWindow(first);
        Check(!first.CopyPublished,"First publication requested nonexistent GPU source");
        var active=state.ActiveWindow;state.Request(ox+7,oz-11,4);
        Check(state.ActiveWindow.HasValue&&state.ActiveWindow.Value.Generation==active.Value.Generation,"Scroll exposed partial window");
        // Count independent set-membership rather than a duplicated strip formula.
        long exposed=0;for(long z=oz-11;z<oz-11+size;z++)for(long x=ox+7;x<ox+7+size;x++)
            if(x<ox||x>=ox+size||z<oz||z>=oz+size)exposed++;
        var moved=Drain(state,size,expected:exposed);Check(moved.CopyPublished,"Overlap update failed to retain GPU source");AssertWindow(moved);
        // The old borrowed mirror was not modified while it remained active.
        AssertWindow(first);
        state.Request(ox+7,oz-11,4);Check(!state.TryTakeJob(8192,out _),"Identical request schedules work");
        state.Request(ox+7,oz-11,8);Check(state.ActiveWindow.Value.Spacing==4,"Spacing switched before acknowledgement");
        var resized=Drain(state,size,expected:(long)size*size);Check(!resized.CopyPublished,"Spacing change copied incompatible texels");AssertWindow(resized);
        Check(state.ResidentBytes==2L*size*size*8,"Resident payload grew after scrolling");
    }
    static void StaleAndFailure()
    {
        const int size=384;var state=new TerrainFarAppearanceState(size);state.Request(-100,-200,4);
        Check(state.TryTakeJob(1024,out var old),"Missing initial job");var oldValues=Fill(old);
        state.Request(1000,2000,4);Check(!state.Complete(old,oldValues),"Stale scroll worker accepted");Check(!state.Fail(old),"Stale failure mutated queue");
        Check(state.TryTakeJob(1024,out var retry),"New target lost pending work");long before=state.PendingSamples;
        Check(state.Fail(retry),"Current failure rejected");Check(state.InFlightSamples==0&&state.PendingSamples==before,"Failure discarded work");Check(!state.Fail(retry),"Duplicate failure accepted");
        var basePub=Drain(state,size,expected:(long)size*size);AssertWindow(basePub);
        state.Request(1001,2000,4);Check(state.TryTakeJob(1024,out var editJob),"Missing scroll strip job");
        state.Invalidate(editJob.Rect.X*4,editJob.Rect.Z*4,editJob.Rect.X*4,editJob.Rect.Z*4);Check(!state.Complete(editJob,Fill(editJob)),"Stale overlapping edit worker accepted");
        Drain(state,size,revision:2);
        state.Request(3000,4000,4);
        while(state.TryTakeJob(8192,out var job))Check(state.Complete(job,Fill(job)),"Staging completion failed");
        Check(state.TryTakePublication(out var stalePub),"Missing finished publication");
        long activeGeneration=state.ActiveWindow.Value.Generation;
        state.Invalidate(3001*4,4001*4,3001*4,4001*4);
        Check(!state.Publish(stalePub),"Stale publication after edit accepted");
        Check(state.ActiveWindow.Value.Generation==activeGeneration,"Invalidated staging replaced active data");
        Drain(state,size,revision:3,expected:13*13);
        state.Request(3001,4000,4);
        while(state.TryTakeJob(8192,out var staged))Check(state.Complete(staged,Fill(staged)),"Scroll staging failed");
        Check(state.TryTakePublication(out var superseded),"Scroll staging token missing");
        state.Request(3002,4000,4);Check(!state.Publish(superseded),"Publication for superseded scroll accepted");
        Drain(state,size,expected:2*size);
    }
    static void DisjointEdits()
    {
        const int size=768;var state=new TerrainFarAppearanceState(size);state.Request(-384,-384,4);var original=Drain(state,size,expected:(long)size*size);
        // At4m spacing,32m wind +3*4m footprint halo =11 cells each side.
        // These integer point edits therefore require two disjoint23x23 squares.
        state.Invalidate(-800,-800,-800,-800);state.Invalidate(800,800,800,800);
        var updated=Drain(state,size,revision:2,expected:2*23*23);
        Check(Same(updated.Samples[Index(-200,-200,size)],Value(-200,-200,2)),"First disjoint edit disappeared");
        Check(Same(updated.Samples[Index(200,200,size)],Value(200,200,2)),"Second disjoint edit disappeared");
        Check(Same(updated.Samples[Index(0,0,size)],Value(0,0)),"Unchanged gap was rebuilt/recolored");
        AssertWindow(original);
        // Identical overlapping edit obligations must not be paid twice.
        state.Invalidate(0,0,0,0);state.Invalidate(0,0,0,0);
        Drain(state,size,revision:3,expected:23*23);
    }
    static void PartialWorkSurvivesEdits()
    {
        const int size=384;var state=new TerrainFarAppearanceState(size);state.Request(0,0,4);
        Check(state.TryTakeJob(8192,out var completed),"Initial work missing");Check(state.Complete(completed,Fill(completed)),"Initial completion failed");
        Check(state.TryTakeJob(8192,out var unrelated),"Second work missing");
        // This edit is in a future row, disjoint from both completed and running jobs.
        state.Invalidate(1000,1000,1000,1000);
        Check(!state.ActiveWindow.HasValue&&!state.TryGetSample(0,0,out _),"Partial initial fill leaked into active reads");
        Check(state.Complete(unrelated,Fill(unrelated)),"Nonintersecting running job was discarded after edit");
        var retained=Drain(state,size,revision:2,expected:(long)size*size-completed.SampleCount-unrelated.SampleCount);
        Check(Same(retained.Samples[Index(100,0,size)],Value(100,0)),"Unaffected completed initial rows were rebuilt");
        Check(Same(retained.Samples[Index(250,250,size)],Value(250,250,2)),"New source edit not included in initial publication");

        var overlap=new TerrainFarAppearanceState(size);overlap.Request(0,0,4);
        Check(overlap.TryTakeJob(8192,out var done),"Second fixture work missing");Check(overlap.Complete(done,Fill(done)),"Second fixture completion failed");
        Check(overlap.TryTakeJob(8192,out var stale),"Overlapping running work missing");
        overlap.Invalidate(stale.Rect.X*4,stale.Rect.Z*4,stale.Rect.X*4,stale.Rect.Z*4);
        Check(!overlap.Complete(stale,Fill(stale)),"Intersecting running result accepted after source edit");
        Check(overlap.PendingSamples<(long)size*size-1000,"Local edit restarted initial full-window fill");
        var result=Drain(overlap,size,revision:2);
        Check(Same(result.Samples[Index(100,0,size)],Value(100,0)),"Local edit destroyed distant completed work");
        Check(Same(result.Samples[Index(stale.Rect.X,stale.Rect.Z,size)],Value(stale.Rect.X,stale.Rect.Z,2)),"Requeued overlapping job lost edit");

        // Active data must remain queryable while another target is only partly sampled.
        state.Request(1,0,4);Check(state.TryTakeJob(16,out var partial),"Partial scroll missing");
        Check(state.Complete(partial,Fill(partial,3)),"Partial scroll completion failed");
        Check(state.TryGetSample(100,0,out var stillOld)&&Same(stillOld,Value(100,0)),"Unpublished partial scroll changed active data");
        Check(!state.TryGetSample(size,0,out _),"Newly exposed texel became visible before publication");
    }
    static void DirtyOverflow()
    {
        const int size=768;var state=new TerrainFarAppearanceState(size);state.Request(-384,-384,4);Drain(state,size,expected:(long)size*size);
        for(int z=0;z<6;z++)for(int x=0;x<8;x++)
        {
            double px=(-300+x*70)*4,pz=(-300+z*70)*4;state.Invalidate(px,pz,px,pz);
            Check(state.PendingSamples>0&&state.PendingSamples<=(long)size*size,"Many edits made work unbounded");
            Check(state.PendingJobs<=17,"Dirty-overflow fallback failed to bound region queue");
        }
        var publication=Drain(state,size,revision:4,expected:(long)size*size);AssertWindow(publication,4);
        Check(state.ResidentBytes==2L*size*size*8,"Dirty-overflow fallback grew mirrors");
    }
    static void PublicationIncludesLateEdits()
    {
        const int size=384;var state=new TerrainFarAppearanceState(size);state.Request(-192,-192,4);Drain(state,size,expected:(long)size*size);
        state.Invalidate(-400,-400,-400,-400);
        Check(state.TryTakeJob(128,out var first),"First dirty-region job missing");
        Check(state.Complete(first,Fill(first,2)),"Dirty-region partial completion failed");
        state.Invalidate(400,400,400,400);
        Check(state.TryGetSample(-100,-100,out var active)&&Same(active,Value(-100,-100)),"Partial refresh leaked before acknowledgement");
        var publication=Drain(state,size,revision:2,expected:2*23*23-first.SampleCount);
        Check(publication.CopyPublished,"Partial refresh lost its published GPU base");
        // Independent oracle enumerates every texel affected by each point edit.
        // Every one must be uploaded, even if its source changed after staging began.
        foreach(int center in new[]{-100,100})for(int z=center-11;z<=center+11;z++)for(int x=center-11;x<=center+11;x++)
        {
            bool upload=false;foreach(var region in publication.ChangedRegions)if(region.Contains(x,z)){upload=true;break;}
            Check(upload,"Late edit missing from GPU publication regions");
            Check(Same(publication.Samples[Index(x,z,size)],Value(x,z,2)),"Late edit missing from published CPU samples");
        }
    }
    static void TargetSelection()
    {
        // Independent geometric fixture: rendered squares have half-widths30,
        //60,120m. Coarser rings remove their immediate child's whole square.
        var chain=new[]{new ClipmapLevelInfo(0,1,-32,-32,true,1),new ClipmapLevelInfo(1,2,-32,-32,true,1),new ClipmapLevelInfo(2,4,-32,-32,true,1)};
        Check(TerrainFarAppearanceTarget.TryChoose(chain,64,0,0,100,55,55,out var clipped)&&clipped.Level==1,"Whole-world anchor outside source domain selected");
        Check(TerrainFarAppearanceTarget.TryChoose(chain,64,0,0,55,200,200,out var axial)&&axial.Level==1,"Configured axial view fixture chose wrong ring");
        Check(TerrainFarAppearanceTarget.TryChoose(chain,64,0,0,80,200,200,out var corner)&&corner.Level==2,"Far-frustum corner radius omitted reachable outer ring");
        // At x54 the unbounded outer ring is6m away, but that entire side lies
        // outside the source's x55 boundary. Remaining outer strips are60m away.
        Check(TerrainFarAppearanceTarget.TryChoose(chain,64,54,0,8,55,200,out var edge)&&edge.Level==1,"Domain-edge clipping wasted target on invisible outer strip");
        Check(TerrainFarAppearanceTarget.TryChoose(chain,64,-54,0,8,55,200,out var negative)&&negative.Level==1,"Negative domain edge was not clipped symmetrically");
        Check(!TerrainFarAppearanceTarget.TryChoose(Array.Empty<ClipmapLevelInfo>(),64,0,0,80,200,200,out _),"Empty drawable chain selected appearance target");
    }
    static int Main(string[] args)
    {
        string error=null;try{Torus();ScrollAndSpacing();StaleAndFailure();DisjointEdits();PartialWorkSurvivesEdits();DirtyOverflow();PublicationIncludesLateEdits();TargetSelection();}catch(Exception e){error=e.ToString();}
        var report=new{status=error==null?"PASS":"FAIL",assertions,publications,sampled,error,scope="Focused standalone .NET8 cache lifecycle/addressing only; no Unity or world-data sampling"};
        File.WriteAllText(args[0],JsonSerializer.Serialize(report,new JsonSerializerOptions{WriteIndented=true}));
        Console.WriteLine(JsonSerializer.Serialize(report));return error==null?0:1;
    }
}
