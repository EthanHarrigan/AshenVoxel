using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using AshenVoxel.WorldScale;
using AshenVoxel.WorldScaleRuntime;
using UnityEngine;

namespace AshenVoxel.Exploration
{
    /// <summary>One background learned overview request. Source passes and mesh
    /// passes are independent; the baked world remains available during inference.</summary>
    public sealed class ExplorationOverviewRefinement:MonoBehaviour
    {
        [Serializable] public sealed class Request
        {public string native_model_world_id;public int spacing_m,row0,col0,rows,cols;}
        [Serializable] public sealed class Metadata
        {
            public string schema,source_id,native_model_world_id,height_sha256,height_units;
            public int spacing_m,row0,col0,rows,cols;
            public double origin_x_m,origin_z_m;
        }
        [Serializable] sealed class ProviderState {public bool ready=false;public string source_id=null,native_model_world_id=null;}
        [Serializable] sealed class JobState {public string request_id=null,source_id=null,state=null,error=null;public int completed_rows=0;}
        [Serializable] sealed class BakedIdentity {public string generator_version=null,native_model_world_id=null;}
        const int MaximumResponseBytes=131072;
        HttpClient client;Uri endpoint;CancellationTokenSource cancel;
        Task<ExplorationOverviewPatch> request;ExplorationOverviewPatch completed;
        ExplorationAtlasField baked;WorldExplorer vista;
        ExplorationOverviewDemand.Plan plan;ExplorationRefinementBounds bounds;
        double nextPlan,nextRequest;int planRevision=-1;bool stopped;
        string networkStatus="Waiting for distant coverage";
        public ExplorationOverviewField WorldField {get;private set;}
        public string Status {get;private set;}="Overview source not configured";
        public int PassSpacingMeters=>plan?.PassSpacingMeters??160;
        public int CompletedCells=>plan?.CompletedCells??0;
        public int DesiredCells=>plan?.TotalCells??0;
        public bool Pending=>request!=null||completed!=null;
        public event Action<ExplorationRefinementBounds> RegionChanged;

        public IWorldField Configure(ExplorationAtlasField source,ExplorationRefinementField live,WorldExplorer renderer,string url)
        {
            baked=source;vista=renderer;
            var identity=JsonUtility.FromJson<BakedIdentity>(source.Macro.SourceIdentity);
            if(identity==null||identity.generator_version!="learned-lowfreq-overview-v1"||!Hash(identity.native_model_world_id))
            {Status="Far source refinement requires the checked 320 m learned overview";return live;}
            if(!Uri.TryCreate(url,UriKind.Absolute,out endpoint)||!endpoint.IsLoopback||endpoint.Scheme!="http"||!string.IsNullOrEmpty(endpoint.UserInfo))
                throw new ArgumentException("Overview provider must be a local HTTP endpoint");
            WorldField=new ExplorationOverviewField(source,live,identity.native_model_world_id);
            bounds=new ExplorationRefinementBounds(-source.WorldWidthMeters*.5,-source.WorldDepthMeters*.5,source.WorldWidthMeters*.5,source.WorldDepthMeters*.5);
            if(Math.Abs(source.WorldWidthMeters-source.OriginalWidthMeters)>.001||Math.Abs(source.WorldDepthMeters-source.OriginalDepthMeters)>.001||Math.Abs(source.VerticalScale-1)>1e-7)
            {Status="Overview decoding requires original map dimensions and vertical scale 1";WorldField=null;return live;}
            client=new HttpClient{Timeout=TimeSpan.FromSeconds(20)};cancel=new CancellationTokenSource();Status="320 m base ready; far 160 m source pass pending";
            return WorldField;
        }
        public void RefreshBaseSnapshot()=>WorldField?.RefreshBaseSnapshot();
        public void Tick(WorldPoint observer,double radiusMeters,bool enabled,bool fineBusy,bool matterOwned)
        {
            if(stopped||WorldField==null)return;
            double now=Time.unscaledTimeAsDouble;
            if(request!=null&&request.IsCompleted)
            {
                var done=request;request=null;
                try{completed=done.GetAwaiter().GetResult();}
                catch(Exception error){networkStatus="Overview paused: "+error.GetBaseException().Message;nextRequest=now+30;}
            }
            if(!enabled){if(request!=null)cancel.Cancel();Status="Far source refinement paused; retained terrain remains visible";return;}
            if(now>=nextPlan||planRevision!=WorldField.Revision)
            {
                nextPlan=now+2;planRevision=WorldField.Revision;
                var coverage=WorldField.CapturePublishedCoverage();
                // Retire at most one distant unprotected patch per planning tick.
                foreach(var p in coverage)if(p.InfluenceBounds.DistanceSquared(observer.X,observer.Z)>Math.Pow(radiusMeters+5120,2))
                    if(!matterOwned&&WorldField.TryRetire(p.CellX,p.CellZ,observer,out var retired,out _)){Changed(retired);coverage=WorldField.CapturePublishedCoverage();break;}
                plan=ExplorationOverviewDemand.Build(observer,radiusMeters,bounds,coverage);
            }
            if(completed!=null&&!matterOwned&&!vista.VoxelMemoryPressureBlocked)
            {
                bool wanted=false;foreach(var cell in plan.Desired)if(cell.CellX==completed.CellX&&cell.CellZ==completed.CellZ){wanted=true;break;}
                if(!wanted)completed=null;
                else if(WorldField.TryPublish(completed,observer,out var changed,out string reason))
                {completed=null;Changed(changed);nextPlan=0;networkStatus="New learned heights published";}
                else networkStatus="Completed source held: "+reason;
            }
            if(request==null&&completed==null&&now>=nextRequest&&plan.NextRequest.HasValue)
            {
                if(!vista.HasActiveCoverage)networkStatus="Waiting for complete baked distant coverage";
                else if(fineBusy)networkStatus="Nearby 5 m decoding has priority";
                else if(matterOwned)networkStatus="Source publication held during matter session";
                else if(vista.VoxelMemoryPressureBlocked)networkStatus="Waiting for terrain memory headroom";
                else
                {
                    var cell=plan.NextRequest.Value;int spacing=plan.PassSpacingMeters;
                    if(cancel.IsCancellationRequested){cancel.Dispose();cancel=new CancellationTokenSource();}
                    networkStatus="Requesting learned "+spacing+" m region";nextRequest=now+1;
                    request=RequestPatchAsync(client,endpoint,WorldField.NativeModelWorldId,cell.CellX,cell.CellZ,spacing,
                        value=>Volatile.Write(ref networkStatus,value),cancel.Token);
                }
            }
            Status=(plan.PassSpacingMeters==0?"Far learned source passes complete through 40 m":
                "Far learned "+plan.PassSpacingMeters+" m: "+plan.CompletedCells+" / "+plan.TotalCells+" regions")+". "+Volatile.Read(ref networkStatus);
        }
        void Changed(ExplorationRefinementBounds region)
        {vista.RefreshExternalRegion(region.MinimumX,region.MinimumZ,region.MaximumX,region.MaximumZ);RegionChanged?.Invoke(region);}
        static bool Hash(string value)=>value!=null&&Regex.IsMatch(value,"\\A[0-9a-f]{64}\\z");
        public static Request MakeRequest(string nativeId,long cellX,long cellZ,int spacing)
        {
            if(!Hash(nativeId)||(spacing!=160&&spacing!=80&&spacing!=40))throw new ArgumentException("Checked model identity and 160/80/40 m spacing required");
            return new Request{native_model_world_id=nativeId,spacing_m=spacing,row0=checked((int)((cellZ*2560-320)/spacing)),col0=checked((int)((cellX*2560-320)/spacing)),rows=3200/spacing+1,cols=3200/spacing+1};
        }
        public static ExplorationOverviewPatch Decode(byte[] raw,Request expected,long cellX,long cellZ,string sourceId)
        {
            if(expected==null)throw new ArgumentNullException(nameof(expected));
            var canonical=MakeRequest(expected.native_model_world_id,cellX,cellZ,expected.spacing_m);
            if(expected.row0!=canonical.row0||expected.col0!=canonical.col0||expected.rows!=canonical.rows||expected.cols!=canonical.cols)
                throw new InvalidDataException("Overview request does not match the destination core");
            if(raw==null||raw.Length<16||raw.Length>MaximumResponseBytes)throw new InvalidDataException("Overview payload size");
            uint stored=BitConverter.ToUInt32(raw,raw.Length-4);
            if(Encoding.ASCII.GetString(raw,0,8)!="AVOV0001"||AtlasIO.Crc32(raw,raw.Length-4)!=stored)throw new InvalidDataException("Overview checksum/magic");
            int n=BitConverter.ToInt32(raw,8);if(n<2||n>16384||n>raw.Length-16)throw new InvalidDataException("Overview metadata size");
            var meta=JsonUtility.FromJson<Metadata>(Encoding.UTF8.GetString(raw,12,n));
            if(meta==null||meta.schema!="ashen-regional-learned-overview-v1"||meta.source_id!=sourceId||!Hash(sourceId)||
                meta.native_model_world_id!=expected.native_model_world_id||meta.spacing_m!=expected.spacing_m||meta.row0!=expected.row0||meta.col0!=expected.col0||
                meta.rows!=expected.rows||meta.cols!=expected.cols||meta.height_units!="meters"||meta.origin_x_m!=(double)expected.col0*expected.spacing_m||meta.origin_z_m!=(double)expected.row0*expected.spacing_m||
                raw.Length!=16+n+expected.rows*expected.cols*4)throw new InvalidDataException("Overview identity/window differs from request");
            using(var sha=SHA256.Create())if(BitConverter.ToString(sha.ComputeHash(raw,12+n,raw.Length-n-16)).Replace("-","").ToLowerInvariant()!=meta.height_sha256)
                throw new InvalidDataException("Overview height hash");
            var heights=new float[expected.rows*expected.cols];Buffer.BlockCopy(raw,12+n,heights,0,heights.Length*4);
            return new ExplorationOverviewPatch(cellX,cellZ,expected.spacing_m,heights,expected.native_model_world_id);
        }
        static async Task<byte[]> Read(HttpResponseMessage response,CancellationToken token)
        {
            if(response.Content.Headers.ContentLength>MaximumResponseBytes)throw new InvalidDataException("Overview response exceeds limit");
            using(var stream=await response.Content.ReadAsStreamAsync().ConfigureAwait(false))using(var output=new MemoryStream())
            {var bytes=new byte[4096];int n;while((n=await stream.ReadAsync(bytes,0,bytes.Length,token).ConfigureAwait(false))>0){if(output.Length+n>MaximumResponseBytes)throw new InvalidDataException("Overview response exceeds limit");output.Write(bytes,0,n);}return output.ToArray();}
        }
        public static async Task<ExplorationOverviewPatch> RequestPatchAsync(HttpClient http,Uri endpoint,string nativeId,long cellX,long cellZ,int spacing,Action<string> status,CancellationToken token)
        {
            var expected=MakeRequest(nativeId,cellX,cellZ,spacing);string job=null;bool finished=false;
            var root=new Uri(endpoint.AbsoluteUri.TrimEnd('/')+"/");
            try
            {
                string source;
                using(var response=await http.GetAsync(new Uri(root,"status?spacing="+spacing.ToString(System.Globalization.CultureInfo.InvariantCulture)),HttpCompletionOption.ResponseHeadersRead,token).ConfigureAwait(false))
                {
                    response.EnsureSuccessStatusCode();var state=JsonUtility.FromJson<ProviderState>(Encoding.UTF8.GetString(await Read(response,token).ConfigureAwait(false)));
                    if(state==null||!state.ready||state.native_model_world_id!=nativeId||!Hash(state.source_id))throw new InvalidDataException("Overview provider not ready or model identity differs");source=state.source_id;
                }
                using(var body=new StringContent(JsonUtility.ToJson(expected),Encoding.UTF8,"application/json"))
                using(var response=await http.PostAsync(new Uri(root,"overview"),body,token).ConfigureAwait(false))
                {
                    response.EnsureSuccessStatusCode();var state=JsonUtility.FromJson<JobState>(Encoding.UTF8.GetString(await Read(response,token).ConfigureAwait(false)));
                    if(state==null||!Hash(state.request_id)||state.source_id!=source)throw new InvalidDataException("Invalid overview receipt");job=state.request_id;
                }
                for(int polls=0;polls<1800;polls++)
                {
                    await Task.Delay(polls==0?60:polls<10?250:1000,token).ConfigureAwait(false); // pre-baked cache hits answer at once
                    using(var response=await http.GetAsync(new Uri(root,"overview/"+job),HttpCompletionOption.ResponseHeadersRead,token).ConfigureAwait(false))
                    {
                        response.EnsureSuccessStatusCode();var bytes=await Read(response,token).ConfigureAwait(false);
                        if(response.StatusCode==HttpStatusCode.OK){var result=Decode(bytes,expected,cellX,cellZ,source);finished=true;return result;}
                        if(response.StatusCode!=HttpStatusCode.Accepted)throw new InvalidDataException("Unexpected overview status");
                        var state=JsonUtility.FromJson<JobState>(Encoding.UTF8.GetString(bytes));
                        if(state==null||state.request_id!=job||state.state=="FAILED"||state.state=="CANCELLED")throw new InvalidDataException(state?.error??"Overview request failed");
                        status?.Invoke("Overview "+state.state+": "+state.completed_rows+" / "+expected.rows+" rows");
                    }
                }
                throw new TimeoutException("Overview request exceeded 30 minutes; baked terrain retained");
            }
            finally
            {
                if(job!=null&&!finished)try
                {using(var cleanup=new CancellationTokenSource(TimeSpan.FromSeconds(3)))using(await http.DeleteAsync(new Uri(root,"overview/"+job),cleanup.Token).ConfigureAwait(false)){} }
                catch(Exception){/* Server admission remains single-job and bounded. */}
            }
        }
        public void Shutdown()
        {
            if(stopped)return;stopped=true;cancel?.Cancel();
            if(request!=null)request.ContinueWith(t=>{if(t.IsFaulted){var observed=t.Exception;}client?.Dispose();cancel?.Dispose();},TaskScheduler.Default);
            else{client?.Dispose();cancel?.Dispose();}completed=null;
        }
        void OnDestroy()=>Shutdown();
    }
}
