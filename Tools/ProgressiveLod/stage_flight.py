"""Freeze installed startup-budget sources and rebase the altitude experiment outside Assets."""
from pathlib import Path
import ast, datetime, difflib, hashlib, json, shutil
ROOT=Path(__file__).resolve().parents[2]
DEST=ROOT/'.terrain-cache/startup-throughput/flight-refinement-1'
assert not DEST.exists(),'Preserve existing experiment; use a new identity'
expected={
 'WorldScaleRuntime/WorldExplorer.cs':'15a24d325bc62db2dbe517ee5b2e6bd916f877d6b6d9b966dc2d964831a8d393',
 'WorldScaleRuntime/WorldExplorerIntegration.cs':'5d132d061fa7f855e480f20e2aaa7af174684085dd1b0349313399f5426a3700',
 'WorldScaleRuntime/WorldExplorerVoxels.cs':'4bd6abd85ad8fe18d83e6ca22c6f03a3b944f29fef52a117fc5444e889d1da47',
 'Exploration/ExplorationSettings.cs':'66626530b591492e92ea2771b50e7a6acf3c8c789211ddcc42bacdf37d3976b9',
 'Exploration/ExplorationBootstrap.cs':'f609db6e1bf116d81a063e926dd24a3f8157acf939c04e4325ca9ad1f5a4de55',
}
tree=ast.parse((ROOT/'Tools/ProgressiveLod/stage_altitude.py').read_text())
changes=next(ast.literal_eval(n.value) for n in tree.body if isinstance(n,ast.Assign) and any(isinstance(t,ast.Name) and t.id=='changes' for t in n.targets))
def sha(p):return hashlib.sha256(p.read_bytes()).hexdigest()
rows=[];diff=[]
for path,h in expected.items():
    rel=Path('Assets/AshenVoxel')/path;src=ROOT/rel;assert sha(src)==h,rel
    base=DEST/'baseline'/rel;base.parent.mkdir(parents=True,exist_ok=True);shutil.copyfile(src,base);assert sha(base)==h
    before=base.read_text();after=before
    for old,new in changes.get(src.name,[]):
        assert after.count(old)==1,src.name;after=after.replace(old,new)
    dst=DEST/'altitude'/rel;dst.parent.mkdir(parents=True,exist_ok=True)
    if after==before:shutil.copyfile(base,dst)
    else:dst.write_text(after,encoding='utf-8',newline='\n')
    rows.append({'path':rel.as_posix(),'before':h,'sha256':sha(dst)})
    diff.extend(difflib.unified_diff(before.splitlines(True),after.splitlines(True),fromfile='a/'+rel.as_posix(),tofile='b/'+rel.as_posix()))
manifest={'utc':datetime.datetime.now(datetime.timezone.utc).isoformat(),'status':'CPU-only altitude rebase; NOT INSTALLED','files':rows}
(DEST/'altitude/manifest.json').write_text(json.dumps(manifest,indent=2)+'\n')
(DEST/'altitude/candidate.diff').write_text(''.join(diff))
print(DEST)
