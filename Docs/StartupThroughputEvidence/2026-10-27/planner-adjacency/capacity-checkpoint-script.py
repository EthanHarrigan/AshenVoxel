from pathlib import Path
import datetime, hashlib, json, shutil

ROOT = Path(__file__).resolve().parents[2]
E = ROOT / 'Docs/StartupThroughputEvidence/2026-10-27'
O = E / 'planner-adjacency'
B = ROOT / '.terrain-cache/startup-throughput/planner-capacity-doc-preimages'
def sha(p): return hashlib.sha256(p.read_bytes()).hexdigest()
def copy(s, d):
    d.parent.mkdir(parents=True, exist_ok=True)
    assert not d.exists()
    shutil.copyfile(s, d)
    assert sha(s) == sha(d)
def save(p, data):
    p.parent.mkdir(parents=True, exist_ok=True)
    assert not p.exists()
    p.write_text(json.dumps(data, indent=2) + '\n', encoding='utf-8')

assert sha(ROOT / 'Assets/AshenVoxel/WorldScaleData/LodPlanner.cs') == '31f2f4bafeb44ad064c1c2e1702ebb700c051ec40e0a1a28de5777603522d88f'
base = E / 'flight-refinement/baseline-source'
runs = ['20261027-130323-624390-production-PlanCapacityProgram',
        '20261027-130614-316266-production-overlay-PlanCapacityProgram']
data = []
for name in runs:
    src = ROOT / '.terrain-cache/progressive-lod/runs' / name
    dst = O / 'checks' / name
    r = json.loads((src / 'result.json').read_text())
    assert r['status'] == 'PASS' and r['result']['geometry_builds'] == 0
    for filename in ['result.json', 'console.txt', 'compile.rsp']:
        copy(src / filename, dst / filename)
    overrides = []
    for rel, digest in r['source_sha256'].items():
        frozen = src / 'source' / rel
        assert sha(frozen) == digest
        if (base / rel).is_file() and sha(base / rel) == digest:
            continue
        copy(frozen, dst / 'source' / rel)
        overrides.append(rel)
    save(dst / 'archive-source-layout.json', {
        'base_snapshot': '../../../flight-refinement/baseline-source',
        'overrides': 'source', 'overridden_files': overrides,
        'all_hashes': 'result.json/source_sha256'})
    data.append(r)
assert data[0]['data_sha256'] == data[1]['data_sha256']
rows = []
for a, b in zip(data[0]['result']['rows'], data[1]['result']['rows']):
    assert (a['view_km'], a['cap']) == (b['view_km'], b['cap'])
    rows.append({'view_km': a['view_km'], 'cap': a['cap'],
        'before': a, 'after': b})
save(O / 'capacity-checkpoint.json', {
    'utc': datetime.datetime.now(datetime.timezone.utc).isoformat(),
    'status': 'Read-only current-plan capacity scan; candidate NOT INSTALLED',
    'runs': runs, 'data_sha256': data[0]['data_sha256'],
    'rows': rows,
    'before_minima': data[0]['result']['minima'],
    'after_minima': data[1]['result']['minima'],
    'limits': [
        'At this spawn, both 256 and 384 preserve observer L0 at 20km and saved 30km views.',
        'Exhaustive integer supported caps from 64 to first L0: baseline150/163; candidate234/247 for20km/30km.',
        'These are current greedy-plan outcomes, not mathematical feasibility bounds or recommendations to increase limits.',
        '192cap candidate initial near spacing is12.8m at20km and25.6m at30km, versus baseline3.2m.',
        'Observer detail alone does not establish neighborhood quality; see earlier exact512 shipping comparison.',
        'No geometry builds, changed settings, priorities, budget defaults, or native process. No Start/FPS speedup claim.']})
copy(ROOT / '.terrain-cache/startup-throughput/planner-adjacency-1/PlanCapacityProgram.cs', O / 'fixtures/PlanCapacityProgram.cs')
copy(Path(__file__), O / 'capacity-checkpoint-script.py')

note = '''Planner capacity follow-up (2026-10-27 13:08UTC), read-only: at the actual
spawn,256/384 caps retain observer L0 for20km and saved30km views. Exhaustive
supported caps64..first L0 give234/247 for the corrected planner versus150/163
baseline. These are current greedy-plan outcomes, not universal minima or a
recommendation to raise caps. At192/30km corrected initial near spacing is25.6m
(20km:12.8m), versus3.2m baseline. No geometry, policy/default change or install.
Evidence: Docs/StartupThroughputEvidence/2026-10-27/planner-adjacency/capacity-checkpoint.json.

'''
todo = '''- [x] Read-only256/384 capacity checks and exhaustive current-plan first-L0
      scan at actual spawn/20km and saved30km. Candidate minima234/247;
      both256/384 retain observer L0. No cap/default or priority changes.
- [ ] Planner correctness adoption remains under review;192 profile coarsens.
      Keep isolated until exact-source publication review is complete.

'''
detail = '''
### 2026-10-27 — Planner capacity scan, no runtime adoption

At actual spawn, both256 and384 caps retain observer L0 (25.6m tile, initial
3.2m grid) at20km and saved30km views, with the same total tile counts as baseline.
Exhaustive supported integer caps64..first L0 find baseline150/163 and corrected
234/247, respectively. These are current greedy-plan outcomes, not theoretical
minimum counts or cap recommendations. At192/30km the corrected plan is L3 with
25.6m initial grid versus baseline L0/3.2m; at20km it is L2/12.8m. The earlier
512-profile exact plan/neighborhood comparison remains the broader quality check.
Both source-frozen runs pass, with zero geometry builds. No settings, priority,
runtime source or native process changes. Candidate remains isolated for review.
[Evidence](StartupThroughputEvidence/2026-10-27/planner-adjacency/capacity-checkpoint.json).
'''
for rel, addition, insert in [
        ('MEMORY.md', note, True), ('TODO.md', todo, True),
        ('Docs/AI_HANDOFF.md', note.replace('Docs/StartupThroughputEvidence', 'StartupThroughputEvidence'), True),
        ('Docs/STARTUP_LOD_PROGRESS.md', detail, False),
        ('Docs/SESSION_LOG.md', detail, False)]:
    p = ROOT / rel
    old = p.read_bytes()
    text = old.decode('utf-8')
    copy(p, B / rel)
    n = text.find('\n') + 1
    updated = text[:n] + '\n' + addition + text[n:] if insert else text.rstrip() + '\n' + addition
    assert p.read_bytes() == old
    p.write_bytes(updated.encode('utf-8'))
index = E / 'archive-sha256.json'
old = index.read_bytes()
copy(index, B / 'archive-sha256.json')
hashes = {p.relative_to(E).as_posix(): sha(p) for p in sorted(E.rglob('*')) if p.is_file() and p != index}
assert index.read_bytes() == old
index.write_text(json.dumps(hashes, indent=2) + '\n', encoding='utf-8')
print('Archived two source-frozen capacity runs; no runtime adoption.')
