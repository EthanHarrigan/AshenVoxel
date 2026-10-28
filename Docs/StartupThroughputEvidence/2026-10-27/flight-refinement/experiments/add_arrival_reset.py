from pathlib import Path
import hashlib,json,difflib,shutil,datetime
D=Path(__file__).resolve().parent;ROOT=D.parents[2];dest=D/'flight-arrival'
assert not dest.exists();shutil.copytree(D/'flight',dest)
rel=Path('Assets/AshenVoxel/Exploration/ExplorationController.cs');original=ROOT/rel
assert hashlib.sha256(original.read_bytes()).hexdigest()=='bcbdaa0ee9a7550d0439209ed839cc69c85c794a5156906dc9deed3d9a8894aa'
for variant in ['baseline','altitude','flight-arrival']:
    target=D/variant/rel;target.parent.mkdir(parents=True,exist_ok=True);assert not target.exists();shutil.copyfile(original,target)
p=dest/rel;text=p.read_text()
old='Motor.ResetAt(feet,flying);pendingSeconds=0;eyeInitialized=false;ApplyView(0);'
assert text.count(old)==1;text=text.replace(old,old+'vista.ResetObserverMotion();')
old='pendingSeconds=0;eyeInitialized=false;ApplyView(0);Status="Walking";return true;'
assert text.count(old)==1;text=text.replace(old,'pendingSeconds=0;eyeInitialized=false;ApplyView(0);vista.ResetObserverMotion();Status="Walking";return true;')
p.write_text(text,encoding='utf-8',newline='\n')
p=dest/'Assets/AshenVoxel/WorldScaleRuntime/WorldExplorerVoxels.cs';text=p.read_text()
old='        void ResetVoxelTravelMotion()'
assert text.count(old)==1
text=text.replace(old,'''        /// <summary>Explicit teleport/respawn notification; normal pose updates
        /// retain global velocity history across render-origin rebases.</summary>
        public void ResetObserverMotion()
        {
            SetVoxelTravelDeferral(false);ResetVoxelTravelMotion();
            voxelRefinementScanPending=true;ForceReplan();
        }
'''+old)
p.write_text(text,encoding='utf-8',newline='\n')
rows=[];diff=[]
for p in sorted((dest/'Assets').rglob('*.cs')):
    rel=p.relative_to(dest);base=D/'baseline'/rel
    rows.append({'path':rel.as_posix(),'before':hashlib.sha256(base.read_bytes()).hexdigest(),'sha256':hashlib.sha256(p.read_bytes()).hexdigest()})
    diff.extend(difflib.unified_diff(base.read_text().splitlines(True),p.read_text().splitlines(True),fromfile='a/'+rel.as_posix(),tofile='b/'+rel.as_posix()))
(dest/'manifest.json').write_text(json.dumps({'utc':datetime.datetime.now(datetime.timezone.utc).isoformat(),'status':'Isolated flight candidate with explicit arrival reset; NOT INSTALLED','files':rows},indent=2)+'\n')
(dest/'candidate.diff').write_text(''.join(diff))
print(dest)
