from pathlib import Path
import datetime,json,subprocess,sys
D=Path(__file__).resolve().parent;ROOT=D.parents[2]
out=D/'route-comparison';out.mkdir(exist_ok=False)
rows=[]
for repeat in range(2):
    order=['baseline','altitude','flight-v3'] if repeat==0 else ['flight-v3','altitude','baseline']
    for variant in order:
        before=set((ROOT/'.terrain-cache/progressive-lod/runs').glob('*FlightRouteProgram'))
        command=[sys.executable,'-X','utf8',str(ROOT/'Tools/ProgressiveLod/run_checks.py'),'--production','--overlay-dir',str(D/variant),'--program',str(ROOT/'Tools/ProgressiveLod/FlightRouteProgram.cs')]
        result=subprocess.run(command,cwd=ROOT,stdout=subprocess.PIPE,stderr=subprocess.STDOUT,text=True)
        (out/f'{repeat}-{variant}.txt').write_text(result.stdout,encoding='utf-8')
        created=set((ROOT/'.terrain-cache/progressive-lod/runs').glob('*FlightRouteProgram'))-before
        assert len(created)==1,created
        run=created.pop();data=json.loads((run/'result.json').read_text())
        row={'repeat':repeat,'variant':variant,'run':run.name,'status':data['status'],'returncode':result.returncode,'wall_ms':data.get('wall_ms'),'results':data.get('results')}
        rows.append(row);(out/'runs.json').write_text(json.dumps(rows,indent=2)+'\n')
        print(f'{repeat} {variant}: {data["status"]} {data.get("wall_ms",0)/1000:.2f}s -> {run.name}',flush=True)
        if result.returncode:raise SystemExit(result.returncode)
print('PASS all six bounded route comparisons',flush=True)
