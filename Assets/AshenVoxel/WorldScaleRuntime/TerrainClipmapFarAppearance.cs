using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using AshenVoxel.Core;
using AshenVoxel.WorldScale;
using UnityEngine;
using Stopwatch=System.Diagnostics.Stopwatch;

namespace AshenVoxel.WorldScaleRuntime
{
    public sealed partial class TerrainClipmap
    {
        sealed class FarWork
        {
            public TerrainFarAppearanceState Owner;
            public FarAppearanceJob Job;
            public Task<FarAppearanceSample[]> Task;
        }
        TerrainFarAppearanceState farState;
        FarWork farWork;
        FarAppearancePublication farPublication;
        readonly Queue<ClipmapRect> farUploads=new Queue<ClipmapRect>();
        readonly Vector3[] appearanceCorners=new Vector3[4];
        readonly Stopwatch farClock=new Stopwatch();
        int farTargetLevel=-1,farProposedLevel=-1,farFailures;
        double farNextTargetCheck,farProposedSince;
        string farProfileIdentity;
        bool farDisabled;
        public bool EnableFarAppearance=true;
        // Bootstrap sets this only after Ready; the covered warmup frame is not
        // permission to spend additional sampling/memory on optional appearance.
        public bool FarAppearanceBackgroundAllowed{get;set;}
        public string FarAppearanceStatus{get;private set;}="Far color detail waits until loading completes";
        public long FarAppearancePendingSamples=>farState?.PendingSamples??0;
        public int FarAppearanceInFlightSamples=>farWork?.Job.SampleCount??0;
        public long FarAppearanceCpuBytes=>(farState?.ResidentBytes??0)+(long)FarAppearanceInFlightSamples*8;
        public long FarAppearanceSamplesCompleted{get;private set;}
        public long FarAppearanceStaleSamples{get;private set;}
        public int FarAppearanceTargetLevel=>farTargetLevel;

        static FarAppearanceSample[] SampleFarAppearance(FarAppearanceJob job,SurfaceMaterialSampler sampler)
        {
            var samples=new FarAppearanceSample[job.SampleCount];int n=0;double s=job.Window.Spacing;
            for(long z=job.Rect.Z;z<job.Rect.EndZ;z++)for(long x=job.Rect.X;x<job.Rect.EndX;x++)
            {
                var c=sampler.EvaluateColumn(x*s,z*s,s);
                double rough=c.SurfaceMaterial==MaterialId.Ice?.18:c.SurfaceMaterial==MaterialId.Snow?.92:
                    Math.Max(.45,(c.SurfaceMaterial==MaterialId.Mud||c.SurfaceMaterial==MaterialId.Clay?.82:.95)-c.Wetness*.24);
                uint appearance=((uint)(128+(int)c.SurfaceMaterial)<<24)|(Byte(rough)<<16)|(Byte(c.Wetness)<<8)|(c.SurfaceMaterial==MaterialId.Snow?255u:0u);
                samples[n++]=new FarAppearanceSample(c.SurfaceTint,appearance);
            }
            return samples;
        }
        void ChooseFarAppearanceTarget()
        {
            double now=Time.realtimeSinceStartupAsDouble;
            if(now<farNextTargetCheck)return;farNextTargetCheck=now+.5;
            var camera=Vista.MainCamera;
            camera.CalculateFrustumCorners(new Rect(0,0,1,1),camera.farClipPlane,Camera.MonoOrStereoscopicEye.Mono,appearanceCorners);
            double radius=0;foreach(var corner in appearanceCorners)radius=Math.Max(radius,corner.magnitude);radius+=1;
            var observer=Vista.GlobalCamera;
            if(!TerrainFarAppearanceTarget.TryChoose(visible,State.Size,observer.X,observer.Z,radius,worldHalfX,worldHalfZ,out var selected))return;
            if(farTargetLevel<0)farTargetLevel=selected.Level;
            else if(selected.Level!=farTargetLevel)
            {
                if(farProposedLevel!=selected.Level){farProposedLevel=selected.Level;farProposedSince=now;}
                if(now-farProposedSince>=2)farTargetLevel=selected.Level;
            }
            else farProposedLevel=selected.Level;
            // Texture and geometry lattice origins agree exactly: geometry
            // origins are even, and the appearance lattice is3/2 as dense.
            var level=State.GetLevel(farTargetLevel);if(!level.Published)return;
            farState.Request(checked(level.OriginX*3/2),checked(level.OriginZ*3/2),level.Spacing*(2.0/3));
            CancelObsoleteFarAppearanceUpload();
        }
        void CancelObsoleteFarAppearanceUpload()
        {
            if(farPublication==null)return;
            if(farState!=null&&farState.TryTakePublication(out var current)&&ReferenceEquals(current,farPublication))return;
            resources?.CancelFarAppearance();farPublication=null;farUploads.Clear();
        }
        void InvalidateFarAppearance(double x0,double z0,double x1,double z1)
        {farState?.Invalidate(x0,z0,x1,z1);CancelObsoleteFarAppearanceUpload();}
        void TickFarAppearance()
        {
            if(farWork!=null&&farWork.Owner==null&&farWork.Task.IsCompleted)
            {if(farWork.Task.IsFaulted){var ignored=farWork.Task.Exception;}farWork=null;}
            if(farDisabled)return;
            if(!EnableFarAppearance){ReleaseFarAppearance();FarAppearanceStatus="Far color detail disabled";return;}
            if(!FarAppearanceBackgroundAllowed||LoadingScreenActive)return;
            if(State.Size!=256&&State.Size!=512)
            {FarAppearanceStatus="Far color detail limited to Fast/High memory budget";return;}
            try
            {
                // No initial readiness condition refers to this optional state.
                if(farState==null)
                {
                    if(!IsReady)return;
                    farState=new TerrainFarAppearanceState(State.Size*3/2);
                    farProfileIdentity=Vista.ClipmapProfileIdentity;
                }
                if(farProfileIdentity!=Vista.ClipmapProfileIdentity)
                {farProfileIdentity=Vista.ClipmapProfileIdentity;farState.InvalidateAll();CancelObsoleteFarAppearanceUpload();}
                ChooseFarAppearanceTarget();
                resources.AdvanceFarAppearance(Time.unscaledDeltaTime);
                if(farWork!=null&&farWork.Task.IsCompleted)
                {
                    var work=farWork;farWork=null;
                    if(ReferenceEquals(work.Owner,farState))
                    {
                        if(work.Task.IsFaulted)
                        {
                            var error=work.Task.Exception; // Observe even a cancelled generation's fault.
                            if(farState.Fail(work.Job)&&++farFailures>=3)
                                throw new InvalidOperationException("Far color sampling failed repeatedly",error.GetBaseException());
                        }
                        else if(farState.Complete(work.Job,work.Task.Result)){FarAppearanceSamplesCompleted+=work.Job.SampleCount;farFailures=0;}
                        else FarAppearanceStaleSamples+=work.Job.SampleCount;
                    }
                }
                CancelObsoleteFarAppearanceUpload();
                // Primary work is always admitted first. This lane starts only
                // after its CPU queue is empty; later edits may preempt its next job.
                bool primaryIdle=workers.Count==0&&publication==null&&State.PendingSamples==0;
                if(primaryIdle)
                {
                    farClock.Restart();
                    if(farPublication==null&&farState.TryTakePublication(out var ready))
                    {
                        int count=0;foreach(var r in ready.ChangedRegions){farUploads.Enqueue(r);count+=r.Count;}
                        resources.BeginFarAppearance(ready.Window,ready.CopyPublished,count);farPublication=ready;
                    }
                    int budget=8192;
                    while(farPublication!=null&&farUploads.Count>0&&budget>0&&farClock.Elapsed.TotalMilliseconds<.5)
                    {
                        var r=farUploads.Dequeue();int width=Math.Min(r.Width,budget),rows=Math.Min(r.Height,budget/width);
                        var part=new ClipmapRect(r.X,r.Z,width,rows);
                        resources.UploadFarAppearance(farPublication.Window,part,farPublication.Samples);budget-=part.Count;
                        if(width<r.Width)farUploads.Enqueue(new ClipmapRect(r.X+width,r.Z,r.Width-width,rows));
                        if(rows<r.Height)farUploads.Enqueue(new ClipmapRect(r.X,r.Z+rows,r.Width,r.Height-rows));
                    }
                    if(farWork==null&&farPublication==null&&farState.TryTakeJob(2048,out var job))
                    {
                        var sampler=Vista.CaptureClipmapSampler();
                        farWork=new FarWork{Owner=farState,Job=job,Task=Task.Run(()=>SampleFarAppearance(job,sampler))};
                    }
                }
                if(farPublication!=null&&farUploads.Count==0&&resources.CanPublishFarAppearance)
                {
                    resources.PublishFarAppearance(farPublication.Window);
                    if(!farState.Publish(farPublication))throw new InvalidOperationException("Far color publication became stale");
                    farPublication=null;
                }
                FarAppearanceStatus="Far color L"+farTargetLevel+" · "+(FarAppearancePendingSamples/1000)+"k samples pending · "+FarAppearanceInFlightSamples+" in flight"+
                    (farPublication!=null?" · uploading/fading":resources.FarAppearancePublished?" · active":" · sampling");
            }
            catch(Exception e)
            {
                // Optional fidelity failure must never remove terrain geometry or
                // disable the established clipmap. Existing appearance is fallback.
                ReleaseFarAppearance();farDisabled=true;FarAppearanceStatus="Far color detail off; base colors retained: "+e.Message;
                Debug.LogWarning(FarAppearanceStatus+"\n"+e,this);
            }
        }
        void ReleaseFarAppearance()
        {
            if(farWork!=null)farWork.Task.ContinueWith(t=>{var ignored=t.Exception;},TaskContinuationOptions.OnlyOnFaulted);
            // A cancelled thread-pool task still occupies this lane until it
            // finishes. It must never complete into a fresh state after re-enable.
            if(farWork!=null)farWork.Owner=null;
            farState=null;farPublication=null;farUploads.Clear();
            try{resources?.DisableFarAppearance();}catch(Exception e){Debug.LogWarning("Far color cleanup: "+e.Message,this);}
            farTargetLevel=farProposedLevel=-1;farNextTargetCheck=0;farFailures=0;
        }
    }
}
