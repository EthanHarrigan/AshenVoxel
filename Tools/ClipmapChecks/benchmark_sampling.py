"""Bounded actual-source SurfaceMaterialSampler benchmark; never opens Unity."""
from pathlib import Path
import datetime, hashlib, json, shutil, subprocess, sys
ROOT=Path(__file__).resolve().parents[2]
out=ROOT/'.terrain-cache/clipmap-sampling'/datetime.datetime.now(datetime.timezone.utc).strftime('%Y%m%d-%H%M%S-%f');out.mkdir(parents=True)
dotnet=Path(shutil.which('dotnet'));version=lambda p:tuple(map(int,p.name.split('.')))
sdk=sorted((dotnet.parent/'sdk').glob('8.*'),key=version)[-1]
refs=sorted((dotnet.parent/'packs/Microsoft.NETCore.App.Ref').glob('8.*'),key=version)[-1]/'ref/net8.0'
sources=sorted((ROOT/'Assets/AshenVoxel/Core').rglob('*.cs'))+[p for p in sorted((ROOT/'Assets/AshenVoxel/WorldScaleData').glob('*.cs')) if not p.name.startswith('TerrainClipmap')]+[Path(__file__).with_name('SamplingBenchmark.cs')]
assets=[ROOT/'Assets/AshenVoxel/Exploration/Samples'/name for name in ['ExplorationMacro.bytes','ExplorationMacro.climate.bytes','ExplorationFine.bytes','ExplorationFine.climate.bytes','ExplorationBiomes.bytes','ExplorationFar40.bytes','ExplorationReshape.bytes']]
def hashes(files):
    result={}
    for p in files:
        with p.open('rb') as f: result[p.relative_to(ROOT).as_posix()]=hashlib.file_digest(f,'sha256').hexdigest()
    return result
source_hashes=hashes(sources);asset_hashes=hashes(assets)
q=lambda p:'"'+str(p)+'"';assembly=out/'SamplingBenchmark.dll';rsp=out/'compile.rsp'
rsp.write_text('\n'.join(['-nologo','-warnaserror+','-langversion:9.0','-optimize+','-target:exe','-out:'+q(assembly)]+['-r:'+q(p) for p in sorted(refs.glob('*.dll'))]+[q(p) for p in sources]),encoding='utf-8')
r=subprocess.run([str(dotnet),str(sdk/'Roslyn/bincore/csc.dll'),'@'+str(rsp)],text=True,capture_output=True);(out/'compile.log').write_text(r.stdout+r.stderr,encoding='utf-8');print(r.stdout+r.stderr,end='');r.check_returncode()
assembly.with_suffix('.runtimeconfig.json').write_text(json.dumps({'runtimeOptions':{'tfm':'net8.0','framework':{'name':'Microsoft.NETCore.App','version':'8.0.0'},'configProperties':{'System.Runtime.TieredCompilation':False}}}))
r=subprocess.run([str(dotnet),str(assembly),str(ROOT),str(out/'result.json')]+sys.argv[1:],text=True,capture_output=True,timeout=150);(out/'console.log').write_text(r.stdout+r.stderr,encoding='utf-8');print(r.stdout+r.stderr,end='');r.check_returncode()
if hashes(sources)!=source_hashes or hashes(assets)!=asset_hashes:raise RuntimeError('Benchmark source or asset changed during run')
report=json.loads((out/'result.json').read_text());report.update(source_sha256=source_hashes,asset_sha256=asset_hashes,generated_utc=datetime.datetime.now(datetime.timezone.utc).isoformat(),jit='TieredCompilation=false; 2048 warmup evaluations per level')
if '--all-levels' in sys.argv:
    report['extrapolation']='100k samples distributed across all15 saved-spawn startup windows; L14 fixed anchor at0,0. Each level contributes512² times its measured mean. Excludes sample array allocation, appearance packing, copying, snapshot and upload work. No measured parallel speedup.'
    report['sampling_mode']='all15_actual_startup_windows'
else:
    report['sampling_mode']='representative_three_windows'
(out/'result.json').write_text(json.dumps(report,indent=2)+'\n',encoding='utf-8');print(out/'result.json')
