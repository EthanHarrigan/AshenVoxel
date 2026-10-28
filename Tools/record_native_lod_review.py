"""Freeze this task's executed native captures and matching source identities.

Run only after the opt-in isolated Unity capture/mesh runs have completed.
Does not manufacture or refresh the older release's acceptance reports.
"""
from pathlib import Path
import datetime,hashlib,json,re,shutil

ROOT=Path(__file__).resolve().parents[1]
COPY=ROOT/'Tools/NativeVerification/bin/Project'
OUT=ROOT/'Docs/NativeLODReview'

def sha(p):return hashlib.sha256(p.read_bytes()).hexdigest()

def main():
    OUT.mkdir(exist_ok=True)
    capture=COPY/'Logs/ExplorationCapture'
    for label in ('overview','ground','redwood'):
        for ext in ('png','json'):
            shutil.copy2(capture/(label+'.'+ext),OUT/('current-'+label+'.'+ext))
    sources={};differences=[]
    for p in (COPY/'Assets').rglob('*'):
        if p.suffix not in ('.cs','.shader','.asmdef'):continue
        rel=p.relative_to(COPY);mirror=ROOT/rel
        if not mirror.exists() or sha(p)!=sha(mirror):differences.append(dict(path=rel.as_posix(),state='workspace changed after capture'))
        sources[rel.as_posix()]=sha(p)
    for p in (ROOT/'Assets').rglob('*.cs'):
        if not (COPY/p.relative_to(ROOT)).exists():differences.append(dict(path=p.relative_to(ROOT).as_posix(),state='new concurrent workspace source, not tested'))
    for rel in ('Assets/AshenVoxel/Exploration/VegetationStreaming.cs','Assets/AshenVoxel/Exploration/ForestHorizon.cs','Assets/AshenVoxel/Exploration/ForestBillboard.cs','Assets/AshenVoxel/Exploration/ForestImpostor.shader','Assets/AshenVoxel/WorldScaleData/ExplorationMovement.cs','Assets/AshenVoxel/WorldScaleData/ExplorationPrototypePack.cs'):
        assert sha(ROOT/rel)==sha(COPY/rel),'Task-owned gameplay changed after capture: '+rel
    assets={}
    for folder in ('Assets/AshenVoxel/Exploration/Resources','Assets/AshenVoxel/Exploration/Samples','Assets/AshenVoxel/Exploration/Prototypes'):
        for p in (ROOT/folder).rglob('*'):
            if p.is_file():
                rel=p.relative_to(ROOT);assert sha(p)==sha(COPY/rel),'Native asset differs: '+str(rel)
                assets[rel.as_posix()]=sha(p)
    for rel in ('Assets/AshenVoxel/Settings/ExplorationSettings.asset','Packages/manifest.json','ProjectSettings/ProjectVersion.txt'):
        assets[rel]=sha(COPY/rel)
        if sha(ROOT/rel)!=sha(COPY/rel):differences.append(dict(path=rel,state='workspace asset/settings changed after capture'))
    lognames=('native-baseline.log','native-atlas.log','native-mesh-checks.log','native-lod-current.log')
    logs={};observations=[]
    for name in lognames:
        p=ROOT/'Logs'/name;s=p.read_text(encoding='utf-8',errors='replace');logs['Logs/'+name]=sha(p)
        assert 'error CS' not in s and 'Shader error' not in s,name
        # EditorApplication.Exit(0) capture runs do not print the CLI -quit
        # success banner. Require observed shutdown plus completed capture data.
        assert 'Cleanup mono' in s,name
        observations.extend(name+': '+line for line in s.splitlines() if 'Exception:' in line or 'all mesh regression checks passed' in line or 'Forest atlas baked:' in line)
    assert 'all mesh regression checks passed' in (ROOT/'Logs/native-mesh-checks.log').read_text(encoding='utf-8')
    (OUT/'native-editor-observations.txt').write_text('\n'.join(observations)+'\n',encoding='utf-8')
    reports=[]
    for name in ('exploration-movement-validation.json','exploration-population-validation.json','exploration-streaming-validation.json','exploration-smoke-validation.json','tree-validation.json','shading-validation.json'):
        p=ROOT/'Docs'/name;data=json.loads(p.read_text(encoding='utf-8'));assert data['status']=='PASS'
        for rel,expected in data.get('source_sha256',{}).items():
            rel=rel.replace('\\','/')
            if rel.startswith('Assets/'):
                assert (COPY/rel).exists() and sha(COPY/rel)==expected,'Report is not for the tested source snapshot: '+name+' '+rel
        reports.append(dict(path='Docs/'+name,sha256=sha(p),checks=data.get('checks'),scope=data.get('scope')))
    captures={p.name:sha(p) for p in OUT.iterdir() if p.suffix in ('.png','.json') and p.name!='native-lod-review.json'}
    views=[json.loads((OUT/('current-'+v+'.json')).read_text()) for v in ('overview','ground','redwood')]
    assert all(not v['impostor_shader_errors'] and not v['horizon_error'] and v['horizon_pending']==0 and v['horizon_stands']>0 for v in views)
    result=dict(status='PASS',generated_utc=datetime.datetime.now(datetime.timezone.utc).isoformat(),scope='Actual Windows Unity 6000.3.27f1 D3D11 atlas, static Play camera captures and mesh regressions in the frozen isolated project copy; not subsequent concurrent workspace edits, interactive input, standalone or FPS/GPU profiling',workspace_differences_after_native_capture=differences,source_sha256=sources,asset_sha256=assets,capture_sha256=captures,local_raw_log_sha256=logs,validation_reports=reports,views=views,remaining=['Reimport and test concurrent voxel-terrain integration','Interactive input and walking feel','DX12','Standalone build','Temporal/view-angle transitions','Baked-light and billboard depth/normal approximation','Coarse stand/terrain material quality','GPU/overdraw/VRAM and frame-time distributions','New release freshness audit'])
    (OUT/'native-lod-review.json').write_text(json.dumps(result,indent=2)+'\n',encoding='utf-8')
    print('PASS native review: frozen source snapshot, three native views, real shader flags, native mesh regressions; '+str(len(differences))+' later workspace differences explicitly excluded; no FPS claim')

if __name__=='__main__':main()
