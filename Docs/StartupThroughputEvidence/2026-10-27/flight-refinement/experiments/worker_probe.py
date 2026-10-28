from pathlib import Path
import json,shutil,hashlib,subprocess,sys
D=Path(__file__).resolve().parent;ROOT=D.parents[2]
out=D/'worker-probe';out.mkdir(exist_ok=False)
program=(ROOT/'Tools/ProgressiveLod/FlightRouteProgram.cs').read_text()
program=program.replace('    static string phase="setup";','    internal static volatile string phase="setup";')
program=program.replace('maxScratch,travelDeferredTicks,results=Results,','maxScratch,travelDeferredTicks,results=Results,worker_samples=FlightWorkerProbe.Rows.ToArray(),')
program+='''
static class FlightWorkerProbe
{
    internal sealed class Row
    {public string phase{get;set;}public string key{get;set;}public bool contact{get;set;}public int columns{get;set;}public bool completed{get;set;}public bool cancellation_requested{get;set;}public double worker_wall_ms{get;set;}}
    internal static readonly System.Collections.Concurrent.ConcurrentQueue<Row> Rows=new System.Collections.Concurrent.ConcurrentQueue<Row>();
    internal sealed class Measure:IDisposable
    {
        readonly Row row;readonly System.Threading.CancellationToken cancel;readonly Stopwatch watch=Stopwatch.StartNew();
        internal Measure(LodTile tile,int n,bool contact,System.Threading.CancellationToken cancel)
        {this.cancel=cancel;row=new Row{phase=Program.phase,key=tile.Key.ToString(),contact=contact,columns=n};}
        internal void Complete(){row.completed=true;}
        public void Dispose(){row.worker_wall_ms=watch.Elapsed.TotalMilliseconds;row.cancellation_requested=cancel.IsCancellationRequested;Rows.Enqueue(row);}
    }
    internal static Measure Begin(LodTile tile,int n,bool contact,System.Threading.CancellationToken cancel)=>new Measure(tile,n,contact,cancel);
}
'''
path=out/'WorkerFlightRouteProgram.cs';path.write_text(program,encoding='utf-8')
reports=[]
for variant in ['baseline','altitude','flight-v3']:
    overlay=out/variant;shutil.copytree(D/variant,overlay)
    p=overlay/'Assets/AshenVoxel/WorldScaleRuntime/WorldExplorerVoxels.cs';text=p.read_text();before=hashlib.sha256(p.read_bytes()).hexdigest()
    old='                var clock=System.Diagnostics.Stopwatch.StartNew();'
    assert text.count(old)==1;text=text.replace(old,old+'\n                using var probe=FlightWorkerProbe.Begin(tile,n,contact,cancel);')
    old='workerMilliseconds=clock.Elapsed.TotalMilliseconds;return data;'
    assert text.count(old)==1;text=text.replace(old,'workerMilliseconds=clock.Elapsed.TotalMilliseconds;probe.Complete();return data;')
    p.write_text(text,encoding='utf-8',newline='\n')
    (overlay/'probe-identity.json').write_text(json.dumps({'variant':variant,'original_sha256':before,'instrumented_sha256':hashlib.sha256(p.read_bytes()).hexdigest(),
       'scope':'Two common timing hooks around actual geometry worker only. Not deployable; depends on fixture class. Worker wall duration is not CPU utilization or game latency.'},indent=2)+'\n')
    prior=set((ROOT/'.terrain-cache/progressive-lod/runs').glob('*WorkerFlightRouteProgram'))
    r=subprocess.run([sys.executable,'-X','utf8',str(ROOT/'Tools/ProgressiveLod/run_checks.py'),'--production','--overlay-dir',str(overlay),'--program',str(path)],cwd=ROOT,text=True,stdout=subprocess.PIPE,stderr=subprocess.STDOUT)
    (out/f'{variant}.log').write_text(r.stdout,encoding='utf-8')
    made=set((ROOT/'.terrain-cache/progressive-lod/runs').glob('*WorkerFlightRouteProgram'))-prior;assert len(made)==1
    run=made.pop();data=json.loads((run/'result.json').read_text());reports.append({'variant':variant,'run':run.name,'status':data['status']})
    (out/'runs.json').write_text(json.dumps(reports,indent=2)+'\n');print(variant+': '+data['status']+' '+run.name,flush=True)
    if r.returncode:raise SystemExit(r.returncode)
