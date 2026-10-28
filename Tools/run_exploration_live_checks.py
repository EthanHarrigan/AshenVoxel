#!/usr/bin/env python3
"""Check immutable live decoded-terrain publication, guard/cache limits and actual runtime integration."""
from pathlib import Path
import argparse,datetime,hashlib,json,os,re,shutil,subprocess
ROOT=Path(__file__).resolve().parents[1]
def main():
    parser=argparse.ArgumentParser(description=__doc__);parser.add_argument('--live-url');parser.add_argument('--report',type=Path,default=ROOT/'Docs/exploration-live-validation.json');args=parser.parse_args()
    dotnet=Path(shutil.which('dotnet') or ROOT.parent/'.dotnet/dotnet').resolve()
    sdk=sorted((dotnet.parent/'sdk').glob('8.*'))[-1];refs=sorted((dotnet.parent/'packs/Microsoft.NETCore.App.Ref').glob('8.*'))[-1]/'ref/net8.0'
    out=ROOT/'Tools/ExplorationLiveChecks/bin';out.mkdir(parents=True,exist_ok=True);assembly=out/'ExplorationLiveChecks.dll'
    files=sorted((ROOT/'Assets/AshenVoxel/Core').rglob('*.cs'))+sorted((ROOT/'Assets/AshenVoxel/WorldScaleData').rglob('*.cs'))
    files += sorted((ROOT/'Assets/AshenVoxel/WorldScaleRuntime').glob('WorldExplorer*.cs'))
    files += [ROOT/'Assets/AshenVoxel/WorldScaleRuntime'/name for name in ['RemoteTerrainField.cs','VisualWater.cs']]
    files += [ROOT/'Assets/AshenVoxel/Exploration/ExplorationLiveRefinement.cs', ROOT/'Tools/WorldExplorerChecks/UnityShim.cs',ROOT/'Tools/AuthoringChecks/ProfileShim.cs',ROOT/'Tools/ExplorationLiveChecks/Program.cs']
    hashes={str(p.relative_to(ROOT)):hashlib.sha256(p.read_bytes()).hexdigest() for p in files}
    quote=lambda p:'"'+str(p)+'"'
    options=['-nologo','-warnaserror+','-langversion:9.0','-optimize+','-target:exe','-out:'+quote(assembly)]+['-r:'+quote(p) for p in sorted(refs.glob('*.dll'))]+[quote(p) for p in files]
    rsp=out/'compile.rsp';rsp.write_text('\n'.join(options));(out/'ExplorationLiveChecks.runtimeconfig.json').write_text(json.dumps({'runtimeOptions':{'tfm':'net8.0','framework':{'name':'Microsoft.NETCore.App','version':'8.0.0'}}}))
    subprocess.run([str(dotnet),str(sdk/'Roslyn/bincore/csc.dll'),'@'+str(rsp)],check=True)
    samples=ROOT/'Assets/AshenVoxel/Exploration/Samples';assets={str(p.relative_to(ROOT)):hashlib.sha256(p.read_bytes()).hexdigest() for p in samples.glob('*') if p.suffix in ['.bytes','.json']}
    command=[str(dotnet),str(assembly),str(samples)]
    if args.live_url:command.append(args.live_url)
    result=subprocess.run(command,text=True,capture_output=True);print(result.stdout,end='');print(result.stderr,end='');result.check_returncode()
    if hashes!={str(p.relative_to(ROOT)):hashlib.sha256(p.read_bytes()).hexdigest() for p in files}:raise RuntimeError('Source changed during exploration live checks')
    if assets!={str(p.relative_to(ROOT)):hashlib.sha256(p.read_bytes()).hexdigest() for p in samples.glob('*') if p.suffix in ['.bytes','.json']}:raise RuntimeError('Bundled neural assets changed during checks')
    count=int(re.search(r'All (\d+) exploration live refinement checks passed',result.stdout)[1])
    report={'status':'PASS','checks':count,'generated_utc':datetime.datetime.now(datetime.timezone.utc).isoformat(),'scope':'Actual engine-free terrain compositor and runtime C# with Unity API substitutes; controlled safety fixtures and bundled actual learned terrain. Not native Unity, URP visual testing or an FPS measurement.','source_sha256':hashes,'asset_sha256':assets,'sdk':sdk.name}
    if args.live_url:report['actual_live_probe']=json.loads(next(line.split('ACTUAL_LIVE_PROBE ',1)[1] for line in result.stdout.splitlines() if line.startswith('ACTUAL_LIVE_PROBE ')))
    args.report.parent.mkdir(parents=True,exist_ok=True)
    args.report.write_text(json.dumps(report,indent=2)+'\n')
if __name__=='__main__':main()
