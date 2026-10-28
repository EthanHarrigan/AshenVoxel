#!/usr/bin/env python3
"""Compile actual climate archive and world data, with optional Python/baked interoperability."""
from pathlib import Path
import argparse,datetime,hashlib,json,os,shutil,subprocess
ROOT=Path(__file__).resolve().parents[1]
def version(p):return tuple(int(v) if v.isdigit() else 0 for v in p.name.split('.'))
def main():
 p=argparse.ArgumentParser(description=__doc__);p.add_argument('--height',type=Path);p.add_argument('--climate',type=Path);a=p.parse_args()
 if bool(a.height)!=bool(a.climate):p.error('--height and --climate must be supplied together')
 candidates=[Path(os.environ['DOTNET_ROOT'])/'dotnet' if os.environ.get('DOTNET_ROOT') else None,Path(shutil.which('dotnet')) if shutil.which('dotnet') else None,ROOT.parent/'.dotnet/dotnet']
 dotnet=next((p.resolve() for p in candidates if p and p.is_file()),None)
 if dotnet is None:p.error('Install .NET8 SDK or setDOTNET_ROOT')
 sdk=sorted((dotnet.parent/'sdk').glob('8.*'),key=version)[-1];refs=sorted((dotnet.parent/'packs/Microsoft.NETCore.App.Ref').glob('8.*'),key=version)[-1]/'ref/net8.0'
 out=ROOT/'Tools/ClimateChecks/bin';out.mkdir(parents=True,exist_ok=True);q=lambda p:'"'+str(p)+'"'
 files=sorted((ROOT/'Assets/AshenVoxel/Core').rglob('*.cs'))+sorted((ROOT/'Assets/AshenVoxel/WorldScaleData').glob('*.cs'))
 hashes={str(p.relative_to(ROOT)):hashlib.sha256(p.read_bytes()).hexdigest() for p in files}
 def compile(assembly,refdir,sources,target):
  opts=['-nologo','-warnaserror+','-langversion:9.0','-optimize+','-target:'+target,'-out:'+q(assembly)]
  opts+=['-r:'+q(p) for p in sorted(refdir.glob('*.dll'))]+[q(p) for p in sources]
  rsp=out/(assembly.stem+'.rsp');rsp.write_text('\n'.join(opts));subprocess.run([str(dotnet),str(sdk/'Roslyn/bincore/csc.dll'),'@'+str(rsp)],check=True)
 assembly=out/'ClimateChecks.dll';compile(assembly,refs,files+[ROOT/'Tools/ClimateChecks/Program.cs'],'exe')
 (out/'ClimateChecks.runtimeconfig.json').write_text(json.dumps({'runtimeOptions':{'tfm':'net8.0','framework':{'name':'Microsoft.NETCore.App','version':'8.0.0'}}}))
 report=ROOT/'Docs/climate-validation.json';command=[str(dotnet),str(assembly),str(report)]
 if a.height:command.extend([str(a.height.resolve()),str(a.climate.resolve())])
 subprocess.run(command,check=True)
 standard=sorted((dotnet.parent/'packs/NETStandard.Library.Ref').glob('2.1.*'),key=version)
 if standard:compile(out/'ClimateNetStandard.dll',standard[-1]/'ref/netstandard2.1',files,'library');print('PASS C#9/.NET Standard2.1 climate archive API compile')
 if hashes!={str(p.relative_to(ROOT)):hashlib.sha256(p.read_bytes()).hexdigest() for p in files}:raise RuntimeError('World data changed during climate checks')
 d=json.loads(report.read_text());d.update({'generated_utc':datetime.datetime.now(datetime.timezone.utc).isoformat(),'source_sha256':hashes,'sdk':sdk.name})
 if a.height:d['external_sha256']={'height':hashlib.sha256(a.height.read_bytes()).hexdigest(),'climate':hashlib.sha256(a.climate.read_bytes()).hexdigest()}
 report.write_text(json.dumps(d,indent=2)+'\n')
if __name__=='__main__':main()
