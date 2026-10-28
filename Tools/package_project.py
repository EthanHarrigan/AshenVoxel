#!/usr/bin/env python3
"""Package source, Unity assets, docs and previews; omit caches and compiled output."""
from pathlib import Path
import argparse,hashlib,subprocess,sys,zipfile
ROOT=Path(__file__).resolve().parents[1]
EXCLUDED={'.git','.dotnet','.terrain-cache','Library','library','Temp','temp','Logs','logs','obj','bin','Builds','builds','__pycache__','.vs','.idea','UserSettings'}
def main():
 parser=argparse.ArgumentParser(description=__doc__)
 parser.add_argument('--output',type=Path,default=ROOT.parent/'AshenVoxel-Foundation.zip')
 args=parser.parse_args()
 for name in ['check_project.py','check_documentation.py','check_exploration_release.py']:
  subprocess.run([sys.executable,str(ROOT/'Tools'/name)],check=True)
 files=sorted(p for p in ROOT.rglob('*') if p.is_file() and not EXCLUDED.intersection(p.relative_to(ROOT).parts) and p.suffix not in {'.pyc','.dll','.pdb','.zip','.avxl'})
 checksums=[]
 args.output.parent.mkdir(parents=True,exist_ok=True)
 with zipfile.ZipFile(args.output,'w',compression=zipfile.ZIP_DEFLATED,compresslevel=9) as archive:
  for p in files:
   relative=p.relative_to(ROOT).as_posix();data=p.read_bytes()
   checksums.append(hashlib.sha256(data).hexdigest()+'  '+relative)
   info=zipfile.ZipInfo('AshenVoxel/'+relative,date_time=(1980,1,1,0,0,0))
   info.compress_type=zipfile.ZIP_DEFLATED;info.external_attr=0o100644<<16
   archive.writestr(info,data)
  info=zipfile.ZipInfo('AshenVoxel/MANIFEST.sha256',date_time=(1980,1,1,0,0,0))
  info.compress_type=zipfile.ZIP_DEFLATED;info.external_attr=0o100644<<16
  archive.writestr(info,'\n'.join(checksums)+'\n')
 with zipfile.ZipFile(args.output) as archive:
  bad=archive.testzip()
  if bad:raise RuntimeError('Archive integrity failure: '+bad)
 digest=hashlib.sha256(args.output.read_bytes()).hexdigest()
 args.output.with_suffix('.zip.sha256').write_text(digest+'  '+args.output.name+'\n')
 print(f'Packaged {len(files)} files; {args.output.stat().st_size:,} bytes: {args.output}')
 print('SHA256 '+digest)
if __name__=='__main__':main()
