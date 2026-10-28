from pathlib import Path
import struct,json,hashlib,subprocess,shutil,datetime
import numpy as np
from scipy import ndimage
HERE=Path(__file__).resolve().parent;ROOT=HERE.parents[1]
BASE=ROOT/'.terrain-cache/root-contact-runtime-prototype';VISUAL=ROOT/'.terrain-cache/grounding-root-visual'
sha=lambda p:hashlib.sha256(p.read_bytes()).hexdigest()
DT=np.dtype([('x','<i2'),('y','<i2'),('z','<i2'),('tint','<u4')])
def read(path,miny=-32768,maxy=32767):
    a=np.fromfile(path,DT,offset=4);assert len(a)==struct.unpack('<i',path.read_bytes()[:4])[0]
    return {(int(v['x']),int(v['y']),int(v['z'])):int(v['tint']) for v in a if miny<=v['y']<=maxy}
def write(path,data):
    with path.open('wb') as f:
        f.write(struct.pack('<i',len(data)))
        for p,t in sorted(data.items()):f.write(struct.pack('<hhhI',*p,t))
def oracle(root,context):
    assert {p:t for p,t in root.items() if p[1]>=29}=={p:t for p,t in context.items() if p[1]<=30}
    full={p:t for p,t in root.items() if p[1]<30};full.update(context)
    lo=np.array(list(full)).min(0)-3;hi=np.array(list(full)).max(0)+4
    wood=np.zeros(tuple(hi-lo),bool)
    for p in full:wood[tuple(np.array(p)-lo)]=True
    core=ndimage.binary_erosion(wood,structure=np.ones((3,3,3),bool))
    retained=wood & ndimage.binary_dilation(core,structure=np.ones((5,5,5),bool))
    morphology=retained.copy();center=-lo[[0,2]].astype(float);leader_rows=0
    # Exact current AVPM policy, preserving argwhere's lexicographic tie behavior.
    for y in range(max(0,-lo[1]),wood.shape[1]):
        points=np.argwhere(wood[:,y,:])
        if not len(points):break
        d=((points-center)**2).sum(axis=1);near=points[d<=16]
        if not len(near):break
        closest=points[d.argmin()];local=near[((near-closest)**2).sum(axis=1)<=9]
        center=local.mean(axis=0);keep=points[((points-center)**2).sum(axis=1)<=9]
        retained[keep[:,0],y,keep[:,1]]=True
        if y+lo[1]<=29:leader_rows+=1
    out={};hard=stick=leader_added=0
    for p,t in root.items():
        channel=0 if p[1]>=30 else 1 if retained[tuple(np.array(p)-lo)] else 2
        hard+=channel==1;stick+=channel==2
        leader_added+=channel==1 and not morphology[tuple(np.array(p)-lo)]
        out[p]=(t,channel)
    return out,dict(hard=hard,stick=stick,context_only=sum(p[1]>=30 for p in root),leader_added=int(leader_added),leader_rows=int(leader_rows))
inputs=[BASE/'RootContact.cs',BASE/'report.json',VISUAL/'sites.json',ROOT/'Tools/ExplorationPrototypes/bake_interactions.py']
prior=json.loads((BASE/'report.json').read_text())
for path,digest in prior['source_sha256'].items():
    if path.endswith(('RootContact.cs','v2-expected-10.bin','v2-expected-11.bin','v2-template-10.bin','v2-template-11.bin')):assert sha(Path(path))==digest,'Frozen geometry mismatch'
cases=[]
def add_case(name,rootpath,contextpath,kind,id=None):
    root=read(rootpath,maxy=30);context=read(contextpath,miny=29,maxy=32);out,stats=oracle(root,context)
    path=HERE/(name+'-oracle.bin')
    with path.open('wb') as f:
        f.write(struct.pack('<i',len(out)))
        for p,(t,ch) in sorted(out.items()):f.write(struct.pack('<hhhIB',*p,t,ch))
    row=dict(name=name,root_path=str(rootpath),context_path=str(contextpath),oracle_path=str(path),kind=kind,stats=stats)
    if id is not None:row['id']=id
    cases.append(row);inputs.extend([rootpath,contextpath,path])
for id in [10,11]:
    add_case('actual-'+str(id),BASE/f'v2-expected-{id}.bin',VISUAL/f'wood-{id}-baseline.bin','actual-bounded-builder',id)
    inputs.extend([BASE/f'v2-template-{id}.bin',VISUAL/f'terrain-{id}.bin'])
def synthetic(name,points):
    wood={tuple(p):0xff765432 for p in points};root={p:t for p,t in wood.items() if p[1]<=30};context={p:t for p,t in wood.items() if 29<=p[1]<=32}
    rp=HERE/(name+'-root.bin');cp=HERE/(name+'-context.bin');write(rp,root);write(cp,context);add_case(name,rp,cp,'synthetic-policy-only')
synthetic('thin-shoot',[(0,y,0) for y in range(33)]+[(10,y,0) for y in range(9)])
synthetic('thick-trunk',[(x,y,z) for x in range(-2,3) for y in range(-3,33) for z in range(-2,3)]+[(8,y,0) for y in range(4,7)])
synthetic('leader-drift',[(y//3,y,0) for y in range(33)])
synthetic('leader-remote-stop',[(0,0,0),(5,1,0),(0,2,0)])
synthetic('leader-empty-stop',[(0,0,0),(0,2,0)])
synthetic('leader-tie',[(-2,0,0),(2,0,0),(-3,1,0),(3,1,0),(-4,2,0),(4,2,0)])
synthetic('leader-jump4',[(0,0,0),(4,1,0),(8,2,0)])
synthetic('leader-fractional',[(0,0,0),(1,0,0),(0,0,1),(3,1,0),(0,1,3),(1,2,1),(-2,2,0)])
boundary=[(8,29,10)]+[(x,y,z) for x in range(9,12) for y in range(30,33) for z in range(9,12)]
synthetic('context32-required',boundary)
synthetic('context32-missing-control',[p for p in boundary if p[1]<32])
assert cases[-2]['stats']['hard']==1 and cases[-1]['stats']['hard']==0
manifest=HERE/'manifest.json';manifest.write_text(json.dumps(dict(cases=cases),indent=2));inputs.append(manifest)
dotnet=Path(shutil.which('dotnet'));version=lambda p:tuple(int(n) for n in p.name.split('.'))
sdk=sorted((dotnet.parent/'sdk').glob('8.*'),key=version)[-1];refs=sorted((dotnet.parent/'packs/Microsoft.NETCore.App.Ref').glob('8.*'),key=version)[-1]/'ref/net8.0'
core=sorted((ROOT/'Assets/AshenVoxel/Core').rglob('*.cs'));sources=core+[BASE/'RootContact.cs',HERE/'Interaction.cs',HERE/'Checks.cs'];inputs+=sources+[Path(__file__)]
hashes={str(p):sha(p) for p in inputs};q=lambda p:'"'+str(p)+'"';assembly=HERE/'InteractionChecks.dll'
def compile(target,refs,sources,kind):
    opts=['-nologo','-warnaserror+','-langversion:9.0','-optimize+','-target:'+kind,'-out:'+q(target)]+['-r:'+q(p) for p in refs.glob('*.dll')]+[q(p) for p in sources]
    rsp=target.with_suffix('.rsp');rsp.write_text('\n'.join(opts),encoding='utf-8')
    subprocess.run([str(dotnet),str(sdk/'Roslyn/bincore/csc.dll'),'@'+str(rsp)],check=True)
compile(assembly,refs,sources,'exe')
compile(HERE/'Interaction.netstandard21.dll',dotnet.parent/'packs/NETStandard.Library.Ref/2.1.0/ref/netstandard2.1',sources[:-1],'library')
assembly.with_suffix('.runtimeconfig.json').write_text(json.dumps({'runtimeOptions':{'tfm':'net8.0','framework':{'name':'Microsoft.NETCore.App','version':'8.0.0'}}}))
subprocess.run([str(dotnet),str(assembly),str(HERE),str(BASE),str(VISUAL)],check=True)
assert hashes=={str(p):sha(p) for p in inputs},'Inputs changed during run'
report=json.loads((HERE/'checks.json').read_text());report.update(utc=datetime.datetime.now(datetime.timezone.utc).isoformat(),source_sha256=hashes,oracle_cases=[dict(name=c['name'],kind=c['kind'],**c['stats']) for c in cases],netstandard21_csharp9_compile=True,
    scope='Isolated CPU interaction derivation for two final bounded root results. Hard and stick are disjoint interaction channels; material remains Wood. Canonical rows30..32 are query context only.',
    policy='Exactly bake_interactions.py tree morphology3-cube erosion then5-cube dilation, plus original contiguous-row leader tracking from localY0. Nearest lexicographic x/z tie; near radius4; local and keep radius3.',
    bounds=dict(root_cells_max=65536,context_cells_max=16384,combined_core_cells_max=81920,per_erosion_neighbor_checks_max=27,per_emitted_dilation_neighbor_checks_max=125,leader_point_passes_per_row_max=3,leader_output_rows_max=30),
    limitations=['No native collider/resistance upload, gameplay/edit integration or pack updates.','Canonical context construction/validation/hashing is shared synchronous setup; per-instance validation, erosion, dilation, leader scans and classification are counted continuations.','Candidate counts bound work, not allocator, GC, CPU milliseconds or identity-provider latency.','Single-owner API; revision providers must never reuse identities across source/context changes.','Synthetic fixtures isolate interaction policy and are not all admissible connected root geometries. Actual cases rebuild the frozen bounded root builder first.','The existing AVPM leader policy means contiguous nearby sections, not an independent6-neighbor connectivity proof; the preceding root builder supplies connectivity.','Context completeness and canonical provenance are a caller contract. Missing row32 occupancy can change classification; the explicit control proves it.','Root rows<=29 are derived here. Upper shared geometry/interaction channels are not republished; their existing channels remain a separate integration coherence gate.'])
(HERE/'report.json').write_text(json.dumps(report,indent=2),encoding='utf-8')
print(json.dumps({k:v for k,v in report.items() if k not in ['source_sha256']},indent=2))
