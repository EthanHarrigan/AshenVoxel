from pathlib import Path
import datetime, hashlib, json, shutil, statistics

D=Path(__file__).resolve().parent; ROOT=D.parents[2]
E=ROOT/'Docs/StartupThroughputEvidence/2026-10-27'
ARCH=E/'flight-refinement'; OUT=ARCH/'final-checkpoint'
assert not OUT.exists(), 'Preserve previous checkpoint'
def sha(p): return hashlib.sha256(p.read_bytes()).hexdigest()
def copy(src,dst):
    dst.parent.mkdir(parents=True,exist_ok=True); shutil.copyfile(src,dst)
    assert sha(src)==sha(dst)
def save(p,data):
    p.parent.mkdir(parents=True,exist_ok=True)
    p.write_text(json.dumps(data,indent=2)+'\n',encoding='utf-8')

manifest=json.loads((D/'flight-final/manifest.json').read_text())
for f in manifest['files']:
    assert sha(ROOT/f['path'])==f['before'], f['path']
    assert sha(D/'flight-final'/f['path'])==f['sha256']
for folder in ['flight-final','final-comparison']:
    for p in (D/folder).rglob('*'):
        if p.is_file(): copy(p,OUT/folder/p.relative_to(D/folder))
for name in ['archive_final.py','compare_final.py','fix_vertical_arrival.py']:
    copy(D/name,OUT/'scripts'/name)
for name in ['FlightRouteProgram.cs','FlightPolicyProgram.cs','FlightControllerProgram.cs','ContactReplanFlightProgram.cs','run_checks.py']:
    copy(ROOT/'Tools/ProgressiveLod'/name,OUT/'fixtures'/name)

baseHashes={p.relative_to(ARCH/'baseline-source').as_posix():sha(p) for p in (ARCH/'baseline-source').rglob('*') if p.is_file()}
reports=[]
for run in sorted((ROOT/'.terrain-cache/progressive-lod/runs').iterdir()):
    if not (run/'result.json').exists() or (ARCH/'checks'/run.name).exists(): continue
    data=json.loads((run/'result.json').read_text())
    if 'flight-refinement-1' not in (data.get('overlay_dir') or ''): continue
    dst=OUT/'checks'/run.name
    for name in ['result.json','console.txt','compile.rsp']:
        if (run/name).exists(): copy(run/name,dst/name)
    overrides=[]
    for rel,digest in data['source_sha256'].items():
        if baseHashes.get(rel)==digest: continue
        assert sha(run/'source'/rel)==digest
        copy(run/'source'/rel,dst/'source'/rel); overrides.append(rel)
    save(dst/'archive-source-layout.json',{'base_snapshot':'../../../baseline-source','overrides':'source','overridden_files':overrides,'all_hashes':'result.json/source_sha256'})
    reports.append({'run':run.name,'variant':data.get('overlay_dir'),'status':data['status'],'checks':data.get('checks'),'assemblies':data.get('assemblies')})

legs={'fast outbound','fast reverse return','immediate direction reversal','pre-descent transit'}
paired=[]
runs=json.loads((D/'final-comparison/runs.json').read_text()); assert len(runs)==4 and all(r['status']=='PASS' for r in runs)
for r in runs:
    data=json.loads((ROOT/'.terrain-cache/progressive-lod/runs'/r['run']/'result.json').read_text())
    jobs=[j for j in data['worker_samples'] if j['phase'] in legs]
    pubs=[s for s in data['results'] if s['phase'] in legs]
    paired.append({**r,'total_harness_wall_ms':data['wall_ms'], 'travel_geometry_jobs':len(jobs),
        'travel_contact_jobs':sum(j['contact'] for j in jobs),'travel_geometry_incomplete':sum(not j['completed'] for j in jobs),
        'travel_summed_worker_wall_ms':sum(j['worker_wall_ms'] for j in jobs),
        'far_publications_during_fast_legs':sum(sum(s['far_by_columns'].values()) for s in pubs),
        'contact_publications_during_fast_legs':sum(s['contacts'] for s in pubs),
        'expensive_publications_retired_by_destination':sum(s['expensive_retired_by_end'] for s in pubs),
        'far_by_columns':{str(n):sum(s['far_by_columns'].get(str(n),0) for s in pubs) for n in [8,32,64,128,256]},
        'max_far_tiles':data['maxFar'],'max_contact_tiles':data['maxContact'],'max_workers':data['maxWorkers'],'max_uploads_per_tick':data['maxUploads'],
        'max_far_payload_bytes':data['maxFarBytes'],'max_contact_payload_bytes':data['maxContactBytes'],'max_scratch_bytes':data['maxScratch'],
        'recoveries':[s for s in data['results'] if 'recovery_virtual_ticks' in s]})
baseline=[r for r in paired if r['variant']=='baseline']; final=[r for r in paired if r['variant']=='flight-final']
mean=lambda rows,key:statistics.mean(r[key] for r in rows)
comparison={'baseline_travel_worker_wall_ms_mean':mean(baseline,'travel_summed_worker_wall_ms'),
    'final_travel_worker_wall_ms_mean':mean(final,'travel_summed_worker_wall_ms'),
    'travel_worker_wall_reduction_percent':100*(1-mean(final,'travel_summed_worker_wall_ms')/mean(baseline,'travel_summed_worker_wall_ms')),
    'baseline_harness_wall_ms_mean':mean(baseline,'total_harness_wall_ms'),'final_harness_wall_ms_mean':mean(final,'total_harness_wall_ms')}
limits=json.loads((ARCH/'summary.json').read_text())['limits']
summary={'utc':datetime.datetime.now(datetime.timezone.utc).isoformat(),
    'status':'flight-final CPU candidate; NOT INSTALLED; supersedes flight-v4 checkpoint only for this exact source',
    'candidate':manifest,'main_runtime_preimages_unchanged':True,'validation':reports,'paired_routes':paired,'comparison':comparison,
    'decision':'Candidate merits controlled native flight review. No automatic installation. Keep exact-source native compile, real mountainous flight/arrival recovery, visual quality, frame and memory gates open.',
    'v4_regression':{'run':'20261027-121035-494461-production-overlay-ContactReplanFlightProgram','status':'FAIL','cause':'Same-XZ vertical descent did not preempt far work because contact-plan early return skipped urgency reevaluation.','fixed_run':'20261027-121136-258454-production-overlay-ContactReplanFlightProgram','fix':'Retain same-center fast path but reevaluate existing contact urgency and cancel/requeue current distant work if urgent.','prior_summary':'../summary.json remains historical pre-regression evidence, not adoption approval'},
    'policy':{'entry_mps':128,'exit_mps':96,'hysteresis_seconds':0.25,'stationary_exit':'Immediate below1m/s','reverse_exit':'Immediate,0.5s recovery hold','descent_exit_mps':-12,'altitude_margin_m':32.1,'during_transit_new_far_column_limit':32,'new_queues_caches_workers':0,'matter_resolution_m':0.1},
    'limits':limits,'coordinator_native_smoke':'../../coordinator-smoke/bounded-result.json; separate installed startup-budget profile, NOT this candidate'}
save(OUT/'summary.json',summary)
copy(Path('C:/Users/ethan/Documents/Codex/2026-10-27/task-3/main-smoke/startup-breakdown.json'),OUT/'coordinator-startup-breakdown.json')
save(ARCH/'current-status.json',{'utc':summary['utc'],'status':summary['status'],'current':'final-checkpoint/summary.json','prior':'summary.json','prior_candidate_rejected':'flight-v4 same-XZ descent regression; preserved failure in final checkpoint','installed':False})
print(json.dumps({'archive':str(OUT),'comparison':comparison,'runs':len(reports),'paired_routes':[{k:v for k,v in r.items() if k!='recoveries'} for r in paired]},indent=2))
