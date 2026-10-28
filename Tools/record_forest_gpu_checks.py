"""Freeze actual GPU acceptance, source identities and the 100km scene capture.

Requires the completed native D3D11/D3D12 ForestGpuChecks and scene/mesh runs.
Never re-labels the previous local LOD review or frozen gameplay release.
"""
from pathlib import Path
import datetime,hashlib,json,shutil

ROOT=Path(__file__).resolve().parents[1]
PROJECT=ROOT/'Tools/ForestGpuChecks/bin/Project'
OUT=ROOT/'Docs/ForestGpuReview'
def sha(p):return hashlib.sha256(p.read_bytes()).hexdigest()

def main():
    OUT.mkdir(exist_ok=True)
    result=json.loads((PROJECT/'Logs/ForestGpuChecks/validation.json').read_text())
    assert result['api']=='Direct3D12' and result['checks']==10
    shutil.copy2(PROJECT/'Logs/ForestGpuChecks/validation.json',OUT/'native-d3d12.json')
    shutil.copy2(PROJECT/'Logs/ForestGpuChecks/indirect-100km.png',OUT/'indirect-d3d12.png')
    for api in ('d3d11','d3d12'):
        report=json.loads((OUT/('native-'+api+'.json')).read_text())
        assert report['checks']==10 and report['visible_30km']==1 and report['visible_100km']==3 and report['visible_after_turn']==1 and report['visible_after_rebase']==3 and report['tree_pixels']>100
    scene=json.loads((OUT/'scene-100km-d3d11.json').read_text())
    assert scene['horizon_gpu'] and not scene['gpu_shader_errors'] and not scene['horizon_error']
    assert scene['forest_published_m']==100000 and scene['forest_farthest_stand_m']>80000 and scene['horizon_draws']==1 and scene['gpu_visible']>0
    sources={};assets={}
    for p in (ROOT/'Assets').rglob('*'):
        if not p.is_file():continue
        if p.suffix not in ('.cs','.shader','.compute','.asmdef') and 'Exploration/Resources' not in p.as_posix() and p.name not in ('ExplorationPrototypes.bytes','ExplorationSettings.asset'):continue
        rel=p.relative_to(ROOT);mirror=PROJECT/rel
        assert mirror.is_file() and sha(p)==sha(mirror),'Native source/asset differs: '+str(rel)
        (sources if p.suffix in ('.cs','.shader','.compute','.asmdef') else assets)[rel.as_posix()]=sha(p)
    logs={}
    for name in ('forest-gpu-native.log','forest-gpu-native-d3d12.log','forest-gpu-scene.log','forest-gpu-mesh.log'):
        p=ROOT/'Logs'/name;content=p.read_text(encoding='utf-8',errors='replace')
        assert 'error CS' not in content and 'Shader error' not in content,name
        if name!='forest-gpu-scene.log':assert 'return code 0' in content,name
        logs['Logs/'+name]=sha(p)
    assert 'all mesh regression checks passed' in (ROOT/'Logs/forest-gpu-mesh.log').read_text(encoding='utf-8')
    logs['Logs/forest-gpu-native-failed-counter-offset.log']=sha(ROOT/'Logs/forest-gpu-native-failed-counter-offset.log')
    reports={}
    for name in ('exploration-streaming-validation.json','exploration-smoke-validation.json','exploration-movement-validation.json'):
        p=ROOT/'Docs'/name;d=json.loads(p.read_text());assert d['status']=='PASS'
        for rel,expected in d.get('source_sha256',{}).items():
            if rel.startswith('Assets/'):assert sha(ROOT/rel)==expected,'Stale source check: '+name+' '+rel
        reports['Docs/'+name]=dict(sha256=sha(p),checks=d.get('checks'),scope=d.get('scope'))
    files={p.name:sha(p) for p in OUT.iterdir() if p.suffix in ('.png','.json') and p.name!='validation.json'}
    report=dict(status='PASS',generated_utc=datetime.datetime.now(datetime.timezone.utc).isoformat(),scope='Native Unity 6000.3.27f1 compute, indirect draw counters and rendered pixels on RTX3080Ti D3D11/D3D12; full Play100km source scene on D3D11. Not GPU/FPS distribution profiling.',source_sha256=sources,asset_sha256=assets,executed_report_sha256=files,local_log_sha256=logs,headless_reports=reports,scene=scene,graphics_checks_per_api=10,placement_policy='Immutable CPU terrain/biome snapshots and bounded background worker; GPU performs frustum/range culling, append compaction, indirect counts and procedural drawing',limits=['Approximate coarse stand positions and baked lighting','Terrain/fog and world boundaries still apply','GPU/overdraw/VRAM/FPS distributions unmeasured','No CUDA/Python dependency or terrain generation was started'])
    (OUT/'validation.json').write_text(json.dumps(report,indent=2)+'\n',encoding='utf-8')
    print('PASS forest GPU review: matching native source, D3D11/D3D12 compute/draw checks, 100km source scene and native mesh checks; no FPS claim')

if __name__=='__main__':main()
