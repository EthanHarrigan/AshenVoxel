"""Merge the measured scheduler with patch6 wind guards, outside live Assets."""
from pathlib import Path
import datetime, difflib, hashlib, json, shutil
from prepare import ROOT, CACHE, FROZEN, once

def main():
    rel = Path('Assets/AshenVoxel/Exploration/VegetationStreaming.cs')
    before = (ROOT/rel).read_text()
    frozen = (FROZEN/rel).read_text()
    candidate = (CACHE/'compact/VegetationStreaming.cs').read_text()
    for line in ['            StartupProbe.MatrixRebuilds++;', 'StartupProbe.CellsBuilt++;', 'StartupProbe.MatrixVisits++;']:
        candidate = candidate.replace(line, '')
    a, b = frozen.splitlines(True), before.splitlines(True)
    hunks = []
    for tag, i, j, k, l in difflib.SequenceMatcher(None, a, b).get_opcodes():
        if tag == 'equal': continue
        old, new = ''.join(a[i:j]), ''.join(b[k:l])
        assert ('ExplorationWind.MaximumDisplacementMeters' in new or 'MotionVectorGenerationMode.Object' in new), (tag, old, new)
        # Include stable surrounding lines so insertion-only hunks remain unique.
        lo, hi = max(0, i-1), min(len(a), j+1)
        candidate = once(candidate, ''.join(a[lo:hi]), ''.join(a[lo:i])+new+''.join(a[j:hi]))
        hunks.append(new)
    assert len(hunks) == 2, 'Expected only the two known patch6 wind guards'
    candidate = once(candidate, '            bool advancedMatrices=buildingMatrices;',
        '            // Advance the captured snapshot first, then let cell generation use the remaining budget.\n            bool advancedMatrices=buildingMatrices;')
    candidate = once(candidate, '            coverageReserve=0;\n            if(impostorMaterial!=null)',
        '            // Reserve the actual cheapest representation of every captured entry, including hard props.\n            coverageReserve=0;\n            if(impostorMaterial!=null)')
    candidate = once(candidate, '                if(impostorMaterial!=null)coverageReserve-=',
        '                // Retire this entry before range filtering; skipped entries must release their reservation too.\n                if(impostorMaterial!=null)coverageReserve-=')
    target = CACHE/'merged'/rel
    target.parent.mkdir(parents=True, exist_ok=True)
    target.write_text(candidate)
    files = [rel]
    manifest = json.loads((ROOT/'.terrain-cache/progressive-lod/source-manifest.json').read_text())
    for name, entry in manifest.items():
        path = Path('Assets/AshenVoxel/WorldScaleRuntime')/name
        assert hashlib.sha256((ROOT/path).read_bytes()).hexdigest() == entry['baseline_sha256']
        destination = CACHE/'merged'/path
        destination.parent.mkdir(parents=True, exist_ok=True)
        shutil.copyfile(ROOT/'.terrain-cache/progressive-lod/candidate'/name, destination)
        files.append(path)
    result = {'id': 'startup-lod-1', 'utc': datetime.datetime.now(datetime.timezone.utc).isoformat(), 'files': [
        {'path': p.as_posix(), 'before': hashlib.sha256((ROOT/p).read_bytes()).hexdigest(),
         'sha256': hashlib.sha256((CACHE/'merged'/p).read_bytes()).hexdigest()} for p in files]}
    (CACHE/'merged/manifest.json').write_text(json.dumps(result, indent=2)+'\n')
    print(json.dumps(result, indent=2))

if __name__ == '__main__': main()
