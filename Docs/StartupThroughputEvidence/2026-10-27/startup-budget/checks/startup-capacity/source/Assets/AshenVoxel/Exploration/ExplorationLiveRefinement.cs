using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using AshenVoxel.WorldScale;
using AshenVoxel.WorldScaleRuntime;
using UnityEngine;

namespace AshenVoxel.Exploration
{
    /// <summary>Optional asynchronous decoded terrain extension. Every successful
    /// publication is an immutable grid outside the protected player/query/prop
    /// area. Unavailable or mismatched services leave the baked world playable.</summary>
    public sealed class ExplorationLiveRefinement:MonoBehaviour
    {
        public double TileMeters=640,BlendMeters=128,LookAheadMeters=1600;
        public int MaximumPatches=16;
        public long MaximumPayloadBytes=16L*1024*1024;
        public double ProtectedRadiusMeters=512,QueryAndPropGuardMeters=96;
        public ExplorationRefinementField WorldField {get;private set;}
        public bool Connected=>remote!=null&&remote.Connected&&accepted;
        public int PendingRequests=>work.Count;
        public string Status {get;private set;}="Live decoding is off; bundled learned terrain is ready.";
        public event Action<ExplorationRefinementBounds> RegionChanged;
        sealed class Work
        {
            public string Key;
            public ExplorationRefinementBounds Core;
            public Task<ExplorationRefinementPatch> Task;
            public ExplorationRefinementPatch Complete;
        }
        readonly List<Work> work=new List<Work>(2);
        readonly Dictionary<string,float> retries=new Dictionary<string,float>();
        ExplorationAtlasField baked;
        WorldExplorer vista;
        RemoteTerrainField remote;
        Task connecting;
        string endpoint;
        bool requested,accepted,compatible;
        float nextConnect,nextRequests,nextStatus;
        Vector3 lastHeading=Vector3.forward;

        public ExplorationRefinementField Configure(ExplorationAtlasField source,WorldExplorer renderer,string endpoint="http://127.0.0.1:8787",bool enabled=true)
        {
            if(source==null)throw new ArgumentNullException(nameof(source));
            if(renderer==null)throw new ArgumentNullException(nameof(renderer));
            StopClient();baked=source;vista=renderer;this.endpoint=endpoint;
            compatible=Math.Abs(source.WorldWidthMeters-source.OriginalWidthMeters)<.001&&Math.Abs(source.WorldDepthMeters-source.OriginalDepthMeters)<.001&&Math.Abs(source.VerticalScale-1)<1e-7;
            if(double.IsNaN(TileMeters)||TileMeters<160||TileMeters>1280||double.IsNaN(BlendMeters)||BlendMeters<32||BlendMeters>256||double.IsNaN(LookAheadMeters)||LookAheadMeters<1024||LookAheadMeters>10000)
                throw new ArgumentOutOfRangeException("Live tile/blend/look-ahead controls");
            WorldField=new ExplorationRefinementField(source,source.WorldId,5,MaximumPatches,MaximumPayloadBytes,ProtectedRadiusMeters,QueryAndPropGuardMeters);
            SetEnabled(enabled);return WorldField;
        }
        public void SetEnabled(bool enabled)
        {
            requested=enabled;
            if(!enabled){StopClient();Status="Live decoding is off; bundled and retained learned terrain remain playable.";return;}
            if(!compatible){StopClient();Status="Live decoding requires original map dimensions and vertical scale1; the resized baked world remains playable.";return;}
            nextConnect=0;Status="Looking for the matching local model service…";
        }
        public bool HasActiveDetail(double x,double z)=>WorldField!=null&&WorldField.HasActiveDetail(x,z);
        public double ResolutionMetersAt(double x,double z)=>WorldField==null?double.NaN:WorldField.ResolutionMetersAt(x,z);
        public void Tick(WorldPoint observer,Vector3 forward)
        {
            if(WorldField==null)return;WorldField.Touch(observer);
            if(!requested||!compatible)return;
            if(remote==null&&Time.unscaledTime>=nextConnect)StartConnection();
            if(connecting!=null&&connecting.IsCompleted)
            {
                Task completed=connecting;connecting=null;
                if(completed.IsFaulted||completed.IsCanceled)
                {string why=completed.IsFaulted?completed.Exception.GetBaseException().Message:"canceled";StopClient();nextConnect=Time.unscaledTime+30;Status="Local model unavailable; bundled learned terrain continues. "+why;return;}
                if(remote==null||remote.Status.world_id!=baked.WorldId||Math.Abs(remote.Status.spacing_m-5)>1e-7)
                {StopClient();nextConnect=Time.unscaledTime+30;Status="Model world identity or5m coordinates differ; bundled learned terrain continues.";return;}
                accepted=true;Status="Matching InfiniteDiffusion service connected; decoding ahead.";
            }
            if(remote!=null&&!string.IsNullOrEmpty(remote.TerminalFailure))
            {Status="Model stopped; bundled and retained learned terrain continue. "+remote.TerminalFailure;StopClient();nextConnect=Time.unscaledTime+30;return;}
            if(!Connected)return;
            for(int i=work.Count-1;i>=0;i--)
            {
                Work item=work[i];
                if(item.Complete==null&&item.Task.IsCompleted)
                {
                    if(item.Task.IsFaulted||item.Task.IsCanceled)
                    {
                        if(item.Task.IsFaulted){var observed=item.Task.Exception;}
                        retries[item.Key]=Time.unscaledTime+30;work.RemoveAt(i);continue;
                    }
                    item.Complete=item.Task.Result;
                }
                if(item.Complete!=null&&WorldField.TryPublish(item.Complete,observer,out ExplorationRefinementBounds[] changes))
                {
                    foreach(var bounds in changes)
                    {
                        vista.RefreshExternalRegion(bounds.MinimumX,bounds.MinimumZ,bounds.MaximumX,bounds.MaximumZ);
                        RegionChanged?.Invoke(bounds);
                    }
                    work.RemoveAt(i);
                }
            }
            if(Time.unscaledTime>=nextRequests)
            {
                nextRequests=Time.unscaledTime+.5f;
                Vector3 horizontal=new Vector3(forward.x,0,forward.z);
                if(horizontal.x*horizontal.x+horizontal.z*horizontal.z>.0001f)lastHeading=horizontal.normalized;
                QueueAhead(observer,lastHeading);
            }
            if(Time.unscaledTime>=nextStatus)
            {
                nextStatus=Time.unscaledTime+.5f;
                Status="Live model connected · "+WorldField.ResidentPatches+" retained decoded patches · "+work.Count+" queued/ahead · "+
                    (WorldField.HasActiveDetail(observer.X,observer.Z)?"active live detail":"bundled learned terrain here");
                if(work.Exists(item=>item.Complete!=null))Status+=" · completed patches wait outside the player guard";
            }
        }
        void StartConnection()
        {
            try
            {
                remote=new RemoteTerrainField(baked,endpoint,false,4,maximumResidentBytes:8L*1024*1024);
                connecting=remote.InitializeAsync();accepted=false;nextConnect=Time.unscaledTime+30;
            }
            catch(Exception exception){StopClient();nextConnect=Time.unscaledTime+30;Status="Local model unavailable; bundled world continues. "+exception.Message;}
        }
        bool FullyBaked(ExplorationRefinementBounds core)
        {
            var influence=core.Expanded(BlendMeters);
            return baked.FineWeightAt(influence.MinimumX,influence.MinimumZ)>=.999999&&baked.FineWeightAt(influence.MaximumX,influence.MaximumZ)>=.999999&&
                baked.FineWeightAt(influence.MinimumX,influence.MaximumZ)>=.999999&&baked.FineWeightAt(influence.MaximumX,influence.MinimumZ)>=.999999;
        }
        void QueueAhead(WorldPoint observer,Vector3 heading)
        {
            if(work.Count>=2)return;
            for(int attempt=0;attempt<6&&work.Count<2;attempt++)
            {
                int side=attempt%3==0?0:attempt%3==1?-1:1;
                double distance=LookAheadMeters+(attempt/3)*TileMeters;
                double px=observer.X+heading.x*distance-heading.z*side*TileMeters*.8,pz=observer.Z+heading.z*distance+heading.x*side*TileMeters*.8;
                long gx=(long)Math.Floor(px/TileMeters),gz=(long)Math.Floor(pz/TileMeters);string key=gx+"/"+gz+"/"+TileMeters.ToString(System.Globalization.CultureInfo.InvariantCulture);
                if(WorldField.ContainsPatch(key)||work.Exists(item=>item.Key==key)||retries.TryGetValue(key,out float until)&&Time.unscaledTime<until)continue;
                var core=new ExplorationRefinementBounds(gx*TileMeters,gz*TileMeters,(gx+1)*TileMeters,(gz+1)*TileMeters);
                var required=core.Expanded(BlendMeters+SurfaceProfile.SampleRadiusMax+10);
                if(required.MinimumX< -baked.WorldWidthMeters*.5||required.MaximumX>baked.WorldWidthMeters*.5||required.MinimumZ< -baked.WorldDepthMeters*.5||required.MaximumZ>baked.WorldDepthMeters*.5||FullyBaked(core))continue;
                if(core.Expanded(BlendMeters+QueryAndPropGuardMeters).DistanceSquared(observer.X,observer.Z)<=ProtectedRadiusMeters*ProtectedRadiusMeters)continue;
                var request=new Work{Key=key,Core=core};request.Task=FetchCopy(core,key,required);work.Add(request);
            }
            // Retry metadata is finite even during long exploration flights.
            if(retries.Count>64)retries.Clear();
        }
        async Task<ExplorationRefinementPatch> FetchCopy(ExplorationRefinementBounds core,string key,ExplorationRefinementBounds required)
        {
            RemoteTerrainField client=remote;string id=baked.WorldId;
            await client.PrefetchAsync(required.MinimumX,required.MinimumZ,required.MaximumX,required.MaximumZ);
            IWorldField snapshot=client.ReadySnapshot(required.MinimumX,required.MinimumZ,required.MaximumX,required.MaximumZ);
            double blend=BlendMeters;
            return await Task.Run(()=>ExplorationRefinementPatch.CopySnapshot(snapshot,key,id,core,blend,SurfaceProfile.SampleRadiusMax,5));
        }
        void StopClient()
        {
            foreach(var item in work)ObserveFailure(item.Task);work.Clear();ObserveFailure(connecting);connecting=null;
            if(remote!=null)remote.Dispose();remote=null;accepted=false;
        }
        static void ObserveFailure(Task task)
        {if(task!=null)task.ContinueWith(completed=>{var observed=completed.Exception;},TaskContinuationOptions.OnlyOnFaulted);}
        void OnDestroy(){StopClient();}
    }
}
