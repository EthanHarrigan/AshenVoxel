from pathlib import Path
import hashlib,json,difflib,shutil
D=Path(__file__).resolve().parent
rel=Path('Assets/AshenVoxel/WorldScaleRuntime/WorldExplorerVoxels.cs');p=D/'flight'/rel
old=p.read_text();before=p.read_bytes();backup=D/'flight-before-queue-fairness'/rel;backup.parent.mkdir(parents=True,exist_ok=True);assert not backup.exists();backup.write_bytes(before)
needle='                currentBuild!=null||pending.Count>0)return;'
assert old.count(needle)==1
new=old.replace(needle,'                currentBuild!=null||HasRunnableVoxelWork())return;')
needle='        bool HoldVoxelDetailWork(Work work)=>'
new=new.replace(needle,'''        bool HasRunnableVoxelWork()
        {
            foreach(Work work in pending)if(!HoldVoxelDetailWork(work))return true;
            return false;
        }
'''+needle)
assert p.read_bytes()==before;p.write_text(new,encoding='utf-8',newline='\n')
m=D/'flight/manifest.json';shutil.copyfile(m,D/'flight-before-queue-fairness/manifest.json');manifest=json.loads(m.read_text());diff=[]
for row in manifest['files']:
    src=D/'flight'/row['path'];row['sha256']=hashlib.sha256(src.read_bytes()).hexdigest()
    base=(D/'baseline'/row['path']).read_text();after=src.read_text()
    diff.extend(difflib.unified_diff(base.splitlines(True),after.splitlines(True),fromfile='a/'+row['path'],tofile='b/'+row['path']))
m.write_text(json.dumps(manifest,indent=2)+'\n');(D/'flight/candidate.diff').write_text(''.join(diff))
print('Held quality requests cannot block eligible coverage work; old prototype preserved.')
