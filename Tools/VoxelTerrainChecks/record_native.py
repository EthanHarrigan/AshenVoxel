"""Record an actually completed isolated native run without relabeling earlier runs."""
from pathlib import Path
import datetime,hashlib,json,shutil
ROOT=Path(__file__).resolve().parents[2]
def sha(p):return hashlib.sha256(p.read_bytes()).hexdigest()
def main():
    project=ROOT/'.terrain-cache/voxel-native-project';run=project/'Logs/VoxelNativeAttempt2';out=ROOT/'Docs/VoxelTerrainEvidence'
    report=json.loads((run/'validation.json').read_text(encoding='utf-8'));assert report['status']=='PASS'
    sources={};differences=[]
    for p in (project/'Assets').rglob('*'):
        if p.suffix not in ('.cs','.shader','.asmdef'):continue
        relative=p.relative_to(project).as_posix();sources[relative]=sha(p)
        current=ROOT/relative
        if p.name=='VoxelNativeCheck.cs':continue
        if not current.exists() or sha(current)!=sha(p):differences.append(relative)
    for name in ['Assets/AshenVoxel/WorldScaleData/VoxelTerrainPage.cs','Assets/AshenVoxel/Exploration/ExplorationVoxelTerrain.cs',
                 'Assets/AshenVoxel/WorldScaleRuntime/WorldExplorerIntegration.cs','Assets/AshenVoxel/WorldScaleRuntime/GroundSurface.shader']:
        assert sha(ROOT/name)==sha(project/name),'Terrain runtime changed after native snapshot: '+name
    for p in (ROOT/'Assets').rglob('*'):
        if p.suffix in ('.cs','.shader','.compute','.asmdef') and not (project/p.relative_to(ROOT)).exists():
            differences.append(p.relative_to(ROOT).as_posix()+' (added after snapshot)')
    for label in ['close','transition']:shutil.copy2(run/(label+'.png'),out/('native-'+label+'.png'))
    shutil.copy2(project/'native-run-2.log',out/'native-run-2.log')
    shutil.copy2(project/'Assets/AshenVoxel/Exploration/Editor/VoxelNativeCheck.cs',out/'native-harness.cs')
    report.update(generated_utc=datetime.datetime.now(datetime.timezone.utc).isoformat(),attempt=2,
                  source_sha256=sources,workspace_differences_after_snapshot=differences,
                  screenshots_sha256={label:sha(out/('native-'+label+'.png')) for label in ['close','transition']},
                  note='Controlled native fixture only. First import-time harness timeout is preserved in native-attempt1. Proxy fixture uses a deliberately constant color, so its material seam is not an actual-world palette comparison.')
    (out/'native-validation.json').write_text(json.dumps(report,indent=2)+'\n',encoding='utf-8')
    print('Recorded real native attempt2; voxel conversion, residency and mask source match. Other differences:',differences)
if __name__=='__main__':main()
