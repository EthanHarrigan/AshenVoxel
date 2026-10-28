from pathlib import Path
import hashlib,json,subprocess,shutil,sys
D=Path(__file__).resolve().parent;ROOT=D.parents[2];out=D/'retention-comparison';out.mkdir(exist_ok=False)
program=out/'RetentionWorkerRouteProgram.cs';shutil.copyfile(D/'worker-probe/WorkerFlightRouteProgram.cs',program)
for variant in ['altitude-retain','flight-v4']:
    overlay=out/variant;shutil.copytree(D/variant,overlay);p=overlay/'Assets/AshenVoxel/WorldScaleRuntime/WorldExplorerVoxels.cs'
    text=p.read_text();before=hashlib.sha256(p.read_bytes()).hexdigest()
    a='                var clock=System.Diagnostics.Stopwatch.StartNew();';b='workerMilliseconds=clock.Elapsed.TotalMilliseconds;return data;'
    assert text.count(a)==text.count(b)==1
    text=text.replace(a,a+'\n                using var probe=FlightWorkerProbe.Begin(tile,n,contact,cancel);').replace(b,'workerMilliseconds=clock.Elapsed.TotalMilliseconds;probe.Complete();return data;')
    p.write_text(text,encoding='utf-8',newline='\n')
    (overlay/'probe-identity.json').write_text(json.dumps({'variant':variant,'original_sha256':before,'instrumented_sha256':hashlib.sha256(p.read_bytes()).hexdigest(),'scope':'Common geometry-worker timing hooks only; not deployable'},indent=2)+'\n')
reports=[]
for repeat in range(2):
    for variant in (['altitude-retain','flight-v4'] if repeat==0 else ['flight-v4','altitude-retain']):
        prior=set((ROOT/'.terrain-cache/progressive-lod/runs').glob('*RetentionWorkerRouteProgram'))
        r=subprocess.run([sys.executable,'-X','utf8',str(ROOT/'Tools/ProgressiveLod/run_checks.py'),'--production','--overlay-dir',str(out/variant),'--program',str(program)],cwd=ROOT,text=True,stdout=subprocess.PIPE,stderr=subprocess.STDOUT)
        (out/f'{repeat}-{variant}.txt').write_text(r.stdout,encoding='utf-8')
        made=set((ROOT/'.terrain-cache/progressive-lod/runs').glob('*RetentionWorkerRouteProgram'))-prior;assert len(made)==1
        run=made.pop();data=json.loads((run/'result.json').read_text());reports.append({'repeat':repeat,'variant':variant,'run':run.name,'status':data['status']})
        (out/'runs.json').write_text(json.dumps(reports,indent=2)+'\n');print(str(repeat)+' '+variant+': '+data['status']+' '+run.name,flush=True)
        if r.returncode:raise SystemExit(r.returncode)
