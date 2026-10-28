#!/usr/bin/env python3
"""Compile/drive the actual playable exploration boot with real bundled assets, no Unity/GPU."""
from pathlib import Path
import argparse,datetime,hashlib,json,os,re,shutil,subprocess,tempfile
ROOT=Path(__file__).resolve().parents[1]
def ver(p):return tuple(int(x) if x.isdigit() else 0 for x in p.name.split('.'))
def sha(p):return hashlib.sha256(p.read_bytes()).hexdigest()
def main():
 p=argparse.ArgumentParser(description=__doc__);p.add_argument('--keep-user-options',action='store_true');a=p.parse_args()
 opts=[Path(os.environ['DOTNET_ROOT'])/'dotnet' if os.environ.get('DOTNET_ROOT') else None,Path(shutil.which('dotnet')) if shutil.which('dotnet') else None,ROOT.parent/'.dotnet/dotnet']
 dotnet=next(x.resolve() for x in opts if x and x.is_file());sdk=sorted((dotnet.parent/'sdk').glob('8.*'),key=ver)[-1];refs=sorted((dotnet.parent/'packs/Microsoft.NETCore.App.Ref').glob('8.*'),key=ver)[-1]/'ref/net8.0'
 out=ROOT/'Tools/ExplorationSmokeChecks/bin';out.mkdir(parents=True,exist_ok=True)
 files=sorted((ROOT/'Assets/AshenVoxel/Core').rglob('*.cs'))+sorted((ROOT/'Assets/AshenVoxel/WorldScaleData').glob('*.cs'))
 files+=[ROOT/'Assets/AshenVoxel/WorldScaleRuntime'/n for n in ['WorldExplorer.cs','WorldExplorerIntegration.cs','WorldExplorerVoxels.cs','RemoteTerrainField.cs','VisualWater.cs']]
 files+=sorted((ROOT/'Assets/AshenVoxel/Exploration').glob('*.cs'))+sorted((ROOT/'Tools/ExplorationSmokeChecks').glob('*.cs'))
 hashes={x.relative_to(ROOT).as_posix():sha(x) for x in files};q=lambda x:'"'+str(x)+'"';assembly=out/'ExplorationSmokeChecks.dll'
 options=['-nologo','-warnaserror+','-langversion:9.0','-optimize+','-target:exe','-out:'+q(assembly)]+['-r:'+q(x) for x in sorted(refs.glob('*.dll'))]+[q(x) for x in files]
 rsp=out/'compile.rsp';rsp.write_text('\n'.join(options));subprocess.run([str(dotnet),str(sdk/'Roslyn/bincore/csc.dll'),'@'+str(rsp)],check=True)
 assembly.with_suffix('.runtimeconfig.json').write_text(json.dumps({'runtimeOptions':{'tfm':'net8.0','framework':{'name':'Microsoft.NETCore.App','version':'8.0.0'}}}))
 guids={}
 for meta in (ROOT/'Assets').rglob('*.meta'):
  m=re.search(r'^guid: ([0-9a-f]{32})$',meta.read_text(),re.M)
  if m:guids[m[1]]=str(meta.with_suffix(''))
 settings=ROOT/'Assets/AshenVoxel/Settings/ExplorationSettings.asset';config=out/'input.json';config.write_text(json.dumps({'root':str(ROOT),'settings':str(settings),'scene':str(ROOT/'Assets/Scenes/Exploration.unity')}));guidfile=out/'guid-paths.json';guidfile.write_text(json.dumps(guids))
 assetpaths=[settings,ROOT/'Assets/Scenes/Exploration.unity']+sorted((ROOT/'Assets/AshenVoxel/Exploration/Samples').glob('*.bytes'))+[ROOT/'Assets/AshenVoxel/Exploration/Samples/exploration-world.json',ROOT/'Assets/AshenVoxel/Exploration/Prototypes/ExplorationPrototypes.bytes']
 assethashes={x.relative_to(ROOT).as_posix():sha(x) for x in assetpaths}
 report=ROOT/'Docs/exploration-smoke-validation.json'
 with tempfile.TemporaryDirectory(prefix='ashen-exploration-smoke-') as data:
  subprocess.run([str(dotnet),str(assembly),str(report),str(config),str(guidfile),data],check=True)
  if a.keep_user_options:shutil.copytree(data,out/'last-user-options',dirs_exist_ok=True)
 assert hashes=={x.relative_to(ROOT).as_posix():sha(x) for x in files},'Source changed during smoke run; retry after freeze'
 assert assethashes=={x.relative_to(ROOT).as_posix():sha(x) for x in assetpaths},'Real sample/scene assets changed during smoke run'
 d=json.loads(report.read_text());d.update({'generated_utc':datetime.datetime.now(datetime.timezone.utc).isoformat(),'source_sha256':hashes,'assets_sha256':assethashes,'sdk':sdk.name});report.write_text(json.dumps(d,indent=2)+'\n')
if __name__=='__main__':main()
