#!/usr/bin/env python3
"""Compile an immutable actual C# source snapshot and export controlled10cm examples."""
from pathlib import Path
import datetime,hashlib,json,os,shutil,subprocess
ROOT=Path(__file__).resolve().parents[2];BUILD=ROOT/'Tools/SurfacePreview/bin';OUT=ROOT/'Docs/SurfaceGallery'
def sha(p):return hashlib.sha256(p.read_bytes()).hexdigest()
def version(p):return tuple(int(v) if v.isdigit() else 0 for v in p.name.split('.'))
def main():
 BUILD.mkdir(parents=True,exist_ok=True);OUT.mkdir(parents=True,exist_ok=True)
 candidates=[Path(os.environ['DOTNET_ROOT'])/'dotnet' if os.environ.get('DOTNET_ROOT') else None,Path(shutil.which('dotnet')) if shutil.which('dotnet') else None,ROOT.parent/'.dotnet/dotnet']
 dotnet=next(p.resolve() for p in candidates if p and p.is_file());sdk=sorted((dotnet.parent/'sdk').glob('8.*'),key=version)[-1]
 refs=sorted((dotnet.parent/'packs/Microsoft.NETCore.App.Ref').glob('8.*'),key=version)[-1]/'ref/net8.0'
 sources=sorted((ROOT/'Assets/AshenVoxel/Core').rglob('*.cs'))+sorted((ROOT/'Assets/AshenVoxel/WorldScaleData').glob('*.cs'))+[ROOT/'Tools/SurfacePreview/ExportSurfaces.cs']
 hashes={str(p.relative_to(ROOT)):sha(p) for p in sources};snapshot=[]
 for p in sources:
  d=BUILD/'source'/p.relative_to(ROOT);d.parent.mkdir(parents=True,exist_ok=True);d.write_bytes(p.read_bytes());snapshot.append(d)
 assert hashes=={str(p.relative_to(ROOT)):sha(p) for p in sources},'Source changed while snapshotting'
 assembly=BUILD/'ExportSurfaces.dll';q=lambda p:'"'+str(p)+'"'
 options=['-nologo','-warnaserror+','-langversion:9.0','-optimize+','-target:exe','-out:'+q(assembly)]+['-r:'+q(p) for p in sorted(refs.glob('*.dll'))]+[q(p) for p in snapshot]
 rsp=BUILD/'compile.rsp';rsp.write_text('\n'.join(options));(BUILD/'ExportSurfaces.runtimeconfig.json').write_text(json.dumps({'runtimeOptions':{'tfm':'net8.0','framework':{'name':'Microsoft.NETCore.App','version':'8.0.0'}}}))
 subprocess.run([str(dotnet),str(sdk/'Roslyn/bincore/csc.dll'),'@'+str(rsp)],check=True)
 subprocess.run([str(dotnet),str(assembly),str(BUILD)],check=True)
 report={'created_utc':datetime.datetime.now(datetime.timezone.utc).isoformat(),'sdk':sdk.name,'source_sha256':hashes,
  'note':'Controlled synthetic fixtures of actual C# surface classification and10cm matter. Not Unity or neural terrain output.',
  'exports':{p.name:sha(p) for p in BUILD.glob('*') if p.suffix in {'.bin','.json'} and not p.name.endswith('runtimeconfig.json')}}
 (OUT/'provenance.json').write_text(json.dumps(report,indent=2)+'\n');(BUILD/'source-provenance.json').write_text(json.dumps(report,indent=2)+'\n')
 for p in BUILD.glob('*.json'):
  if p.stem in ['alpine','forest','pale-shore','black-shore','arid']:shutil.copyfile(p,OUT/p.name)
 print('Export provenance saved',OUT)
if __name__=='__main__':main()
