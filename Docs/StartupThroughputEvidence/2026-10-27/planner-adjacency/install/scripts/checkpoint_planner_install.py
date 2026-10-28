from pathlib import Path
import datetime, hashlib, json, shutil

ROOT = Path(__file__).resolve().parents[2]
E = ROOT / 'Docs/StartupThroughputEvidence/2026-10-27'
O = E / 'planner-adjacency'
B = ROOT / '.terrain-cache/startup-throughput/planner-install-doc-preimages'
def sha(p): return hashlib.sha256(p.read_bytes()).hexdigest()
def copy(s, d):
    d.parent.mkdir(parents=True, exist_ok=True)
    assert not d.exists(), d
    shutil.copyfile(s, d)
    assert sha(s) == sha(d)
def save(p, data):
    assert not p.exists(), p
    p.parent.mkdir(parents=True, exist_ok=True)
    p.write_text(json.dumps(data, indent=2) + '\n', encoding='utf-8')

installed = json.loads((O / 'install/installed.json').read_text())
assert installed['installed']
for r in installed['files']: assert sha(ROOT / r['path']) == r['sha256']
for name in ['install_planner_adjacency.py', 'verify_planner_adjacency.py']:
    copy(ROOT / '.terrain-cache/startup-throughput' / name, O / 'install/scripts' / name)
copy(Path(__file__), O / 'install/scripts/checkpoint_planner_install.py')
name = '20261027-131916-728357-production-Program'
run = ROOT / '.terrain-cache/progressive-lod/runs' / name
dst = O / 'checks' / name
r = json.loads((run / 'result.json').read_text())
assert r['status'] == 'PASS' and r['checks'] == 54
base = E / 'flight-refinement/baseline-source'
overrides = []
for f in ['result.json', 'console.txt', 'compile.rsp']: copy(run / f, dst / f)
for rel, digest in r['source_sha256'].items():
    assert sha(run / 'source' / rel) == digest
    if (base / rel).is_file() and sha(base / rel) == digest: continue
    copy(run / 'source' / rel, dst / 'source' / rel)
    overrides.append(rel)
save(dst / 'archive-source-layout.json', {
    'base_snapshot': '../../../flight-refinement/baseline-source',
    'overrides': 'source', 'overridden_files': overrides,
    'all_hashes': 'result.json/source_sha256'})
save(O / 'current-status.json', {
    'utc': datetime.datetime.now(datetime.timezone.utc).isoformat(),
    'installed': True, 'current': 'install/installed.json',
    'status': 'Single-file guarded integration approved after review; native compile/reload and mesh regression PASS',
    'render_plan_revision': 'planner-adjacency-1/1651adfb',
    'revision_scope': 'Deployment/evidence identity for changed render-plan topology; no new source generator or serialized format identity.',
    'world_scale_installed_source_check': 'checks/' + name + '/result.json',
    'earlier_candidate_status_is_historical': ['summary.json', 'capacity-checkpoint.json'],
    'limits': [
        'Correctness fix, not a measured Start/FPS gain. Existing512/30km recorded plan and576samples unchanged.',
        'At192 cap, actual-spawn initial near grid3.2m becomes12.8m at20km or25.6m at30km. No cap or priority workaround.',
        'Exact dyadic adjacency does not repair floating render-meter endpoint membership at very large coordinates.',
        'Predicate handles signed64 keys/levels0..24; planner still limits camera coordinates to +/-1e12m and does not promise arbitrary-key rendering.',
        'Extreme overflow fallback allocates an exception; normal measured path has zero allocation.',
        'No native world-scale Play or mountainous-flight/FPS acceptance in this install. Native slot returned to coordinator.']})

note = '''Planner-adjacency-1 LIVE (2026-10-27 ~13:19UTC): coordinator released the
one-file31f2 ->1651 correction after reviewing constrained-cap quality costs.
Fresh4.432GiB headroom, idle/no-job/hash/readable-backup gates passed. Main Unity
compile/reload and native mesh regression PASS (32,198faces/RGB,128,416AO,
12worlds); installed-source .NET world-scale54 and project integrity PASS.
Only LodPlanner changed: all6 current runtime files includingVoxels464c,
original scene/settings/options, world JSON and prototype pack are unchanged.
Render-plan revision planner-adjacency-1/1651adfb is a deployment identity;
world/source generator and formats are unchanged.192-cap near coarsening remains
explicit (3.2 ->12.8m at20km;3.2 ->25.6m at30km). Current512 plan comparison was
unchanged. Large-coordinate render-meter rounding remains a separate limitation.
No Play/new Editor, cap raise or speedup claim. Native slot released. Earlier
NOT INSTALLED statements below are historical checkpoints. Current evidence:
Docs/StartupThroughputEvidence/2026-10-27/planner-adjacency/current-status.json.

'''
detail = '''
### 2026-10-27 — Planner-adjacency-1 installed after explicit tradeoff review

Coordinator released only LodPlanner31f2 ->1651 after reviewing the192-cap
coarsening and unchanged recorded512/30km plan/neighborhood. Fresh idle/no-job
preflight had4.432GiB commit headroom. Both readable rollback copies and exact
source hashes passed. Main Unity compile/reload and the bounded mesh oracle
passed32,198faces/RGB,128,416AO across12worlds. Installed-source .NET world-scale
checks passed54; these are not native world-scale Play or FPS tests. Integrity
passed184assets/11assemblies. All6 protected runtime files, original scene,
settings and saved options, world JSON and prototype pack remain unchanged.
No Play/new Editor; native slot returned to coordinator.

The render-plan deployment revision is `planner-adjacency-1/1651adfb`; generator
identity and serialized formats are unchanged. Topology can change because
neighbor split closure now uses exact dyadic keys. At192tiles the actual-spawn
initial near grid changes3.2 ->12.8m at20km and3.2 ->25.6m at30km. Caps/priorities
were not raised or altered to conceal that cost. The recorded512 profile is
unchanged, but this is not a guarantee for all positions. It is a correctness
fix, with the earlier measured planner CPU cost increase retained, not a speedup.

Exact key adjacency does not fix floating meter-coordinate render/membership
rounding. The earlier synthetic(-1e8,+1e8) overlap diagnostic remains; full
signed64-key predicate tests do not establish arbitrary-coordinate rendering.
Build still limits camera coordinates to+/-1e12m. Extreme endpoint fallback
allocates an overflow exception; ordinary-coordinate predicate calls measured
zero allocation. Earlier candidate/install-hold records retain their dates.
[Current status and installed evidence](StartupThroughputEvidence/2026-10-27/planner-adjacency/current-status.json).
'''
for rel, addition, mode in [
        ('MEMORY.md', note, 'insert'),
        ('Docs/AI_HANDOFF.md', note.replace('Docs/StartupThroughputEvidence', 'StartupThroughputEvidence'), 'insert'),
        ('Docs/STARTUP_LOD_PROGRESS.md', detail, 'append'),
        ('Docs/SESSION_LOG.md', detail, 'append')]:
    p = ROOT / rel; old = p.read_bytes(); text = old.decode('utf-8'); copy(p, B / rel)
    n = text.find('\n') + 1
    new = text[:n] + '\n' + addition + text[n:] if mode == 'insert' else text.rstrip() + '\n' + addition
    assert p.read_bytes() == old; p.write_bytes(new.encode('utf-8'))

p = ROOT / 'TODO.md'; old = p.read_bytes(); text = old.decode('utf-8'); copy(p, B / 'TODO.md')
replacements = {
'''- [ ] Planner correctness adoption remains under review;192 profile coarsens.
      Keep isolated until exact-source publication review is complete.''':
'''- [x] Coordinator-reviewed one-file planner integration installed13:19UTC;
      native compile/mesh and installed-source world-scale54 pass.192 profile
      coarsening accepted explicitly; no cap/priority workaround. See current-status.''',
'''- [ ] Planner adoption:192-cap near quality regresses; do not install or silently
      raise caps/reprioritize. Current512 profile sample is unchanged, not a full-world
      guarantee. Review render-topology/provenance implications before any publication.''':
'''- [x] Review render-topology/provenance and192-cap tradeoff; guarded integration
      approved and completed. Current512 sample is unchanged, not a full-world guarantee.
- [ ] Validate native mountainous-flight visual convergence and frame/memory behavior;
      do not equate planner/mesh regression checks with these acceptance gates.''',
'''- [ ] Separate planner boundary correctness follow-up:192-cap plan has34 unique
      unbalanced pairs. Do not mix with timer fix or silently lower initial quality.''':
'''- [x] Separate planner boundary correctness follow-up removes34 unique unbalanced
      pairs; installed after explicit constrained-cap quality review, separate from timer.'''
}
for before, after in replacements.items():
    # Preserve other owners' edits and Windows/newline conventions while matching logically.
    normalized = text.replace('\r\n', '\n')
    assert before in normalized, before
    text = normalized.replace(before, after, 1)
assert p.read_bytes() == old; p.write_bytes(text.encode('utf-8'))

p = ROOT / 'Docs/DECISIONS.md'; old = p.read_bytes(); text = old.decode('utf-8'); copy(p, B / 'Docs/DECISIONS.md')
anchor = '## D040 — Progressive cached terrain with bounded admission'
addition = '''

2026-10-27 correctness follow-up: render-plan deployment revision
`planner-adjacency-1/1651adfb` uses exact dyadic TileKey adjacency for split
closure. Source generator identity, formats, caps and priorities are unchanged.
The recorded512/30km plan is unchanged;192-cap near detail coarsens at the tested
spawn. This cost was reviewed before guarded integration. Arbitrary-coordinate
rendering is not guaranteed by integer predicate range tests; floating meter
endpoint rounding remains. See [installed evidence and limits](StartupThroughputEvidence/2026-10-27/planner-adjacency/current-status.json).
'''
assert anchor in text; text = text.replace(anchor, anchor + addition, 1)
assert p.read_bytes() == old; p.write_bytes(text.encode('utf-8'))
index = E / 'archive-sha256.json'; old = index.read_bytes(); copy(index, B / 'archive-sha256.json')
hashes = {p.relative_to(E).as_posix(): sha(p) for p in sorted(E.rglob('*')) if p.is_file() and p != index}
assert index.read_bytes() == old; index.write_text(json.dumps(hashes, indent=2) + '\n', encoding='utf-8')
print('Installed planner status, provenance, tradeoffs and54-check frozen evidence archived.')
