from pathlib import Path
import json,hashlib,shutil,datetime
ROOT=Path(__file__).resolve().parents[2];D=ROOT/'.terrain-cache/startup-throughput';E=ROOT/'Docs/StartupThroughputEvidence/2026-10-27'
O=E/'planner-adjacency';assert not O.exists()
def sha(p):return hashlib.sha256(p.read_bytes()).hexdigest()
def copy(s,d):d.parent.mkdir(parents=True,exist_ok=True);shutil.copyfile(s,d);assert sha(s)==sha(d)
def save(p,d):p.parent.mkdir(parents=True,exist_ok=True);p.write_text(json.dumps(d,indent=2)+'\n',encoding='utf-8')
manifest=json.loads((D/'planner-adjacency-1/manifest.json').read_text())
for row in manifest['files']:assert sha(ROOT/row['path'])==row['before']
for p in (D/'planner-adjacency-1').rglob('*'):
    if p.is_file():copy(p,O/'candidate'/p.relative_to(D/'planner-adjacency-1'))
base=E/'flight-refinement/baseline-source';hashes={p.relative_to(base).as_posix():sha(p) for p in base.rglob('*') if p.is_file()}
runs=['20261027-125023-389103-production-overlay-StartupPlanShapeProgram','20261027-125348-681876-production-PlannerAdjacencyProgram',
 '20261027-125406-071351-production-PlannerAdjacencyProgram','20261027-125416-806939-production-overlay-PlannerAdjacencyProgram',
 '20261027-125418-587993-production-overlay-api','20261027-125457-610637-production-PlannerAdjacencyProgram',
 '20261027-125459-302513-production-overlay-PlannerAdjacencyProgram','20261027-125652-031071-production-ShippingPlanProgram',
 '20261027-125654-550279-production-overlay-ShippingPlanProgram','20261027-125800-837438-production-overlay-Program',
 '20261027-125920-777840-production-overlay-PlannerAdjacencyProgram']
reports=[];data={}
for name in runs:
    run=ROOT/'.terrain-cache/progressive-lod/runs'/name;r=json.loads((run/'result.json').read_text());data[name]=r;dst=O/'checks'/name;overrides=[]
    for n in ['result.json','console.txt','compile.rsp']:
        if (run/n).exists():copy(run/n,dst/n)
    for rel,digest in r['source_sha256'].items():
        if hashes.get(rel)==digest:continue
        assert sha(run/'source'/rel)==digest;copy(run/'source'/rel,dst/'source'/rel);overrides.append(rel)
    save(dst/'archive-source-layout.json',{'base_snapshot':'../../../flight-refinement/baseline-source','overrides':'source','overridden_files':overrides,'all_hashes':'result.json/source_sha256'})
    reports.append({'run':name,'status':r['status'],'checks':r.get('checks'),'failed_assertions':r.get('failedChecks'),'assemblies':r.get('assemblies'),'error':r.get('error')})
sa=data[runs[7]];sb=data[runs[8]];assert sa['data_sha256']==sb['data_sha256'];a=sa['result'];b=sb['result']
assert a['tile_rows']==b['tile_rows'] and a['ring_samples']==b['ring_samples']
ring=[]
for radius in sorted({p['radius_m'] for p in a['ring_samples']}):
    pairs=[(x,y) for x,y in zip(a['ring_samples'],b['ring_samples']) if x['radius_m']==radius and (x['eligible'] or y['eligible'])]
    ring.append({'radius_m':radius,'eligible_union_samples':len(pairs),'coarser':sum(y['target_spacing_m']>x['target_spacing_m'] for x,y in pairs),
        'finer':sum(y['target_spacing_m']<x['target_spacing_m'] for x,y in pairs),'max_spacing_before':max(x['target_spacing_m'] for x,y in pairs),'max_spacing_after':max(y['target_spacing_m'] for x,y in pairs)})
pa=data[runs[5]]['plans'];pb=data[runs[6]]['plans']
shapeBefore=json.loads((E/'startup-plan-shape/checks/20261027-123939-552925-production-StartupPlanShapeProgram/result.json').read_text())['rows']
shapeAfter=data[runs[0]]['rows']
summary={'utc':datetime.datetime.now(datetime.timezone.utc).isoformat(),'status':'One-file CPU/API candidate; NOT INSTALLED;192-cap near-quality gate not met',
 'candidate':manifest,'validation':reports,
 'actual_source_cap_comparison':[{'cap':x['maximum_tiles'],'before_tiles':x['desired_tiles'],'after_tiles':y['desired_tiles'],'before_near_level':x['near_level'],'after_near_level':y['near_level'],
   'before_initial_near_spacing_m':x['near_initial_spacing_m'],'after_initial_near_spacing_m':y['near_initial_spacing_m'],
   'before_unbalanced_directed':len(x['unbalanced_neighbors']),'after_unbalanced_directed':len(y['unbalanced_neighbors'])} for x,y in zip(shapeBefore,shapeAfter)],
 'shipping_profile':{k:v for k,v in b.items() if k not in ['tile_rows','ring_samples']},'shipping_data_sha256':sb['data_sha256'],
 'shipping_comparison':{'exact_tile_rows_identical':True,'all_ring_samples_identical':True,'sample_count':len(a['ring_samples']),
   'eligibility_union_count':sum(r['eligible_union_samples'] for r in ring),'rings':ring,
   'closest_eligible_tiles_extending_outside_contact':sorted((r for r in b['tile_rows'] if r['eligible'] and r['extends_outside_contact']),key=lambda r:r['distance_m'])[:10]},
 'same_fixture_cpu_observation':{'baseline_run':runs[5],'candidate_run':runs[6],'baseline_sum_15_plan_medians_ms':sum(r['median_plan_ms'] for r in pa),
   'candidate_sum_15_plan_medians_ms':sum(r['median_plan_ms'] for r in pb),'baseline_normal_predicate_100k_ms':data[runs[5]]['normal_predicate_100k_ms'],
   'candidate_normal_predicate_100k_ms':data[runs[6]]['normal_predicate_100k_ms'],'candidate_normal_predicate_allocated_bytes':0},
 'additional_near_regressions':[{'x':x['x'],'z':x['z'],'cap':x['limit'],'before_level':x['near_level'],'after_level':y['near_level']} for x,y in zip(pa,pb) if x['near_level']!=y['near_level']],
 'preserved_failures':['Baseline missing level validation and null handling aborted first fixture; refined harness records failures and continues.',
   'Candidate first fixture selected observer tile using rounded render-meter endpoints; at(-1e8,+1e8)/192 two tiles matched due to endpoint rounding. Canonical-grid selection is unique; raw meter membership count remains recorded. Runtime meter queries were not changed.'],
 'limits':['Correctness candidate, not a measured worldgen/Start/FPS speedup; synthetic planner costs increased in this observation.',
   'Checked Int64 path allocates zero in100k calls. Full signed64 keys at levels0..24 use exact decimal fallback after checked overflow; that rare path allocates the OverflowException.',
   'Same-grid dyadic TileKey contract; arbitrary hand-authored nonaligned LodTile coordinates are outside this predicate contract.',
   'Actual192-cap near coarsening is the current priority/split-closure outcome, not proof every balanced192-tile plan must coarsen spawn.',
   'Shipping sample is one recorded spawn and shim viewport, with saved view/FOV; exact camera position from screenshot not known. No native geometry, admission, occlusion or visual acceptance.',
   'No runtime priority, budget, world/pack data or generation-version change; render plan topology changes and provenance/adoption require review.'],
 'decision':'Do not install automatically. Fixes recorded adjacency/balance defects but constrained-cap near quality is not preserved. Root review must decide the narrow correctness change separately; no cap increase or priority workaround added.'}
save(O/'summary.json',summary)
for name in ['PlannerAdjacencyProgram.cs','ShippingPlanProgram.cs','StartupPlanShapeProgram.cs']:copy(ROOT/'Tools/ProgressiveLod'/name,O/'fixtures'/name)
copy(Path(__file__),O/'checkpoint-script.py')

# Finish the separately authorized timer installation record without relabeling old results.
H=E/'flight-hysteresis';installed=json.loads((H/'install/installed.json').read_text());assert installed['installed']
for row in installed['files']:assert sha(ROOT/row['path'])==row['sha256']
for name in ['install_flight_hysteresis.py','verify_flight_hysteresis.py']:copy(D/name,H/'install/scripts'/name)
save(H/'current-status.json',{'utc':summary['utc'],'installed':True,'status':'flight-hysteresis-1 installed; native compile/reload and bounded mesh oracle PASS; no Play/new Editor',
    'current':'install/installed.json','candidate_and_reproducer':'summary.json','native_mountainous_flight_verified':False})
B=D/'planner-checkpoint-doc-preimages';copy(E/'flight-refinement/current-status.json',B/'flight-current-status.json')
old=json.loads((E/'flight-refinement/current-status.json').read_text());old.update(timer_followup='../flight-hysteresis/current-status.json',latest_voxels_sha256=installed['files'][0]['sha256'])
save(E/'flight-refinement/current-status.json',old)
note='''Flight-hysteresis-1 LIVE (2026-10-27 ~12:57UTC): exact one-line timer reset
installed after coordinator/quality slot release. Fresh4.030GiB headroom, native
compile/reload and mesh regression PASS (32,198faces/RGB,128,416AO,12worlds).
Voxels464c5661… and both rollback copies verified; other5flight sources and
original scene/settings/options unchanged. No Play/new Editor; slot released.
Evidence: Docs/StartupThroughputEvidence/2026-10-27/flight-hysteresis/install.

Planner adjacency candidate (2026-10-27 13:00UTC), NOT INSTALLED: checked integer
TileKey endpoints plus widened extreme-key fallback fix the34 unique192-cap
imbalance pairs.4403 predicate/coverage/balance/range assertions,54 existing
world-scale checks and API5 pass. At actual192 cap, near initial grid becomes
12.8m versus3.2m; adoption quality gate remains unmet. Current serialized512/
30km profile with saved93.43deg FOV has identical511tiles and all576 sampled
target-detail values, including412eligible points outside100m contact. Synthetic
planner cost increased26.36 ->38.07ms summed15medians; no speedup claim. No new
cache/worker/budget or priority policy. Exact evidence/adoption limits:
Docs/StartupThroughputEvidence/2026-10-27/planner-adjacency/summary.json.

'''
todo='''- [x] Install exact one-line flight timer correction; verify native compile,
      mesh oracle, readable rollback, other5sources and original settings/scene/options.
- [x] Separate integer-adjacency candidate: range/corner/negative/overflow checks,
      exhaustive plan non-overlap/root-area coverage/balance, actual cap comparison
      and serialized512/30km neighborhood comparison. Preserve failures.
- [ ] Planner adoption:192-cap near quality regresses; do not install or silently
      raise caps/reprioritize. Current512 profile sample is unchanged, not a full-world
      guarantee. Review render-topology/provenance implications before any publication.
- [ ] Native mountainous flight/quality/FPS remains unverified for flight policy.

'''
detail='''
## Timer correction installed; planner adjacency remains isolated

The one-line slow-speed timestamp reset was installed separately at approximately
12:57UTC after explicit coordinator release. Idle preflight had4.030GiB commit
headroom. Main native compile/reload and bounded mesh oracle pass32,198faces/RGB,
128,416AO and12worlds. Exact Voxels464c5661… and independent/hook preimages were
verified; other five flight sources and original scene/settings/options are
unchanged. No Play/new Editor or new flight benchmark. Earlier timer-candidate
statements are historical. [Installed evidence](StartupThroughputEvidence/2026-10-27/flight-hysteresis/install/installed.json).

The separate one-file LodPlanner candidate validates levels0..24 before shifting,
uses checked Int64 grid endpoints, and falls back to exact widened decimal
arithmetic when a full signed64 key's endpoint exceeds Int64. A shared edge must
have positive overlap; corner contact is not adjacency. Normal project-coordinate
calls measured zero allocation; extreme fallback allocates an overflow exception.
No persistent cache, worker, queue or budget change was added. API5,54 existing
world-scale checks and4403 focused assertions pass, including independent
BigInteger predicate cases and complete pairwise non-overlap/balance plus exact
root-area coverage for15 synthetic plans. Counts include repeated oracle cases.

| Actual source,20km cap | Before/after tiles | Before/after near initial spacing | Unbalanced unique pairs before/after |
| --- | ---: | ---: | ---: |
|64|63 /63|819.2m /819.2m|0 /0|
|192|192 /192|3.2m /12.8m|34 /0|
|512|510 /510|3.2m /3.2m|0 /0|

The192-cap regression is explicit. Correct neighbor closure consumes different
slots under the existing priority rule; this does not prove all possible balanced
192-tile allocations must sacrifice that near resolution. No cap increase or
priority workaround was added. Synthetic near-quality regressions also occur at
one very large coordinate with64/192caps. This candidate is **not installed** and
does not meet the requested across-cap near-quality adoption gate.

The one actual shipping-profile measurement reads512tiles/30km/contact100m/
1.5px from serialized settings, LoadSavedOptions from the scene, and saved view/
FOV30km/93.4328deg. At recorded spawn yaw20/pitch8 and a1440x900 shim viewport,
both versions produce identical511tile records and all576 ring samples from101m
to8192m.412 samples have refinement eligibility in either plan; all are unchanged.
The nearest eligible far tiles extending past contact remain25.6m/128columns,
or0.2m target spacing. Those are requested targets before mesh memory admission,
not native achieved detail, point visibility or occlusion proof.

This is a correctness change with cost: the matching synthetic15-plan run sums
median planning times26.36 ->38.07ms.100k normal predicate calls took11.56 ->
12.81ms in that pair. These small .NET observations are not Start/FPS estimates.
An initial test used rounded meter endpoints to select the observer tile and
found two memberships at(-1e8,+1e8)/192. The final test uses canonical grid
selection and retains that meter-endpoint diagnostic; runtime meter queries
were not changed. Earlier failures retain their original source identities.
No world/pack files were edited, but render-plan topology changes; provenance
and adoption decisions remain for review. [Candidate, all source-matched evidence and limitations](StartupThroughputEvidence/2026-10-27/planner-adjacency/summary.json).
'''
log='''
### 2026-10-27 — Timer installed; one isolated adjacency candidate reviewed by tests

Installed flight-hysteresis-1 only after coordinator release;4.030GiB headroom,
native compile/reload and mesh oracle PASS, exact source/two rollback hashes,
five other sources and protected originals unchanged. No Play/new Editor; slot
released. Planner1651adfb… remains outsideAssets over unchanged31f2f4ba… .
4403 focused/54 world-scale/API5 pass; actual192 imbalance34 ->0 pairs but near
grid3.2 ->12.8m. Serialized512/30km+savedFOV93.43 measurement has identical511
tiles/576samples (412eligible).15-plan CPU summedmedians26.36 ->38.07ms, no
speedup claim. Preserve baseline predicate failures and initial float-membership
fixture failure; exact integer coverage remains nonoverlapping. No automatic
planner adoption or budget/priority branch. Evidence: flight-hysteresis/install
and planner-adjacency under StartupThroughputEvidence/2026-10-27.
'''
for rel,addition,mode in [('MEMORY.md',note,'insert'),('TODO.md',todo,'insert'),('Docs/AI_HANDOFF.md',note.replace('Docs/StartupThroughputEvidence','StartupThroughputEvidence'),'insert'),('Docs/STARTUP_LOD_PROGRESS.md',detail,'append'),('Docs/SESSION_LOG.md',log,'append')]:
    p=ROOT/rel;old=p.read_bytes();text=old.decode('utf-8');copy(p,B/rel);n=text.find('\n')+1
    new=text[:n]+'\n'+addition+text[n:] if mode=='insert' else text.rstrip()+'\n'+addition
    assert p.read_bytes()==old;p.write_bytes(new.encode('utf-8'))
index=E/'archive-sha256.json';old=index.read_bytes();copy(index,B/'archive-sha256.json')
hashes={p.relative_to(E).as_posix():sha(p) for p in sorted(E.rglob('*')) if p.is_file() and p!=index}
assert index.read_bytes()==old;save(index,hashes)
print(json.dumps({'archive':str(O),'candidate_installed':False,'timer_installed':True,'checks':reports},indent=2))
