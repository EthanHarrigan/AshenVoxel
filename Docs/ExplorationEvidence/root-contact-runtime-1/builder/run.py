from pathlib import Path
import struct,json,math,hashlib,subprocess,shutil,datetime,collections
HERE=Path(__file__).resolve().parent; ROOT=HERE.parents[1]
EVIDENCE=ROOT/'.terrain-cache/grounding-root-visual'
digest=lambda p:hashlib.sha256(p.read_bytes()).hexdigest()
sites=json.loads((EVIDENCE/'sites.json').read_text()); inputs=[]; fixture_reports=[]
def cells(path):
    raw=path.read_bytes(); n=struct.unpack_from('<i',raw)[0];assert len(raw)==4+n*10
    return {(x,y,z):t for x,y,z,t in struct.iter_unpack('<hhhI',raw[4:]) if y<=30}
def cost(path):
    raw=path.read_bytes(); n=struct.unpack_from('<i',raw)[0]; off=4;out={}
    for _ in range(n):
        x,z,target,count=struct.unpack_from('<hhhi',raw,off);off+=10
        for _ in range(count):
            y,t=struct.unpack_from('<hI',raw,off);off+=6;out[x,y,z]=t
    assert off==len(raw);return out
def columns(data):
    out=collections.defaultdict(list)
    for (x,y,z),t in data.items():out[x,z].append((y,t))
    for values in out.values():values.sort()
    return out
for site in sites:
    id=site['id']; original_path=EVIDENCE/f'wood-{id}-baseline.bin';costpath=EVIDENCE/f'continuous-shaft/cost-{id}.bin'; terrainpath=EVIDENCE/f'terrain-{id}.bin'
    inputs.extend([original_path,costpath,terrainpath,EVIDENCE/f'continuous-shaft/expected-{id}.bin'])
    original=cells(original_path);oldbase=cost(costpath)
    pinned={p:t for p,t in original.items() if p[1]>=29};shaft={(x,z) for x,y,z in pinned if y==29}
    base={p:t for p,t in oldbase.items() if p[1]<29};base.update(pinned)
    base.update({p:t for p,t in original.items() if p[1]<29 and (p[0],p[2]) in shaft})
    raw=terrainpath.read_bytes();gx0,gz0,side=struct.unpack_from('<qqi',raw)
    co=math.cos(math.radians(site['yaw']));si=math.sin(math.radians(site['yaw']));radius=(abs(co)+abs(si))*.5;ground={}
    def target(x,z):
        cx=site['x']*10+co*(x+.5)+si*(z+.5);cz=site['z']*10-si*(x+.5)+co*(z+.5);low=math.inf;high=-math.inf
        for gz in range(math.floor(cz-radius),math.ceil(cz+radius)):
            for gx in range(math.floor(cx-radius),math.ceil(cx+radius)):
                dx,dz=gx+.5-cx,gz+.5-cz;limit=.5+radius-1e-9
                if abs(dx)>=limit or abs(dz)>=limit or abs(co*dx-si*dz)>=limit or abs(si*dx+co*dz)>=limit:continue
                assert 0<=gx-gx0<side and 0<=gz-gz0<side
                bare=struct.unpack_from('<q',raw,20+((gz-gz0)*side+gx-gx0)*24+8)[0];low=min(low,(bare+1)*.1);high=max(high,(bare+1)*.1)
        assert math.isfinite(low);ground[x,z]=(low,high);return math.floor((low-site['original_y'])/.1+1e-7)
    result={}; cols=columns(base);maxdeform=0
    with (HERE/f'v2-template-{id}.bin').open('wb') as f:
        f.write(struct.pack('<i',len(cols)))
        for (x,z),col in sorted(cols.items()):
            bottom=col[0][0];t=target(x,z) if bottom<29 else bottom;maxdeform=max(maxdeform,abs(t-bottom))
            f.write(struct.pack('<hhhi',x,z,t,len(col)))
            for y,tint in col:
                f.write(struct.pack('<hI',y,tint))
                if y>=29:result[x,y,z]=tint;continue
                ratio=(29-t)/(29-bottom)
                lo=math.floor(t+(y-bottom)*ratio+1e-7);hi=min(29,math.ceil(t+(y+1-bottom)*ratio-1e-7))
                for ny in range(lo,hi):result[x,ny,z]=tint
    with (HERE/f'v2-expected-{id}.bin').open('wb') as f:
        f.write(struct.pack('<i',len(result)))
        for (x,y,z),t in sorted(result.items()):f.write(struct.pack('<hhhI',x,y,z,t))
    assert {p:t for p,t in result.items() if p[1]>=29}==pinned
    reached={p for p in result if p[1]==30};pending=collections.deque(reached)
    while pending:
        x,y,z=pending.popleft()
        for dx,dy,dz in [(1,0,0),(-1,0,0),(0,1,0),(0,-1,0),(0,0,1),(0,0,-1)]:
            q=(x+dx,y+dy,z+dz)
            if q in result and q not in reached:reached.add(q);pending.append(q)
    gaps=[];burial=[];max_overlap_delta=0
    for xz,col in columns(result).items():
        if col[0][0]>=29:continue
        low,high=ground[xz];bottom=site['original_y']+col[0][0]*.1;gaps.append(max(0,low*-1+bottom));burial.append(high-bottom)
        source_bottom=cols[xz][0][0]
        max_overlap_delta=max(max_overlap_delta,*(abs(math.floor((h-site['original_y'])/.1+1e-7)-source_bottom) for h in (low,high)))
    assert len(result)==len(reached) and max(gaps)<1e-8
    fixture_reports.append(dict(id=id,columns=len(cols),source_cells=len(base),expected_cells=len(result),max_target_deformation_cells=maxdeform,max_each_overlap_ground_delta_cells=max_overlap_delta,pinned_rows_29_30_equal_canonical=True,disconnected_cells=len(result)-len(reached),contact_columns_checked=len(gaps),max_basal_gap_m=max(gaps),max_basal_burial_m=max(burial),contact_definition='Each deformed column bottom compared with minimum and maximum bare ground tops of ALL positively overlapping global10cm cells. Burial is maximum across those cells, not center-only.'))
    for name in ['template','expected']:inputs.append(HERE/f'v2-{name}-{id}.bin')
dotnet=Path(shutil.which('dotnet'));version=lambda p:tuple(int(n) for n in p.name.split('.'))
sdk=sorted((dotnet.parent/'sdk').glob('8.*'),key=version)[-1];refs=sorted((dotnet.parent/'packs/Microsoft.NETCore.App.Ref').glob('8.*'),key=version)[-1]/'ref/net8.0'
sources=sorted((ROOT/'Assets/AshenVoxel/Core').rglob('*.cs'))+[HERE/'RootContact.cs',HERE/'Checks.cs']
inputs+=sources+[Path(__file__),EVIDENCE/'sites.json',EVIDENCE/'grounded-camera/manifest.json',EVIDENCE/'independent-review.json']
hashes={str(p):digest(p) for p in inputs}
assembly=HERE/'RootContactChecks.dll';q=lambda p:'"'+str(p)+'"'
options=['-nologo','-warnaserror+','-langversion:9.0','-optimize+','-target:exe','-out:'+q(assembly)]+['-r:'+q(p) for p in refs.glob('*.dll')]+[q(p) for p in sources]
rsp=HERE/'compile.rsp';rsp.write_text('\n'.join(options),encoding='utf-8')
subprocess.run([str(dotnet),str(sdk/'Roslyn/bincore/csc.dll'),'@'+str(rsp)],check=True)
standardrefs=dotnet.parent/'packs/NETStandard.Library.Ref/2.1.0/ref/netstandard2.1'
standard_sources=sorted((ROOT/'Assets/AshenVoxel/Core').rglob('*.cs'))+[HERE/'RootContact.cs']
standardoptions=['-nologo','-warnaserror+','-langversion:9.0','-optimize+','-target:library','-out:'+q(HERE/'RootContact.netstandard21.dll')]+['-r:'+q(p) for p in standardrefs.glob('*.dll')]+[q(p) for p in standard_sources]
standardrsp=HERE/'netstandard21.rsp';standardrsp.write_text('\n'.join(standardoptions),encoding='utf-8')
subprocess.run([str(dotnet),str(sdk/'Roslyn/bincore/csc.dll'),'@'+str(standardrsp)],check=True)
assembly.with_suffix('.runtimeconfig.json').write_text(json.dumps({'runtimeOptions':{'tfm':'net8.0','framework':{'name':'Microsoft.NETCore.App','version':'8.0.0'}}}))
subprocess.run([str(dotnet),str(assembly),str(HERE),str(EVIDENCE)],check=True)
assert hashes=={str(p):digest(p) for p in inputs},'Inputs changed during validation'
report=json.loads((HERE/'checks.json').read_text());report.update(utc=datetime.datetime.now(datetime.timezone.utc).isoformat(),fixture_generation=fixture_reports,source_sha256=hashes,netstandard21_csharp9_compile=True,
policy_caps=dict(max_deformation_cells_each_overlap=20,max_occupied_including_guard=65536,max_allocated_chunks=128,max_raw_unmerged_faces=65536,max_columns=8192,max_source_identity_characters=4096,local_xz_bounds=[-128,127],source_y_bounds=[-64,30],minimum_source_to_pinned_seam_clearance='target < seam-3 (strict)',emission_and_collision_maximum_local_y=29,guard_local_y=30,query_candidates_per_root_column_maximum=9,face_candidates_per_occupied_cell=6,connectivity_visited_and_queue_maximum=65536,connectivity_candidate_bound='One seed candidate per occupied cell plus6 neighbor candidates per reached cell; shares Faces Step budget, separate counters; requires every cell reachable from pinned seam rows before publication.'),
scope='Isolated engine-free .NET8 CPU prototype using actual Core.VoxelWorld.Set. Terrain is exact prerecorded actual-world bare ground. No Unity/native/render/collision adoption.',
limitations=['Unit scale only; no runtime pack template export/loading hook.','Step budgets bound candidates, not milliseconds, allocation latency, provider call latency or GC. Shared template validation/hashing is synchronous once per template.','No asynchronous worker/queue/cache, production integration, mesh upload, gameplay collision or authoritative world edit integration.','Raw faces are a conservative unmerged admission count; parent separately owns emission-domain mesher validation.','Geometry/matter volume changes require a production generator version and persisted provenance decision. No release identity assigned.','Provider must never reuse a source identity across mutations; API is single-owner, not thread-safe.','Only two natural mature sites plus synthetic gates; broad steep terrain, LOD, wind, lighting, live edit and native visual gates remain.'])
(HERE/'report.json').write_text(json.dumps(report,indent=2),encoding='utf-8')
print(json.dumps({k:v for k,v in report.items() if k not in ['source_sha256']},indent=2))
