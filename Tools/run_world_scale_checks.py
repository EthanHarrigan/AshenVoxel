#!/usr/bin/env python3
"""Compile actual engine-free global-world sources with .NET8; no restore/network."""
from pathlib import Path
import argparse,datetime,hashlib,json,os,shutil,subprocess
ROOT=Path(__file__).resolve().parents[1]
def version(p):return tuple(int(v) if v.isdigit() else 0 for v in p.name.split('.'))
def main():
 p=argparse.ArgumentParser(description=__doc__);p.add_argument('--atlas',type=Path);a=p.parse_args()
 candidates=[Path(os.environ['DOTNET_ROOT'])/'dotnet' if os.environ.get('DOTNET_ROOT') else None,Path(shutil.which('dotnet')) if shutil.which('dotnet') else None,ROOT.parent/'.dotnet/dotnet']
 dotnet=next((x.resolve() for x in candidates if x and x.is_file()),None)
 if dotnet is None:p.error('Install .NET8 SDK or setDOTNET_ROOT')
 sdk=sorted((dotnet.parent/'sdk').glob('8.*'),key=version)[-1]
 refs=sorted((dotnet.parent/'packs/Microsoft.NETCore.App.Ref').glob('8.*'),key=version)[-1]/'ref/net8.0'
 build=ROOT/'Tools/WorldScaleChecks/bin';build.mkdir(parents=True,exist_ok=True);quote=lambda p:'"'+str(p)+'"'
 core=sorted((ROOT/'Assets/AshenVoxel/Core').rglob('*.cs'))+sorted((ROOT/'Assets/AshenVoxel/WorldScaleData').rglob('*.cs'))
 hashes={str(v.relative_to(ROOT)):hashlib.sha256(v.read_bytes()).hexdigest() for v in core}
 def compile(assembly,refdir,sources,target):
  opts=['-nologo','-warnaserror+','-langversion:9.0','-optimize+','-target:'+target,'-out:'+quote(assembly)]
  opts+=['-r:'+quote(v) for v in sorted(refdir.glob('*.dll'))]+[quote(v) for v in sources]
  rsp=build/(assembly.stem+'.rsp');rsp.write_text('\n'.join(opts));subprocess.run([str(dotnet),str(sdk/'Roslyn/bincore/csc.dll'),'@'+str(rsp)],check=True)
 assembly=build/'WorldScaleChecks.dll';compile(assembly,refs,core+[ROOT/'Tools/WorldScaleChecks/Program.cs'],'exe')
 (build/'WorldScaleChecks.runtimeconfig.json').write_text(json.dumps({'runtimeOptions':{'tfm':'net8.0','framework':{'name':'Microsoft.NETCore.App','version':'8.0.0'}}}))
 result=ROOT/'Docs/world-scale-validation.json';command=[str(dotnet),str(assembly),str(result)]
 if a.atlas:command.append(str(a.atlas.resolve()))
 subprocess.run(command,check=True)
 standard=sorted((dotnet.parent/'packs/NETStandard.Library.Ref').glob('2.1.*'),key=version)
 if standard:compile(build/'WorldScaleNetStandard.dll',standard[-1]/'ref/netstandard2.1',core,'library');print('PASS C#9/.NET Standard2.1 world-scale API compile')
 if hashes!={str(v.relative_to(ROOT)):hashlib.sha256(v.read_bytes()).hexdigest() for v in core}:raise RuntimeError('World source changed during checks')
 data=json.loads(result.read_text());data.update({'generated_utc':datetime.datetime.now(datetime.timezone.utc).isoformat(),'sdk':sdk.name,'source_sha256':hashes});result.write_text(json.dumps(data,indent=2)+'\n')
if __name__=='__main__':main()
