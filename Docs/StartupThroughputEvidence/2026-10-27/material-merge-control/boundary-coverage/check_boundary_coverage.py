from pathlib import Path
from collections import defaultdict,Counter
import json,hashlib
ROOT=Path(__file__).resolve().parent

def canonical(data,faces):
    n=data['n'];unit=data['step']*.1;anchor=round(data['AnchorY']/.1)
    maximum=0.;tops=Counter();sides=defaultdict(list)
    def snap(value,spacing):
        nonlocal maximum
        cell=round(value/spacing);error=abs(value-cell*spacing);maximum=max(maximum,error)
        assert error<.05, ('coordinate not within half a10cm cell',value,error)
        return cell
    for f in faces:
        axis=f['axis'];positive=f['positive'];x,z=f['x'],f['z'];y=f['y']
        if axis==1:
            assert positive
            x0,x1=snap(x,unit),snap(x+f['v'],unit);z0,z1=snap(z,unit),snap(z+f['u'],unit)
            h=anchor+snap(y,.1)
            assert 0<=x0<x1<=n and 0<=z0<z1<=n
            for ix in range(x0,x1):
                for iz in range(z0,z1):tops[(ix,iz,h)]+=1
        else:
            assert axis in (0,2)
            plane=snap(x if axis==0 else z,unit)
            start=z if axis==0 else x;width=f['v'] if axis==0 else f['u'];height=f['u'] if axis==0 else f['v']
            a,b=snap(start,unit),snap(start+width,unit);low=anchor+snap(y,.1);high=anchor+snap(y+height,.1)
            assert 0<=plane<=n and 0<=a<b<=n and low<high
            for along in range(a,b):sides[(axis,positive,plane,along)].append((low,high))
    normalized={}
    for key,intervals in sides.items():
        events=Counter()
        for low,high in intervals:events[low]+=1;events[high]-=1
        result=[];previous=None;count=0
        for point,delta in sorted(events.items()):
            if previous is not None and previous<point and count:
                if result and result[-1][1]==previous and result[-1][2]==count:result[-1]=(result[-1][0],point,count)
                else:result.append((previous,point,count))
            count+=delta;previous=point
        assert count==0
        normalized[key]=result
    return tops,normalized,maximum

def expected(data):
    n=data['n'];halo=data['haloTopCells'];get=lambda x,z:halo[x+1+(z+1)*(n+2)]
    tops=Counter();sides={}
    for z in range(n):
        for x in range(n):
            high=get(x,z);tops[(x,z,high)]=1
            for axis,positive,dx,dz in [(0,False,-1,0),(0,True,1,0),(2,False,0,-1),(2,True,0,1)]:
                nx,nz=x+dx,z+dz;low=get(nx,nz)
                if nx<0 or nx>=n or nz<0 or nz>=n:low=min(data['floorCell'],low)
                if low>=high:continue
                plane=x+int(positive) if axis==0 else z+int(positive);along=z if axis==0 else x
                sides[(axis,positive,plane,along)]=[(low,high,1)]
    return tops,sides

rows=[];actuals=[]
for variant in ['baseline','control']:
    source=ROOT/'boundary-coverage'/variant/'faces.json';data=json.loads(source.read_text());a,b,error=canonical(data,data['faces']);x,y=expected(data)
    assert a==x,(variant,'top coverage mismatch')
    assert b==y,(variant,'side/boundary interval coverage mismatch')
    assert max(a.values())==1 and all(count==1 for spans in b.values() for low,high,count in spans)
    # Establish that equal-area cancellation cannot conceal a missing or duplicated face.
    missing=canonical(data,data['faces'][1:]);duplicate=canonical(data,data['faces']+[data['faces'][0]])
    assert missing[:2]!=(x,y) and duplicate[:2]!=(x,y)
    payload=json.dumps({'tops':sorted(a.items()),'sides':sorted(b.items())},separators=(',',':')).encode()
    actuals.append((a,b));rows.append({'variant':variant,'faces':len(data['faces']),'top_cells':len(a),'side_boundary_strips':len(b),'canonical_sha256':hashlib.sha256(payload).hexdigest(),'max_coordinate_snap_error_m':error,'no_missing_or_duplicate_canonical_coverage':True,'hole_and_duplicate_sensitivity_checks':True,'faces_sha256':hashlib.sha256(source.read_bytes()).hexdigest()})
assert actuals[0]==actuals[1]
report={'status':'PASS','tile':'rear-lod-coverage, level9,8columns,13107.2m extent','scope':'One changed tile with largest observed area discrepancy. Each face decomposed into horizontal sample strips and10cm vertical integer intervals, preserving orientation and plane; full multiplicity compared against direct sampled-height/halo/seam-floor oracle. Colors and intentional material changes excluded. Canonical coverage equal; not bit-identical float tessellation or native visual proof.','baseline_control_canonical_coverage_equal':True,'rows':rows,'checker_sha256':hashlib.sha256(Path(__file__).read_bytes()).hexdigest()}
(ROOT/'boundary-coverage/result.json').write_text(json.dumps(report,indent=2)+'\n')
print(json.dumps(report,indent=2))
