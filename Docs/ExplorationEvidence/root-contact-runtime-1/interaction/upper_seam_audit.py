"""Supplementary CPU oracle audit. Reads frozen inputs; writes only this folder."""
from pathlib import Path
import struct,json,hashlib,datetime
import numpy as np
from scipy import ndimage

HERE=Path(__file__).resolve().parent;ROOT=HERE.parents[1]
GEOMETRY=ROOT/'.terrain-cache/root-contact-runtime-prototype'
VISUAL=ROOT/'.terrain-cache/grounding-root-visual'
sha=lambda p:hashlib.sha256(p.read_bytes()).hexdigest()
DT=np.dtype([('x','<i2'),('y','<i2'),('z','<i2'),('tint','<u4')])
def read(path,maximum):
    a=np.fromfile(path,DT,offset=4)
    with path.open('rb') as f:assert len(a)==struct.unpack('<i',f.read(4))[0]
    return {(int(c['x']),int(c['y']),int(c['z'])):int(c['tint']) for c in a if c['y']<=maximum}
def hash_points(points):
    return hashlib.sha256(b''.join(struct.pack('<iii',*p) for p in sorted(points))).hexdigest()
def classify(cells,lo,hi):
    wood=np.zeros(tuple(hi-lo),bool)
    for p in cells:wood[tuple(np.array(p)-lo)]=True
    core=ndimage.binary_erosion(wood,structure=np.ones((3,3,3),bool))
    morph=wood & ndimage.binary_dilation(core,structure=np.ones((5,5,5),bool))
    hard=morph.copy();center=-lo[[0,2]].astype(float);states={};kept={};stop=None
    for index in range(max(0,-lo[1]),wood.shape[1]):
        y=int(index+lo[1])
        if y>35:stop=dict(y=y,reason='audit context ends at35; not a physical leader termination');break
        points=np.argwhere(wood[:,index,:])
        if not len(points):stop=dict(y=y,reason='empty row');break
        previous=center.copy();distance=((points-center)**2).sum(axis=1);near=points[distance<=16]
        if not len(near):stop=dict(y=y,reason='no point within4cells');break
        closest=points[distance.argmin()];local=near[((near-closest)**2).sum(axis=1)<=9]
        center=local.mean(axis=0);keep=points[((points-center)**2).sum(axis=1)<=9]
        hard[keep[:,0],index,keep[:,1]]=True
        selected={(int(x+lo[0]),y,int(z+lo[2])) for x,z in keep};kept[y]=selected
        states[y]=dict(previous_center_local_xz=(previous+lo[[0,2]]).tolist(),closest_local_xz=(closest+lo[[0,2]]).tolist(),near_count=len(near),local_count=len(local),next_center_local_xz=(center+lo[[0,2]]).tolist(),kept_count=len(keep),kept_sha256=hash_points(selected))
    roles={p:bool(hard[tuple(np.array(p)-lo)]) for p in cells if 30<=p[1]<=32}
    morphology={p:bool(morph[tuple(np.array(p)-lo)]) for p in roles}
    return roles,morphology,states,kept,stop

geometry_report=json.loads((GEOMETRY/'report.json').read_text())
inputs=[Path(__file__),GEOMETRY/'report.json',HERE/'Interaction.cs',ROOT/'Tools/ExplorationPrototypes/bake_interactions.py']
inputs.extend([p for id in [10,11] for p in [VISUAL/f'wood-{id}-baseline.bin',GEOMETRY/f'v2-expected-{id}.bin']])
before_hashes={str(p):sha(p) for p in inputs}
reports=[]
for id in [10,11]:
    original_path=VISUAL/f'wood-{id}-baseline.bin';root_path=GEOMETRY/f'v2-expected-{id}.bin'
    assert sha(root_path)==geometry_report['source_sha256'][str(root_path)],'Frozen root changed'
    assert sha(original_path)==geometry_report['source_sha256'][str(original_path)],'Frozen canonical wood changed'
    original=read(original_path,35);root=read(root_path,29)
    composite={p:t for p,t in original.items() if p[1]>=30};composite.update(root)
    assert {p:t for p,t in original.items() if 30<=p[1]<=35}=={p:t for p,t in composite.items() if 30<=p[1]<=35}
    allpoints=np.array(list(original)+list(composite));lo=allpoints.min(0)-3;hi=allpoints.max(0)+4
    old,oldm,olds,oldk,oldstop=classify(original,lo,hi);new,newm,news,newk,newstop=classify(composite,lo,hi)
    assert old.keys()==new.keys()
    changed=[];morph_changed=[];rows=[]
    for p in sorted(old):
        if old[p]!=new[p]:changed.append(dict(x=p[0],y=p[1],z=p[2],before='hard' if old[p] else 'stick',after='hard' if new[p] else 'stick',tint=f'{original[p]:08x}'))
        if oldm[p]!=newm[p]:morph_changed.append(dict(x=p[0],y=p[1],z=p[2],before_hard=oldm[p],after_hard=newm[p]))
    for y in range(30,33):
        points=[p for p in old if p[1]==y]
        rows.append(dict(y=y,cells=len(points),original_hard=sum(old[p] for p in points),composite_hard=sum(new[p] for p in points),original_stick=sum(not old[p] for p in points),composite_stick=sum(not new[p] for p in points),changed_roles=sum(old[p]!=new[p] for p in points),changed_morphology=sum(oldm[p]!=newm[p] for p in points)))
    seamstates=[]
    for y in [29,30]:
        before=olds.get(y);after=news.get(y);removed=oldk.get(y,set())-newk.get(y,set());added=newk.get(y,set())-oldk.get(y,set())
        seamstates.append(dict(y=y,original=before,composite=after,states_identical=before==after,removed_kept_cells=[list(p) for p in sorted(removed)],added_kept_cells=[list(p) for p in sorted(added)]))
    reports.append(dict(id=id,source_cells_through35=len(original),composite_cells_through35=len(composite),upper_geometry_rgb_30_through35_identical=True,upper_rows=rows,changed_upper_roles=changed,changed_upper_morphology=morph_changed,leader_seam_states=seamstates,leader_state_changed_rows_through32=[y for y in range(33) if olds.get(y)!=news.get(y)],original_leader_stop=oldstop,composite_leader_stop=newstop,upper_rows30_through32_shareable_under_this_oracle=len(changed)==0))
hashes={str(p):sha(p) for p in inputs}
assert hashes==before_hashes,'Inputs changed during seam audit'
report=dict(utc=datetime.datetime.now(datetime.timezone.utc).isoformat(),scope='Supplementary independent NumPy/SciPy CPU policy audit. Original canonical wood versus bounded root<=29 plus unchanged canonical upper>=30, with canonical context through35. No runtime or source changes.',
policy='Same erosion3/dilation5 and supported leader near4/local3/keep3 from localY0 as bake_interactions.py. Both variants use a common array origin to avoid incidental offset-dependent floating rounding.',
results=reports,source_sha256=hashes,
limitations=['This compares exact current policy classifications for rows30..32, not decoded existing AVPM span payloads.','Canonical context through35 is sufficient for morphology on rows<=32. Rows above32 are outside this classification audit.','If leader state differs at30, upper leader effects may persist beyond32 and need further checking before sharing upper channels.','No native collider/resistance, source mutation, upload, LOD, wind or gameplay integration verified.'])
(HERE/'upper-seam-report.json').write_text(json.dumps(report,indent=2),encoding='utf-8')
print(json.dumps({k:v for k,v in report.items() if k!='source_sha256'},indent=2))
