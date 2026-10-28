from pathlib import Path
import shutil,json,hashlib,difflib,datetime
D=Path(__file__).resolve().parent
for source,name in [('altitude','altitude-retain'),('flight-v3','flight-v4')]:
    dest=D/name;assert not dest.exists();shutil.copytree(D/source,dest)
    p=dest/'Assets/AshenVoxel/WorldScaleRuntime/WorldExplorerVoxels.cs';text=p.read_text()
    old='''                else {CancelBuild();if(!w.Contact&&w.Revision==revision&&desired.Contains(w.Tile.Key))pending.Enqueue(w);}'''
    new='''                // Crossing a contact cell must not repeatedly discard useful
                // distant work. Ground/descent urgency still preempts it.
                else if(w.Contact||w.Revision!=revision||!desired.Contains(w.Tile.Key)||HasUrgentContactWork())
                {CancelBuild();if(!w.Contact&&w.Revision==revision&&desired.Contains(w.Tile.Key))pending.Enqueue(w);}'''
    assert text.count(old)==1;text=text.replace(old,new)
    if name=='flight-v4':
        old='''                if(speed2<96*96)''';new='''                if(speed2<1)SetVoxelTravelDeferral(false);
                else if(speed2<96*96)'''
        assert text.count(old)==1;text=text.replace(old,new)
    p.write_text(text,encoding='utf-8',newline='\n')
    rows=[];diff=[]
    for p in sorted((dest/'Assets').rglob('*.cs')):
        rel=p.relative_to(dest);base=D/'baseline'/rel
        rows.append({'path':rel.as_posix(),'before':hashlib.sha256(base.read_bytes()).hexdigest(),'sha256':hashlib.sha256(p.read_bytes()).hexdigest()})
        diff.extend(difflib.unified_diff(base.read_text().splitlines(True),p.read_text().splitlines(True),fromfile='a/'+rel.as_posix(),tofile='b/'+rel.as_posix()))
    (dest/'manifest.json').write_text(json.dumps({'utc':datetime.datetime.now(datetime.timezone.utc).isoformat(),'status':name+' CPU-only; NOT INSTALLED','files':rows},indent=2)+'\n')
    (dest/'candidate.diff').write_text(''.join(diff))
    print(dest)
