using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using AshenVoxel.Core;
using AshenVoxel.WorldScale;
using UnityEngine;
using Stopwatch=System.Diagnostics.Stopwatch;

namespace AshenVoxel.WorldScaleRuntime
{
    /// <summary>Main-thread publication boundary. Worker tasks own only immutable
    /// field snapshots and row-major result arrays; Unity resources stay here.</summary>
    [DefaultExecutionOrder(250)]
    public sealed class TerrainClipmap:MonoBehaviour
    {
        sealed class Work
        {public ClipmapJob Job;public Task<ClipmapSample[]> Task;public SurfaceMaterialSampler Sampler;public double Spacing;}
        readonly List<Work> workers=new List<Work>(8);
        readonly Queue<ClipmapRect> uploads=new Queue<ClipmapRect>();
        readonly Stopwatch clock=new Stopwatch();
        readonly Stopwatch startupClock=new Stopwatch();bool reportedFineReady;
        TerrainClipmapResources resources;
        ClipmapPublication publication;
        ClipmapLevelInfo[] visible=Array.Empty<ClipmapLevelInfo>();
        int actualInFlight,consecutiveFailures;
        bool stopped;
        long drawnRevision=-1;
        double worldHalfX,worldHalfZ;
        public WorldExplorer Vista{get;private set;}
        public TerrainClipmapState State{get;private set;}
        public string Status{get;private set;}="Clipmap not initialized";
        public bool Failed{get;private set;}
        public string LastSamplingError{get;private set;}="";
        public long DataRevision=>State?.DataRevision??0;
        public long PendingSamples=>State?.PendingSamples??0;
        public int InFlightSamples=>actualInFlight;
        public int VisibleLevels=>visible.Length;
        public int DrawCalls=>resources?.DrawCallsLastFrame??0;
        public long Triangles=>resources?.TrianglesLastDraw??0;
        public long ResidentCpuBytes=>(State?.ResidentBytes??0)+(resources?.CpuStagingBytes??0)+(long)actualInFlight*12;
        public long GpuBytes=>resources?.GpuBytes??0;
        public bool HasCoverage=>visible.Length>0;
        public bool IsReady=>State!=null&&State.IsReady;
        public int MaximumInFlightSamples=300000;
        public int SamplesPerJob=8192;
        public int UploadSamplesPerFrame=65536;
        public double UploadMilliseconds=2;
        public double LastWorkMilliseconds{get;private set;}
        public long SamplesCompleted{get;private set;}
        public int StaleJobsRejected{get;private set;}
        public Material Material=>resources?.Material;

        public void Initialize(WorldExplorer vista,int size,double spacing,double halfX,double halfZ,double viewMeters)
        {
            if(State!=null)throw new InvalidOperationException("Clipmap is already initialized");
            Vista=vista??throw new ArgumentNullException(nameof(vista));worldHalfX=halfX;worldHalfZ=halfZ;
            int levels=TerrainClipmapMath.ComputeLevelCount(size,spacing,viewMeters,halfX,halfZ);
            // Validate GPU support and allocate before disabling the legacy path.
            resources=new TerrainClipmapResources(size,levels);
            try
            {
                State=new TerrainClipmapState(size,spacing,levels,maxInFlightSamples:MaximumInFlightSamples,maxJobs:8);
                State.Move(vista.GlobalCamera.X,vista.GlobalCamera.Z);
                vista.AttachClipmap(this);startupClock.Start();Debug.Log("Clipmap preview initialized: "+size+" texels, "+levels+" levels, "+spacing+"m finest spacing; native acceptance pending",this);Status="Clipmap preview: preparing whole-world fallback";
            }
            catch{resources.Dispose();resources=null;State=null;throw;}
        }
        public bool IsReadyArea(double x,double z,double radius)=>State!=null&&State.IsReadyArea(x,z,radius);
        public bool IsFineReadyArea(double x,double z,double radius)
            =>visible.Length>0&&visible[0].Level==0&&TerrainClipmapMath.Contains(visible[0],State.Size,x,z,radius);
        public bool TryRenderedHeight(double x,double z,out double height)
        {height=0;return State!=null&&State.TryRenderedHeight(x,z,out height);}
        public void Invalidate(double x0,double z0,double x1,double z1)
        {if(State==null||stopped)return;State.Invalidate(x0,z0,x1,z1);CancelObsoleteUpload();}
        void CancelObsoleteUpload()
        {
            if(publication==null)return;
            if(State.TryTakePublication(out var current)&&ReferenceEquals(publication,current))return;
            resources.CancelUpdate();publication=null;uploads.Clear();
        }
        static uint Byte(double x)=>(uint)Math.Max(0,Math.Min(255,Math.Round(x*255)));
        static ClipmapSample[] Sample(ClipmapJob job,double spacing,SurfaceMaterialSampler sampler)
        {
            var values=new ClipmapSample[job.SampleCount];int n=0;
            for(int z=0;z<job.Rect.Height;z++)for(int x=0;x<job.Rect.Width;x++)
            {
                var c=sampler.EvaluateColumn((job.Rect.X+x)*spacing,(job.Rect.Z+z)*spacing,spacing);
                double rough=c.SurfaceMaterial==MaterialId.Ice?.18:c.SurfaceMaterial==MaterialId.Snow?.92:
                    Math.Max(.45,(c.SurfaceMaterial==MaterialId.Mud||c.SurfaceMaterial==MaterialId.Clay?.82:.95)-c.Wetness*.24);
                uint appearance=((uint)(128+(int)c.SurfaceMaterial)<<24)|(Byte(rough)<<16)|(Byte(c.Wetness)<<8)|(c.SurfaceMaterial==MaterialId.Snow?255u:0u);
                values[n++]=new ClipmapSample((float)c.SurfaceHeightMeters,c.SurfaceTint,appearance);
            }
            return values;
        }
        void LateUpdate()
        {
            if(stopped||State==null||Vista==null||Vista.MainCamera==null)return;
            try
            {
                var observer=Vista.GlobalCamera;
                // Outside the declared world domain the retained anchor is no
                // longer a coverage promise. Restore legacy planning explicitly.
                if(Math.Abs(observer.X)>worldHalfX||Math.Abs(observer.Z)>worldHalfZ)
                    throw new InvalidOperationException("Observer left the configured clipmap world; Legacy coverage resumed");
                clock.Restart();State.Move(observer.X,observer.Z);CancelObsoleteUpload();
                for(int i=workers.Count-1;i>=0;i--)
                {
                    var work=workers[i];if(!work.Task.IsCompleted)continue;
                    workers.RemoveAt(i);actualInFlight-=work.Job.SampleCount;
                    if(work.Task.IsFaulted)
                    {
                        bool current=State.Fail(work.Job);if(current)
                        {
                            consecutiveFailures++;var j=work.Job;
                            LastSamplingError="Clipmap worker failure: job="+j.Id+" generation="+j.Generation+" level="+j.Level+" rect="+j.Rect.X+","+j.Rect.Z+","+j.Rect.Width+","+j.Rect.Height+" spacing="+work.Spacing.ToString("R",System.Globalization.CultureInfo.InvariantCulture)+" source="+work.Sampler.SourceIdentity+"\n"+work.Task.Exception;
                            Debug.LogWarning(LastSamplingError,this);
                        }else StaleJobsRejected++;
                        if(current&&consecutiveFailures>=3)throw new InvalidOperationException("Clipmap sampling failed repeatedly",work.Task.Exception.GetBaseException());
                    }
                    else
                    {if(State.Complete(work.Job,work.Task.Result)){SamplesCompleted+=work.Job.SampleCount;consecutiveFailures=0;}else StaleJobsRejected++;}
                }
                if(publication==null&&State.TryTakePublication(out var ready))
                {
                    publication=ready;int count=0;foreach(var r in ready.ChangedRegions){uploads.Enqueue(r);count+=r.Count;}
                    resources.BeginUpdate(ready.Level,State.GetLevel(ready.Level).Published,count);
                }
                int budget=Math.Max(State.Size,UploadSamplesPerFrame);
                while(publication!=null&&uploads.Count>0&&budget>0&&clock.Elapsed.TotalMilliseconds<Math.Max(.25,UploadMilliseconds))
                {
                    var r=uploads.Dequeue();int rows=Math.Min(r.Height,Math.Max(1,budget/r.Width));
                    var part=new ClipmapRect(r.X,r.Z,r.Width,rows);
                    resources.Upload(publication.Level,publication.OriginX,publication.OriginZ,part,publication.Samples);budget-=part.Count;
                    if(rows<r.Height)uploads.Enqueue(new ClipmapRect(r.X,r.Z+rows,r.Width,r.Height-rows));
                }
                if(publication!=null&&uploads.Count==0)
                {
                    // Dispatches, the slice switch and draws share graphics queue
                    // ordering. CPU mirror changes only after complete submission.
                    resources.Publish(publication.Level);
                    if(!State.Publish(publication))throw new InvalidOperationException("Clipmap publication generation changed during owner-thread upload");
                    publication=null;
                }
                int maxWorkers=Math.Max(1,Math.Min(8,Environment.ProcessorCount-2));
                while(publication==null&&workers.Count<maxWorkers&&actualInFlight<MaximumInFlightSamples)
                {
                    int allowance=Math.Min(SamplesPerJob,MaximumInFlightSamples-actualInFlight);
                    if(!State.TryTakeJob(allowance,out var job))break;
                    // A publication invalidation cancels intersecting generations;
                    // unrelated immutable workers remain useful and keep their cap.
                    SurfaceMaterialSampler sampler=Vista.CaptureClipmapSampler();double spacing=State.GetLevel(job.Level).Spacing;
                    actualInFlight+=job.SampleCount;
                    workers.Add(new Work{Job=job,Sampler=sampler,Spacing=spacing,Task=Task.Run(()=>Sample(job,spacing,sampler))});
                }
                if(drawnRevision!=DataRevision){visible=State.GetVisibleLevels();drawnRevision=DataRevision;}
                resources.Draw(Vista.MainCamera,gameObject.layer,Vista.RenderOrigin,visible);
                Status=IsReady?"Clipmap preview · current detail ready · visual acceptance pending":
                    "Clipmap preview · "+visible.Length+"/"+State.LevelCount+" visible levels · "+(PendingSamples/1000)+"k samples pending";
                LastWorkMilliseconds=clock.Elapsed.TotalMilliseconds;
                if(!reportedFineReady&&IsFineReadyArea(observer.X,observer.Z,2))
                {reportedFineReady=true;Debug.Log("Clipmap preview first fine coverage: "+startupClock.Elapsed.TotalSeconds.ToString("F3")+"s; "+SamplesCompleted+" samples; CPU payload "+ResidentCpuBytes+" bytes; GPU payload "+GpuBytes+" bytes; "+DrawCalls+" draws / "+Triangles+" triangles. Visual acceptance remains pending.",this);}
            }
            catch(Exception e)
            {Failed=true;Status="Clipmap preview fell back to Legacy: "+e.Message;Debug.LogWarning(Status+"\n"+e,this);Shutdown(true);}
        }
        public void Shutdown(bool restoreLegacy)
        {
            if(stopped)return;stopped=true;enabled=false;
            resources?.Dispose();resources=null;publication=null;uploads.Clear();
            // Tasks retain immutable input and bounded private results only. Observe
            // any late faults without blocking the main thread or touching Unity.
            foreach(var work in workers)work.Task.ContinueWith(t=>{var ignored=t.Exception;},TaskContinuationOptions.OnlyOnFaulted);
            workers.Clear();actualInFlight=0;
            if(restoreLegacy&&Vista!=null)Vista.DetachClipmap(this);
            State=null;visible=Array.Empty<ClipmapLevelInfo>();
        }
        void OnDestroy(){Shutdown(false);}
    }
}
