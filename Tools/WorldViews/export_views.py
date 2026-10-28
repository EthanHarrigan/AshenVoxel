#!/usr/bin/env python3
"""Export actual WorldExplorer meshes under data-only Unity substitutes."""
from pathlib import Path
import datetime,hashlib,json,os,shutil,subprocess
ROOT=Path(__file__).resolve().parents[2];BUILD=ROOT/'Tools/WorldViews/bin';OUT=ROOT/'Docs/WorldViews'
def sha(p):return hashlib.sha256(p.read_bytes()).hexdigest()
def version(p):return tuple(int(v) if v.isdigit() else 0 for v in p.name.split('.'))
def main():
 BUILD.mkdir(parents=True,exist_ok=True);OUT.mkdir(parents=True,exist_ok=True)
 candidates=[Path(os.environ['DOTNET_ROOT'])/'dotnet' if os.environ.get('DOTNET_ROOT') else None,Path(shutil.which('dotnet')) if shutil.which('dotnet') else None,ROOT.parent/'.dotnet/dotnet']
 dotnet=next(p.resolve() for p in candidates if p and p.is_file());sdk=sorted((dotnet.parent/'sdk').glob('8.*'),key=version)[-1]
 refs=sorted((dotnet.parent/'packs/Microsoft.NETCore.App.Ref').glob('8.*'),key=version)[-1]/'ref/net8.0'
 files=sorted((ROOT/'Assets/AshenVoxel/Core').rglob('*.cs'))+sorted((ROOT/'Assets/AshenVoxel/WorldScaleData').glob('*.cs'))+sorted((ROOT/'Assets/AshenVoxel/WorldScaleRuntime').glob('*.cs'))
 files += [ROOT/'Tools/WorldExplorerChecks/UnityShim.cs',ROOT/'Tools/AuthoringChecks/ProfileShim.cs',ROOT/'Tools/WorldViews/ExportWorldViews.cs']
 hashes={str(p.relative_to(ROOT)):sha(p) for p in files};q=lambda p:'"'+str(p)+'"';assembly=BUILD/'ExportWorldViews.dll'
 options=['-nologo','-langversion:9.0','-warnaserror+','-optimize+','-target:exe','-out:'+q(assembly)]+['-r:'+q(p) for p in sorted(refs.glob('*.dll'))]+[q(p) for p in files]
 rsp=BUILD/'compile.rsp';rsp.write_text('\n'.join(options));(BUILD/'ExportWorldViews.runtimeconfig.json').write_text(json.dumps({'runtimeOptions':{'tfm':'net8.0','framework':{'name':'Microsoft.NETCore.App','version':'8.0.0'}}}))
 subprocess.run([str(dotnet),str(sdk/'Roslyn/bincore/csc.dll'),'@'+str(rsp)],check=True)
 subprocess.run([str(dotnet),str(assembly),str(ROOT),str(BUILD)],check=True)
 assert hashes=={str(p.relative_to(ROOT)):sha(p) for p in files},'Source changed during export'
 exports={}
 for path in BUILD.glob('*.json'):
  if path.name.endswith('.runtimeconfig.json'):continue
  meta=json.loads(path.read_text());meta['binary_sha256']=sha(path.with_suffix('.bin'));(OUT/path.name).write_text(json.dumps(meta,indent=2)+'\n');exports[path.stem]=meta['binary_sha256']
 source_assets={str(p.relative_to(ROOT)):sha(p) for p in (ROOT/'Assets/AshenVoxel/WorldScaleRuntime/Samples').glob('*.bytes')}
 (OUT/'provenance.json').write_text(json.dumps({'created_utc':datetime.datetime.now(datetime.timezone.utc).isoformat(),'source_sha256':hashes,'asset_sha256':source_assets,'binary_sha256':exports,'sdk':sdk.name,'note':'Actual current C# field/planner/mesh source; API substitutes for data extraction, not Unity screenshots.'},indent=2)+'\n')
 print('Exported immutable actual runtime data for',', '.join(exports))
if __name__=='__main__':main()
