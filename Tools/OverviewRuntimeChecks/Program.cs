using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using AshenVoxel.Exploration;
static class Program
{
    const string Native="aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",Source="bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb",Job="cccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc";
    static readonly Uri Endpoint=new Uri("http://127.0.0.1:1/unit/");static int checks,failures;static string fixtures;
    static void Check(bool ok,string message){checks++;if(!ok)failures++;Console.WriteLine((ok?"PASS ":"FAIL ")+message);}
    static byte[] Fixture(string name)=>File.ReadAllBytes(Path.Combine(fixtures,name+".bin"));
    static void Reject(Action action,string message)
    {try{action();Check(false,message);}catch(Exception error)when(error is InvalidDataException||error is ArgumentException||error is JsonException||error is OverflowException){Check(true,message);}}
    sealed class Handler:HttpMessageHandler
    {
        public readonly List<string> Calls=new List<string>();public readonly string Mode;public int Polls,Deletes;public bool PostWindowValid,CleanupTokenIndependent;
        public Handler(string mode){Mode=mode;}
        static HttpResponseMessage Json(HttpStatusCode code,object value)=>new HttpResponseMessage(code){Content=new StringContent(JsonSerializer.Serialize(value),Encoding.UTF8,"application/json")};
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)
        {
            string path=request.RequestUri.AbsolutePath;Calls.Add(request.Method+" "+path);
            if(request.Method==HttpMethod.Delete){Deletes++;CleanupTokenIndependent=!token.IsCancellationRequested;return new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent("{}")};}
            if(path.EndsWith("/status",StringComparison.Ordinal))
            {
                if(Mode=="status-cancel")await Task.Delay(Timeout.Infinite,token);
                if(Mode=="http-error")return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable){Content=new StringContent("unavailable")};
                var state=Json(HttpStatusCode.OK,new{ready=Mode!="not-ready",source_id=Source,native_model_world_id=Mode=="wrong-model"?Job:Native});
                if(Mode=="oversize-header")state.Content.Headers.ContentLength=65537;
                if(Mode=="oversize-stream")state.Content=new UnknownLengthContent(new byte[70000]);
                return state;
            }
            if(request.Method==HttpMethod.Post)
            {
                using var document=JsonDocument.Parse(await request.Content.ReadAsStringAsync());var body=document.RootElement;
                PostWindowValid=body.GetProperty("native_model_world_id").GetString()==Native&&body.GetProperty("spacing_m").GetInt32()==160&&body.GetProperty("row0").GetInt32()==62&&body.GetProperty("col0").GetInt32()==-50&&body.GetProperty("rows").GetInt32()==21&&body.GetProperty("cols").GetInt32()==21&&request.Content.Headers.ContentType.MediaType=="application/json";
                return Json(HttpStatusCode.Accepted,new{request_id=Mode=="bad-job-receipt"?"bad":Job,source_id=Mode=="bad-source-receipt"?Job:Source,state="QUEUED"});
            }
            Polls++;
            if(Mode=="wrong-job")return Json(HttpStatusCode.Accepted,new{request_id=Source,state="RUNNING",completed_rows=4});
            if(Mode=="failed")return Json(HttpStatusCode.Accepted,new{request_id=Job,state="FAILED",error="Controlled provider failure",completed_rows=0});
            if(Mode=="canceled")return Json(HttpStatusCode.Accepted,new{request_id=Job,state="CANCELLED",error="Controlled provider cancellation",completed_rows=0});
            if(Mode=="poll-http-error")return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable){Content=new StringContent("offline")};
            if(Mode=="success"&&Polls==1)return Json(HttpStatusCode.Accepted,new{request_id=Job,state="RUNNING",completed_rows=4}); // Protocol does not require202 source_id.
            return new HttpResponseMessage(HttpStatusCode.OK){Content=new ByteArrayContent(Fixture(Mode=="wrong-final-source"?"wrong-source":"valid160"))};
        }
    }
    sealed class UnknownLengthContent:HttpContent
    {
        readonly byte[] bytes;public UnknownLengthContent(byte[] value){bytes=value;}
        protected override bool TryComputeLength(out long length){length=0;return false;}
        protected override Task SerializeToStreamAsync(Stream stream,TransportContext context)=>stream.WriteAsync(bytes,0,bytes.Length);
    }
    static async Task Failure(string mode,bool cleanup)
    {
        var handler=new Handler(mode);using var http=new HttpClient(handler);bool rejected=false;
        try{await ExplorationOverviewRefinement.RequestPatchAsync(http,Endpoint,Native,-3,4,160,null,CancellationToken.None);}
        catch(Exception error)when(error is InvalidDataException||error is HttpRequestException){rejected=true;}
        Check(rejected,"HTTP "+mode+" fails without publishing a patch");
        Check(handler.Deletes==(cleanup?1:0),"HTTP "+mode+" has bounded injected cleanup ownership");
    }
    static async Task Main(string[] args)
    {
        fixtures=args[0];var expected=ExplorationOverviewRefinement.MakeRequest(Native,-3,4,160);
        Check(expected.row0==62&&expected.col0==-50&&expected.rows==21&&expected.cols==21,"MakeRequest maps negative canonical cells to complete flat160m window with320m halo");
        var fine=ExplorationOverviewRefinement.MakeRequest(Native,-3,4,80);Check(fine.row0==124&&fine.col0==-100&&fine.rows==41&&fine.cols==41,"80m window has matching physical core/halo and41 samples per axis");
        var raw=Fixture("valid160");var patch=ExplorationOverviewRefinement.Decode(raw,expected,-3,4,Source);
        Check(patch.CellX==-3&&patch.CellZ==4&&patch.SpacingMeters==160&&patch.Columns==21&&patch.PayloadBytes==1764&&Math.Abs(patch.Height(-7680,10240)-105.5)<.000001,"actual C# decoder accepts Python encoder bytes and samples their exact original physical positions");
        var finePatch=ExplorationOverviewRefinement.Decode(Fixture("valid80"),fine,-3,4,Source);Check(finePatch.Columns==41&&finePatch.PayloadBytes==6724,"Python80m fixture decodes with complete native byte payload");
        raw[raw.Length-8]^=1;Check(Math.Abs(patch.Height(-7680,10240)-105.5)<.000001,"decoded patch retains owned heights after transport bytes are changed");
        foreach(string name in new[]{"wrong-source","wrong-native","wrong-origin","wrong-schema","wrong-units","wrong-dimensions","wrong-window","nan","infinity","too-high","wrong-sha","corrupt-crc","truncated","truncated-valid-crc","wrong-magic","bad-metadata-length"})
            Reject(()=>ExplorationOverviewRefinement.Decode(Fixture(name),expected,-3,4,Source),"decode rejects "+name);
        Reject(()=>ExplorationOverviewRefinement.Decode(new byte[65537],expected,-3,4,Source),"decode rejects oversized response before allocation");
        Reject(()=>ExplorationOverviewRefinement.Decode(Fixture("valid160"),expected,-2,4,Source),"decode binds expected physical window to the requested canonical cell");
        Reject(()=>ExplorationOverviewRefinement.MakeRequest("bad",0,0,160),"unverified native model identity rejected");
        Reject(()=>ExplorationOverviewRefinement.MakeRequest(Native,0,0,320),"unsupported source spacing rejected");
        Reject(()=>ExplorationOverviewRefinement.MakeRequest(Native,long.MaxValue,0,160),"canonical request arithmetic overflow rejected");
        var success=new Handler("success");using(var http=new HttpClient(success))
        {
            var progress=new List<string>();var result=await ExplorationOverviewRefinement.RequestPatchAsync(http,Endpoint,Native,-3,4,160,progress.Add,CancellationToken.None);
            Check(result.CellX==-3&&result.CellZ==4&&success.PostWindowValid&&success.Polls==2&&success.Deletes==0,"injected HTTP status→POST→202→200 returns verified Python payload without deleting a successful job");
            Check(success.Calls.SequenceEqual(new[]{"GET /unit/status","POST /unit/overview","GET /unit/overview/"+Job,"GET /unit/overview/"+Job})&&progress.Any(value=>value.Contains("4 / 21")),"transport preserves endpoint base path and forwards bounded provider progress");
        }
        foreach(string mode in new[]{"not-ready","wrong-model","http-error","oversize-header","oversize-stream","bad-job-receipt","bad-source-receipt"})await Failure(mode,false);
        foreach(string mode in new[]{"wrong-job","failed","canceled","poll-http-error","wrong-final-source"})await Failure(mode,true);
        var canceled=new Handler("cancel-after-receipt");using(var http=new HttpClient(canceled))using(var token=new CancellationTokenSource())
        {
            token.CancelAfter(100);bool observed=false;
            try{await ExplorationOverviewRefinement.RequestPatchAsync(http,Endpoint,Native,-3,4,160,null,token.Token);}catch(OperationCanceledException){observed=true;}
            Check(observed&&canceled.PostWindowValid&&canceled.Polls==0&&canceled.Deletes==1&&canceled.CleanupTokenIndependent,"caller cancellation during poll delay cancels promptly and cleans job using independent injected token");
        }
        var early=new Handler("status-cancel");using(var http=new HttpClient(early))using(var token=new CancellationTokenSource())
        {
            token.CancelAfter(50);bool observed=false;try{await ExplorationOverviewRefinement.RequestPatchAsync(http,Endpoint,Native,-3,4,160,null,token.Token);}catch(OperationCanceledException){observed=true;}
            Check(observed&&early.Calls.Count==1&&early.Deletes==0,"cancellation before provider identity never creates or deletes a job");
        }
        File.WriteAllText(args[1],JsonSerializer.Serialize(new{status=failures==0?"PASS":"FAIL",checks,failures,scope="Actual frozen C# decoder/HTTP client with local Unity JSON substitutes, Python production wire encoder, injected HttpMessageHandler only. No real sockets, server, model, Unity or throughput measurement."},new JsonSerializerOptions{WriteIndented=true}));
        Environment.ExitCode=failures==0?0:1;
    }
}
