from pathlib import Path
import datetime,hashlib,json,math

HERE=Path(__file__).resolve().parent;ROOT=HERE.parents[1]
def sha(path):
    h=hashlib.sha256()
    with path.open('rb') as stream:
        for block in iter(lambda:stream.read(1024*1024),b''):h.update(block)
    return h.hexdigest()
verified={}
for name in ['seam30-manifest.json','aligned-manifest.json']:
    manifest=json.loads((HERE/name).read_text(encoding='utf8'))
    for rel,expected in manifest['sha256'].items():
        p=HERE/rel;actual=sha(p)
        assert actual==expected,f'Frozen output changed: {p}'
        verified[str(p)]=actual
for name in ['proposed-cut-provenance.json','dependency-provenance.json']:
    provenance=json.loads((HERE/name).read_text(encoding='utf8'))
    for path,expected in provenance['source_and_input_sha256'].items():
        p=Path(path);actual=sha(p)
        assert actual==expected,f'Input changed after run: {p}'
        verified[str(p)]=actual
clipping=json.loads((HERE/'clipping-counterexample.json').read_text(encoding='utf8'))
for path,expected in clipping['source_sha256'].items():
    p=Path(path);actual=sha(p)
    assert actual==expected,f'Shader-model input changed: {p}'
    verified[str(p)]=actual
bake=json.loads((ROOT/'Docs/exploration-prototype-bake.json').read_text(encoding='utf8'))
known={k.replace('\\','/'):v for k,v in bake['bakes'][0]['source_sha256'].items()}
matched={}
for name in ['Tools/ExplorationPrototypes/ExportPrototypes.cs','Assets/AshenVoxel/Rendering/GreedyChunkMesher.cs','Tools/ExplorationPrototypes/UnityShim.cs','Assets/AshenVoxel/Core/VoxelWorld.cs','Assets/AshenVoxel/Core/VertexAppearance.cs','Assets/AshenVoxel/Core/VoxelColor.cs','Assets/AshenVoxel/Core/Materials.cs']:
    actual=sha(ROOT/name);assert actual==known[name],name+' differs from historical baker'
    matched[name]=actual
anchors=[]
for record in bake['records']:
    if record.get('id') not in (10,11):continue
    p=record['generation']['profile'];a=p['Age']
    reach=p['CrownRadius']*a**.85*1.5+p['LeafClusterRadius']*a**.65*3+p['TrunkRadius']*a**1.15*(1.7+p['RootFlare']*1.1)+3
    side=math.ceil(reach*2/.1);side+=side%2
    anchors.append({'prototype_id':record['id'],'source_dimensions':[side,math.ceil((p['Height']*a*1.25+4)/.1),side],'source_anchor':[side//2,4,side//2],'basis':'ExportPrototypes formula and historical bake profile, not a new generation run'})
names=['ProposedCutChecks.cs','proposed_cut_run.py','proposed-cuts.json','proposed-cut-provenance.json','proposed-cut-compile.log','proposed-cut-run.log','DependencyChecks.cs','dependency_run.py','dependency-results.json','dependency-provenance.json','dependency-compile.log','dependency-run.log','clipping_check.py','clipping-counterexample.json','DEPENDENCY_ANALYSIS.md','finalize_dependency.py']
output=HERE/'dependency-manifest.json'
assert not output.exists(),'Do not overwrite a frozen manifest'
manifest={'utc':datetime.datetime.now(datetime.timezone.utc).isoformat(),'status':'FROZEN supplemental CPU analysis; no runtime integration',
 'mechanically_supported_cuts':[30,32,36,60,124],'mechanical_assertions':36016006,'dependency_assertions':33839,'rational_clipping_assertions':25,
 'unchanged_frozen_inputs':verified,'historical_baker_source_matches':matched,'derived_anchors':anchors,
 'sha256':{name:sha(HERE/name) for name in names},'limits':'Upper face invariance is conditional on exact canonical coarse representatives and complete halo. No composite root LOD, HLSL/native render, runtime queue or FPS acceptance.'}
output.write_text(json.dumps(manifest,indent=2),encoding='utf8')
print(f'PASS: {len(verified)} frozen/input files unchanged; {len(matched)} historical baker sources match; {len(names)} new evidence files frozen.')
print('dependency-manifest.json SHA256 '+sha(output))
