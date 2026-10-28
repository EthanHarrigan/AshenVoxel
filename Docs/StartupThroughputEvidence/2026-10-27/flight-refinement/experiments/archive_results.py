from pathlib import Path
import datetime,hashlib,json,shutil,statistics
D=Path(__file__).resolve().parent;ROOT=D.parents[2]
E=ROOT/'Docs/StartupThroughputEvidence/2026-10-27';OUT=E/'flight-refinement'
assert not OUT.exists(),'Preserve existing archive'
def sha(p):return hashlib.sha256(p.read_bytes()).hexdigest()
def copy(src,dst):
    dst.parent.mkdir(parents=True,exist_ok=True);shutil.copyfile(src,dst);assert sha(src)==sha(dst)
manifest=json.loads((D/'flight-v4/manifest.json').read_text())
for r in manifest['files']:
    assert sha(ROOT/r['path'])==r['before'],r['path']
    assert sha(D/'flight-v4'/r['path'])==r['sha256']
for p in D.rglob('*'):
    if p.is_file() and p.suffix in {'.cs','.py','.json','.diff','.txt','.log'}:copy(p,OUT/'experiments'/p.relative_to(D))
for name in ['FlightRouteProgram.cs','FlightPolicyProgram.cs','FlightControllerProgram.cs','ContactReplanFlightProgram.cs','stage_flight.py','run_checks.py']:
    copy(ROOT/'Tools/ProgressiveLod'/name,OUT/'fixtures'/name)
baseRun=ROOT/'.terrain-cache/progressive-lod/runs/20261027-115146-701761-production-overlay-FlightRouteProgram'
baseReport=json.loads((baseRun/'result.json').read_text());baseHashes=baseReport['source_sha256']
for p in (baseRun/'source').rglob('*'):
    if p.is_file():copy(p,OUT/'baseline-source'/p.relative_to(baseRun/'source'))
reports=[]
for run in sorted((ROOT/'.terrain-cache/progressive-lod/runs').iterdir()):
    result=run/'result.json'
    if not result.exists():continue
    data=json.loads(result.read_text())
    if 'flight-refinement-1' not in (data.get('overlay_dir') or ''):continue
    dst=OUT/'checks'/run.name
    for name in ['result.json','console.txt','compile.rsp']:
        if (run/name).exists():copy(run/name,dst/name)
    overridden=[]
    for rel,digest in data['source_sha256'].items():
        if baseHashes.get(rel)==digest:continue
        src=run/'source'/rel;assert sha(src)==digest;copy(src,dst/'source'/rel);overridden.append(rel)
    (dst/'archive-source-layout.json').write_text(json.dumps({'base_snapshot':'../../baseline-source','overrides':'source','overridden_files':overridden,'all_hashes':'result.json/source_sha256'},indent=2)+'\n')
    reports.append({'run':run.name,'variant':data.get('overlay_dir'),'status':data['status'],'checks':data.get('checks'),'assemblies':data.get('assemblies')})
legs={'fast outbound','fast reverse return','immediate direction reversal','pre-descent transit'}
route=[]
for group in ['worker-probe','retention-comparison']:
    for r in json.loads((D/group/'runs.json').read_text()):
        data=json.loads((ROOT/'.terrain-cache/progressive-lod/runs'/r['run']/'result.json').read_text())
        jobs=[s for s in data['worker_samples'] if s['phase'] in legs];pubs=[s for s in data['results'] if s['phase'] in legs]
        route.append({'group':group,**r,'total_harness_wall_ms':data['wall_ms'],'travel_geometry_jobs':len(jobs),'travel_contact_jobs':sum(s['contact'] for s in jobs),
          'travel_geometry_incomplete':sum(not s['completed'] for s in jobs),'travel_summed_worker_wall_ms':sum(s['worker_wall_ms'] for s in jobs),
          'far_publications_during_fast_legs':sum(sum(s['far_by_columns'].values()) for s in pubs),'contact_publications_during_fast_legs':sum(s['contacts'] for s in pubs),
          'expensive_publications_retired_by_destination':sum(s['expensive_retired_by_end'] for s in pubs),'far_by_columns':{str(n):sum(s['far_by_columns'].get(str(n),0) for s in pubs) for n in [8,32,64,128,256]},
          'max_far_tiles':data['maxFar'],'max_contact_tiles':data['maxContact'],'max_workers':data['maxWorkers'],'max_uploads_per_tick':data['maxUploads'],
          'max_far_payload_bytes':data['maxFarBytes'],'max_contact_payload_bytes':data['maxContactBytes'],'max_scratch_bytes':data['maxScratch'],
          'recoveries':[s for s in data['results'] if 'recovery_virtual_ticks' in s]})
narrow=[r for r in route if r['variant']=='altitude-retain'];combined=[r for r in route if r['variant']=='flight-v4']
ratio=statistics.mean(r['travel_summed_worker_wall_ms'] for r in combined)/statistics.mean(r['travel_summed_worker_wall_ms'] for r in narrow)
summary={'utc':datetime.datetime.now(datetime.timezone.utc).isoformat(),'status':'CPU-only experiments complete; flight-v4 is best tested candidate, NOT INSTALLED; native quality/flight/latency gate remains',
 'candidate':manifest,'main_runtime_preimages_unchanged':True,'validation':reports,'paired_worker_routes':route,
 'comparison':{'worker_duration_reduction_vs_narrow_retention_percent':100*(1-ratio),'new_far_publications_per_fast_route':83,
   'narrow_new_far_publications':sorted(set(r['far_publications_during_fast_legs'] for r in narrow)),
   'narrow_harness_wall_ms_mean':statistics.mean(r['total_harness_wall_ms'] for r in narrow),'combined_harness_wall_ms_mean':statistics.mean(r['total_harness_wall_ms'] for r in combined)},
 'decision':'Do not install automatically. Retention alone fixes a reproduced cancellation defect but contact churn still blocks far progress; combined v4 merits controlled native review. Preserve smaller alternative, rejected160 threshold and slower stationary-exit version.',
 'limits':['Fixed nine-tile conservative flat enclosures, real geometry/scheduling/cache paths under .NET Unity substitutes; not native mountainous routes.',
  '0.05s virtual ticks with2ms requested wall yield; Windows wait granularity and async timing differ. Neither ticks nor total harness wall time is game latency/FPS.',
  'Worker timings sum stopwatch durations including parallel sampling; not CPU utilization, full Start speedup or all wasted CPU.',
  'Instrumentation uses two common worker hooks in separate overlays; deployable candidates contain none of them.',
  'Transit defers new quality above32 columns and nonurgent contacts; retained meshes are not downgraded merely for speed. Visible new detail may remain coarse until arrival.',
  'Unknown/stale or insufficient altitude enclosures conservatively retain ordinary priority, so gains may be smaller in mountains/valleys.',
  'One concurrent FlightPolicy run failed a zero-allocation assertion without measured bytes; serialized diagnostic rerun reported0 bytes/0 queries over10000calls. Failure remains, no universal allocation guarantee.',
  'A run-directory timestamp collision prevented one test launch. A one-tile fixture lacked neighboring bounds and correctly failed retention; expanded enclosure fixture passes. Both failures preserved separately.'],
 'coordinator_native_smoke':'../coordinator-smoke/bounded-result.json; separate installed startup-budget profile, NOT this flight candidate'}
(OUT/'summary.json').write_text(json.dumps(summary,indent=2)+'\n')
(OUT/'harness-failures.json').write_text(json.dumps({'timestamp_collision':{'utc_prefix':'20261027-120142-300480','action':'parallel same-program runs collided before one launched','error':'FileExistsError WinError183','resolution':'run identical program labels sequentially; no runtime change'},
 'missing_enclosure_fixture':{'run':'20261027-120151-873498-production-overlay-ContactReplanFlightProgram','reason':'one desired tile omitted negative neighboring contact enclosures; conservative unknown-bound urgency correctly preempted far work','resolution':'fixture supplies complete3x3 enclosures; runtime unchanged'},
 'threshold_failure':{'run':'20261027-115054-270177-production-overlay-FlightControllerProgram','reason':'160m/s exact entry threshold repeatedly reset by float-time derivative at actual default boost','resolution':'separate128/96 variant'},
 'allocation_assertion':{'failed_run':'20261027-120527-310409-production-overlay-FlightPolicyProgram','diagnostic_pass':'20261027-120605-683108-production-overlay-FlightPolicyProgram','cause':'not established; exact initial byte/query deltas absent','runtime_changes_between_runs':False}},indent=2)+'\n')
print(json.dumps({'archive':str(OUT),'runs':len(reports),'comparison':summary['comparison'],'changed_files':[r for r in manifest['files'] if r['before']!=r['sha256']]},indent=2))
