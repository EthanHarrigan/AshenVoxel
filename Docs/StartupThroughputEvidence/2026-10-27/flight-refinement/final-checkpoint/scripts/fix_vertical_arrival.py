from pathlib import Path
import shutil,hashlib,json,difflib,datetime
D=Path(__file__).resolve().parent;dest=D/'flight-final';assert not dest.exists();shutil.copytree(D/'flight-v4',dest)
p=dest/'Assets/AshenVoxel/WorldScaleRuntime/WorldExplorerVoxels.cs';text=p.read_text()
old='            if(cx==contactCenterX&&cz==contactCenterZ&&revision==contactRevision&&radius==contactRadius)return;'
new='''            if(cx==contactCenterX&&cz==contactCenterZ&&revision==contactRevision&&radius==contactRadius)
            {
                // Vertical descent can make the existing queue urgent without
                // changing XZ cells, source revision or the contact radius.
                if(currentBuild!=null&&!currentBuild.Work.Contact&&HasUrgentContactWork())
                {
                    Work work=currentBuild.Work;CancelBuild();
                    if(work.Revision==revision&&desired.Contains(work.Tile.Key))pending.Enqueue(work);
                }
                return;
            }'''
assert text.count(old)==1;text=text.replace(old,new);p.write_text(text,encoding='utf-8',newline='\n')
rows=[];diff=[]
for p in sorted((dest/'Assets').rglob('*.cs')):
    rel=p.relative_to(dest);base=D/'baseline'/rel
    rows.append({'path':rel.as_posix(),'before':hashlib.sha256(base.read_bytes()).hexdigest(),'sha256':hashlib.sha256(p.read_bytes()).hexdigest()})
    diff.extend(difflib.unified_diff(base.read_text().splitlines(True),p.read_text().splitlines(True),fromfile='a/'+rel.as_posix(),tofile='b/'+rel.as_posix()))
(dest/'manifest.json').write_text(json.dumps({'utc':datetime.datetime.now(datetime.timezone.utc).isoformat(),'status':'flight-final CPU-only, vertical-arrival correction; NOT INSTALLED','files':rows},indent=2)+'\n');(dest/'candidate.diff').write_text(''.join(diff))
print(dest)
