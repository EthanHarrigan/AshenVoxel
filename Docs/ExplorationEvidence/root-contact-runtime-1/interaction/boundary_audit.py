"""Boundary-only supplement; does not rerun or modify frozen suites/evidence."""
from pathlib import Path
import json,struct,hashlib,datetime
import numpy as np
from scipy import ndimage
HERE=Path(__file__).resolve().parent;ROOT=HERE.parents[1]
GEOMETRY=ROOT/'.terrain-cache/root-contact-runtime-prototype';VISUAL=ROOT/'.terrain-cache/grounding-root-visual'
sha=lambda p:hashlib.sha256(p.read_bytes()).hexdigest()
DT=np.dtype([('x','<i2'),('y','<i2'),('z','<i2'),('tint','<u4')])
def read(path):
    data=np.fromfile(path,DT,offset=4)
    with path.open('rb') as f:assert len(data)==struct.unpack('<i',f.read(4))[0]
    return {(int(p['x']),int(p['y']),int(p['z'])):int(p['tint']) for p in data if p['y']<=35}
def classify(cells,lo,hi):
    wood=np.zeros(tuple(hi-lo),bool)
    for p in cells:wood[tuple(np.array(p)-lo)]=True
    core=ndimage.binary_erosion(wood,structure=np.ones((3,3,3),bool))
    morphology=wood&ndimage.binary_dilation(core,structure=np.ones((5,5,5),bool));hard=morphology.copy();center=-lo[[0,2]].astype(float)
    for y in range(max(0,-lo[1]),min(wood.shape[1],36-lo[1])):
        points=np.argwhere(wood[:,y,:])
        if not len(points):break
        d=((points-center)**2).sum(axis=1);near=points[d<=16]
        if not len(near):break
        closest=points[d.argmin()];local=near[((near-closest)**2).sum(axis=1)<=9];center=local.mean(axis=0)
        keep=points[((points-center)**2).sum(axis=1)<=9];hard[keep[:,0],y,keep[:,1]]=True
    return {p:bool(hard[tuple(np.array(p)-lo)]) for p in cells if p[1] in (29,30)}

frozen=json.loads((HERE/'report.json').read_text());geometry=json.loads((GEOMETRY/'report.json').read_text())
inputs=[Path(__file__),HERE/'report.json',HERE/'Interaction.cs',GEOMETRY/'report.json',GEOMETRY/'RootContact.cs',ROOT/'Tools/ExplorationPrototypes/bake_interactions.py']
for id in [10,11]:inputs.extend([VISUAL/f'wood-{id}-baseline.bin',GEOMETRY/f'v2-expected-{id}.bin',HERE/f'actual-{id}-oracle.bin'])
before={str(p):sha(p) for p in inputs}
for path,digest in before.items():
    if path in frozen['source_sha256']:assert digest==frozen['source_sha256'][path],'Frozen interaction input changed'
    if path in geometry['source_sha256']:assert digest==geometry['source_sha256'][path],'Frozen root input changed'
results=[]
for id in [10,11]:
    canonical=read(VISUAL/f'wood-{id}-baseline.bin');root=read(GEOMETRY/f'v2-expected-{id}.bin')
    emitted={p:t for p,t in root.items() if p[1]<=29};guard={p:t for p,t in root.items() if p[1]==30}
    assert len(root)==len(emitted)+len(guard) and max(p[1] for p in emitted)==29
    composite={p:t for p,t in canonical.items() if p[1]>=30};composite.update(emitted)
    for y in (29,30):assert {p:t for p,t in canonical.items() if p[1]==y}=={p:t for p,t in composite.items() if p[1]==y}
    allpoints=np.array(list(canonical)+list(composite));lo=allpoints.min(0)-3;hi=allpoints.max(0)+4
    old=classify(canonical,lo,hi);new=classify(composite,lo,hi);assert old.keys()==new.keys()
    data=(HERE/f'actual-{id}-oracle.bin').read_bytes();count=struct.unpack_from('<i',data)[0];assert len(data)==4+11*count
    channels={(x,y,z):channel for x,y,z,tint,channel in struct.iter_unpack('<hhhIB',data[4:])}
    assert channels.keys()==root.keys()
    assert all(channel in (1,2) if p[1]<=29 else channel==0 for p,channel in channels.items())
    assert all(channels[p]==0 for p in guard) and all(channels[p]!=0 for p in emitted)
    assert all((channels[p]==1)==new[p] for p in new if p[1]==29)
    prior=next(c for c in frozen['cases'] if c['name']==f'actual-{id}')
    assert prior['hard']+prior['stick']==len(emitted) and prior['root_cells']==len(root)
    rows=[]
    for y in (29,30):
        points=[p for p in old if p[1]==y]
        changes=[dict(x=p[0],y=y,z=p[2],canonical='hard' if old[p] else 'stick',composite='hard' if new[p] else 'stick') for p in sorted(points) if old[p]!=new[p]]
        rows.append(dict(y=y,ownership='root emitted; canonical pinned geometry' if y==29 else 'upper preserved; root context only',cells=len(points),canonical_hard=sum(old[p] for p in points),canonical_stick=sum(not old[p] for p in points),composite_hard=sum(new[p] for p in points),composite_stick=sum(not new[p] for p in points),root_channel_hard=sum(channels[p]==1 for p in points),root_channel_stick=sum(channels[p]==2 for p in points),root_channel_none=sum(channels[p]==0 for p in points),changed_roles=changes))
    results.append(dict(id=id,stored_root_cells=len(root),emitted_root_cells=len(emitted),context_only_row30_cells=len(guard),maximum_emitted_local_y=29,emission_domain='localY <30, equivalently localY <=29',all_emitted_cells_have_exactly_one_hard_or_stick_role=True,all_row30_cells_have_no_root_collision_channel=True,rows=rows))
assert before=={str(p):sha(p) for p in inputs},'Input changed during boundary audit'
report=dict(utc=datetime.datetime.now(datetime.timezone.utc).isoformat(),scope='Boundary-only independent CPU oracle supplement. Canonical versus composite labels at last root-owned row29 and first preserved upper row30. Existing exact-match channel fixtures are checked against their frozen validation/source hashes.',
    assertions_passed=True,results=results,source_sha256=before,
    context_rule='Morphology dependency radius is3cells. Emission through29 requires canonical context through32; emitting30 would require33. This audit supplies canonical context through35.',
    geometry_note='Runtime-v2 pins canonical row29 androw30. Row29 is emitted with the regenerated root body but is not itself deformed; deformation stops below29. Row30 belongs to the unchanged upper tree.',
    limitations=['No frozen source/evidence modified and no broad suite rerun.','The frozen C# validation previously matched every recorded channel; this supplement validates those exact hash-qualified fixtures and independent boundary labels.','No direct packed upper-span payload, native collider or gameplay binding check.'])
(HERE/'boundary-report.json').write_text(json.dumps(report,indent=2),encoding='utf-8')
print(json.dumps({k:v for k,v in report.items() if k!='source_sha256'},indent=2))
