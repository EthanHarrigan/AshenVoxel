#!/usr/bin/env python3
"""Snapshot and execute actual C# plant/rock generators for offline10cm previews."""
from pathlib import Path
import argparse,datetime,hashlib,json,os,shutil,subprocess
ROOT=Path(__file__).resolve().parents[2];BUILD=ROOT/'Tools/GroundCoverPreview/bin';OUT=ROOT/'Docs/GroundCoverGallery'
def sha(p):return hashlib.sha256(p.read_bytes()).hexdigest()
def version(p):return tuple(int(v) if v.isdigit() else 0 for v in p.name.split('.'))
def main():
 global BUILD,OUT
 parser=argparse.ArgumentParser(description=__doc__);parser.add_argument('--mode',choices=('ground-cover','shapes'),default='ground-cover');args=parser.parse_args()
 if args.mode=='shapes':BUILD=BUILD/'shapes';OUT=ROOT/'Docs/RockShapeGallery'
 BUILD.mkdir(parents=True,exist_ok=True);OUT.mkdir(parents=True,exist_ok=True)
 candidates=[Path(os.environ['DOTNET_ROOT'])/'dotnet' if os.environ.get('DOTNET_ROOT') else None,Path(shutil.which('dotnet')) if shutil.which('dotnet') else None,ROOT.parent/'.dotnet/dotnet']
 dotnet=next(p.resolve() for p in candidates if p and p.is_file());sdk=sorted((dotnet.parent/'sdk').glob('8.*'),key=version)[-1]
 refs=sorted((dotnet.parent/'packs/Microsoft.NETCore.App.Ref').glob('8.*'),key=version)[-1]/'ref/net8.0'
 files=sorted((ROOT/'Assets/AshenVoxel/Core').rglob('*.cs'))+[ROOT/'Tools/GroundCoverPreview/ExportGroundCover.cs']
 hashes={str(p.relative_to(ROOT)):sha(p) for p in files};snapshot=[]
 for p in files:
  dest=BUILD/'source'/p.relative_to(ROOT);dest.parent.mkdir(parents=True,exist_ok=True);dest.write_bytes(p.read_bytes());snapshot.append(dest)
 assert hashes=={str(p.relative_to(ROOT)):sha(p) for p in files},'Sources changed during snapshot'
 assembly=BUILD/'ExportGroundCover.dll';q=lambda p:'"'+str(p)+'"'
 options=['-nologo','-warnaserror+','-langversion:9.0','-optimize+','-target:exe','-out:'+q(assembly)]+['-r:'+q(p) for p in sorted(refs.glob('*.dll'))]+[q(p) for p in snapshot]
 rsp=BUILD/'compile.rsp';rsp.write_text('\n'.join(options));assembly.with_suffix('.runtimeconfig.json').write_text(json.dumps({'runtimeOptions':{'tfm':'net8.0','framework':{'name':'Microsoft.NETCore.App','version':'8.0.0'}}}))
 subprocess.run([str(dotnet),str(sdk/'Roslyn/bincore/csc.dll'),'@'+str(rsp)],check=True)
 subprocess.run([str(dotnet),str(assembly),str(BUILD),args.mode],check=True)
 report={'created_utc':datetime.datetime.now(datetime.timezone.utc).isoformat(),'sdk':sdk.name,'source_sha256':hashes,'tool_sha256':sha(Path(__file__)),
  'mode':args.mode,'note':'Actual C# procedural plant/rock specimens on10cm voxel grid. Authored arrangements and lighting fixtures are explicit; not Unity or ecological world scatter.',
  'exports':{p.name:sha(p) for p in BUILD.iterdir() if p.suffix in {'.bin','.json'} and not p.name.endswith('runtimeconfig.json') and p.name!='source-provenance.json'}}
 (OUT/'provenance.json').write_text(json.dumps(report,indent=2)+'\n');(BUILD/'source-provenance.json').write_text(json.dumps(report,indent=2)+'\n')
 for p in BUILD.glob('*.json'):
  if p.name in report['exports']:shutil.copyfile(p,OUT/p.name)
 print('Actual-data export complete:',OUT)
if __name__=='__main__':main()
